# Common wisdom (handed to the AI on every judgement)

- The card is the RECORD of what the alert engine saw. History is what the historian holds for the same window. They can disagree; say which one you trust and why.
- Relays (RoleType d) are 0/1. Analog channels (RoleType c) are engineering units. Point operation data (RoleType p) is per-throw.
- Same-second follow-up samples on v484 A10 firmware reuse the event timestamp (`ets == prev`). When two values share a device second, the LATER arrival is the settled one.
- A 4-20 mA channel reading exactly -4.00 is 0 mA on the loop after live-zero offset: instrument, not process.
- Never claim a threshold was breached if the rule outcome says NOT_MET. You may argue the rule is wrong; you may not overrule it.

Per-cause files: `Rules/wisdom/<causeCode>.md` (doctrinal / field / circuit / learned / checklist).
