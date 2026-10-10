using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Domain
{
    // =====================================================================================
    //  IStationPlanSource -- what the host must supply. The web project implements it with
    //  its own services (site/asset/alert services, Roster API, Historian model endpoints),
    //  so this library stays free of MVC, session and Domain types.
    //
    //  Every method may throw or return null; the builder records the gap and carries on.
    // =====================================================================================
    public interface IStationPlanSource
    {
        Task<IList<PlanSiteInfo>> GetSitesAsync(CancellationToken cancellationToken);

        Task<IList<PlanAssetInfo>> GetAssetsAsync(int siteId, CancellationToken cancellationToken);

        // The saved roster worksheet for one station and day (E7RosterGenerator run).
        Task<JToken> GetWorksheetAsync(int divisionId, int siteId, DateTime day, CancellationToken cancellationToken);

        // Alerts for the whole division in [from, to]; the builder filters by site.
        Task<IList<PlanAlertRow>> GetAlertsAsync(int divisionId, DateTime from, DateTime to, CancellationToken cancellationToken);

        // Raw replies of the Historian prediction endpoints (track / pm-operation-v4).
        Task<JObject> GetTrackModelAsync(int siteId, int assetId, CancellationToken cancellationToken);

        Task<JObject> GetPmModelAsync(int siteId, int assetId, CancellationToken cancellationToken);
    }

    // =====================================================================================
    //  Small TTL cache shared by every request in the process. Failures are never cached.
    // =====================================================================================
    internal static class PlanCache
    {
        private sealed class Entry
        {
            public DateTime ExpiresUtc;
            public object Value;
        }

        private static readonly ConcurrentDictionary<string, Entry> entries =
            new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);

        public static async Task<T> GetOrLoadAsync<T>(string key, int seconds, Func<Task<T>> loader) where T : class
        {
            Entry hit;
            if (seconds > 0 && entries.TryGetValue(key, out hit) && hit.ExpiresUtc > DateTime.UtcNow)
            {
                return (T)hit.Value;
            }

            T value = await loader().ConfigureAwait(false);
            if (value != null && seconds > 0)
            {
                entries[key] = new Entry { ExpiresUtc = DateTime.UtcNow.AddSeconds(seconds), Value = value };
                Prune();
            }

            return value;
        }

        private static void Prune()
        {
            if (entries.Count < 2000)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            foreach (KeyValuePair<string, Entry> pair in entries)
            {
                if (pair.Value.ExpiresUtc <= now)
                {
                    Entry removed;
                    entries.TryRemove(pair.Key, out removed);
                }
            }
        }
    }

    // =====================================================================================
    //  PlanIntentParser -- decides whether a chat question is a maintenance-plan request and
    //  pulls out station, asset, hours and language. Returns null for every other question,
    //  so the ChatBot keeps its normal path for them.
    // =====================================================================================
    public static class PlanIntentParser
    {
        // Words that make a question a plan request (English and Hinglish).
        private static readonly Regex planWords = new Regex(
            @"\b(plan|planning|roster|attend|to-?do|work\s*list|kya\s+kar(na|nu|u|e|en|ein|oon)|kya\s+dekh(na|u|e|en)|kya\s+check|kaam\s+(kya|hai)|what\s+(to|should)\s+(i\s+|we\s+)?(do|attend|check|inspect)|what\s+needs\s+(attention|attending)|maintenance\s+(plan|list|kaam|work|schedule))\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "4 ghante", "4 hours", "4 hrs", "2.5 h"
        private static readonly Regex hoursPattern = new Regex(
            @"(\d{1,2}(?:\.\d)?)\s*(ghante|ghanta|ghnte|ghante?h|hours?|hrs?|hr|h)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex halfDay = new Regex(
            @"\b(half\s*day|aadha\s*din|adha\s*din)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex fullDay = new Regex(
            @"\b(full\s*day|whole\s*day|pura\s*din|poora\s*din|pure\s*din)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Questions that are clearly about monitoring data, not a plan.
        private static readonly Regex monitoringWords = new Regex(
            @"\b(live\s+values?|history|timestamps?|tpr\s+state|last\s+\d+\s+readings?|tag\s+values?|device\s+time)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Asset codes: PT-115, PT-115/116, PM07, 3T, 12AT, S12, SH04
        private static readonly Regex assetPattern = new Regex(
            @"\b(PT-?\d{1,4}[A-Z]?(?:/\d{1,4}[A-Z]?)?|PM-?\d{1,4}[A-Z]?|SH-?\d{1,4}|S-?\d{1,3}[A-Z]{0,3}|\d{1,3}[A-Z]{0,2}T[A-Z0-9]{0,2})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex hinglishWords = new Regex(
            @"\b(hai|hain|kya|karna|karu|chahiye|ghante|ghanta|batao|bataiye|mein|kaun|kaise|kab|abhi|aaj|pe|par|wala|wali)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static PlanIntent TryParse(string question, IList<PlanSiteInfo> sites, int previousSiteId, int defaultHours)
        {
            if (string.IsNullOrWhiteSpace(question))
            {
                return null;
            }

            string q = question.Trim();
            bool devanagari = ContainsDevanagari(q);
            bool hasPlanWord = planWords.IsMatch(q) || ContainsDevanagariPlanWord(q);
            Match hoursMatch = hoursPattern.Match(q);
            bool hasHours = hoursMatch.Success || halfDay.IsMatch(q) || fullDay.IsMatch(q) || ContainsDevanagariHours(q);
            bool mentionsMaintenance = q.IndexOf("maintenance", StringComparison.OrdinalIgnoreCase) >= 0
                || q.IndexOf("maintenence", StringComparison.OrdinalIgnoreCase) >= 0
                || q.IndexOf("mainten", StringComparison.OrdinalIgnoreCase) >= 0;

            if (monitoringWords.IsMatch(q) && !hasPlanWord)
            {
                return null;
            }

            PlanSiteInfo site = MatchSite(q, sites);
            string assetText = MatchAsset(q, site);

            bool isPlan = hasPlanWord || (hasHours && (site != null || previousSiteId > 0)) || (mentionsMaintenance && site != null && assetText == null);
            if (!isPlan)
            {
                return null;
            }

            PlanIntent intent = new PlanIntent();
            intent.AssetText = assetText;
            intent.Hours = defaultHours;
            intent.HoursGiven = false;

            if (hoursMatch.Success)
            {
                double hours;
                if (double.TryParse(hoursMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out hours))
                {
                    intent.Hours = (int)Math.Round(Math.Max(1.0, Math.Min(12.0, hours)));
                    intent.HoursGiven = true;
                }
            }
            else if (halfDay.IsMatch(q))
            {
                intent.Hours = 4;
                intent.HoursGiven = true;
            }
            else if (fullDay.IsMatch(q))
            {
                intent.Hours = 8;
                intent.HoursGiven = true;
            }
            else
            {
                int devHours = DevanagariHours(q);
                if (devHours > 0)
                {
                    intent.Hours = devHours;
                    intent.HoursGiven = true;
                }
            }

            if (devanagari)
            {
                intent.Language = "hi";
            }
            else if (hinglishWords.Matches(q).Count >= 2)
            {
                intent.Language = "hinglish";
            }
            else
            {
                intent.Language = "en";
            }

            if (site == null && previousSiteId > 0 && sites != null)
            {
                site = sites.FirstOrDefault(s => s.SiteId == previousSiteId);
            }

            intent.Site = site;
            intent.AskForSite = site == null;
            return intent;
        }

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }

            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static PlanSiteInfo MatchSite(string q, IList<PlanSiteInfo> sites)
        {
            if (sites == null || sites.Count == 0)
            {
                return null;
            }

            string[] tokens = Regex.Split(q.ToLowerInvariant(), @"[^a-z0-9]+").Where(t => t.Length > 0).ToArray();
            string joined = Normalize(q);
            PlanSiteInfo best = null;
            int bestScore = 0;
            bool tie = false;

            foreach (PlanSiteInfo s in sites)
            {
                int score = 0;
                string name = Normalize(s.Name);
                string code = Normalize(s.Code);

                if (name.Length >= 4 && joined.Contains(name))
                {
                    score = 100 + name.Length;
                }
                else if (name.Length >= 4)
                {
                    foreach (string t in tokens)
                    {
                        if (t.Length >= 4 && (name.StartsWith(t, StringComparison.Ordinal) || t.StartsWith(name, StringComparison.Ordinal)))
                        {
                            score = Math.Max(score, 50 + t.Length);
                        }
                    }
                }

                if (score == 0 && code.Length >= 2)
                {
                    foreach (string t in tokens)
                    {
                        if (t == code)
                        {
                            score = 40;
                        }
                    }
                }

                if (score > bestScore)
                {
                    best = s;
                    bestScore = score;
                    tie = false;
                }
                else if (score > 0 && score == bestScore && best != null && best.SiteId != s.SiteId)
                {
                    tie = true;
                }
            }

            if (tie)
            {
                return null;
            }

            return best;
        }

        private static string MatchAsset(string q, PlanSiteInfo site)
        {
            foreach (Match m in assetPattern.Matches(q))
            {
                string v = m.Value.Trim();
                string n = Normalize(v);

                // A bare number followed by "h" was caught by the hours rule, and a station code
                // can look like an asset code (e.g. "SK"); skip both.
                if (site != null && (n == Normalize(site.Code) || n == Normalize(site.Name)))
                {
                    continue;
                }

                if (Regex.IsMatch(v, @"^\d{1,3}T$", RegexOptions.IgnoreCase) || Regex.IsMatch(v, @"^[A-Za-z]", RegexOptions.None) || Regex.IsMatch(v, @"^\d{1,3}[A-Za-z]{0,2}T", RegexOptions.IgnoreCase))
                {
                    return v.ToUpperInvariant();
                }
            }

            return null;
        }

        private static bool ContainsDevanagari(string s)
        {
            foreach (char c in s)
            {
                if (c >= '\u0900' && c <= '\u097f')
                {
                    return true;
                }
            }

            return false;
        }

        // "kya karna" / "yojana" / "kaam" written in Devanagari.
        private static bool ContainsDevanagariPlanWord(string s)
        {
            string[] words =
            {
                "\u0915\u094d\u092f\u093e \u0915\u0930\u0928\u093e",
                "\u092f\u094b\u091c\u0928\u093e",
                "\u0915\u093e\u092e",
                "\u092e\u0947\u0902\u091f\u0947\u0928\u0947\u0902\u0938"
            };
            foreach (string w in words)
            {
                if (s.Contains(w))
                {
                    return true;
                }
            }

            return false;
        }

        // "<n> ghante" written in Devanagari.
        private static bool ContainsDevanagariHours(string s)
        {
            return s.Contains("\u0918\u0902\u091f\u0947") || s.Contains("\u0918\u0902\u091f\u093e");
        }

        private static int DevanagariHours(string s)
        {
            Match m = Regex.Match(s, @"(\d{1,2})\s*(\u0918\u0902\u091f\u0947|\u0918\u0902\u091f\u093e)");
            int h;
            if (m.Success && int.TryParse(m.Groups[1].Value, out h))
            {
                return Math.Max(1, Math.Min(12, h));
            }

            return 0;
        }
    }

    // =====================================================================================
    //  StationPlanBuilder -- one call, whole station plan.
    //
    //  1. Inventory, saved roster worksheet and the division's 15-day alerts are read in
    //     parallel (each cached).
    //  2. The assets worth a card are picked: an open roster item or any alert in the window.
    //  3. Only those assets get a live model read (track / pm-operation-v4), in parallel,
    //     each with its own timeout and cache.
    //  4. PlanRules turns the evidence into cards: priority, verdict, action, minutes.
    //  5. Cards are ranked and fitted into the time on site.
    //
    //  The builder never throws for a missing source -- it notes the gap in
    //  plan.Source.Partial and the page shows it.
    // =====================================================================================
    public static class StationPlanBuilder
    {
        private sealed class Fetch<T> where T : class
        {
            public T Value;
            public string Error;
        }

        internal sealed class AssetWork
        {
            public AssetWork()
            {
                Items = new List<JObject>();
                Alerts = new List<PlanAlertRow>();
            }

            public PlanAssetInfo Info;
            public bool InInventory;
            public List<JObject> Items;
            public List<PlanAlertRow> Alerts;
            public JObject TrackLive;
            public JObject PmLive;
            public string TrackError;
            public string PmError;
        }

        public static async Task<IList<PlanSiteInfo>> GetSitesCachedAsync(IStationPlanSource source, StationPlanOptions options, CancellationToken cancellationToken)
        {
            return await PlanCache.GetOrLoadAsync(
                "plan:sites",
                options.InventoryCacheSeconds,
                () => source.GetSitesAsync(cancellationToken)).ConfigureAwait(false);
        }

        public static async Task<StationPlan> BuildAsync(PlanIntent intent, IStationPlanSource source, StationPlanOptions options, CancellationToken cancellationToken)
        {
            if (intent == null || intent.Site == null)
            {
                throw new ArgumentException("A plan needs a resolved station.");
            }

            Stopwatch watch = Stopwatch.StartNew();
            PlanSiteInfo site = intent.Site;
            DateTime now = options.Now;
            DateTime day = now.Date;
            DateTime from = day.AddDays(-(options.WindowDays - 1));

            StationPlan plan = new StationPlan();
            plan.Site = new PlanSiteOut { Id = site.SiteId, DivisionId = site.DivisionId, Code = site.Code, Name = site.Name };
            plan.Hours = intent.Hours;
            plan.Language = intent.Language;
            plan.Source.LiveCheckAt = now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

            int coreTimeout = Math.Max(2000, options.BuildTimeoutMs - 3000);

            // ---- 1. core reads in parallel -------------------------------------------------
            Task<Fetch<IList<PlanAssetInfo>>> assetsTask = TryFetchAsync(
                () => PlanCache.GetOrLoadAsync(
                    "plan:assets:" + site.SiteId,
                    options.InventoryCacheSeconds,
                    () => source.GetAssetsAsync(site.SiteId, cancellationToken)),
                coreTimeout);

            Task<Fetch<JToken>> sheetTask = TryFetchAsync(
                () => PlanCache.GetOrLoadAsync(
                    "plan:sheet:" + site.DivisionId + ":" + site.SiteId + ":" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    options.CacheSeconds,
                    () => source.GetWorksheetAsync(site.DivisionId, site.SiteId, day, cancellationToken)),
                coreTimeout);

            Task<Fetch<IList<PlanAlertRow>>> alertsTask = TryFetchAsync(
                () => PlanCache.GetOrLoadAsync(
                    "plan:alerts:" + site.DivisionId + ":" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    options.AlertCacheSeconds,
                    () => source.GetAlertsAsync(site.DivisionId, from, now, cancellationToken)),
                coreTimeout);

            await Task.WhenAll(assetsTask, sheetTask, alertsTask).ConfigureAwait(false);

            Fetch<IList<PlanAssetInfo>> assets = assetsTask.Result;
            Fetch<JToken> sheet = sheetTask.Result;
            Fetch<IList<PlanAlertRow>> alerts = alertsTask.Result;

            if (assets.Error != null)
            {
                plan.Source.Partial.Add("Asset inventory unavailable (" + assets.Error + ")");
            }

            if (sheet.Error != null)
            {
                plan.Source.Partial.Add("Roster run unavailable (" + sheet.Error + ")");
            }

            if (alerts.Error != null)
            {
                plan.Source.Partial.Add("Alert history unavailable (" + alerts.Error + ")");
            }

            // ---- 2. evidence per asset -----------------------------------------------------
            Dictionary<int, AssetWork> work = new Dictionary<int, AssetWork>();

            if (assets.Value != null)
            {
                foreach (PlanAssetInfo a in assets.Value)
                {
                    if (a == null || a.AssetId <= 0 || a.Family == PlanFamily.Other)
                    {
                        continue;
                    }

                    if (!work.ContainsKey(a.AssetId))
                    {
                        work[a.AssetId] = new AssetWork { Info = a, InInventory = true };
                    }
                }
            }

            List<JObject> sheetItems = PlanRules.SheetItems(sheet.Value);
            plan.Source.RosterRunAt = PlanRules.SheetRunAt(sheet.Value, sheetItems);

            foreach (JObject item in sheetItems)
            {
                int assetId = PlanRules.Int(item, "AssetId", "assetId");
                if (assetId <= 0)
                {
                    continue;
                }

                AssetWork w = GetOrAdd(work, assetId, PlanRules.Str(item, "AssetName", "assetName", "Name"),
                    PlanRules.Int(item, "AssetTypeId", "assetTypeId"),
                    PlanRules.Str(item, "AssetFamily", "assetFamily", "AssetTypeName", "assetTypeName"));
                if (w != null)
                {
                    w.Items.Add(item);
                }
            }

            List<PlanAlertRow> siteAlerts = new List<PlanAlertRow>();
            if (alerts.Value != null)
            {
                foreach (PlanAlertRow r in alerts.Value)
                {
                    if (r == null || r.SiteId != site.SiteId || r.AssetId <= 0)
                    {
                        continue;
                    }

                    if (r.SetAt < from || r.SetAt > now.AddMinutes(5))
                    {
                        continue;
                    }

                    siteAlerts.Add(r);
                    AssetWork w = GetOrAdd(work, r.AssetId, r.AssetName, r.AssetTypeId, r.AssetTypeName);
                    if (w != null)
                    {
                        w.Alerts.Add(r);
                    }
                }
            }

            plan.Source.AlertsRead = siteAlerts.Count;

            // ---- single-asset request -------------------------------------------------------
            AssetWork target = null;
            if (!string.IsNullOrEmpty(intent.AssetText))
            {
                target = FindAsset(work.Values, intent.AssetText);
                if (target == null)
                {
                    plan.NotFoundAsset = intent.AssetText;
                }
                else
                {
                    plan.SingleAsset = true;
                }
            }

            // ---- candidates ----------------------------------------------------------------
            List<AssetWork> candidates = new List<AssetWork>();
            foreach (AssetWork w in work.Values)
            {
                if (target != null && w != target)
                {
                    continue;
                }

                bool openItem = w.Items.Any(PlanRules.IsOpenItem);
                if (w == target || openItem || w.Alerts.Count > 0)
                {
                    candidates.Add(w);
                }
            }

            // ---- 3. live model reads, flagged assets only -----------------------------------
            await ReadModelsAsync(candidates, source, site, options, watch, plan, cancellationToken).ConfigureAwait(false);

            // ---- 4. cards ------------------------------------------------------------------
            PlanRules.Context ctx = new PlanRules.Context(options, from, now, site);
            List<PlanAssetCard> cards = new List<PlanAssetCard>();
            foreach (AssetWork w in candidates)
            {
                PlanAssetCard card = PlanRules.BuildCard(w, ctx);
                if (card != null && (card.Priority != PlanPriority.None || w == target))
                {
                    cards.Add(card);
                }
            }

            cards = cards
                .OrderByDescending(c => PlanPriority.Rank(c.Priority))
                .ThenByDescending(c => c.Score)
                .ThenBy(c => c.Name, NaturalNameComparer.Instance)
                .ToList();

            // Keep every URGENT / INSPECT SOON card; cap MONITOR cards.
            List<PlanAssetCard> kept = new List<PlanAssetCard>();
            int monitorKept = 0;
            foreach (PlanAssetCard c in cards)
            {
                if (c.Priority == PlanPriority.Monitor || c.Priority == PlanPriority.None)
                {
                    if (monitorKept >= options.MaxCards)
                    {
                        continue;
                    }

                    monitorKept++;
                }

                kept.Add(c);
            }

            for (int i = 0; i < kept.Count; i++)
            {
                kept[i].Rank = i + 1;
                kept[i].Key = "a" + kept[i].AssetId.ToString(CultureInfo.InvariantCulture);
            }

            plan.Assets = kept;

            // ---- 5. time budget, families, headline ------------------------------------------
            FitTimeBudget(plan, options);
            BuildFamilies(plan, work, cards);
            plan.Headline = BuildHeadline(siteAlerts, alerts.Error != null, kept.Count, plan);

            watch.Stop();
            plan.Source.BuildMs = watch.ElapsedMilliseconds;
            return plan;
        }

        private static AssetWork GetOrAdd(Dictionary<int, AssetWork> work, int assetId, string name, int typeId, string typeName)
        {
            AssetWork w;
            if (work.TryGetValue(assetId, out w))
            {
                if (string.IsNullOrEmpty(w.Info.Name) && !string.IsNullOrEmpty(name))
                {
                    w.Info.Name = name;
                }

                return w;
            }

            string family = typeId > 0 ? PlanFamily.FromTypeId(typeId) : PlanFamily.FromTypeName(typeName);
            if (family == PlanFamily.Other)
            {
                return null;
            }

            w = new AssetWork
            {
                Info = new PlanAssetInfo
                {
                    AssetId = assetId,
                    Name = string.IsNullOrEmpty(name) ? "Asset " + assetId.ToString(CultureInfo.InvariantCulture) : name,
                    TypeId = typeId,
                    Family = family
                },
                InInventory = false
            };
            work[assetId] = w;
            return w;
        }

        private static AssetWork FindAsset(IEnumerable<AssetWork> all, string text)
        {
            string wanted = PlanIntentParser.Normalize(text);
            if (wanted.Length == 0)
            {
                return null;
            }

            List<AssetWork> list = all.ToList();
            foreach (AssetWork w in list)
            {
                if (PlanIntentParser.Normalize(w.Info.Name) == wanted)
                {
                    return w;
                }
            }

            // "PT-115" should find "PT-115/116"; "PM07" should find "PM-07".
            foreach (AssetWork w in list)
            {
                string n = PlanIntentParser.Normalize(w.Info.Name);
                if (n.StartsWith(wanted, StringComparison.Ordinal) && wanted.Length >= 2)
                {
                    return w;
                }
            }

            string digits = Regex.Replace(wanted, "[^0-9]", "");
            string letters = Regex.Replace(wanted, "[0-9]", "");
            foreach (AssetWork w in list)
            {
                string n = PlanIntentParser.Normalize(w.Info.Name);
                if (digits.Length > 0 && Regex.Replace(n, "[^0-9]", "").StartsWith(digits, StringComparison.Ordinal)
                    && Regex.Replace(n, "[0-9]", "") == letters)
                {
                    return w;
                }
            }

            return null;
        }

        private static async Task ReadModelsAsync(List<AssetWork> candidates, IStationPlanSource source, PlanSiteInfo site,
            StationPlanOptions options, Stopwatch watch, StationPlan plan, CancellationToken cancellationToken)
        {
            List<Task> tasks = new List<Task>();
            SemaphoreSlim gate = new SemaphoreSlim(Math.Max(1, options.MaxParallel));

            foreach (AssetWork w in candidates)
            {
                AssetWork asset = w;
                if (asset.Info.Family == PlanFamily.Track)
                {
                    tasks.Add(ReadOneAsync(gate, options, watch, () => PlanCache.GetOrLoadAsync(
                        "plan:trk:" + site.SiteId + ":" + asset.Info.AssetId,
                        options.CacheSeconds,
                        () => source.GetTrackModelAsync(site.SiteId, asset.Info.AssetId, cancellationToken)),
                        r => asset.TrackLive = r,
                        e => asset.TrackError = e));
                }
                else if (asset.Info.Family == PlanFamily.Point)
                {
                    tasks.Add(ReadOneAsync(gate, options, watch, () => PlanCache.GetOrLoadAsync(
                        "plan:pm:" + site.SiteId + ":" + asset.Info.AssetId,
                        options.CacheSeconds,
                        () => source.GetPmModelAsync(site.SiteId, asset.Info.AssetId, cancellationToken)),
                        r => asset.PmLive = r,
                        e => asset.PmError = e));
                }
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            int trackMissing = candidates.Count(c => c.Info.Family == PlanFamily.Track && c.TrackLive == null);
            int pmMissing = candidates.Count(c => c.Info.Family == PlanFamily.Point && c.PmLive == null);
            if (trackMissing > 0)
            {
                plan.Source.Partial.Add("Live track model not read for " + trackMissing + " track circuit(s); roster run values used");
            }

            if (pmMissing > 0)
            {
                plan.Source.Partial.Add("Live point-machine model not read for " + pmMissing + " machine(s); roster run values used");
            }
        }

        private static async Task ReadOneAsync(SemaphoreSlim gate, StationPlanOptions options, Stopwatch watch,
            Func<Task<JObject>> read, Action<JObject> onValue, Action<string> onError)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                long remaining = options.BuildTimeoutMs - 1500 - watch.ElapsedMilliseconds;
                if (remaining < 500)
                {
                    onError("build time budget used up");
                    return;
                }

                int timeout = (int)Math.Min(options.LiveTimeoutMs, remaining);
                Fetch<JObject> f = await TryFetchAsync(read, timeout).ConfigureAwait(false);
                if (f.Value != null)
                {
                    onValue(f.Value);
                }
                else
                {
                    onError(f.Error);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        private static async Task<Fetch<T>> TryFetchAsync<T>(Func<Task<T>> work, int timeoutMs) where T : class
        {
            Fetch<T> result = new Fetch<T>();
            try
            {
                Task<T> task = work();
                Task winner = await Task.WhenAny(task, Task.Delay(timeoutMs)).ConfigureAwait(false);
                if (winner != task)
                {
                    Observe(task);
                    result.Error = "timeout after " + timeoutMs + " ms";
                    return result;
                }

                result.Value = await task.ConfigureAwait(false);
                if (result.Value == null)
                {
                    result.Error = "no data";
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.GetType().Name + ": " + ex.Message;
            }

            return result;
        }

        private static void Observe(Task task)
        {
            task.ContinueWith(
                t =>
                {
                    AggregateException ignored = t.Exception;
                },
                TaskContinuationOptions.OnlyOnFaulted);
        }

        private static void FitTimeBudget(StationPlan plan, StationPlanOptions options)
        {
            int total = plan.Hours * 60;
            plan.TimeBudget.SetupLabel = "Permit, block and walk-in";
            plan.TimeBudget.SetupMinutes = options.SetupMinutes;
            int left = total - options.SetupMinutes;

            foreach (PlanAssetCard c in plan.Assets)
            {
                if (c.Minutes <= left)
                {
                    plan.TimeBudget.InPlan.Add(c.Key);
                    left -= c.Minutes;
                }
                else
                {
                    plan.TimeBudget.NextVisit.Add(c.Key);
                }
            }

            plan.TimeBudget.BufferMinutes = Math.Max(0, left);
        }

        private static void BuildFamilies(StationPlan plan, Dictionary<int, AssetWork> work, List<PlanAssetCard> allCards)
        {
            string[] order = { PlanFamily.Track, PlanFamily.Point, PlanFamily.Signal, PlanFamily.Ips };
            HashSet<int> withCard = new HashSet<int>(allCards.Where(c => c.Priority != PlanPriority.None).Select(c => c.AssetId));

            foreach (string fam in order)
            {
                List<AssetWork> inFamily = work.Values.Where(w => w.Info.Family == fam).ToList();
                List<PlanAssetCard> famCards = allCards.Where(c => c.Family == fam).ToList();
                if (inFamily.Count == 0 && famCards.Count == 0)
                {
                    continue;
                }

                PlanFamilySummary s = new PlanFamilySummary();
                s.Family = fam;
                s.Label = PlanFamily.Label(fam);
                s.Total = inFamily.Count;
                s.Urgent = famCards.Count(c => c.Priority == PlanPriority.Urgent);
                s.Soon = famCards.Count(c => c.Priority == PlanPriority.Soon);
                s.Monitor = famCards.Count(c => c.Priority == PlanPriority.Monitor);
                s.Healthy = inFamily
                    .Where(w => w.InInventory && !withCard.Contains(w.Info.AssetId))
                    .Select(w => w.Info.Name)
                    .OrderBy(n => n, NaturalNameComparer.Instance)
                    .ToList();
                plan.Families.Add(s);
            }
        }

        private static string BuildHeadline(List<PlanAlertRow> siteAlerts, bool alertsFailed, int cardCount, StationPlan plan)
        {
            if (alertsFailed)
            {
                return "Alert history could not be read. This plan uses the roster run and the live models only.";
            }

            int active = siteAlerts.Count(a => !a.ResetAt.HasValue);
            StringBuilder sb = new StringBuilder();
            if (active > 0)
            {
                sb.Append(active == 1 ? "1 alert is active right now. " : active + " alerts are active right now. ");
                sb.Append(siteAlerts.Count + " alerts in the last 15 days.");
            }
            else if (siteAlerts.Count > 0)
            {
                sb.Append("No alert is active right now. ");
                sb.Append(siteAlerts.Count == 1 ? "1 alert in the last 15 days, reset." : siteAlerts.Count + " alerts in the last 15 days, all reset.");
            }
            else
            {
                sb.Append("No alerts in the last 15 days.");
            }

            if (cardCount == 0)
            {
                sb.Append(" Nothing at this station needs attention today.");
            }

            if (!string.IsNullOrEmpty(plan.NotFoundAsset))
            {
                sb.Append(" Asset " + plan.NotFoundAsset + " was not found here, so the whole station is shown.");
            }

            return sb.ToString();
        }
    }

    // "1T, 2T, 10T" rather than "10T, 1T, 2T": digit runs compare as numbers.
    internal sealed class NaturalNameComparer : IComparer<string>
    {
        public static readonly NaturalNameComparer Instance = new NaturalNameComparer();

        public int Compare(string x, string y)
        {
            string a = x ?? "";
            string b = y ?? "";
            int i = 0;
            int j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i;
                    int sj = j;
                    while (i < a.Length && char.IsDigit(a[i]))
                    {
                        i++;
                    }

                    while (j < b.Length && char.IsDigit(b[j]))
                    {
                        j++;
                    }

                    string na = a.Substring(si, i - si).TrimStart('0');
                    string nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length)
                    {
                        return na.Length.CompareTo(nb.Length);
                    }

                    int c = string.CompareOrdinal(na, nb);
                    if (c != 0)
                    {
                        return c;
                    }
                }
                else
                {
                    int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (c != 0)
                    {
                        return c;
                    }

                    i++;
                    j++;
                }
            }

            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
