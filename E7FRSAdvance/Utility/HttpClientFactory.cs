using System;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace E7FRSAdvance.Utility
{
    public class HttpClientFactory : IDisposable
    {
        public HttpClient client;

        public HttpClientFactory(string baseUrl = null, string mediaType = null, string token = null)
        {
            if (HttpContext.Current.Request.IsSecureConnection)
            {
                System.Net.ServicePointManager.SecurityProtocol =
             SecurityProtocolType.Tls12;

                // Ignore cert validation (dev only)
                System.Net.ServicePointManager.ServerCertificateValidationCallback +=
                    (sender, cert, chain, sslPolicyErrors) => true;
            }
          

            client = new HttpClient();
            client.Timeout = new TimeSpan(1, 20, 0);


            if (baseUrl == null)
            {
                if (HttpContext.Current.Request.IsSecureConnection)
                    client.BaseAddress = new Uri(ConfigurationManager.AppSettings.Get("APIBaseUrlSecure"));
                else
                    client.BaseAddress = new Uri(ConfigurationManager.AppSettings.Get("APIBaseUrl"));
            }
            else
                client.BaseAddress = new Uri(baseUrl);

            if (mediaType == null)
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            else
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));

            if (token != null)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            if (ClsHttpContent.LoginUser != null)
            {
                client.DefaultRequestHeaders.Add("UserId", ClsHttpContent.LoginUser.Id.ToString());
                client.DefaultRequestHeaders.Add("RoleId", ClsHttpContent.LoginUser.RoleId.ToString());

            }
        }

        public async Task<HttpResponseMessage> PostAsync(string controllerName, string srlzRequest)
        {
            return await client.PostAsync(controllerName, new StringContent(srlzRequest, Encoding.UTF8, "application/json"));
        }

        public async Task<HttpResponseMessage> GetAsync(string controllerName)
        {
            return await client.GetAsync(controllerName);
        }

        public void Dispose()
        {
            client = null;
            GC.Collect();
        }

    }
}