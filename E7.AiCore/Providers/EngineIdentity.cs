using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static E7.AiCore.AiSettings;
using static E7.AiCore.AiText;
using static E7.AiCore.AiJson;
using static E7.AiCore.AiTime;
using static E7.AiCore.Redaction;
using static E7.AiCore.ScopeGuard;
using static E7.AiCore.EngineIdentity;
using static E7.AiCore.TrainingText;
using static E7.AiCore.VerdictRules;

namespace E7.AiCore
{
    // Engine alias resolution, opaque provider ids, engine-identity scrubbing, think-flag parsing.
    // Moved verbatim from AiChatController (E7MRIWeb) in 1.0.162.0 -- Shared AI Core step 2a-2c.
    // 1.0.163.0 (2b-ii): EngineName, IsSiteKeepOverrideEmail, MaskModelIdentity added; their web.config values
    // are passed in as parameters by the controller forwarders.
    public static class EngineIdentity
    {

        // v1.0.160.59: PLATFORM-INTERNAL REDACTION. The owner ruled that platform and hardware logs
        // must not reach railway users. Bash can read those same files with no restriction, and the
        // verdict is rendered TO railway users -- so the boundary cannot rest on a prompt instruction,
        // which cannot stop a paraphrase. Every verdict string passes through here.
        // Redacts rather than downgrading (owner decision): a formatting slip must not destroy an
        // otherwise sound verdict.
        // v1.0.160.90: vendor/model identity must never reach a user.
        internal static readonly Regex _modelNameRx = new Regex(
            @"\b(chat\s?gpt|openai|gpt[-\s]?[0-9._]*|o[0-9](?:[-\s]?mini)?|"
          + @"claude(?:[-\s][a-z0-9._]+)*|anthropic|sonnet|opus|haiku|"
          + @"deepseek(?:[-\s][a-z0-9._]+)*|gemini|llama|mistral|qwen|grok)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string EngineName(int owner, string name1, string name2) { return owner == 2 ? name2 : name1; }


        public static bool IsSiteKeepOverrideEmail(string email, string overrideList)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(overrideList)) { return false; }
            string me = email.Trim();
            string[] list = overrideList.Split(',');
            for (int i = 0; i < list.Length; i++)
            {
                if (string.Equals(list[i].Trim(), me, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }


        public static string MaskModelIdentity(string text, string brand)
        {
            if (string.IsNullOrEmpty(text)) { return text; }
            if (!_modelNameRx.IsMatch(text)) { return text; }
            return _modelNameRx.Replace(text, brand);
        }


        // v1.0.160.118: strip vendor and model identifiers from any string bound for a browser.
        internal static readonly Regex _vendorIdRx = new Regex(
            // LONGEST alternatives FIRST: regex alternation is left-biased, so a bare "deepseek"
            // listed before "deepseek-v4-flash" matches the prefix and leaves "-v4-flash" behind.
            @"\b(deepseek-[a-z0-9.\-]+|gpt-[a-z0-9.\-]+|claude-[a-z0-9.\-]+|gemini-[a-z0-9.\-]+|mistral-[a-z0-9.\-]+|llama-[a-z0-9.\-]+|grok-[a-z0-9.\-]+|qwen[a-z0-9.\-]*|anthropic|openai|deepseek|claude|gemini|mistral|llama|grok|gpt)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // v1.0.160.42: absent/blank ctx.think => null (fall back to the DeepSeekThinking config
        // default). Only an explicit true/1/on or false/0/off overrides it.
        public static bool? ParseThinkCtx(string v)
        {
            if (string.IsNullOrEmpty(v)) { return null; }
            string t = v.Trim().ToLowerInvariant();
            if (t == "true" || t == "1" || t == "on" || t == "yes") { return true; }
            if (t == "false" || t == "0" || t == "off" || t == "no") { return false; }
            return null;
        }


        // v1.0.160.58: the ONE predicate for debug/tools access. Owner rule: site-keeping users and
        // the explicit emails in SiteKeepingOverrideEmails get everything; everyone else gets
        // Analyze + Re-run only. SafeIsSiteKeeping already means exactly "role OR listed email", so
        // this is a named alias rather than a second scheme to keep in step.
        // v1.0.160.85: read the portal login while HttpContext.Current is still alive. Must be
        // called at the TOP of an action, before any await.
        // v1.0.160.107 -- F1. Inbound: the client sends opaque engine ids; map to the raw provider
        // BEFORE gating and client construction so Web.config Model* keys, audit rows and logs are
        // unchanged. Old raw ids stay accepted for one release (pages cached before this build).
        public static string ResolveEngineAlias(string requested)
        {
            string p = (requested ?? "").Trim().ToLowerInvariant();
            if (p == "nexus10") { return "anthropic"; }
            if (p == "orion10") { return "openai"; }
            if (p == "quantum10") { return "deepseek"; }
            return p;
        }


        // =========================================================================================
        // 1.0.187.0 -- ONE PLACE THAT ANSWERS "WHICH VENDOR".
        //
        // WHY THIS EXISTS. The brand->vendor map lived in exactly one place,
        // AnalysisEngine.State.cs ProviderKind, and the Judge did not use it. The Judge read only
        // the AiProvider appSetting and ignored the request entirely, so with Web.config carrying
        //
        //     <add key="AiProvider" value="anthropic" />
        //
        // the same request -- provider "QUANTUM" -- went to two different vendors:
        //
        //     AiChat/AnalyzeAlertJson   ProviderKind reads the request   -> deepseek   (worked)
        //     AlertJudge                ReadProvider ignores it          -> anthropic  (400, no credit)
        //
        // That is the same fault AnalysisEngine was bitten by and fixed three times over:
        //   1.0.160.9   bare brand "quantum" fell through to anthropic and ran on the Anthropic key
        //   1.0.160.11  the match had to be Contains, not ==, because the picker sends "ENERGY7 QUANTUM"
        //   1.0.160.201 any caller that sent NO provider ran claude-sonnet-4-6 while the page ran quantum11
        //
        // The map is here, in the shared core, so there is one answer for every caller instead of
        // one per endpoint. Deliberately ADDITIVE: nothing in this file changes, and
        // AnalysisEngine.ProviderKind is NOT touched by this release -- it already has the same map
        // and already works. Pointing it at this method later would remove the duplication, but that
        // is an AnalysisEngine change and belongs to whoever owns that file.
        //
        // KEEP THE ORDER. Numbered variants are matched first so their model overrides survive;
        // bare brands are matched with Contains, not equality, because the picker sends the full
        // label ("ENERGY7 QUANTUM"). Reversing either reintroduces a fixed bug.
        //
        // Returns "deepseek", "openai" or "anthropic" -- never null, never anything else.
        // =========================================================================================
        public static string ResolveProviderKind(string requested, string configDefault)
        {
            string p = (requested ?? "").Trim();
            if (p.Length == 0) { p = (configDefault ?? "").Trim(); }
            p = p.ToLowerInvariant();

            // explicit vendor values win outright
            if (p == "deepseek" || p == "openai" || p == "anthropic") { return p; }

            if (p.Contains("deep")) { return "deepseek"; }

            // numbered variants FIRST, so their model overrides are untouched
            if (p == "orion16" || p == "orion17") { return "openai"; }
            if (p == "nexus16" || p == "nexus19" || p == "nexus49") { return "anthropic"; }

            // bare brands. Contains, not ==, so "ENERGY7 QUANTUM" matches too.
            if (p.Contains("quantum") || p.Contains("tesla 2") || p.Contains("tesla2")) { return "deepseek"; }
            if (p.Contains("orion") || p.Contains("tesla 1") || p.Contains("tesla1")) { return "openai"; }
            if (p.Contains("nexus") || p.Contains("ultra")) { return "anthropic"; }
            if (p.Contains("gpt")) { return "openai"; }

            return "anthropic";
        }

        /// <summary>
        /// 1.0.187.0 -- true when the caller asked for a vendor's FLASH/lite variant
        /// (quantum11, or the word "flash"). Only DeepSeek has a distinct flash model today.
        /// </summary>
        public static bool WantsFlashVariant(string requested)
        {
            string p = (requested ?? "").Trim().ToLowerInvariant();
            return p == "quantum11" || p.Contains("flash");
        }


        // Outbound: the only provider token a browser may see. FAILS CLOSED -- an unknown or crafted
        // value ("gpt", "claude", arbitrary text) becomes "unknown" and is never reflected back.
        public static string OpaqueProviderId(string input)
        {
            string low = (input ?? "").Trim().ToLowerInvariant();
            switch (low)
            {
                case "nexus10":
                case "nexus16":
                case "nexus19":
                case "nexus49":
                case "orion10":
                case "orion16":
                case "orion17":
                case "quantum10":
                case "quantum11":
                    return low;
            }
            switch (low)
            {
                case "anthropic": return "nexus10";
                case "openai": return "orion10";
                case "deepseek": return "quantum10";
            }
            // 1.0.193.0: any EngineCode of the AiModelConfig list is an opaque brand id too
            if (AiModelConfig.IsKnownEngine(low))
            {
                return low;
            }
            return "unknown";
        }


        public static string ScrubEngineIdentity(string v)
        {
            if (string.IsNullOrEmpty(v)) { return v; }
            if (!_vendorIdRx.IsMatch(v)) { return v; }
            return _vendorIdRx.Replace(v, "the engine");
        }
    }
}
