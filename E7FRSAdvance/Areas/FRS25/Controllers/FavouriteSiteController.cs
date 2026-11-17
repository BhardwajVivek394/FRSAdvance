using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using System;
using System.Linq;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    /// <summary>
    /// Per-user favourite site (exactly one). The header star picker and the
    /// star inside every Station dropdown call these endpoints; the layout
    /// renders the saved value so each page can pre-select it on load.
    /// </summary>
    // Not [Authenticate]: that filter also requires the controller to be one of the
    // user's menu items, and FavouriteSite is a header feature on every page, not a
    // menu. A logged-in user is enough here; the site list itself is already
    // limited to the user's own sites (SiteService.GetAll) and Save is re-checked by the API.
    [SessionState(System.Web.SessionState.SessionStateBehavior.ReadOnly)]
    public class FavouriteSiteController : Controller
    {
        private readonly ISiteService _siteService;

        public FavouriteSiteController(ISiteService siteService)
        {
            _siteService = siteService;
        }

        protected override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            if (ClsHttpContent.LoginUser == null)
            {
                // JSON instead of a redirect: these are AJAX calls from the header picker.
                filterContext.HttpContext.Response.StatusCode = 401;
                filterContext.HttpContext.Response.TrySkipIisCustomErrors = true;
                filterContext.Result = Json(new { ok = false, message = "Session expired. Please log in again." },
                    JsonRequestBehavior.AllowGet);
                return;
            }
            base.OnActionExecuting(filterContext);
        }

        private int CurrentUserId
        {
            get { var u = ClsHttpContent.LoginUser; return u == null ? 0 : u.Id; }
        }

        [HttpGet]
        public JsonResult Get()
        {
            return Json(new { ok = true, favourite = FavouriteSiteStore.GetForCurrentUser() }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Sites the user may pick from (same list the filters use).</summary>
        [HttpGet]
        public JsonResult Sites()
        {
            try
            {
                var sites = (_siteService.GetAll() ?? new System.Collections.Generic.List<Domain.Site>())
                    .Where(s => s != null && s.Id > 0)
                    .OrderBy(s => s.Name)
                    .Select(s => new { s.Id, s.Name, s.StationCode, s.ZoneId, s.DivisionId, s.ZoneName, s.DivisionName })
                    .ToList();
                return Json(new { ok = true, sites = sites }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = ex.Message, sites = new object[0] }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult Set(int siteId)
        {
            try
            {
                var userId = CurrentUserId;
                if (userId <= 0) return Json(new { ok = false, message = "Session expired. Please log in again." });

                // The API checks the site is active and assigned to this user, then
                // upserts dbo.UserFavouriteSite (one row per user).
                var fav = FavouriteSiteStore.SaveForCurrentUser(siteId);
                return Json(new { ok = true, favourite = fav });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult Clear()
        {
            try
            {
                FavouriteSiteStore.ClearForCurrentUser();
                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = ex.Message });
            }
        }
    }
}
