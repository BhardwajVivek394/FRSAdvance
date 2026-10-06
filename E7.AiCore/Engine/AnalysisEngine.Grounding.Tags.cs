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
    // AnalysisEngine -- Grounding.Tags.
    // Moved verbatim from AiChatController.Grounding.Tags.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {

        // v1.0.160.174: map a point op V/C card name -> its -PM-0X002-Avg channel attid.
        // A-C=1002 A-V=2002 B-C=3002 B-V=4002 (Normal); +5000 for Reverse (6002/7002/8002/9002).
        // KR/CR (relay + indication) are NOT op V/C -> return 0 (bound normally).
        internal static int PmOpChannelForName(string name)
        {
            string a = (name ?? "").ToUpperInvariant();
            if (a.IndexOf("KR") >= 0 || a.IndexOf("CR") >= 0) { return 0; }
            bool A = a.IndexOf("A END") >= 0, B = a.IndexOf("B END") >= 0;
            bool NW = a.IndexOf("NW") >= 0, RW = a.IndexOf("RW") >= 0;
            bool V = a.IndexOf("-V") >= 0, C = a.IndexOf("-C") >= 0;
            if (!(A ^ B) || !(NW ^ RW) || !(V ^ C)) { return 0; }
            int baseCh;
            if (A && C) { baseCh = 1000; }
            else if (A && V) { baseCh = 2000; }
            else if (B && C) { baseCh = 3000; }
            else if (B && V) { baseCh = 4000; }
            else { return 0; }
            if (RW) { baseCh += 5000; }
            return baseCh + 2;
        }

        // Only the aliases that actually appear in this alert's formula/description are injected,
        // so the block stays short and every line is relevant to the verdict being made.
        internal static string BuildAliasBridge(string formulaAndCard)
        {
            if (string.IsNullOrEmpty(formulaAndCard)) { return ""; }
            string hay = formulaAndCard.ToUpperInvariant();
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < _aliasPairs.GetLength(0); i++)
            {
                string title = _aliasPairs[i, 0], alias = _aliasPairs[i, 1];
                if (hay.IndexOf(alias.ToUpperInvariant(), StringComparison.Ordinal) < 0) { continue; }
                if (b.Length == 0)
                {
                    b.Append("[NAME BRIDGE - the alert card and the formula use ALIAS names; the telemetry above ")
                     .Append("is listed under RDPMS TITLES. These are THE SAME MEASUREMENT. Do NOT report a ")
                     .Append("component as missing because its alias name does not appear in the telemetry:]").Append("\n");
                }
                b.Append("  ").Append(alias).Append("  ==  ").Append(title).Append("\n");
            }
            if (b.Length > 0) { b.Append("\n"); }
            return b.ToString();
        }


        internal static string GateNameForAttId(int attId)
        {
            string g;
            return _gateById.TryGetValue(attId, out g) ? g : null;
        }

        internal static bool GateIsUngatedId(int attId) { return _gateUngatedIds.Contains(attId); }

        // Point detection only: the relay that must be proven DOWN while the gate is UP.
        internal static string GateCounterpart(string gateName)
        {
            if (string.Equals(gateName, "NWKR", StringComparison.OrdinalIgnoreCase)) { return "RWKR"; }
            if (string.Equals(gateName, "RWKR", StringComparison.OrdinalIgnoreCase)) { return "NWKR"; }
            return null;
        }

        internal static string GateCanon(string x)
        {
            if (string.IsNullOrEmpty(x)) { return ""; }
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < x.Length; i++)
            {
                char ch = x[i];
                if (char.IsLetterOrDigit(ch)) { b.Append(char.ToUpperInvariant(ch)); }
                else if (ch == '_') { b.Append('_'); }
            }
            return b.ToString();
        }

        // Relay identity is by NAME among the asset's DataLogger relays -- NEVER by AssetAttributeId,
        // because relay ids are asset-specific and collide with analog ids (GatedAverage's own note).
        // Exact canon match wins; otherwise a UNIQUE "_<GATE>" suffix match ("Combined-NWKR");
        // none or ambiguous -> null, fail-closed.
        internal static JObject GateRelayRow(string tagsJson, string gateName)
        {
            if (string.IsNullOrEmpty(tagsJson) || string.IsNullOrEmpty(gateName)) { return null; }
            try
            {
                string cg = GateCanon(gateName), suffix = "_" + cg;
                JObject exact = null; int exactN = 0;
                JObject token = null; int tokenN = 0;
                List<JToken> docs = ParseJsonDocuments(tagsJson);
                for (int d = 0; d < docs.Count; d++)
                {
                    foreach (JToken row in docs[d].SelectTokens("$..*"))
                    {
                        JObject o = row as JObject;
                        if (o == null) { continue; }
                        JToken nm = PropCI(o, "AssetAttributeName");
                        if (nm == null) { continue; }
                        JToken rt = PropCI(o, "RoleType");
                        if (rt != null && rt.ToString().IndexOf("DataLogger", StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                        string cn = GateCanon(nm.ToString());
                        if (cn == cg) { exact = o; exactN++; }
                        else if (cn.EndsWith(suffix, StringComparison.Ordinal)) { token = o; tokenN++; }
                    }
                }
                if (exactN == 1) { return exact; }
                if (exactN == 0 && tokenN == 1) { return token; }
            }
            catch { }
            return null;
        }


        internal static string AttIdForTag(string tagsJson, string tagId)
        {
            if (string.IsNullOrEmpty(tagsJson) || string.IsNullOrEmpty(tagId)) { return null; }
            try
            {
                List<JToken> docs = ParseJsonDocuments(tagsJson);
                for (int d = 0; d < docs.Count; d++)
                {
                    foreach (JToken row in docs[d].SelectTokens("$..*"))
                    {
                        JObject o = row as JObject;
                        if (o == null) { continue; }
                        JToken tid = PropCI(o, "TagID");
                        if (tid == null) { tid = PropCI(o, "TagId"); }
                        if (tid == null || !string.Equals(tid.ToString(), tagId, StringComparison.Ordinal)) { continue; }
                        JToken att = PropCI(o, "AssetAttributeId");
                        if (att == null) { att = PropCI(o, "AttributeId"); }
                        if (att != null && !string.IsNullOrEmpty(att.ToString())) { return att.ToString(); }
                    }
                }
            }
            catch { }
            return null;
        }


        internal void RegisterTagMeta(string tagId, string attr, bool isTpr, bool isTrigger)
        {
            if (string.IsNullOrEmpty(tagId)) { return; }
            JObject m = new JObject(); m["attr"] = attr ?? ""; m["isTpr"] = isTpr; m["isTrigger"] = isTrigger;
            lock (Sess._trendLock) { Sess._tagMeta[tagId] = m; }
        }


        // v1.0.152.6: a search_tags row belongs to the current asset (or carries no AssetId, in
        // which case it cannot be disqualified). search_tags can return rows for other similarly-
        // named assets, so TPR + derived components must be AssetId-grounded (mirrors PickAlertingTagId).
        internal static bool RowAssetOk(JObject o, string assetId, bool allowMissing)
        {
            if (string.IsNullOrEmpty(assetId)) { return true; }
            JToken aid = PropCI(o, "AssetId");
            if (aid == null) { return allowMissing; }   // v1.0.152.7: fail-closed for name-scoped search (allowMissing=false)
            return string.Equals(aid.ToString().Trim(), assetId.Trim(), StringComparison.Ordinal);
        }


        // v1.0.152.6: resolve the DIGITAL TPR state tag grounded to AssetId, preferring EXACT
        // digital-state names over a broad Contains("TPR") (which can match the analog "TPR V").
        internal static string ResolveTprTag(string tagsJson, string assetId, bool allowMissing)
        {
            if (string.IsNullOrEmpty(tagsJson)) { return null; }
            // v1.0.160.108: resolve the DIGITAL TPR STATE by the fixed rule AssetId+attid+datatype
            // (attid 6, datatype=DataLogger) -- immune to the attid-6 name collision with the analog
            // "TPR V" (RDPMS). Name chain kept only as a last-ditch fallback.
            return PickTagIdByAttrId(tagsJson, assetId, "6", true)
                ?? TagIdForAttributeNameExactForAsset(tagsJson, "TPR", assetId, allowMissing)
                ?? TagIdForAttributeNameExactForAsset(tagsJson, "TPR STATUS", assetId, allowMissing)
                ?? TagIdForAttributeNameExactForAsset(tagsJson, "TPR STATE", assetId, allowMissing)
                ?? TagIdForAttributeNameExactForAsset(tagsJson, "TPR (LOC)", assetId, allowMissing)
                ?? TagIdForAttributeNameForAsset(tagsJson, "TPR", assetId, allowMissing);
        }


        // v1.0.152.6: EXACT name match, grounded to AssetId.
        internal static string TagIdForAttributeNameExactForAsset(string tagsJson, string canonical, string assetId, bool allowMissing)
        {
            if (string.IsNullOrEmpty(tagsJson) || string.IsNullOrEmpty(canonical)) { return null; }
            string want = canonical.ToUpperInvariant().Trim();
            try
            {
                List<JToken> docs = ParseJsonDocuments(tagsJson);
                for (int d = 0; d < docs.Count; d++)
                {
                    foreach (JToken row in docs[d].SelectTokens("$..*"))
                    {
                        JObject o = row as JObject; if (o == null) { continue; }
                        JToken nm = PropCI(o, "AssetAttributeName");
                        JToken tid = PropCI(o, "TagID") ?? PropCI(o, "TagId");
                        if (nm == null || tid == null) { continue; }
                        if (nm.ToString().ToUpperInvariant().Trim() != want) { continue; }
                        if (!RowAssetOk(o, assetId, allowMissing)) { continue; }
                        return tid.ToString();
                    }
                }
            }
            catch { }
            return null;
        }


        // v1.0.152.6: exact-then-Contains name match, grounded to AssetId. Used for the TPR
        // last-resort and every derived formula component (so a synthetic derived line never
        // combines telemetry from different assets).
        internal static string TagIdForAttributeNameForAsset(string tagsJson, string canonical, string assetId, bool allowMissing)
        {
            if (string.IsNullOrEmpty(tagsJson) || string.IsNullOrEmpty(canonical)) { return null; }
            List<string> wants = new List<string> { canonical };
            if (canonical.StartsWith("TR V")) { wants.Add("VR"); wants.Add("TR V"); }
            try
            {
                List<JToken> docs = ParseJsonDocuments(tagsJson);
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int d = 0; d < docs.Count; d++)
                    {
                        foreach (JToken row in docs[d].SelectTokens("$..*"))
                        {
                            JObject o = row as JObject; if (o == null) { continue; }
                            JToken nm = PropCI(o, "AssetAttributeName");
                            JToken tid = PropCI(o, "TagID") ?? PropCI(o, "TagId");
                            if (nm == null || tid == null) { continue; }
                            if (!RowAssetOk(o, assetId, allowMissing)) { continue; }
                            string upper = nm.ToString().ToUpperInvariant().Trim();
                            for (int w = 0; w < wants.Count; w++)
                            {
                                bool hit = pass == 0 ? upper == wants[w]
                                    : (upper.IndexOf(wants[w], StringComparison.Ordinal) >= 0 || wants[w].IndexOf(upper, StringComparison.Ordinal) >= 0);
                                if (hit) { return tid.ToString(); }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }


        internal string AlertingTagId()
        {
            return Sess.requiredAlertingTagId ?? Sess.presumedAlertingTagId;
        }


        // v1.0.76.0: the ONLY mechanical route to presumedAlertingTagId. Runs the
        // SAME matcher prefetch uses (PickAlertingTagId) against a model-issued
        // search_tags result, so a substitute tag is never trusted on faith.
        internal void TryResolveAlertingTagFromSearch(string searchResult)
        {
            if (Sess.requiredAlertingTagId != null || Sess.presumedAlertingTagId != null)
            {
                return;   // identity already known -- never overwritten
            }
            if (Sess._prefetchCtx == null || string.IsNullOrEmpty(searchResult))
            {
                return;
            }
            string assetId = GetCtx(Sess._prefetchCtx, "assetId");
            string resolved = PickAlertingTagId(searchResult, Sess._prefetchCtx, assetId);
            if (resolved != null)
            {
                Sess.presumedAlertingTagId = resolved;
                Sess.coverageTagPresumed = true;
            }
        }


        internal static bool IsHistoryFamily(string toolName)
        {
            for (int i = 0; i < _jsonHistoryFamily.Length; i++)
            {
                if (string.Equals(toolName, _jsonHistoryFamily[i], StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }


        // v1.0.160.104: resolve a component's TagID AND AssetAttributeId from the SAME search_tags
        // row, so the history tag and the envelope lookup are guaranteed to reference the same
        // attribute (not two independent scans that could match different rows). tag=null / attid=-1
        // when unresolved. Mirrors the tag resolver's two matching passes.
        internal static void ResolveTagAndAttidForAsset(string tagsJson, string canonical, string assetId, out string tag, out int attid)
        {
            tag = null; attid = -1;
            if (string.IsNullOrEmpty(tagsJson) || string.IsNullOrEmpty(canonical)) { return; }
            List<string> wants = new List<string> { canonical };
            if (canonical.StartsWith("TR V")) { wants.Add("VR"); wants.Add("TR V"); }
            try
            {
                List<JToken> docs = ParseJsonDocuments(tagsJson);
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int d = 0; d < docs.Count; d++)
                    {
                        foreach (JToken row in docs[d].SelectTokens("$..*"))
                        {
                            JObject o = row as JObject;
                            if (o == null) { continue; }
                            JToken nm = PropCI(o, "AssetAttributeName");
                            JToken tid = PropCI(o, "TagID") ?? PropCI(o, "TagId");
                            if (nm == null || tid == null) { continue; }
                            if (!RowAssetOk(o, assetId, false)) { continue; }
                            string upper = nm.ToString().ToUpperInvariant().Trim();
                            for (int w = 0; w < wants.Count; w++)
                            {
                                bool hit = pass == 0
                                    ? upper == wants[w]
                                    : (upper.IndexOf(wants[w], StringComparison.Ordinal) >= 0 || wants[w].IndexOf(upper, StringComparison.Ordinal) >= 0);
                                if (hit)
                                {
                                    tag = tid.ToString();
                                    JToken aid = PropCI(o, "AssetAttributeId");
                                    int parsed;
                                    if (aid != null && int.TryParse(aid.ToString(), out parsed)) { attid = parsed; }
                                    return;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }


        // v1.0.160.42: TRUE if the relay is STEADY UP across [ts-guard, ts+guard] with no transition.
        // Discard (FALSE) on a toggle inside the window (train edge / device-clock skew) or a steady
        // DOWN (train present). Unknown / no relay data => TRUE (keep -- never drop real degradation).
        internal static bool GateSteadyUp(List<double[]> tpr, double ts, int guardSec)
        {
            if (tpr == null || tpr.Count == 0) { return true; }
            double lo = ts - guardSec;
            double hi = ts + guardSec;
            int haveSeed = 0; int seedState = 0;
            for (int i = 0; i < tpr.Count; i++)
            {
                if (tpr[i][0] <= lo) { seedState = tpr[i][1] >= 0.5 ? 1 : 0; haveSeed = 1; }
                else { break; }
            }
            int curState = seedState; int haveCur = haveSeed; int transition = 0;
            for (int i = 0; i < tpr.Count; i++)
            {
                double e = tpr[i][0];
                if (e <= lo) { continue; }
                if (e > hi) { break; }
                int st = tpr[i][1] >= 0.5 ? 1 : 0;
                if (haveCur == 1 && st != curState) { transition = 1; break; }
                curState = st; haveCur = 1;
            }
            if (transition == 1) { return false; }
            if (haveCur == 1 && curState == 0) { return false; }
            return true;
        }


        // v1.0.82.0 rev J: resolve a canonical attribute name to a TagID using the
        // asset's search_tags rows (which carry TagID + AssetAttributeName in the
        // standard profile). Exact uppercase match first, then Contains either way.
        // "TR V" additionally accepts "VR" (site naming varies for the track-relay
        // voltage: "Vr" vs "TR V (Relay)").
        internal static string TagIdForAttributeName(string tagsJson, string canonical)
        {
            if (string.IsNullOrEmpty(tagsJson) || string.IsNullOrEmpty(canonical)) { return null; }
            List<string> wants = new List<string> { canonical };
            // v1.0.85.0: the track-relay voltage Title is "TR V (Relay)"; some sites
            // name it "Vr" instead. Accept both. Match is whitespace/unit-normalized.
            if (canonical.StartsWith("TR V")) { wants.Add("VR"); wants.Add("TR V"); }
            try
            {
                List<JToken> docs = ParseJsonDocuments(tagsJson);
                for (int pass = 0; pass < 2; pass++)   // pass 0 exact, pass 1 contains
                {
                    for (int d = 0; d < docs.Count; d++)
                    {
                        foreach (JToken row in docs[d].SelectTokens("$..*"))
                        {
                            JObject o = row as JObject;
                            if (o == null) { continue; }
                            JToken nm = PropCI(o, "AssetAttributeName");
                            JToken tid = PropCI(o, "TagID") ?? PropCI(o, "TagId");
                            if (nm == null || tid == null) { continue; }
                            string upper = nm.ToString().ToUpperInvariant().Trim();
                            for (int w = 0; w < wants.Count; w++)
                            {
                                bool hit = pass == 0
                                    ? upper == wants[w]
                                    : (upper.IndexOf(wants[w], StringComparison.Ordinal) >= 0
                                       || wants[w].IndexOf(upper, StringComparison.Ordinal) >= 0);
                                if (hit) { return tid.ToString(); }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }


        // -- v1.0.79.0 -- The MCP server (v1.5.55.1) does NOT read a "query" param on
        // search_tags; the real search-text filter is "AssetName" (ILIKE), with an
        // optional "AttributeName" (ILIKE) to narrow to one attribute. The dead
        // "query" param this used to send was ignored, so the server returned
        // unfiltered/arbitrary tags -- the "wrong assets" failure that made every
        // track-circuit alert fall through to the AssetId fallback. AssetName is the
        // asset's name; AttributeName is added ONLY when DomainAttributeName resolves
        // the alert's triggered parameter to a canonical tag name, because the raw
        // paramLabel ("ITC RELAY END(mA)") does not ILIKE-match the real name
        // ("Ir mA") and would filter the correct tag OUT. When no canonical name is
        // known, AssetName alone is sent and the matcher picks among the asset's tags.
        // v1.0.160.55: upstream drivers of a DERIVED attribute, per the circuit invariants. A derived
        // name is not a stored tag, so it must be expanded to the measured components that produce it
        // or the series never gets fetched (and the K2 residual then has nothing to work with).
        internal static string[] CircuitDriversFor(string canonicalAttr)
        {
            if (string.IsNullOrEmpty(canonicalAttr)) { return null; }
            string a = canonicalAttr.Trim().ToUpperInvariant();
            // v1.0.160.63: MEASURED partners too, not just derived expansions. TC TR VOLT LOW maps
            // to ITC RELAY END alone, so without this If is never fetched and K2 cannot run --
            // confirmed on alert 524613, where Phase 1 injected 7 circuit rules and Phase 2
            // produced no [circuit] line at all.
            if (a == "IR MA") { return new string[] { "IF MA" }; }                                 // K2 partner
            if (a == "IF MA") { return new string[] { "IR MA" }; }                                 // K2 partner
            // v1.0.160.66: the range table spells it "IBlAST" (attribute 684), so the canonical
            // form is IBLAST. Keying only on IBALST meant this expansion never matched.
            if (a == "IBALST" || a == "IBLAST") { return new string[] { "IF MA", "IR MA" }; }      // K2
            if (a == "RRAIL") { return new string[] { "VF", "VR", "IF MA", "IR MA" }; }            // K3/K4
            if (a == "RTC VAR RES") { return new string[] { "VTC VAR RES", "IF MA" }; }            // K3
            if (a == "RTC CH FEED END") { return new string[] { "CHOKE V", "IF MA" }; }            // K3
            if (a == "TR V (RELAY)") { return new string[] { "IR MA" }; }                          // K4
            if (a == "ITC BATT CHARG") { return new string[] { "CHARGER MA", "IF MA" }; }          // K1
            return null;
        }


        // v1.0.160.55: the attributes this cause actually depends on. Ordered so the most
        // authoritative source wins the cap: L1 the RDPMS cause map, L2 the card's own conditions
        // (failing first), L3 circuit-wisdom drivers of any derived member.
        // v1.0.160.80: canonical name of the FIRST FAILING triggered condition -- i.e. the attribute
        // the alerting-tag path resolves and fetches. Same failing-first rule DriverSetForCause
        // uses, so the two cannot disagree about which attribute is "the alerting one".
        internal static string FirstFailingParamCanon(JObject ctx)
        {
            try
            {
                JObject card = ctx != null ? ctx["alertCard"] as JObject : null;
                JArray tc = card != null ? card["triggeredConditions"] as JArray : null;
                if (tc == null) { return ""; }
                for (int i = 0; i < tc.Count; i++)
                {
                    JObject c = tc[i] as JObject;
                    if (c == null || c["paramLabel"] == null) { continue; }
                    JArray th = c["thresholds"] as JArray;
                    if (th == null) { continue; }
                    for (int k = 0; k < th.Count; k++)
                    {
                        JObject t0 = th[k] as JObject;
                        if (t0 != null && t0["status"] != null
                            && string.Equals(t0["status"].ToString().Trim(), "FAIL", StringComparison.OrdinalIgnoreCase))
                        {
                            string cn = DomainAttributeName(c["paramLabel"].ToString());
                            return string.IsNullOrEmpty(cn) ? c["paramLabel"].ToString() : cn;
                        }
                    }
                }
            }
            catch { }
            return "";
        }


        internal static List<string> DriverSetForCause(JObject ctx)
        {
            List<string> outp = new List<string>();
            if (ctx == null) { return outp; }
            try
            {
                // ---- L1: the authoritative cause -> attribute map (already generated from the master)
                string cc = (GetCtx(ctx, "causeCode") ?? "").Trim();
                string[] mapped;
                if (cc.Length > 0 && RdpmsMaps.CauseToAttributes.TryGetValue(cc, out mapped) && mapped != null)
                {
                    for (int i = 0; i < mapped.Length; i++) { AddCanon(outp, mapped[i]); }
                }
                // ---- L2: every triggered condition, FAILING ones first (never just [0])
                JObject card = ctx["alertCard"] as JObject;
                JArray tc = card != null ? card["triggeredConditions"] as JArray : null;
                if (tc != null)
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        for (int i = 0; i < tc.Count; i++)
                        {
                            JObject c = tc[i] as JObject;
                            if (c == null || c["paramLabel"] == null) { continue; }
                            bool failed = false;
                            JArray th = c["thresholds"] as JArray;
                            if (th != null)
                            {
                                for (int k = 0; k < th.Count; k++)
                                {
                                    JObject t0 = th[k] as JObject;
                                    if (t0 != null && t0["status"] != null
                                        && string.Equals(t0["status"].ToString().Trim(), "FAIL", StringComparison.OrdinalIgnoreCase))
                                    { failed = true; break; }
                                }
                            }
                            if ((pass == 0) != failed) { continue; }   // pass 0 = failing, pass 1 = the rest
                            AddCanon(outp, c["paramLabel"].ToString());
                        }
                    }
                }
                // ---- L3: circuit-wisdom drivers of any DERIVED member already collected
                List<string> seed = new List<string>(outp);
                for (int i = 0; i < seed.Count; i++)
                {
                    string[] drv = CircuitDriversFor(seed[i]);
                    if (drv == null) { continue; }
                    for (int d = 0; d < drv.Length; d++) { AddCanon(outp, drv[d]); }
                }
            }
            catch { }
            // Cap so a wide IPS cause cannot fan out unbounded; L1 members were added first.
            int cap = ClampCfg(ReadInt("AnalyzeDriverSetMax", 6), 1, 12);
            if (outp.Count > cap) { outp.RemoveRange(cap, outp.Count - cap); }
            return outp;
        }


        // canonicalise + de-duplicate in one step; unmappable names are dropped rather than guessed.
        internal static void AddCanon(List<string> list, string raw)
        {
            // v1.0.160.66: DomainAttributeName maps LABELS ("ITC FEED END(mA)") and does NOT round-trip
            // its own output, so an already-canonical name such as "IF MA" -- exactly what
            // CircuitDriversFor returns -- resolved to nothing and was silently dropped. That is why
            // the K2 partner never reached the driver set. Fall back to the raw upper-cased name.
            string c = DomainAttributeName(raw);
            if (string.IsNullOrEmpty(c)) { c = (raw ?? "").Trim().ToUpperInvariant(); }
            if (string.IsNullOrEmpty(c)) { return; }
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], c, StringComparison.OrdinalIgnoreCase)) { return; }
            }
            list.Add(c);
        }


        // v1.0.160.56: instance passthrough so the driver set can be LOGGED (AiLog is an instance
        // member and NewSearchArgs is static). Returns the args untouched; recomputing the set is a
        // few dictionary lookups over the alert card, no I/O.
        internal JObject LogDriverSet(JObject args, JObject ctx)
        {
            try
            {
                List<string> d = DriverSetForCause(ctx);
                if (d.Count > 0)
                {
                    Sess._driverSetStr = string.Join(", ", d.ToArray());   // v1.0.160.57: for the audit record
                    AiLog("INFO", "PREFETCH", "driver set (" + d.Count + "): " + string.Join(", ", d.ToArray())
                        + (d.Count > 1 ? " -- full tag list (not narrowed to one attribute)" : " -- narrowed"));
                }
            }
            catch { }
            return args;
        }


        // Is this a point-machine alert? (cause code PT * -> POINT MACHINE)
        internal static bool IsPointCard(JObject ctx)
        {
            return AssetTypeFor(GetCtx(ctx, "causeCode")) == "POINT MACHINE";
        }


        // v1.0.96.0: is this specifically a point DETECTION-RELAY cause? For these, detection
        // disagreement MAY CONFIRM the reported condition; for all other point causes
        // (operation/current/voltage) disagreement weakens the mechanical assessment. Labeling
        // the class explicitly stops the model inferring it from similar cause-code wording.
        internal static bool IsPointDetectionRelayCause(string causeCode)
        {
            string c = (causeCode ?? "").Trim().ToUpperInvariant();
            return c == "PT NWKR RELAY DEFECT"
                || c == "PT RWKR RELAY DEFECT"
                || c == "PT NWKR RELAY DEFECT OP"
                || c == "PT RWKR RELAY DEFECT OP";
        }


        // v1.0.123.0: resolve which point-machine END (A or B) an alert refers to, from the
        // alert's paramLabel/description (e.g. "A TPT R" -> A, "B TPT R" -> B). Returns "A", "B",
        // or null if not stated. Card shows paramLabel like "A TPT R", so this is reliable.
        internal static string PmEndFromAlert(JObject ctx)
        {
            string[] probes = {
                GetCtx(ctx, "paramLabel"), GetCtx(ctx, "description"),
                GetCtx(ctx, "rawMessage"), GetCtx(ctx, "assetName")
            };
            // also look inside alertCard.triggeredConditions[0].paramLabel
            JToken card = ctx["alertCard"];
            JObject co = card as JObject;
            if (co != null)
            {
                JArray tc = co["triggeredConditions"] as JArray;
                JObject t0 = (tc != null && tc.Count > 0) ? tc[0] as JObject : null;
                if (t0 != null && t0["paramLabel"] != null)
                { probes = AppendOne(probes, t0["paramLabel"].ToString()); }
            }
            for (int i = 0; i < probes.Length; i++)
            {
                string p = probes[i];
                if (string.IsNullOrEmpty(p)) { continue; }
                string u = " " + p.Trim().ToUpperInvariant() + " ";
                // "A TPT", "A END", "A-END", leading "A "
                if (u.Contains(" A TPT") || u.Contains(" A END") || u.Contains(" A-END") || u.StartsWith(" A ")) { return "A"; }
                if (u.Contains(" B TPT") || u.Contains(" B END") || u.Contains(" B-END") || u.StartsWith(" B ")) { return "B"; }
            }
            return null;
        }


        internal static string[] AppendOne(string[] arr, string v)
        {
            string[] o = new string[arr.Length + 1];
            Array.Copy(arr, o, arr.Length); o[arr.Length] = v; return o;
        }


        // Returns the FRS-range attid for a point-machine operation VALUE attid, or the input
        // unchanged when there is no distinct range attid (indication/relay attids already carry
        // their own range).
        internal static string PmRangeAttidFor(string valueAttid)
        {
            if (string.IsNullOrEmpty(valueAttid)) { return valueAttid; }
            string r;
            return _pmValueToRangeAttid.TryGetValue(valueAttid.Trim(), out r) ? r : valueAttid;
        }


        // v1.0.124.0: resolve a point cause + END to the flat list of [attid, alt, label] to fetch.
        // End-specific tags are picked by END (unknown -> both A & B). Relays/single tags pass
        // through. VPT-110/IPT/VIPS-110 are handled by the existing mapping and not included here.
        internal static List<string[]> PmTagsForCause(string causeCode, string end)
        {
            List<string[]> outp = new List<string[]>();
            if (string.IsNullOrEmpty(causeCode)) { return outp; }
            PmTag[] spec;
            if (!_pmCauseTags.TryGetValue(causeCode.Trim(), out spec) || spec == null) { return outp; }
            for (int i = 0; i < spec.Length; i++)
            {
                PmTag t = spec[i];
                if (!string.IsNullOrEmpty(t.B))   // end-specific (A/B)
                {
                    // v1.0.154.0: emit the alert's end FIRST, then the OTHER end as a fallback in
                    // the SAME entry (Item2). Some point machines store both ends' operation
                    // voltage/current in ONE channel (proven on asset 41898: B-end attid 4002 is
                    // absent, but A-end 2002 carries the B-end incidence value 72.64V). Without the
                    // cross-end fallback a B-end alert looks for 4002, misses, and goes INCONCLUSIVE
                    // even though 2002 has the data. PickTagIdByAttrId tries Item1 then Item2.
                    if (end == "A") { outp.Add(new string[] { t.A, t.B, t.Label + " A" }); }
                    else if (end == "B") { outp.Add(new string[] { t.B, t.A, t.Label + " B" }); }
                    else
                    {
                        outp.Add(new string[] { t.A, t.B, t.Label + " A" });
                        outp.Add(new string[] { t.B, t.A, t.Label + " B" });
                    }
                }
                else { outp.Add(new string[] { t.A, t.Alt, t.Label }); }
            }
            return outp;
        }


        // v1.0.120.0: resolve a TagId from an already-fetched search_tags result by NUMERIC
        // AssetAttributeId (the EdgeX op attid), scoped to this asset. This is the option-A
        // resolution path: get_asset_tags carries no TagId, search_tags is the only source, so we
        // reuse the prefetched search_tags payload and match on AssetAttributeId (exact, numeric -
        // more robust than name matching). Returns null if not present.
        // v1.0.144.0: IPS-only fallback resolver - return the TagID of ANY tag registered for the
        // given assetId (the first one found), regardless of attribute. Used only when the mapped
        // IPS attid does not resolve, to ground on the asset's available telemetry instead of going
        // INCONCLUSIVE. NOTE: the returned tag may be a DIFFERENT attribute than the alerting one.
        internal static string PickAnyTagIdForAsset(string tagsJson, string assetId)
        {
            if (string.IsNullOrEmpty(tagsJson) || string.IsNullOrEmpty(assetId)) { return null; }
            try
            {
                List<JToken> docs = ParseJsonDocuments(tagsJson);
                for (int d = 0; d < docs.Count; d++)
                {
                    foreach (JToken row in docs[d].SelectTokens("$..*"))
                    {
                        JObject o = row as JObject;
                        if (o == null) { continue; }
                        JToken tid = PropCI(o, "TagID") ?? PropCI(o, "TagId") ?? PropCI(o, "tag");
                        if (tid == null) { continue; }
                        JToken aid = PropCI(o, "AssetId");
                        if (aid != null && string.Equals(aid.ToString(), assetId, StringComparison.Ordinal))
                        { return tid.ToString(); }
                    }
                }
            }
            catch { }
            return null;
        }


        internal static string PickTagIdByAttrId(string tagsJson, string assetId, string attid)
        {
            if (string.IsNullOrEmpty(tagsJson) || string.IsNullOrEmpty(attid)) { return null; }
            try
            {
                List<JToken> docs = ParseJsonDocuments(tagsJson);
                for (int d = 0; d < docs.Count; d++)
                {
                    foreach (JToken row in docs[d].SelectTokens("$..*"))
                    {
                        JObject o = row as JObject;
                        if (o == null) { continue; }
                        JToken tid = PropCI(o, "TagID") ?? PropCI(o, "TagId") ?? PropCI(o, "tag");
                        // v1.0.122.0: the tag table exposes the op attid under BOTH AssetAttributeId
                        // and EdgeXAttributeId (= 6005); other history responses use AttributeId.
                        // Match any of them so resolution works across formats.
                        JToken aaid = PropCI(o, "AssetAttributeId") ?? PropCI(o, "EdgeXAttributeId") ?? PropCI(o, "AttributeId");
                        if (tid == null || aaid == null) { continue; }
                        if (!string.Equals(aaid.ToString().Trim(), attid, StringComparison.Ordinal)) { continue; }
                        JToken aid = PropCI(o, "AssetId");
                        if (assetId != null && aid != null && !string.Equals(aid.ToString(), assetId, StringComparison.Ordinal)) { continue; }
                        return tid.ToString();
                    }
                }
            }
            catch { }
            return null;
        }


        // v1.0.160.108: datatype is binary -- RoleType starting 'D' = DataLogger, everything else
        // (RDPMS, empty, unknown) = RDPMS. This is the fixed rule the resolver keys on.
        internal static bool IsDataLoggerRole(string roleType)
        {
            string r = (roleType ?? "").Trim();
            return r.Length > 0 && (r[0] == 'D' || r[0] == 'd');
        }


        // v1.0.160.108: THE fixed resolution rule -- AssetId + attid + datatype -> TagID. Splits the
        // attid-6 collision (TPR DataLogger vs TPR V RDPMS) that the datatype-blind 3-arg form cannot.
        internal static string PickTagIdByAttrId(string tagsJson, string assetId, string attid, bool wantDataLogger)
        {
            if (string.IsNullOrEmpty(tagsJson) || string.IsNullOrEmpty(attid)) { return null; }
            try
            {
                List<JToken> docs = ParseJsonDocuments(tagsJson);
                for (int d = 0; d < docs.Count; d++)
                {
                    foreach (JToken row in docs[d].SelectTokens("$..*"))
                    {
                        JObject o = row as JObject;
                        if (o == null) { continue; }
                        JToken tid = PropCI(o, "TagID") ?? PropCI(o, "TagId") ?? PropCI(o, "tag");
                        JToken aaid = PropCI(o, "AssetAttributeId") ?? PropCI(o, "EdgeXAttributeId") ?? PropCI(o, "AttributeId");
                        if (tid == null || aaid == null) { continue; }
                        if (!string.Equals(aaid.ToString().Trim(), attid, StringComparison.Ordinal)) { continue; }
                        JToken aid = PropCI(o, "AssetId");
                        if (assetId != null && aid != null && !string.Equals(aid.ToString(), assetId, StringComparison.Ordinal)) { continue; }
                        JToken rt = PropCI(o, "RoleType");
                        if (IsDataLoggerRole(rt == null ? "" : rt.ToString()) != wantDataLogger) { continue; }
                        return tid.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        internal static Dictionary<string, RdpmsMaps.HealthBand> HealthBandIndex()
        {
            if (_healthBandIndex != null) { return _healthBandIndex; }
            lock (_hbLock)
            {
                if (_healthBandIndex != null) { return _healthBandIndex; }
                var idx = new Dictionary<string, RdpmsMaps.HealthBand>(StringComparer.Ordinal);
                foreach (var kv in RdpmsMaps.HealthBands)
                {
                    // the raw key (IF_MA) and its space form
                    idx[kv.Key] = kv.Value;
                    idx[kv.Key.Replace('_', ' ')] = kv.Value;
                    // every name in Resolves: "RDPMS: TR V (Relay) / VTC TR"
                    string res = kv.Value.Resolves ?? "";
                    int colon = res.IndexOf(':');
                    if (colon >= 0) { res = res.Substring(colon + 1); }
                    foreach (string part in res.Split('/'))
                    {
                        string nk = NormAttrKey(part);
                        if (nk.Length > 0 && !idx.ContainsKey(nk)) { idx[nk] = kv.Value; }
                        string nku = nk.Replace(' ', '_');
                        if (nku.Length > 0 && !idx.ContainsKey(nku)) { idx[nku] = kv.Value; }
                    }
                }
                _healthBandIndex = idx;
                return idx;
            }
        }


        internal static string HealthBandNote(string attrName)
        {
            if (string.IsNullOrEmpty(attrName)) { return ""; }
            var idx = HealthBandIndex();
            RdpmsMaps.HealthBand band;
            bool found = idx.TryGetValue(NormAttrKey(attrName), out band)
                      || idx.TryGetValue(NormAttrKey(attrName).Replace(' ', '_'), out band);
            if (!found)
            {
                // resolve alias -> Title, then try again
                string title;
                if (_aliasToTitle.TryGetValue(NormAttrKey(attrName), out title))
                {
                    found = idx.TryGetValue(NormAttrKey(title), out band)
                         || idx.TryGetValue(NormAttrKey(title).Replace(' ', '_'), out band);
                }
            }
            if (!found) { return ""; }
            string note = " valid [" + band.Min.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "-" + band.Max.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
            if (!string.IsNullOrEmpty(band.Corr)) { note += ", correlate with " + band.Corr; }
            return note;
        }

        internal static string NormAttrKey(string s)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            string prev = null;
            while (prev != s) { prev = s; s = _unitSuffix.Replace(s, ""); }
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
            return s.ToUpperInvariant();
        }


        // v1.0.85.0: resolve an alert-card attribute name (alias OR already-Title) to
        // the real Title used by search_tags. Falls back to the legacy heuristic only
        // if the alias table has no entry (keeps behavior for anything not yet mapped).
        internal static string DomainAttributeName(string want)
        {
            if (string.IsNullOrEmpty(want)) { return null; }
            string key = NormAttrKey(want);
            string title;
            if (_aliasToTitle.TryGetValue(key, out title)) { return title.ToUpperInvariant(); }
            // legacy fallback (unmapped names): old Contains heuristic
            string u = want.ToUpperInvariant();
            bool isI = u.Contains("(MA)") || u.Contains(" MA") || u.Contains("CURR") || u.StartsWith("I");
            if (u.Contains("CHOKE")) { return "CHOKE V"; }
            if (u.Contains("CHARGER")) { return isI ? "CHARGER MA" : "CHARGER OP V"; }
            if (u.Contains("TPR"))
            {
                if (u.Contains("LOC")) { return "TPR V (LOC)"; }
                return "TPR V";
            }
            if (u.Contains(" TR") || u.EndsWith("TR") || u.Contains("TRACK RELAY")) { return "TR V (RELAY)"; }
            bool relay = u.Contains("RELAY END") || u.Contains("RELAY-END") || u.Contains("(IR)");
            bool feed = u.Contains("FEED END") || u.Contains("FEED-END") || u.Contains("(IF)");
            if (relay) { return isI ? "IR MA" : "VR"; }
            if (feed) { return isI ? "IF MA" : "VF"; }
            return null;
        }


        internal static string PickAlertingTagId(string tagsJson, JObject ctx, string assetId)
        {
            if (string.IsNullOrEmpty(tagsJson)) { return null; }
            // v1.0.143.0: ATTID fast paths - resolve the alerting tag by number, not name. PM tags
            // are named "NNNNN-PM-06005-*" (name match fails); IPS resolves the same clean way. Both
            // match AssetAttributeId via PickTagIdByAttrId. Falls through to the name matcher below.
            {
                string cc0 = GetCtx(ctx, "causeCode");
                if (!string.IsNullOrEmpty(cc0) && cc0.Trim().ToUpperInvariant().StartsWith("PT "))
                {
                    List<string[]> pmt = PmTagsForCause(cc0.Trim(), PmEndFromAlert(ctx));
                    for (int i = 0; i < pmt.Count; i++)
                    {
                        string tid = PickTagIdByAttrId(tagsJson, assetId, pmt[i][0]);
                        if (string.IsNullOrEmpty(tid) && !string.IsNullOrEmpty(pmt[i][1]))
                        { tid = PickTagIdByAttrId(tagsJson, assetId, pmt[i][1]); }
                        if (!string.IsNullOrEmpty(tid)) { return tid; }
                    }
                }
                if (!string.IsNullOrEmpty(cc0))
                {
                    string ipsAttid;
                    if (_ipsCauseAttid.TryGetValue(cc0.Trim(), out ipsAttid) && !string.IsNullOrEmpty(ipsAttid))
                    {
                        string tid = PickTagIdByAttrId(tagsJson, assetId, ipsAttid);
                        // v1.0.144.0: IPS-ONLY fallback - if the mapped attid does not resolve,
                        // ground on ANY tag registered for this assetId (whatever attribute it is)
                        // rather than go INCONCLUSIVE for a missing exact tag. IPS only.
                        if (string.IsNullOrEmpty(tid)) { tid = PickAnyTagIdForAsset(tagsJson, assetId); }
                        if (!string.IsNullOrEmpty(tid)) { return tid; }
                    }
                }
            }
            string want = "";
            JToken card = ctx["alertCard"];
            if (card != null && card.Type == JTokenType.Object)
            {
                JToken tc = card["triggeredConditions"];
                if (tc != null && tc.Type == JTokenType.Array && ((JArray)tc).Count > 0)
                {
                    // v1.0.160.12: anchor on the condition that actually FAILED its threshold -- for
                    // a multi-condition AND rule the deciding (failed) attribute is the fault and is
                    // not always triggeredConditions[0]. (505259: [0] ITC RELAY END PASSED, [1] VTC
                    // 24 DC LOC FAILED = the fault; anchoring on [0] coverage-checked the wrong tag
                    // and spuriously downgraded.) Skip type=="ips" corroborators. Fall back to [0]
                    // when none is failed, so single-condition alerts are unchanged.
                    JArray tca = (JArray)tc;
                    JToken chosenCond = null;
                    foreach (JToken ctk in tca)
                    {
                        JObject co = ctk as JObject; if (co == null) { continue; }
                        string cty = co["type"] != null ? co["type"].ToString().ToLowerInvariant() : "";
                        if (cty == "ips") { continue; }
                        if (TriggeredConditionFailed(co)) { chosenCond = co; break; }
                    }
                    if (chosenCond == null) { chosenCond = tca[0]; }
                    JToken p = chosenCond["paramLabel"];
                    if (p != null) { want = p.ToString(); }
                }
            }
            // v1.0.91.0 STAGE 3: if the card gave no paramLabel (want still empty -- the
            // empty-payload/browser case), map the cause code to its alerting attribute via
            // RdpmsMaps.CauseToAttributes (from the wisdom xlsx) instead of matching the raw
            // cause-code text against tag names. The card's paramLabel, when present, remains
            // authoritative and is NOT overridden.
            // NOTE (multi-attribute): 74 of 151 cause codes map to MULTIPLE analog attributes
            // (e.g. TC BALST/SLPR RES LOW -> Ir mA + IBALST). PickAlertingTagId selects ONE
            // tag to anchor the grounding, so it takes the PRIMARY (first) attribute here.
            // This is the correct anchor for the alert's headline condition; the full derived
            // component set is still pulled by the derived branch when the primary is derived.
            // A future refinement (a PrimaryAttribute/role column in the xlsx, or grounding
            // every mapped attribute) is tracked as an open item -- see the changelog.
            if (want.Length == 0)
            {
                string cc = GetCtx(ctx, "causeCode");
                if (!string.IsNullOrEmpty(cc))
                {
                    string[] attrs;
                    if (RdpmsMaps.CauseToAttributes.TryGetValue(cc.Trim(), out attrs) && attrs != null && attrs.Length > 0)
                    {
                        want = attrs[0];   // PRIMARY attribute anchors the grounding tag
                    }
                    else
                    {
                        want = cc;         // no mapping (18 codes) -> raw cause code (legacy)
                    }
                }
            }
            if (want.Length == 0) { return null; }

            // v1.0.160.112 (BUG-2): resolve the alerting tag by ATTID from the AliasName (the card's
            // FAIL-condition paramLabel, or the CauseToAttributes primary). The card carries no attid;
            // RdpmsAttrMap.AttidForAlias bridges AliasName -> attid, then PickTagIdByAttrId matches the
            // series by attid + datatype (RDPMS) -- immune to the card-title <-> attribute-name alias
            // the name scorer below misses (card "VTC 24 DC TPR I/P(V)" vs tag row name "TPR V", both
            // attid 6 -> 527627). Falls through to the existing name/per-end logic when unmapped.
            {
                int aliasAttid = RdpmsAttrMap.AttidForAlias(want);
                if (aliasAttid > 0)
                {
                    string tidByAlias = PickTagIdByAttrId(tagsJson, assetId, aliasAttid.ToString(), false);
                    if (!string.IsNullOrEmpty(tidByAlias)) { return tidByAlias; }
                }
            }

            // v1.0.160.21: PER-END attid anchor -- for the PM NWKR/RWKR causes, resolve the grounding
            // tag by AssetAttributeId (the per-end attid) rather than the generic name, so we anchor
            // on the real A/B-end sensor, not the rollup. Falls through to name scoring if no match.
            {
                string ccPe = GetCtx(ctx, "causeCode");
                int[] peAttids;
                if (!string.IsNullOrEmpty(ccPe)
                    && CauseLogicMaps.CauseToAttributesPerEnd.TryGetValue(ccPe.Trim(), out peAttids))
                {
                    string peTag = PickTagByAttid(tagsJson, assetId, peAttids);
                    if (peTag != null) { return peTag; }
                }
            }

            string[] wantWords = System.Text.RegularExpressions.Regex
                .Split(want.ToUpperInvariant(), "[^A-Z0-9]+");
            string domain = DomainAttributeName(want);
            string bestTag = null; int bestScore = 0;
            string exactTag = null;
            try
            {
                List<JToken> docs = ParseJsonDocuments(tagsJson);
                for (int d = 0; d < docs.Count; d++)
                {
                    foreach (JToken row in docs[d].SelectTokens("$..*"))
                    {
                        JObject o = row as JObject;
                        if (o == null) { continue; }
                        // Real field names, confirmed from a live search_tags response:
                        // TagID, AssetId, AssetAttributeId, AssetAttributeName.
                        // The earlier guesses ("AttributeName"/"Name") matched nothing,
                        // so the matcher silently returned null every time.
                        JToken tid = PropCI(o, "TagID") ?? PropCI(o, "TagId") ?? PropCI(o, "tag");
                        JToken nm = PropCI(o, "AssetAttributeName") ?? PropCI(o, "AttributeName") ?? PropCI(o, "Name");
                        if (tid == null || nm == null) { continue; }
                        // search_tags matches by name, so it can return other assets
                        JToken aid = PropCI(o, "AssetId");
                        if (assetId != null && aid != null && !string.Equals(aid.ToString(), assetId, StringComparison.Ordinal)) { continue; }
                        string upper = nm.ToString().ToUpperInvariant().Trim();
                        // an exact domain hit wins outright -- no scoring needed
                        if (domain != null && upper == domain) { exactTag = tid.ToString(); }
                        int score = 0;
                        for (int w = 0; w < wantWords.Length; w++)
                        {
                            if (wantWords[w].Length < 2) { continue; }
                            if (upper.IndexOf(wantWords[w], StringComparison.Ordinal) >= 0) { score++; }
                        }
                        if (score > bestScore) { bestScore = score; bestTag = tid.ToString(); }
                    }
                }
            }
            catch { return null; }
            if (exactTag != null) { return exactTag; }
            // require more than a single weak word hit: "MA" alone matches several
            // attributes and picking one of them arbitrarily is worse than not guessing
            return bestScore >= 2 ? bestTag : null;
        }


        // v1.0.160.21: return the TagID of the search_tags row whose AssetAttributeId equals one of
        // the given per-end attids (tried in order -- alerting/A-end first) for this asset. Anchors
        // PM NWKR/RWKR grounding on the per-end sensor instead of the generic rollup. Attid match is
        // exact, so it is immune to the VPT110/VPT 110 name-spelling inconsistencies.
        internal static string PickTagByAttid(string tagsJson, string assetId, int[] attids)
        {
            if (string.IsNullOrEmpty(tagsJson) || attids == null) { return null; }
            try
            {
                List<JToken> docs = ParseJsonDocuments(tagsJson);
                for (int a = 0; a < attids.Length; a++)
                {
                    string target = attids[a].ToString();
                    for (int d = 0; d < docs.Count; d++)
                    {
                        foreach (JToken row in docs[d].SelectTokens("$..*"))
                        {
                            JObject o = row as JObject;
                            if (o == null) { continue; }
                            JToken aaid = PropCI(o, "AssetAttributeId");
                            if (aaid == null || aaid.ToString().Trim() != target) { continue; }
                            JToken aid = PropCI(o, "AssetId");
                            if (assetId != null && aid != null && !string.Equals(aid.ToString(), assetId, StringComparison.Ordinal)) { continue; }
                            JToken tid = PropCI(o, "TagID") ?? PropCI(o, "TagId") ?? PropCI(o, "tag");
                            if (tid != null) { return tid.ToString(); }
                        }
                    }
                }
            }
            catch { }
            return null;
        }


        // v1.0.78.0: does a search_tags (or history_get) JSON payload contain any
        // row for this AssetId? Used to decide whether a search_tags result is even
        // about the alerting asset before it is shown to the model. Reuses the same
        // document parser and case-insensitive field lookup the matcher uses.
        internal static bool TagsContainAsset(string tagsJson, string assetId)
        {
            if (string.IsNullOrEmpty(tagsJson) || string.IsNullOrEmpty(assetId)) { return false; }
            try
            {
                List<JToken> docs = ParseJsonDocuments(tagsJson);
                for (int d = 0; d < docs.Count; d++)
                {
                    foreach (JToken row in docs[d].SelectTokens("$..*"))
                    {
                        JObject o = row as JObject;
                        if (o == null) { continue; }
                        JToken aid = PropCI(o, "AssetId");
                        if (aid != null && string.Equals(aid.ToString(), assetId, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
