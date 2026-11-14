using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace E7.AiCore
{
    // Settings parsing (host-neutral). The web app reads the raw appSettings value and passes it in, so
    // static-field initialisers in the controller keep working with no start-up registration order.
    // Parse* bodies are the 1.0.162.0 Read* bodies with ConfigurationManager.AppSettings[key] -> raw.
    // Moved from AiChatController in 1.0.163.0 -- Shared AI Core 2b-ii.
    public static class AiSettings
    {

        public static int ParseInt(string raw, int def)
        {
            int v;
            return int.TryParse(raw, out v) ? v : def;
        }


        // v1.0.160.68: config double (pricing rates, fx).
        public static double ParseDbl(string raw, double def)
        {
            string s2 = raw;
            double v;
            if (!string.IsNullOrWhiteSpace(s2)
                && double.TryParse(s2.Trim(), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out v)) { return v; }
            return def;
        }


        public static bool ParseBool(string raw, bool def)
        {
            if (string.IsNullOrWhiteSpace(raw)) return def;
            raw = raw.Trim();
            return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("1", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
        }


        // ---- user-facing engine names --------------------------------------
        // What the operator sees. "MCP1"/"MCP2" are internal plumbing names and
        // mean nothing to a maintainer. Rename from web.config without a rebuild:
        //   <add key="AiEngineName1" value="ENERGY7 ULTRA 1" />
        //   <add key="AiEngineName2" value="ENERGY7 ULTRA 2" />
        //   <add key="AiEngineBrand" value="ENERGY7 ULTRA" />
        // Logs and traces deliberately KEEP srv1/srv2 -- diagnosis needs the
        // plumbing names, the operator does not.
        public static string ParseStr(string raw, string def)
        {
            string v = raw;
            return string.IsNullOrWhiteSpace(v) ? def : v.Trim();
        }

        public static int ClampCfg(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }


        public static long ReadLong(JObject o, string key)
        {
            if (o == null) { return 0; }
            JToken t = o[key];
            long v;
            if (t == null || t.Type == JTokenType.Null) { return 0; }
            return long.TryParse(t.ToString(), out v) ? v : 0;
        }

        // Host-neutral settings source for code that runs inside E7.AiCore (used from 2e on).
        // The web app implements it over web.config; tests can implement it over a dictionary.
        public interface ISettingsSource { string Get(string key); }

        public static string ReadStr(ISettingsSource src, string key, string def) { return ParseStr(src == null ? null : src.Get(key), def); }
        public static int ReadInt(ISettingsSource src, string key, int def) { return ParseInt(src == null ? null : src.Get(key), def); }
        public static double ReadDbl(ISettingsSource src, string key, double def) { return ParseDbl(src == null ? null : src.Get(key), def); }
        public static bool ReadBool(ISettingsSource src, string key, bool def) { return ParseBool(src == null ? null : src.Get(key), def); }
    }
}
