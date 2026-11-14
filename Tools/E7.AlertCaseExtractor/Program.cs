using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace E7.AlertCaseExtractor
{
    // E7.AlertCaseExtractor -- decided alerts to CSV for the Alert Judge.
    //
    //   E7.AlertCaseExtractor.exe              incremental (default; what Task Scheduler runs nightly)
    //   E7.AlertCaseExtractor.exe --full       re-read full history and rebuild every row
    //   E7.AlertCaseExtractor.exe --dry-run    do everything except write files
    //   E7.AlertCaseExtractor.exe --check      verify DB columns and counts; reads no alert data
    //   E7.AlertCaseExtractor.exe --replay     test the Judge's case matching on the newest cases; reads out\judge-cases.json only
    //
    // Exit codes: 0 ok, 1 finished with row errors (watermark kept) or the Judge's copy could not be written, 2 fatal.
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool full = args.Any(a => string.Equals(a, "--full", StringComparison.OrdinalIgnoreCase));
            bool dryRun = args.Any(a => string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase));
            bool check = args.Any(a => string.Equals(a, "--check", StringComparison.OrdinalIgnoreCase));
            bool replay = args.Any(a => string.Equals(a, "--replay", StringComparison.OrdinalIgnoreCase));

            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extractor-settings.json");
            ExtractorSettings cfg;
            RunLog log;
            try
            {
                cfg = ExtractorSettings.Load(settingsPath);
                log = new RunLog(Path.Combine(cfg.Root, "logs"));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FATAL settings: " + ex.Message);
                return 2;
            }

            log.Info("E7.AlertCaseExtractor " + typeof(Program).Assembly.GetName().Version + ", logic " + RowBuilder.LogicVersion
                + (check ? ", --check" : replay ? ", --replay" : full ? ", --full" : ", incremental") + (dryRun ? ", dry-run" : ""));
            if (!check && !replay)
            {
                log.Info(SourceReader.SiteFilterText(cfg));
            }

            try
            {
                if (replay)
                {
                    // 1.1.0: reads only out\judge-cases.json -- no database, nothing written
                    string casesPath = Path.Combine(cfg.Root, "out", JudgeCasesWriter.FileName);
                    if (!File.Exists(casesPath))
                    {
                        log.Error("replay: " + casesPath + " not found -- run the extractor once first");
                        return 2;
                    }

                    JudgeCasesWriter.Replay(E7.AiCore.Judge.JudgeCaseSet.Load(casesPath), log);
                    return 0;
                }

                if (check)
                {
                    SourceReader.Check(cfg, log);
                    return 0;
                }

                return Run(cfg, log, full, dryRun);
            }
            catch (Exception ex)
            {
                log.Error("FATAL " + ex.GetType().Name + ": " + ex.Message);
                return 2;
            }
        }

        private static int Run(ExtractorSettings cfg, RunLog log, bool forceFull, bool dryRun)
        {
            string stateDir = Path.Combine(cfg.Root, "state");
            string outDir = Path.Combine(cfg.Root, "out");
            string ledgerPath = Path.Combine(stateDir, "ledger.jsonl");
            string statePath = Path.Combine(stateDir, "state.json");

            Ledger ledger = Ledger.Load(ledgerPath);
            RunState state = RunState.Load(statePath);

            // Full read when asked, on the very first run, or when any stored row came from other logic.
            bool full = forceFull || ledger.Count == 0 || ledger.HasOtherLogic(RowBuilder.LogicVersion);
            if (full && !forceFull)
            {
                log.Info(ledger.Count == 0 ? "ledger empty: first run, reading full history" : "logic version changed: reading full history to rebuild every row");
            }

            DateTime from = cfg.HistoryStart;
            if (!full && state.Watermark.HasValue)
            {
                from = state.Watermark.Value.AddDays(-cfg.OverlapDays);
            }

            List<SourceRecord> records = SourceReader.Read(cfg, from, log);
            AddSiteGeo(cfg, records, SourceReader.ReadSiteGeo(cfg, log), log);
            WeatherCache wx = new WeatherCache(cfg, stateDir);

            int created = 0;
            int logic = 0;
            int changed = 0;
            int skipped = 0;
            int errors = 0;
            DateTime? maxUpdated = state.Watermark;
            foreach (SourceRecord r in records)
            {
                if (r.UpdatedAt.HasValue && (!maxUpdated.HasValue || r.UpdatedAt.Value > maxUpdated.Value))
                {
                    maxUpdated = r.UpdatedAt.Value;
                }

                try
                {
                    string station = RowBuilder.StationOf(r);
                    string hash = RowBuilder.SourceHash(r, wx.StateFor(station, r.SetTime));
                    string reason;
                    if (!ledger.NeedsProcessing(r.Key, hash, out reason))
                    {
                        skipped++;
                        continue;
                    }

                    LedgerEntry e = new LedgerEntry();
                    e.Key = r.Key;
                    e.LogicVersion = RowBuilder.LogicVersion;
                    e.SourceHash = hash;
                    e.Label = r.Label;
                    e.ProcessedUtc = DateTime.UtcNow;
                    e.Row = RowBuilder.Build(r, wx);
                    ledger.Put(e);

                    if (reason == "new")
                    {
                        created++;
                    }
                    else if (reason == "logic")
                    {
                        logic++;
                    }
                    else
                    {
                        changed++;
                    }
                }
                catch (Exception ex)
                {
                    // the alert stays as it was in the ledger (or absent) and is tried again next run
                    errors++;
                    log.Warn(r.Key + " (" + r.CauseCode + ") not processed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            int removed = 0;
            if (full && errors == 0)
            {
                removed = ledger.RemoveNotIn(new HashSet<string>(records.Select(x => x.Key), StringComparer.OrdinalIgnoreCase));
            }

            List<Dictionary<string, string>> rows = ledger.All
                .Where(x => x.Row != null)
                .OrderBy(x => GetOr(x.Row, "set_time"), StringComparer.Ordinal)
                .Select(x => new Dictionary<string, string>(x.Row, StringComparer.Ordinal))
                .ToList();
            CsvWriter.AddSiteContext(rows, cfg.SiteWindowMinutes);

            int pass = rows.Count(x => GetOr(x, "label") == "PASS");
            log.Info("read " + records.Count + ", new " + created + ", logic-rebuilt " + logic + ", changed " + changed
                + ", skipped " + skipped + ", errors " + errors + ", removed " + removed
                + ", weather calls " + wx.Calls + " | ledger " + rows.Count + " rows (" + pass + " PASS, " + (rows.Count - pass) + " SUPPRESSED)");

            if (dryRun)
            {
                log.Info("dry-run: nothing written");
                return errors > 0 ? 1 : 0;
            }

            ledger.Save();
            AtomicFile.Write(Path.Combine(outDir, "cases_all.csv"), CsvWriter.Build(rows, RowBuilder.Columns));

            // 1.1.0: the same cases in the form the Judge reads. Always written to out\; copied to the
            // web site only when JudgeCasesPath is set. A failed copy never loses the run: the CSV, the
            // ledger and out\judge-cases.json are already written, and the exit code says 1.
            E7.AiCore.Judge.JudgeCaseSet caseSet = JudgeCasesWriter.Build(rows, cfg, "E7.AlertCaseExtractor " + typeof(Program).Assembly.GetName().Version);
            string casesJson = caseSet.ToJson();
            AtomicFile.Write(Path.Combine(outDir, JudgeCasesWriter.FileName), casesJson);
            log.Info("wrote out/" + JudgeCasesWriter.FileName + ": " + caseSet.CaseCount + " case(s), " + caseSet.Causes.Count + " cause(s), " + (casesJson.Length / 1024) + " KB");
            bool copyFailed = false;
            if (string.IsNullOrWhiteSpace(cfg.JudgeCasesPath))
            {
                log.Info("JudgeCasesPath is blank: the Judge's copy was not written");
            }
            else
            {
                string target = JudgeCasesWriter.Target(cfg.JudgeCasesPath);
                try
                {
                    AtomicFile.Write(target, casesJson);
                    log.Info("copied the cases to " + target);
                }
                catch (Exception ex)
                {
                    copyFailed = true;
                    log.Error("could NOT write " + target + " (" + ex.GetType().Name + ": " + ex.Message + ") -- the Judge keeps its previous cases");
                }
            }

            // The watermark only moves after a clean run: a run with row errors re-reads the same window next time.
            if (errors == 0)
            {
                state.Watermark = maxUpdated;
            }

            state.LastRunUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            state.LastResult = errors == 0 ? "ok" : "errors=" + errors;
            state.Save(statePath);
            log.Info("wrote out/cases_all.csv, state/ledger.jsonl" + (errors == 0 ? ", watermark " + (state.Watermark.HasValue ? state.Watermark.Value.ToString("yyyy-MM-dd HH:mm:ss") : "none") : ", watermark NOT moved"));
            return errors > 0 || copyFailed ? 1 : 0;
        }

        /// <summary>
        /// Station code -> lat/long from the DB (by the alert's SiteId), for every station the settings file
        /// does not already list. A lat/long in extractor-settings.json always wins.
        /// </summary>
        private static void AddSiteGeo(ExtractorSettings cfg, List<SourceRecord> records, Dictionary<int, StationGeo> siteGeo, RunLog log)
        {
            int added = 0;
            foreach (SourceRecord r in records)
            {
                if (!r.SiteId.HasValue)
                {
                    continue;
                }

                string station = RowBuilder.StationOf(r);
                if (string.IsNullOrEmpty(station))
                {
                    continue;
                }

                StationGeo existing;
                if (cfg.Stations.TryGetValue(station, out existing) && existing != null && !(existing.Lat == 0 && existing.Lon == 0))
                {
                    continue;
                }

                StationGeo g;
                if (siteGeo.TryGetValue(r.SiteId.Value, out g))
                {
                    cfg.Stations[station] = g;
                    added++;
                }
            }

            log.Info("stations with lat/long: " + cfg.Stations.Count + " (" + added + " taken from the DB)");
        }

        private static string GetOr(Dictionary<string, string> row, string key)
        {
            string v;
            return row.TryGetValue(key, out v) && v != null ? v : "";
        }
    }
}
