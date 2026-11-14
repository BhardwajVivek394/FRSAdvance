# CHANGELOG -- E7.AlertCaseExtractor

## v1.1.0.0 -- 2026-11-10
**Version:** 1.0.2.0 -> 1.1.0.0 (Version + AssemblyVersion + FileVersion). Row LogicVersion 1.1.0 -> 1.2.0,
so the first run after the update re-reads full history by itself.
Doc: ChangesDone_Judge_1.0.202.0.md (root).

### `JudgeCasesWriter.cs` (new), `Program.cs` -- out\judge-cases.json for the Judge
Every run writes all decided alerts as cases (id, asset, decision, discard reason, maintenance flag,
set / decided time, card features) to out\judge-cases.json and copies it to `JudgeCasesPath`.
A failed copy is logged, exit code 1; the CSV and ledger are already written.

### `--replay`
Tests the Judge's matching on the newest 30% of the cases. Reads out\judge-cases.json only.

### `RowBuilder.cs` -- case features in the ledger row
`_judge_features`, built by the Judge's own source file (linked: E7.AiCore\Judge\JudgeCaseBase.cs).
Not a CSV column: cases_all.csv is unchanged.

### `ExtractorSettings.cs` -- new `JudgeCasesPath`
The web site's App_Data\AlertJudge folder (or a full .json path). Blank = no copy.
## v1.0.2.0 -- 2026-11-05
**Version:** 1.0.1.0 -> 1.0.2.0 (Version + AssemblyVersion + FileVersion). Row LogicVersion stays 1.1.0.
Review: Review_AlertCaseExtractor_1.0.2.0.md.

### `ExtractorSettings.cs` -- new `SiteIds`
Only alerts of these SiteIds are read. Empty = every site. For now: `[161, 167]` (SK, GLTA).

### `SourceReader.cs` -- filter the read and `--check`
`AND p.[SiteId] IN (...)` is added to the read and to every PendingAlertApproval count in `--check`.
Ints only, so the SQL text cannot be injected. `--check` logs the filter first.

### `Program.cs` -- log the filter
Each run logs `site filter: ...`. After changing SiteIds run `--full` once: it drops ledger rows of
sites no longer read.

---

## v1.0.1.0 -- 2026-11-05
**Version:** 1.0.0.0 -> 1.0.1.0 (Version + AssemblyVersion + FileVersion). Row LogicVersion 1.0.0 -> 1.1.0
(forces one full rebuild). Review: Review_AlertCaseExtractor_1.0.1.0.md.

### `SourceReader.cs` -- label by Status, read in batches
v1.0.0 selected nothing on the live DB: `UpdatedBy` is NULL on every row. Now PASS = Status 1 with
FRSAlertId > 0, and SUPPRESSED = Status 2, whatever the discard reason. Approvals without an FRS id are
not read. Rows are read 500 at a time by pending Id; a single query of ~150 MB dropped the remote
connection after 5 minutes. A dropped batch is retried twice. Also new:
- JudgeDecidedBy and ReapprovedFromDiscard are read.
- The maintenance window of the same asset at set time (dbo.MaintenanceMode, OUTER APPLY).
- `ReadSiteGeo`: site lat/long from dbo.SiteTemperature.
- `--check` gives the new counts.

### `RowBuilder.cs` -- why it was discarded
New columns:
- `discard_reason`: MARKED_TRUE, MARKED_FALSE, MAINTENANCE, TESTING, NOT_SENSITIVE, DUPLICATE,
  DEVICE_FAULT, CONFIG_THRESHOLD, OTHER or NONE.
- `remark_flag`: t = TRUE, f = FALSE.
- `is_test`, `judge_decided_by`, `reapproved`, `ack_status_name`.
- `in_maintenance_window`, `maint_window_id`, `maint_minutes_into_window`.

These are for learning only. They never drop a row or change its label.

### `Program.cs` -- station lat/long from the DB
A station not set in extractor-settings.json takes its lat/long from its SiteId in dbo.SiteTemperature.

### `ExtractorSettings.cs` -- defaults
SetTimeColumn defaults to `DeviceTimestamp` (the live column). New `BatchSize` = 500.

---

## v1.0.0.0 -- 2026-11-05
**Version:** new project, 1.0.0.0 (Version + AssemblyVersion + FileVersion). Row LogicVersion 1.0.0.

### `SourceReader.cs` -- read-only query of decided alerts
Reads dbo.PendingAlertApproval LEFT JOIN dbo.FRSAlert for alerts a person decided (UpdatedBy not null):
FRSAlertId set -> PASS, Status = 2 -> SUPPRESSED. The column list is built from the live table, so an
optional column the table lacks is left out instead of failing the query. `--check` reports the columns
and the PASS / SUPPRESSED / system-decided counts before any real run.

### `Ledger.cs` -- process each alert once
state/ledger.jsonl keeps one entry per alert (key, logic version, source hash, row). A run rebuilds an
alert only if it is new, its source hash changed (remark edited, reset arrived, decision flipped, weather
moved from recent to final), or RowBuilder.LogicVersion changed -- the last forces one full rebuild.
Ledger, CSV and state are written via temp file + replace; the watermark moves only after a clean run.

### `RowBuilder.cs` -- one CSV row per alert
Values at set and reset from the raw JSON and the alert cards: trigger value / threshold / margin, track
readings (Ir, If, IBALST, Vr, Vf, Choke V, charger, TPR), point readings (direction, op time / current /
voltage, KR / CR, location voltages), reset value and recovered flag, weather, remark (people only).

### `WeatherCache.cs` -- Open-Meteo, cached per station-month
Archive API for alerts older than WeatherFinalAfterDays (one call per station per month, cached on disk),
forecast API with past_days for newer ones. Rain at set hour, 6 h and 24 h before, code, temperature.

### `CsvWriter.cs` -- out/cases_all.csv
Writes all ledger rows sorted by set time, and fills the cross-alert columns at write time:
same_cause_site_count (same cause, same station, +/- SiteWindowMinutes) and same_asset_alerts_7d.

---
