using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore.Judge
{
    // ------------------------------------------------------------------------------------------
    // JudgeCardDataCheck (1.0.208.0) -- the card's own (value, time) pairs against history.
    //
    // The alert card prints each condition's MEASURED value with its timestamp ("v1 at t1").
    // Every such pair is validated: the value in effect at that very timestamp is read from
    // history and compared. No guessing about the alert instant is needed -- the card itself
    // says when each value was sampled.
    //
    //   card says v1 at t1, history shows v1 at t1  -> AGREE
    //   history shows something else at t1          -> DIFFER  -> the alert is SUPPRESSED
    //                                                  (decidedBy card-data): the card prints
    //                                                  values the recorded data does not show.
    //   no tag / no history / derived attribute     -> not counted either way
    //
    // Runs for EVERY alert as part of point 3; the catalogue rule (where one exists) still
    // runs after it as an extra gate.
    // ------------------------------------------------------------------------------------------
    internal sealed class JudgeCardDataRow
    {
        public string Name;
        public int Attid;
        public string Kind;         // relay | analog
        public double CardValue;
        public DateTimeOffset CardTime;
        public double? HistValue;
        public DateTimeOffset? HistTime;
        public string Result;       // AGREE | DIFFER | NO_HISTORY
    }

    internal sealed class JudgeCardDataResult
    {
        public List<JudgeCardDataRow> Rows = new List<JudgeCardDataRow>();
        public int Agree;
        public int Differ;

        public bool Mismatch { get { return Differ > 0; } }

        /// <summary>The disagreeing rows, for the suppression reason.</summary>
        public string DifferText()
        {
            return string.Join("; ", Rows.Where(x => x.Result == "DIFFER").Select(x =>
                x.Name + " printed " + JudgeUtil.F(x.CardValue, "0.###")
                + " at " + JudgeUtil.T(x.CardTime, "HH:mm:ss")
                + " but history shows " + JudgeUtil.F(x.HistValue, "0.###")
                + " (recorded " + JudgeUtil.T(x.HistTime, "HH:mm:ss") + ")"));
        }
    }

    internal static class JudgeCardDataCheck
    {
        internal static async Task<JudgeCardDataResult> RunAsync(JudgeCard card, JudgeEvidence evidence, JudgeEvidenceBundle ev, JudgeSettings cfg, List<string> log, CancellationToken ct)
        {
            JudgeCardDataResult r = new JudgeCardDataResult();
            if (card == null || card.Conditions == null || card.Conditions.Count == 0)
            {
                log.Add("cardData: no structured conditions on the card -- nothing to validate");
                return r;
            }

            HashSet<int> seen = new HashSet<int>();
            foreach (JudgeCardCondition c in card.Conditions)
            {
                ct.ThrowIfCancellationRequested();
                if (c == null)
                {
                    continue;
                }

                string name = !string.IsNullOrWhiteSpace(c.Name) ? c.Name : c.Label;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (string.Equals(c.Kind, "derived", StringComparison.OrdinalIgnoreCase))
                {
                    log.Add("cardData: " + name + " is derived -- skipped");
                    continue;
                }

                // the measured value the card prints
                double v1 = 0;
                bool have = false;
                if (c.CardValue != null)
                {
                    v1 = c.CardValue.Value;
                    have = true;
                }
                else if (card.Values.TryGetValue(name, out v1))
                {
                    have = true;
                }
                else
                {
                    int rbit;
                    if (card.Relays.TryGetValue(name, out rbit) && rbit >= 0)
                    {
                        v1 = rbit;
                        have = true;
                    }
                }

                if (!have)
                {
                    log.Add("cardData: " + name + " -- no measured value on the card, skipped");
                    continue;
                }

                // the attribute id the card carries for it
                int attid;
                if (!card.AttributeIds.TryGetValue(name, out attid)
                    && (c.Label == null || !card.AttributeIds.TryGetValue(c.Label, out attid)))
                {
                    attid = 0;
                }

                if (attid <= 0)
                {
                    log.Add("cardData: " + name + " -- no attribute id on the card, skipped");
                    continue;
                }

                if (!seen.Add(attid))
                {
                    continue;
                }

                if (JudgeAttrFacts.IsDerived(attid))
                {
                    log.Add("cardData: " + name + " attid=" + attid + " is derived -- skipped");
                    continue;
                }

                // the card's own timestamp for the value
                DateTimeOffset t1;
                if (c.CardTime != null)
                {
                    t1 = c.CardTime.Value;
                }
                else
                {
                    DateTimeOffset vt;
                    t1 = card.ValueTimes.TryGetValue(name, out vt) ? vt : card.TriggeredAt;
                }

                bool relay = string.Equals(c.Kind, "relay", StringComparison.OrdinalIgnoreCase);
                char role = relay ? cfg.RoleDataLogger[0] : cfg.RoleRdpms[0];

                // the rule fetch may already have resolved this attid's tag -- reuse it
                long knownTag = 0;
                JudgeTagSeries series;
                if (ev != null && ev.Series != null && ev.Series.TryGetValue("attid:" + attid, out series))
                {
                    knownTag = series.TagId;
                }

                JudgeCardDataRow row = new JudgeCardDataRow();
                row.Name = name;
                row.Attid = attid;
                row.Kind = relay ? "relay" : "analog";
                row.CardValue = v1;
                row.CardTime = t1;
                r.Rows.Add(row);

                JudgeSample? sn = await evidence.ValueAtAsync("cardData " + name, card.SiteId, card.AssetId, card.AssetName, name, attid, role, t1, knownTag, log, ct).ConfigureAwait(false);
                if (sn == null)
                {
                    row.Result = "NO_HISTORY";
                    log.Add("cardData: " + name + " attid=" + attid + " card=" + JudgeUtil.F(v1, "0.###")
                        + " @ " + JudgeUtil.T(t1, "HH:mm:ss") + " -> no history value, not counted");
                    continue;
                }

                JudgeSample s = sn.Value;
                row.HistValue = s.Value;
                row.HistTime = s.Ts;

                // relay: same state. analog: within 1% (or 0.5 absolute for small values) --
                // the card and the historian sample the same source, small rounding is expected.
                bool ok = relay
                    ? (Math.Abs(s.Value) >= 0.5) == (Math.Abs(v1) >= 0.5)
                    : Math.Abs(s.Value - v1) <= Math.Max(0.5, Math.Abs(v1) * 0.01);

                row.Result = ok ? "AGREE" : "DIFFER";
                if (ok)
                {
                    r.Agree++;
                }
                else
                {
                    r.Differ++;
                }

                log.Add("cardData: " + name + " attid=" + attid + " card=" + JudgeUtil.F(v1, "0.###")
                    + " @ " + JudgeUtil.T(t1, "HH:mm:ss")
                    + ", history=" + JudgeUtil.F(s.Value, "0.###") + " @ " + JudgeUtil.T(s.Ts, "HH:mm:ss")
                    + " -> " + row.Result);
            }

            log.Add("cardData: " + r.Agree + " agree, " + r.Differ + " differ, "
                + (r.Rows.Count - r.Agree - r.Differ) + " without history");
            return r;
        }
    }
}
