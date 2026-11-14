using System;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeHost -- production wiring. The web action calls JudgeHost.Engine; tests build a
    // JudgeEngine with a fake MCP and a fake model call instead. This file and JudgeMcpDomain.cs
    // are the only Judge files that know about the rest of E7.AiCore.
    // ============================================================================================
    internal static class JudgeHost
    {
        private static readonly object gate = new object();
        private static JudgeEngine engine;

        internal static JudgeEngine Engine
        {
            get
            {
                lock (gate)
                {
                    if (engine == null)
                    {
                        // 2.6 -- BOTH sinks. JudgeAIlogs is the judge's own file and depends on
                        // nothing; AiLogStatic is kept so nothing that already greps ai-log-*.txt
                        // breaks. Either one failing must not stop the other, hence the two trys.
                        JudgeLog.Sink = (level, tag, message) =>
                        {
                            try
                            {
                                // 2.8 -- tag TRACE means "already in the per-request trace", and the
                                // trace is flushed to JudgeAIlogs as one block per judgement. Writing
                                // it here as well is what made every history_get and WINDOW SKEW line
                                // appear twice in the 09.10.2026 log. The ai-log below still gets it.
                                if (!string.Equals(tag, "TRACE", StringComparison.Ordinal))
                                {
                                    JudgeFileLog.Write(level, tag, message);
                                }
                            }
                            catch
                            {
                            }

                            try
                            {
                                AnalysisEngine.AiLogStatic(level, tag, message);
                            }
                            catch
                            {
                            }
                        };

                        JudgeFileLog.Write("INFO", "JUDGE", "judge " + JudgeEnvelope.Build
                            + " starting; log file " + (JudgeFileLog.FilePath ?? "(not resolved)"));
                        engine = new JudgeEngine(new JudgeMcpDomain(), null, null, null);
                    }

                    return engine;
                }
            }
        }
    }
}
