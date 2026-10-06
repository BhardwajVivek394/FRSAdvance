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
    // AnalysisEngine -- Guardrails.
    // Moved verbatim from AiChatController.Guardrails.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {
        // Moved to E7.AiCore.Redaction.LimitForLog in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string LimitForLog(string text, int maxChars) => Redaction.LimitForLog(text, maxChars);

        // Moved to E7.AiCore.Redaction.RedactSecretText in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string RedactSecretText(string text) => Redaction.RedactSecretText(text);

        // Moved to E7.AiCore.Redaction.CloneAndRedact in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static JToken CloneAndRedact(JToken token) => Redaction.CloneAndRedact(token);

        // Moved to E7.AiCore.Redaction.IsSensitiveKey in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static bool IsSensitiveKey(string key) => Redaction.IsSensitiveKey(key);

        // Moved to E7.AiCore.ScopeGuard.IsInstructionExtractionRequest in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static bool IsInstructionExtractionRequest(string text) => ScopeGuard.IsInstructionExtractionRequest(text);

        // Moved to E7.AiCore.ScopeGuard.LastRealUserText in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string LastRealUserText(JArray messages) => ScopeGuard.LastRealUserText(messages);


        // Replace any long verbatim run of the protected corpus that reached user-visible text.
        // Never logs the corpus or the matched fragment.
        internal string ScrubInstructionEcho(string text)
        {
            if (string.IsNullOrEmpty(text)) { return text; }
            try
            {
                string corpus = Sess._protectedCorpus;
                if (string.IsNullOrEmpty(corpus)) { return text; }
                string[] cw = corpus.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (cw.Length < InstructionEchoMinWords) { return text; }
                string result = text;
                for (int i = 0; i + InstructionEchoMinWords <= cw.Length; i++)
                {
                    string run = string.Join(" ", cw, i, InstructionEchoMinWords);
                    if (run.Length < 40) { continue; }
                    int at = result.IndexOf(run, StringComparison.OrdinalIgnoreCase);
                    if (at >= 0)
                    {
                        result = result.Substring(0, at) + "[internal instructions withheld]"
                               + result.Substring(at + run.Length);
                    }
                }
                return result;
            }
            catch { return text; }
        }


        // v1.0.160.108 H1: the ONLY metadata a browser may receive. Raw provider, model id, client
        // library names, fault/toolTrace/diagnostics and byte/telemetry counters stay server-side --
        // they are in the audit row and the log, where ops needs them.
        internal static JObject PublicMeta(JObject meta, bool privileged)
        {
            JObject o = new JObject();
            if (meta == null) { return o; }
            string[] common = new string[] { "componentVersion", "requestId", "mode", "coverageDowngrade", "analysisId" };
            for (int i = 0; i < common.Length; i++)
            {
                if (meta[common[i]] != null) { o[common[i]] = meta[common[i]]; }
            }
            // provider is already opaque at this point; re-run it so a crafted value cannot survive.
            if (meta["provider"] != null) { o["provider"] = OpaqueProviderId(meta["provider"].ToString()); }
            if (meta["durationMs"] != null) { o["elapsed"] = meta["durationMs"]; }
            // boolean ONLY -- the count itself is capacity information.
            o["tokenWarning"] = (meta["tokensIn"] != null && meta["tokensIn"].Type == JTokenType.Integer
                                 && (int)meta["tokensIn"] > _warnTokens);
            if (!privileged) { return o; }
            // Privileged additions are OPERATIONAL only. Still no model id, no provider name, no
            // library name, no fault/toolTrace/diagnostics -- those never reach any browser.
            string[] ops = new string[] { "tokensIn", "tokensOut", "modelMs", "toolMs", "discoveryMs" };
            for (int i = 0; i < ops.Length; i++)
            {
                if (meta[ops[i]] != null) { o[ops[i]] = meta[ops[i]]; }
            }
            if (meta["toolCalls"] != null) { o["toolCount"] = meta["toolCalls"]; }
            o["cacheHit"] = (meta["tokensCacheRead"] != null && meta["tokensCacheRead"].Type == JTokenType.Integer
                             && (int)meta["tokensCacheRead"] > 0);
            return o;
        }


        // What a browser may see of the 7W audit. Static by necessity -- called with no instance.
        internal static JObject RedactAuditForClient(JObject a7, bool privileged)
        {
            if (a7 == null) { return null; }
            try
            {
                JObject c = (JObject)a7.DeepClone();
                if (privileged) { return c; }
                JObject which = c["which"] as JObject;
                if (which != null)
                {
                    if (which["engine"] != null) { which["engine"] = OpaqueProviderId(which["engine"].ToString()); }
                    which.Remove("provider");
                    which.Remove("model");
                }
                JObject where = c["where"] as JObject;
                if (where != null) { where.Remove("providerUrl"); }
                // Field removal handles what is known. This catches what is not: a vendor string added
                // to the audit later, in a field nobody redacts, is stripped before it can leave.
                try
                {
                    string flat = c.ToString(Formatting.None);
                    string clean = ScrubEngineIdentity(flat);
                    if (!string.Equals(flat, clean, StringComparison.Ordinal))
                    {
                        AiLogStatic("WARN", "AUDIT", "a vendor identifier survived field redaction and was scrubbed from the client audit");
                        return JObject.Parse(clean);
                    }
                }
                catch { return null; }
                return c;
            }
            catch { return null; }
        }


        internal void CaptureLoginUser()
        {
            try
            {
                dynamic lu = AiUserContext.Get();   // 3c: login user via AiUserContext (web type not visible to the core)
                if (lu != null)
                {
                    string nm = ((lu.FirstName ?? "") + " " + (lu.LastName ?? "")).Trim();
                    if (nm.Length > 0) { Sess._luName = nm; }
                    if (!string.IsNullOrWhiteSpace(lu.EmailAddress)) { Sess._luEmail = lu.EmailAddress.Trim(); }
                    Sess._luSiteKeep = lu.IsSiteKeeping;
                }
            }
            catch { }
        }


        // v1.0.160.91: INSTANCE, and prefers the captured value. Static + LoginUser cannot work here:
        // by the time the agentic loop filters tools, HttpContext.Current is long gone.
        internal bool AiDebugAllowed()
        {
            if (Sess._luSiteKeep) { return true; }                       // captured before the first await
            if (!string.IsNullOrEmpty(Sess._luEmail)
                && IsSiteKeepOverrideEmail(Sess._luEmail)) { return true; }
            return SafeIsSiteKeeping();                             // pre-await paths still work
        }


        internal static bool SafeIsSiteKeeping()
        {
            try
            {
                dynamic u = AiUserContext.Get();   // 3c: login user via AiUserContext
                if (u == null) { return false; }
                if (u.IsSiteKeeping) { return true; }
                // v1.0.160.4: named test users get site-keeping too (mirrors the view's override).
                // NO AiLog here -- this method is STATIC and AiLog is an instance method (CS0120 in
                // 1.0.160.3). The grant is logged at the call site instead, which has the instance.
                return IsSiteKeepOverrideEmail(u.EmailAddress);
            }
            catch { return false; }   // no session/context -> treat as non-site-keeping (safe default)
        }


        internal bool ScrubHit(string v)
        {
            if (string.IsNullOrEmpty(v)) { return false; }
            for (int i = 0; i < _platformMarks.Length; i++)
            {
                if (v.IndexOf(_platformMarks[i], StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            }
            return v.IndexOf(".log", StringComparison.OrdinalIgnoreCase) >= 0;
        }


        internal string ScrubPlatformInternals(string v, string field)
        {
            // v1.0.160.90: mask vendor/model identity FIRST, on the same path. Placed here so every
            // one of the 12 call sites is covered -- and BEFORE the ScrubHit early return, or a string
            // with a model name but no platform marker would sail straight through.
            v = MaskModelIdentity(v);
            // v1.0.160.107 -- F7 pin 4c: the instruction-echo backstop lives INSIDE this funnel, so
            // every user-visible string (verdict, hi, evidence, caveats, viz, recommended_action)
            // is covered by construction -- a later-added field cannot bypass it. Runs BEFORE the
            // ScrubHit early return, like MaskModelIdentity, for the same reason.
            v = ScrubInstructionEcho(v);
            // v1.0.160.117 CUT C: a rule id in prose is a taxonomy disclosure. Its own pass, because
            // the instruction-echo scrub above matches long verbatim runs, not a 3-token id.
            v = ScrubWisdomIds(v);
            if (!ScrubHit(v)) { return v; }
            AiLog("WARN", "VERDICT", "platform-internal text redacted from " + field
                + " (bash/log content must not reach a railway user)");
            return "[internal diagnostic detail removed]";
        }

        // Moved to E7.AiCore.ScopeGuard.IsOutOfScope in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static bool IsOutOfScope(string text) => ScopeGuard.IsOutOfScope(text);
    }
}
