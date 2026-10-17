# Responsive & Alignment Fixes: what changed (Phase 1)

**Date:** 17 October 2026
**Branch:** feature-branch (not committed)
**Based on:** E7-Responsive-Proposal.md (§3.1–3.4)

## 1. Files at a glance

| File | Status | Change |
|---|---|---|
| `Areas/FRS25/assets/css/e7-responsive-fixes.css` | **New** (304 lines) | All layout fixes, scoped to `#e7Body` |
| `Areas/FRS25/assets/js/e7-responsive-fixes.js` | **New** (106 lines) | Drawer backdrop and close behaviour, AI panel focus, SIP notice |
| `Areas/FRS25/Views/Shared/_Layout.cshtml` | +5 / −1 | Links the CSS (last in `<head>`) and the JS (last before `</body>`); wraps "Ask AI" text in a span |
| `E7FRSAdvance.csproj` | +2 | Registers the two new files as Content so they are published |
| `Areas/FRS25/FRS25AreaRegistration.cs` | +7 | Route alias `/FRS25/MaintenanceRoster/...` |
| `Areas/FRS25/Controllers/UserSiteController.cs` | 1 line | "Select Divison" → "Select Division" |
| `Views/AdvanceAssetType/Index.cshtml` | 3 lines | Stray `}` removed; breadcrumb completed |
| `Views/AdvanceAssetType/_AssetTypeListPartial.cshtml` | 1 line | "In Active" → "Inactive" |
| `Views/EquipmentRoom/History.cshtml` | 1 line | Breadcrumb "EquipmentRoom" → "Equipment Room" |
| `Views/Aichat/WisdomManage.cshtml` | 1 line | Debug text "reviewer:yes approver:yes" removed |
| `Views/MaintenceRoster/Index.cshtml` | 1 line | "roster v3 · x.y" version text removed from the toolbar |
| `Views/AssetDetail/Utilization.cshtml` | +11 / −6 | Dates shown as DD-MM-YYYY |
| `Views/Performance/Index.cshtml` | +12 / −6 | Dates shown as DD-MM-YYYY |

**Rollback:** remove the `<link>` line and the `<script>` line in `_Layout.cshtml` that mention `e7-responsive-fixes`. The template text fixes are independent of these two lines.

---

## 2. Before → after, by page

### 2.1 Critical (§3.1)

| Page | Before | After |
|---|---|---|
| **Equipment Room History** | `#filterContainer` didn't wrap; Search cut off | Fields are `flex: 1 1 150px` and wrap in the same order; the Search button keeps its natural width and stays last |
| **Alerts** | `1fr 360px` grid; the page's own rule stacks it at ≤1400 | Side by side at ≥1200 (`minmax(0,1fr) minmax(0,360px)`, side panel back to a column); stacked at ≤1199; the last stat card spans the row when it would be alone |
| **Point Machine Clustering** (`#pmcApp .layout`) | 300px sidebar + content, `height: calc(100vh - 52px)` | At ≤991, stacked in a column with both panels full width and the drag-resizer hidden |
| **Track Shorting** (`#tlkApp .layout-container`) | 340px sidebar, `overflow: hidden` | Same stacking at ≤991; content padding drops from 32px to 16px |
| **PM Operation** (`.pm-combined-section .main`, all 4 models) | 200px sidebar, `overflow: hidden`; chart cards `min-width: 320–340px` | Same stacking at ≤991; inline min-widths capped at `min(100%, 280px)` |
| **RDPMS Health Live / Asset Utilization / Performance** | `.select-btn { min-width: 230px }`, `.filter-input { width: 120px }` | Each field is `flex: 1 1 180px; min-width: 0` and its control fills its slot; button items (e.g. Actual Failure Entry) keep their natural width; the Actual Failure Entry modal's filters wrap |
| **SIP Creation** | Editor unusable on phones | Editor hidden below 992px and replaced by a "Use a larger screen" notice (added by the JS). It's unchanged at ≥992px |

### 2.2 Shared (§3.2)

| Area | Before | After |
|---|---|---|
| Header | Wrapped to about 3 rows on mobile | ≤991: one row (☰ · title with "…" · buttons). ≤575: theme label, user name/role and the "Ask AI" text hidden (icons stay); title on its own line under the row |
| Drawer | `.open` toggled with no backdrop and no close behaviour | Dark backdrop (z-index 1049, just under the sidebar's 1050); closes on backdrop tap, Escape (focus returns to ☰), or choosing a page. Parent menu items keep it open. Sets `aria-expanded` on ☰ |
| Scrolling | Nested scroll areas: `.at-table-scroll`, `#atCardView`, `#divTelemetryLive`, Alerts timeline and maintenance list | At ≤991, height limits are removed so the page scrolls as one; wide tables still scroll sideways |
| Filter bars | Uneven widths | 2 equal columns at ≤767, 1 column at ≤399 (Equipment Room, RDPMS family, Alerts, Command Control) |
| Stat cards | Lone card on the last row | Last odd card spans the row (Alerts ≤1199, Health ≤767); Command Control cards share the row equally |
| Footer | Covered by the chat button | `padding-right: 76px` |
| Small text and buttons | Labels 8.5–9px; buttons 22–28px | Labels at least 11px; at ≤991 the header pills, ☰, view-mode buttons, "+" and chat tabs are at least 36×36 |
| AI panel | Reachable with Tab while closed | `inert` + `aria-hidden` while closed, kept in sync with its `.open` class. Done in JS rather than the guide's `visibility: hidden` rule so the slide-out animation still plays |

### 2.3 Page alignment (§3.3)

| Page | Change |
|---|---|
| Telemetry Live | `#wsStatusBar` fits its content; "+" (`#atpExpandBtn`) stays centred on the chip row; schematic header wraps without squeezing the title |
| Health | Lone stat card fixed (above) |
| Command Control | `.filter-group` labels centred against their fields; the station grid `minmax(430px)` becomes `minmax(min(100%,430px))` so it no longer overflows phones |
| Asset Types | The -15px `.page-title > .row` margins are reset |
| RDPMS Health Summary / Maintenance Roster | Labels are one line high and don't wrap, so "To Date"/"To Time" and "Division"/"Roster date" line up |
| Alert Analysis / Pending / Unified | Outer card padding is 8px on mobile; `.aa-topmeta` and `.aa-spills` badges wrap |
| ELD AI Analysis | `#eldApp > header` wraps, so "Refresh now" stays inside |
| Chatbot / ChatbotNew | `.e7-cat-tabs` fades at the right edge as a scroll hint; the input placeholder ends with "…" instead of being clipped |

### 2.4 Content fixes (§3.4)

| Item | Before → after |
|---|---|
| Asset Types | `Layout = "…"; }` + an extra `}` → one closing brace. Breadcrumb `[feather icon] › Asset type` → `[home icon → Dashboard] › Asset Types`. Badge "In Active" → "Inactive" |
| Equipment Room History | "EquipmentRoom" → "Equipment Room" |
| Command Control | "Select Divison" → "Select Division" |
| Wisdom Manage | "Signed in as X - reviewer:yes approver:yes" → "Signed in as X" |
| Maintenance Roster | Version text removed from the UI; it is still logged and included in the diagnostics export. `/FRS25/MaintenanceRoster/*` now works, and `/FRS25/MaintenceRoster/*` still works for bookmarks and the menu |
| Asset Utilization, Performance | Date fields showed MM/DD/YYYY (Utilization's picker format `mm/dd/yy` actually gave a 2-digit year) → DD-MM-YYYY. A `toServerDate()` helper still sends MM/DD/YYYY, so server parsing is unchanged |

---

## 3. Not done or needs a decision

| Item | Reason |
|---|---|
| Pending Alert Analysis title | Already its own (`ViewBag.Title`, breadcrumb and heading all say "Pending Alert Analysis"). No change |
| Alert Analysis "one loader / stray Asset Type label" | Couldn't find a label without a field or a duplicate loader in the 1 MB view. Needs a screenshot or steps to reproduce |
| AiDiag in the menu | Menu items come from the database (`FRS25Menus`). Removing it or making it admin-only is a data/permission change and is decision #3 in the proposal |
| Renaming the `MaintenceRoster` controller | Not done: it would break DB menu URLs. A correctly spelled route alias was added instead |
| Phase 2 (§3.5) | Out of scope |

## 4. Verification

- **Build:** `MSBuild E7FRSAdvance.csproj` succeeded with no new warnings.
- **Static test page:** the real CSS and JS were tested against copies of the layout's header, sidebar and drawer, the filter bar, the Alerts grid, the Track Shorting two-pane and the SIP editor.
  - 1440px: no sideways scroll; Alerts side by side; filters inside the card.
  - 768px: header one row; drawer backdrop, Escape, tap-outside and page-select close all work, and parent items keep it open; two-pane stacked; SIP notice shown.
  - 420px: filters in 2 columns.
  - 390px: title under the buttons; "Ask AI" shows icon only; filters in 1 column; no sideways scroll.
- **Visual before/after:** [E7-Responsive-BeforeAfter.html](E7-Responsive-BeforeAfter.html) shows 10 screens side by side, rendered with the real layout CSS and theme CSS. Building it caught one bug, now fixed: between 576px and 860px, `e7-energy7-theme.css` (`.e7-header-title.title { order: 3 }`) outranked the header rules and would have put the title after the buttons. The header selectors now include `.title` and `.top-actions`.
- **Not yet checked:** the 28 real pages against live data. IIS Express wasn't serving on :44305, and the pages need a login. QA should run the checklist in §5 of the proposal.
