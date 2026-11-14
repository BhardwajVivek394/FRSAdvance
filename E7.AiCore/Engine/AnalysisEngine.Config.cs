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
    // AnalysisEngine -- Config.
    // Moved verbatim from AiChatController.Config.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {
        // Moved to E7.AiCore in 1.0.163.0 (forwarder keeps call sites unchanged).
        internal static int ReadInt(string key, int def) => AiSettings.ParseInt(ConfigurationManager.AppSettings[key], def);

        // Moved to E7.AiCore in 1.0.163.0 (forwarder keeps call sites unchanged).
        internal static double ReadDbl(string key, double def) => AiSettings.ParseDbl(System.Configuration.ConfigurationManager.AppSettings[key], def);

        // Moved to E7.AiCore in 1.0.163.0 (forwarder keeps call sites unchanged).
        internal static bool ReadBool(string key, bool def) => AiSettings.ParseBool(ConfigurationManager.AppSettings[key], def);

        // Moved to E7.AiCore in 1.0.163.0 (forwarder keeps call sites unchanged).
        internal static int ClampCfg(int v, int lo, int hi) => AiSettings.ClampCfg(v, lo, hi);

        // Moved to E7.AiCore in 1.0.163.0 (forwarder keeps call sites unchanged).
        internal static string ReadStr(string key, string def) => AiSettings.ParseStr(ConfigurationManager.AppSettings[key], def);

        // Moved to E7.AiCore in 1.0.163.0 (forwarder keeps call sites unchanged).
        internal static long ReadLong(JObject o, string key) => AiSettings.ReadLong(o, key);
    }
}
