using E7MRIWeb.Areas.FRS25.Controllers;
using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeMcpDomain -- the production IJudgeMcp: the same Domain McpClient singleton, E7 token and
    // client lock that AnalysisEngine.CallToolRoutedAsync uses for AnalyzeAlertJson. Nothing is
    // configured twice: McpServerUrl, E7LoginBaseUrl/User/Pass and MaxToolResultChars come from
    // web.config exactly as they do for AiChat.
    //
    // This is the ONLY Judge file that touches Domain. Everything else compiles without it, which
    // is how the parity harness runs the judge logic outside IIS.
    // ============================================================================================
    internal sealed class JudgeMcpDomain : IJudgeMcp
    {
        public async Task<string> CallToolAsync(string toolName, JObject args, CancellationToken ct)
        {
            string e7tok = await E7ApiToken.GetTokenAsync(ct).ConfigureAwait(false);
            if (e7tok == null && !string.IsNullOrEmpty(E7ApiToken.LastError))
            {
                JudgeLog.Write("WARN", "E7TOKEN", "judge: " + E7ApiToken.LastError);
            }

            bool taken = false;
            try
            {
                if (AnalysisEngine._serializeClients)
                {
                    await AnalysisEngine._mcp1Lock.WaitAsync(ct).ConfigureAwait(false);
                    taken = true;
                }

                string result = await AnalysisEngine._mcp.Value.CallToolAsync(toolName, args, AnalysisEngine._maxToolLen, e7tok).ConfigureAwait(false);
                if (e7tok != null && result != null && result.IndexOf("e7_session_expired", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    E7ApiToken.Invalidate();
                    JudgeLog.Write("WARN", "E7TOKEN", "judge: E7 session expired on '" + toolName + "'; token invalidated");
                }

                return result ?? "";
            }
            finally
            {
                if (taken)
                {
                    AnalysisEngine._mcp1Lock.Release();
                }
            }
        }
    }
}
