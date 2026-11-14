using System;

namespace E7.AiCore.Judge
{
    /// <summary>
    /// The judge's log line sink. Production (JudgeHost) points it at AnalysisEngine.AiLogStatic, so
    /// judge lines land in the same ai-log-YYYYMMDD.txt as AnalyzeAlertJson, tagged [JUDGE].
    /// Tests point it at the console. Never throws.
    /// </summary>
    internal static class JudgeLog
    {
        internal static Action<string, string, string> Sink;

        internal static void Write(string level, string tag, string message)
        {
            try
            {
                Action<string, string, string> s = Sink;
                if (s != null)
                {
                    s(level, tag, message);
                }
            }
            catch
            {
            }
        }
    }
}
