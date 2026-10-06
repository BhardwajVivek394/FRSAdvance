using Domain;
using E7MRIWeb.Areas.FRS25.Controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore
{
    // AnalysisEngine -- AgentLoop.
    // Moved verbatim from AiChatController.AgentLoop.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {

        internal async Task AwaitWithCancel(Task t, System.Threading.CancellationToken ct, int bgWeight = 1)
        {
            if (!ct.CanBeCanceled) { await t.ConfigureAwait(false); return; }
            Task done = await Task.WhenAny(t, Task.Delay(System.Threading.Timeout.Infinite, ct)).ConfigureAwait(false);
            if (done != t)
            {
                RegisterAbandoned(t, bgWeight);
                ct.ThrowIfCancellationRequested();
            }
            await t.ConfigureAwait(false);
        }


        internal async Task<T> AwaitWithCancel<T>(Task<T> t, System.Threading.CancellationToken ct, int bgWeight = 1)
        {
            if (!ct.CanBeCanceled) { return await t.ConfigureAwait(false); }
            Task done = await Task.WhenAny(t, Task.Delay(System.Threading.Timeout.Infinite, ct)).ConfigureAwait(false);
            if (done != t)
            {
                RegisterAbandoned(t, bgWeight);
                ct.ThrowIfCancellationRequested();
            }
            return await t.ConfigureAwait(false);
        }


        // runs BEFORE the OCE unwinds the loop, so the wrapper-loop completion
        // continuation (which settles the reservation) always sees the final
        // abandoned weight. The real task releases its own weight on completion.
        internal void RegisterAbandoned(Task t, int weight)
        {
            if (weight < 1) { weight = 1; }
            System.Threading.Interlocked.Add(ref Sess._jsonAbandonedW, weight);
            int w = weight;
            t.ContinueWith(x =>
            {
                var _ = x.Exception; // observe
                System.Threading.Interlocked.Add(ref _jsonBgReserved, -w);
            });
        }


        internal static void ObserveAbandoned(Task t)
        {
            t.ContinueWith(x => { var _ = x.Exception; },
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }


        internal async Task RunAgenticLoop(JArray incomingMessages, string systemPromptOverride = null, bool rawJsonMode = false, bool validateVerdictOutput = false, System.Threading.CancellationToken ct = default(System.Threading.CancellationToken))
        {
            List<JObject> messages = new List<JObject>();
            foreach (JToken m in incomingMessages)
            {
                JObject jo = m as JObject;
                if (jo != null) messages.Add(jo);
                else messages.Add(JObject.FromObject(m));
            }

            string conversationId = Guid.NewGuid().ToString("N");
            if (!string.IsNullOrEmpty(Sess._traceId)) { conversationId = Sess._traceId; } // correlate logs

            string lastUserText = string.Empty;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i]["role"] != null && messages[i]["role"].ToString() == "user")
                {
                    lastUserText = messages[i]["content"] != null ? messages[i]["content"].ToString() : "";
                    break;
                }
            }

            if (!rawJsonMode && IsOutOfScope(lastUserText))
            {
                WriteSse("text", new
                {
                    text = "That's a bit outside what I can help with here \u2014 I'm focused on this " +
                           "alert and the railway monitoring data behind it. Happy to keep digging " +
                           "into this asset or the site's telemetry whenever you like."
                });
                return;
            }

            // ── Load tools from both servers ──
            System.Diagnostics.Stopwatch _swDisc = System.Diagnostics.Stopwatch.StartNew();
            ToolLoadResult loaded;
            try
            {
                loaded = await LoadAllToolsAsync(ct).ConfigureAwait(false);
            }
            catch (ClientBusyException) { throw; } // endpoint condition -> HTTP 503 busy
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) { throw; } // our cancel only
                AiLog("ERROR", "MCP", "tool discovery timed out");
                if (Sess._jsonCapture != null) { AddDiag("MCP_DISCOVERY_TIMEOUT"); }
                else { WriteSse("text", new { text = "[MCP failed: timeout]\n\n" }); }
                loaded = new ToolLoadResult { Tools = new JArray() };
            }
            catch (Exception ex)
            {
                if (Sess._jsonCapture != null)
                {
                    AddDiag("MCP_DISCOVERY_FAILED");
                    AiLogEx("MCP", "tool discovery failed", ex);
                    System.Diagnostics.Trace.TraceError("[AnalyzeAlertJson] MCP discovery: " + ex.Message);
                }
                else { WriteSse("text", new { text = "[MCP failed: " + ex.Message + "]\n\n" }); }
                loaded = new ToolLoadResult { Tools = new JArray() };
            }
            finally
            {
                if (_swDisc.IsRunning)
                {
                    _swDisc.Stop();
                    if (Sess._traceActive) { Sess._trDiscoveryMs += _swDisc.ElapsedMilliseconds; }
                }
            }

            if (loaded.Error1 != null)
            {
                AiLog("ERROR", "MCP", "MCP1 error: " + loaded.Error1);
                if (Sess._jsonCapture != null) { AddDiag("MCP1_ERROR"); }
                else { WriteSse("text", new { text = "[MCP1 error: " + loaded.Error1 + "]\n" }); }
            }
            if (loaded.Error2 != null)
            {
                AiLog("ERROR", "MCP", "MCP2 error: " + loaded.Error2);
                if (Sess._jsonCapture != null) { AddDiag("MCP2_ERROR"); }
                else { WriteSse("text", new { text = "[MCP2 error: " + loaded.Error2 + "]\n" }); }
            }

            if (loaded.Tools.Count == 0)
            {
                AiLog("ERROR", "MCP", "no tools from either server -- analysis cannot ground");
                if (Sess._jsonCapture != null) { AddDiag("NO_TOOLS"); }
                else { WriteSse("text", new { text = "[No tools from either server]\n\n" }); }
            }
            else
                WriteSse("tool_use", new { name = _engineBrand + " ready \u00b7 "
                    + (loaded.Count1 + loaded.Count2) + " capabilities" });
            if (Sess._traceActive)
            {
                JObject dd = new JObject();
                dd["ms"] = Sess._trDiscoveryMs; dd["tools"] = loaded.Tools.Count;
                dd["mcp1"] = loaded.Count1; dd["mcp2"] = loaded.Count2;
                dd["cacheHit"] = Sess._trDiscoveryMs < 5;   // ~0 ms means the 300 s cache served it
                TraceAnalyze("discovery", dd);
                AiLog("INFO", "CACHE", "prompt cache " + (_promptCache ? "ON (tools+system prefix)" : "off"));
                AiLog("INFO", "MCP", "discovery " + Sess._trDiscoveryMs + "ms tools=" + loaded.Tools.Count
                    + " (mcp1=" + loaded.Count1 + " mcp2=" + loaded.Count2 + ")"
                    + (Sess._trDiscoveryMs < 5 ? " [cache]" : ""));
            }

            Func<string, int> ownerOf = (tn) =>
            {
                if (loaded.OwnerMap != null && !string.IsNullOrEmpty(tn) && loaded.OwnerMap.ContainsKey(tn)) { return loaded.OwnerMap[tn]; }
                return ResolveToolOwner(tn);
            };

            // ── System prompt ──
            string sysBase = ConfigurationManager.AppSettings["SystemPrompt"];
            if (string.IsNullOrWhiteSpace(sysBase))
            {
                sysBase = "You are an EdgeX industrial data assistant. " +
                          "You MUST use the available tools to answer -- never make up data. " +
                          "Always call a tool first. Never answer from memory.\n\n" +
                          "=== PERFORMANCE / HEALTH EVALUATION ===\n" +
                          "When the user asks about 'performance', 'health', 'status', " +
                          "'how is X doing/performing', 'behaviour of X', or similar, follow this workflow:\n\n" +
                          "STEP 1 - IDENTIFY SCOPE: Resolve entity (asset/site/section/division/zone). " +
                          "Use search_sites to get SiteId. Use search_assets for AssetId.\n\n" +
                          "STEP 2 - BASELINE: Call get_attribute_range(SiteId) for safe range " +
                          "(AverageValue, MaxSafeValue, MinSafeValue, MinFailValue per attribute). " +
                          "Cross-reference AssetId with search_assets to get AssetName.\n\n" +
                          "STEP 3 - LIVE VALUES: Call get_tag_current(SiteId) or by AssetId.\n\n" +
                          "STEP 4 - DETERIORATION CHECK: Compare live vs baseline. " +
                          "Current > MaxSafeValue or < MinSafeValue = YELLOW. " +
                          "Current beyond MinFailValue or deviation > 50% from AverageValue = RED. " +
                          "Otherwise = GREEN. Skip attributes where all thresholds are null. " +
                          "Only report attributes that are NOT green.\n\n" +
                          "STEP 5 - ALERTS: Call get_frs_alerts with appropriate window: " +
                          "Asset level: SiteIds + AssetIds, last 5 days. " +
                          "Site/Section/Division/Zone: SiteIds, last 1 day. " +
                          "Use pagination (PageSize=50, Skip for next pages).\n\n" +
                          "STEP 6 - FILTER TESTING ALERTS: Exclude alerts where Remark or " +
                          "MaintainerRemarks contains (case-insensitive): " +
                          "'testing', 'test', 'check', 'routine', 'maintenance', 'calibration', " +
                          "'energy7 staff', 'e7 staff', 'working'. " +
                          "Count excluded alerts separately.\n\n" +
                          "STEP 7 - PRODUCE HEALTH VERDICT:\n" +
                          "ENTITY_NAME - Health: GREEN/YELLOW/RED (N active concerns)\n\n" +
                          "DETERIORATION FLAGS (only non-GREEN attributes):\n" +
                          "  Asset Attribute: current X (avg Y, safe Z-W) [% deviation]\n\n" +
                          "ACTIVE ALERTS (genuine only):\n" +
                          "  Asset - CauseCode (Failure/Predictive) - since HH:MM (duration)\n" +
                          "  (N testing/check alerts excluded)\n\n" +
                          "LAST N-DAY SUMMARY:\n" +
                          "  N genuine alerts, M testing excluded\n" +
                          "  Most active: Asset1 (count), Asset2 (count)\n\n" +
                          "RULES:\n" +
                          "- Do NOT dump raw alert tables or bare sensor values.\n" +
                          "- Always show deviation from baseline, not just raw numbers.\n" +
                          "- Keep concise -- highlight only anomalies and concerns.\n" +
                          "- For site/division/zone: roll up per asset type (Track/Signal/Point Machine/Power Supply).\n" +
                          "=== END PERFORMANCE / HEALTH EVALUATION ===";
            }
            string systemPrompt = !string.IsNullOrWhiteSpace(systemPromptOverride)
                ? systemPromptOverride + BuildDateAnchor()
                // v1.0.160.167: the follow-up is free text -- a plan can be asked for here, so it
                // carries the same scope and plan-format rules as the ChatBot. Override paths (the
                // verdict) keep their own schema and are deliberately excluded.
                : sysBase + BuildDateAnchor() + LoadTrainingText() + RailwayScopeRule() + MaintenancePlanRule();
            // v1.0.160.107 -- F7. Append the protection rule, and record the INSTRUCTION part as the
            // protected corpus for the echo backstop. LoadTrainingText() is deliberately EXCLUDED:
            // it is domain knowledge answers are supposed to use, and scrubbing it would redact
            // legitimate railway doctrine.
            systemPrompt = systemPrompt + InstructionProtectionRule();
            Sess._protectedCorpus = (!string.IsNullOrWhiteSpace(systemPromptOverride) ? systemPromptOverride : sysBase)
                             + InstructionProtectionRule();

            // ---- DOMAIN WISDOM (analysis paths only; independent of prefetch) ----
            if (Sess._prefetchCtx != null && messages.Count > 0)
            {
                // v1.0.147.0: owner-mandated per-asset-type checklist rides with the wisdom
                // block (same injection point, same guard).
                // v1.0.160.57: build each block separately so the AUDIT can record what was actually
                // injected. Concatenating them inline made "did wisdom reach the model?" unanswerable.
                string _bChk = BuildAssetChecklist(Sess._prefetchCtx);
                string _bFld = BuildFieldWisdom(Sess._prefetchCtx);
                string _bDoc = BuildDoctrinalWisdom(Sess._prefetchCtx);
                string _bCir = BuildCircuitWisdom(Sess._prefetchCtx);
                Sess._wisChecklist = CountRuleLines(_bChk); Sess._wisField = CountRuleLines(_bFld);
                Sess._wisDoctrinal = CountRuleLines(_bDoc); Sess._wisCircuit = CountRuleLines(_bCir);
                // v1.0.160.117 CUT C: tag AFTER counting, so the existing counts are unchanged.
                _bChk = TagWisdomBlock(_bChk, "CHK"); _bFld = TagWisdomBlock(_bFld, "FLD");
                _bDoc = TagWisdomBlock(_bDoc, "DOC"); _bCir = TagWisdomBlock(_bCir, "CIR");
                string _chk = _bChk + _bFld + _bDoc + _bCir;   // v1.0.153.0: adopted field wisdom rides the same injection; v1.0.160.24: doctrinal reference appended (Item 9); v1.0.160.50: circuit topology + upstream causality appended
                // v1.0.160.59: bash purpose contract, injected only when bash is actually available
                // here. Advisory by nature -- the enforceable half is ScrubPlatformInternals.
                if (_bashInVerdict)
                {
                    _chk += "\n=== bash (available, " + _bashMaxCalls + " calls max) ===\n"
                          + "Use bash ONLY for computation or data queries that VERIFY telemetry -- for example "
                          + "checking whether raw samples exist in the store around the incidence. It is not a "
                          + "general shell for this analysis.\n"
                          + "NEVER quote, paste, summarise or characterise platform or hardware log content in "
                          + "the verdict. Those are internal operational logs and this verdict is read by railway "
                          + "maintainers. State any conclusion as a MEASURED FACT about the asset (e.g. 'no "
                          + "samples exist between 09:58 and 10:04'), never as a log excerpt or file path.\n";
                }
                if (!string.IsNullOrEmpty(_chk))
                {
                    JObject _cf = messages[0] as JObject;
                    if (_cf != null && _cf["content"] != null)
                    {
                        _cf["content"] = _cf["content"].ToString() + "\n\n" + _chk;
                    }
                }
                string _wis = BuildWisdomBlock(Sess._prefetchCtx);
                Sess._wisLearned = CountRuleLines(_wis);
                _wis = TagWisdomBlock(_wis, "LRN");
                Sess._wisChars = (_chk == null ? 0 : _chk.Length) + (_wis == null ? 0 : _wis.Length);
                if (!string.IsNullOrEmpty(_wis))
                {
                    JObject _wf = messages[0] as JObject;
                    if (_wf != null && _wf["content"] != null)
                    {
                        _wf["content"] = _wf["content"].ToString() + "\n\n" + _wis;
                    }
                }
            }

            // ---- PRE-FETCH -------------------------------------------------
            if (_prefetch && Sess._prefetchCtx != null && loaded.Tools.Count > 0)
            {
                string ev = null;
                try
                {
                    ev = await PrefetchEvidenceAsync(Sess._prefetchCtx, loaded, ct).ConfigureAwait(false);
                }
                catch (ClientBusyException) { throw; }
                catch (OperationCanceledException) { if (ct.IsCancellationRequested) { throw; } }
                catch (Exception pex) { AiLogEx("PREFETCH", "failed; falling back to model-driven tools", pex); }

                if (!string.IsNullOrEmpty(ev) && messages.Count > 0)
                {
                    JObject first = messages[0] as JObject;
                    if (first != null && first["content"] != null)
                    {
                        first["content"] = first["content"].ToString() + "\n\n" + ev;
                    }
                }
            }

            // SYNTHESIS MODE. If pre-fetch already grounded the analysis, there is
            // nothing left for the model to fetch -- so send NO tools at all. That
            // removes the tool schemas (~2,000 tokens), removes the tool-use system
            // prompt the API injects alongside them, and makes a second turn
            // structurally impossible rather than merely discouraged. Three prompt
            // revisions failed to stop the model looping; taking the tools away
            // settles it.
            // Gated on _jsonGotHistory: without telemetry the model still needs the
            // ability to go and find it, and speed must not cost grounding.
            Sess._synthesisMode = _prefetch && rawJsonMode && Sess._prefetchComplete && Sess._prefetchCtx != null;
            if (!Sess._synthesisMode && _prefetch && Sess._prefetchGap != null)
            {
                AiLog("INFO", "SYNTH", "staying agentic -- " + Sess._prefetchGap);
            }
            if (Sess._synthesisMode)
            {
                systemPrompt = AnalyzeSynthesisPrompt() + InstructionProtectionRule();   // v1.0.160.107 -- F7
                Sess._protectedCorpus = AnalyzeSynthesisPrompt() + InstructionProtectionRule();
                AiLog("INFO", "SYNTH", "grounded by pre-fetch -- single call, no tools, compact prompt");
            }

            List<object> workMessages = TrimHistory(messages, _maxHistory).Cast<object>().ToList();
            // Verdict mode permits 9 tools but was sending all 22 schemas to the
            // model on EVERY turn: wasted input tokens twice per analysis, and it
            // invited calls to forbidden tools that were then denied, burning a
            // whole turn. Send only what is actually callable.
            // Applied on BOTH paths now. The streaming popup was shipping all 28
            // schemas on every turn (measured: 9 turns x 28 schemas), which cost
            // tokens and invited calls to tools that cannot help a verdict.
            JArray toolsForModel = loaded.Tools;
            {
                JArray permitted = new JArray();
                foreach (JToken tool in loaded.Tools)
                {
                    string tn = tool["name"] != null ? tool["name"].ToString() : "";
                    if (_jsonToolCaps.ContainsKey(tn)) { permitted.Add(tool); }
                    // v1.0.160.40 (D1 fix): bash is FREE-CHAT only. _jsonCapture alone was the wrong
                    // signal -- it is set only by the JSON-verdict helper, so the STREAMING AnalyzeAlert
                    // path (rawJsonMode:true, validateVerdictOutput:true) left it null and would have
                    // exposed bash to the automated verdict loop. Gate on the loop's own intent flags:
                    // Chat() and AnalyzeChat() (operator follow-up) pass both false -> bash allowed;
                    // both verdict paths pass rawJsonMode:true -> bash withheld.
                    // v1.0.160.60: bash is a TOOL, so it follows the owner rule -- site-keeping users
                    // and the config emails only. It had NO role check before, and the follow-up chat
                    // box is open to every authenticated user, so an ordinary railway user could have
                    // reached platform log content through a follow-up question.
                    // v1.0.160.191: also withhold bash from a GROUNDED alert follow-up chat
                    // (systemPromptOverride set = alert context injected). It wastes steps re-probing
                    // when the card+logic are already in front of the model. Pure free-chat
                    // (no grounding, systemPromptOverride == null) is unchanged.
                    // 1.0.167.0: bash removed for all users -- the two branches that permitted it (free chat for
                    // site-keeping users; analysis when AnalyzeBashInVerdict was on) are gone.
                }
                if (permitted.Count > 0) { toolsForModel = permitted; }
            }
            // 1.0.167.0: never hand bash to the model, including the fallback above that sends every loaded tool
            // when nothing matched the permitted list.
            {
                JArray noBash = new JArray();
                foreach (JToken tool in toolsForModel)
                {
                    string tn = tool["name"] != null ? tool["name"].ToString() : "";
                    if (!string.Equals(tn, "bash", StringComparison.OrdinalIgnoreCase)) { noBash.Add(tool); }
                }
                toolsForModel = noBash;
            }

            // empty array => AnthropicClient omits "tools" from the request entirely
            object[] toolsArray = Sess._synthesisMode ? new object[0] : toolsForModel.ToObject<object[]>();

            // v1.0.89.0: cache diagnostics. cache_read/write were 0 on single-turn
            // synthesis runs; this records the facts that decide WHY, so a run tells us
            // instead of guessing: the cached-prefix token estimate (Anthropic requires
            // >=1024 tokens to cache at all), whether tools are present (the ~2k tool
            // schema block is the main thing worth caching; synthesis mode drops it),
            // the synthesis flag, and whether the estimated prefix clears the minimum.
            if (_promptCache)
            {
                int sysTokEst = EstimateTokens(new List<object>(), systemPrompt);
                int toolTokEst = 0;
                if (toolsArray != null && toolsArray.Length > 0)
                {
                    try { toolTokEst = Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(toolsArray)) / 4; }
                    catch { toolTokEst = 0; }
                }
                int cachedPrefixEst = sysTokEst + toolTokEst;
                AiLog("INFO", "CACHE", "prefix diag: systemTok~" + sysTokEst
                    + " toolsTok~" + toolTokEst + " (tools=" + (toolsArray == null ? 0 : toolsArray.Length) + ")"
                    + " cachedPrefixTok~" + cachedPrefixEst
                    + " synthesis=" + Sess._synthesisMode
                    + " meetsAnthropic1024min=" + (cachedPrefixEst >= 1024 ? "yes" : "NO -> Anthropic will not cache")
                    + (Sess._synthesisMode ? " NOTE: synthesis drops the tool block, so only the compact system prompt is cacheable" : ""));
            }

            // v1.0.160.194: chat is bounded by a higher backstop + progress guard; analyze keeps
            // _maxIter. Kill-switch: when _chatLoopGuard is false, loopCap == _maxIter and every
            // guard branch below is gated off -- chat behaves exactly as 1.0.160.193.
            int loopCap = rawJsonMode || !_chatLoopGuard ? _maxIter : _chatMaxIter;
            bool chatForceAnswer = false;
            HashSet<string> chatToolSigs = new HashSet<string>();
            System.Diagnostics.Stopwatch chatWall = System.Diagnostics.Stopwatch.StartNew();
            for (int iter = 0; iter < loopCap; iter++)
            {
                ct.ThrowIfCancellationRequested();
                // v1.0.160.194: on the last permitted chat turn, or once the wall budget is spent,
                // force a tool-less answer turn so the loop ends on a reply, never a dead step-limit line.
                if (!rawJsonMode && _chatLoopGuard
                    && (iter == loopCap - 1 || chatWall.Elapsed.TotalSeconds >= _chatMaxWallSec))
                {
                    chatForceAnswer = true;
                }
                int est = EstimateTokens(workMessages, systemPrompt);
                if (est > _warnTokens)
                    System.Diagnostics.Trace.TraceWarning("[AiChat] est_tokens=" + est);

                // MUST be initialised: the finally below reads it to record recvBytes,
                // and a finally can run before the assignment completes -- an
                // uninitialised local there is a definite-assignment error (CS0165).
                string raw = null;
                // exact bytes on the wire for THIS turn (system + messages + tools)
                long _sentBytes = 0;
                if (_traceDeep && Sess._traceActive)
                {
                    try
                    {
                        _sentBytes = Encoding.UTF8.GetByteCount(systemPrompt ?? "")
                                   + Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(workMessages))
                                   + Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(toolsArray));
                    }
                    catch { _sentBytes = -1; }
                }
                WriteSse("step", new { phase = "model_start", iter = iter, engine = _engineName1 });
                System.Diagnostics.Stopwatch _swModel = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    try
                    {
                        // v1.0.160.194: a forced-answer chat turn sends NO tools, so the model must
                        // synthesize a reply from what it already gathered (same as synthesis mode, 19323).
                        object[] turnTools = !rawJsonMode && chatForceAnswer ? new object[0] : toolsArray;
                        raw = await AwaitWithCancel(
                            WithClientLock(_aiLock, () => CallModelAsync(workMessages.ToArray(), turnTools, systemPrompt), ct, Sess._jsonCapture != null), ct)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        // failed/cancelled turns still contribute elapsed time
                        _swModel.Stop();
                        if (Sess._traceActive)
                        {
                            Sess._trTurns++; Sess._trAnalysisTurns++; Sess._trModelMs += _swModel.ElapsedMilliseconds;
                            Sess._trBytesSent += (_sentBytes > 0 ? _sentBytes : 0);
                            long _recvBytes = string.IsNullOrEmpty(raw) ? 0 : Encoding.UTF8.GetByteCount(raw);
                            Sess._trBytesRecv += _recvBytes;

                            JObject mt = new JObject();
                            mt["iter"] = iter;
                            mt["ms"] = _swModel.ElapsedMilliseconds;
                            mt["sentBytes"] = _sentBytes;
                            mt["recvBytes"] = _recvBytes;
                            mt["estTokensIn"] = est;
                            mt["msgCount"] = workMessages.Count;
                            mt["toolSchemas"] = toolsArray == null ? 0 : toolsArray.Length;
                            // REAL token usage from the model reply beats any estimate
                            JObject u = ReadUsage(raw);
                            if (u != null)
                            {
                                mt["usage"] = u;
                                Sess._trTokIn += ReadLong(u, "input_tokens");
                                Sess._trTokOut += ReadLong(u, "output_tokens");
                                Sess._trTokCacheRead += ReadLong(u, "cache_read_input_tokens");
                                Sess._trTokCacheWrite += ReadLong(u, "cache_creation_input_tokens");
                            }
                            TraceAnalyze("model_turn", mt);
                        }
                        WriteSse("step", new
                        {
                            phase = "model_done",
                            iter = iter,
                            ms = _swModel.ElapsedMilliseconds,
                            tokIn = Sess._trTokIn,
                            tokOut = Sess._trTokOut
                        });
                    }
                }
                catch (AnthropicRateLimitException)
                {
                    WriteSse("error", new { message = "Rate limit. Wait ~60s." });
                    return;
                }
                catch (AnthropicAuthException)
                {
                    WriteSse("error", new { message = "AI auth failed." });
                    return;
                }

                JObject resp = JObject.Parse(raw);
                string stopReason = resp["stop_reason"] != null ? resp["stop_reason"].ToString() : "";
                JArray content = resp["content"] as JArray;
                if (content == null) content = new JArray();

                List<JToken> toolUseBlocks = new List<JToken>();
                List<JToken> textBlocks = new List<JToken>();
                foreach (JToken block in content)
                {
                    string t = block["type"] != null ? block["type"].ToString() : "";
                    if (t == "tool_use") toolUseBlocks.Add(block);
                    if (t == "text") textBlocks.Add(block);
                }

                // ── TOOL USE ──
                if (stopReason == "tool_use" && toolUseBlocks.Count > 0)
                {
                    foreach (JToken block in toolUseBlocks)
                    {
                        string toolName = block["name"] != null ? block["name"].ToString() : "";
                        int owner = ownerOf(toolName);
                        WriteSse("tool_use", new { name = toolName + " \u00b7 " + EngineName(owner) });
                    }

                    workMessages.Add(new { role = "assistant", content = content });

                    List<object> toolResults = new List<object>();
                    if (Sess._jsonCapture != null)
                    {
                        // verdict mode: permit + clamp first, then execute in
                        // parallel chunks (<=4); ALL side-effects (logs, counters,
                        // results) are applied sequentially after the join, so
                        // LogToolEvent and the counters stay single-threaded.
                        List<JObject> execs = new List<JObject>();
                        foreach (JToken block in toolUseBlocks)
                        {
                            string toolName = block["name"] != null ? block["name"].ToString() : "";
                            string toolId = block["id"] != null ? block["id"].ToString() : "";
                            JToken toolArgs = block["input"];
                            int owner = ownerOf(toolName);
                            ct.ThrowIfCancellationRequested();
                            string denyReason;
                            if (!JsonToolPermitted(toolName, out denyReason))
                            {
                                LogToolEvent(conversationId, "denied", iter, toolId, toolName, owner, toolArgs, denyReason, null, 0);
                                AiLog("WARN", "TOOL", "denied " + toolName + " -- " + denyReason);
                                toolResults.Add(new { type = "tool_result", tool_use_id = toolId, content = denyReason });
                                continue;
                            }
                            string already = PrefetchAlreadyHas(toolName, toolArgs as JObject);
                            if (already != null)
                            {
                                LogToolEvent(conversationId, "denied", iter, toolId, toolName, owner, toolArgs, already, null, 0);
                                AiLog("INFO", "TOOL", "skipped " + toolName + " -- already pre-fetched");
                                toolResults.Add(new { type = "tool_result", tool_use_id = toolId, content = already });
                                continue;
                            }
                            // v1.0.160.194: chat progress guard. A repeated identical call to a
                            // read-only lookup is thrashing, not progress -- skip the re-run, tell the
                            // model it already has that result, and force the next turn to answer with
                            // no tools. Scoped to IsRepeatGuardTool (search_tags) so a tool that may
                            // legitimately refresh live data is never blocked; widen only after testing.
                            if (!rawJsonMode && _chatLoopGuard && IsRepeatGuardTool(toolName))
                            {
                                string callSig = toolName + "|"
                                    + (toolArgs != null ? toolArgs.ToString(Newtonsoft.Json.Formatting.None) : "");
                                if (!chatToolSigs.Add(callSig))
                                {
                                    string repeatNote = "You already ran '" + toolName
                                        + "' with these arguments; the result is already available above."
                                        + " Do not repeat the tool. Answer the user's question from the"
                                        + " evidence already gathered.";
                                    LogToolEvent(conversationId, "denied", iter, toolId, toolName, owner, toolArgs, "repeat call -- chat progress guard", null, 0);
                                    AiLog("INFO", "TOOL", "chat repeat blocked: " + toolName);
                                    toolResults.Add(new { type = "tool_result", tool_use_id = toolId, content = repeatNote });
                                    chatForceAnswer = true;
                                    continue;
                                }
                            }
                            string zeroId = ZeroIdentifier(toolArgs);
                            if (zeroId != null)
                            {
                                denyReason = "Refused: '" + toolName + "' was called with " + zeroId
                                    + "=0, which identifies nothing. Resolve the real id first "
                                    + "(search_assets for AssetId, get_asset_tags for TagId). If it cannot "
                                    + "be resolved, say so and return INCONCLUSIVE - do not call with zeros.";
                                LogToolEvent(conversationId, "denied", iter, toolId, toolName, owner, toolArgs, denyReason, null, 0);
                                AiLog("WARN", "TOOL", "refused " + toolName + " with " + zeroId + "=0");
                                toolResults.Add(new { type = "tool_result", tool_use_id = toolId, content = denyReason });
                                continue;
                            }
                            if (loaded.OwnerMap == null || !loaded.OwnerMap.ContainsKey(toolName))
                            {
                                // verdict mode never falls back to shared routing state:
                                // the tool must exist in THIS request's discovery snapshot
                                denyReason = "Tool '" + toolName + "' is not in this request's discovered toolset.";
                                LogToolEvent(conversationId, "denied", iter, toolId, toolName, owner, toolArgs, denyReason, null, 0);
                                toolResults.Add(new { type = "tool_result", tool_use_id = toolId, content = denyReason });
                                continue;
                            }
                            HashSet<string> declaredArgs = null;
                            if (loaded.ArgMap != null && loaded.ArgMap.ContainsKey(toolName)) { declaredArgs = loaded.ArgMap[toolName]; }
                            toolArgs = ClampToolArgs(toolName, toolArgs, declaredArgs); // mechanical cost bound
                            CollectAssetHints(toolArgs);
                            LogToolEvent(conversationId, "requested", iter, toolId, toolName, owner, toolArgs, null, null, -1);
                            JObject e0 = new JObject();
                            e0["toolName"] = toolName; e0["toolId"] = toolId; e0["owner"] = owner; e0["args"] = toolArgs;
                            execs.Add(e0);
                        }
                        int chunkSize = _jsonParallelTools ? _jsonMaxChunk : 1; // parallel OFF by default until client thread-safety is certified
                        for (int c0 = 0; c0 < execs.Count; c0 += chunkSize)
                        {
                            int cN = Math.Min(chunkSize, execs.Count - c0);
                            Task<string>[] tasks = new Task<string>[cN];
                            System.Diagnostics.Stopwatch swAll = System.Diagnostics.Stopwatch.StartNew();
                            for (int k = 0; k < cN; k++)
                            {
                                JObject e0 = execs[c0 + k];
                                // children carry NO token: the outer WhenAll wrapper owns
                                // cancellation AND the abandonment weight (no double count)
                                tasks[k] = CallToolRoutedAsync(e0["toolName"].ToString(), e0["args"], System.Threading.CancellationToken.None, (int)e0["owner"]);
                            }
                            try
                            {
                                await AwaitWithCancel((Task)Task.WhenAll(tasks), ct, cN).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                if (ct.IsCancellationRequested) { throw; } // our cancel: in-flight chunk drains, observed
                            }
                            catch { /* individual faults handled per task below */ }
                            finally
                            {
                                swAll.Stop();
                                if (Sess._traceActive)
                                {
                                    Sess._trToolMs += swAll.ElapsedMilliseconds;
                                    // v1.0.160.110 SL5: in-loop WALL-CLOCK, kept separate from the
                                    // summed per-call figure so total can be reconciled honestly.
                                    Sess._trInLoopWallMs += swAll.ElapsedMilliseconds;
                                }
                            }
                            // lock contention is an ENDPOINT condition (503 busy), not a
                            // per-tool failure: surface it instead of burying it in a
                            // tool_result. Previously WhenAll faults were swallowed and
                            // ClientBusyException became "internal error".
                            for (int k = 0; k < cN; k++)
                            {
                                AggregateException tag = tasks[k].Exception;
                                if (tag != null && tag.GetBaseException() is ClientBusyException) { throw tag.GetBaseException(); }
                            }
                            for (int k = 0; k < cN; k++)
                            {
                                JObject e0 = execs[c0 + k];
                                string toolName = e0["toolName"].ToString();
                                string toolId = e0["toolId"].ToString();
                                int owner = (int)e0["owner"];
                                JToken toolArgs = e0["args"];
                                string result;
                                Task<string> tk = tasks[k];
                                if (tk.Status == TaskStatus.RanToCompletion)
                                {
                                    string rawResult = tk.Result;
                                    result = NarrowAttributeRange(toolName, rawResult);
                                    bool usable = LooksUsableToolResult(result, IsEvidenceTool(toolName));
                                    if (Sess._traceActive)
                                    {
                                        long rawLen = rawResult == null ? 0 : rawResult.Length;
                                        long keptLen = result == null ? 0 : result.Length;
                                        Sess._trToolCallCount++;
                                        Sess._trToolBytes += rawLen;
                                        Sess._trToolBytesKept += keptLen;
                                        JObject tc = new JObject();
                                        tc["tool"] = toolName;
                                        tc["server"] = "srv" + owner;
                                        tc["iter"] = iter;
                                        tc["ms"] = swAll.ElapsedMilliseconds;
                                        tc["argsBytes"] = toolArgs == null ? 0
                                            : Encoding.UTF8.GetByteCount(toolArgs.ToString(Formatting.None));
                                        tc["resultChars"] = rawLen;
                                        tc["keptChars"] = keptLen;
                                        tc["narrowed"] = keptLen != rawLen;
                                        tc["truncatedInTransit"] = rawResult != null && _truncMarker.IsMatch(rawResult);
                                        tc["usable"] = usable;
                                        tc["evidenceClass"] = IsEvidenceTool(toolName);
                                        tc["historyClass"] = IsHistoryFamily(toolName);
                                        if (!usable) { tc["rejectReason"] = RejectReason(result, IsEvidenceTool(toolName)); }
                                        try { tc["args"] = CloneAndRedact(toolArgs); } catch { }
                                        TraceAnalyze("tool_call", tc);
                                        if (rawResult != null && _truncMarker.IsMatch(rawResult))
                                        {
                                            AiLog("WARN", "TOOL", toolName + " TRUNCATED in transit at "
                                                + rawLen + " chars -- narrow the query");
                                        }
                                        if (keptLen != rawLen)
                                        {
                                            AiLog("INFO", "TOOL", toolName + " narrowed " + rawLen + " -> " + keptLen + " chars");
                                        }
                                        if (!usable)
                                        {
                                            AiLog("WARN", "TOOL", toolName + " UNUSABLE (" + rawLen + "ch) "
                                                + RejectReason(result, IsEvidenceTool(toolName)));
                                        }
                                    }
                                    if (Sess._jsonToolTrace != null && Sess._jsonToolTrace.Count < 20)
                                    {
                                        string entry = toolName + (usable ? ":ok(" : ":UNUSABLE(")
                                            + (result == null ? "null" : result.Length + "ch") + ")";
                                        if (!usable)
                                        {
                                            entry += " [" + RejectReason(result, IsEvidenceTool(toolName)) + "] >>"
                                                + TracePreview(result);
                                        }
                                        Sess._jsonToolTrace.Add(entry);
                                    }
                                    if (usable)
                                    {
                                        Sess._jsonToolOk++;
                                        if (IsEvidenceTool(toolName)) { Sess._jsonGotEvidence = true; }
                                        // v1.0.76.0: a successful model-issued search_tags is the
                                        // ONLY route to a presumed alerting tag, resolved mechanically.
                                        if (string.Equals(toolName, "search_tags", StringComparison.OrdinalIgnoreCase))
                                        {
                                            TryResolveAlertingTagFromSearch(result);
                                        }
                                        if (IsHistoryFamily(toolName))
                                        {
                                            Sess._jsonGotHistory = true;
                                            if (Sess._jsonEvidenceArgs == null)
                                            {
                                                try
                                                {
                                                    JObject ev = new JObject();
                                                    // identity FIRST: truncation must never drop the hash
                                                    ev["resultSha"] = ShortHash(result);
                                                    ev["resultLen"] = result == null ? 0 : result.Length;
                                                    ev["tool"] = toolName;
                                                    ev["server"] = "srv" + owner;
                                                    ev["args"] = LimitForLog(CloneAndRedact(toolArgs).ToString(Formatting.None), 400);
                                                    Sess._jsonEvidenceArgs = LimitForLog(ev.ToString(Formatting.None), 900);
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                    LogToolEvent(conversationId, "response", iter, toolId, toolName, owner, toolArgs, result, null, swAll.ElapsedMilliseconds);
                                    RecordToolTiming(toolName, toolArgs, result, iter, swAll.ElapsedMilliseconds);
                                    // v1.0.75.0: coverage stamps are only readable for the true series
                                    // tool. analyse_alert/get_hist_realtime stay in _jsonHistoryFamily
                                    // for grounding, but their report shapes carry no series to verify.
                                    if (usable && IsCoverageCheckedTool(toolName))
                                    {
                                        string cov = HistoryCoverageNote(result, HistoryTagFromArgs(toolArgs as JObject));
                                        if (cov != null)
                                        {
                                            result = result + "\n" + cov;
                                            AddDiag("HISTORY_SHORT");
                                            AiLog("WARN", "TOOL", toolName + " " + cov);
                                        }
                                    }
                                    if (!usable)
                                    {
                                        // verdict mode: unusable/error payloads (which may embed raw
                                        // backend text from the MCP dispatcher) never reach the model
                                        result = IsOptionalEnrichment(toolName)
                                            ? "Optional enrichment '" + toolName + "' is unavailable this cycle. "
                                              + "It is supplementary only: the verdict does not depend on it. "
                                              + "Do NOT mention it in caveats or evidence -- an unavailable "
                                              + "optional source is not a limitation of this analysis."
                                            : "Tool '" + toolName + "' returned no usable data.";
                                    }
                                }
                                else
                                {
                                    Exception tex = tk.Exception != null ? tk.Exception.GetBaseException() : null;
                                    if (tk.IsCanceled || (tex is OperationCanceledException && !ct.IsCancellationRequested))
                                    {
                                        result = "Tool '" + toolName + "' failed: timeout"; // cancelled/timed-out child
                                    }
                                    else
                                    {
                                        result = "Tool '" + toolName + "' failed: " + SanitizeToolFailure(tex);
                                    }
                                    if (Sess._jsonToolTrace != null && Sess._jsonToolTrace.Count < 20) { Sess._jsonToolTrace.Add(toolName + ":FAIL"); }
                                    AiLogEx("TOOL", "call failed " + toolName + " (srv" + owner + ")", tex);
                                    LogToolEvent(conversationId, "error", iter, toolId, toolName, owner, toolArgs, result, tex, swAll.ElapsedMilliseconds);
                                }
                                toolResults.Add(new { type = "tool_result", tool_use_id = toolId, content = result });
                            }
                        }
                    }
                    else
                    {
                        foreach (JToken block in toolUseBlocks)
                        {
                            string toolName = block["name"] != null ? block["name"].ToString() : "";
                            string toolId = block["id"] != null ? block["id"].ToString() : "";
                            JToken toolArgs = block["input"];
                            int owner = ownerOf(toolName);

                            ct.ThrowIfCancellationRequested();

                            // ---- same policy as the verdict path ----
                            string sseDeny = null;
                            if (Sess._jsonToolUsed != null && !JsonToolPermitted(toolName, out sseDeny)) { }
                            string sseAlready = PrefetchAlreadyHas(toolName, toolArgs as JObject);
                            if (sseAlready != null) { sseDeny = sseAlready; }
                            string sseZero = ZeroIdentifier(toolArgs);
                            if (sseZero != null)
                            {
                                sseDeny = "Refused: '" + toolName + "' was called with " + sseZero
                                    + "=0, which identifies nothing. Resolve the real id first.";
                            }
                            if (sseDeny != null)
                            {
                                AiLog("WARN", "TOOL", "denied " + toolName + " -- " + sseDeny);
                                LogToolEvent(conversationId, "denied", iter, toolId, toolName, owner, toolArgs, sseDeny, null, 0);
                                WriteSse("step", new { phase = "tool_done", name = toolName,
                                                       engine = EngineName(owner), ms = 0, chars = 0, ok = false });
                                toolResults.Add(new { type = "tool_result", tool_use_id = toolId, content = sseDeny });
                                continue;
                            }
                            HashSet<string> sseDeclared = null;
                            if (loaded.ArgMap != null && loaded.ArgMap.ContainsKey(toolName)) { sseDeclared = loaded.ArgMap[toolName]; }
                            toolArgs = ClampToolArgs(toolName, toolArgs, sseDeclared);
                            CollectAssetHints(toolArgs);

                            LogToolEvent(
                                conversationId,
                                "requested",
                                iter,
                                toolId,
                                toolName,
                                owner,
                                toolArgs,
                                null,
                                null,
                                -1);

                            string result;
                            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                            try
                            {
                                string sseRaw = await CallToolRoutedAsync(toolName, toolArgs, ct, owner)
                                    .ConfigureAwait(false);
                                sw.Stop();
                                result = NarrowAttributeRange(toolName, sseRaw);
                                bool sseUsable = LooksUsableToolResult(result, IsEvidenceTool(toolName));
                                // v1.0.76.0: same mechanical resolution as the JSON path.
                                if (sseUsable && string.Equals(toolName, "search_tags", StringComparison.OrdinalIgnoreCase))
                                {
                                    TryResolveAlertingTagFromSearch(result);
                                }
                                if (sseUsable && IsCoverageCheckedTool(toolName))
                                {
                                    string cov = HistoryCoverageNote(result, HistoryTagFromArgs(toolArgs as JObject));
                                    if (cov != null) { result = result + "\n" + cov; AiLog("WARN", "TOOL", toolName + " " + cov); }
                                }
                                if (!sseUsable)
                                {
                                    AiLog("WARN", "TOOL", toolName + " UNUSABLE (" + (sseRaw == null ? 0 : sseRaw.Length)
                                        + "ch) " + RejectReason(result, IsEvidenceTool(toolName)));
                                    result = IsOptionalEnrichment(toolName)
                                        ? "Optional enrichment '" + toolName + "' is unavailable this cycle. "
                                          + "It is supplementary only; do not treat its absence as a limitation."
                                        : "Tool '" + toolName + "' returned no usable data.";
                                }
                                LogToolEvent(
                                    conversationId,
                                    "response",
                                    iter,
                                    toolId,
                                    toolName,
                                    owner,
                                    toolArgs,
                                    result,
                                    null,
                                    sw.ElapsedMilliseconds);
                            }
                            catch (OperationCanceledException)
                            {
                                if (ct.IsCancellationRequested) { throw; } // our cancel only
                                sw.Stop();
                                result = "Tool '" + toolName + "' failed: timeout"; // recoverable (rev2 parity)

                                LogToolEvent(
                                    conversationId,
                                    "error",
                                    iter,
                                    toolId,
                                    toolName,
                                    owner,
                                    toolArgs,
                                    result,
                                    null,
                                    sw.ElapsedMilliseconds);
                            }
                            catch (ClientBusyException) { throw; } // surfaces as HTTP 503 busy
                            catch (Exception ex)
                            {
                                sw.Stop();
                                result = "Tool '" + toolName + "' failed: " + SanitizeToolFailure(ex);

                                LogToolEvent(
                                    conversationId,
                                    "error",
                                    iter,
                                    toolId,
                                    toolName,
                                    owner,
                                    toolArgs,
                                    result,
                                    ex,
                                    sw.ElapsedMilliseconds);
                            }

                            // Streaming path only: the popup renders these into the live
                            // progress trail. Verdict mode captures instead of streaming,
                            // so emitting there would be a no-op.
                            WriteSse("step", new
                            {
                                phase = "tool_done",
                                name = toolName,
                                engine = EngineName(owner),
                                ms = sw.ElapsedMilliseconds,
                                chars = result == null ? 0 : result.Length,
                                ok = !result.StartsWith("Tool '", StringComparison.Ordinal)
                                     && !result.StartsWith("Refused:", StringComparison.Ordinal)
                                     && !result.StartsWith("Optional enrichment", StringComparison.Ordinal)
                            });

                            toolResults.Add(new
                            {
                                type = "tool_result",
                                tool_use_id = toolId,
                                content = result
                            });
                        }

                    }

                    workMessages.Add(new { role = "user", content = toolResults });
                    continue;
                }

                // ── TEXT ──
                if (textBlocks.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (JToken b in textBlocks)
                        if (b["text"] != null) sb.Append(b["text"].ToString());

                    // v1.0.160.60: scrub the CHAT/FOLLOW-UP prose too. rawJsonMode is JSON and is
                    // sanitised field-by-field later; this branch is the reply a user reads, and
                    // it is where bash output would land if it were pasted into an answer.
                    string fullText = rawJsonMode ? sb.ToString()
                        : ScrubPlatformInternals(FilterOperationalActions(sb.ToString()), "chat");
                    if (Sess._assistantCapture != null) { try { Sess._assistantCapture.Append(fullText); } catch { } }   // v1.0.133.0

                // SAFETY. rawJsonMode skips FilterOperationalActions because the text is
                // JSON, and the popup then parsed that JSON in the browser -- so the
                // operator-visible path applied NONE of the verdict contract: no
                // movement-directive filter, no category check, no numeric validation,
                // no word limits, no unknown-field stripping. Those ran only on the
                // service endpoint. Validate here (validateVerdictOutput, NOT rawJsonMode
                // -- AnalyzeChat also sends rawJsonMode=true but is prose, never a verdict)
                // and emit the sanitised object as its own frame; the client prefers it
                // over anything it parsed itself.
                if (validateVerdictOutput && Sess._jsonCapture == null && !string.IsNullOrWhiteSpace(fullText))
                {
                    JObject vObj = null;
                    try { vObj = JObject.Parse(fullText.Trim()); }
                    catch { vObj = ExtractFirstJsonObject(fullText); }

                    if (vObj != null)
                    {
                        string vReason;
                        if (ValidateAndSanitizeVerdict(vObj, out vReason))
                        {
                            // v1.0.111.0: attach server-computed derived_inputs on the popup path
                            // too - suppressed on INCONCLUSIVE / inadequate coverage.
                            if (ShouldAttachDerivedInputs(vObj)) { vObj["derived_inputs"] = Sess._derivedInputs; }
                            // v1.0.111.0: attach server-VERIFIED triage (computed from the parsed
                            // same-asset/same-cause alert history), also suppressed on INCONCLUSIVE.
                            AttachTriageIfEligible(vObj);
                            if (Sess._weatherCard != null) { vObj["weather"] = Sess._weatherCard; }   // v1.0.119.0
                            if (Sess._trainContext != null) { vObj["train_context"] = Sess._trainContext; }   // v1.0.160.33
                            // v1.0.108.0 / hardened 1.0.111.0: bilingual on the popup path. vObj is
                            // sanitized; AttachHinglishAsync now runs under the AI lock with its own
                            // timeout+cancellation and records its tokens. Best-effort - a failure
                            // leaves the English verdict frame unchanged.
                            if (_bilingualHindi)
                            {
                                try { await AttachHinglishAsync(vObj, ct).ConfigureAwait(false); }
                                catch (Exception exTrP) { AiLog("WARN", "HI", "popup hinglish translate failed (English unaffected): " + SanitizeJsonError(exTrP.Message)); }
                            }
                            // v1.0.160.113: the POPUP is fed by SSE, not by the JSON envelope, so
                            // context_patch / asset_condition / snapshot must be emitted HERE too --
                            // adding them only to env (AnalyzeAlertJson) left the view with nothing
                            // to render, which is exactly the transport mismatch to avoid.
                            try
                            {
                                if (Sess._prefetchCtx != null)
                                {
                                    JObject _pp = new JObject();
                                    string _st = GetCtx(Sess._prefetchCtx, "station");
                                    string _at = GetCtx(Sess._prefetchCtx, "assetType");
                                    if (_st.Length > 0) { _pp["station"] = _st; }
                                    if (_at.Length > 0) { _pp["assetType"] = _at; }
                                    // v1.0.160.130: the SSE path is the one the POPUP reads. Adding
                                    // resetTime only to the JSON envelope would leave the banner
                                    // exactly as wrong as before -- the same transport mismatch.
                                    string _rs = GetCtx(Sess._prefetchCtx, "resetTime");
                                    if (_rs.Length > 0) { _pp["resetTime"] = _rs; }
                                    if (_pp.Count > 0) { WriteSse("context_patch", new { patch = _pp }); }
                                }
                                JObject _ac = BuildAssetCondition();
                                if (_ac != null) { WriteSse("asset_condition", new { condition = _ac }); }
                                if (Sess._snapshotRetrievedAt != DateTime.MinValue)
                                {
                                    JObject _sn = new JObject();
                                    _sn["retrievedAt"] = Sess._snapshotRetrievedAt.ToString("dd/MM/yyyy HH:mm:ss");
                                    DateTime _incS;
                                    if (Sess._prefetchCtx != null && TryParseAlertTime(GetCtx(Sess._prefetchCtx, "time"), out _incS))
                                    { _sn["ageFromIncidenceSec"] = (long)(Sess._snapshotRetrievedAt - _incS).TotalSeconds; }
                                    if (Sess._snapshotValues != null && Sess._snapshotValues.Count > 0) { _sn["values"] = Sess._snapshotValues; }
                                    WriteSse("snapshot", new { snapshot = _sn });
                                }
                                // v1.0.160.176: point machine -> fetch v4 analysis SERVER-SIDE and
                                // stream it as a 'pm' event (server->server, so no browser CORS /
                                // mixed-content). The view sets window._aiPm and renders the PM tab.
                                // Best-effort: a failure here never breaks the verdict stream.
                                try
                                {
                                    string _pmType = GetCtx(Sess._prefetchCtx, "assetType") ?? "";
                                    string _pmAid = GetCtx(Sess._prefetchCtx, "assetId");
                                    if (_pmType.ToUpperInvariant().Contains("POINT") && !string.IsNullOrEmpty(_pmAid))
                                    {
                                        string _pmSid = GetCtx(Sess._prefetchCtx, "siteId");
                                        string _pmUrl = "http://172.31.25.102:8005/api/asset/ai-prediction/pm-operation-v4/"
                                                      + (string.IsNullOrEmpty(_pmSid) ? "" : _pmSid + "/") + _pmAid;
                                        // v1.0.160.178: log this server-side fetch to the MCP/tool log
                                        // (ai-tool jsonl) via LogToolEvent -- success and failure both land
                                        // there as a 'pm-operation-v4' entry with latency + error, so a
                                        // silent bind failure is diagnosable.
                                        string _pmConv = string.IsNullOrEmpty(Sess._traceId) ? "prefetch" : Sess._traceId;
                                        JObject _pmArgs = new JObject(); _pmArgs["url"] = _pmUrl;
                                        System.Diagnostics.Stopwatch _pmSw = System.Diagnostics.Stopwatch.StartNew();
                                        try
                                        {
                                            using (System.Net.Http.HttpClient _pmHc = new System.Net.Http.HttpClient())
                                            {
                                                _pmHc.Timeout = TimeSpan.FromSeconds(60);
                                                string _pmRes = await _pmHc.GetStringAsync(_pmUrl).ConfigureAwait(false);
                                                JObject _pmObj = JObject.Parse(_pmRes);
                                                if (_pmObj["a"] is JObject _pmA) { _pmA.Remove("ops"); }
                                                if (_pmObj["b"] is JObject _pmB) { _pmB.Remove("ops"); }
                                                _pmSw.Stop();
                                                LogToolEvent(_pmConv, "response", -1, "pm-v4", "pm-operation-v4", 0, _pmArgs, _pmObj.ToString(), null, _pmSw.ElapsedMilliseconds);
                                                WriteSse("pm", new { pm = _pmObj });
                                            }
                                        }
                                        catch (Exception _pmEx)
                                        {
                                            _pmSw.Stop();
                                            LogToolEvent(_pmConv, "error", -1, "pm-v4", "pm-operation-v4", 0, _pmArgs, null, _pmEx, _pmSw.ElapsedMilliseconds);
                                        }
                                    }
                                }
                                catch { }
                            }
                            catch { }
                            // v1.0.160.154: attach the asset-history summary so the story's pie
                            // chapter renders from the verdict itself -- including on REUSED opens,
                            // because the .149 store copies vObj.
                            if (Sess._assetHistory != null)
                            {
                                try
                                {
                                    vObj["asset_history"] = Sess._assetHistory;
                                }
                                catch { }
                            }
                            // v1.0.160.153: a verdict produced while the E7 API was failing is
                            // stamped BEFORE emit and store, so the browser, the diagnostic download
                            // and the cache row all carry the same mark. TryServeFromCache refuses
                            // rows carrying it (541980: the degraded row said NOT_CONFIRMED 72, the
                            // healthy re-run said CONFIRMED 82).
                            if (Sess._e7ApiInputFailed)
                            {
                                try
                                {
                                    vObj["degraded_inputs"] = true;
                                    vObj["degraded_note"] = "Analysed while the E7 API was failing"
                                        + " (403 / session expired): envelopes, FRS row and range"
                                        + " history were unavailable, so evidence is reduced."
                                        + " Re-run once upstream is healthy.";
                                }
                                catch { }
                            }
                            WriteSse("verdict", new { verdict = vObj });
                            // v1.0.160.144: store the FULL verdict for the cache, here, where vObj
                            // is in scope. .141 stored _lastVerdictSummary instead -- four fields
                            // (verdict/confidence/category/headline) and nothing else -- so a served
                            // row rendered INCONCLUSIVE 0% with every field blank. The log said it
                            // outright: "payload (364 chars)". What is written must be what the
                            // browser was sent, or a reused verdict is not the same verdict.
                            // v1.0.160.149: store EVERYTHING the fresh render used, not just the
                            // verdict. A fresh run also emits asset_condition, snapshot and
                            // context_patch as separate SSE events; storing only the verdict meant a
                            // reused analysis could never be a clone of the original -- no Live data
                            // chapter, no asset-health band, no station/assetType fill. Replaying the
                            // same blocks is what makes "reused" mean the same answer, not a subset.
                            try
                            {
                                JObject _store = new JObject();
                                _store["verdict"] = vObj;
                                // BuildAssetCondition() is the same call the fresh path makes.
                                // (_assetCondition and _incF were my invention -- neither exists.)
                                JObject _acStore = BuildAssetCondition();
                                if (_acStore != null) { _store["asset_condition"] = _acStore; }
                                if (Sess._snapshotRetrievedAt != DateTime.MinValue)
                                {
                                    JObject _ss = new JObject();
                                    _ss["retrievedAt"] = Sess._snapshotRetrievedAt.ToString("dd/MM/yyyy HH:mm:ss");
                                    DateTime _incStore;
                                    if (Sess._prefetchCtx != null
                                        && TryParseAlertTime(GetCtx(Sess._prefetchCtx, "time"), out _incStore)
                                        && _incStore != DateTime.MinValue)
                                    { _ss["ageFromIncidenceSec"] = (long)(Sess._snapshotRetrievedAt - _incStore).TotalSeconds; }
                                    if (Sess._snapshotValues != null && Sess._snapshotValues.Count > 0) { _ss["values"] = Sess._snapshotValues; }
                                    _store["snapshot"] = _ss;
                                }
                                JObject _cp = new JObject();
                                string _cs = GetCtx(Sess._prefetchCtx, "station");
                                string _ca = GetCtx(Sess._prefetchCtx, "assetType");
                                string _cr = GetCtx(Sess._prefetchCtx, "resetTime");
                                if (_cs.Length > 0) { _cp["station"] = _cs; }
                                if (_ca.Length > 0) { _cp["assetType"] = _ca; }
                                if (_cr.Length > 0) { _cp["resetTime"] = _cr; }
                                if (_cp.Count > 0) { _store["context_patch"] = _cp; }
                                Sess._lastResponseFull = _store.ToString(Newtonsoft.Json.Formatting.None);
                            }
                            catch { }
                            // v1.0.160.146: SAY WHAT THE VERDICT WAS. The SSE path logs the row open,
                            // the tool calls and the completion size -- and never the verdict itself.
                            // So a run that produced INCONCLUSIVE/0%/blank and a run that produced a
                            // full answer look identical in the log, and the only way to tell them
                            // apart is to have been watching the screen. That is the third time this
                            // week a missing log line has cost a round of guessing.
                            try
                            {
                                string _vv = vObj["verdict"] != null ? vObj["verdict"].ToString() : "(none)";
                                string _vc = vObj["confidence"] != null ? vObj["confidence"].ToString() : "-";
                                string _vl = vObj["likely_cause"] != null ? vObj["likely_cause"].ToString() : "";
                                bool _thin = (_vv == "(none)" || _vv.Length == 0 || _vl.Length == 0);
                                AiLog(_thin ? "WARN" : "INFO", "VERDICT",
                                    "verdict=" + _vv + " confidence=" + _vc
                                    + " likely_cause=" + (_vl.Length > 0 ? Trunc(_vl, 60) : "(EMPTY)")
                                    + " fields=" + vObj.Count
                                    + " payload=" + (Sess._lastResponseFull != null ? Sess._lastResponseFull.Length : 0) + " chars"
                                    + (_thin ? " -- THIN VERDICT, the model returned little or nothing usable" : ""));
                            }
                            catch { }
                            try
                            {
                                WriteSse("stats", new {
                                    tokIn = Sess._trTokIn, tokOut = Sess._trTokOut,
                                    cacheRead = Sess._trTokCacheRead, cacheWrite = Sess._trTokCacheWrite,
                                    modelMs = Sess._trModelMs, toolMs = Sess._trToolMs, discoveryMs = Sess._trDiscoveryMs
                                });
                            }
                            catch { }
                            try
                            {
                                if (Sess._trendCandidate != null)
                                {
                                    // v1.0.152.0: multi-series frame + deterministic train mask.
                                    JObject tf = BuildTrendFrame();
                                    WriteSse("trend", tf);   // v1.0.146.0: real telemetry for the viz
                                }
                                else
                                {
                                    AiLog("INFO", "TREND", "no series captured this run (chart falls back to modelled shape)");
                                }
                            }
                            catch { }
                            try
                            {
                                Sess._lastVerdictSummary = new JObject();
                                Sess._lastVerdictSummary["verdict"] = vObj["verdict"];
                                Sess._lastVerdictSummary["confidence"] = vObj["confidence"];
                                Sess._lastVerdictSummary["category"] = vObj["category"];
                                Sess._lastVerdictSummary["headline"] = vObj["headline"];
                            }
                            catch { }
                        }
                        else
                        {
                            AiLog("WARN", "VERDICT", "popup verdict failed validation: " + vReason);
                            LogRejectedReply(fullText);   // v1.0.137.0
                            WriteSse("error", new { message = "Verdict failed validation: " + vReason });
                        }
                    }
                    else
                    {
                        AiLog("WARN", "VERDICT", "popup verdict was not JSON");
                        LogRejectedReply(fullText);   // v1.0.137.0
                        WriteSse("error", new { message = "Verdict was not valid JSON." });
                    }
                }
                    if (Sess._jsonCapture != null)
                    {
                        // lossless: capture the untouched turn text; no chunking
                        Sess._jsonCapture.Append(fullText);
                    }
                    else
                    {
                        // The joining space lives INSIDE a chunk, so when a chunk was
                        // flushed the next one started bare and the browser concatenated
                        // them with no gap ("threshold4.67"). Carry the boundary space
                        // into the next chunk.
                        string[] words = fullText.Split(' ');
                        StringBuilder chunk = new StringBuilder();
                        bool emittedAny = false;
                        for (int w = 0; w < words.Length; w++)
                        {
                            if (chunk.Length > 0) { chunk.Append(' '); }
                            else if (emittedAny) { chunk.Append(' '); }
                            chunk.Append(words[w]);
                            if (chunk.Length > 60 || w == words.Length - 1)
                            {
                                WriteSse("text", new { text = chunk.ToString() });
                                chunk.Clear();
                                emittedAny = true;
                            }
                        }
                    }
                }
                else if (stopReason == "end_turn")
                {
                    WriteSse("text", new { text = "[No response generated]" });
                }
                return;
            }

            WriteSse("text", new { text = "\n\n[Reached " + _maxIter + "-step limit.]" });
        }


        internal static List<JObject> TrimHistory(List<JObject> messages, int maxTurns)
        {
            if (messages.Count <= 2 || maxTurns <= 0) return messages;
            List<int> realUserIdx = new List<int>();
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i]["role"] == null || messages[i]["role"].ToString() != "user") continue;
                JToken c = messages[i]["content"];
                bool isToolResult = false;
                JArray arr = c as JArray;
                if (arr != null && arr.Count > 0 && arr[0]["type"] != null)
                    isToolResult = arr[0]["type"].ToString() == "tool_result";
                if (!isToolResult) realUserIdx.Add(i);
            }
            if (realUserIdx.Count <= maxTurns) return messages;
            return messages.Skip(realUserIdx[realUserIdx.Count - maxTurns]).ToList();
        }


        internal static int EstimateTokens(IEnumerable<object> messages, string system)
        {
            int total = Chars(system);
            foreach (object msg in messages)
            {
                JObject j;
                JObject jo = msg as JObject;
                j = jo != null ? jo : JObject.FromObject(msg);
                JToken c = j["content"];
                if (c == null) continue;
                if (c.Type == JTokenType.String)
                    total += Chars(c.ToString());
                else if (c.Type == JTokenType.Array)
                    foreach (JToken b in (JArray)c)
                    {
                        string t = b["type"] != null ? b["type"].ToString() : "";
                        if (t == "text" && b["text"] != null) total += Chars(b["text"].ToString());
                        if (t == "tool_use" && b["input"] != null) total += Chars(JsonConvert.SerializeObject(b["input"]));
                        if (t == "tool_result" && b["content"] != null) total += Chars(b["content"].ToString());
                    }
            }
            return total;
        }


        internal static int Chars(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return (int)Math.Ceiling(s.Length / 4.0);
        }


        // v1.0.108.0: translate the ALREADY-SANITIZED verdict into Hinglish (Romanized Hindi)
        // and attach it as verdictObj["hi"]. Runs AFTER ValidateAndSanitizeVerdict, so every
        // string it sees has already passed FilterOperationalActions + TrimWords - the Hinglish
        // therefore inherits that safety and cannot introduce an operational directive. Only the
        // display text fields are translated; verdict/confidence/category/viz stay as-is. A
        // separate single-shot model call with NO tools. Best-effort - the caller swallows any
        // failure and ships the English verdict unchanged.
        // v1.0.111.0: translate the SANITIZED verdict into Hinglish and attach as v["hi"].
        // Hardened per review: takes a cancellation token, runs the single translation call under
        // the shared AI lock (the model clients' thread-safety is unverified) with its OWN short
        // timeout, and records its tokens/time into the trace counters so the reported cost is
        // honest. It extracts the assistant text from the RAW API reply correctly (content blocks),
        // and re-runs the operational-action filter on the output. recommended_action is NOT
        // translated - it stays English until Hinglish directive filtering is mechanically reliable,
        // so the one field that could carry a movement instruction is always filter-checked English.
        internal async Task AttachHinglishAsync(JObject v, System.Threading.CancellationToken ct)
        {
            if (v == null) { return; }
            // sanitized display strings to translate - NOTE: recommended_action intentionally excluded
            JObject src = new JObject();
            string[] trFields = { "headline", "likely_cause", "caveats" };
            for (int i = 0; i < trFields.Length; i++)
            {
                JToken t = v[trFields[i]];
                if (t != null && t.Type == JTokenType.String && t.ToString().Length > 0) { src[trFields[i]] = t.ToString(); }
            }
            JArray ev = v["evidence"] as JArray;
            if (ev != null && ev.Count > 0) { src["evidence"] = ev; }
            if (src.Count == 0) { return; }

            string sys =
                "You are a translator for Indian Railway signalling staff. Translate the given verdict fields "
                + "into HINGLISH (Romanized Hindi in Latin script, the way railway field staff speak - NOT "
                + "Devanagari). RULES: (1) Keep every technical term, tag name, and abbreviation as-is in Latin "
                + "(Ir, If, TPR, RRAIL, IBALST, mA, V, ballast, glued joint, threshold, CONFIRMED, NOT_CONFIRMED, "
                + "INCONCLUSIVE). (2) Keep ALL numbers, units and values EXACTLY unchanged. (3) Translate ONLY - "
                + "do not add, remove, soften or invent any content, and never add an operational instruction. "
                + "(4) Natural field engineer tone. Return ONLY a JSON object with the SAME keys as the input "
                + "(headline, likely_cause, caveats as strings; evidence as an array of strings). No preamble, "
                + "no markdown, no extra keys.";
            object[] messages = new object[]
            {
                new { role = "user", content = "Translate these verdict fields to Hinglish. Return only the JSON object:\n" + src.ToString(Formatting.None) }
            };

            // dedicated short timeout for the translation, linked to the caller's token
            string raw = null;
            System.Diagnostics.Stopwatch swHi = System.Diagnostics.Stopwatch.StartNew();
            using (System.Threading.CancellationTokenSource hiCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                hiCts.CancelAfter(TimeSpan.FromSeconds(_hindiTimeoutSec));
                // v1.0.112.0: actually SERIALIZE against the shared model client. The previous
                // WithClientLock(..., mustOwn:false) SKIPPED the lock (mustOwn=false short-circuits
                // it), and AwaitWithCancel registers background-abandonment against _jsonBgReserved
                // - which this call never reserved, so it must NOT be used here. Take _aiLock
                // directly (honouring _serializeClients), observe the timeout, and always release.
                bool locked = false;
                try
                {
                    if (_serializeClients)
                    {
                        locked = await _aiLock.WaitAsync(_clientLockMs, hiCts.Token).ConfigureAwait(false);
                        if (!locked)
                        {
                            AiLog("WARN", "HI", "translation client lock timeout - skipping Hinglish (English verdict unaffected)");
                            return;
                        }
                    }
                    hiCts.Token.ThrowIfCancellationRequested();
                    raw = await CallModelAsync(messages, new object[0], sys).ConfigureAwait(false);
                }
                finally
                {
                    if (locked) { _aiLock.Release(); }
                    swHi.Stop();
                }
            }
            if (string.IsNullOrEmpty(raw)) { return; }

            // token/time accounting - the translation is a SECOND paid call; record it so the
            // trace does not under-report cost (turns is incremented too, so turns=1 is not a lie).
            if (Sess._traceActive)
            {
                Sess._trModelMs += swHi.ElapsedMilliseconds;
                Sess._trTurns++;
                JObject u = ReadUsage(raw);
                if (u != null)
                {
                    Sess._trTokIn += ReadLong(u, "input_tokens");
                    Sess._trTokOut += ReadLong(u, "output_tokens");
                    Sess._trTokCacheRead += ReadLong(u, "cache_read_input_tokens");
                    Sess._trTokCacheWrite += ReadLong(u, "cache_creation_input_tokens");
                }
            }

            // extract the assistant TEXT from the raw API reply (content blocks) - CallModelAsync
            // returns the full envelope, not the text.
            string outText = ExtractAssistantText(raw);
            if (string.IsNullOrEmpty(outText)) { return; }
            if (outText.IndexOf("```", StringComparison.Ordinal) >= 0)
            { outText = outText.Replace("```json", "").Replace("```", "").Trim(); }

            JObject hi = null;
            try { hi = JObject.Parse(outText); }
            catch { hi = ExtractFirstJsonObject(outText); }
            if (hi == null) { return; }

            // belt-and-braces: re-run the operational-action filter on the Hinglish output.
            JObject hiClean = new JObject();
            for (int i = 0; i < trFields.Length; i++)
            {
                JToken t = hi[trFields[i]];
                if (t != null && t.Type == JTokenType.String)
                { hiClean[trFields[i]] = ScrubPlatformInternals(FilterOperationalActions(t.ToString()), "hi." + trFields[i]); }
            }
            JArray hiEv = hi["evidence"] as JArray;
            if (hiEv != null)
            {
                JArray outEv = new JArray();
                for (int i = 0; i < hiEv.Count; i++)
                {
                    if (hiEv[i] != null && hiEv[i].Type == JTokenType.String)
                    { outEv.Add(ScrubPlatformInternals(FilterOperationalActions(hiEv[i].ToString()), "hi.evidence")); }
                }
                if (outEv.Count > 0) { hiClean["evidence"] = outEv; }
            }
            // carry the English recommended_action into the hi block UNCHANGED, clearly marked,
            // so the card can show it under the Hinglish section without an unfiltered translation.
            if (v["recommended_action"] != null && v["recommended_action"].Type == JTokenType.String)
            { hiClean["recommended_action_en"] = ScrubPlatformInternals(v["recommended_action"].ToString(), "hi.recommended_action_en"); }
            if (hiClean.Count > 0) { v["hi"] = hiClean; }
        }


        // Extract concatenated assistant text ("text" content blocks) from a raw Anthropic reply.
        internal static string ExtractAssistantText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) { return null; }
            try
            {
                JObject resp = JObject.Parse(raw);
                JArray content = resp["content"] as JArray;
                if (content == null) { return null; }
                StringBuilder sb = new StringBuilder();
                foreach (JToken block in content)
                {
                    string t = block["type"] != null ? block["type"].ToString() : "";
                    if (t == "text" && block["text"] != null) { sb.Append(block["text"].ToString()); }
                }
                return sb.ToString();
            }
            catch { return null; }
        }
    }
}
