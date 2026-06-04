using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class BlogController : Controller
    {
        // GET: Blog
        public ActionResult Index()
        {
            return View(new BlogLister());
        }

        public PartialViewResult GetAllBlogLister(BlogLister mBlogLister)
        {
            mBlogLister.Pager.Take = mBlogLister.Pager.PageSize;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mBlogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Blog/GetAllBlogLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mBlogLister = JsonConvert.DeserializeObject<BlogLister>(jsonString);
                        if (mBlogLister != null && mBlogLister.Blogs != null && mBlogLister.Blogs.Count > 0)
                        {
                            mBlogLister.Blogs.ForEach(x => {
                                foreach (var blogImage in x.BlogImages)
                                {
                                    blogImage.ImagePath = ConfigurationManager.AppSettings.Get("FileBaseUrl") + blogImage.ImagePath;
                                }
                            });
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            return PartialView(mBlogLister);
        }
    }
}