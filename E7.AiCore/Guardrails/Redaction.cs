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
    // Secret redaction for logs and audit payloads.
    // Moved verbatim from AiChatController (E7MRIWeb) in 1.0.162.0 -- Shared AI Core step 2a-2c.
    public static class Redaction
    {

        public static string LimitForLog(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = RedactSecretText(text);
            if (maxChars <= 0 || text.Length <= maxChars) return text;
            return text.Substring(0, maxChars)
                + "\n...[truncated " + (text.Length - maxChars) + " chars]";
        }


        public static string RedactSecretText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return Regex.Replace(
                text,
                @"(?i)(password|token|secret|api[_-]?key|authorization)\s*[:=]\s*[""']?[^,""'\s}]+",
                "$1=***REDACTED***");
        }


        public static JToken CloneAndRedact(JToken token)
        {
            if (token == null) return JValue.CreateNull();

            JObject obj = token as JObject;
            if (obj != null)
            {
                JObject copy = new JObject();
                foreach (JProperty prop in obj.Properties())
                {
                    if (IsSensitiveKey(prop.Name))
                        copy[prop.Name] = "***REDACTED***";
                    else
                        copy[prop.Name] = CloneAndRedact(prop.Value);
                }
                return copy;
            }

            JArray arr = token as JArray;
            if (arr != null)
            {
                JArray copy = new JArray();
                foreach (JToken item in arr)
                    copy.Add(CloneAndRedact(item));
                return copy;
            }

            return token.DeepClone();
        }


        public static bool IsSensitiveKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string k = key.ToLowerInvariant();
            return k.Contains("password")
                || k.Contains("token")
                || k.Contains("secret")
                || k.Contains("apikey")
                || k.Contains("api_key")
                || k.Contains("authorization");
        }
    }
}
