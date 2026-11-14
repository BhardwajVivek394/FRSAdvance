using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore.Judge
{
    /// <summary>The model call. Production: JudgeAi.HttpCall (OpenAI-compatible or Anthropic shape). Tests: a fake.</summary>
    internal delegate string JudgeModelCall(string url, string apiKey, bool anthropicShape, string requestBody, int timeoutSec);

    // ============================================================================================
    // JudgeAi -- the AI step. Port of AlertJudge v1.1.2.0 Pipeline/AiReasoner.cs: the same system
    // prompt, the same JSON input pack (question, case, gates, attrsAtSet, alertCard, thresholds,
    // telemetry, causeCatalog, decisionReference), the same downsample / trend / provenance rules.
    //
    // CHANGE HERE to change what the AI is asked or what it is given. The rule text it follows is
    // App_Data\AlertJudge\Rules\decision-reference.md (+ Rules\wisdom\<cause>.md), editable without a rebuild.
    //
    // 2.0 adds a "liveCheck" section (the card conditions re-checked against live data) and asks two
    // explicit questions on top of the recommendation: logic_correct (is the cause logic right for
    // this card and catalog?) and data_correct (does the live data really show the condition?).
    // JudgePolicy passes only when both are true.
    //
    // Provider keys come from web.config exactly as AiChat / AssetHealth read them:
    //   AiProvider = deepseek | openai | anthropic; DeepSeekApiKey/DeepSeekModel/DeepSeekBaseUrl;
    //   OpenAiApiKey/OpenAiModel/OpenAiBaseUrl; AnthropicApiKey/AnthropicModel.
    // ============================================================================================
    internal sealed class JudgeAi
    {
        private readonly JudgeSettings cfg;
        private readonly JudgeModelCall call;

        internal JudgeAi(JudgeSettings cfg)
            : this(cfg, null)
        {
        }

        internal JudgeAi(JudgeSettings cfg, JudgeModelCall call)
        {
            this.cfg = cfg;
            this.call = call ?? HttpCall;
        }

        /// <summary>
        /// 1.0.189.0 -- HttpCall found a 200 OK with no usable content and is reporting WHY.
        /// A string starting with this marker is a diagnostic, not a reply. It exists because the
        /// JudgeModelCall delegate returns a plain string (test fakes depend on that signature), so
        /// there is nowhere else to put finish_reason and the token counts without a static field
        /// that four concurrent judgements would race on.
        /// A fake that returns "" still produces the plain "empty reply" of earlier builds.
        /// </summary>
        internal const string EmptyMarker = "\u0001judge-no-content\u0001";

        /// <summary>
        /// 1.0.189.1 -- an appSettings bool, parsed EXACTLY as AiSettings.ParseBool parses it, which
        /// is what AnalysisEngine.ReadBool and therefore CreateDeepSeekClient use.
        ///
        /// 1.0.189.0 had its own three-way parse -- true/1 -> true, false/0 -> false, anything else
        /// -> the default. That agreed with ParseBool on every value except two: "yes" and "on",
        /// which ParseBool accepts as TRUE. So DeepSeekThinking="yes" would have enabled thinking
        /// for AnalyzeAlertJson and left it disabled for the Judge -- the two endpoints disagreeing
        /// about one config value, which is the whole class of bug 1.0.187.0 and 1.0.188.0 existed
        /// to end. Unlikely in practice; not a difference worth keeping.
        ///
        /// Verbatim from Settings\AiSettings.cs ParseBool: blank -> def, then true|1|yes|on -> true
        /// and EVERYTHING else -> false.
        /// </summary>
        private static bool ReadCfgBool(string key, bool def)
        {
            string raw = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(raw)) { return def; }
            raw = raw.Trim();
            return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("1", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>1.0.189.0 -- an appSettings int, read the way AnalysisEngine.ReadInt does.</summary>
        private static int ReadCfgInt(string key, int def)
        {
            string s = ConfigurationManager.AppSettings[key];
            int v;
            return !string.IsNullOrWhiteSpace(s) && int.TryParse(s.Trim(), out v) ? v : def;
        }

        internal sealed class Provider
        {
            public string Name = "";
            public string Url = "";
            public string ApiKey = "";
            public string Model = "";
            public bool AnthropicShape;
            public AiModelRow Row;   // 1.0.193.0 -- the catalogue row behind the four fields above
        }

        /// <summary>
        /// 1.0.187.0 -- kept so nothing that called the old parameterless form breaks. Resolves the
        /// vendor from configuration alone, i.e. as if the request named no provider.
        /// </summary>
        internal static Provider ReadProvider()
        {
            return ReadProvider(null, null);
        }

        // =========================================================================================
        // 1.0.187.0 -- THE JUDGE NOW ASKS THE SAME QUESTION EVERYONE ELSE ASKS.
        //
        // Before this, ReadProvider() read ONLY the AiProvider appSetting and never looked at the
        // request. With Web.config carrying AiProvider = "anthropic", a request saying
        // provider = "QUANTUM" went to two different vendors depending on which endpoint got it:
        //
        //     AiChat/AnalyzeAlertJson   -> deepseek    (ProviderKind reads the request)   worked
        //     AlertJudge                -> anthropic   (this method ignored it)           400, no credit
        //
        // So "my AnalyzeAlertJson works but the Judge does not" was never about the Judge's logic.
        // It was two code paths disagreeing about what "QUANTUM" means. The map now lives once, in
        // EngineIdentity.ResolveProviderKind.
        //
        // PRECEDENCE, highest first:
        //   1. Ai.Provider in judge-settings.json   -- re-read within 30 s, NO app-pool recycle.
        //      This exists because a Web.config edit needs a recycle and a deploy window, and the
        //      vendor is exactly the thing you want to change at 2 a.m. when one account is down.
        //   2. the request's judge.engine, else ctx.provider   -- what the caller asked for.
        //   3. the AiProvider appSetting   -- the server default.
        //   4. quantum10 = DeepSeek Pro.   <-- 1.0.188.0, see below.
        //
        // Set Ai.Provider = "" (the default) to leave the choice to the request and Web.config,
        // exactly as AnalyzeAlertJson does.
        //
        // -----------------------------------------------------------------------------------------
        // 1.0.188.0 -- STEP 4 WAS "anthropic". IT IS NOW "quantum10", MATCHING EVERYONE ELSE.
        //
        // 187 made the Judge read the request. It did NOT fix the case where nothing names a vendor
        // at all -- no Ai.Provider, no provider on the request, and no AiProvider key in Web.config.
        // There the Judge fell back to anthropic while the two other readers of that same key in
        // this very assembly fell back to DeepSeek:
        //
        //     AnalysisEngine.State.cs  _aiProviderDefault = ReadStr("AiProvider", "quantum10")
        //     ChatEngine.Main.cs       AppSettings["AiProvider"] ?? "quantum10"
        //     JudgeAi.cs (until now)   AppSettings["AiProvider"] ?? "anthropic"      <-- the odd one
        //
        // This is not a new judgement call. It is AnalysisEngine changelog 1.0.160.202, "DEFAULT
        // ENGINE = QUANTUM (DeepSeek Pro) FOR ALL" -- an owner decision already taken. 1.0.160.201
        // records the identical fault one release earlier: "_aiProviderDefault fell back to
        // 'anthropic' when Web.config had no AiProvider key, so any caller that sends no provider
        // ran claude-sonnet-4-6 while the page has run quantum11". The Judge was written after that
        // fix and never received it.
        //
        // WHY quantum10 AND NOT "deepseek". Both resolve to the DeepSeek transport, but quantum10 is
        // the literal AnalysisEngine uses, it travels through the same brand map, and
        // WantsFlashVariant("quantum10") is false -- so a provider-less caller gets DeepSeek PRO,
        // exactly as .160.202 specifies. Hardcoding "deepseek" would work today and drift the moment
        // the brand map changes.
        //
        // NOT CHANGED: the final "return anthropic" in ResolveProviderKind. That answers a DIFFERENT
        // question -- "a vendor WAS named and I do not recognise it" -- and AnalysisEngine's
        // ProviderKind ends the same way. Keeping those two identical is the whole point of having
        // one map. Only the nothing-was-named default moved.
        // -----------------------------------------------------------------------------------------
        // =========================================================================================
        internal static Provider ReadProvider(string requestedEngine, JudgeSettings cfg)
        {
            Provider p = new Provider();

            string pinned = cfg != null ? (cfg.AiProvider ?? "").Trim() : "";

            // =====================================================================================
            // 1.0.193.0 -- dbo.AiModelConfig DECIDES, NOT Web.config.
            //
            // Precedence, per the 16.10.2026 decision:
            //   1. Ai.Provider in judge-settings.json -- an operator typed it, so it counts as a
            //      HUMAN choice and outranks the table. This is the 2 a.m. lever: the file is
            //      re-read within 30 s, so moving the judge to another engine still needs no
            //      rebuild and no app-pool recycle.
            //   2. the IsDefault = 1 AND IsEnabled = 1 row.
            //
            // requestedEngine is deliberately NOT in that list. AlertV3 sends a baked-in "QUANTUM"
            // on every judge request -- a hardcoded literal in another program, not a person
            // choosing a model -- so the catalogue default outranks it. It is still carried into
            // the ai: log line in ReasonAsync, because "what did the caller ask for, and what did
            // it actually get?" is the first question asked of any judgement.
            //
            // WHAT THIS REPLACES: the four-branch AiProvider read that resolved OpenAiModel /
            // DeepSeekModel / DeepSeekFlashModel / AnthropicModel and the three *ApiKey keys out of
            // appSettings. None of those are consulted for the model or the key any more.
            // OpenAiBaseUrl and DeepSeekBaseUrl are still honoured, through AiModelRow.Endpoint(),
            // until the table carries its own BaseUrl column.
            // =====================================================================================
            AiModelRow row = pinned.Length > 0
                ? AiModelCatalog.Resolve(pinned, true)
                : AiModelCatalog.Default();

            p.Row = row;
            p.Name = row.Provider;
            p.ApiKey = row.Key ?? "";
            p.Model = row.ModelName;
            p.Url = row.Endpoint();
            p.AnthropicShape = row.AnthropicShape;

            return p;
        }

        internal async Task<JudgeAiVerdict> ReasonAsync(JudgeCard card, JudgeCauseDef def, JudgeVerifierOutcome v0, JudgeEvidenceBundle ev, JudgeLiveResult live, JudgePriorDiscardResult prior, string engine, List<string> log, CancellationToken ct)
        {
            JudgeAiVerdict v = new JudgeAiVerdict();
            Provider p;
            try
            {
                p = ReadProvider(engine, cfg);
            }
            catch (AiModelCatalogException ex)
            {
                // 1.0.205.0 -- the model list could not be loaded and nothing is cached (the
                // AiModelConfig API refused or was unreachable, typically right after an app pool
                // restart). That is "the AI is unavailable", which JudgePolicy already handles
                // (decidedBy ai-unavailable). It must not turn a judgement whose card, gates and
                // live re-check already ran into status model_error with no verdict: on
                // 2026-11-10 that happened 54 times.
                v.Error = "AI model list unavailable: " + ex.Message;
                log.Add("ai: skipped (" + v.Error + ")");
                JudgeLog.Write("WARN", "JUDGE", "AI model list unavailable for alert " + (card != null ? card.AlertId.ToString() : "?") + ": " + ex.Message);
                return v;
            }
            if (!cfg.AiEnabled || string.IsNullOrWhiteSpace(p.ApiKey) || string.IsNullOrWhiteSpace(p.Url))
            {
                // 1.0.187.0 -- name the vendor that was selected and why there is no key for it.
                // "ai disabled or not configured" alone sent us looking in the wrong place once.
                // 1.0.193.0 -- the key now comes from the catalogue row, so name the ROW and the
                // table. Pointing at an appSetting that is no longer read is how a 1.0.187.0-style
                // "looked in the wrong place" hour happens.
                v.Error = !cfg.AiEnabled
                    ? "ai disabled (Ai.Enabled = false)"
                    : "no API key on the AiModelConfig row for '"
                      + (p.Row != null ? p.Row.EngineCode : p.Name)
                      + "' -- set the Key column on that row (provider " + p.Name + ")";
                log.Add("ai: skipped (" + v.Error + ")");
                return v;
            }

            string model = ResolveEngine(engine, p);
            bool withPrior = prior != null && prior.Rows.Count > 0;
            string system = BuildSystemPrompt(live != null, withPrior);
            JudgeAiInputSummary summary;
            string user = BuildInputPack(card, def, v0, ev, live, prior, out summary);
            summary.Model = model;
            summary.Provider = p.Name ?? "";     // 1.0.200.0 -- surfaced as meta.resolvedProvider
            summary.SystemChars = system.Length;
            summary.UserChars = user.Length;
            v.Input = summary;
            // 1.0.187.0 -- the VENDOR, not just the model. meta.aiModel already carried the model
            // string, but nothing said which provider it was sent to, so "are both endpoints on the
            // same vendor?" could not be answered from a log.
            // 1.0.193.0 -- the line now names the catalogue ROW that was chosen and WHY, including
            // the case this release deliberately introduces: the caller asked for one engine and
            // the table's default overruled it. Web.config's AiProvider key is no longer consulted
            // and is no longer quoted here, so the log cannot send anyone back to it.
            log.Add("ai: provider=" + p.Name + " model=" + model
                + " [" + (p.Row != null ? p.Row.EngineCode + " / " + p.Row.DisplayName : "(no row)") + "]"
                + " (asked '" + (engine ?? "(none)") + "'"
                + (cfg.AiProvider != null && cfg.AiProvider.Trim().Length > 0
                    ? ", pinned by Ai.Provider='" + cfg.AiProvider.Trim() + "'"
                    : ", from AiModelConfig IsDefault -- a service caller's provider does not override it")
                + ")");
            log.Add("ai: sending " + summary.Sections.Count + " sections, " + summary.SeriesSent + " series / " + summary.SamplesSent + " samples, " + summary.UserChars + " chars to " + model);

            // =====================================================================================
            // 1.0.189.0 -- THE DEEPSEEK REQUEST IS NOW THE ONE Domain\DeepSeekClient.cs SENDS.
            //
            // 1.0.188.0 got the Judge onto DeepSeek and it answered 200 OK with NOTHING in
            // choices[0].message.content, which HttpCall turned into "". Cause, from your own code:
            //
            //   Domain\DeepSeekClient.cs  "v1.0.135.0: DeepSeek V4 models run THINKING MODE ON BY
            //   DEFAULT (per API docs the request-body key {"thinking":{"type":"disabled"}} turns it
            //   off). Thinking adds 30-80s of hidden reasoning per call and bills at the output
            //   rate -- unusable for a live verdict. Default OFF here."
            //
            //   AnalysisEngine changelog 1.0.135.0  "that, not the model choice, made QUANTUM take
            //   43-92s. DeepSeekClient now always sends {"thinking":{"type":"disabled"}} unless
            //   DeepSeekThinking=true."
            //
            // JudgeAi is the ONLY caller in this assembly that does not go through DeepSeekClient,
            // so it is the only one that never learned this. It sent no "thinking" key (server
            // default = ON) and max_tokens = Ai.MaxTokens = 1500. On a thinking model max_tokens
            // caps reasoning AND output together, so the budget was spent on hidden reasoning and
            // the answer was truncated to zero characters before it began -- finish_reason
            // "length", content "". ~16s of the 21s judgement was the model reasoning into a void.
            //
            // Two keys now match AnalysisEngine.CreateDeepSeekClient exactly:
            //   DeepSeekThinking  (default false) -> {"thinking":{"type":"disabled"}}
            //   DeepSeekMaxTokens (default 8096)  -> max_tokens, for the DeepSeek path only
            // Ai.MaxTokens still governs Anthropic and OpenAI, which have no thinking key and for
            // which 1500 has always been enough.
            //
            // NOT raising the timeout instead: with thinking ON a call takes 30-80s against
            // Ai.TimeoutSec = 60, so a bigger budget would trade empty replies for timeouts.
            // Disabling thinking is the fix; the bigger budget is only so the verdict JSON fits.
            // =====================================================================================
            JObject body = new JObject();
            body["model"] = model;
            body["max_tokens"] = cfg.AiMaxTokens;

            // 1.0.200.0 -- temperature is NOT sent to Anthropic. The Claude 5 family rejects any
            // non-default sampling parameter outright:
            //   400 invalid_request_error "`temperature` is deprecated for this model."
            // Because this key was set BEFORE the vendor branch below, it went to every vendor, so
            // the moment a nexus* row became the AiModelConfig default the AI stopped running for
            // every alert -- 6 of 6 Anthropic resolutions failed on 03.11.2026 while the one
            // DeepSeek resolution succeeded. DeepSeek and OpenAI accept the key; Anthropic does not.
            //
            // AnthropicClient learned this at v1.0.160.34 and documents it in its own header. JudgeAi
            // builds its request body itself instead of going through that client, so it never
            // received the fix -- the same bypass that cost it the DeepSeek "thinking" default,
            // noted in the comment above. Verdict consistency comes from the computed sustain/breach
            // result (computed-verdict-wins), not from sampling controls.
            if (p.Name != "anthropic")
            {
                body["temperature"] = 0.0;
            }

            if (p.Name == "deepseek")
            {
                // 1.0.193.0 -- the catalogue row's Thinking / MaxTokens columns win when the table
                // carries them; both fall back to DeepSeekThinking / DeepSeekMaxTokens exactly as
                // before while those columns are absent, so behaviour is unchanged until the
                // ALTER TABLE is run. The log line names which source actually applied.
                bool think = p.Row != null ? p.Row.ThinkingOn() : ReadCfgBool("DeepSeekThinking", false);
                JObject thinkObj = new JObject();
                thinkObj["type"] = think ? "enabled" : "disabled";
                body["thinking"] = thinkObj;

                int dsMax = p.Row != null ? p.Row.TokenCeiling(8096) : ReadCfgInt("DeepSeekMaxTokens", 8096);
                if (dsMax > 0) { body["max_tokens"] = dsMax; }

                bool fromTable = p.Row != null && p.Row.Thinking.HasValue;
                bool maxFromTable = p.Row != null && p.Row.MaxTokens.HasValue;
                log.Add("ai: deepseek thinking=" + (think ? "enabled" : "disabled")
                    + " (" + (fromTable ? "AiModelConfig.Thinking" : "DeepSeekThinking") + "), max_tokens=" + body["max_tokens"]
                    + " (" + (maxFromTable ? "AiModelConfig.MaxTokens" : "DeepSeekMaxTokens")
                    + "; Ai.MaxTokens=" + cfg.AiMaxTokens + " applies to the other vendors)"
                    + (think ? "  <-- WARNING: thinking ON adds 30-80s and may exceed Ai.TimeoutSec=" + cfg.AiTimeoutSec : ""));
            }

            JArray messages = new JArray();
            if (p.AnthropicShape)
            {
                body["system"] = system;
            }
            else
            {
                JObject sm = new JObject();
                sm["role"] = "system";
                sm["content"] = system;
                messages.Add(sm);
            }

            JObject um = new JObject();
            um["role"] = "user";
            um["content"] = user;
            messages.Add(um);
            body["messages"] = messages;
            string reqBody = body.ToString(Formatting.None);

            try
            {
                string text = await Task.Run(() => call(p.Url, p.ApiKey, p.AnthropicShape, reqBody, cfg.AiTimeoutSec), ct).ConfigureAwait(false);

                // 1.0.189.0 -- a 200 OK with no content now says WHY. Before this, HttpCall returned
                // "" for several different situations -- empty content, a null content, no choices
                // array at all -- and every one of them reported the same unhelpful "empty reply"
                // with rawJson null, so the audit kept nothing of the actual response. The cause of
                // the 11.10.2026 failures had to be deduced from elapsed time instead of read off a log.
                if (text != null && text.StartsWith(EmptyMarker, StringComparison.Ordinal))
                {
                    v.Error = "empty reply -- " + text.Substring(EmptyMarker.Length);
                    log.Add("ai: " + v.Error);
                    return v;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    v.Error = "empty reply";
                    log.Add("ai: " + v.Error);
                    return v;
                }

                string content = StripFences(text);
                JToken j = JudgeUtil.Parse(content);
                v.Ran = true;
                v.RawJson = j;
                v.Recommendation = NormRec(S(j, "recommendation"));
                if (live != null)
                {
                    // only asked in 2.0 mode; in clone mode (Live.Enabled=false) the fields are not read
                    v.LogicCorrect = B(j, "logic_correct");
                    v.DataCorrect = B(j, "data_correct");
                }
                v.Verdict = S(j, "verdict");
                v.Headline = S(j, "headline");
                v.LikelyCause = S(j, "likely_cause");
                v.Category = S(j, "category");
                v.RecommendedAction = S(j, "recommended_action");
                v.Caveats = S(j, "caveats");
                int ci;
                v.Confidence = JudgeUtil.TryInt32(JudgeUtil.Prop(j, "confidence"), out ci) ? Math.Max(0, Math.Min(100, ci)) : 0;
                JToken evs = JudgeUtil.Prop(j, "evidence");
                if (JudgeUtil.IsArray(evs))
                {
                    foreach (JToken e in evs)
                    {
                        v.Evidence.Add(JudgeEvidenceItem.Of(S(e, "tag") ?? "computed", S(e, "text") ?? e.ToString(Formatting.None), "ai"));
                    }
                }

                JToken di = JudgeUtil.Prop(j, "degraded_inputs");
                if (JudgeUtil.IsArray(di))
                {
                    v.DegradedInputs = di.Select(x => x.Type == JTokenType.String ? (string)x : "").Where(x => x != "").ToList();
                }

                // Provenance (AiChat .182): known tags only; a [fetched] line must name a fetched attribute.
                v.Evidence.RemoveAll(e => e.Tag != "record" && e.Tag != "fetched" && e.Tag != "computed");
                List<string> fetchedNames = ev.Series.Values.Select(x => x.Attribute).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (live != null)
                {
                    foreach (JudgeConditionResult cr in live.Conditions.Where(c => c.Status == "HELD" || c.Status == "LAPSED"))
                    {
                        if (!fetchedNames.Contains(cr.Condition.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            fetchedNames.Add(cr.Condition.Name);
                        }
                    }
                }

                int downgraded = 0;
                foreach (JudgeEvidenceItem e in v.Evidence.Where(e => e.Tag == "fetched"))
                {
                    if (!fetchedNames.Any(n => e.Text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        e.Tag = "computed";
                        downgraded++;
                    }
                }

                if (downgraded > 0)
                {
                    log.Add("ai: " + downgraded + " [fetched] line(s) named no fetched attribute → [computed]");
                }

                log.Add("ai: " + model + " → " + v.Recommendation + " (" + (live != null ? "logic=" + Yn(v.LogicCorrect) + " data=" + Yn(v.DataCorrect) + " " : "") + "verdict=" + v.Verdict + " conf=" + v.Confidence + " cause='" + v.LikelyCause + "' ev=" + v.Evidence.Count + ")");
                return v;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (WebException wex)
            {
                string detail = "";
                try
                {
                    HttpWebResponse hr = wex.Response as HttpWebResponse;
                    if (hr != null)
                    {
                        using (StreamReader sr = new StreamReader(hr.GetResponseStream()))
                        {
                            detail = (int)hr.StatusCode + ": " + JudgeUtil.Trunc(sr.ReadToEnd(), 300);
                        }
                    }
                }
                catch
                {
                }

                v.Error = detail.Length > 0 ? detail : "WebException: " + wex.Message;
                log.Add("ai: " + v.Error);
                return v;
            }
            catch (Exception ex)
            {
                v.Error = ex.GetType().Name + ": " + ex.Message;
                log.Add("ai: " + v.Error);
                return v;
            }
        }

        private static string Yn(bool? b)
        {
            return b == null ? "?" : (b.Value ? "yes" : "no");
        }

        // -----------------------------------------------------------------------------------------
        // the SYSTEM message
        // -----------------------------------------------------------------------------------------
        /// <summary>
        /// withLive=false AND withPriorDiscards=false reproduces the AlertJudge 1.1.2.0 prompt byte for
        /// byte (Live.Enabled=false, no prior human discard on this asset+cause) -- the parity case.
        /// </summary>
        internal string BuildSystemPrompt(bool withLive, bool withPriorDiscards)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("You are the RDPMS alert judge for Indian Railways signalling (RDSO/SPN/257/2025 Ver 2.0).");
            string priorSection = withPriorDiscards ? ", priorDiscards" : "";
            sb.AppendLine(withLive
                ? "You will receive ONE JSON object: question, case, gates, liveCheck" + priorSection + ", attrsAtSet, then alertCard, thresholds, telemetry, causeCatalog, decisionReference."
                : "You will receive ONE JSON object: question, case, gates" + priorSection + ", attrsAtSet, then alertCard, thresholds, telemetry, causeCatalog, decisionReference.");
            sb.AppendLine("Rules of engagement:");
            sb.AppendLine("- P0: the verdict is BINARY — CONFIRMED or NOT_CONFIRMED, never INCONCLUSIVE. The card is the CLAIM; telemetry decides. Missing or inconclusive telemetry never CONFIRMS, and NOT_CONFIRMED means SUPPRESS.");
            sb.AppendLine("- Read `question` first: it states which of the two cases this is and exactly what you must decide.");
            sb.AppendLine("  CASE SET_ONLY : the alert is still standing. Judge the state IN EFFECT at the set time and what happened since.");
            sb.AppendLine("  CASE SET_RESET: the alert has cleared. The cause must have held continuously for `case.sustainSec` from the set time. Cleared earlier = transient = SUPPRESS.");
            sb.AppendLine("- `gates` are deterministic and already PASSED (card matched catalog; DataLogger relay history; RDPMS analog history for PREDICTIVE; adjacent tracks for track failures). You are the final judgement on top of them.");
            if (withLive)
            {
                sb.AppendLine("- `liveCheck` is every condition printed on the card re-checked against LIVE historian data over the check window: the value in effect at the set time, the window min/max, whether it HELD or LAPSED and when. The card's own measured numbers may have been captured mid-transition; liveCheck is what really happened.");
            }

            if (withPriorDiscards)
            {
                sb.AppendLine("- `priorDiscards` lists the times a HUMAN reviewer discarded this same cause on this same asset, with the remark they wrote. Each row's `reviewerSaysAlertWasValid` is their own answer to \"was the alert valid?\": false rows never reach you (those are suppressed before this point), so every row you see is a discard made for some OTHER reason -- a planned outage, maintenance in progress, a duplicate already in hand. Treat them as what a knowledgeable colleague noticed on this equipment before: weigh the remark, say in your evidence whether it explains what you are seeing now, and do NOT let a discard alone decide. The telemetry still decides.");
            }

            sb.AppendLine("- `attrsAtSet` gives, per attribute, the value in effect at the set time (`valueAtSet`, carried forward — `lastChangedBeforeSet` says how old it is), how many seconds it stayed true (`stayedTrueForSec`) and how many times it changed inside the frame.");
            sb.AppendLine("- An `attrsAtSet` entry carrying `supersededBy` is NOT the governing result: it is the strict sustain-from-set reading, and for a FAILURE the onset rule replaces it. Use the `liveCheck` entry for that attribute instead, and do not cite the superseded status as a reason to suppress.");
            if (withLive)
            {
                sb.AppendLine("- Every evidence item MUST carry a tag: record (from alertCard), fetched (from telemetry/thresholds/liveCheck), computed (derived by you).");
                // =================================================================================
                // 1.0.190.0 -- THE SENTENCE THAT DELETED THREE TRUE ALERTS.
                //
                // It used to end: "...with nothing that explains it away (train movement,
                // instrument artefact, A SPIKE THAT RECOVERED)?"
                //
                // Every closed alert recovered -- recovery is what a reset IS; one that had not
                // recovered would still be open. So that clause made EVERY reset alert suppressible,
                // and on 11.10.2026 the model took it: 1137362, 1034216 and 1023426 all returned
                // data_correct=false with "it recovered before reset" as the reason, after every
                // computed check had passed. Two of those three reasons were also arithmetically
                // false, because the model read the wrong half of an OR threshold.
                //
                // data_correct is now scoped to the thing the catalogue actually specifies -- the
                // sustain window -- and the model is told plainly that recovery afterwards is
                // expected. "train movement" is gone too: that judgement belongs to the catalogue's
                // prior-cause exclusion (#10 IBalst, #11 RRail), not to an eyeball on a series.
                // =================================================================================
                sb.AppendLine("- Answer TWO questions explicitly: logic_correct — is the cause logic on the card the right logic for this cause (same attributes, same directions, thresholds consistent with causeCatalog)? data_correct — over the SUSTAIN WINDOW ONLY (liveCheck.windowFrom to liveCheck.windowTo), does the telemetry really show every condition the card claims, judged at the effective threshold liveCheck gives? Both true -> recommendation PASS. Either false -> SUPPRESS.");
                sb.AppendLine("- data_correct is about the sustain window and nothing else. What a value did AFTER that window is not part of the rule the alert fired on.");
                sb.AppendLine("- A reset alert recovered by definition — that is what a reset means. `resetCheck: CONFIRMED_RECOVERED` is the alert service recording a measured excursion AND its recovery, and it CORROBORATES the alert. Never answer data_correct=false because the value returned to normal before the reset, or because the condition did not persist for the alert's whole life: no rule requires either.");
                sb.AppendLine("- `gates.resetBeforeSustain` is the ONLY test for \"cleared too early\": true = the alert reset before the sustain window completed (a genuine transient); false = it did not. Use that field; never judge \"early\" from the reset time yourself.");
                sb.AppendLine("- `gates.causeHeldForSec` and each condition's `stayedTrueForSec` say how long the condition stayed true from the set instant. At or above SustainSec satisfies the rule, and a LARGER number is stronger evidence, never weaker.");
                sb.AppendLine("- Each liveCheck condition carries `effectiveThreshold` and `effectiveDirection`: ONE number and ONE direction, already collapsed from the card's clauses (MAX of them for a low rule, MIN for a high one). Judge every reading against THAT number. Do not pick a different threshold out of the card's list — a reading below the effective threshold is still breaching even when it sits above some lower clause.");
                sb.AppendLine("- Your final field is recommendation: PASS (deliver the alert) or SUPPRESS (hold it). Follow decisionReference exactly.");
                sb.AppendLine("- Reply with ONE JSON object and nothing else:");
                sb.AppendLine("{\"recommendation\":\"PASS|SUPPRESS\",\"logic_correct\":true|false,\"data_correct\":true|false,\"verdict\":\"CONFIRMED|NOT_CONFIRMED\",\"confidence\":0-100,\"headline\":\"...\",\"likely_cause\":\"...\",\"category\":\"Genuine failure|Transient|False alarm\",\"evidence\":[{\"tag\":\"record|fetched|computed\",\"text\":\"...\"}],\"recommended_action\":\"...\",\"caveats\":\"...\",\"degraded_inputs\":[]}");
            }
            else
            {
                sb.AppendLine("- Every evidence item MUST carry a tag: record (from alertCard), fetched (from telemetry/thresholds), computed (derived by you).");
                sb.AppendLine("- Your final field is recommendation: PASS (deliver the alert) or SUPPRESS (hold it). Your answer IS the decision. Follow decisionReference exactly.");
                sb.AppendLine("- Reply with ONE JSON object and nothing else:");
                sb.AppendLine("{\"recommendation\":\"PASS|SUPPRESS\",\"verdict\":\"CONFIRMED|NOT_CONFIRMED\",\"confidence\":0-100,\"headline\":\"...\",\"likely_cause\":\"...\",\"category\":\"Genuine failure|Transient|False alarm\",\"evidence\":[{\"tag\":\"record|fetched|computed\",\"text\":\"...\"}],\"recommended_action\":\"...\",\"caveats\":\"...\",\"degraded_inputs\":[]}");
            }

            sb.AppendLine();
            string reference = LoadText(cfg.DecisionReferencePath);
            if (reference != null)
            {
                sb.AppendLine("DECISION REFERENCE:");
                sb.AppendLine(reference);
            }

            return sb.ToString();
        }

        // -----------------------------------------------------------------------------------------
        // the USER message, one JSON object
        // -----------------------------------------------------------------------------------------
        internal string BuildInputPack(JudgeCard card, JudgeCauseDef def, JudgeVerifierOutcome v, JudgeEvidenceBundle ev, JudgeLiveResult live, JudgePriorDiscardResult prior, out JudgeAiInputSummary summary)
        {
            JudgeAiInputSummary sum = new JudgeAiInputSummary();
            summary = sum;

            JArray thresholds = new JArray();
            foreach (JudgeAttrResult a in v.Attrs)
            {
                JudgeTagSeries s;
                ev.Series.TryGetValue("attid:" + a.Attid, out s);
                double bavg;
                bool hasAvg = ev.BaselineAvg.TryGetValue(a.Attid, out bavg);
                JObject t = new JObject();
                t["attr"] = a.Name;
                t["attid"] = a.Attid;
                t["class"] = a.Class;
                t["gate"] = a.Gate;
                t["threshold"] = JudgeUtil.NumOrNull(a.Threshold);
                t["formula"] = a.ThresholdFormula;
                t["baselineAvg"] = hasAvg ? new JValue(bavg) : JValue.CreateNull();
                t["windowMin"] = JudgeUtil.NumOrNull(s != null ? s.Min : null);
                t["windowMax"] = JudgeUtil.NumOrNull(s != null ? s.Max : null);
                t["windowAvg"] = JudgeUtil.NumOrNull(s != null ? s.Avg : null);
                t["windowFirst"] = JudgeUtil.NumOrNull(s != null ? s.First : null);
                t["windowLast"] = JudgeUtil.NumOrNull(s != null ? s.Last : null);
                double? delta = null;
                if (s != null && s.Avg != null && hasAvg && bavg != 0)
                {
                    delta = Math.Round((s.Avg.Value - bavg) / Math.Abs(bavg) * 100, 1);
                }

                t["deltaVsBaselinePct"] = JudgeUtil.NumOrNull(delta);
                t["trend"] = s != null ? Trend(s.Samples, cfg.TrendBuckets) : null;
                t["verifierStatus"] = a.Status;
                t["verifierDetail"] = a.Detail;
                thresholds.Add(t);
            }

            JArray telemetry = new JArray();
            HashSet<long> seenTags = new HashSet<long>();
            foreach (JudgeTagSeries s in ev.Series.Values)
            {
                if (!seenTags.Add(s.TagId))
                {
                    continue;
                }

                List<JudgeSample> ds = Downsample(s.Samples, cfg.MaxSamplesPerAttr);
                sum.SeriesSent++;
                sum.SamplesSent += ds.Count;
                JObject t = new JObject();
                t["attr"] = s.Attribute;
                t["attid"] = s.Attid;
                t["tagId"] = s.TagId;
                t["roleType"] = s.RoleType.ToString();
                t["totalSamples"] = s.Samples.Count;
                t["sent"] = ds.Count;
                JArray samples = new JArray();
                foreach (JudgeSample x in ds)
                {
                    samples.Add(new JArray(JudgeUtil.T(x.Ts, "HH:mm:ss.fff"), Math.Round(x.Value, 4)));
                }

                t["samples"] = samples;
                telemetry.Add(t);
            }

            JObject catalog = null;
            if (def != null)
            {
                catalog = new JObject();
                catalog["seq"] = def.Seq;
                catalog["cause"] = def.Cause;
                catalog["alertType"] = def.AlertType;
                catalog["ldPercent"] = JudgeUtil.NumOrNull(def.LdPercent);
                catalog["lowerRule"] = def.LowerRule;
                catalog["sustainSec"] = def.SustainSec;
                catalog["derivedFormula"] = def.DerivedFormula;
                catalog["source"] = def.Source;
                catalog["ruleRaw"] = def.RuleRaw;
                catalog["verdictLogic"] = def.VerdictLogic;
                JArray attrs = new JArray();
                foreach (JudgeCauseAttr a in def.Attrs)
                {
                    JObject o = new JObject();
                    o["name"] = a.Name;
                    o["attid"] = a.Attid;
                    o["datatype"] = a.Datatype;
                    o["class"] = a.Class;
                    o["state"] = a.State;
                    o["cmp"] = a.Cmp;
                    o["thresholdRef"] = a.ThresholdRef;
                    o["effectiveThreshold"] = a.EffectiveThreshold;
                    o["gate"] = a.Gate;
                    attrs.Add(o);
                }

                catalog["attrs"] = attrs;
            }

            string wisdom = LoadWisdom(def != null ? def.Cause : card.CauseCode);

            string question;
            if (live != null)
            {
                question = v.AlertState == "SET_ONLY"
                    ? "The alert is STILL SET (no reset). Judge the state IN EFFECT at the set time " + JudgeUtil.T(v.SetTime, "HH:mm:ss") + " and what has happened since, up to " + JudgeUtil.T(ev.To, "HH:mm:ss") + ". Does the telemetry support this cause right now? Is the card's logic the right logic for this cause (logic_correct), and does the live data really show every condition set (data_correct)? Answer PASS only if both are true, else SUPPRESS."
                    : "The alert SET at " + JudgeUtil.T(v.SetTime, "HH:mm:ss") + " and RESET at " + JudgeUtil.T(v.ResetTime, "HH:mm:ss") + " (" + JudgeUtil.F((v.ResetTime.Value - v.SetTime).TotalSeconds, "0") + "s). The cause had to HOLD for SustainSec=" + v.SustainSec + "s from the set time -- that is the whole of the timing rule. It is NOT required to hold until the reset. Is the card's logic the right logic for this cause (logic_correct), and over the sustain window does the telemetry show every condition the card claims, at liveCheck's effective threshold (data_correct)? Answer PASS if both are true. SUPPRESS only if a condition LAPSED inside the sustain window, or gates.resetBeforeSustain is true, or the telemetry contradicts the card at the set instant. The value recovering after the window is expected and is not a reason to suppress.";
            }
            else
            {
                question = v.AlertState == "SET_ONLY"
                    ? "The alert is STILL SET (no reset). Judge the state IN EFFECT at the set time " + JudgeUtil.T(v.SetTime, "HH:mm:ss") + " and what has happened since, up to " + JudgeUtil.T(ev.To, "HH:mm:ss") + ". Does the telemetry support this cause right now? Answer PASS or SUPPRESS."
                    : "The alert SET at " + JudgeUtil.T(v.SetTime, "HH:mm:ss") + " and RESET at " + JudgeUtil.T(v.ResetTime, "HH:mm:ss") + " (" + JudgeUtil.F((v.ResetTime.Value - v.SetTime).TotalSeconds, "0") + "s). The cause had to HOLD for SustainSec=" + v.SustainSec + "s from the set time -- that is the whole of the timing rule, and it is NOT required to hold until the reset. Answer PASS if it held for that period and the telemetry supports the cause. SUPPRESS only if it LAPSED inside the sustain window, or gates.resetBeforeSustain is true, or the telemetry contradicts the card. The value recovering after the window is expected and is not a reason to suppress.";
            }

            JObject gates = new JObject();
            gates["cardMatchesCatalog"] = v.CardMatch != null ? new JValue(v.CardMatch.Match) : JValue.CreateNull();
            gates["relay"] = GateJson(v.Relay);
            gates["analog"] = GateJson(v.Analog);
            JObject adj = new JObject();
            adj["result"] = v.Adjacency.Result;
            adj["detail"] = v.Adjacency.Detail;
            adj["findings"] = new JArray(v.Adjacency.Findings);
            gates["adjacency"] = adj;
            gates["resetBeforeSustain"] = v.ResetBeforeSustain;
            gates["causeHeldForSec"] = JudgeUtil.NumOrNull(v.HeldForSec);
            gates["reclassifiedCause"] = v.ReclassifiedCause;
            gates["notEvaluated"] = new JArray(v.CoverageNotes);

            // =====================================================================================
            // 1.0.191.0 -- MARK THE SUPERSEDED SUSTAIN-FROM-SET RESULTS.
            //
            // attrsAtSet evaluates every catalogue attribute as "true continuously FROM the set
            // instant". For a FAILURE with Live.FailureOnset=true (1.0.186.0) that is NOT the
            // governing rule: checkWindow is a window to FIND the condition in, so a condition that
            // arrives a few seconds in and holds to the window end counts as held.
            //
            // The pack carried both results for the same attribute, with no precedence stated:
            //
            //   attrsAtSet   ITC Relay End < 100  status LAPSED, stayedTrueForSec 0, valueAtSet 108.801
            //   liveCheck    ITC RELAY END(mA)    status HELD, onset 08:38:35, 5s into a 10s window
            //
            // On 1263751 the AI quoted the LAPSED one -- "attrsAtSet for ITC Relay End < 100 shows
            // status LAPSED, stayedTrueForSec=0" -- and suppressed on it. It was not careless:
            // nothing told it which of the two governs. Same failure as handing it an OR threshold
            // and letting it choose a clause.
            //
            // Any attid the live re-check resolved by onset now carries supersededBy on its
            // attrsAtSet entry, and the prompt says such an entry is not the governing result.
            // =====================================================================================
            HashSet<int> onsetAttids = new HashSet<int>();
            if (live != null && live.Conditions != null)
            {
                foreach (JudgeConditionResult ocr in live.Conditions)
                {
                    if (ocr != null && ocr.OnsetUsed && ocr.Attid > 0)
                    {
                        onsetAttids.Add(ocr.Attid);
                    }
                }
            }

            JArray attrsAtSet = new JArray();
            foreach (JudgeAttrResult a in v.Attrs)
            {
                JObject o = new JObject();
                o["attr"] = a.Name;
                o["attid"] = a.Attid;
                o["class"] = a.Class;
                o["requirement"] = a.Detail != null ? a.Detail.Split(':').FirstOrDefault() : null;
                o["status"] = a.Status;
                o["valueAtSet"] = JudgeUtil.NumOrNull(a.StateAtSet);
                o["valueAtSetFrom"] = a.StateSource;
                o["lastChangedBeforeSet"] = JudgeUtil.IsoOrNull(a.StateAtSetTs);
                o["threshold"] = JudgeUtil.NumOrNull(a.Threshold);
                o["formula"] = a.ThresholdFormula;
                o["stayedTrueForSec"] = JudgeUtil.NumOrNull(a.HeldForSec);
                o["transitionsInFrame"] = a.TransitionsInFrame;
                o["detail"] = a.Detail;

                // 1.0.191.0 -- this attribute was re-checked by onset in liveCheck, so a LAPSED here
                // is the sustain-from-set reading and is NOT what governs.
                if (onsetAttids.Contains(a.Attid)
                    && string.Equals(a.Status, "LAPSED", StringComparison.OrdinalIgnoreCase))
                {
                    o["supersededBy"] = "liveCheck (Live.FailureOnset: for a FAILURE the condition may"
                        + " arrive inside checkWindow). This sustain-from-set result does NOT govern;"
                        + " the liveCheck entry for the same attribute does.";
                }

                attrsAtSet.Add(o);
            }

            JObject caseObj = new JObject();
            caseObj["alertState"] = v.AlertState;
            caseObj["setTime"] = JudgeUtil.Iso(v.SetTime);
            caseObj["resetTime"] = JudgeUtil.IsoOrNull(v.ResetTime);
            caseObj["sustainSec"] = v.SustainSec;
            caseObj["evalMode"] = v.EvalMode;
            caseObj["frameFrom"] = JudgeUtil.Iso(ev.From);
            caseObj["frameTo"] = JudgeUtil.Iso(ev.To);
            caseObj["alertType"] = v.AlertType;
            caseObj["cause"] = v.CauseCode;
            caseObj["seq"] = v.Seq;

            JObject telemetryObj = new JObject();
            telemetryObj["frameFrom"] = JudgeUtil.Iso(ev.From);
            telemetryObj["frameTo"] = JudgeUtil.Iso(ev.To);
            telemetryObj["note"] = "first sample = value in effect at set (carried forward; its time = last change), then transitions inside the frame";
            telemetryObj["degraded"] = ev.Degraded;
            telemetryObj["degradedInputs"] = new JArray(ev.DegradedInputs);
            telemetryObj["series"] = telemetry;

            JObject reference = new JObject();
            reference["causeWisdom"] = wisdom;
            reference["note"] = "general rules are in the system message";

            JObject pack = new JObject();
            pack["question"] = question;
            pack["case"] = caseObj;
            pack["gates"] = gates;
            if (live != null)
            {
                pack["liveCheck"] = JudgeEnvelope.LiveJson(live, true);
            }

            if (prior != null && prior.Rows.Count > 0)
            {
                pack["priorDiscards"] = JudgePriorDiscards.Json(prior);
            }

            pack["attrsAtSet"] = attrsAtSet;
            pack["alertCard"] = card.Raw;
            pack["thresholds"] = thresholds;
            pack["telemetry"] = telemetryObj;
            pack["causeCatalog"] = catalog;
            pack["decisionReference"] = reference;
            List<string> sections = new List<string> { "question", "case", "gates" };
            if (live != null) { sections.Add("liveCheck"); }
            if (prior != null && prior.Rows.Count > 0) { sections.Add("priorDiscards"); }
            sections.AddRange(new[] { "attrsAtSet", "alertCard", "thresholds", "telemetry", "causeCatalog", "decisionReference" });
            sum.Sections.AddRange(sections);
            // the 1.1.2.0 serializer wrote no null properties and whole doubles without ".0"; alertCard is verbatim
            foreach (JProperty prop in pack.Properties().ToList())
            {
                if (prop.Name != "alertCard")
                {
                    JudgeUtil.Tidy(prop.Value);
                }
            }

            return pack.ToString(Formatting.None);
        }

        private static JObject GateJson(JudgeGate g)
        {
            JObject o = new JObject();
            o["result"] = g.Result;
            o["detail"] = g.Detail;
            return o;
        }

        internal static JObject Trend(List<JudgeSample> samples, int buckets)
        {
            JObject o = new JObject();
            if (samples.Count == 0)
            {
                o["direction"] = "none";
                return o;
            }

            buckets = Math.Max(2, Math.Min(buckets, samples.Count));
            int per = (int)Math.Ceiling(samples.Count / (double)buckets);
            List<double> means = new List<double>();
            for (int i = 0; i < samples.Count; i += per)
            {
                means.Add(Math.Round(samples.Skip(i).Take(per).Average(x => x.Value), 4));
            }

            double first = means[0];
            double last = means[means.Count - 1];
            double span = Math.Abs(last - first);
            string dir = span < 1e-9 ? "flat" : (last > first ? "rising" : "falling");
            o["direction"] = dir;
            o["bucketMeans"] = new JArray(means);
            o["changeFirstToLast"] = Math.Round(last - first, 4);
            return o;
        }

        internal static List<JudgeSample> Downsample(List<JudgeSample> s, int max)
        {
            if (s.Count <= max)
            {
                return s;
            }

            List<JudgeSample> outp = new List<JudgeSample>(max);
            double step = (s.Count - 1) / (double)(max - 1);
            for (int i = 0; i < max; i++)
            {
                outp.Add(s[(int)Math.Round(i * step)]);
            }

            return outp;
        }

        // 1.0.187.0 -- the FLASH decision moved into ReadProvider, where the vendor is known and the
        // model string is built from the right appSetting. This is now only a passthrough, kept so
        // the shape of the pipeline reads the same and so any other caller still compiles.
        internal static string ResolveEngine(string alias, Provider p)
        {
            return p.Model;
        }

        private string LoadText(string relPath)
        {
            try
            {
                string path = cfg.ResolvePath(relPath);
                if (path != null && File.Exists(path))
                {
                    return File.ReadAllText(path);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private string LoadWisdom(string causeCode)
        {
            try
            {
                string dir = cfg.ResolvePath(cfg.WisdomDir);
                if (dir == null || !Directory.Exists(dir))
                {
                    return null;
                }

                StringBuilder sb = new StringBuilder();
                foreach (string f in new[] { "common.md", SafeFile(causeCode ?? "") + ".md" })
                {
                    string p = Path.Combine(dir, f);
                    if (File.Exists(p))
                    {
                        sb.AppendLine(File.ReadAllText(p));
                    }
                }

                return sb.Length == 0 ? null : sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string SafeFile(string s)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            return new string(s.Select(ch => bad.Contains(ch) ? '_' : ch).ToArray());
        }

        private static string NormRec(string r)
        {
            if (r == null)
            {
                return null;
            }

            string u = r.Trim().ToUpperInvariant();
            if (u.StartsWith("SUPPRESS"))
            {
                return "SUPPRESS";
            }

            if (u.StartsWith("PASS"))
            {
                return "PASS";
            }

            return u;
        }

        private static string S(JToken e, string k)
        {
            JToken v = JudgeUtil.Prop(e, k);
            if (v != null && v.Type == JTokenType.String)
            {
                return (string)v;
            }

            return null;
        }

        private static bool? B(JToken e, string k)
        {
            JToken v = JudgeUtil.Prop(e, k);
            if (v == null)
            {
                return null;
            }

            if (v.Type == JTokenType.Boolean)
            {
                return v.Value<bool>();
            }

            if (v.Type == JTokenType.String)
            {
                string s = ((string)v).Trim().ToLowerInvariant();
                if (s == "true" || s == "yes")
                {
                    return true;
                }

                if (s == "false" || s == "no")
                {
                    return false;
                }
            }

            return null;
        }

        private static string StripFences(string s)
        {
            s = (s ?? "").Trim();
            if (s.StartsWith("```"))
            {
                int i = s.IndexOf('\n');
                s = i > 0 ? s.Substring(i + 1) : s;
                if (s.EndsWith("```"))
                {
                    s = s.Substring(0, s.Length - 3);
                }
            }

            return s.Trim();
        }

        // -----------------------------------------------------------------------------------------
        // HTTP (same shape as RosterEngine.RosterModelCall, plus the configurable timeout)
        // -----------------------------------------------------------------------------------------
        internal static string HttpCall(string url, string apiKey, bool anthropicShape, string reqBody, int timeoutSec)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Accept = "application/json";
            req.Timeout = Math.Max(10, timeoutSec) * 1000;
            req.ReadWriteTimeout = req.Timeout;
            if (anthropicShape)
            {
                req.Headers["x-api-key"] = apiKey;
                req.Headers["anthropic-version"] = "2023-06-01";
            }
            else
            {
                req.Headers["Authorization"] = "Bearer " + apiKey;
            }

            byte[] buf = Encoding.UTF8.GetBytes(reqBody);
            req.ContentLength = buf.Length;
            using (Stream rs = req.GetRequestStream())
            {
                rs.Write(buf, 0, buf.Length);
            }

            string respBody;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
            {
                respBody = sr.ReadToEnd();
            }

            JObject root = JObject.Parse(respBody);
            if (anthropicShape)
            {
                JArray content = root["content"] as JArray;
                StringBuilder sb = new StringBuilder();
                if (content != null)
                {
                    foreach (JToken blk in content)
                    {
                        JObject o = blk as JObject;
                        if (o != null && (o["type"] == null || o["type"].ToString() == "text") && o["text"] != null)
                        {
                            sb.Append(o["text"].ToString());
                        }
                    }
                }

                // 1.0.189.0 -- same treatment for the Anthropic shape: if no text block came back,
                // name the stop_reason instead of returning "".
                if (sb.Length > 0)
                {
                    return sb.ToString();
                }

                string stop = root["stop_reason"] != null ? root["stop_reason"].ToString() : "(none)";
                int blocks = content != null ? content.Count : 0;
                return EmptyMarker + "stop_reason=" + stop + ", content blocks=" + blocks
                    + ", no text block"
                    + (stop == "max_tokens" ? " -- max_tokens was reached before any text" : "");
            }

            JArray choices = root["choices"] as JArray;
            if (choices != null && choices.Count > 0)
            {
                JObject msg = choices[0]["message"] as JObject;
                if (msg != null && msg["content"] != null)
                {
                    string got = msg["content"].ToString();
                    if (got.Length > 0)
                    {
                        return got;
                    }
                }

                // ---------------------------------------------------------------------------------
                // 1.0.189.0 -- 200 OK, a choice, and no content. Say what the response actually
                // contained rather than returning "" and leaving the caller to guess.
                //
                // The diagnosis this is built for: on a thinking-enabled DeepSeek model max_tokens
                // caps reasoning AND output together, so the budget is spent before the answer
                // starts and the server returns finish_reason "length" with content "" and the
                // reasoning in reasoning_content. That is one line in a log and was an afternoon
                // of deduction without it.
                // ---------------------------------------------------------------------------------
                string finish = choices[0]["finish_reason"] != null
                    ? choices[0]["finish_reason"].ToString() : "(none)";

                string reasoning = "";
                if (msg != null && msg["reasoning_content"] != null)
                {
                    reasoning = msg["reasoning_content"].ToString();
                }

                JObject usage = root["usage"] as JObject;
                string tokens = "";
                if (usage != null)
                {
                    foreach (string k in new[] { "completion_tokens", "total_tokens", "reasoning_tokens" })
                    {
                        if (usage[k] != null) { tokens += (tokens.Length > 0 ? ", " : "") + k + "=" + usage[k]; }
                    }
                }

                string why = "finish_reason=" + finish + ", content_chars=0"
                    + (tokens.Length > 0 ? ", " + tokens : "")
                    + (reasoning.Length > 0
                        ? ", reasoning_content present (" + reasoning.Length
                          + " chars) -- the model reasoned but never answered, which on DeepSeek V4 means"
                          + " thinking was enabled and max_tokens was spent on it"
                        : ", no reasoning_content");

                return EmptyMarker + why;
            }

            // 1.0.189.0 -- no choices array at all. A 4xx throws, so this is a 200 of a shape the
            // OpenAI-compatible reader does not know; keep a slice of it, which is the only way to
            // tell this apart from an empty answer.
            return EmptyMarker + "no 'choices' array in a 200 response; body was "
                + JudgeUtil.Trunc(respBody, 300);
        }
    }
}
