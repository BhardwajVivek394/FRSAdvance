# Review — Bind safe Min / Max / Average from the FRS Attribute Range API (remove hard-coded DEFAULT_RANGES)

Status: **PROPOSAL — not applied yet.** Confirm and I will apply it.

## 1. Why the hard-coded values are what you see today

The API `FRSAttributeRange/SiteId/{siteId}` (proxied by `/FRS25/Telemetry/GetFRSAttributeRangeBySiteId`)
returns rows shaped like `Domain/AssetAttributeAlert.cs → FRSAttributeRange`:

```
Id, AssetId, AssetAttributeId, AlertInfoId,
MaxSafeValue, MinSafeValue, MinFailValue, AverageValue, AttributeName
```

`loadRanges()` in `telemetrylive-health.js` looks for these field names:

```
min: MinValue, MinimumValue, LowerLimit, LowerValue, MinRange, RangeMin, LowValue, Min
max: MaxValue, MaximumValue, UpperLimit, UpperValue, MaxRange, RangeMax, HighValue, Max
```

**`MinSafeValue` / `MaxSafeValue` are not in either list.** That means `mn` and `mx` both come back null, and every
API row hits `continue`, so `H.ranges` stays empty. `rangeFor()` then always falls back to `DEFAULT_RANGES`. The same
API is already read correctly elsewhere (`AiChatController.cs:10297` maps `MaxSafeValue`/`MinSafeValue`/`AverageValue`).

A second, smaller problem: rows are matched only by `AssetId | AttributeName`. `AttributeName` is an "Extra" field
on the model and may not match the live title or alias. The live rows already carry `AssetAttributeId`
(`attrId` in `readAsset`), which is a much more reliable key.

## 2. Changes

All changes are front-end JS. There are no C# or API changes, and no DLL or version bump.

### 2.1 `Areas/FRS25/assets/PageJs/TelemetryLive/telemetrylive-health.js`

**(a) Delete the hard-coded table (lines 50–63).** Keep `TRACK_ORDER` and `OCC_SUPPRESS`; they are ordering and
occupancy rules, not ranges.

```diff
-    /* RDPMS default safe bands (by attribute Title), used when the site has
-       no FRS attribute range for that asset / attribute. */
-    var DEFAULT_RANGES = {
-        IFMA: { min: 250, max: 500 },
-        ... (10 entries)
-        CHARGEROPV: { min: 4.5, max: 10 }
-    };
```

**(b) `loadRanges()` (lines 159–172): read the real field names and index by attribute id as well as by name.**

```diff
                         var name = r.AttributeName || r.AttributeTitle || r.Title || r.Name || '';
-                        var mn = pickNum(r, ['MinValue', 'MinimumValue', 'LowerLimit', 'LowerValue', 'MinRange', 'RangeMin', 'LowValue', 'Min']);
-                        var mx = pickNum(r, ['MaxValue', 'MaximumValue', 'UpperLimit', 'UpperValue', 'MaxRange', 'RangeMax', 'HighValue', 'Max']);
-                        if (mn === null && mx === null) continue;
-                        map[String(r.AssetId) + '|' + norm(name)] = {
-                            min: mn, max: mx,
-                            avg: pickNum(r, ['AverageValue', 'AvgValue', 'Average']),
-                            src: 'FRS range'
-                        };
+                        var mn = pickNum(r, ['MinSafeValue', 'MinValue', 'Min']);
+                        var mx = pickNum(r, ['MaxSafeValue', 'MaxValue', 'Max']);
+                        var av = pickNum(r, ['AverageValue', 'AvgValue', 'Average']);
+                        var fl = pickNum(r, ['MinFailValue']);
+                        if (mn === null && mx === null && av === null) continue;
+                        var rg = { min: mn, max: mx, avg: av, fail: fl, src: 'FRS range' };
+                        var aat = r.AssetAttributeId;
+                        if (aat !== null && aat !== undefined && aat !== '' && aat !== 0) map[String(r.AssetId) + '_' + aat] = rg;   /* id first */
+                        if (name) map[String(r.AssetId) + '|' + norm(name)] = rg;
                         n++;
```

**(c) `rangeFor()` (lines 182–187): match by attribute id, then by name. Drop the default fallback.**

```diff
-    function rangeFor(aid, title, alias) {
-        var r = H.ranges[aid + '|' + norm(title)] || H.ranges[aid + '|' + norm(alias)];
-        if (r && (r.min !== null || r.max !== null)) return r;
-        var d = DEFAULT_RANGES[norm(title)] || DEFAULT_RANGES[norm(alias)];
-        return d ? { min: d.min, max: d.max, avg: null, src: 'RDPMS default' } : null;
-    }
+    function rangeFor(aid, title, alias, attrId) {
+        var r = (attrId ? H.ranges[aid + '_' + attrId] : null) ||
+            H.ranges[aid + '|' + norm(title)] || H.ranges[aid + '|' + norm(alias)];
+        return r || null;
+    }
```

**(d) `readAsset()` line 349: pass the attribute id.**

```diff
-                range: rangeFor(aid, title, alias)
+                range: rangeFor(aid, title, alias, attrId)
```

Line 313 (point-machine rows) stays as it is. Those rows are synthesised and have no attribute id, so they only
match if the FRS row's `AttributeName` equals the label.

**(e) `classify()` (lines 2048–2050): accept an optional attribute id** (existing callers keep working).

```diff
-    function classify(aid, label, v) {
+    function classify(aid, label, v, attrId) {
         if (v === null || v === undefined || isNaN(v)) return { st: 'none' };
-        var g = rangeFor(String(aid || ''), label, label);
+        var g = rangeFor(String(aid || ''), label, label, attrId);
```

### 2.2 `Areas/FRS25/assets/sip/js/sip-telemetry.js` — pass the attribute id to `classify`

```diff
@@ liveReadings() ~line 973
-            var c = cls ? cls(rec.id, e.label, v) : { st: 'ok' };
+            var c = cls ? cls(rec.id, e.label, v, o.attrId) : { st: 'ok' };
```

`pmReadings()` (line 1037) and the inspector trend (line 1182) have no attribute id at hand, so they keep
name-only matching.

### 2.3 `Areas/FRS25/assets/PageJs/TelemetryLive/telemetrylive-drawer.js`

**(a) History graph, line 1497: pass the column's attribute id.**

```diff
-            if (cls && last !== null && !c.dl) { cr = cls(D.aid, c.name, last) || {}; if (!cr.range && name !== c.name) cr = cls(D.aid, name, last) || cr; }
+            if (cls && last !== null && !c.dl) { cr = cls(D.aid, c.name, last, c.attrId) || {}; if (!cr.range && name !== c.name) cr = cls(D.aid, name, last, c.attrId) || cr; }
```

**(b) `exportCsv()` (lines 2038–2042): add the Average column from the API.**

```diff
-        var lines = ['Asset,Reading,Value,Safe min,Safe max,Status'];
+        var lines = ['Asset,Reading,Value,Safe min,Safe max,Average,Status'];
         A.rows.concat(A.derived || []).forEach(function (r) {
             var g = r.range || {};
-            lines.push([a.AssetName || D.aid, r.alias || r.label, r.v === null ? '' : r.v, g.min == null ? '' : g.min, g.max == null ? '' : g.max, r.derived ? 'calculated' : stOf(r)]
+            lines.push([a.AssetName || D.aid, r.alias || r.label, r.v === null ? '' : r.v, g.min == null ? '' : g.min, g.max == null ? '' : g.max, g.avg == null ? '' : g.avg, r.derived ? 'calculated' : stOf(r)]
```

## 3. Behaviour after the change

| Case | Before | After |
|---|---|---|
| FRS row exists with Min/Max Safe | ignored → hard-coded band (track) or no band | API Min/Max used everywhere (Health view, drawer, CSV, SIP inspector, graph, Ask-AI context) |
| FRS row has only AverageValue | ignored | row is kept; avg goes to the CSV; no min/max band, so status uses `getValueColorClass` (Health view) / `none` (CSV) |
| No FRS row for the attribute | hard-coded band (10 track titles only) | no band; Health view falls back to `getValueColorClass` danger rule, CSV shows `none` |

## 4. Points to confirm before I apply

1. **Remove the defaults completely** (as above), or keep `DEFAULT_RANGES` only as a last fallback when the site has
   *no* FRS rows at all? Removing them means a track asset with no FRS configuration loses its band.
2. **One-sided ranges.** The status logic needs *both* min and max (`g.max > g.min`). If the FRS table holds only
   `MinSafeValue` for some attributes (e.g. relay-end readings), those rows won't be graded. I can add
   "below min → low" / "above max → high" for one-sided ranges if you want.
3. **`MinFailValue`.** It's now stored as `range.fail`. Should readings below it show as a separate `fail` status
   (red), with the current `low` (amber) between fail and min?
4. `telemetrylive.js:1303 loadFRSAttributeRange()` (Track Relay resistance `AverageValue` for derived calcs) already
   reads the right field. It stays untouched.
