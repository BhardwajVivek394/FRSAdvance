# Changes Done -- FRS Advance (E7FRSAdvance): Claude Haiku 5.5 + AiModelConfig API (engine, model, key)
**Date:** 2026-10-16
**Repo / branch:** C:\GitFRSAdvance\FRSAdvance, `feature-branch` (uncommitted)
**Source:** port of the e7mriv2web main-branch work, see `ChangesDone_main_Haiku_AiModelConfig.md`
(main port of 619 E7.AiCore 1.0.190.0 -> 1.0.198.0, including the owner corrections of 2026-10-15:
Haiku 5.5 only, and IsDefault engine on the page when there is no dropdown).

## Component versions
| Component | Old -> New |
|---|---|
| AiChatController.ComponentVersion | 1.0.160.203 -> 1.0.160.204 |
| ChatBotController.ComponentVersion | 1.3.3.0 -> 1.3.3.1 |
| ChatBotNewController.ComponentVersion | 1.3.1.0 -> 1.3.1.1 |
| Domain AnthropicClient.ComponentVersion | 1.0.3.0 -> 1.0.4.0 |

## How FRS Advance differs from main (how the port was done)
- Controllers are the same code as main except the namespace (`E7FRSAdvance.*`), so the main diff was
  applied as a patch (paths and `E7MRIV2Web.Utility` / `E7MRIV2Web.AiCore` rewritten to `E7FRSAdvance.*`):
  AnthropicClient.cs, FilterConfig.cs, AiChatController.cs (+ .AiAudit.cs), ChatBotNewController.cs,
  MaintenceRosterController.cs. All hunks applied; FRS Advance's own extra code (train context on a
  cached verdict, partial roster class, enum differences) is untouched.
- ChatBotController is newer on FRS Advance (1.3.3.0, default `quantum10`). 10 of 13 hunks applied;
  3 by hand: default engine (fallback kept at `quantum10`) + API provider in ProviderKind, CHATBOT log
  line in Chat(), version.
- New files use namespace **`E7FRSAdvance.AiCore`** / `E7FRSAdvance.Utility` (the leftover
  `E7.AiCore` folder in this repo has only bin/obj; a stale `E7.AiCore.dll` cannot clash).
- Views: the FRS Advance pages are redesigned, but the engine code blocks are the same as main, so the
  same 6 edits were made per alert view and 3 per ChatBot view (indentation and line endings kept).
- No `AlertLive/_AiDiagnostic.cshtml` exists in FRS Advance, so that main view has no counterpart here.

## Behaviour (same as main)
1. Engines come from `GET {APIBaseUrl}AiModelConfig/GetAllWithKeys` (global AiModelConfigFilter, user's
   token, cached 300 s). Default engine = the IsDefault row. No API answer -> Web.config models/keys.
2. The engine row decides provider + model + API key: alert analysis, ChatBot, ChatBotNew, Roster Diagnose.
   Normal users and the roster run the IsDefault row.
3. Dropdowns on AlertAnalysis, PendingAlertAnalysis, UnifiedAlertAnalysis, ChatBot, ChatbotNew list every
   API engine (EngineCode + DisplayName only), IsDefault selected; users without the dropdown send the
   IsDefault engine.
4. Claude Haiku 5.5 only (`AiModelConfig.IsHaiku55`): max_tokens = model output cap from Anthropic's Models
   API, `output_config.effort = "low"` unless Think is ON, one full-thinking re-run on an invalid verdict.
   Think checkbox shown for QUANTUM engines and Haiku 5.5 engines. Other models unchanged.
5. Keys never reach a page or a log. Logs: AIMODEL, MODEL, CHATBOT, roster "Diagnose AI provider=... model=...".
6. ChatBot / ChatBotNew: no glued words at streamed-piece boundaries.

## Files
| File | Change |
|---|---|
| E7FRSAdvance/Utility/AiCoreShim.cs (NEW) | AiSettings helpers + AiCoreLog -> AiChatController.AiLogStatic |
| E7FRSAdvance/Utility/AiEngineInfo.cs (NEW) | engine row |
| E7FRSAdvance/Utility/AiModelConfig.cs (NEW) | engine list, Resolve, provider/model/key, output cap, page JSON helpers, IsHaiku55 |
| E7FRSAdvance/Utility/AiModelConfigFilter.cs (NEW) | calls the API with the user's token |
| E7FRSAdvance/App_Start/FilterConfig.cs | registers AiModelConfigFilter |
| E7FRSAdvance/E7FRSAdvance.csproj | Compile entries for the 4 new files |
| E7FRSAdvance/Web.config | AiModelConfigEnabled / Url / CacheSec / TimeoutMs; HistGetSendsUtc=false |
| Domain/AnthropicClient.cs | optional effort -> output_config.effort |
| Areas/FRS25/Controllers/AiChatController.cs (+ .AiAudit.cs) | as main |
| Areas/FRS25/Controllers/ChatBotController.cs, ChatBotNewController.cs | as main |
| Areas/FRS25/Controllers/MaintenceRosterController.cs | Diagnose: API provider/model/key, Haiku cap/effort, log line |
| Areas/FRS25/Views/AlertAnalysis, PendingAlertAnalysis, UnifiedAlertAnalysis /Index | dropdown, labels, default, Think rule, site-keep gate from API |
| Areas/FRS25/Views/ChatBot, ChatbotNew /Index | engine list + default from API |

## Verified
- [x] Build: Domain + E7FRSAdvance (Debug) -- no errors
- [x] The 5 changed views precompile (aspnet_compiler, scratch copy)
- [ ] Runtime on a server (not run here)

## Deploy
1. `Domain.dll`, `E7FRSAdvance.dll` to the server bin.
2. The 5 views: AlertAnalysis/Index, PendingAlertAnalysis/Index, UnifiedAlertAnalysis/Index, ChatBot/Index, ChatbotNew/Index.
3. Web.config keys above (`HistGetSendsUtc=false`, `AiModelConfigUrl=AiModelConfig/GetAllWithKeys`).
4. API data: one IsDefault row = nexus20 / anthropic / claude-haiku-5-5 with a valid key.

## Verify after deploy
- ai-log: `[AIMODEL] engines=N default engine=nexus20 provider=anthropic model=claude-haiku-5-5`
- Analyse an alert: `[MODEL] ... model=claude-haiku-5-5 returned=claude-haiku-5-5 stop=end_turn ...`
- ChatBot question: `[CHATBOT] page=ChatBot engine=nexus20 provider=anthropic model=claude-haiku-5-5`
- Roster Diagnose: `Diagnose AI provider=anthropic model=claude-haiku-5-5`
- Page source has no `sk-` text

## Fix (2026-10-16): AiDiag D2 always said "no timestamps" (AiChatController 1.0.160.205)
- Cause: D2 looked only for ISO date strings (`2026-10-16T11:00:00`), but `history_get` returns
  `{"ts":[epochSec..],"v":[..]}`, so D2 was WARN "no timestamps" for every asset and both
  `HistGetSendsUtc` values (seen on 617, JIG-1 / 1T).
- AiChatController.Diag.cs: D2 also reads the epoch `ts` arrays (UTC epoch -> IST), and adds the server's
  window echo (`"from"`) against the hour asked for:
  `window: asked from HH:mm IST, server applied HH:mm IST -- window OK` or `-- SKEW N min (flip HistGetSendsUtc)`.
  A SKEW turns D2 into FAIL. The echo answers HistGetSendsUtc even when the asset had no samples in the hour.
- Diagnostic page only; the analysis path is unchanged. Build OK.

## Change (2026-10-16): the last Web.config-only AI pages now use the API IsDefault engine
Before this, three pages ignored AiModelConfig and read provider/model/key only from Web.config.
| Page | Before | Now |
|---|---|---|
| Point Machine Operation -> AI summary (PostAiSummary, PostPm3AiSummary) | always DeepSeek, DeepSeekModel / DeepSeekApiKey | IsDefault engine (provider, model, key); fallback DeepSeek + Web.config |
| Track Leakage -> AI summary (AiSummary) | always DeepSeek, DeepSeekModel / DeepSeekApiKey | IsDefault engine; fallback DeepSeek + Web.config |
| Roster -> Asset Health (AssetHealthEngine 1.0.0.0 -> 1.0.1.0) | AiProvider + *Model / *ApiKey | IsDefault engine (same pattern as Roster Diagnose); fallback AiProvider + Web.config |
- New `Utility/AiDefaultClient.cs` (+ csproj entry): one-shot call on the IsDefault engine for PM Operation and
  Track Leakage (Anthropic / DeepSeek / OpenAI client, cached per provider:model:keyTag:maxTokens).
- Haiku 5.5 only: model's own output cap + effort "low" (as on every other page); other models keep
  1500 (PM / Track Leakage) and 900 (Asset Health) max tokens.
- Logs: ai-log `[AISUMMARY] page=PointMachineOperation|TrackLeakage provider=... model=...`;
  roster log `AssetHealth AI provider=... model=...`. The missing-key error now names the provider.
- Build OK. Not run on a server.

## Change (2026-10-16): logs for every AI feature + a page that shows whether they can be written
AiChatController 1.0.160.206, ChatBotController 1.3.3.2, ChatBotNewController 1.3.1.2.
Reason: on 617 no ai-log appeared. Every log writer swallows its own failures, so a folder the app pool
cannot write looked exactly like "nothing happened".
- **Log folders configurable** (`Utility/AiCoreShim.cs` -> `AiLogPaths`): new Web.config `AiLogDir` (ai-log,
  ai-tool, ai-analyze, ai-chat) next to the existing `RosLogDir` (roster). Empty = App_Data as before.
  Used by AiChatController, AiChatController.Wisdom (incl. DownloadAiLogs), ChatBot, ChatBotNew.
- **AiLogStatic** (used by AIMODEL / CHATBOT / AISUMMARY lines) no longer fails silently: counts dropped
  lines and keeps the last error, like AiLog.
- **ChatBot / ChatBotNew**: besides the question line, one line per question when it ends:
  `[CHATBOT] page=ChatBot done outcome=ok|error|rate-limit|auth-failed ms=N`, and an ERROR line with the
  exception when the question fails (before, errors went only to Windows Trace).
- **PM Operation / Track Leakage AI summary**: every error is logged `[AISUMMARY] page=... failed (HTTP n): ...`.
- **AiDiag "L - LOGS"** (view AiDiag/Index + Diag.cs):
  L1 AI log folder: path, write probe + one real test line, today's files and sizes, written/dropped counts;
  L2 roster log folder: path, write probe, today's file; L3 AiLogMinLevel (above INFO hides MODEL/CHATBOT...);
  L4 AiToolLoggingEnabled. A FAIL names the folder and the exact error (e.g. access denied).

| Feature | Log file | Lines |
|---|---|---|
| Alert analysis (3 pages) | ai-log, ai-analyze, ai-tool | MODEL per call, AIMODEL, analysis trace, tool calls |
| ChatBot / ChatBotNew | ai-log, ai-tool | CHATBOT question + done/error, tool calls |
| Roster Diagnose / Asset Health | roster-*.log | Diagnose AI provider=... model=..., AssetHealth AI provider=... model=... |
| PM Operation / Track Leakage AI summary | ai-log | AISUMMARY page=... provider/model, errors |
| AiDiag | ai-log | DIAG run summary + write test |

Deploy: `E7FRSAdvance.dll`, view `AiDiag/Index.cshtml`, Web.config keys `AiLogDir`, `RosLogDir` (empty is fine).
Build OK, AiDiag view precompiles. Not run on a server.

## Git commit comment
```
FRSAdvance: Haiku 5.5 via AiModelConfig/GetAllWithKeys (port of e7mriv2web main) -- engine, model and key from the API for alert analysis, ChatBot, ChatBotNew and Roster; dropdowns from the API; Haiku 5.5 only: output cap from Models API, effort low unless Think, one full-thinking re-run on an invalid verdict; MODEL/CHATBOT logs; HistGetSendsUtc=false
```
