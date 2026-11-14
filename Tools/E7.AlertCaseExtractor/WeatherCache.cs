using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;

namespace E7.AlertCaseExtractor
{
    internal sealed class WeatherPoint
    {
        public double? RainAtSet;
        public double? Rain6h;
        public double? Rain24h;
        public int? Code;
        public double? Temp;
    }

    // Open-Meteo, the provider E7.AiCore AnalysisEngine.Prefetch already uses. Hourly data in IST.
    //  - older than WeatherFinalAfterDays: the archive API, one call per station per MONTH, cached on
    //    disk under state/weather/ -- a 2,000-alert first run makes a handful of calls, not 2,000.
    //  - newer: the forecast API with past_days, fetched once per run and kept in memory only. Those rows
    //    carry weather_state "recent"; when they age past the cut-off the state becomes "final", the
    //    source hash changes, and the row is rebuilt from the archive once.
    internal sealed class WeatherCache
    {
        internal const string Final = "final";
        internal const string Recent = "recent";
        internal const string NoStation = "nostation";
        internal const string NoTime = "notime";

        private static readonly HttpClient http = CreateClient();

        private readonly ExtractorSettings cfg;
        private readonly string dir;
        private readonly DateTime cutoff;
        private readonly Dictionary<string, Dictionary<DateTime, HourWx>> maps = new Dictionary<string, Dictionary<DateTime, HourWx>>(StringComparer.OrdinalIgnoreCase);

        internal int Calls;

        private sealed class HourWx
        {
            public double? Precip;
            public int? Code;
            public double? Temp;
        }

        private sealed class MonthFile
        {
            public string Station;
            public string Month;
            public DateTime CoveredTo;
            public Dictionary<string, double?[]> Hours = new Dictionary<string, double?[]>();
        }

        internal WeatherCache(ExtractorSettings cfg, string stateDir)
        {
            this.cfg = cfg;
            dir = Path.Combine(stateDir, "weather");
            cutoff = DateTime.Now.Date.AddDays(-Math.Max(1, cfg.WeatherFinalAfterDays));
        }

        private static HttpClient CreateClient()
        {
            HttpClient c = new HttpClient();
            c.Timeout = TimeSpan.FromSeconds(30);
            return c;
        }

        /// <summary>Which kind of weather a row gets. Part of the source hash, so it costs no network call.</summary>
        internal string StateFor(string station, DateTime? setTime)
        {
            if (!setTime.HasValue)
            {
                return NoTime;
            }

            StationGeo g;
            if (string.IsNullOrEmpty(station) || !cfg.Stations.TryGetValue(station, out g) || g == null || (g.Lat == 0 && g.Lon == 0))
            {
                return NoStation;
            }

            return setTime.Value < cutoff ? Final : Recent;
        }

        /// <summary>Rain in the set hour, the 6 h and the 24 h up to it; code and temperature at the set hour.</summary>
        internal WeatherPoint Get(string station, DateTime setTime)
        {
            DateTime setHour = new DateTime(setTime.Year, setTime.Month, setTime.Day, setTime.Hour, 0, 0);
            WeatherPoint p = new WeatherPoint();
            double r6 = 0;
            double r24 = 0;
            bool any6 = false;
            bool any24 = false;
            for (int i = 0; i < 24; i++)
            {
                HourWx h = HourAt(station, setHour.AddHours(-i));
                if (h == null || !h.Precip.HasValue)
                {
                    continue;
                }

                r24 += h.Precip.Value;
                any24 = true;
                if (i < 6)
                {
                    r6 += h.Precip.Value;
                    any6 = true;
                }
            }

            HourWx at = HourAt(station, setHour);
            if (at != null)
            {
                p.RainAtSet = at.Precip;
                p.Code = at.Code;
                p.Temp = at.Temp;
            }

            p.Rain6h = any6 ? Math.Round(r6, 2) : (double?)null;
            p.Rain24h = any24 ? Math.Round(r24, 2) : (double?)null;
            return p;
        }

        private HourWx HourAt(string station, DateTime hour)
        {
            Dictionary<DateTime, HourWx> map = hour < cutoff ? Month(station, hour.Year, hour.Month) : RecentMap(station);
            HourWx h;
            return map.TryGetValue(hour, out h) ? h : null;
        }

        private Dictionary<DateTime, HourWx> Month(string station, int year, int month)
        {
            string tag = year.ToString("0000") + "-" + month.ToString("00");
            string key = station + "|" + tag;
            Dictionary<DateTime, HourWx> map;
            if (maps.TryGetValue(key, out map))
            {
                return map;
            }

            DateTime first = new DateTime(year, month, 1);
            DateTime last = first.AddMonths(1).AddDays(-1);
            DateTime wantTo = last < cutoff.AddDays(-1) ? last : cutoff.AddDays(-1);
            string file = Path.Combine(dir, Safe(station) + "_" + tag + ".json");

            MonthFile mf = null;
            if (File.Exists(file))
            {
                mf = JsonConvert.DeserializeObject<MonthFile>(File.ReadAllText(file, Encoding.UTF8));
            }

            if (mf == null || mf.CoveredTo < wantTo)
            {
                StationGeo g = cfg.Stations[station];
                string url = "https://archive-api.open-meteo.com/v1/archive"
                    + "?latitude=" + g.Lat.ToString(CultureInfo.InvariantCulture)
                    + "&longitude=" + g.Lon.ToString(CultureInfo.InvariantCulture)
                    + "&start_date=" + first.ToString("yyyy-MM-dd")
                    + "&end_date=" + wantTo.ToString("yyyy-MM-dd")
                    + "&hourly=precipitation,weather_code,temperature_2m"
                    + "&timezone=Asia%2FKolkata";
                map = Fetch(url);
                mf = new MonthFile();
                mf.Station = station;
                mf.Month = tag;
                mf.CoveredTo = wantTo;
                foreach (KeyValuePair<DateTime, HourWx> kv in map)
                {
                    mf.Hours[kv.Key.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)] = new double?[] { kv.Value.Precip, kv.Value.Code, kv.Value.Temp };
                }

                AtomicFile.Write(file, JsonConvert.SerializeObject(mf, Formatting.None));
            }
            else
            {
                map = new Dictionary<DateTime, HourWx>();
                foreach (KeyValuePair<string, double?[]> kv in mf.Hours)
                {
                    DateTime t;
                    if (!DateTime.TryParseExact(kv.Key, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                    {
                        continue;
                    }

                    HourWx h = new HourWx();
                    h.Precip = kv.Value.Length > 0 ? kv.Value[0] : null;
                    h.Code = kv.Value.Length > 1 && kv.Value[1].HasValue ? (int?)(int)kv.Value[1].Value : null;
                    h.Temp = kv.Value.Length > 2 ? kv.Value[2] : null;
                    map[t] = h;
                }
            }

            maps[key] = map;
            return map;
        }

        private Dictionary<DateTime, HourWx> RecentMap(string station)
        {
            string key = station + "|recent";
            Dictionary<DateTime, HourWx> map;
            if (maps.TryGetValue(key, out map))
            {
                return map;
            }

            StationGeo g = cfg.Stations[station];
            int pastDays = Math.Min(92, Math.Max(7, cfg.WeatherFinalAfterDays + 3));
            string url = "https://api.open-meteo.com/v1/forecast"
                + "?latitude=" + g.Lat.ToString(CultureInfo.InvariantCulture)
                + "&longitude=" + g.Lon.ToString(CultureInfo.InvariantCulture)
                + "&past_days=" + pastDays.ToString(CultureInfo.InvariantCulture)
                + "&forecast_days=1"
                + "&hourly=precipitation,weather_code,temperature_2m"
                + "&timezone=Asia%2FKolkata";
            map = Fetch(url);
            maps[key] = map;
            return map;
        }

        private Dictionary<DateTime, HourWx> Fetch(string url)
        {
            Calls++;
            string body = http.GetStringAsync(url).GetAwaiter().GetResult();
            JObject o = JObject.Parse(body);
            JObject hourly = o["hourly"] as JObject;
            if (hourly == null)
            {
                throw new InvalidDataException("Open-Meteo reply has no hourly block: " + url);
            }

            JArray times = hourly["time"] as JArray;
            JArray precip = hourly["precipitation"] as JArray;
            JArray codes = hourly["weather_code"] as JArray;
            JArray temps = hourly["temperature_2m"] as JArray;
            Dictionary<DateTime, HourWx> map = new Dictionary<DateTime, HourWx>();
            if (times == null)
            {
                return map;
            }

            for (int i = 0; i < times.Count; i++)
            {
                DateTime t;
                if (!DateTime.TryParseExact(times[i].ToString(), "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                {
                    continue;
                }

                HourWx h = new HourWx();
                h.Precip = precip != null && i < precip.Count ? RowBuilder.Num(precip[i]) : null;
                double? code = codes != null && i < codes.Count ? RowBuilder.Num(codes[i]) : null;
                h.Code = code.HasValue ? (int?)(int)code.Value : null;
                h.Temp = temps != null && i < temps.Count ? RowBuilder.Num(temps[i]) : null;
                map[t] = h;
            }

            return map;
        }

        private static string Safe(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in s)
            {
                sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            }

            return sb.ToString();
        }
    }
}
