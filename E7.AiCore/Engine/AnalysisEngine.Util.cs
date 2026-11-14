using Domain;
using E7MRIWeb.Areas.FRS25.Controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore
{
    // AnalysisEngine -- Util.
    // Moved verbatim from AiChatController.Util.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {
        // Moved to E7.AiCore.AiText.Trunc in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string Trunc(string s, int n) => AiText.Trunc(s, n);

        // Moved to E7.AiCore.AiText.FixedTimeEquals in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static bool FixedTimeEquals(string a, string b) => AiText.FixedTimeEquals(a, b);

        // Moved to E7.AiCore.AiText.ShortHash in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string ShortHash(string s0) => AiText.ShortHash(s0);

        // Moved to E7.AiCore.AiJson.ClampDecimalToInt in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static int ClampDecimalToInt(JToken t, int lo, int hi, int def) => AiJson.ClampDecimalToInt(t, lo, hi, def);

        // Moved to E7.AiCore.AiTime.TryParseFlexibleDate in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static bool TryParseFlexibleDate(string raw, out DateTime dt, out bool wasDdFmt) => AiTime.TryParseFlexibleDate(raw, out dt, out wasDdFmt);

        // Moved to E7.AiCore.AiJson.PropCI in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static JToken PropCI(JObject jo, string name) => AiJson.PropCI(jo, name);

        // Moved to E7.AiCore.AiJson.Truthy in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static bool Truthy(JToken t) => AiJson.Truthy(t);

        // Moved to E7.AiCore.AiJson.ZeroIdentifier in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string ZeroIdentifier(JToken args) => AiJson.ZeroIdentifier(args);

        // Moved to E7.AiCore.AiTime.ToEpochIst in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static long ToEpochIst(DateTime wallClockIst) => AiTime.ToEpochIst(wallClockIst);

        // Moved to E7.AiCore.AiJson.GetCtx in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string GetCtx(JObject o, string key) => AiJson.GetCtx(o, key);

        // Moved to E7.AiCore.AiTime.TryParseAlertTime in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static bool TryParseAlertTime(string s, out DateTime dt) => AiTime.TryParseAlertTime(s, out dt);

        // Moved to E7.AiCore.AiTime.ResolveIstZone in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static TimeZoneInfo ResolveIstZone() => AiTime.ResolveIstZone();
    }
}
