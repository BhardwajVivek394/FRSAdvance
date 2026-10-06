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
    // JSON/context helpers (case-insensitive props, truthy, clamp, id zeroing, ctx read).
    // Moved verbatim from AiChatController (E7MRIWeb) in 1.0.162.0 -- Shared AI Core step 2a-2c.
    public static class AiJson
    {

        // A call with asset_id=0 or site_id=0 identifies NOTHING, but the backend
        // answers it anyway with a zero-filled shell -- observed: get_hist_realtime
        // with asset_id=0 returned 3,217 chars of {"Id":0,"Name":null,
        // "CreatedDate":"0001-01-01T00:00:00"...}. That shell is large and
        // well-formed enough to pass the usability check, and get_hist_realtime is
        // history-class, so it would have SATISFIED THE GROUNDING GATE and allowed a
        // verdict of "ok" resting on no telemetry at all. Refuse the call instead.
        internal static readonly string[] _idArgKeys =
            { "site_id", "siteid", "asset_id", "assetid", "tag_id", "tagid",
              "attribute_id", "attributeid", "assetattributeid" };


        public static int ClampDecimalToInt(JToken t, int lo, int hi, int def)
        {
            decimal dv;
            if (t == null || !decimal.TryParse(t.ToString(), out dv)) { return def; }
            if (dv > hi) { return hi; }
            if (dv < lo) { return lo; }
            return (int)Math.Floor(dv);
        }


        public static JToken PropCI(JObject jo, string name)
        {
            foreach (var prop in jo.Properties())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) { return prop.Value; }
            }
            return null;
        }


        public static bool Truthy(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) { return false; }
            if (t.Type == JTokenType.Boolean) { return (bool)t; }
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
            {
                decimal dv2; return decimal.TryParse(t.ToString(), out dv2) && dv2 != 0m; // isError:1
            }
            string sv3 = t.ToString();
            return string.Equals(sv3, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sv3, "1", StringComparison.Ordinal)
                || string.Equals(sv3, "yes", StringComparison.OrdinalIgnoreCase);
        }


        public static string ZeroIdentifier(JToken args)
        {
            JObject o = args as JObject;
            if (o == null) { return null; }
            foreach (var prop in o.Properties())
            {
                string ln = prop.Name.ToLowerInvariant();
                bool isId = false;
                for (int i = 0; i < _idArgKeys.Length; i++) { if (ln == _idArgKeys[i]) { isId = true; break; } }
                if (!isId) { continue; }
                JToken v = prop.Value;
                if (v == null || v.Type == JTokenType.Null) { return prop.Name; }
                if (v.Type == JTokenType.Array) { continue; } // TrackIds etc. handled by the tool
                decimal dv;
                if (decimal.TryParse(v.ToString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out dv) && dv == 0m)
                {
                    return prop.Name;
                }
                if (v.Type == JTokenType.String && v.ToString().Trim().Length == 0) { return prop.Name; }
            }
            return null;
        }


        public static string GetCtx(JObject o, string key)
        {
            JToken t = o[key];
            if (t == null || t.Type == JTokenType.Null)
            {
                return "";
            }
            return t.ToString();
        }
    }
}
