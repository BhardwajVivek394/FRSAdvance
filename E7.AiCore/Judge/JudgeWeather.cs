// ============================================================================
// E7.AiCore.Judge -- JudgeWeather.cs  (1.0.208.0)
// THE RAIN RULE. Rain at the site around the alert time explains a low track relay voltage and
// a low ballast / sleeper resistance, so for the causes in Learned.RainCauses the alert is a
// real one and is PASSED.
//
//   site position : Learned.RainSites["<siteId>"] = "lat,lon"; when absent, the latitude /
//                   longitude that get_attribute_range_history returns for the asset.
//   rain          : Open-Meteo hourly precipitation, summed over the RainLookbackHours before the
//                   alert time. Rain = total >= RainMinMm.
//   any failure   : no answer -- the other checks decide. A weather fault never passes and
//                   never suppresses an alert.
// ============================================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace E7.AiCore.Judge
{
    internal sealed class JudgeRainResult
    {
        public bool Checked;
        public bool Rain;
        public double Mm;
        public double Lat;
        public double Lon;
        public string GeoSource = "";
        public string Error;
    }

    internal static class JudgeWeather
    {
        private static readonly HttpClient Http = new HttpClient();
        private static readonly ConcurrentDictionary<int, double[]> SiteGeo = new ConcurrentDictionary<int, double[]>();

        internal static bool Applies(string cause, JudgeSettings cfg)
        {
            if (string.IsNullOrWhiteSpace(cause) || cfg.LearnedRainCauses == null)
            {
                return false;
            }

            string c = Norm(cause);
            foreach (string x in cfg.LearnedRainCauses)
            {
                if (Norm(x) == c)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Norm(string s)
        {
            return System.Text.RegularExpressions.Regex.Replace((s ?? "").ToUpperInvariant(), @"[\s\-_/]+", " ").Trim();
        }

        internal static async Task<JudgeRainResult> CheckAsync(IJudgeMcp mcp, JudgeCard card, JudgeSettings cfg, List<string> log, CancellationToken ct)
        {
            JudgeRainResult r = new JudgeRainResult();
            try
            {
                DateTime at = card.TriggeredAt != default(DateTimeOffset)
                    ? card.TriggeredAt.ToOffset(TimeSpan.FromMinutes(330)).DateTime
                    : DateTime.UtcNow.AddMinutes(330);

                double[] geo = await GeoAsync(mcp, card, cfg, at, r, ct).ConfigureAwait(false);
                if (geo == null)
                {
                    r.Error = r.Error ?? "site latitude / longitude not known";
                    log.Add("learned: rain NOT CHECKED -- " + r.Error + " (set Learned.RainSites[\"" + card.SiteId + "\"] = \"lat,lon\")");
                    return r;
                }

                r.Lat = geo[0];
                r.Lon = geo[1];

                // The archive lags by days, so a fresh alert is read from the forecast service,
                // which also carries the last few days hour by hour.
                double ageHours = (DateTime.UtcNow.AddMinutes(330) - at).TotalHours;
                string url;
                if (ageHours < 60)
                {
                    url = "https://api.open-meteo.com/v1/forecast?latitude=" + F(r.Lat) + "&longitude=" + F(r.Lon)
                        + "&hourly=precipitation&past_days=3&forecast_days=1&timezone=Asia%2FKolkata";
                }
                else
                {
                    url = "https://archive-api.open-meteo.com/v1/archive?latitude=" + F(r.Lat) + "&longitude=" + F(r.Lon)
                        + "&start_date=" + at.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        + "&end_date=" + at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        + "&hourly=precipitation&timezone=Asia%2FKolkata";
                }

                string raw;
                using (CancellationTokenSource wc = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    wc.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, cfg.LearnedRainTimeoutSec)));
                    HttpResponseMessage resp = await Http.GetAsync(url, wc.Token).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        r.Error = "weather service answered " + (int)resp.StatusCode;
                        log.Add("learned: rain NOT CHECKED -- " + r.Error);
                        return r;
                    }

                    raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                }

                JObject o = JObject.Parse(raw);
                JArray times = o["hourly"] != null ? o["hourly"]["time"] as JArray : null;
                JArray prec = o["hourly"] != null ? o["hourly"]["precipitation"] as JArray : null;
                if (times == null || prec == null || times.Count == 0)
                {
                    r.Error = "weather service returned no hourly rows";
                    log.Add("learned: rain NOT CHECKED -- " + r.Error);
                    return r;
                }

                DateTime lo = at.AddHours(-Math.Max(1, cfg.LearnedRainLookbackHours));
                double sum = 0;
                int used = 0;
                for (int i = 0; i < times.Count && i < prec.Count; i++)
                {
                    DateTime t;
                    if (!DateTime.TryParse(times[i].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                    {
                        continue;
                    }

                    // an hourly row covers [t, t+1h)
                    if (t.AddHours(1) <= lo || t > at)
                    {
                        continue;
                    }

                    double mm;
                    if (prec[i].Type != JTokenType.Null
                        && double.TryParse(prec[i].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out mm))
                    {
                        sum += mm;
                        used++;
                    }
                }

                if (used == 0)
                {
                    r.Error = "no weather rows for the " + cfg.LearnedRainLookbackHours + " h before the alert";
                    log.Add("learned: rain NOT CHECKED -- " + r.Error);
                    return r;
                }

                r.Checked = true;
                r.Mm = Math.Round(sum, 1);
                r.Rain = sum >= cfg.LearnedRainMinMm;
                log.Add("learned: rain " + (r.Rain ? "YES" : "no") + " -- " + F(r.Mm) + " mm in the " + cfg.LearnedRainLookbackHours
                    + " h before " + at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " IST at " + F(r.Lat) + "," + F(r.Lon)
                    + " (" + r.GeoSource + "; limit " + F(cfg.LearnedRainMinMm) + " mm)");
            }
            catch (Exception ex)
            {
                r.Checked = false;
                r.Rain = false;
                r.Error = ex.GetType().Name + ": " + ex.Message;
                log.Add("learned: rain NOT CHECKED -- " + r.Error);
            }

            return r;
        }

        private static async Task<double[]> GeoAsync(IJudgeMcp mcp, JudgeCard card, JudgeSettings cfg, DateTime at, JudgeRainResult r, CancellationToken ct)
        {
            string fixedGeo;
            if (cfg.LearnedRainSites != null && cfg.LearnedRainSites.TryGetValue(card.SiteId.ToString(CultureInfo.InvariantCulture), out fixedGeo))
            {
                string[] p = (fixedGeo ?? "").Split(',');
                double a, b;
                if (p.Length == 2
                    && double.TryParse(p[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out a)
                    && double.TryParse(p[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out b))
                {
                    r.GeoSource = "settings";
                    return new[] { a, b };
                }
            }

            double[] cached;
            if (SiteGeo.TryGetValue(card.SiteId, out cached))
            {
                r.GeoSource = "data server (cached)";
                return cached;
            }

            if (mcp == null || card.AssetId <= 0)
            {
                return null;
            }

            JObject args = new JObject();
            args["AssetId"] = card.AssetId;
            args["StartDate"] = at.AddDays(-1).ToString("ddMMyyyy_HHmmss", CultureInfo.InvariantCulture);
            args["EndDate"] = at.AddHours(1).ToString("ddMMyyyy_HHmmss", CultureInfo.InvariantCulture);
            string text;
            try
            {
                text = await mcp.CallToolAsync(cfg.LearnedRainGeoTool, args, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                r.Error = "site position lookup failed: " + ex.Message;
                return null;
            }

            double[] found = FindLatLon(text);
            if (found != null)
            {
                SiteGeo[card.SiteId] = found;
                r.GeoSource = "data server";
            }
            else
            {
                r.Error = "no latitude / longitude in the " + cfg.LearnedRainGeoTool + " answer";
            }

            return found;
        }

        private static double[] FindLatLon(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            int i = text.IndexOf('{');
            if (i < 0)
            {
                return null;
            }

            JToken root;
            try
            {
                root = JToken.Parse(text.Substring(i));
            }
            catch
            {
                return null;
            }

            return Walk(root, 0);
        }

        private static double[] Walk(JToken t, int depth)
        {
            if (t == null || depth > 4)
            {
                return null;
            }

            JObject o = t as JObject;
            if (o != null)
            {
                double lat = 0;
                double lon = 0;
                bool haveLat = false;
                bool haveLon = false;
                foreach (JProperty p in o.Properties())
                {
                    if (string.Equals(p.Name, "latitude", StringComparison.OrdinalIgnoreCase))
                    {
                        haveLat = double.TryParse(p.Value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out lat);
                    }
                    else if (string.Equals(p.Name, "longitude", StringComparison.OrdinalIgnoreCase))
                    {
                        haveLon = double.TryParse(p.Value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out lon);
                    }
                }

                if (haveLat && haveLon && (Math.Abs(lat) > 1e-6 || Math.Abs(lon) > 1e-6))
                {
                    return new[] { lat, lon };
                }

                foreach (JProperty p in o.Properties())
                {
                    double[] sub = Walk(p.Value, depth + 1);
                    if (sub != null)
                    {
                        return sub;
                    }
                }
            }

            JArray arr = t as JArray;
            if (arr != null && arr.Count > 0)
            {
                return Walk(arr[0], depth + 1);
            }

            return null;
        }

        private static string F(double v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
