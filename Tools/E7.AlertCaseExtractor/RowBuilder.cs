using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace E7.AlertCaseExtractor
{
    // One decided alert -> one CSV row. Values at set time come from the raw JSON the alert service
    // stored and from the alert card; reset values from the reset card / reset raw JSON.
    //
    // CHANGE THE ROW LOGIC HERE, AND BUMP LogicVersion WHEN YOU DO. A new LogicVersion makes the next
    // run re-read full history and rebuild every row, so the CSV never mixes two definitions of a column.
    internal static class RowBuilder
    {
        // 1.1.0: label = Status (1 with FRS id -> PASS, 2 -> SUPPRESSED); discard_reason, remark_flag,
        // is_test, judge_decided_by, reapproved, ack_status_name, maintenance-window columns.
        // 1.2.0: every row also carries the Judge case features (ledger only), so judge-cases.json can be
        // written each run without reading the cards again.
        internal const string LogicVersion = "1.2.0";

        internal static readonly string[] Columns =
        {
            // identity
            "key", "pending_id", "frs_id", "site_id", "station", "asset_id", "asset_name", "asset_type", "cause_code", "alert_category",
            // label -- what the human did
            "label", "discard_reason", "remark_flag", "is_test", "judge_decided_by", "reapproved",
            "decided_at", "discard_was_valid", "ack_status_id", "ack_status_name",
            // timing
            "set_time", "reset_time", "duration_s", "set_hour", "weekday",
            // the condition that triggered, as the card / raw trigger states it
            "trigger_attr", "trigger_value", "trigger_threshold", "threshold_label", "trigger_comparison", "margin_pct",
            "enabling", "card_condition_count", "card_fail_count",
            // track circuit readings at set
            "ir_ma", "if_ma", "ibalst_ma", "vr_v", "vf_v", "choke_v", "charger_ma", "batt_charg_ma", "tpr", "tpr_v", "rtc_var_res", "rrail",
            // point machine readings at set
            "direction", "with_operation", "op_time_s", "op_current_a", "op_voltage_v", "kr_state", "cr_state", "loc_v_a", "loc_v_b",
            // at reset
            "trigger_value_reset", "recovered", "ibalst_reset_ma",
            // maintenance window of the same asset at set time (dbo.MaintenanceMode)
            "in_maintenance_window", "maint_window_id", "maint_minutes_into_window",
            // environment
            "weather_state", "rain_at_set_mm", "rain_6h_mm", "rain_24h_mm", "weather_code", "temp_c",
            // filled across all rows when the CSV is written (CsvWriter.AddSiteContext)
            "same_cause_site_count", "same_asset_alerts_7d",
            // human text -- for people only, the system never uses it
            "remark"
        };

        internal static string StationOf(SourceRecord r)
        {
            JObject card = Card(r);
            if (card == null)
            {
                return null;
            }

            return Str(card, "stationCode") ?? Str(card, "station");
        }

        /// <summary>SHA-256 of exactly the source fields a row is built from, plus the weather state. Equal hash = nothing to redo.</summary>
        internal static string SourceHash(SourceRecord r, string weatherState)
        {
            JObject o = new JObject();
            o["label"] = r.Label;
            o["pendingId"] = r.PendingId;
            o["frsId"] = r.FrsId;
            o["status"] = r.Status;
            o["siteId"] = r.SiteId;
            o["assetId"] = r.AssetId;
            o["cause"] = r.CauseCode;
            o["set"] = Time(r.SetTime);
            o["reset"] = Time(r.ResetTime);
            o["updated"] = Time(r.UpdatedAt);
            o["valid"] = r.DiscardWasValid;
            o["discardRemark"] = r.DiscardRemark;
            o["approvalRemark"] = r.ApprovalRemark;
            o["frsRemark"] = r.FrsRemark;
            o["ack"] = r.AckStatusId;
            o["localCard"] = r.LocalCard;
            o["cloudCard"] = r.CloudCard;
            o["plainCard"] = r.PlainCard;
            o["resetCard"] = r.ResetCard;
            o["localRaw"] = r.LocalRaw;
            o["raw"] = r.Raw;
            o["localResetRaw"] = r.LocalResetRaw;
            o["resetRaw"] = r.ResetRaw;
            o["judgeDecidedBy"] = r.JudgeDecidedBy;
            o["reapproved"] = r.Reapproved;
            o["maintId"] = r.MaintId;
            o["maintFrom"] = Time(r.MaintFrom);
            o["maintTo"] = Time(r.MaintTo);
            o["weather"] = weatherState;

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(o.ToString(Formatting.None)));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }

        internal static Dictionary<string, string> Build(SourceRecord r, WeatherCache wx)
        {
            Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string c in Columns)
            {
                row[c] = "";
            }

            JObject card = Card(r);
            JObject raw = Data(r.LocalRaw, r.Raw);
            JObject resetRaw = Data(r.LocalResetRaw, r.ResetRaw);
            JObject resetCard = Parse(r.ResetCard);
            JObject meta = card != null ? card["meta"] as JObject : null;
            string station = StationOf(r);

            // identity + label
            row["key"] = r.Key;
            row["pending_id"] = r.PendingId.ToString(CultureInfo.InvariantCulture);
            row["frs_id"] = Int(r.FrsId);
            row["site_id"] = Int(r.SiteId);
            row["station"] = station ?? "";
            row["asset_id"] = Int(r.AssetId);
            row["asset_name"] = card != null ? Str(card, "assetName") ?? "" : "";
            row["asset_type"] = AssetType(meta, r.CauseCode);
            row["cause_code"] = r.CauseCode ?? "";
            row["alert_category"] = card != null ? Str(card, "alertType") ?? "" : "";
            row["label"] = r.Label;
            // every discard stays SUPPRESSED; the reason only says why
            if (r.Label == "SUPPRESSED")
            {
                row["discard_reason"] = DiscardReason(r.DiscardRemark);
            }

            row["remark_flag"] = RemarkFlag(r.Label == "PASS" ? r.ApprovalRemark : r.DiscardRemark);
            row["is_test"] = (IsTest(r.ApprovalRemark) || IsTest(r.DiscardRemark)) ? "1" : "0";
            row["judge_decided_by"] = Clean(r.JudgeDecidedBy);
            row["reapproved"] = r.Reapproved.HasValue ? (r.Reapproved.Value ? "1" : "0") : "";
            row["decided_at"] = Time(r.UpdatedAt);
            row["discard_was_valid"] = r.DiscardWasValid.HasValue ? (r.DiscardWasValid.Value ? "1" : "0") : "";
            row["ack_status_id"] = Int(r.AckStatusId);
            row["ack_status_name"] = AckName(r.AckStatusId);

            // maintenance window
            row["in_maintenance_window"] = r.MaintId.HasValue ? "1" : "0";
            row["maint_window_id"] = Int(r.MaintId);
            if (r.MaintFrom.HasValue && r.SetTime.HasValue && r.SetTime.Value >= r.MaintFrom.Value)
            {
                row["maint_minutes_into_window"] = ((long)(r.SetTime.Value - r.MaintFrom.Value).TotalMinutes).ToString(CultureInfo.InvariantCulture);
            }

            // timing
            row["set_time"] = Time(r.SetTime);
            row["reset_time"] = Time(r.ResetTime);
            if (r.SetTime.HasValue && r.ResetTime.HasValue && r.ResetTime.Value >= r.SetTime.Value)
            {
                row["duration_s"] = ((long)(r.ResetTime.Value - r.SetTime.Value).TotalSeconds).ToString(CultureInfo.InvariantCulture);
            }

            if (r.SetTime.HasValue)
            {
                row["set_hour"] = r.SetTime.Value.Hour.ToString(CultureInfo.InvariantCulture);
                row["weekday"] = r.SetTime.Value.DayOfWeek.ToString();
            }

            // trigger: the raw Trigger block when the alert service filled it, else the card's first failing condition
            List<CardCond> conds = Conditions(card);
            CardCond failing = conds.FirstOrDefault(c => c.Failed);
            JObject trig = raw != null ? raw["Trigger"] as JObject : null;
            double? triggerValue = trig != null ? Num(trig["Value"]) : null;
            bool fromRaw = triggerValue.HasValue;
            string attr = null;
            double? threshold = null;
            string thresholdLabel = null;
            string comparison = null;
            if (fromRaw)
            {
                attr = Str(trig, "Attribute");
                threshold = Num(trig["Threshold"]);
                thresholdLabel = Str(trig, "ThresholdSource");
                comparison = Str(trig, "Comparison");
            }
            else if (failing != null)
            {
                attr = failing.Label;
                triggerValue = failing.Value;
                threshold = failing.Threshold;
                thresholdLabel = failing.ThresholdLabel;
                comparison = failing.Comparison;
            }

            row["trigger_attr"] = Clean(attr);
            row["trigger_value"] = Num(triggerValue);
            row["trigger_threshold"] = Num(threshold);
            row["threshold_label"] = Clean(thresholdLabel);
            row["trigger_comparison"] = Clean(comparison);
            row["margin_pct"] = Num(Margin(triggerValue, threshold, comparison));
            row["enabling"] = Enabling(card);
            // 1.2.0: the card as the Judge will see it (same code as the Judge: JudgeCaseBase.cs). Kept in the
            // ledger only -- it is not a CSV column -- and turned into out/judge-cases.json by JudgeCasesWriter.
            row[JudgeCasesWriter.FeaturesKey] = E7.AiCore.Judge.JudgeCaseFeatures.FromCard(card).ToJson().ToString(Formatting.None);
            if (card != null)
            {
                row["card_condition_count"] = conds.Count.ToString(CultureInfo.InvariantCulture);
                row["card_fail_count"] = conds.Count(c => c.Failed).ToString(CultureInfo.InvariantCulture);
            }

            // track circuit readings
            row["ir_ma"] = Num(Attr(raw, "Ir mA"));
            row["if_ma"] = Num(Attr(raw, "If mA"));
            row["ibalst_ma"] = Num(Top(raw, "IBALST"));
            row["vr_v"] = Num(Attr(raw, "Vr"));
            row["vf_v"] = Num(Attr(raw, "Vf"));
            row["choke_v"] = Num(Attr(raw, "Choke V"));
            row["charger_ma"] = Num(Attr(raw, "Charger mA"));
            row["batt_charg_ma"] = Num(Top(raw, "ITC_BATT_CHARG"));
            row["tpr"] = Num(Attr(raw, "TPR"));
            row["tpr_v"] = Num(Attr(raw, "TPR V"));
            row["rtc_var_res"] = Num(Top(raw, "RTC_VAR_RES"));
            row["rrail"] = Num(Top(raw, "RRAIL"));

            // point machine readings
            JObject fc = raw != null ? raw["FailureCondition"] as JObject : null;
            string direction = fc != null ? Str(fc, "Direction") : null;
            string side = Side(direction);
            row["direction"] = direction ?? "";
            if (fc != null && fc["WithOperation"] != null && fc["WithOperation"].Type == JTokenType.Boolean)
            {
                row["with_operation"] = fc["WithOperation"].Value<bool>() ? "1" : "0";
            }

            if (side != null)
            {
                JObject endA = Path(raw, "Ends", "A");
                JObject endB = Path(raw, "Ends", "B");
                JObject relays = raw != null ? raw["SharedRelays"] as JObject : null;
                row["op_time_s"] = Num(CondValue(conds, side + "_T"));
                row["op_current_a"] = Num(CondValue(conds, side + "_C") ?? Num(endA != null ? endA[side + "_C"] : null));
                row["op_voltage_v"] = Num(CondValue(conds, side + "_V") ?? Num(endA != null ? endA[side + "_V"] : null));
                row["kr_state"] = Flag(relays, side + "KR");
                row["cr_state"] = Flag(relays, side + "CR");
                row["loc_v_a"] = Num(endA != null ? Num(endA[side + "_LOC_V"]) : null);
                row["loc_v_b"] = Num(endB != null ? Num(endB[side + "_LOC_V"]) : null);
            }

            // reset: the same quantity the trigger was decided on, from the same kind of source
            double? resetValue = null;
            if (fromRaw)
            {
                resetValue = RawValueFor(resetRaw, attr);
            }
            else if (failing != null)
            {
                resetValue = ResetCardValue(resetCard, failing.Label);
            }

            row["trigger_value_reset"] = Num(resetValue);
            if (r.ResetTime.HasValue && resetValue.HasValue && threshold.HasValue && !string.IsNullOrEmpty(comparison))
            {
                row["recovered"] = Breached(resetValue.Value, threshold.Value, comparison) ? "N" : "Y";
            }

            row["ibalst_reset_ma"] = Num(Top(resetRaw, "IBALST"));

            // environment
            string state = wx.StateFor(station, r.SetTime);
            row["weather_state"] = state;
            if (r.SetTime.HasValue && (state == WeatherCache.Final || state == WeatherCache.Recent))
            {
                WeatherPoint p = wx.Get(station, r.SetTime.Value);
                row["rain_at_set_mm"] = Num(p.RainAtSet);
                row["rain_6h_mm"] = Num(p.Rain6h);
                row["rain_24h_mm"] = Num(p.Rain24h);
                row["weather_code"] = Int(p.Code);
                row["temp_c"] = Num(p.Temp);
            }

            string remark = r.FrsRemark;
            if (string.IsNullOrWhiteSpace(remark))
            {
                remark = r.ApprovalRemark;
            }

            if (string.IsNullOrWhiteSpace(remark))
            {
                remark = r.DiscardRemark;
            }

            row["remark"] = (remark ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return row;
        }

        // ---- remarks (for learning only; the Judge never sees a remark on a live alert) -------------

        /// <summary>Lower-case, trimmed, inner whitespace collapsed.</summary>
        private static string Norm(string remark)
        {
            if (string.IsNullOrWhiteSpace(remark))
            {
                return "";
            }

            return string.Join(" ", remark.Trim().ToLowerInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static bool ContainsAny(string text, params string[] parts)
        {
            foreach (string p in parts)
            {
                if (text.Contains(p))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAny(string text, params string[] values)
        {
            foreach (string v in values)
            {
                if (text == v)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>t = True, f = False (the reviewers' shorthand). Not a test marker.</summary>
        internal static string RemarkFlag(string remark)
        {
            string r = Norm(remark).TrimEnd('.', ',', ';');
            if (IsAny(r, "t", "tt", "true", "true alert"))
            {
                return "TRUE";
            }

            if (IsAny(r, "f", "ff", "false", "false alert"))
            {
                return "FALSE";
            }

            return "";
        }

        /// <summary>Only the words test / tesing / testing mark a test. "t" is True, not test.</summary>
        internal static bool IsTest(string remark)
        {
            string r = Norm(remark);
            return r.Length > 0 && ContainsAny(r, "test", "tesing");
        }

        /// <summary>Why a person discarded the alert. Checked in this order; the first match wins.</summary>
        internal static string DiscardReason(string remark)
        {
            string r = Norm(remark);
            if (r.Length == 0)
            {
                return "NONE";
            }

            string flag = RemarkFlag(remark);
            if (flag == "TRUE")
            {
                return "MARKED_TRUE";
            }

            if (flag == "FALSE")
            {
                return "MARKED_FALSE";
            }

            if (ContainsAny(r, "mainte", "maintai", "block", "change over") || IsAny(r.TrimEnd('.', ',', ';'), "m", "mm", "cr", "sw"))
            {
                return "MAINTENANCE";
            }

            if (ContainsAny(r, "test", "tesing"))
            {
                return "TESTING";
            }

            if (ContainsAny(r, "sensi", "sensa"))
            {
                return "NOT_SENSITIVE";
            }

            if (ContainsAny(r, "already", "auto-discard", "duplicate"))
            {
                return "DUPLICATE";
            }

            if (ContainsAny(r, "hardware", "logger", "debouncer", "bit", "a10", "sync", "service", "broadcast", "connector", "sim"))
            {
                return "DEVICE_FAULT";
            }

            if (ContainsAny(r, "thres", "theres", "logic", "min", "mapping", "fot", "sip", "pickup", "calling", "caling"))
            {
                return "CONFIG_THRESHOLD";
            }

            return "OTHER";
        }

        /// <summary>E7MRIV2Web Utility.AcknowledgemenStatus, copied so this tool does not reference the web project.</summary>
        private static string AckName(int? id)
        {
            if (!id.HasValue)
            {
                return "";
            }

            switch (id.Value)
            {
                case 0:
                    return "Not_Given";
                case 1:
                    return "Pending";
                case 2:
                    return "True";
                case 3:
                    return "False";
                case 4:
                    return "Maintenance";
                case 5:
                    return "PT";
                case 6:
                    return "Acknowledge";
                default:
                    return "";
            }
        }

        // ---- card --------------------------------------------------------------------------------

        private sealed class CardCond
        {
            public string Label;
            public string ParamName;
            public double? Value;
            public double? Threshold;
            public string ThresholdLabel;
            public string Comparison;
            public bool Failed;
        }

        private static JObject Card(SourceRecord r)
        {
            return Parse(r.LocalCard) ?? Parse(r.CloudCard) ?? Parse(r.PlainCard);
        }

        private static List<CardCond> Conditions(JObject card)
        {
            List<CardCond> list = new List<CardCond>();
            JArray arr = card != null ? card["triggeredConditions"] as JArray : null;
            if (arr == null)
            {
                return list;
            }

            foreach (JObject tc in arr.OfType<JObject>())
            {
                CardCond c = new CardCond();
                c.Label = Str(tc, "paramLabel");
                c.ParamName = Str(tc, "paramName");
                c.Value = Num(tc["measured"]);
                JArray ths = tc["thresholds"] as JArray;
                JObject chosen = null;
                if (ths != null)
                {
                    chosen = ths.OfType<JObject>().FirstOrDefault(t => string.Equals(Str(t, "status"), "FAIL", StringComparison.OrdinalIgnoreCase));
                    c.Failed = chosen != null;
                    if (chosen == null)
                    {
                        chosen = ths.OfType<JObject>().FirstOrDefault();
                    }
                }

                if (chosen != null)
                {
                    c.Threshold = Num(chosen["value"]);
                    c.ThresholdLabel = Str(chosen, "label");
                    c.Comparison = Str(chosen, "comparison");
                }

                list.Add(c);
            }

            return list;
        }

        private static double? CondValue(List<CardCond> conds, string paramName)
        {
            CardCond c = conds.FirstOrDefault(x => string.Equals(x.ParamName, paramName, StringComparison.OrdinalIgnoreCase));
            return c != null ? c.Value : null;
        }

        private static string Enabling(JObject card)
        {
            JArray arr = card != null ? card["enablingConditions"] as JArray : null;
            if (arr == null)
            {
                return "";
            }

            List<string> parts = new List<string>();
            foreach (JObject e in arr.OfType<JObject>())
            {
                string label = Clean(Str(e, "label") ?? Str(e, "paramLabel"));
                string value = Clean(Str(e, "value") ?? Str(e, "measured"));
                string first = value.Split(' ').FirstOrDefault() ?? "";
                parts.Add(label + "=" + first);
            }

            return string.Join(";", parts);
        }

        private static double? ResetCardValue(JObject resetCard, string label)
        {
            if (resetCard == null || string.IsNullOrEmpty(label))
            {
                return null;
            }

            JArray trig = resetCard["triggeredConditions"] as JArray;
            if (trig != null)
            {
                JObject m = trig.OfType<JObject>().FirstOrDefault(t => string.Equals(Str(t, "paramLabel"), label, StringComparison.OrdinalIgnoreCase));
                if (m != null)
                {
                    return Num(m["measured"]);
                }
            }

            JArray en = resetCard["enablingConditions"] as JArray;
            if (en != null)
            {
                JObject m = en.OfType<JObject>().FirstOrDefault(t => string.Equals(Str(t, "label"), label, StringComparison.OrdinalIgnoreCase));
                if (m != null)
                {
                    return Num(m["value"]);
                }
            }

            return null;
        }

        private static string AssetType(JObject meta, string cause)
        {
            string signal = meta != null ? Str(meta, "signalId") ?? "" : "";
            string code = (cause ?? "").ToUpperInvariant();
            if (signal.StartsWith("TC", StringComparison.OrdinalIgnoreCase) || code.StartsWith("TC "))
            {
                return "TRACK";
            }

            if (signal.StartsWith("PT", StringComparison.OrdinalIgnoreCase) || code.StartsWith("PT "))
            {
                return "POINT";
            }

            return "";
        }

        // ---- raw JSON ----------------------------------------------------------------------------

        /// <summary>First payload that parses and is real data -- not the "Reconcile: DB already reset by other node" stub.</summary>
        private static JObject Data(string first, string second)
        {
            foreach (string s in new[] { first, second })
            {
                JObject o = Parse(s);
                if (o != null && !string.Equals(Str(o, "source"), "Reconcile", StringComparison.OrdinalIgnoreCase))
                {
                    return o;
                }
            }

            return null;
        }

        /// <summary>attributes[] by attributeName; the alert service writes both "Attributes" and "attributes".</summary>
        private static double? Attr(JObject raw, string name)
        {
            if (raw == null)
            {
                return null;
            }

            JArray arr = (raw["Attributes"] ?? raw["attributes"]) as JArray;
            if (arr == null)
            {
                return null;
            }

            JObject a = arr.OfType<JObject>().FirstOrDefault(x => string.Equals((Str(x, "attributeName") ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase));
            return a != null ? Num(a["value"]) : null;
        }

        /// <summary>A top-level derived value; some reset payloads nest them under "Derived".</summary>
        private static double? Top(JObject raw, string key)
        {
            if (raw == null)
            {
                return null;
            }

            double? v = Num(raw[key]);
            if (v.HasValue)
            {
                return v;
            }

            JObject d = raw["Derived"] as JObject;
            return d != null ? Num(d[key]) : null;
        }

        private static double? RawValueFor(JObject raw, string attr)
        {
            if (raw == null || string.IsNullOrEmpty(attr))
            {
                return null;
            }

            string a = attr.Replace("(calculated)", "").Trim();
            switch (a.ToUpperInvariant())
            {
                case "IR_MA":
                    return Attr(raw, "Ir mA");
                case "IF_MA":
                    return Attr(raw, "If mA");
                case "TPR_V":
                    return Attr(raw, "TPR V");
                default:
                    return Top(raw, a);
            }
        }

        private static JObject Path(JObject raw, string a, string b)
        {
            JObject x = raw != null ? raw[a] as JObject : null;
            return x != null ? x[b] as JObject : null;
        }

        private static string Side(string direction)
        {
            if (string.IsNullOrEmpty(direction))
            {
                return null;
            }

            if (direction.StartsWith("Rev", StringComparison.OrdinalIgnoreCase))
            {
                return "RW";
            }

            if (direction.StartsWith("Nor", StringComparison.OrdinalIgnoreCase))
            {
                return "NW";
            }

            return null;
        }

        private static string Flag(JObject relays, string name)
        {
            JToken t = relays != null ? relays[name] : null;
            if (t == null || t.Type != JTokenType.Boolean)
            {
                return "";
            }

            return t.Value<bool>() ? "1" : "0";
        }

        // ---- arithmetic --------------------------------------------------------------------------

        private static bool IsLower(string comparison)
        {
            string c = (comparison ?? "").Trim();
            return c.StartsWith("<") || (c.Length > 0 && c[0] == LessOrEqual);
        }

        /// <summary>How far past the threshold, in percent of the threshold. Positive = breached, whichever way the rule points.</summary>
        private static double? Margin(double? value, double? threshold, string comparison)
        {
            if (!value.HasValue || !threshold.HasValue || threshold.Value == 0 || string.IsNullOrEmpty(comparison))
            {
                return null;
            }

            double t = threshold.Value;
            double pct = IsLower(comparison) ? (t - value.Value) / Math.Abs(t) * 100.0 : (value.Value - t) / Math.Abs(t) * 100.0;
            return Math.Round(pct, 2);
        }

        private static bool Breached(double value, double threshold, string comparison)
        {
            if (IsLower(comparison))
            {
                return value < threshold;
            }

            return value > threshold;
        }

        // ---- small helpers -----------------------------------------------------------------------

        private static JObject Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JToken.Parse(json) as JObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Str(JObject o, string name)
        {
            JToken t = o != null ? o[name] : null;
            if (t == null || t.Type == JTokenType.Null)
            {
                return null;
            }

            return t.ToString();
        }

        internal static double? Num(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null)
            {
                return null;
            }

            if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer)
            {
                return t.Value<double>();
            }

            double d;
            if (double.TryParse(t.ToString().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                return d;
            }

            return null;
        }

        private static string Num(double? v)
        {
            return v.HasValue ? v.Value.ToString("0.####", CultureInfo.InvariantCulture) : "";
        }

        private static string Int(int? v)
        {
            return v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "";
        }

        private static string Time(DateTime? t)
        {
            return t.HasValue ? t.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "";
        }

        // Card text uses a middle dot, the <= / >= signs and arrows; kept as char codes so this file stays ASCII.
        private const char MiddleDot = (char)0x00B7;
        private const char LessOrEqual = (char)0x2264;
        private const char GreaterOrEqual = (char)0x2265;

        /// <summary>Labels to plain ASCII: "80% Avg" with a middle dot -> "80% Avg", "PICKUP" + arrow -> "PICKUP".</summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }

            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (ch == MiddleDot)
                {
                    sb.Append(' ');
                }
                else if (ch == LessOrEqual)
                {
                    sb.Append("<=");
                }
                else if (ch == GreaterOrEqual)
                {
                    sb.Append(">=");
                }
                else if (ch >= 32 && ch < 127)
                {
                    sb.Append(ch);
                }
            }

            return string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
