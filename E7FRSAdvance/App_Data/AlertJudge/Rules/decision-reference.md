# Decision reference (sent to the model on every judgement)

## What you are deciding
One alert. Output `recommendation: PASS` (deliver to dashboard/SMS) or `SUPPRESS` (hold it back). Nothing else.

## Doctrine (from CauseCatalog, P0–P3)
- **P0** — The alert card is the CLAIM, not the authority. The card carries its cause logic (thresholds, relay states, sustain window). Telemetry decides: if the logic HELD → CONFIRMED; if it lapsed, or the enabling relay was not in state → NOT_CONFIRMED; if telemetry is absent or does not reach the set time → NOT_CONFIRMED. **Missing data never confirms — and an unconfirmed alert is SUPPRESSED.**
- **P1** — FAILURE alerts are decided by the DataLogger relays (TPR/NWKR/HECR…); RDPMS analog values only select WHICH cause. PREDICTIVE alerts need BOTH the enabling relay in state AND the analog breach, sustained. Relay alone does not confirm; analog alone does not confirm.
- **P2** — Thresholds are formulas resolved per asset: `MAX(avg*LD, MinSafe)`, `MIN(avg*HD, MaxSafe)`, `avg*pct`, or a direct MinSafe/MaxSafe/MinFail. `thresholds[].threshold` is the resolved number; `baselineAvg` is the 24 h mean it was built from.
- **P3** — An attr the asset does not have (not in its tag table) is SKIPPED and does not count against the cause. An attr the asset has but with no data in the window is COVERAGE — a gap, not a skip.

## The two cases — read `question` and `case.alertState` first

**CASE SET_ONLY** — the alert is standing, no reset yet.
- Frame = set time → now. `attrsAtSet[].valueAtSet` is what was in effect at the set time (carried forward from its last change, which may be hours earlier — `lastChangedBeforeSet` tells you).
- PASS when the values in effect at the set time satisfy the cause (relay in the required state, analog on the breached side of the resolved threshold) and nothing since contradicts it.
- SUPPRESS when the state at set does not match the cause, when the breach was already gone at set time, or when what happened since explains it away (train movement, instrument artefact, a stuck channel).

**CASE SET_RESET** — the alert set and later reset.
- The cause must have HELD continuously from the set time for `case.sustainSec`. `gates.causeHeldForSec` and `attrsAtSet[].stayedTrueForSec` are the measured durations.
- PASS when every gating attribute stayed true for at least `sustainSec` from the set time.
- SUPPRESS when the alert reset before `sustainSec` elapsed (`gates.resetBeforeSustain = true` — a transient), or any gating attribute lapsed inside the sustain window, or the telemetry contradicts the card.

## What already passed before you were asked
1. The card's own logic matched the CauseCatalog entry (same attributes, same direction).
2. DataLogger relay history confirmed the relay state (FAILURE causes: this alone decides).
3. For PREDICTIVE causes, RDPMS analog history confirmed the breach against the resolved threshold.
4. For track failures, the adjacent tracks were UP at the set time and did not drop shortly after.

## PASS vs SUPPRESS — your answer is the decision
- **PASS** when the telemetry, read in context of the case above, supports a real alert.
- **SUPPRESS** when it does not — a spike that recovered inside the frame, a −4.00 mA loop / stuck channel, a pattern that is train movement or shunting, a reset before sustain, or card numbers that contradict the telemetry.
- If you cannot tell — the evidence is incomplete, contradictory, or does not reach the set time — say why in `caveats` and answer **SUPPRESS**. An alert only passes when the telemetry positively supports it.

## Reading the telemetry section
- `samples` are `[time, value]`: the first sample is the value IN EFFECT at the alert (carried forward from its last change, which may be hours earlier — its time shows when), then every transition inside the frame [alert, now/reset], oldest first, downsampled evenly. `totalSamples` is the real count. A relay with one sample and no transitions did not move during the frame.
- Relays (roleType `d`) are 0/1. Analog (roleType `c`) is engineering units.
- `thresholds[].trend.direction` is rising/falling/flat across the window; `deltaVsBaselinePct` is how far the window average sits from the 24 h baseline.
- Same-second follow-up samples on v484 A10 firmware reuse the event timestamp — the later arrival is the settled value.
- A 4–20 mA channel reading exactly −4.00 is 0 mA on the loop after live-zero offset: instrument, not process.

## Evidence discipline
- `record` = taken from `alertCard`. `fetched` = taken from `telemetry` or `thresholds` (must name the attribute). `computed` = something you derived.
- Never write a `fetched` line about an attribute that is not in `telemetry.series`.
- `verifierOutcome` is deterministic and authoritative. You may argue it is wrong; you may not overrule it by asserting a condition held.

## Live re-check (2.0) — `liveCheck`
- Every condition printed on the card (enabling relay, main and derived triggered conditions) was re-checked against LIVE historian data over the check window `[set time, set time + liveCheck.windowSec]`. `liveCheck.conditions[]` gives, per condition, the value in effect at the set time, the window min/max, `liveHeld`, and where it broke (`firstBreakAt`, `firstBreakValue`).
- The card's own measured numbers may have been captured mid-transition. When `liveCheck` and the card disagree, `liveCheck` is what really happened. A condition marked `IGNORED` was not counted (no formula known yet); say so in `caveats` if it matters.
- You only see an alert whose `liveCheck.result` is PASS (or SKIPPED when the card carried no structured conditions). Your job is the two questions below.

## The two questions (2.0) — `logic_correct` and `data_correct`
- `logic_correct`: is the logic on the card the right logic for this cause? Same attributes as `causeCatalog`, same directions, thresholds consistent with the catalog formulas (`MAX(avg*LD, MinSafe)` etc.), the enabling relay the catalog requires. A card that fired on the wrong attribute, the wrong direction, or a threshold that contradicts the catalog is `logic_correct: false`.
- `data_correct`: does the live data really show every condition set through the window, with nothing that explains it away — train movement (adjacent track sequence), an instrument artefact (−4.00 mA, a stuck channel), a spike that recovered, a value that only touched the threshold? If any of these, `data_correct: false`.
- `recommendation` is PASS only when both are true. Either false, or either unknowable from the data given, is SUPPRESS with the reason in `caveats`.
