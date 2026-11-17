using System;
using System.Collections.Generic;
using System.Linq;

namespace E7FRSAdvance.Areas.FRS25.Helper
{
    /// <summary>
    /// Ported from 616 (E7MRIWeb.Areas.FRS25.Helper.SignalAspect).
    /// Single source of truth for the virtual Signal sub-types (Calling / Route / Shunt)
    /// shown as their own dashboard cards.
    /// </summary>
    public static class SignalAspect
    {
        // ── Real asset type ───────────────────────────────────────────
        public const int SignalAssetTypeId = (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL;
        public const string MainSignalName = "Main Signal";

        // ── Virtual asset-type ids (negative, so they never collide with DB ids) ──
        public const int Calling = -101;
        public const int Route = -102;
        public const int Shunt = -103;

        // ── Card names ────────────────────────────────────────────────
        public const string CallingName = "Calling Signal";
        public const string RouteName = "Route Signal";
        public const string ShuntName = "Shunt Signal";

        // ── Alert CauseCode fragments ─────────────────────────────────
        public const string CallingCauseCode = "COSIG";
        public const string RouteCauseCode = "ROSIG";
        public const string ShuntCauseCode = "SHSIG";

        // ── Route indications: each one present on a signal counts as one Route Signal ──
        public static readonly string[] RouteCodes = { "AUG", "BUG", "CUG", "DUG", "EUG", "FUG" };

        /// <summary>True for -101 / -102 / -103.</summary>
        public static bool IsSignalAspect(int assetTypeId)
        {
            return assetTypeId == Calling || assetTypeId == Route || assetTypeId == Shunt;
        }

        /// <summary>Virtual id → real id to send to the API (-101/-102/-103 → SIGNAL).</summary>
        public static int ToApiAssetTypeId(int assetTypeId)
        {
            return IsSignalAspect(assetTypeId) ? SignalAssetTypeId : assetTypeId;
        }

        /// <summary>
        /// For a SIGNAL alert, returns the virtual id from its CauseCode.
        /// Any other alert (or no cause code) keeps its original id. Null-safe.
        /// </summary>
        public static int ResolveAlertAssetTypeId(int assetTypeId, string causeCode)
        {
            if (assetTypeId != SignalAssetTypeId || string.IsNullOrEmpty(causeCode))
                return assetTypeId;

            if (Has(causeCode, ShuntCauseCode)) return Shunt;
            if (Has(causeCode, CallingCauseCode)) return Calling;
            if (Has(causeCode, RouteCauseCode)) return Route;
            return assetTypeId;
        }

        /// <summary>Drops Distant Signal and inserts the three static cards right after "Main Signal".</summary>
        public static List<Domain.AssetType> AddStaticAssetTypes(List<Domain.AssetType> source)
        {
            // Copy: 'source' may be a cached instance — never mutate it.
            var result = (source ?? new List<Domain.AssetType>())
                .Where(x => x != null && x.Id != (int)E7FRSAdvance.Utility.Utility.AssetType.Distant_Signal)
                .ToList();

            var statics = new[]
            {
                new Domain.AssetType { Id = Calling, Name = CallingName },
                new Domain.AssetType { Id = Route,   Name = RouteName },
                new Domain.AssetType { Id = Shunt,   Name = ShuntName }
            };

            int mainIdx = result.FindIndex(x => x.Name != null &&
                x.Name.Trim().Equals(MainSignalName, StringComparison.OrdinalIgnoreCase));
            int insertAt = mainIdx >= 0 ? mainIdx + 1 : result.Count;

            foreach (var s in statics)
            {
                bool exists = result.Any(x => x.Name != null &&
                    x.Name.Trim().Equals(s.Name, StringComparison.OrdinalIgnoreCase));
                if (exists) continue;

                result.Insert(insertAt, s);
                insertAt++;
            }
            return result;
        }

        private static bool Has(string text, string fragment)
        {
            return text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
