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
    // IST time and alert-time parsing helpers; EdgeX dd-format constant.
    // Moved verbatim from AiChatController (E7MRIWeb) in 1.0.162.0 -- Shared AI Core step 2a-2c.
    public static class AiTime
    {

        // The API caps rows and returns the OLDEST ones, so a wide window silently
        // stops short of the alert. Say so IN the tool result: the model cannot see
        // that the last sample predates the incidence unless it is told.
        // Alert timestamps are IST wall-clock with no DateTimeKind, so
        // ToUniversalTime() silently used the SERVER's timezone -- on a UTC host the
        // comparison was 5h30m out, which is exactly the size of error that would let
        // a short history look complete. Convert with an explicit offset instead.
        public static readonly TimeSpan _istOffset = TimeSpan.FromMinutes(330);

        public const string _ddFmt = "ddMMyyyy_HHmmss";


        public static bool TryParseFlexibleDate(string raw, out DateTime dt, out bool wasDdFmt)
        {
            wasDdFmt = false;
            if (DateTime.TryParseExact(raw, _ddFmt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dt))
            {
                wasDdFmt = true; return true;
            }
            return DateTime.TryParse(raw, out dt);
        }

        public static long ToEpochIst(DateTime wallClockIst)
        {
            DateTime utc = DateTime.SpecifyKind(wallClockIst, DateTimeKind.Unspecified) - _istOffset;
            return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }


        // Alert time arrives as "HH:mm:ss/dd.MM.yyyy" (alert card) or
        // "dd/MM/yyyy HH:mm:ss" (board row); the MCP history tools expect
        // ddMMyyyy_HHmmss. That conversion is a step the model gets wrong --
        // and a wrong window returns an empty series, which the grounding gate
        // then correctly rejects as ungrounded. So compute it here and hand the
        // exact strings over.
        public static bool TryParseAlertTime(string s, out DateTime dt)
        {
            dt = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(s)) { return false; }
            string t = s.Trim();
            string[] fmts = {
                "HH:mm:ss/dd.MM.yyyy", "H:m:s/d.M.yyyy",
                "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:m:s",
                "dd.MM.yyyy HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss"
            };
            if (DateTime.TryParseExact(t, fmts,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out dt)) { return true; }
            return DateTime.TryParse(t,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out dt);
        }


        public static TimeZoneInfo ResolveIstZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
            catch { }
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
            catch { }
            return TimeZoneInfo.CreateCustomTimeZone(
                "IST", TimeSpan.FromMinutes(330), "India Standard Time", "IST");
        }
    }
}
