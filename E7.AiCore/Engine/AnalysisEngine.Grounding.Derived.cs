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
    // AnalysisEngine -- Grounding.Derived.
    // Moved verbatim from AiChatController.Grounding.Derived.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {

        internal JArray SynthDerivedSeries(string formula)
        {
            if (string.IsNullOrEmpty(formula)) { return null; }
            List<string> vars = ExtractFormulaComponents(formula);
            if (vars == null || vars.Count == 0) { return null; }
            // gather each variable's RAW series as sorted (epochSec,value); match by name against
            // captured attrs (case-insensitive, allow the captured key to contain the var or v/v).
            Dictionary<string, List<double[]>> byVar = new Dictionary<string, List<double[]>>(StringComparer.OrdinalIgnoreCase);
            foreach (string v in vars)
            {
                List<object[]> raw = null;
                if (!Sess._trendRawByAttr.TryGetValue(v, out raw))
                {
                    foreach (KeyValuePair<string, List<object[]>> kv in Sess._trendRawByAttr)
                    {
                        string k = kv.Key ?? "";
                        if (k.Equals(v, StringComparison.OrdinalIgnoreCase)
                            || k.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0
                            || v.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                        { raw = kv.Value; break; }
                    }
                }
                if (raw == null) { return null; }   // a variable has no captured series -> cannot synth
                List<double[]> pr = new List<double[]>();
                foreach (object[] pt in raw)
                {
                    double ep = TrendTsToEpoch(new JValue(pt[0].ToString()));
                    double vv; if (!double.TryParse(pt[1].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out vv)) { continue; }
                    if (!double.IsNaN(ep)) { pr.Add(new double[] { ep, vv }); }
                }
                if (pr.Count < 2) { return null; }
                pr.Sort(delegate(double[] a, double[] c) { return a[0].CompareTo(c[0]); });
                byVar[v] = pr;
            }
            // v1.0.152.8 (P0): build a UNION of every driver's timestamps + the incidence and bind
            // as-of at each. Do NOT clock off the largest series -- at its OWN timestamps its age is
            // ~0, so a STALE BASE DRIVER (the 16.5h IR MA case) evaded the 152.7 freshness bound and
            // the series merely ended early instead of gapping. The incidence timestamp forces an
            // as-of (and a freshness test on EVERY driver) at the alert time itself; the union also
            // makes the derived value update whenever ANY input changes.
            double synthMaxAge = ClampCfg(ReadInt("DerivedSynthMaxAgeSec", 21600), 600, 172800);   // 6h default; operator-tunable
            Sess._lastSynthGapped = false; Sess._lastSynthMaxAgeSec = 0; Sess._lastSynthWorstRejAgeSec = 0; Sess._lastSynthEvalGapped = false;
            SortedSet<double> tsSet = new SortedSet<double>();
            foreach (KeyValuePair<string, List<double[]>> kv in byVar) { for (int pi = 0; pi < kv.Value.Count; pi++) { tsSet.Add(kv.Value[pi][0]); } }
            if (Sess._incidenceEpoch > 0) { tsSet.Add(Sess._incidenceEpoch); }
            List<double> uni = new List<double>(tsSet);
            if (uni.Count < 2) { return null; }
            // full null-aware series on the union timeline (value = NaN marks a gap sentinel)
            List<double[]> full = new List<double[]>();
            bool lastValid = false;
            for (int ui = 0; ui < uni.Count; ui++)
            {
                double t = uni[ui];
                Dictionary<string, double> bind = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                bool ok = true; double ptMaxAge = 0; bool staleGap = false; double rejAge = 0;
                foreach (string v in vars)
                {
                    double val, ageSec;
                    if (!AsOfValueAt(byVar[v], t, synthMaxAge, out val, out ageSec))
                    {
                        ok = false;
                        if (!double.IsNaN(ageSec)) { staleGap = true; if (ageSec > rejAge) { rejAge = ageSec; } }   // present but older than the bound
                        break;
                    }
                    if (ageSec > ptMaxAge) { ptMaxAge = ageSec; }
                    bind[v] = val;
                }
                if (!ok)
                {
                    if (staleGap)
                    {
                        Sess._lastSynthGapped = true;
                        if (rejAge > Sess._lastSynthWorstRejAgeSec) { Sess._lastSynthWorstRejAgeSec = rejAge; }   // v1.0.152.8 (P1): the age that CAUSED the gap, not the max ACCEPTED age
                        if (lastValid) { full.Add(new double[] { t, double.NaN }); lastValid = false; }   // one null sentinel at the gap boundary
                    }
                    continue;   // no-sample-yet (before this driver starts) -> skip silently
                }
                double dv;
                if (TryEvalFormula(formula, bind, out dv) && !double.IsNaN(dv) && !double.IsInfinity(dv))
                {
                    if (ptMaxAge > Sess._lastSynthMaxAgeSec) { Sess._lastSynthMaxAgeSec = ptMaxAge; }
                    full.Add(new double[] { t, dv }); lastValid = true;
                }
                else
                {
                    // v1.0.152.9 (P1): a mathematically undefined point (formula eval failed / NaN /
                    // Infinity) after a valid segment must break the line exactly like a stale-driver
                    // rejection -- otherwise valid -> undefined -> valid reconnects across the span.
                    if (lastValid) { full.Add(new double[] { t, double.NaN }); lastValid = false; Sess._lastSynthEvalGapped = true; }
                }
            }
            int validCount = 0; for (int fi = 0; fi < full.Count; fi++) { if (!double.IsNaN(full[fi][1])) { validCount++; } }
            if (validCount < 3) { return null; }
            // downsample: ~cap regular picks PLUS every null gap sentinel (output can slightly
            // exceed cap when gaps exist) so the client can break the line at each gap
            int cap = 240;
            List<int> pick = new List<int>();
            if (full.Count <= cap) { for (int fi = 0; fi < full.Count; fi++) { pick.Add(fi); } }
            else
            {
                HashSet<int> chosen = new HashSet<int>();
                for (int bi = 0; bi < cap; bi++) { int ix = (int)(((long)bi * (full.Count - 1) + (cap - 1) / 2) / (cap - 1)); if (ix > full.Count - 1) { ix = full.Count - 1; } chosen.Add(ix); }
                for (int fi = 0; fi < full.Count; fi++) { if (double.IsNaN(full[fi][1])) { chosen.Add(fi); } }   // always keep gap sentinels
                pick = new List<int>(chosen); pick.Sort();
            }
            JArray outPts = new JArray();
            for (int pk = 0; pk < pick.Count; pk++)
            {
                double[] p = full[pick[pk]];
                DateTimeOffset dto = DateTimeOffset.FromUnixTimeSeconds((long)p[0]);
                string iso = dto.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss");
                if (double.IsNaN(p[1])) { outPts.Add(new JArray(iso, JValue.CreateNull())); }   // explicit null gap marker (client spanGaps:false)
                else { outPts.Add(new JArray(iso, Math.Round(p[1], 4))); }
            }
            return outPts;
        }


        // v1.0.86.0: static, side-effect-free check of whether the card's triggered
        // condition is derived -- usable before the _derivedAlert field is set (the
        // prefetch search_tags call needs it to suppress AttributeName narrowing).
        internal static bool IsDerivedCard(JObject ctx)
        {
            string p, f;
            return DetectDerived(ctx, out p, out f);
        }


        // v1.0.82.0 rev J: is this alert's triggered attribute DERIVED (a formula
        // over component tags, e.g. RRAIL)? Reads triggeredConditions[0]:
        //   "type":"derived"  and/or a "formula" field. Returns the formula (or null)
        // and the paramLabel. Derived attributes have NO telemetry tag of their own.
        internal static bool DetectDerived(JObject ctx, out string param, out string formula)
        {
            param = null; formula = null;
            if (ctx == null) { return false; }
            JToken card = ctx["alertCard"];
            if (card == null || card.Type != JTokenType.Object) { return false; }
            JToken tc = card["triggeredConditions"];
            if (tc == null || tc.Type != JTokenType.Array || ((JArray)tc).Count == 0) { return false; }
            JObject cond = ((JArray)tc)[0] as JObject;
            if (cond == null) { return false; }
            JToken p = cond["paramLabel"];
            if (p != null) { param = p.ToString().Trim(); }
            JToken f = cond["formula"];
            if (f != null && f.Type != JTokenType.Null) { formula = f.ToString(); }
            JToken t = cond["type"];
            bool typed = t != null && string.Equals(t.ToString(), "derived", StringComparison.OrdinalIgnoreCase);
            return typed || !string.IsNullOrEmpty(formula);
        }


        // v1.0.88.0: evaluate a derived formula (the card's own formula string) over the
        // components' as-of values. Grammar is exactly what the formulas use: numbers,
        // component identifiers (aliases), the 4 binary ops + - * /, parentheses, and
        // unary minus. Shunting-yard to RPN, then evaluate. The component map is keyed
        // by the CANONICAL names ExtractFormulaComponents produced; the formula's alias
        // tokens are canonicalised the same way (DomainAttributeName) before lookup, so
        // "ITC FEED END(mA)" in the formula resolves to the same key as the component
        // "If mA". Division-by-zero or any unknown token -> false (no wrong number).
        internal static bool TryEvalFormula(string formula, Dictionary<string, double> vals, out double result)
        {
            result = double.NaN;
            if (string.IsNullOrEmpty(formula)) { return false; }
            string rhs = formula;
            int eq = formula.IndexOf('=');
            if (eq >= 0 && eq + 1 < formula.Length) { rhs = formula.Substring(eq + 1); }
            // tokenize: numbers, identifiers (letters/digits/space/slash/dot), operators, parens
            List<string> toks = new List<string>();
            int i = 0; int n = rhs.Length;
            while (i < n)
            {
                char c = rhs[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '(' || c == ')' || c == '+' || c == '-' || c == '*' || c == '/')
                { toks.Add(c.ToString()); i++; continue; }
                if (char.IsDigit(c) || c == '.')
                {
                    int j = i; while (j < n && (char.IsDigit(rhs[j]) || rhs[j] == '.')) { j++; }
                    toks.Add(rhs.Substring(i, j - i)); i = j; continue;
                }
                if (char.IsLetter(c))
                {
                    int j = i;
                    while (j < n && (char.IsLetterOrDigit(rhs[j]) || rhs[j] == ' ' || rhs[j] == '/' || rhs[j] == '.'))
                    { j++; }
                    // trim a trailing "(unit)" that may have been attached
                    string id = rhs.Substring(i, j - i).Trim();
                    // if the identifier is immediately followed by "(x)" unit, skip it
                    if (j < n && rhs[j] == '(')
                    {
                        int k = rhs.IndexOf(')', j);
                        if (k > j && k - j <= 6) { j = k + 1; }   // short (unit) only
                    }
                    toks.Add("$" + id); i = j; continue;
                }
                return false;   // unexpected char
            }
            // shunting-yard
            List<string> outq = new List<string>();
            Stack<string> ops = new Stack<string>();
            string prev = null;
            for (int t = 0; t < toks.Count; t++)
            {
                string tok = toks[t];
                if (tok.StartsWith("$"))
                {
                    string key = DomainAttributeName(tok.Substring(1));
                    double dv;
                    if (key != null && vals.TryGetValue(key, out dv)) { outq.Add(dv.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
                    else if (vals.TryGetValue(tok.Substring(1), out dv)) { outq.Add(dv.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
                    else { return false; }   // unknown component -> cannot compute
                    prev = "num"; continue;
                }
                double num;
                if (double.TryParse(tok, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out num))
                { outq.Add(tok); prev = "num"; continue; }
                if (tok == "(") { ops.Push(tok); prev = "("; continue; }
                if (tok == ")")
                {
                    while (ops.Count > 0 && ops.Peek() != "(") { outq.Add(ops.Pop()); }
                    if (ops.Count == 0) { return false; }
                    ops.Pop(); prev = "num"; continue;
                }
                // operator; detect unary minus (start, after '(' or after another operator)
                bool unary = tok == "-" && (prev == null || prev == "(" || prev == "op");
                string op = unary ? "u-" : tok;
                while (ops.Count > 0 && ops.Peek() != "(" && Prec(ops.Peek()) >= Prec(op) && op != "u-")
                { outq.Add(ops.Pop()); }
                ops.Push(op); prev = "op"; continue;
            }
            while (ops.Count > 0) { string op = ops.Pop(); if (op == "(") { return false; } outq.Add(op); }
            // evaluate RPN
            Stack<double> st = new Stack<double>();
            for (int t = 0; t < outq.Count; t++)
            {
                string tok = outq[t];
                double num;
                if (double.TryParse(tok, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out num))
                { st.Push(num); continue; }
                if (tok == "u-") { if (st.Count < 1) { return false; } st.Push(-st.Pop()); continue; }
                if (st.Count < 2) { return false; }
                double b = st.Pop(); double a = st.Pop();
                if (tok == "+") { st.Push(a + b); }
                else if (tok == "-") { st.Push(a - b); }
                else if (tok == "*") { st.Push(a * b); }
                else if (tok == "/") { if (Math.Abs(b) < 1e-12) { return false; } st.Push(a / b); }
                else { return false; }
            }
            if (st.Count != 1) { return false; }
            double r = st.Pop();
            if (double.IsNaN(r) || double.IsInfinity(r)) { return false; }
            result = r; return true;
        }


        internal static int Prec(string op)
        {
            if (op == "u-") { return 3; }
            if (op == "*" || op == "/") { return 2; }
            if (op == "+" || op == "-") { return 1; }
            return 0;
        }


        // v1.0.90.0: render a COMPACT, human-readable window of a component's history
        // around the incidence, instead of dumping raw epoch arrays the model can't read.
        // Since 1.0.88 the controller already computes+supplies the as-of value, age, and
        // the derived value, so the model no longer needs the full 300-sample series -- it
        // needs a short readable trend around the incidence (to judge development/recovery).
        // Emits up to `keep` samples straddling the incidence: the last few BEFORE and the
        // first few AFTER, as "HH:MM:SS=value". Falls back to empty on parse failure (the
        // reduced raw tail still follows as a safety net).
        internal static string CompactWindow(string histJson, DateTime incidence, int keep)
        {
            if (string.IsNullOrEmpty(histJson)) { return ""; }
            try
            {
                JObject o = JObject.Parse(histJson);
                JToken tsT = o["ts"]; JToken vT = o["v"];
                if (tsT == null || vT == null || tsT.Type != JTokenType.Array || vT.Type != JTokenType.Array)
                { return ""; }
                JArray ts = (JArray)tsT; JArray v = (JArray)vT;
                int n = Math.Min(ts.Count, v.Count);
                if (n == 0) { return ""; }
                long inc = ToUnixSeconds(incidence);
                // find split: last index with ts <= incidence
                int split = -1;
                long[] tt = new long[n]; string[] vv = new string[n];
                for (int i = 0; i < n; i++)
                {
                    long t; long.TryParse(ts[i].ToString(), out t); tt[i] = t;
                    vv[i] = v[i].ToString();
                    if (t <= inc) { split = i; }
                }
                int half = keep / 2;
                int startB = Math.Max(0, (split < 0 ? 0 : split) - half + 1);
                int endA = Math.Min(n - 1, (split < 0 ? -1 : split) + half);
                StringBuilder w = new StringBuilder();
                for (int i = startB; i <= endA && i < n; i++)
                {
                    if (i < 0) { continue; }
                    DateTimeOffset dto = DateTimeOffset.FromUnixTimeSeconds(tt[i]).ToOffset(TimeSpan.FromHours(5.5));
                    w.Append(dto.ToString("HH:mm:ss")).Append("=").Append(vv[i]);
                    if (i == split) { w.Append("<-as-of"); }
                    w.Append("  ");
                }
                return w.ToString().TrimEnd();
            }
            catch { return ""; }
        }


        // v1.0.88.0: COV-correct as-of extraction. The history API is change-of-value:
        // a row appears only when the value CHANGES. If nothing changed in the window,
        // it returns the last-known value as bracket rows (confirmed empirically: a
        // 10:00-11:00 request returned n=2 identical rows both stamped 05:17 -- the
        // steady value carried forward). So "no row inside the window" means STEADY,
        // not missing. This parses the columnar {ts,v} and returns the value of the
        // last sample with ts <= incidence (the as-of / carried-forward value), its
        // age in seconds, whether the window was bracket-only (steady), and how many
        // rows fell strictly inside the window. Returns false only when NO row at all
        // is <= incidence (the single genuine no-data case).
        // v1.0.109.0: mean of a columnar {ts:[],v:[]} history series - used as a component's
        // baseline for the derived_inputs drift %. Parses the same shape AsOfValue does.
        internal static bool SeriesMean(string histJson, out double mean)
        {
            mean = double.NaN;
            if (string.IsNullOrEmpty(histJson)) { return false; }
            try
            {
                JObject o = JObject.Parse(histJson);
                JToken vT = o["v"];
                if (vT == null || vT.Type != JTokenType.Array) { return false; }
                JArray v = (JArray)vT;
                // v1.0.160.101: MEDIAN, not mean. A track circuit alternates between resting and
                // train-shunted; the mean of both modes describes neither, and comparing a resting
                // at-alert value against it manufactures deviations -- and can invert their sign.
                List<double> acc = new List<double>();
                for (int i = 0; i < v.Count; i++)
                {
                    double val;
                    if (double.TryParse(v[i].ToString(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out val))
                    { acc.Add(val); }
                }
                if (acc.Count == 0) { return false; }
                acc.Sort();
                int m = acc.Count;
                mean = (m % 2 == 1) ? acc[m / 2] : (acc[(m / 2) - 1] + acc[m / 2]) / 2.0;
                return true;
            }
            catch { return false; }
        }


        internal static bool AsOfValue(string histJson, DateTime incidence,
            out double asOf, out long ageSec, out bool bracketOnly, out int inWindow, out int nRows)
        {
            asOf = double.NaN; ageSec = -1; bracketOnly = false; inWindow = 0; nRows = 0;
            if (string.IsNullOrEmpty(histJson)) { return false; }
            try
            {
                JObject o = JObject.Parse(histJson);
                JToken tsT = o["ts"]; JToken vT = o["v"];
                if (tsT == null || vT == null || tsT.Type != JTokenType.Array || vT.Type != JTokenType.Array)
                { return false; }
                JArray ts = (JArray)tsT; JArray v = (JArray)vT;
                nRows = Math.Min(ts.Count, v.Count);
                if (nRows == 0) { return false; }
                long incEpoch = ToUnixSeconds(incidence);
                long fromEpoch = o["from"] != null ? (long)o["from"] : long.MinValue;
                long asOfTs = long.MinValue; double asOfVal = double.NaN; bool have = false;
                for (int i = 0; i < nRows; i++)
                {
                    long t; double val;
                    if (!long.TryParse(ts[i].ToString(), out t)) { continue; }
                    if (!double.TryParse(v[i].ToString(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out val)) { continue; }
                    if (fromEpoch != long.MinValue && t > fromEpoch) { inWindow++; }
                    if (t <= incEpoch && (!have || t >= asOfTs)) { asOfTs = t; asOfVal = val; have = true; }
                }
                if (!have) { return false; }   // no value at/before incidence = true no-data
                asOf = asOfVal;
                ageSec = incEpoch - asOfTs;
                // bracket-only (steady): the caller's detection rule -- few rows AND all
                // rows at/before the window start (i.e. none strictly inside the window).
                bracketOnly = (inWindow == 0);
                return true;
            }
            catch { return false; }
        }


        // v1.0.160.103: resolve the configured operating bounds for (assetId, attid) from the
        // get_attribute_range payload. Handles BOTH the compact NarrowAttributeRange shape
        // (rows[]: a/t/min/max/fail) and the raw shape (AssetId/AssetAttributeId/MinSafeValue/
        // MaxSafeValue/MinFailValue), matching on BOTH asset and attribute. minFail is NaN when the
        // configured value is null (caller applies a floor). Returns false -> fail closed.
        internal static bool RangeBoundsForAsset(string rangeJson, string assetId, int attid, out double minSafe, out double maxSafe, out double minFail)
        {
            minSafe = double.NaN; maxSafe = double.NaN; minFail = double.NaN;
            if (string.IsNullOrEmpty(rangeJson) || attid < 0) { return false; }
            try
            {
                List<JToken> docs = ParseJsonDocuments(rangeJson);
                for (int d = 0; d < docs.Count; d++)
                {
                    foreach (JToken row in docs[d].SelectTokens("$..*"))
                    {
                        JObject o = row as JObject;
                        if (o == null) { continue; }
                        JToken tTok = PropCI(o, "t") ?? PropCI(o, "AssetAttributeId");
                        if (tTok == null) { continue; }
                        int rowAttid;
                        if (!int.TryParse(tTok.ToString(), out rowAttid) || rowAttid != attid) { continue; }
                        JToken aTok = PropCI(o, "a") ?? PropCI(o, "AssetId");
                        // Fix 5: match on BOTH asset and attribute. A row without a resolvable AssetId
                        // is NOT accepted (fail closed) so bounds are never taken from another asset.
                        if (aTok == null || (!string.IsNullOrEmpty(assetId) && aTok.ToString() != assetId)) { continue; }
                        JToken maxTok = PropCI(o, "max") ?? PropCI(o, "MaxSafeValue");
                        JToken minTok = PropCI(o, "min") ?? PropCI(o, "MinSafeValue");
                        JToken failTok = PropCI(o, "fail") ?? PropCI(o, "MinFailValue");
                        double mx, mn;
                        if (maxTok == null || !double.TryParse(maxTok.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out mx)) { continue; }
                        if (minTok == null || !double.TryParse(minTok.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out mn)) { continue; }
                        maxSafe = mx; minSafe = mn;
                        double fl;
                        if (failTok != null && failTok.Type != JTokenType.Null && double.TryParse(failTok.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fl)) { minFail = fl; }
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }


        // v1.0.160.103: a value is IN-ENVELOPE when it lies within the configured operating band.
        // When MinFail is not configured, the floor is half of MinSafe. This excludes track-shunt
        // samples in BOTH directions (current rises above MaxSafe, voltage/relay current falls below
        // the floor) without any TPR timestamp alignment.
        internal static bool InEnvelope(double v, double minSafe, double maxSafe, double minFail)
        {
            double floor = !double.IsNaN(minFail) ? minFail : (minSafe * 0.5);
            return v >= floor && v <= maxSafe;
        }


        // v1.0.160.103: the movement-panel comparator. Selects the at-alert sample (latest ts <=
        // anchor; ALWAYS returned, never envelope-gated) and, for track assets, the latest earlier
        // sample whose value is IN-ENVELOPE within maxAge (measured from the at-alert sample). For
        // non-track assets the reference is simply the immediately-preceding sample. Fails closed
        // (reason) when the envelope is unavailable, when no comparable reference exists, or when the
        // reference is zero. Parses the columnar {ts:[],v:[]} shape AsOfValue uses.
        internal static DerivedCompare PrevInEnvelopeSample(string cHist, long anchorEpoch, long incidenceEpoch, bool trackAsset,
            bool haveBounds, double minSafe, double maxSafe, double minFail, long maxAgeSec)
        {
            if (string.IsNullOrEmpty(cHist)) { return null; }
            try
            {
                JObject o = JObject.Parse(cHist);
                JToken tsT = o["ts"]; JToken vT = o["v"];
                if (tsT == null || vT == null || tsT.Type != JTokenType.Array || vT.Type != JTokenType.Array) { return null; }
                JArray ts = (JArray)tsT; JArray v = (JArray)vT;
                int nRows = Math.Min(ts.Count, v.Count);
                if (nRows == 0) { return null; }
                List<long> rt = new List<long>();
                List<double> rv = new List<double>();
                for (int i = 0; i < nRows; i++)
                {
                    long t; double val;
                    if (!long.TryParse(ts[i].ToString(), out t)) { continue; }
                    if (!double.TryParse(v[i].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val)) { continue; }
                    rt.Add(t); rv.Add(val);
                }
                if (rt.Count == 0) { return null; }
                // sort ascending by ts (the source may be desc)
                int[] idx = new int[rt.Count];
                for (int i = 0; i < idx.Length; i++) { idx[i] = i; }
                Array.Sort(idx, delegate(int a, int b) { return rt[a].CompareTo(rt[b]); });
                // at-alert = latest ts <= anchor
                int atPos = -1;
                for (int i = 0; i < idx.Length; i++)
                {
                    if (rt[idx[i]] <= anchorEpoch) { atPos = i; }
                    else { break; }
                }
                if (atPos < 0) { return null; }
                DerivedCompare dc = new DerivedCompare();
                dc.AtTs = rt[idx[atPos]];
                dc.AtVal = rv[idx[atPos]];
                dc.AtAgeSec = anchorEpoch - dc.AtTs;   // anchor-relative staleness of the DISPLAYED value
                // v1.0.160.109 CUT A (SL 1): fail closed on a stale at-alert sample. Cut A's sort fix
                // removes the cause seen in 543194, but a GENUINE data outage (e.g. the real 8 h
                // Charger mA gap on 11 Aug) can still leave nothing near the anchor. Saying "no
                // comparable reading" is correct; publishing a movement from day-old data is not.
                if (dc.AtAgeSec > (long)_atSampleMaxAgeMin * 60L)
                {
                    dc.StaleAtSample = true;
                    dc.Kind = "no_recent_sample";
                }
                dc.Kind = trackAsset ? "in_envelope" : "previous_sample";
                if (trackAsset && !haveBounds)
                {
                    dc.Reason = "configured envelope unavailable";
                    return dc;
                }
                if (trackAsset)
                {
                    dc.CurrentOutside = !InEnvelope(dc.AtVal, minSafe, maxSafe, minFail);
                }
                // reference = latest EARLIER sample; track -> must be in-envelope; within maxAge of at-alert
                int refPos = -1;
                for (int i = atPos - 1; i >= 0; i--)
                {
                    long candTs = rt[idx[i]];
                    // Fix 4: enforce strict candTs < atTs -- duplicate timestamps must never become a
                    // same-time reference (which would print a spurious 0% or an invalid comparison).
                    if (candTs >= dc.AtTs) { continue; }
                    if (dc.AtTs - candTs > maxAgeSec) { break; }
                    double candV = rv[idx[i]];
                    if (trackAsset && !InEnvelope(candV, minSafe, maxSafe, minFail)) { continue; }
                    refPos = i;
                    break;
                }
                if (refPos < 0)
                {
                    dc.Reason = "no recent comparable reading";
                    return dc;
                }
                dc.HasRef = true;
                dc.RefTs = rt[idx[refPos]];
                dc.RefVal = rv[idx[refPos]];
                dc.RefAgeSec = incidenceEpoch - dc.RefTs;   // incidence-relative age (design schema: incidence - referenceTs)
                if (Math.Abs(dc.RefVal) < 1e-9)
                {
                    dc.Reason = "previous comparable reading is zero; percentage unavailable";
                    return dc;
                }
                dc.HasDelta = true;
                dc.DeltaPct = (dc.AtVal - dc.RefVal) / Math.Abs(dc.RefVal) * 100.0;
                return dc;
            }
            catch { return null; }
        }


        // Unix seconds for a DateTime treated as IST wall-clock (history ts are IST epoch).
        internal static long ToUnixSeconds(DateTime ist)
        {
            DateTimeOffset dto = new DateTimeOffset(
                DateTime.SpecifyKind(ist, DateTimeKind.Unspecified),
                TimeSpan.FromHours(5.5));
            return dto.ToUnixTimeSeconds();
        }


        // v1.0.82.0 rev J: pull the COMPONENT attribute canonical names out of a
        // derived formula. Tokenizes identifier-ish runs (letters/digits/spaces,
        // optional "(unit)") and keeps those DomainAttributeName can canonicalise --
        // e.g. "RRAIL(ohm) = 2*(VTC FEED END(V) - VTC TR) / ((ITC FEED END(mA) +
        // ITC RELAY END(mA))/1000)" -> VF, TR V, IF MA, IR MA. The derived param
        // itself maps to null (no rule) and numeric/unit tokens are filtered out.
        // v1.0.160.30 (Item 4c): the PINNED base-sensor inputs for each derived attid, by CANONICAL
        // name (matches DomainAttributeName output, so they resolve against search_tags reliably --
        // the prose formula's short forms ChgOpV/Vf/If do NOT resolve, which is why the inputs were
        // silently dropped and the derived value came back uncomputable). R737-dependent attids
        // (589/590/591) additionally use TR_V571 = Ir * R737 / 1000, computed in ComputeDerivedDirect.
        internal static List<string> DerivedBaseInputs(int attid)
        {
            switch (attid)
            {
                case 585: return new List<string> { "CHARGER MA", "IF MA" };                 // ITC BATT CHARG = ChgMa - If
                case 586: return new List<string> { "CHARGER OP V", "VF", "CHOKE V" };        // VTC VAR RES = ChgOpV - Vf - Choke
                case 587: return new List<string> { "CHOKE V", "IF MA" };                     // RTC CH FEED END = Choke/If*1000
                case 588: return new List<string> { "CHARGER OP V", "VF", "CHOKE V", "IF MA" }; // RTC VAR RES = (ChgOpV-Vf-Choke)/If*1000
                case 589: return new List<string> { "VR", "IR MA" };                          // RTC CH RELAY END (+ R737)
                case 590: return new List<string> { "VF", "IF MA", "IR MA" };                 // RRAIL (+ R737)
                case 591: return new List<string> { "VR", "IR MA" };                          // VTC CH RELAY END (+ R737)
                case 684: return new List<string> { "IF MA", "IR MA" };                       // IBALST = If - Ir
                default: return null;
            }
        }


        internal static bool DerivedNeedsR737(int attid) { return attid == 589 || attid == 590 || attid == 591; }


        // Extract R737 (attid 737, Track Relay resistance) from the fetched FRS range JSON = the
        // AverageValue of the attid-737 row. R737 is a fixed per-asset constant; NEVER the measured
        // 571 sensor. Returns null if absent (then the R737-dependent derived attids fall back).
        internal static double? RangeAvgForAttid(string rangeJson, int attid)
        {
            if (string.IsNullOrEmpty(rangeJson)) { return null; }
            try
            {
                List<JToken> docs = ParseJsonDocuments(rangeJson);
                for (int d = 0; d < docs.Count; d++)
                {
                    if (docs[d] == null) { continue; }
                    foreach (JToken row in docs[d].SelectTokens("$..*"))
                    {
                        JObject o = row as JObject;
                        if (o == null) { continue; }
                        JToken att = PropCI(o, "AssetAttributeId");
                        if (att == null) { att = PropCI(o, "AttributeId"); }
                        if (att == null) { att = PropCI(o, "Id"); }
                        if (att == null) { att = PropCI(o, "t"); }
                        if (att == null) { continue; }
                        int aid;
                        if (!int.TryParse(att.ToString().Trim(), out aid) || aid != attid) { continue; }
                        JToken av = PropCI(o, "AverageValue");
                        if (av == null) { av = PropCI(o, "avg"); }
                        if (av == null) { av = PropCI(o, "Average"); }
                        if (av == null) { continue; }
                        double v;
                        if (double.TryParse(av.ToString().Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v)) { return v; }
                    }
                }
            }
            catch { }
            return null;
        }


        // Compute a derived attribute directly from the base-sensor as-of values (keyed by canonical
        // name) + R737. Hardcoded from RdpmsMaps.DerivedFormula (no prose parsing, no drift risk).
        // Returns false when a required input is missing or a divisor is ~0 (caller then keeps the
        // existing behaviour). TR_V571 (the derived track-relay voltage) = Ir * R737 / 1000.
        internal static bool ComputeDerivedDirect(int attid, Dictionary<string, double> c, double? r737, out double val)
        {
            val = double.NaN;
            if (c == null) { return false; }
            double If, Ir, Vf, Vr, Choke, ChgMa, ChgOpV;
            bool hIf = c.TryGetValue("IF MA", out If);
            bool hIr = c.TryGetValue("IR MA", out Ir);
            bool hVf = c.TryGetValue("VF", out Vf);
            bool hVr = c.TryGetValue("VR", out Vr);
            bool hChoke = c.TryGetValue("CHOKE V", out Choke);
            bool hChgMa = c.TryGetValue("CHARGER MA", out ChgMa);
            bool hChgOpV = c.TryGetValue("CHARGER OP V", out ChgOpV);
            double trV = 0.0; bool hTr = false;
            if (r737.HasValue && hIr) { trV = Ir * r737.Value / 1000.0; hTr = true; }   // derived TR_V571
            switch (attid)
            {
                case 585: if (hChgMa && hIf) { val = ChgMa - If; return true; } return false;
                case 586: if (hChgOpV && hVf && hChoke) { val = ChgOpV - Vf - Choke; return true; } return false;
                case 587: if (hChoke && hIf && Math.Abs(If) > 1e-9) { val = (Choke / If) * 1000.0; return true; } return false;
                case 588: if (hChgOpV && hVf && hChoke && hIf && Math.Abs(If) > 1e-9) { val = ((ChgOpV - Vf - Choke) / If) * 1000.0; return true; } return false;
                case 684: if (hIf && hIr) { val = If - Ir; return true; } return false;
                case 589: if (hTr && hVr && hIr && Math.Abs(Ir) > 1e-9) { val = ((Vr - trV) / Ir) * 1000.0; return true; } return false;
                case 590: if (hTr && hVf && hIf && hIr && Math.Abs(If + Ir) > 1e-9) { val = 2.0 * (Vf - trV) / (If + Ir) * 1000.0; return true; } return false;
                case 591: if (hTr && hVr) { val = Vr - trV; return true; } return false;
                default: return false;
            }
        }


        // v1.0.160.31 (Item 4b): parse a history_get result {"ts":[epochSec..],"v":[..]} into a
        // sorted (epochSec, value) series.
        internal static List<double[]> ParseTsvSeries(string histJson)
        {
            List<double[]> outp = new List<double[]>();
            if (string.IsNullOrEmpty(histJson)) { return outp; }
            try
            {
                JObject o = JObject.Parse(histJson);
                JToken tsT = o["ts"]; JToken vT = o["v"];
                if (tsT == null || vT == null || tsT.Type != JTokenType.Array || vT.Type != JTokenType.Array) { return outp; }
                JArray ts = (JArray)tsT; JArray v = (JArray)vT;
                int n = Math.Min(ts.Count, v.Count);
                for (int i = 0; i < n; i++)
                {
                    double ep, vv;
                    if (!double.TryParse(ts[i].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out ep)) { continue; }
                    if (ep > 1e12) { ep = ep / 1000.0; }   // ms -> s
                    if (!double.TryParse(v[i].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out vv)) { continue; }
                    outp.Add(new double[] { ep, vv });
                }
                outp.Sort(delegate(double[] a, double[] b) { return a[0].CompareTo(b[0]); });
            }
            catch { }
            return outp;
        }


        // carry-forward as-of: last value at or before t in a sorted series. False if none <= t.
        internal static bool SeriesAsOf(List<double[]> series, double t, out double val)
        {
            val = double.NaN;
            if (series == null || series.Count == 0) { return false; }
            bool have = false;
            for (int i = 0; i < series.Count; i++)
            {
                if (series[i][0] <= t) { val = series[i][1]; have = true; } else { break; }
            }
            return have;
        }


        // v1.0.160.31 (Item 4b): compute the derived value on the UNION timeline of all input series,
        // carrying each input forward (deadband). Returns (epochSec, derivedValue) points.
        internal static List<double[]> ComputeDerivedSeries(int attid, Dictionary<string, List<double[]>> comp, double? r737, double incidenceEpoch)
        {
            List<double[]> outp = new List<double[]>();
            if (comp == null || comp.Count == 0) { return outp; }
            SortedSet<double> tsSet = new SortedSet<double>();
            foreach (KeyValuePair<string, List<double[]>> kv in comp)
            {
                if (kv.Value == null) { continue; }
                for (int i = 0; i < kv.Value.Count; i++) { tsSet.Add(kv.Value[i][0]); }
            }
            if (incidenceEpoch > 0) { tsSet.Add(incidenceEpoch); }   // force an as-of at the alert time
            List<double> uni = new List<double>(tsSet);
            for (int u = 0; u < uni.Count; u++)
            {
                double t = uni[u];
                Dictionary<string, double> bind = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                bool ok = true;
                foreach (KeyValuePair<string, List<double[]>> kv in comp)
                {
                    double vv;
                    if (SeriesAsOf(kv.Value, t, out vv)) { bind[kv.Key] = vv; } else { ok = false; break; }
                }
                if (!ok) { continue; }   // not all inputs known yet at t
                double dv;
                if (ComputeDerivedDirect(attid, bind, r737, out dv)) { outp.Add(new double[] { t, dv }); }
            }
            return outp;
        }


        // v1.0.160.31 (Item 4b): longest continuous run breaching the threshold (upper=true: value >
        // threshold; else value < threshold). Deadband-aware: a breached value persists until the NEXT
        // change, so a run is counted from its start up to the recovery sample (or the last point if it
        // never recovers). Returns the run duration in seconds + its window/peak.
        internal static double LongestBreachRun(List<double[]> series, double threshold, bool upper, out double startTs, out double endTs, out double peak)
        {
            startTs = 0; endTs = 0; peak = upper ? double.MinValue : double.MaxValue;
            if (series == null || series.Count == 0) { return 0.0; }
            double bestDur = 0.0, bestStart = 0, bestEnd = 0, bestPeak = peak;
            double runStart = 0, runPeak = 0; bool inRun = false;
            for (int i = 0; i < series.Count; i++)
            {
                double t = series[i][0], val = series[i][1];
                bool breach = upper ? (val > threshold) : (val < threshold);
                if (breach)
                {
                    if (!inRun) { inRun = true; runStart = t; runPeak = val; }
                    else { runPeak = upper ? Math.Max(runPeak, val) : Math.Min(runPeak, val); }
                }
                else if (inRun)
                {
                    // breach held from runStart until THIS recovery sample (carry-forward).
                    double dur = t - runStart;
                    if (dur >= bestDur) { bestDur = dur; bestStart = runStart; bestEnd = t; bestPeak = runPeak; }
                    inRun = false;
                }
            }
            if (inRun)
            {
                double lastT = series[series.Count - 1][0];   // breach never recovered -> held to the last point
                double dur = lastT - runStart;
                if (dur >= bestDur) { bestDur = dur; bestStart = runStart; bestEnd = lastT; bestPeak = runPeak; }
            }
            startTs = bestStart; endTs = bestEnd; peak = bestPeak;
            return bestDur;
        }


        // v1.0.160.31 (Item 4b): the FRS sustain window ("... for 15s") from the cause-logic string.
        internal static int FrsSustainSeconds(string causeLogic)
        {
            if (string.IsNullOrEmpty(causeLogic)) { return 15; }
            System.Text.RegularExpressions.Match m =
                System.Text.RegularExpressions.Regex.Match(causeLogic, @"for\s+(\d+)\s*s");
            int s;
            if (m.Success && int.TryParse(m.Groups[1].Value, out s) && s > 0) { return s; }
            return 15;
        }


        // generalized numeric field extraction from the FRS range for an attid (avg/max/min/fail).
        internal static double? RangeNumForAttid(string rangeJson, int attid, string[] fields)
        {
            if (string.IsNullOrEmpty(rangeJson) || fields == null) { return null; }
            try
            {
                List<JToken> docs = ParseJsonDocuments(rangeJson);
                for (int d = 0; d < docs.Count; d++)
                {
                    if (docs[d] == null) { continue; }
                    foreach (JToken row in docs[d].SelectTokens("$..*"))
                    {
                        JObject o = row as JObject;
                        if (o == null) { continue; }
                        JToken att = PropCI(o, "AssetAttributeId");
                        if (att == null) { att = PropCI(o, "AttributeId"); }
                        if (att == null) { att = PropCI(o, "Id"); }
                        if (att == null) { att = PropCI(o, "t"); }
                        if (att == null) { continue; }
                        int aid;
                        if (!int.TryParse(att.ToString().Trim(), out aid) || aid != attid) { continue; }
                        for (int f = 0; f < fields.Length; f++)
                        {
                            JToken fv = PropCI(o, fields[f]);
                            if (fv == null) { continue; }
                            double v;
                            if (double.TryParse(fv.ToString().Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v)) { return v; }
                        }
                    }
                }
            }
            catch { }
            return null;
        }


        internal static List<string> ExtractFormulaComponents(string formula)
        {
            List<string> outp = new List<string>();
            if (string.IsNullOrEmpty(formula)) { return outp; }
            // v1.0.84.0: strip the LHS ("RTC CH FEED END(ohm) =") so the derived name
            // itself is not mistaken for a component, then tokenize the RHS.
            string rhs = formula;
            int eq = formula.IndexOf('=');
            if (eq >= 0 && eq + 1 < formula.Length) { rhs = formula.Substring(eq + 1); }
            // v1.0.84.0: allow '/' and '.' INSIDE an attribute name so "ITC TFC O/P"
            // stays one token (the old class split it at '/' into "ITC TFC O" + "P").
            // Operator '/' (division) is always flanked by spaces in these formulas
            // ("... / (ITC FEED END..."), whereas name-internal '/' is not (O/P),
            // so a non-space-delimited slash is treated as part of the name.
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(
                    rhs, @"[A-Za-z][A-Za-z0-9]*(?:[ /.][A-Za-z0-9]+)*(?:\([A-Za-z]+\))?"))
            {
                string tok = m.Value.Trim();
                if (tok.Length < 3) { continue; }
                string canon = DomainAttributeName(tok);
                if (canon == null) { continue; }
                if (!outp.Contains(canon)) { outp.Add(canon); }
            }
            return outp;
        }
    }
}
