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
    // AnalysisEngine -- Endpoints.
    // Moved verbatim from AiChatController.Endpoints.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {

        internal static void SlimPmCurve(JObject op)
        {
            JArray curve = op["current"] as JArray;

            if (curve == null)
            {
                return;
            }

            int count = curve.Count;

            if (count == 0)
            {
                op.Remove("current");
                return;
            }

            JArray sampled = new JArray();

            if (count <= PmCurveMaxPoints)
            {
                foreach (JToken point in curve)
                {
                    sampled.Add(PmCurvePoint(point));
                }
            }
            else
            {
                for (int i = 0; i < PmCurveMaxPoints; i++)
                {
                    int index = (int)((long)i * (count - 1) / (PmCurveMaxPoints - 1));
                    sampled.Add(PmCurvePoint(curve[index]));
                }
            }

            op["current"] = sampled;
        }


        internal static double PmCurvePoint(JToken point)
        {
            double value;

            if (point == null || !double.TryParse(point.ToString(), out value))
            {
                return 0;
            }

            return Math.Round(value, 2);
        }


        internal static int SlimPmOps(JObject side)
        {
            if (side == null)
            {
                return 0;
            }

            JArray ops = side["ops"] as JArray;

            if (ops == null)
            {
                return 0;
            }

            foreach (JToken opToken in ops)
            {
                JObject op = opToken as JObject;

                if (op == null)
                {
                    continue;
                }

                List<string> dropNames = new List<string>();

                foreach (JProperty prop in op.Properties())
                {
                    if (Array.IndexOf(PmOpKeepFields, prop.Name) < 0)
                    {
                        dropNames.Add(prop.Name);
                    }
                }

                foreach (string dropName in dropNames)
                {
                    op.Remove(dropName);
                }

                SlimPmCurve(op);
            }

            return ops.Count;
        }


        // Compact shape signature of a tool result, for cross-run stability comparison.
        internal static string DryRunShape(string txt)
        {
            if (string.IsNullOrEmpty(txt)) { return "empty"; }
            try
            {
                JToken j = JToken.Parse(txt);
                if (j.Type == JTokenType.Array)
                {
                    JArray a = (JArray)j;
                    string keys = a.Count > 0 && a[0] is JObject
                        ? ":" + string.Join(",", ((JObject)a[0]).Properties().Take(6).Select(p => p.Name))
                        : "";
                    return "array[" + a.Count + "]" + keys;
                }
                if (j.Type == JTokenType.Object)
                {
                    JObject o = (JObject)j;
                    return "{" + string.Join(",", o.Properties().Take(8).Select(p =>
                        p.Name + "=" + (p.Value.Type == JTokenType.Array ? "arr" + ((JArray)p.Value).Count : p.Value.Type.ToString().ToLowerInvariant()))) + "}";
                }
                return j.Type.ToString().ToLowerInvariant();
            }
            catch
            {
                if (txt.TrimStart().StartsWith("<")) { return "html/" + txt.Length + "b"; }
                return "text/" + txt.Length + "b";
            }
        }
    }
}
