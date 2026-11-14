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
    // Chat training-text loader with file-change cache.
    // Moved verbatim from AiChatController (E7MRIWeb) in 1.0.162.0 -- Shared AI Core step 2a-2c.
    public static class TrainingText
    {
        internal static readonly string _trainingPath =
            System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "App_Data", "training_.txt");

        internal static readonly object _trainLock = new object();

        internal static long _trainingMtime = 0;


        // ── Training file ───────────────────────────────────────
        internal static string _trainingCache = null;


        public static string LoadTrainingText()
        {
            if (!System.IO.File.Exists(_trainingPath))
                return string.Empty;
            long mtime = new System.IO.FileInfo(_trainingPath).LastWriteTimeUtc.Ticks;
            lock (_trainLock)
            {
                if (_trainingCache != null && mtime == _trainingMtime)
                    return _trainingCache;
                string raw = System.IO.File.ReadAllText(_trainingPath, Encoding.UTF8).Trim();
                _trainingCache = string.IsNullOrEmpty(raw)
                    ? string.Empty
                    : "\n\n=== DOMAIN TRAINING ===\n" + raw + "\n=== END TRAINING ===";
                _trainingMtime = mtime;
                return _trainingCache;
            }
        }
    }
}
