using System;

namespace E7.AiCore
{
    // AiUserContext -- how core code reads the logged-in user without referencing the web project.
    // The web app registers a reader in each controller constructor (ClsHttpContent.LoginUser) and the two role ids
    // from its Role enum. Core code calls Get() at the same point where it used to read ClsHttpContent.LoginUser,
    // so timing is unchanged (the session-bound user is only valid before the first await, as before).
    // The login-user type lives in the web project, so members are read late-bound (dynamic): the same property
    // names as the original code (FirstName, LastName, EmailAddress, IsSiteKeeping, RoleId, station members).
    public static class AiUserContext
    {
        public static Func<object> Current;     // set by the web app: () => ClsHttpContent.LoginUser
        public static int AdminRoleId;          // set by the web app: Utility.Role.Admin.GetHashCode()
        public static int UserRoleId;           // set by the web app: Utility.Role.User.GetHashCode()

        // null when no user (or no reader registered) -- callers already treat null as "not signed in".
        public static object Get()
        {
            Func<object> f = Current;
            return f == null ? null : f();
        }

        // For the two role checks that originally had no null guard: keep their original failure type.
        public static object GetRequired()
        {
            object u = Get();
            if (u == null) { throw new NullReferenceException("LoginUser is null"); }
            return u;
        }
    }
}
