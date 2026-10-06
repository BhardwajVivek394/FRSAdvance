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
    // Small text helpers (truncate, short hash, constant-time compare).
    // Moved verbatim from AiChatController (E7MRIWeb) in 1.0.162.0 -- Shared AI Core step 2a-2c.
    public static class AiText
    {

        public static string Trunc(string s, int n)
        { return string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "..."); }


        public static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null) { return false; }
            byte[] ba = Encoding.UTF8.GetBytes(a);
            byte[] bb = Encoding.UTF8.GetBytes(b);
            int diff = ba.Length ^ bb.Length;
            int n = Math.Max(ba.Length, bb.Length);
            for (int i = 0; i < n; i++)
            {
                byte x = i < ba.Length ? ba[i] : (byte)0;
                byte y = i < bb.Length ? bb[i] : (byte)0;
                diff |= x ^ y;
            }
            return diff == 0;
        }

        // failure text fed back to the MODEL is fixed-code only; the raw
        // exception goes to the server-side tool log / Trace, never to the model.
        public static string ShortHash(string s0)
        {
            if (s0 == null) { return ""; }
            try
            {
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s0));
                    StringBuilder sb2 = new StringBuilder();
                    for (int k = 0; k < 16; k++) { sb2.Append(h[k].ToString("x2")); } // 128-bit prefix
                    return sb2.ToString();
                }
            }
            catch { return ""; }
        }
    }
}
