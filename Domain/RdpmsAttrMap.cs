using System;
using System.Collections.Generic;

namespace Domain
{
    // Canonical EdgeX attribute-id map: Id (AssetAttributeId) <-> Title (RDPMS attr name) <->
    // AliasName (card paramLabel). Source: RDPMS_cause_code_attid_map.xlsx (Id/Title/AliasName cols).
    //
    // HAND-MAINTAINED companion to the AUTO-GENERATED RdpmsMaps. RdpmsMaps.cs is regenerated from the
    // xlsx by gen_rdpms_maps.py and must NOT be edited by hand; this separate file is never overwritten
    // by that converter. When the xlsx changes, update BOTH (or teach the generator to emit these two
    // dictionaries and delete this file).
    //
    // Purpose: the alert card carries only the AliasName (paramLabel, e.g. "VTC 24 DC TPR I/P(V)") and
    // no attid. AliasToAttid turns that into the attid so the alerting series can be resolved by the
    // fixed rule AssetId + attid + datatype (PickTagIdByAttrId), with no name/title string matching.
    public static class RdpmsAttrMap
    {
        // Id -> Title (RDPMS attribute name).
        public static readonly Dictionary<int, string> AttidToTitle =
            new Dictionary<int, string>
        {
            { 1,   "If mA" },
            { 2,   "Ir mA" },
            { 3,   "Vr" },
            { 4,   "Choke V" },
            { 5,   "Charger mA" },
            { 6,   "TPR V" },
            { 7,   "Last Update Feed End( sec ago)" },
            { 8,   "Last Update Relay End( sec ago)" },
            { 249, "Vf" },
            { 344, "Charger V" },
            { 569, "Charger OP V" },
            { 570, "TPR V (Loc)" },
            { 571, "TR V (Relay)" },
            { 585, "ITC BATT CHARG" },
            { 586, "VTC VAR RES" },
            { 587, "RTC CH FEED END" },
            { 588, "RTC VAR RES" },
            { 589, "RTC CH RELAY END" },
            { 590, "RRAIL" },
            { 591, "VTC CH RELAY END" },
            { 684, "IBlAST" },
            { 737, "Track Relay resistance" },
        };

        // normalized AliasName -> Id. Keys are normalized the same way as RdpmsMaps.AliasToTitle
        // (trailing "(unit)" stripped, whitespace collapsed, UPPER, Ordinal) so a card paramLabel such
        // as "VTC 24 DC TPR I/P(V)" resolves via NormalizeAlias(...) to key "VTC 24 DC TPR I/P" -> 6.
        // Ids 7, 8 and 737 have a NULL AliasName in the source and are intentionally omitted here.
        public static readonly Dictionary<string, int> AliasToAttid =
            new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "ITC FEED END",     1 },
            { "ITC RELAY END",    2 },
            { "VTC RELAY END",    3 },
            { "VTC CH FEED END",  4 },
            { "ITC TFC O/P",      5 },
            { "VTC 24 DC TPR I/P", 6 },
            { "VTC FEED END",     249 },
            { "VTC TFC I/P",      344 },
            { "VTC TFC O/P",      569 },
            { "VTC 24 DC LOC",    570 },
            { "VTC TR",           571 },
            { "ITC BATT CHARG",   585 },
            { "VTC VAR RES",      586 },
            { "RTC CH FEED END",  587 },
            { "RTC VAR RES",      588 },
            { "RTC CH RELAY END", 589 },
            { "RRAIL",            590 },
            { "VTC CH RELAY END", 591 },
            { "IBALST",           684 },
        };

        // Normalize a card paramLabel / AliasName to the AliasToAttid key form:
        // drop a single trailing "(...)" unit suffix, collapse internal whitespace, trim, UPPER.
        public static string NormalizeAlias(string aliasName)
        {
            if (string.IsNullOrEmpty(aliasName)) { return string.Empty; }
            string s = aliasName.Trim();
            int lp = s.LastIndexOf('(');
            if (lp > 0 && s.EndsWith(")")) { s = s.Substring(0, lp).Trim(); }
            var parts = s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts).ToUpperInvariant();
        }

        // Resolve a card paramLabel / AliasName to its attid. Returns -1 when unknown.
        public static int AttidForAlias(string aliasName)
        {
            int id;
            return AliasToAttid.TryGetValue(NormalizeAlias(aliasName), out id) ? id : -1;
        }
    }
}
