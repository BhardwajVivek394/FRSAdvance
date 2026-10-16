using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Mvc;
using E7FRSAdvance.AiCore;

namespace E7FRSAdvance.Utility
{
    // 1.0.190.0: keeps E7FRSAdvance.AiCore.AiModelConfig filled from the AiModelConfig/GetDefault API.
    // Called the same way as every other E7 API call: HttpClientFactory (APIBaseUrl / APIBaseUrlSecure)
    // with ClsHttpContent.LoginUser.Token. Runs at the START of a logged-in request, where the session
    // is available; the AI code reads the cached answer later (after awaits, no HttpContext needed).
    // - At most one call per AiModelConfigCacheSec (300 s); other requests skip straight through.
    // - The call itself runs in the background. Only when nothing has been fetched yet does this
    //   request wait for it, at most AiModelConfigTimeoutMs (3000 ms).
    // - Never throws: a failure is logged (ai-log, tag AIMODEL) and Web.config models stay in use.
    public class AiModelConfigFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            try
            {
                Refresh();
            }
            catch
            {
                // no session on this request (sessionless controller) -- nothing to do
            }
            base.OnActionExecuting(filterContext);
        }

        private static void Refresh()
        {
            if (!AiModelConfig.Enabled || ClsHttpContent.LoginUser == null)
            {
                return;
            }
            if (!AiModelConfig.TryBeginRefresh())
            {
                return;
            }

            Task task;
            try
            {
                HttpClientFactory hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token);
                HttpClient client = hcf.client;
                string path = AiModelConfig.Path;
                string source = new Uri(client.BaseAddress, path).ToString();
                task = Task.Run(() => FetchAsync(client, path, source));
            }
            catch (Exception ex)
            {
                AiModelConfig.ReportFailure("AiModelConfig request setup failed: " + ex.Message);
                return;
            }

            if (!AiModelConfig.HasValue)
            {
                // first answer since start-up: wait briefly so this request already uses it
                try
                {
                    task.Wait(AiModelConfig.TimeoutMs);
                }
                catch
                {
                }
            }
        }

        private static async Task FetchAsync(HttpClient client, string path, string source)
        {
            try
            {
                using (CancellationTokenSource cts = new CancellationTokenSource(AiModelConfig.TimeoutMs))
                using (HttpResponseMessage response = await client.GetAsync(path, cts.Token).ConfigureAwait(false))
                {
                    string jsonString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        AiModelConfig.Update(jsonString, source);
                    }
                    else
                    {
                        AiModelConfig.ReportFailure("AiModelConfig HTTP " + (int)response.StatusCode
                            + " from " + source + ": " + Trunc(jsonString));
                    }
                }
            }
            catch (Exception ex)
            {
                AiModelConfig.ReportFailure("AiModelConfig call to " + source + " failed: " + ex.Message);
            }
            finally
            {
                client.Dispose();
            }
        }

        private static string Trunc(string text)
        {
            if (text == null)
            {
                return "";
            }
            if (text.Length > 200)
            {
                return text.Substring(0, 200);
            }
            return text;
        }
    }
}
