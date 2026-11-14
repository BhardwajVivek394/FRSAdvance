using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeCardParser -- reads the alert card into JudgeCard.
    //
    // CHANGE HERE when AlertV3's card format changes (a new key, a new condition shape).
    //
    // Part 1 (Parse) is the AlertJudge v1.1.2.0 CardParser, behaviour for behaviour: the three card
    // shapes (web alert card, AlertV3 ProcessJson, plain scaffold), the same key order, the same
    // TryAdd ("first writer wins") semantics, the same validation list.
    // Part 2 (ParseConditions, new in 2.0) keeps every enabling / triggered condition as a
    // structured object -- label, threshold, comparison, relay state, formula -- for the live
    // re-check. The card's measured value is kept only for display.
    // ============================================================================================
    internal static class JudgeCardParser
    {
        internal static JudgeCard Parse(long alertId, JToken raw, out JudgeCardSummary summary)
        {
            JudgeCardSummary s = new JudgeCardSummary();
            summary = s;
            if (!JudgeUtil.IsObject(raw))
            {
                s.Problems.Add("card is not a JSON object");
                return null;
            }

            JudgeCard c = new JudgeCard();
            c.AlertId = alertId;
            c.Raw = raw.DeepClone();

            JToken meta = JudgeUtil.IsObject(JudgeUtil.Prop(raw, "meta")) ? JudgeUtil.Prop(raw, "meta") : null;
            JToken trig = JudgeUtil.IsObject(JudgeUtil.Prop(raw, "Trigger")) ? JudgeUtil.Prop(raw, "Trigger") : null;

            c.CauseCode = JudgeUtil.Str(raw, "causeCode", "CauseCode", "alertCode", "AlertCode", "cause", "Cause")
                ?? (meta != null ? JudgeUtil.Str(meta, "causeCode") : null)
                ?? (trig != null ? JudgeUtil.Str(trig, "CauseCode") : null)
                ?? "";
            c.CauseId = JudgeUtil.Int(raw, "causeId", "CauseId", "causeCodeId", "CauseCodeId", "cause_id");
            c.CauseTitle = JudgeUtil.Str(raw, "causeTitle", "CauseTitle", "causeName", "title");
            c.SiteId = JudgeUtil.Int(raw, "siteId", "SiteId", "stationId", "StationId") ?? 0;
            c.AssetId = JudgeUtil.Int(raw, "assetId", "AssetId") ?? 0;
            c.AssetName = JudgeUtil.Str(raw, "assetName", "AssetName", "asset");
            c.AssetType = (JudgeUtil.Str(raw, "assetType", "AssetType", "asset_type") ?? InferAssetType(c.CauseCode)).ToUpperInvariant();
            c.AssetTypeId = JudgeUtil.Int(raw, "assetTypeId", "AssetTypeId") ?? 0;
            c.AlertType = (JudgeUtil.Str(raw, "alertType", "AlertType", "type", "kind") ?? "FAILURE").ToUpperInvariant();
            c.PrimaryMetric = JudgeUtil.Str(raw, "attribute", "Attribute", "metric", "Metric", "attributeName", "AttributeName", "assetAttributeName", "AssetAttributeName");
            c.Provider = JudgeUtil.Str(raw, "provider", "Provider");
            c.StationName = JudgeUtil.Str(raw, "stationName", "StationName", "station", "Station");

            DateTimeOffset? trigTs = JudgeUtil.Ts(raw, "triggeredAt", "TriggeredAt", "alertTime", "AlertTime", "SetTime", "setTime", "timestamp", "Timestamp", "time");
            if (trigTs == null)
            {
                s.Problems.Add("triggeredAt missing or unparseable");
            }
            else
            {
                c.TriggeredAt = trigTs.Value;
            }

            // 2.7 -- "resetTime" added: that is what the alert body actually calls it
            // ("resetTime": "08:27:36/28.09.2026"), and it was not in this list, so a body that
            // carried no resetCard object was read as an alert that had never reset.
            c.ResetAt = JudgeUtil.Ts(raw, "resetAt", "ResetAt", "resetTimestamp", "resetTime", "ResetTime");
            JToken rc = JudgeUtil.Prop(raw, "resetCard") ?? JudgeUtil.Prop(raw, "ResetCard");
            if (c.ResetAt == null && JudgeUtil.IsObject(rc))
            {
                c.ResetAt = JudgeUtil.Ts(rc, "resetAt", "resetTimestamp", "timestamp", "Timestamp");
            }

            // 2.7 -- and keep the reset card's MEASUREMENTS, not just its timestamp. They are the
            // other end of the event, recorded by the alert service itself, and they are the only
            // evidence available for an alert too old for the historian. See JudgeResetCheck.
            if (JudgeUtil.IsObject(rc))
            {
                ParseConditionsInto(rc, c.ResetConditions);
                c.HasResetCard = c.ResetConditions.Count > 0;
            }

            // --- values ---------------------------------------------------------------
            foreach (string key in new[] { "values", "Values", "metrics", "Metrics" })
            {
                JToken v = JudgeUtil.Prop(raw, key);
                if (JudgeUtil.IsObject(v))
                {
                    foreach (JProperty p in ((JObject)v).Properties())
                    {
                        double d;
                        if (JudgeUtil.TryNum(p.Value, out d))
                        {
                            c.Values[p.Name] = d;
                        }
                    }
                }
            }

            // --- thresholds -----------------------------------------------------------
            foreach (string key in new[] { "thresholds", "Thresholds", "threshold", "Threshold" })
            {
                JToken t = JudgeUtil.Prop(raw, key);
                if (JudgeUtil.IsObject(t))
                {
                    foreach (JProperty p in ((JObject)t).Properties())
                    {
                        double d;
                        if (JudgeUtil.TryNum(p.Value, out d))
                        {
                            c.Thresholds[p.Name] = d;
                        }
                    }
                }
            }

            // --- relays ---------------------------------------------------------------
            foreach (string key in new[] { "relayStatus", "RelayStatus", "relays", "Relays" })
            {
                JToken r = JudgeUtil.Prop(raw, key);
                if (JudgeUtil.IsObject(r))
                {
                    foreach (JProperty p in ((JObject)r).Properties())
                    {
                        double d;
                        if (JudgeUtil.TryNum(p.Value, out d))
                        {
                            c.Relays[p.Name] = d >= 0.5 ? 1 : 0;
                        }
                        else if (p.Value.Type == JTokenType.Boolean)
                        {
                            c.Relays[p.Name] = p.Value.Value<bool>() ? 1 : 0;
                        }
                    }
                }
            }

            // --- attribute ids (driver tag set) ---------------------------------------
            foreach (string key in new[] { "attributeIds", "AttributeIds", "driverTags", "tags" })
            {
                JToken a = JudgeUtil.Prop(raw, key);
                if (JudgeUtil.IsObject(a))
                {
                    foreach (JProperty p in ((JObject)a).Properties())
                    {
                        int i;
                        if (JudgeUtil.TryInt32(p.Value, out i))
                        {
                            c.AttributeIds[p.Name] = i;
                        }
                    }
                }
            }

            // --- AlertV3 ProcessJson: Trigger{Value, Threshold, Thresholds[], Gates[]} + Attributes[] ---
            if (trig != null)
            {
                string attr = JudgeUtil.Str(trig, "Attribute");
                double tval;
                if (attr != null && JudgeUtil.TryNum(JudgeUtil.Prop(trig, "Value"), out tval))
                {
                    TryAdd(c.Values, attr, tval);
                    TryAdd(c.Values, attr.Replace('_', ' '), tval);
                }

                string tcmp = JudgeUtil.Str(trig, "Comparison");
                if (attr != null && tcmp != null)
                {
                    TryAdd(c.Comparisons, attr, tcmp);
                    TryAdd(c.Comparisons, attr.Replace('_', ' '), tcmp);
                }

                JToken ths = JudgeUtil.Prop(trig, "Thresholds");
                if (JudgeUtil.IsArray(ths))
                {
                    foreach (JToken th in ths)
                    {
                        double thd;
                        if (JudgeUtil.TryNum(JudgeUtil.Prop(th, "value"), out thd))
                        {
                            AddThreshold(c, JudgeUtil.Str(th, "label") ?? "", thd);
                        }
                    }
                }

                JToken gs = JudgeUtil.Prop(trig, "Gates");
                if (JudgeUtil.IsArray(gs))
                {
                    foreach (JToken g in gs)
                    {
                        string gname = JudgeUtil.Str(g, "name");
                        string gstate = JudgeUtil.Str(g, "state");
                        if (gname != null && gstate != null)
                        {
                            c.Relays[gname] = JudgeUtil.RelayBit(gstate);
                        }
                    }
                }
            }

            JToken atts = JudgeUtil.Prop(raw, "Attributes");
            if (JudgeUtil.IsArray(atts))
            {
                foreach (JToken a in atts)
                {
                    string an = JudgeUtil.Str(a, "attributeName");
                    if (an == null)
                    {
                        continue;
                    }

                    int aidv;
                    if (JudgeUtil.TryInt32(JudgeUtil.Prop(a, "attributeId"), out aidv))
                    {
                        TryAdd(c.AttributeIds, an, aidv);
                    }

                    DateTimeOffset? at = JudgeUtil.Ts(a, "timestamp", "Timestamp", "timestampDevice");
                    if (at != null)
                    {
                        TryAdd(c.ValueTimes, an, at.Value);
                    }

                    double avd;
                    if (JudgeUtil.TryNum(JudgeUtil.Prop(a, "value"), out avd))
                    {
                        if (JudgeUtil.IsRelayName(an) && Math.Abs(avd) <= 1)
                        {
                            TryAdd(c.Relays, an, avd >= 0.5 ? 1 : 0);
                        }
                        else
                        {
                            TryAdd(c.Values, an, avd);
                        }
                    }
                }
            }

            // --- conditions: strings (scaffold) OR objects (web card) -----------------------------------
            foreach (string key in new[] { "enablingConditions", "EnablingConditions" })
            {
                JToken ec = JudgeUtil.Prop(raw, key);
                if (!JudgeUtil.IsArray(ec))
                {
                    continue;
                }

                foreach (JToken e in ec)
                {
                    if (e.Type == JTokenType.String)
                    {
                        string str = (string)e;
                        c.EnablingConditions.Add(str);
                        MineCondition(str, c);
                    }
                    else if (JudgeUtil.IsObject(e))
                    {
                        string label = JudgeUtil.Str(e, "label") ?? "";
                        string val = JudgeUtil.Str(e, "value") ?? "";
                        c.EnablingConditions.Add(label + ": " + val);
                        if (label != "" && val != "")
                        {
                            TryAdd(c.Relays, label, JudgeUtil.RelayBit(val));
                            TryAdd(c.Comparisons, label, JudgeUtil.RelayBit(val) == 1 ? "UP" : "DN");
                        }

                        DateTimeOffset? et = JudgeUtil.Ts(e, "time");
                        if (et != null && label != "")
                        {
                            TryAdd(c.ValueTimes, label, et.Value);
                        }
                    }
                }
            }

            foreach (string key in new[] { "triggeredConditions", "TriggeredConditions" })
            {
                JToken tc = JudgeUtil.Prop(raw, key);
                if (!JudgeUtil.IsArray(tc))
                {
                    continue;
                }

                foreach (JToken e in tc)
                {
                    if (e.Type == JTokenType.String)
                    {
                        string str = (string)e;
                        c.TriggeredConditions.Add(str);
                        MineCondition(str, c);
                    }
                    else if (JudgeUtil.IsObject(e))
                    {
                        string label = JudgeUtil.Str(e, "paramLabel") ?? "";
                        string meas = JudgeUtil.Str(e, "measured");
                        c.TriggeredConditions.Add(label + " = " + meas);
                        DateTimeOffset? tt = JudgeUtil.Ts(e, "time");
                        if (tt != null && label != "")
                        {
                            TryAdd(c.ValueTimes, label, tt.Value);
                            TryAdd(c.ValueTimes, JudgeUtil.StripUnit(label), tt.Value);
                        }

                        JToken thc = JudgeUtil.Prop(e, "thresholds");
                        if (JudgeUtil.IsArray(thc))
                        {
                            foreach (JToken th in thc)
                            {
                                string cmpv = JudgeUtil.Str(th, "comparison");
                                if (cmpv != null)
                                {
                                    TryAdd(c.Comparisons, label, cmpv);
                                    TryAdd(c.Comparisons, JudgeUtil.StripUnit(label), cmpv);
                                    break;
                                }
                            }
                        }

                        // 2.4 -- a triggered condition of type "relay" carries a STATE, not a
                        // number: {"type":"relay","paramLabel":"NWKR","measured":"DROP"}. It has to
                        // reach c.Relays, or JudgeVerifier.CardNameFor cannot find the catalogue's
                        // NWKR / RWKR attribute on the card at all -- which is what left alert
                        // 1280129 (PT N OBS) with nothing recognised and an empty mismatch remark.
                        string ctype = (JudgeUtil.Str(e, "type") ?? "main").ToLowerInvariant();
                        if (ctype == "relay" && label != "" && !string.IsNullOrWhiteSpace(meas))
                        {
                            int rbit = JudgeUtil.RelayBit(meas);
                            TryAdd(c.Relays, label, rbit);
                            TryAdd(c.Relays, JudgeUtil.StripUnit(label), rbit);
                            TryAdd(c.Comparisons, label, rbit == 1 ? "UP" : "DN");
                        }

                        double md;
                        if (label != "" && meas != null && double.TryParse(meas, NumberStyles.Float, CultureInfo.InvariantCulture, out md))
                        {
                            TryAdd(c.Values, label, md);
                            string bare = JudgeUtil.StripUnit(label);
                            if (bare != label)
                            {
                                TryAdd(c.Values, bare, md);
                            }
                        }

                        if (JudgeUtil.IsArray(thc))
                        {
                            foreach (JToken th in thc)
                            {
                                double thd;
                                if (JudgeUtil.TryNum(JudgeUtil.Prop(th, "value"), out thd))
                                {
                                    AddThreshold(c, JudgeUtil.Str(th, "label") ?? "", thd);
                                }
                            }
                        }
                    }
                }
            }

            // --- 2.0: structured conditions for the live re-check --------------------------------------
            ParseConditions(raw, meta, c);

            // --- validation -----------------------------------------------------------
            if (string.IsNullOrWhiteSpace(c.CauseCode) && c.CauseId == null)
            {
                s.Problems.Add("causeCode / causeId missing");
            }

            if (c.AssetId == 0)
            {
                s.Problems.Add("assetId missing");
            }

            if (c.SiteId == 0)
            {
                s.Problems.Add("siteId missing");
            }

            if (string.IsNullOrWhiteSpace(c.AssetType))
            {
                s.Problems.Add("assetType missing");
            }

            if (c.Values.Count == 0 && c.Relays.Count == 0)
            {
                s.Problems.Add("card carries no values and no relays");
            }

            if (c.AlertType != "FAILURE" && c.AlertType != "PREDICTIVE")
            {
                s.Problems.Add("alertType '" + c.AlertType + "' not FAILURE|PREDICTIVE");
            }

            s.Valid = s.Problems.Count == 0;
            s.CauseCode = c.CauseCode;
            s.AssetType = c.AssetType;
            s.AlertType = c.AlertType;
            s.TriggeredAt = trigTs;
            s.ValueCount = c.Values.Count;
            s.RelayCount = c.Relays.Count;
            return c;
        }

        // -----------------------------------------------------------------------------------------
        // 2.0: the card's conditions as objects
        // -----------------------------------------------------------------------------------------
        private static readonly Regex RxWindow = new Regex(@"^\s*([0-9]+(?:\.[0-9]+)?)\s*(sec|secs|second|seconds|s|min|mins|minute|minutes|m|hr|hrs|hour|hours|h)?\s*$", RegexOptions.IgnoreCase);

        private static void ParseConditions(JToken raw, JToken meta, JudgeCard c)
        {
            if (meta != null)
            {
                string logic = JudgeUtil.Str(meta, "conditionLogic");
                if (!string.IsNullOrWhiteSpace(logic))
                {
                    c.ConditionLogic = logic.Trim().ToUpperInvariant() == "OR" ? "OR" : "AND";
                }

                string win = JudgeUtil.Str(meta, "checkWindow");
                if (!string.IsNullOrWhiteSpace(win))
                {
                    c.CheckWindowText = win.Trim();
                    c.CheckWindowSec = ParseWindowSec(win);
                }
            }

            ParseConditionsInto(raw, c.Conditions);
        }

        /// <summary>
        /// 2.7 -- the condition-parsing body, lifted out of ParseConditions unchanged so the RESET
        /// card can be read by exactly the same code as the set card. Nothing here is new; the only
        /// change is that the destination list is a parameter instead of always card.Conditions.
        /// Reading the two cards with one parser is the point: a divergence between them would be a
        /// bug in the comparison rather than in the data.
        /// </summary>
        internal static void ParseConditionsInto(JToken raw, List<JudgeCardCondition> into)
        {
            JudgeCard c = new JudgeCard();
            c.Conditions = into;

            foreach (string key in new[] { "enablingConditions", "EnablingConditions" })
            {
                JToken ec = JudgeUtil.Prop(raw, key);
                if (!JudgeUtil.IsArray(ec))
                {
                    continue;
                }

                foreach (JToken e in ec)
                {
                    if (!JudgeUtil.IsObject(e))
                    {
                        continue;
                    }

                    string label = JudgeUtil.Str(e, "label") ?? "";
                    string val = JudgeUtil.Str(e, "value") ?? "";
                    if (label == "")
                    {
                        continue;
                    }

                    JudgeCardCondition cond = new JudgeCardCondition();
                    cond.Kind = "relay";
                    cond.Role = "enabling";
                    cond.Label = label;
                    cond.Name = JudgeUtil.StripUnit(label);
                    cond.Unit = JudgeUtil.Str(e, "unit");
                    cond.CardValueText = val;
                    cond.CardValue = val == "" ? (double?)null : JudgeUtil.RelayBit(val);
                    cond.CardTime = JudgeUtil.Ts(e, "time");
                    cond.WantState = val == "" ? null : (JudgeUtil.RelayBit(val) == 1 ? "UP" : "DN");
                    c.Conditions.Add(cond);
                }
            }

            foreach (string key in new[] { "triggeredConditions", "TriggeredConditions" })
            {
                JToken tc = JudgeUtil.Prop(raw, key);
                if (!JudgeUtil.IsArray(tc))
                {
                    continue;
                }

                foreach (JToken e in tc)
                {
                    if (!JudgeUtil.IsObject(e))
                    {
                        continue;
                    }

                    string label = JudgeUtil.Str(e, "paramLabel") ?? "";
                    if (label == "")
                    {
                        continue;
                    }

                    JudgeCardCondition cond = new JudgeCardCondition();
                    string type = (JudgeUtil.Str(e, "type") ?? "main").ToLowerInvariant();
                    cond.Role = type;
                    cond.Formula = JudgeUtil.Str(e, "formula");
                    // 2.2: AlertV3 already states the OR grouping on the condition itself
                    // (SignalAlertProcessor's SigCardMain(..., logic: "OR", group: "g1")). The judge
                    // read neither field, so a 4-way SPN OR was evaluated as four AND-ed conditions.
                    cond.Logic = JudgeUtil.Str(e, "logic", "Logic");
                    cond.Group = JudgeUtil.Str(e, "group", "Group");
                    bool deltaName = label.StartsWith("∆") || label.StartsWith("Δ") || (label.StartsWith("d", StringComparison.Ordinal) && label.Length > 1 && char.IsUpper(label[1]));
                    cond.Kind = (type == "derived" || !string.IsNullOrWhiteSpace(cond.Formula) || deltaName) ? "derived" : "analog";

                    // 2.4 -- a TRIGGERED condition of type "relay" is a relay, not an analog. The
                    // Point Machine cards carry NWKR/RWKR this way ({"type":"relay","paramLabel":
                    // "NWKR","measured":"DROP","thresholds":[]}), and treating it as an analog with
                    // no thresholds meant it never reached card.Relays, so the catalogue's NWKR
                    // attribute could not be found on the card at all. On alert 1280129 that left
                    // the matcher with nothing it recognised.
                    if (type == "relay")
                    {
                        cond.Kind = "relay";
                    }

                    cond.Label = label;
                    cond.Name = JudgeUtil.StripUnit(label);
                    cond.Unit = JudgeUtil.Str(e, "unit");
                    cond.CardValueText = JudgeUtil.Str(e, "measured");
                    double md;
                    if (cond.CardValueText != null && double.TryParse(cond.CardValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out md))
                    {
                        cond.CardValue = md;
                    }

                    cond.CardTime = JudgeUtil.Ts(e, "time");

                    // 2.4 -- and give it the state the card measured, so it lands in card.Relays
                    // alongside the enabling relays and the matcher can compare it.
                    if (cond.Kind == "relay" && !string.IsNullOrWhiteSpace(cond.CardValueText))
                    {
                        int bit = JudgeUtil.RelayBit(cond.CardValueText);
                        cond.CardValue = bit;
                        cond.WantState = bit == 1 ? "UP" : "DN";
                    }

                    JToken thc = JudgeUtil.Prop(e, "thresholds");
                    if (JudgeUtil.IsArray(thc))
                    {
                        foreach (JToken th in thc)
                        {
                            double thv;
                            if (!JudgeUtil.TryNum(JudgeUtil.Prop(th, "value"), out thv))
                            {
                                continue;
                            }

                            JudgeCardThreshold t = new JudgeCardThreshold();
                            t.Label = JudgeUtil.Str(th, "label") ?? "";
                            t.Value = thv;
                            t.Comparison = JudgeUtil.Str(th, "comparison") ?? "";
                            t.Status = JudgeUtil.Str(th, "status");
                            cond.Thresholds.Add(t);
                        }
                    }

                    c.Conditions.Add(cond);
                }
            }
        }

        internal static int? ParseWindowSec(string text)
        {
            Match m = RxWindow.Match(text ?? "");
            if (!m.Success)
            {
                return null;
            }

            double n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            string unit = m.Groups[2].Value.ToLowerInvariant();
            double mult = 1;
            if (unit.StartsWith("m"))
            {
                mult = 60;
            }
            else if (unit.StartsWith("h"))
            {
                mult = 3600;
            }

            return (int)Math.Round(n * mult);
        }

        // -----------------------------------------------------------------------------------------
        // helpers (same as CardParser)
        // -----------------------------------------------------------------------------------------

        // "Ir mA = 12.5 < MinFail 20"  /  "TPR: 0"  /  "NWKR=1" -> Values/Relays
        private static void MineCondition(string cond, JudgeCard c)
        {
            if (string.IsNullOrWhiteSpace(cond))
            {
                return;
            }

            string[] parts = cond.Split(new[] { '=', ':' }, 2);
            if (parts.Length != 2)
            {
                return;
            }

            string name = parts[0].Trim();
            string rest = parts[1].Trim();
            string numTok = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (numTok == null)
            {
                return;
            }

            double d;
            if (double.TryParse(numTok, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                if (JudgeUtil.IsRelayName(name))
                {
                    TryAdd(c.Relays, name, d >= 0.5 ? 1 : 0);
                }
                else
                {
                    TryAdd(c.Values, name, d);
                }
            }
        }

        // "Min Safe" / "MinSafe" / "80% Avg" / "80%·Avg" / "120%·Avg" -> canonical keys the verifier reads
        private static void AddThreshold(JudgeCard c, string label, double v)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            TryAdd(c.Thresholds, label, v);
            string key = new string(label.Where(ch => char.IsLetterOrDigit(ch) || ch == '%').ToArray());
            TryAdd(c.Thresholds, key, v);
            string lower = key.ToLowerInvariant();
            if (lower == "minsafe")
            {
                TryAdd(c.Thresholds, "MinSafe", v);
            }
            else if (lower == "maxsafe")
            {
                TryAdd(c.Thresholds, "MaxSafe", v);
            }
            else if (lower == "minfail")
            {
                TryAdd(c.Thresholds, "MinFail", v);
            }
            else if (lower == "maxfail")
            {
                TryAdd(c.Thresholds, "MaxFail", v);
            }
            else if (lower.EndsWith("%avg"))
            {
                double pct;
                if (double.TryParse(lower.Replace("%avg", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out pct))
                {
                    TryAdd(c.Thresholds, pct.ToString("0", CultureInfo.InvariantCulture) + "%Avg", v);
                }
            }
        }

        private static string InferAssetType(string cause)
        {
            string u = (cause ?? "").ToUpperInvariant();
            if (u.StartsWith("TC "))
            {
                return "TRACK";
            }

            if (u.StartsWith("PT "))
            {
                return "POINT";
            }

            if (u.StartsWith("SG ") || u.Contains("SIG"))
            {
                return "SIGNAL";
            }

            if (u.StartsWith("IPS") || u.StartsWith("VIPS"))
            {
                return "IPS";
            }

            if (u.StartsWith("CO "))
            {
                return "CO-SIGNAL";
            }

            if (u.StartsWith("RO "))
            {
                return "ROUTE";
            }

            if (u.StartsWith("SH "))
            {
                return "SHUNT";
            }

            return "";
        }

        internal static void TryAdd<TV>(Dictionary<string, TV> d, string key, TV value)
        {
            if (key == null)
            {
                return;
            }

            if (!d.ContainsKey(key))
            {
                d[key] = value;
            }
        }
    }
}
