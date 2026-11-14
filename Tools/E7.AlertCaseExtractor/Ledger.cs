using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace E7.AlertCaseExtractor
{
    /// <summary>One processed alert: which logic built it, from which source data, and the row it produced.</summary>
    internal sealed class LedgerEntry
    {
        public string Key;
        public string LogicVersion;
        public string SourceHash;
        public string Label;
        public DateTime ProcessedUtc;
        public Dictionary<string, string> Row;
    }

    // state/ledger.jsonl -- the "process once" memory. An alert is built again only when it is new,
    // when RowBuilder.LogicVersion moved on, or when its own source data changed (sourceHash).
    internal sealed class Ledger
    {
        private readonly string path;
        private readonly Dictionary<string, LedgerEntry> entries = new Dictionary<string, LedgerEntry>(StringComparer.OrdinalIgnoreCase);

        private Ledger(string path)
        {
            this.path = path;
        }

        internal int Count
        {
            get
            {
                return entries.Count;
            }
        }

        internal IEnumerable<LedgerEntry> All
        {
            get
            {
                return entries.Values;
            }
        }

        internal static Ledger Load(string path)
        {
            Ledger l = new Ledger(path);
            if (!File.Exists(path))
            {
                return l;
            }

            foreach (string line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                LedgerEntry e = JsonConvert.DeserializeObject<LedgerEntry>(line);
                if (e != null && !string.IsNullOrEmpty(e.Key))
                {
                    l.entries[e.Key] = e;
                }
            }

            return l;
        }

        /// <summary>True when any stored row was built by different logic: the run must re-read full history.</summary>
        internal bool HasOtherLogic(string logicVersion)
        {
            return entries.Values.Any(e => e.LogicVersion != logicVersion);
        }

        internal bool NeedsProcessing(string key, string sourceHash, out string reason)
        {
            LedgerEntry entry;
            if (!entries.TryGetValue(key, out entry))
            {
                reason = "new";
                return true;
            }

            if (entry.LogicVersion != RowBuilder.LogicVersion)
            {
                reason = "logic";
                return true;
            }

            if (entry.SourceHash != sourceHash)
            {
                reason = "changed";
                return true;
            }

            reason = "unchanged";
            return false;
        }

        internal void Put(LedgerEntry e)
        {
            entries[e.Key] = e;
        }

        /// <summary>Full runs only: drop alerts that are no longer human-decided (e.g. a decision was reverted).</summary>
        internal int RemoveNotIn(HashSet<string> keys)
        {
            List<string> gone = entries.Keys.Where(k => !keys.Contains(k)).ToList();
            foreach (string k in gone)
            {
                entries.Remove(k);
            }

            return gone.Count;
        }

        internal void Save()
        {
            StringBuilder sb = new StringBuilder();
            foreach (LedgerEntry e in entries.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                sb.Append(JsonConvert.SerializeObject(e, Formatting.None));
                sb.Append('\n');
            }

            AtomicFile.Write(path, sb.ToString());
        }
    }

    /// <summary>state/state.json -- the watermark. It only moves after a run that wrote everything successfully.</summary>
    internal sealed class RunState
    {
        public DateTime? Watermark;
        public string LastRunUtc;
        public string LastResult;

        internal static RunState Load(string path)
        {
            if (!File.Exists(path))
            {
                return new RunState();
            }

            RunState s = JsonConvert.DeserializeObject<RunState>(File.ReadAllText(path, Encoding.UTF8));
            if (s == null)
            {
                return new RunState();
            }

            return s;
        }

        internal void Save(string path)
        {
            AtomicFile.Write(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }

    internal static class AtomicFile
    {
        /// <summary>Write to a temp file beside the target, then swap it in, so a crash never leaves a half-written file.</summary>
        internal static void Write(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
    }

    internal sealed class RunLog
    {
        private readonly string path;

        internal RunLog(string logDir)
        {
            Directory.CreateDirectory(logDir);
            path = Path.Combine(logDir, "extractor-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
        }

        internal void Info(string message)
        {
            Write("INFO", message);
        }

        internal void Warn(string message)
        {
            Write("WARN", message);
        }

        internal void Error(string message)
        {
            Write("ERROR", message);
        }

        private void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + " " + message;
            Console.WriteLine(line);
            try
            {
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // a log write must never stop the run; the console still has the line
            }
        }
    }
}
