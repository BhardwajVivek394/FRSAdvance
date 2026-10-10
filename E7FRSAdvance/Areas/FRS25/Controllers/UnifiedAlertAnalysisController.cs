using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class UnifiedAlertAnalysisController : Controller
    {
        bool siteKeep = E7FRSAdvance.Utility.ClsHttpContent.LoginUser.IsSiteKeeping;

        // 1.0.0.7: the SAME rule the view resolves at line 1104 -- IsSiteKeeping OR a match
        // in SiteKeepingOverrideEmails. Duplicating only the IsSiteKeeping half would let an
        // override user see the Discarded chip and receive nothing behind it.
        // Resolved PER REQUEST, never in a field initialiser: a static-lifetime field would
        // capture the FIRST caller's identity and hand it to everyone after them.

        // ------------------------------------------------------------------
        // UnifiedAlertAnalysis -- component version (Major.Minor.Build.Revision)
        // ------------------------------------------------------------------
        // A NEW page beside the existing two, not a replacement for either.
        // AlertAnalysisController and PendingAlertAnalysisController are UNTOUCHED and
        // keep their own menu entries; this controller is forked from
        // AlertAnalysisController 1.0.6.6 and adds the merge on top. Its own version line
        // starts at 1.0.0.0 -- it is a new shippable component, and inheriting the parent's
        // number would claim a history this file does not have.
        //
        // Because it is a FORK, fixes to LiveValues, VerdictDryRun or GetAlertAudit made on
        // AlertAnalysisController after 1.0.6.6 will NOT appear here. That is the cost of
        // running three pages instead of one, and it is the same cost that made the first
        // two drift apart. Worth revisiting once this page has proved itself.
        // ------------------------------------------------------------------
        public const string ComponentVersion = "1.0.2.0";
        // 1.0.2.0 - ONE ALERT, TWO ID SPACES, AND THE ANALYSES FROM BOTH OF THEM.
        //           An alert that was reviewed, discarded and LATER APPROVED exists under
        //           two unrelated ids: PendingAlertApproval.Id while it sat in review, and
        //           FRSAlert.Id once it was promoted. AIAnalysis rows are keyed by whatever
        //           id the board held WHEN THE RUN WAS MADE -- the analyze flow sends
        //           ctx.alertId, which is the pending id on a discarded card and the FRS id
        //           on an approved one. So every run made before approval is stored under
        //           the pending id and every run made after it under the FRS id.
        //           CONSEQUENCE, and this is what was actually on screen: opening Detail on
        //           an approved alert asked AIAnalysis/Get for the FRS id only, and the
        //           whole pre-approval history -- often the ONLY analysis the alert has,
        //           since the reason it was looked at is that it was in review -- came back
        //           empty. The panel said "No analysis has been run on this alert yet" over
        //           an alert that had been analysed three times.
        //           FIXED at the id, not at the panel:
        //             - dbo.PendingAlertApproval is the only place the two spaces are tied
        //               together (its Id and its FRSAlertId). LinkedAlertIds reads that
        //               link in whichever direction the caller needs, and NEVER guesses:
        //               the caller says which space its id is in, because 3010 is a valid
        //               id in both and an unqualified lookup would hand one alert's
        //               analyses to another. That is the 1.0.0.4 fault, and it is not
        //               being reintroduced here for convenience.
        //             - AlertAnalysisDetail now queries AIAnalysis/Get ONCE PER ID and
        //               merges, de-duping on AnalysisId (a promoted alert can legitimately
        //               return the same run under both). Every run is tagged with the STAGE
        //               it was made in -- "pending" (under review) or "approved" -- and the
        //               panel shows both, because a verdict reached before approval and one
        //               reached after are different evidence, not a duplicate.
        //             - THE SAME LINK EVERYWHERE ELSE, which is the half that would
        //               otherwise leave the board contradicting itself: ResolveAnalysisId
        //               and AnalysisBelongsTo now match FRSAlert_AIAnalysis on BOTH columns
        //               for the same alert, so the rating chip and the validity toggle can
        //               find a run recorded at the pending stage; AlertRatings expands each
        //               requested FRS id with its linked pending ids and fans the result
        //               back to the "F:" key, so the gate enables on the same alerts Detail
        //               can open -- the 1.0.1.2 rule, now honoured across the promotion too;
        //               and the API probe falls back to the linked id as well.
        //             - The board rows carry both ids (FrsAlertId, PendingAlertApprovalId)
        //               so the view sends the link as a hint and Detail still works on a
        //               host where this web server cannot reach E7MRIV2DB_SQL.
        // 1.0.1.3 - THE DETAIL PROJECTION CARRIES THE CASE AND NO LONGER CARRIES THE BUILD.
        //           ProjectAnalysis was shipping engine, provider, modelId, thinking, turns,
        //           tokens, est. cost, total/model duration, verification tier, server node,
        //           controller version and user role -- and was parsing the output blob only
        //           far enough to reach the verdict text, dropping everything else in it.
        //           So the panel could tell you which model ran, and could not tell you what
        //           it measured.
        //           REMOVED, at the projection rather than in the view, so the model names do
        //           not reach the browser at all and cannot come back through devtools or a
        //           copied payload. This is the owner's call made a third time: .104 pulled
        //           the engine/model segment off the REUSED badge ("model never display --
        //           just cache/fresh") and .108 removed the run-log button for "still showing
        //           deepseek". cache/fresh is kept, because that one IS operational.
        //           ADDED, all of it already present in AnalysisOutputJson/AlertInputJson and
        //           previously discarded: viz (the measurement the verdict rests on -- value,
        //           threshold, baseline, change), asset_history (30-day recurrence, and
        //           sameCause separately because nine alerts under nine codes is a different
        //           story from nine under one), snapshot (the live readings with each one's
        //           own safe band and the server's own ok/low/high state), and the alert card
        //           -- what fired against which limits, the relay states either side of the
        //           event, and hadReset.
        //           hadReset is the one that earns its keep. A run made while the alert was
        //           still open judged it WITHOUT knowing it recovered, and on real data that
        //           is exactly why one run says NOT_CONFIRMED and a later one says CONFIRMED.
        //           Without it the panel shows two contradictory verdicts and no way to tell
        //           which of them saw more.
        //           Numbers are projected as STRINGS throughout: they are printed, never
        //           computed on, and a double round-trip turns 0.04 into 0.040000000000001.
        // 1.0.1.2 - ONE SOURCE FOR "DOES THIS ALERT HAVE AN ANALYSIS". The rule is the
        //           page owner's: if the Detail panel finds an AnalysisId, the validity
        //           toggle must be enabled. Detail reads AIAnalysis/Get over the proxy
        //           API; the toggle's gate read dbo.FRSAlert_AIAnalysis over direct SQL.
        //           TWO SOURCES, and every way they can disagree -- SQL unreachable from
        //           this host, a row keyed by an id this board does not use, an analysis
        //           that exists at the API but was never written to that table -- showed
        //           up identically on screen as a dead toggle over a visible AnalysisId.
        //           1.0.1.1 fixed one of those causes (dual-keyed rows) and the symptom
        //           survived, which is the evidence that guessing at causes was the wrong
        //           approach. So the gate now asks the SAME question of the SAME service:
        //             - AlertRatings keeps the SQL query as a cheap bulk answer, but a
        //               failure is no longer fatal, and every alert it did not account
        //               for is probed against AIAnalysis/Get (capped, time-boxed, and
        //               only ever reached for alerts SQL missed -- zero on a healthy box).
        //             - SaveAlertValidity resolves the same way, and validates a
        //               client-supplied analysisId against SQL *or* the API. Without that
        //               the gate would enable a toggle the write then refused, which is
        //               worse than the dead toggle: the operator would read it as the
        //               board being broken rather than the alert being unanalysed.
        // 1.0.1.1 - THE VALIDITY TOGGLE AND STAR CHIP STOPPED DISABLING THEMSELVES ON
        //           ANALYSED ALERTS. An FRSAlert_AIAnalysis row can carry an FRSAlertId
        //           AND a PendingAlertApprovalId -- that IS a promoted pending alert. The
        //           key builder took the FRSAlertId branch and stopped, so the map held
        //           only "F:<frsId>"; a card the board keys "P:<pendingId>" found nothing
        //           and aaCanRate reported "not analysed" over an AnalysisId sitting in
        //           the row. Both keys are now emitted.
        //           Two consequences handled with it: the fetch list is deduped by
        //           DISTINCT AnalysisId (dual keys would otherwise ask the rating and
        //           assessment APIs for the same run twice per board load), and each
        //           result is fanned back out to every key sharing that id.
        // 1.0.1.0 - ANALYSIS DETAIL per alert. AlertAnalysisDetail hits AIAnalysis/Get?
        //           AlertId={id}&IncludeJson=1 -- the AlertId being the SAME row id the
        //           board carries and the analyze flow sends, so no AnalysisId lookup is
        //           needed. It parses server-side and returns only the panel's fields
        //           (verdict, when analysed, engine/model, who ran it, status, duration,
        //           tokens, evidence) rather than shipping the full evidence/snapshot
        //           blobs. The full run HISTORY is returned, newest first.
        // 1.0.0.9 - THE ID IS THE RECORD, THE NAME IS THE DISPLAY. AssessedBy still stores
        //           the user id -- an id survives a rename and two people can share a name
        //           -- but "Marked VALID by 1134" tells a reviewer nothing, so the id is
        //           resolved through User/GetUserById and sent to the board as byName
        //           beside the raw value. The board falls back to the id when a lookup
        //           returns nothing, rather than showing a blank author.
        //           Resolution happens on the REQUEST thread: HttpClientFactory reads
        //           HttpContext.Current and the session token, so it cannot run inside the
        //           Parallel.ForEach that gathers the marks. One lookup per DISTINCT
        //           reviewer, cached 30 minutes -- a board where one person marked forty
        //           alerts costs one call, not forty.
        //           The mark itself is now a typed AssessMark rather than an anonymous
        //           object, because 1.0.0.8 could only read it back by reflection and a
        //           silently-failing GetProperty made a revision look like a first answer.
        // 1.0.0.8 - ALERT VALIDITY, written through api/AIAssessment/Upsert as the REVIEWER
        //           role. SaveAlertValidity posts { AnalysisId, AssessedBy, AssessmentRole,
        //           WasAlertUseful }; AssessedBy is the LOGIN USER ID, not the display name
        //           the CLIENT rows carry, because a reviewer judgement has to be traceable
        //           to an account and two people can share a name.
        //           The current mark is READ FIRST -- AlertRatings now returns an
        //           `assessments` map alongside `ratings`, so the board opens already
        //           knowing which alerts a reviewer has judged instead of showing every
        //           toggle as unset and inviting a second, contradicting answer.
        //           Same analysis-id resolution and the same sitekeeping refusal as the
        //           rating write: a judgement attaches to an ANALYSIS RUN, and a discarded
        //           alert is not everyone's to judge.
        // 1.0.0.7 - DISCARDED ALERTS ARE SITEKEEPING-ONLY, enforced server-side on every
        //           surface that touches them: the board list, the ratings read and the
        //           ratings write. Hiding cards in the view alone would still ship every
        //           discard remark to every browser, one network-tab open from being read.

        // Per request, never a field initialiser -- a static-lifetime field would capture the
        // FIRST caller's identity and hand it to everyone after them.
        private static bool AaPrivileged()
        {
            try
            {
                var lu = E7FRSAdvance.Utility.ClsHttpContent.LoginUser;
                if (lu == null) { return false; }
                if (lu.IsSiteKeeping) { return true; }
                if (lu.EmailAddress == null) { return false; }
                string email = lu.EmailAddress.Trim();
                if (email.Length == 0) { return false; }
                string list = (ConfigurationManager.AppSettings["SiteKeepingOverrideEmails"] ?? "");
                if (list.Length == 0) { return false; }
                foreach (var e in list.Split(new char[] { ',', ';' }))
                {
                    if (string.Equals(e.Trim(), email, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        // 1.0.2.2: TWO gates, both must pass.
        //
        //   FRS list    -- the sites this board covers at all. Fixed, small, config-driven.
        //   Assignment  -- the sites THIS caller holds, resolved the way AlertLive resolves
        //                  them, through the site service against the login token.
        //
        // The board renders the INTERSECTION. Neither list widens the other: an assignment
        // cannot pull in an off-board site, and the FRS list cannot grant access the caller
        // does not already hold.
        //
        // Config wins so the FRS list changes without a deploy; the literal is the fallback
        // for an environment where the key was never added. A key present but BLANK yields an
        // empty set and empties the board -- deliberate, because a typo that silently opened
        // the board to every site would be far worse than one that closes it.
        private const string DefaultFrsSiteIds = "161,167,14,25,83,155,118,92";

        private static HashSet<int> FrsSiteIds()
        {
            var frs = new HashSet<int>();
            string raw = ConfigurationManager.AppSettings["UnifiedAlertAnalysisSiteIds"];
            if (raw == null) { raw = DefaultFrsSiteIds; }

            foreach (var part in raw.Split(new char[] { ',', ';' }))
            {
                int id;
                if (int.TryParse(part.Trim(), out id) && id > 0) { frs.Add(id); }
            }
            return frs;
        }

        // Instance, not static: it needs _siteService, and the caller's identity must be read
        // per request. A static-lifetime cache here would capture the FIRST caller's sites
        // and hand them to everyone after them -- the same trap AaPrivileged avoids.
        private HashSet<int> AssignedSiteIds(BoardCounts counts)
        {
            var allowed = new HashSet<int>();
            var frs = FrsSiteIds();

            if (counts != null) { counts.FrsSiteCount = frs.Count; }
            if (frs.Count == 0)
            {
                AiLogBoard("AssignedSiteIds: FRS site list is empty; board will show nothing.");
                return allowed;
            }

            try
            {
                var sites = Helper.FilterCacheHelper.GetAllSites(_siteService);
                if (sites != null)
                {
                    foreach (var s in sites)
                    {
                        if (s == null || s.Id <= 0) { continue; }
                        if (counts != null) { counts.AssignedSiteCount++; }

                        // The intersection, and the only place a site earns its way on.
                        if (frs.Contains(s.Id)) { allowed.Add(s.Id); }
                    }
                }
            }
            catch (Exception ex)
            {
                // Deliberately NOT falling back to the FRS list. Returning it here would hand
                // every FRS site to a caller whose assignment could not be read -- the one
                // case where being permissive is worst.
                allowed.Clear();
                if (counts != null) { counts.SiteScopeError = ex.GetType().Name + " " + ex.Message; }
                AiLogBoard("AssignedSiteIds failed: " + ex.ToString());
            }

            AiLogBoard("AssignedSiteIds: frs=" + frs.Count
                       + " assigned=" + (counts == null ? -1 : counts.AssignedSiteCount)
                       + " effective=" + allowed.Count
                       + " ids=" + string.Join(",", allowed.OrderBy(x => x)));

            return allowed;
        }


        // 1.0.0.7 - DISCARDED ALERTS ARE SITEKEEPING-ONLY, enforced server-side on every
        //           surface that touches them: the board list, the ratings read, the ratings
        //           write, and GetPendingAlertById on the pending controller. Hiding cards in
        //           the view alone would still ship every discard remark to every browser,
        //           one network-tab open from being read.
        // 1.0.0.6 - AI-analysis RATING. SaveAiRating posts to {ProxyBaseUrl}/api/AIRating/Upsert
        //           with AnalysisId resolved server-side from dbo.FRSAlert_AIAnalysis -- by
        //           FRSAlertId for an approved card, by PendingAlertApprovalId for a discarded
        //           one. The browser never sends a run id, so it cannot post one belonging to a
        //           different alert. AlertRatings reports which alerts HAVE an analysis, so a
        //           chip with nothing to rate is disabled rather than offering a save that
        //           cannot succeed -- the 1.0.55.101 rule.
        // 1.0.0.4 - THE SAME ALERT WAS BEING DRAWN TWICE, ONCE PER STATE.
        //           PendingAlertApproval carries FRSAlertId. When a pending alert is
        //           approved it is promoted to an FRSAlert and that column is filled in --
        //           row 3010 carries FRSAlertId 649845. So both sources return the SAME
        //           physical alert under DIFFERENT ids, and 1.0.0.2's de-dup could not see
        //           it: it compared pending.Id against FRSAlert.Id, which are two unrelated
        //           id spaces. Every approved alert appeared twice, once as A and once as D.
        //           Worse, the two spaces can collide by coincidence -- a pending Id that
        //           happens to equal an unrelated FRSAlert Id was being dropped as a
        //           duplicate. The board now de-dups on FRSAlertId and keeps the two id
        //           spaces in separate sets.
        //
        //           A pending row with FRSAlertId set is SKIPPED outright. It is not
        //           discarded -- it was approved, and the acknowledged source is the
        //           authority for it.
        //
        //           DISCARD REMARK, at last. DiscardRemark and DiscardWasValid are columns
        //           on PendingAlertApproval and always have been; the stored procedure
        //           simply never selected them, which is why every discard flyout read
        //           "No remark was recorded". With the procedure updated they now reach the
        //           card. AiVerdict and AiAnalyzedAt come across too.
        //
        //           All six new fields are read BY NAME, so this compiles and runs against
        //           the current model and starts using them the moment the procedure is
        //           updated and the EDMX regenerated. No second code change.
        //
        //           NOT USED: Status. It is now selected and carried so it is visible, but
        //           what its values mean is not established, and inferring semantics from a
        //           column name is what produced 1.0.0.0 through 1.0.0.2. Tell me the
        //           mapping and the board can use it instead of inferring from FRSAlertId.
        // 1.0.0.3 - DISCARDED CARDS HAD NO TIME, NO DURATION AND NO RESET.
        //           PendingAlertApproval has no SetTimeStamp column.
        //           spGetPendingAlertWithPagination selects pa.DeviceTimestamp and
        //           pa.ResetTimeStamp; there is no set time in that result set at all. So
        //           every pending row reached BuildRow with SetTimeStamp == DateTime.MinValue,
        //           which renders TimeText as "-" and TimeSort as "".
        //
        //           That one empty field took three others down with it:
        //             - DurationText needs a set time, so it came back empty;
        //             - hasReset requires setTs != MinValue, so ResetText was blanked even
        //               on alerts that HAD cleared -- the reset was read, then discarded;
        //             - the view sorts and groups on timeSort, so every discarded row fell
        //               into one undated bucket.
        //           The board only looked half-broken because the approved rows come from
        //           FRSAlert, which does populate SetTimeStamp.
        //
        //           The pending branch now reads DeviceTimestamp as its set time. It is
        //           read through a small name-based helper rather than a direct property,
        //           for the same reason AuditText exists: I can see the column in the
        //           stored procedure but not the property it maps to on Domain.PendingAlert,
        //           and a wrong guess would not compile.
        //
        //           ALSO CONFIRMED BY THAT PROCEDURE: the pending result set has no Remark
        //           and no MaintainerRemarks column, and PendingAlertApproval has no
        //           discard-reason column of any kind. DiscardRemark can therefore never
        //           populate from this source -- see the note on the discard remark below.
        // 1.0.0.2 - THE DECISION NOW COMES FROM THE SOURCE, NOT FROM AlertAudit.IsAlert.
        //           1.0.0.0 and 1.0.0.1 both read the verdict out of a joined AlertAudit
        //           row: IsAlert == true meant approved, anything else meant discarded.
        //           That was my inference, taken from the shape of AlertAnalysis's skip
        //           condition, and it was wrong. It also could not be right in practice --
        //           it made every alert's state depend on a join succeeding, so one failed
        //           lookup emptied the Approved filter, which is exactly what happened:
        //           344 alerts, 344 discarded, 0 approved.
        //
        //           The actual rule, stated by the owner: "approved alerts is same which
        //           was showing in alert analysis". So:
        //             APPROVED  = every row AlertAnalysis/Index shows      (IFRSAlertService)
        //             DISCARDED = every row PendingAlertAnalysis/Index shows (IPendingAlertService)
        //           Each branch below is a VERBATIM copy of that page's own filter, so the
        //           two states are the two pages by construction. If a row shows on Alert
        //           Analysis today it is Approved here; if it shows on Pending Alert
        //           Analysis today it is Discarded here. Nothing is inferred.
        //
        //           AlertAudit is still fetched and joined, but ONLY to supply the remark,
        //           the reviewer and the rating. A failed join now costs a remark, not a
        //           state -- it can no longer move an alert from one filter to the other.
        // 1.0.0.1 - APPROVED ALERTS WERE ALL LANDING UNDER DISCARDED. The audit join was
        //           matching nothing, so IsAlert stayed null on every row and every row
        //           resolved to "D".
        //
        //           Why it was invisible until now: AlertAnalysis only skips a row when
        //           IsTest != null, and IsTest is set ONLY when the join matched. A join
        //           that matches nothing therefore leaves that page displaying every row
        //           exactly as before -- the page never reads the result it just failed to
        //           get. This board does read it, so the same broken join emptied the
        //           Approved filter.
        //
        //           Three fixes, smallest first:
        //           (a) The join required AlertDate and SetTimeStamp to agree on Date, Hour
        //               AND Minute. Any drift -- an audit written a minute after the alert,
        //               a UTC/IST difference on one of the two columns -- produced zero
        //               matches. It now joins on ALERT ID, and uses the timestamp only to
        //               disambiguate when one alert id carries several audit rows.
        //           (b) The board always sends a date window, so GetAlertAudit(from,to) was
        //               always the fetch used and GetByAlertId never ran. That endpoint is
        //               reached through ToShortDateString(), which is CULTURE-DEPENDENT --
        //               dd-MM-yyyy on one server, MM-dd-yyyy on another. When it comes back
        //               empty the id-based fetch now runs as a fallback.
        //           (c) One try/catch wrapped both sources, so a throw anywhere discarded
        //               every row already collected and reported "no alerts". Each source
        //               now has its own, and both log what they got.
        //
        //           Every row now carries AuditMatched so a failed join is visible in the
        //           payload instead of silently reading as a verdict, and LiveAlertsDiag
        //           returns the counts for one window without touching the board.
        // 1.0.0.0 - Forked from AlertAnalysisController 1.0.6.6.
        //           GetLiveAlerts reads BOTH sources -- IFRSAlertService (joined to
        //           AlertAudit) and IPendingAlertService -- and returns one list, tagging
        //           each row with a review decision: "A" approved, "D" discarded. There is
        //           no third state: an alert awaiting review IS a discarded alert as far as
        //           this board is concerned, so the pending source maps to "D" and
        //           IsAlert == null does too. Rows are keyed by Id with the acknowledged
        //           source winning, because an alert that has been through audit carries a
        //           verdict and its pending twin does not. Test alerts are still skipped.
        //           Also projected: DecisionBy / DecisionAt, RailwayRemark (approved),
        //           DiscardRemark (discarded) and AiRating / AiRemark, so a card can show
        //           the right remark for the right decision without a second round trip.
        //
        // Inherited from AlertAnalysisController 1.0.6.6 and earlier (unchanged here):
        // 1.0.6.6 - LiveValues log lines carry the RUN ID and ALERT ID, so a failure can be
        //           joined to the AI diagnostic that produced it. runId arrives from the
        //           browser and is sanitised before it reaches a log file -- an unfiltered
        //           id could forge a line break and inject a fake entry.
        // 1.0.6.5 - LiveValue is an EdgeX DataAPI route (ProxyBaseUrl), NOT the FRS base.
        //           Calling the wrong host 404'd and the catch swallowed the reason.
        // 1.0.6.4 - LiveValue returns Value / AssetAttributeId / TagUnit / TimestampDevice,
        //           not CurrentValue / AssetAttributeName / MinValue / MaxValue.
        // 1.0.6.2 - Remark + MaintainerRemarks projected; they were fetched and discarded.
        // 1.0.6.1 - ResetTimeStamp is DateTime? -- the null case IS the "still open" alert.
        // 1.0.5.0 - VerdictDryRun sets TrySkipIisCustomErrors so JSON error bodies survive.
        // 1.0.3.0 - VerdictDryRun uses HttpWebRequest, not HttpClient: the System.Net.Http
        //           reference/binding-redirect can stop the app pool at startup.
        // ------------------------------------------------------------------

        // 1.0.6.6: every LiveValues line carries the RUN ID and ALERT ID. Without them a failure
        // logs into a different file from the AI diagnostic with nothing to join on but a timestamp,
        // so the one line that explains a blank card cannot be found from the run that produced it.
        // runId is the same traceId the diagnostic prints (sse-xxxxxxxx).
        private static void AiLogLive(string runId, int alertId, string msg)
        {
            try
            {
                string tag = "[UnifiedAlertAnalysis]"
                           + (string.IsNullOrEmpty(runId) ? "" : " [" + SafeIdForLog(runId) + "]")
                           + (alertId > 0 ? " [alert " + alertId + "]" : "");
                System.Diagnostics.Trace.TraceInformation(tag + " " + msg);
            }
            catch { }
        }

        // 1.0.0.1: one line per source per load. Without it, "no approved alerts" and
        // "the audit fetch returned nothing" look identical from the outside -- which is
        // how this shipped. Same lesson as 1.0.6.5: say what failed and why.
        private static void AiLogBoard(string msg)
        {
            try
            {
                System.Diagnostics.Trace.TraceInformation("[UnifiedAlertAnalysis] " + msg);
            }
            catch (Exception)
            {
            }
        }

        // The id comes from the browser, so it is untrusted input on its way into a log file.
        // Cap it and strip anything that could forge a new log line or a new field.
        private static string SafeIdForLog(string v)
        {
            if (string.IsNullOrEmpty(v)) { return ""; }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Length && sb.Length < 40; i++)
            {
                char c = v[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') { sb.Append(c); }
            }
            return sb.ToString();
        }
        // 1.0.6.4 - LiveValues parsed fields the DataAPI does not return. Its response carries
        //           Value / AssetAttributeId / TagUnit / TimestampDevice -- not CurrentValue,
        //           AssetAttributeName, MinValue or MaxValue. Every row failed the numeric parse and
        //           was skipped, so Refresh reported "No readings returned" every time. Now reads the
        //           real fields; names and safe ranges are merged client-side from the snapshot.
        // 1.0.6.3 - LiveValues endpoint for the AI popup's Live data chapter: on-demand current
        //           readings with safe-range state decided server-side, so the card and the
        //           analysis-time snapshot cannot disagree about what "in range" means.
        // 1.0.6.2 - Projects Remark + MaintainerRemarks. They were fetched and discarded. A bare
        //           "true"/"false" in Remark is the maintainer confirming whether the alert was
        //           genuine -- the only ground truth the system holds, and the basis for measuring
        //           verdict accuracy.
        // 1.0.6.1 - ResetTimeStamp is DateTime? (nullable). 1.0.6.0 assigned it straight to a
        //           DateTime, which does not compile. Read through HasValue/Value -- and note that
        //           the null case is not an edge case here: it IS the "still open" alert, which the
        //           Duration/Rectification columns are meant to render as a dash.
        // 1.0.6.0 - GetLiveAlerts returns the columns the board table was missing: ZoneName,
        //           DivisionName, ResetTimeStamp (the populated clear time -- RectificationDateTime
        //           is null on live rows) and a precomputed DurationText (reset - set, d-hh:mm:ss).
        //           Duration is computed HERE, not in the browser: both timestamps are server-side
        //           DateTime, so formatting once avoids every client re-parsing ISO strings and
        //           disagreeing about timezone. An alert with no reset is still OPEN -- it returns
        //           empty strings, and the view renders a dash rather than inventing a duration.

        // Version history (one line per shipped version, newest first):
        // 1.0.5.0 - VerdictDryRun sets TrySkipIisCustomErrors so JSON error bodies are
        //           not replaced by the IIS error page; names the missing config key
        // 1.0.4.0 - VerdictDryRun: encode non-JSON upstream bodies instead of splicing
        //           them raw (produced invalid JSON and hid the real failure)
        // 1.0.3.0 - VerdictDryRun uses HttpWebRequest instead of HttpClient: the
        //           System.Net.Http reference/binding-redirect can stop the app pool
        // 1.0.2.0 - VerdictDryRun: server-side proxy that calls the AnalyzeAlertJson
        //           service endpoint so a triggered alert can be dry-run against the
        //           exact contract the alert service will consume
        // 1.0.1.0 - GetLiveAlerts skips Test alerts (Validity) and Maintenance alerts (Remark)
        // 1.0.0.0 - Initial AlertAnalysis controller

        private readonly IFRSAlertService _frsAlertService;

        // 1.0.7.0: the second source. Registered already -- PendingAlertAnalysisController
        // resolved the same interface -- so this is a constructor argument, not a new
        // container registration.
        private readonly IPendingAlertService pendingAlertService;

        // 1.0.1.3: needed for the Asset Type filter. AlertDetailReportController already
        // resolves this interface, so it is registered -- a constructor argument, not a new
        // container registration.
        private readonly IAssetTypeService _assetTypeService;
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;

        public UnifiedAlertAnalysisController(IFRSAlertService frsAlertService,
                                      IPendingAlertService pendingAlertService,
                                      IAssetTypeService assetTypeService,
                                      ISiteService siteService,
                                      IZoneService zoneService,
                                      IDivisionService divisionService)
        {
            _frsAlertService = frsAlertService;
            this.pendingAlertService = pendingAlertService;
            _assetTypeService = assetTypeService;
            // 1.0.2.0: the site/zone/division services exist here for the same reason they do
            // on AlertLiveController -- they are what resolves the CALLER'S ASSIGNED sites.
            // This board previously had none of them, which is exactly why it showed every
            // site in the estate instead of the user's own.
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
        }

        // GET: FRS25/UnifiedAlertAnalysis
        public ActionResult Index()
        {
            // Same call AlertLiveController.Index makes. Fills ViewBag.Zones / Divisions /
            // Sites from the user's assignment so the filter cascade offers only sites the
            // caller actually holds.
            Helper.FilterCacheHelper.SetFilterViewBag(ViewBag, _siteService, _zoneService,
                                          _divisionService, includeAssetType: false);

            // 1.0.2.2: the dropdown must offer exactly what the board will render. Filtering
            // the rows but not the filter leaves the user picking a site that silently
            // returns nothing, which reads as a broken board rather than a scoped one.
            var effectiveSites = AssignedSiteIds(null);
            ViewBag.Sites = new SelectList(
                (Helper.FilterCacheHelper.GetAllSites(_siteService) ?? new List<Domain.Site>())
                    .Where(s => s != null && effectiveSites.Contains(s.Id))
                    .ToList(), "Id", "Name");

            return View();
        }

        // ==================================================================
        // 1.0.1.3 -- STATION -> ASSET TYPE -> CAUSE CODE.
        // The same chain Alert History uses, with the same endpoint names and the same
        // shapes, so the two pages ask the service the same questions: the selected station
        // narrows which asset types are offered, and the selected asset type narrows which
        // cause codes are offered. The board previously derived its cause list from the
        // alerts already loaded, so the dropdown could only ever offer codes that had
        // already fired -- no way to filter for a cause to confirm it had NOT occurred.
        // ==================================================================

        private List<Domain.AlertInfo> GetCauseCode()
        {
            var causeCodes = new List<Domain.AlertInfo>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AlertInfo/GetAll")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        causeCodes = JsonConvert.DeserializeObject<List<Domain.AlertInfo>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
                // AlertDetailReport rethrows here because its Index cannot render without a
                // cause list. This is an AJAX filter feed, so an empty list degrades to
                // "no cause codes found" rather than taking the whole board down with it.
                causeCodes = new List<Domain.AlertInfo>();
            }
            return causeCodes ?? new List<Domain.AlertInfo>();
        }

        [HttpPost]
        public JsonResult GetAlertInfoByFilter(Domain.FRSAlert request)
        {
            var allCauses = new List<Domain.AlertInfo>();
            var seenIds = new HashSet<int>();

            try
            {
                if (request != null && request.AssetTypeIds != null && request.AssetTypeIds.Any())
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(request);
                        var str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("AlertInfo/GetBy"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            var items = JsonConvert.DeserializeObject<List<Domain.AlertInfo>>(jsonString);
                            if (items != null)
                            {
                                foreach (var item in items)
                                {
                                    if (seenIds.Add(item.Id)) { allCauses.Add(item); }
                                }
                            }
                        }
                    }
                }
                else
                {
                    // No asset type selected -- every cause code.
                    allCauses = GetCauseCode();
                }
            }
            catch (Exception ex)
            {
                AiLogBoard("GetAlertInfoByFilter failed: " + ex.Message);
                allCauses = new List<Domain.AlertInfo>();
            }

            return Json(allCauses.OrderBy(c => c.Name).ToList(), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult GetAssetTypeBySiteIds(List<int> siteIds)
        {
            var mAssetTypes = new List<Domain.AssetType>();
            try
            {
                var lister = new AssetTypeLister();
                lister.SiteIds = siteIds;
                mAssetTypes = _assetTypeService.GetLister(lister);
            }
            catch (Exception ex)
            {
                AiLogBoard("GetAssetTypeBySiteIds failed: " + ex.Message);
                mAssetTypes = new List<Domain.AssetType>();
            }
            return Json(mAssetTypes ?? new List<Domain.AssetType>(), JsonRequestBehavior.AllowGet);
        }

        // ------------------------------------------------------------------
        // POST /FRS25/UnifiedAlertAnalysis/GetLiveAlerts
        //   source 1 -- IFRSAlertService, joined to AlertAudit. IsAlert == true is an
        //               APPROVED alert; anything else is DISCARDED.
        //   source 2 -- IPendingAlertService. Every row is DISCARDED: there is no
        //               separate "awaiting review" state on this board.
        // Test alerts are still skipped and never reach the board or the AI analysis.
        // ------------------------------------------------------------------
        [HttpPost]
        public JsonResult GetLiveAlerts(DateTime? fromDate, DateTime? toDate)
        {
            var rows = new List<object>();
            // 1.0.1.0: FRS ids collected while the approved loop runs, then one SQL query
            // for all of them. One call instead of N per approved alert.
            var approvedFrsIds = new List<int>();
            var counts = new BoardCounts();
            BuildBoardRows(fromDate, toDate, rows, counts, approvedFrsIds);

            AiLogBoard(counts.ToLogLine());

            var json = Json(rows);
            json.MaxJsonLength = int.MaxValue;
            return json;
        }

        // ------------------------------------------------------------------
        // POST /FRS25/UnifiedAlertAnalysis/LiveAlertsDiag
        // The same load, reported as numbers instead of rows. "Approved is empty" and
        // "the audit fetch returned nothing" are different faults with the same symptom,
        // and until 1.0.0.1 there was no way to tell them apart from a browser. Same
        // window as the board, so the two are directly comparable.
        // ------------------------------------------------------------------
        [HttpPost]
        public JsonResult LiveAlertsDiag(DateTime? fromDate, DateTime? toDate)
        {
            var rows = new List<object>();
            var counts = new BoardCounts();
            BuildBoardRows(fromDate, toDate, rows, counts);

            return Json(new
            {
                window = new
                {
                    from = fromDate.HasValue ? fromDate.Value.ToString("dd/MM/yyyy HH:mm:ss") : "(none)",
                    to = toDate.HasValue ? toDate.Value.ToString("dd/MM/yyyy HH:mm:ss") : "(none)"
                },
                acknowledgedRows = counts.AckRows,
                acknowledgedError = counts.AckError ?? "",
                auditRowsByDate = counts.AuditByDate,
                auditRowsById = counts.AuditById,
                auditFetchError = counts.AuditError ?? "",
                auditJoinMatched = counts.AuditMatched,
                auditJoinMissed = counts.AckRows - counts.AuditMatched,
                testAlertsSkipped = counts.TestSkipped,
                approvedFromAcknowledgedSource = counts.Approved,
                skippedAsTestOrUnverified = counts.TestSkipped,
                pendingRows = counts.PendRows,
                pendingError = counts.PendError ?? "",
                pendingDuplicatesDropped = counts.PendDuplicates,
                pendingRowsWithNoTimestamp = counts.PendNoTimestamp,
                // 1.0.0.7: distinguishes "no discarded alerts in this window" from "you are
                // not allowed to see them" -- the same zero on the board otherwise.
                pendingSuppressedByPrivilege = counts.PendSuppressed,
                pendingPromotedToFrsAlert = counts.PendPromoted,
                pendingWithDiscardRemark = counts.PendWithRemark,
                frsSiteIds = string.Join(",", FrsSiteIds().OrderBy(x => x)),
                assignedSiteCount = counts.AssignedSiteCount,
                effectiveSiteIds = string.Join(",", AssignedSiteIds(null).OrderBy(x => x)),
                siteScopeError = counts.SiteScopeError ?? "",
                siteFilteredApproved = counts.SiteFiltered,
                siteFilteredPending = counts.PendSiteFiltered,
                approvalRemarkError = LastApprovalRemarkError ?? "",
                totalRowsReturned = rows.Count,
                readMe = "1.0.0.2: APPROVED = the rows AlertAnalysis shows (IFRSAlertService). "
                       + "DISCARDED = the rows PendingAlertAnalysis shows (IPendingAlertService). "
                       + "AlertAudit supplies the remark and the rating only; it no longer decides state. "
                       + "If approvedFromAcknowledgedSource is 0, compare acknowledgedRows against what "
                       + "the Alert Analysis page shows for the same window -- they must be equal."
            });
        }

        // Plain counters, filled by BuildBoardRows and read by both actions above.
        private class BoardCounts
        {
            public int AckRows;
            public int AuditByDate;
            public int AuditById;
            public int AuditMatched;
            public int TestSkipped;
            public int Approved;
            public int PendRows;
            public int PendDuplicates;
            public int PendNoTimestamp;
            public bool PendSuppressed;
            public int PendPromoted;
            public int PendWithRemark;
            // 1.0.2.0: rows dropped by the site allow-list, per source. "This site is not on
            // the board" and "this site had no alerts" are different facts and the board shows
            // the same empty space for both.
            public int SiteFiltered;
            public int PendSiteFiltered;
            // 1.0.2.2: the two inputs to the scope, kept apart. "frs=8 assigned=40
            // effective=3" reads very differently from "assigned=0" -- one is a user posted
            // mostly off-board, the other is a broken assignment lookup.
            public int FrsSiteCount;
            public int AssignedSiteCount;
            public string SiteScopeError;
            public string AckError;
            public string AuditError;
            public string PendError;

            public string ToLogLine()
            {
                return "board load: ack=" + AckRows
                     + " auditByDate=" + AuditByDate
                     + " auditById=" + AuditById
                     + " matched=" + AuditMatched
                     + " approved=" + Approved
                     + " skipped=" + TestSkipped
                     + " pending=" + PendRows
                     + " pendingDupes=" + PendDuplicates
                     + " pendingNoTimestamp=" + PendNoTimestamp
                     + " pendingPromotedToFrs=" + PendPromoted
                     + " pendingWithDiscardRemark=" + PendWithRemark
                                          + " siteFilteredApproved=" + SiteFiltered
                     + " siteFilteredPending=" + PendSiteFiltered
                     + " frsSites=" + FrsSiteCount
                     + " assignedSites=" + AssignedSiteCount
                     + (AckError != null ? " ACK_ERROR=" + AckError : "")
                     + (AuditError != null ? " AUDIT_ERROR=" + AuditError : "")
                     + (PendError != null ? " PEND_ERROR=" + PendError : "");
            }
        }

        // ------------------------------------------------------------------
        // 1.0.0.1: each source in its OWN try/catch. Before, one try wrapped both, and a
        // throw in the second discarded every row the first had already collected -- the
        // board then reported "no alerts in this window", which is not what happened.
        // ------------------------------------------------------------------
        private void BuildBoardRows(DateTime? fromDate, DateTime? toDate,
    List<object> rows, BoardCounts counts, List<int> approvedFrsIds = null)
        {
            var seenFrsIds = new HashSet<int>();
            var seenPendingIds = new HashSet<int>();

            // 1.0.1.3: PendingAlertApproval.Id values found carrying an FRSAlertId, filled by
            // FetchApprovalRemarks. A SECOND de-dup key that comes from the TABLE rather than
            // from the list endpoint, so a promoted row is still suppressed even if that
            // endpoint stops projecting FRSAlertId the way it already stopped projecting
            // ApprovalRemark.
            var promotedPendingIds = new HashSet<int>();

            // 1.0.2.0: FRSAlert.Id -> the PendingAlertApproval.Id it was promoted from. Filled
            // by the same FetchApprovalRemarks read. It travels to the browser on the row so
            // the Detail panel can ask for the alert's pre-approval analyses without a second
            // lookup, and so it can still ask on a host where SQL is unreachable.
            var pendingIdByFrsId = new Dictionary<int, int>();

            // Read once per load, not once per row -- the config lookup and the parse are
            // cheap but they are not free, and a wide window is thousands of rows.
            var boardSites = AssignedSiteIds(counts);

            // ---------------- pending list, loaded FIRST ----------------
            // 1.0.1.1: PendingAlertApproval is the ONLY place the approval remark lives, and
            // FRSAlertId is the ONLY thing that tells an approved pending row from a discarded
            // one. Loading it before the approved loop lets that loop stamp the remark straight
            // into BuildRow -- no second pass, no reflection rebuild, no N+1 fetch.
            // Loaded for EVERY caller: an unprivileged caller still gets the remark on an
            // approved alert, they just never get a "D" row. That is enforced at emit time.
            Domain.PendingAlertLister pendLister = null;
            var approvalByFrsId = new Dictionary<int, ApprovalInfo>();

            try
            {
                pendLister = new Domain.PendingAlertLister();
                if (fromDate.HasValue) { pendLister.SearchCriteria.FromDate = fromDate.Value; }
                if (toDate.HasValue) { pendLister.SearchCriteria.ToDate = toDate.Value; }
                // Scope at the SOURCE. The Contains guard in the emit loop still stands --
                // this only stops the API shipping rows that would be discarded here anyway.
                if (boardSites.Count > 0) { pendLister.SearchCriteria.SiteIds = boardSites.ToList(); }
                pendLister.Pager.Take = -1;
                pendLister = pendingAlertService.GetAcknowledgementAllAlert(pendLister);

                //if (pendLister != null && pendLister.mPendingAlerts != null)
                //{
                //    counts.PendRows = pendLister.mPendingAlerts.Count;
                //    foreach (var p in pendLister.mPendingAlerts)
                //    {
                //        int frsId = ReadInt(p, new string[] { "FRSAlertId", "FrsAlertId", "FRSAlertID" });
                //        if (frsId <= 0) { continue; }          // genuinely discarded, handled below

                //        counts.PendPromoted++;
                //        // ApprovalRemark is what the API projects for an approved row; DiscardRemark
                //        // is the same physical column under the misleading name, so it is the
                //        // fallback rather than a separate field.
                //        approvalByFrsId[frsId] = new ApprovalInfo
                //        {
                //            Remark = ReadText(p, new string[] { "ApprovalRemark", "DiscardRemark" }),
                //            DecidedAt = ReadTimestamp(p, new string[] { "UpdatedAt", "ProcessedAt", "AiAnalyzedAt" }),
                //            AiVerdict = ReadText(p, new string[] { "AiVerdict" })
                //        };
                //    }
                //}
                if (pendLister != null && pendLister.mPendingAlerts != null)
                {
                    counts.PendRows = pendLister.mPendingAlerts.Count;
                    foreach (var p in pendLister.mPendingAlerts)
                    {
                        // FRSAlertId is nullable and is written ONLY when the row was approved
                        // and promoted into FRSAlert. Null or 0 means it was discarded, and it
                        // stays on the discarded side.
                        int frsId = p.FRSAlertId.GetValueOrDefault();
                        if (frsId <= 0) { continue; }

                        counts.PendPromoted++;

                        // ApprovalRemark is the approver's own words and is THE remark the
                        // approved card must show. DiscardRemark is a fallback only for older
                        // rows, written before the two remarks had separate columns.
                        string remark = (p.ApprovalRemark ?? "").Trim();

                        // AcknowledgemenTimeStamp is WHEN IT WAS APPROVED. AiAnalyzedAt is when
                        // the model looked at it -- a different event, and only a stand-in when
                        // the approval stamp was never written.
                        DateTime decidedAt = p.AcknowledgemenTimeStamp
                                             ?? p.AiAnalyzedAt
                                             ?? DateTime.MinValue;

                        // One FRSAlert can sit behind SEVERAL pending rows -- a re-analysis
                        // writes another. Last-write-wins would pick at random, so a row that
                        // carries a real remark is never replaced by one that carries none.
                        ApprovalInfo existing;
                        if (approvalByFrsId.TryGetValue(frsId, out existing)
                            && existing.Remark.Length > 0 && remark.Length == 0)
                        {
                            continue;
                        }

                        approvalByFrsId[frsId] = new ApprovalInfo
                        {
                            Remark = remark,
                            DecidedAt = decidedAt,
                            AiVerdict = p.AiVerdict ?? "",
                            // 1.0.2.9: the bit, if this is where it lives. Costs nothing
                            // while the column is absent -- BitText just returns "".
                            AiConfirmed = BitText(p, AI_CONFIRMED_NAMES)
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                counts.PendError = ex.GetType().Name + " " + ex.Message;
                AiLogBoard("pending source failed: " + ex.ToString());
            }

            // ---------------- source 1: acknowledged / approved ----------------
            try
            {
                var ackLister = new Domain.FRSAlertLister();
                if (fromDate.HasValue) { ackLister.SearchCriteria.FromDate = fromDate.Value; }
                if (toDate.HasValue) { ackLister.SearchCriteria.ToDate = toDate.Value; }
                if (boardSites.Count > 0) { ackLister.SearchCriteria.SiteIds = boardSites.ToList(); }
                ackLister.Pager.Take = -1;
                ackLister = _frsAlertService.GetAcknowledgementAllAlert(ackLister);

                if (ackLister != null && ackLister.mFRSAlerts != null && ackLister.mFRSAlerts.Count > 0)
                {
                    counts.AckRows = ackLister.mFRSAlerts.Count;
                    var ids = ackLister.mFRSAlerts.Select(x => x.Id).ToList();

                    // 1.0.1.3: THE source of the approval remark. Read from
                    // dbo.PendingAlertApproval keyed by FRSAlertId, because
                    // GetAcknowledgementAllAlert returns ApprovalRemark null -- its procedure
                    // does not project the column. One batched read for the whole window,
                    // BEFORE the emit loop, so the remark is in hand when BuildRow is called.
                    var approvalRemarks = FetchApprovalRemarks(ids, promotedPendingIds, pendingIdByFrsId);
                    var mAlertAudits = FetchAudits(ackLister.SearchCriteria.FromDate,
                                                   ackLister.SearchCriteria.ToDate, ids, counts);
                    var auditsByAlert = GroupAuditsByAlertId(mAlertAudits);

                    foreach (var frsAlert in ackLister.mFRSAlerts)
                    {
                        var alertAudit = MatchAudit(auditsByAlert, frsAlert.Id, frsAlert.SetTimeStamp);
                        if (alertAudit != null && alertAudit.Id > 0)
                        {
                            counts.AuditMatched++;
                            frsAlert.mAlertAudit = alertAudit;
                            frsAlert.IsTest = alertAudit.IsTest;
                            frsAlert.IsAlert = alertAudit.IsAlert;
                            if (alertAudit.IsTest) { frsAlert.IsAlert = null; }
                        }
                    }

                    foreach (var a in ackLister.mFRSAlerts)
                    {
                        // 1.0.2.0: site allow-list, checked BEFORE anything else so a site that
                        // is off this board never reaches the test/audit logic and never lands
                        // in a counter that suggests it was considered.
                        if (!boardSites.Contains(a.SiteId)) { counts.SiteFiltered++; continue; }

                        // ---- VERBATIM from AlertAnalysisController 1.0.6.6 -- do not "improve" ----
                        if (a.IsTest != null)
                        {
                            if (a.IsTest.Value || a.IsAlert == null) { counts.TestSkipped++; continue; }
                        }
                        // ---- end verbatim ----

                        if (seenFrsIds.Contains(a.Id)) { continue; }
                        seenFrsIds.Add(a.Id);

                        var audit = a.mAlertAudit;
                        counts.Approved++;
                        if (approvedFrsIds != null) { approvedFrsIds.Add(a.Id); }

                        ApprovalInfo appr;
                        if (!approvalByFrsId.TryGetValue(a.Id, out appr)) { appr = ApprovalInfo.Empty; }

                        // PendingAlertApproval.ApprovalRemark, joined on FRSAlertId = FRSAlert.Id.
                        // The table wins; the list endpoint's value is only a fallback, and it is
                        // null today. If it led, a good remark would be silently blanked.
                        string approvalRemark;
                        if (!approvalRemarks.TryGetValue(a.Id, out approvalRemark)
                            || approvalRemark.Length == 0)
                        {
                            approvalRemark = appr.Remark;
                        }
                        if (approvalRemark.Length > 0) { counts.PendWithRemark++; }

                        // 1.0.2.9: FRSAlert first -- it is the row the approval belongs to
                        // -- then its audit, then the pending row the remark already came
                        // from. First source with an answer wins; "" means none of the
                        // three recorded one, and that stays "" all the way to the panel.
                        string aiConfirmed = BitText(a, AI_CONFIRMED_NAMES);
                        if (aiConfirmed.Length == 0) { aiConfirmed = BitText(audit, AI_CONFIRMED_NAMES); }
                        if (aiConfirmed.Length == 0) { aiConfirmed = appr.AiConfirmed; }

                        // 1.0.2.0: the review-stage id this alert was promoted from, 0 when
                        // it never went through review (or when SQL could not say). The row
                        // carries it so Detail can merge the analyses made before approval.
                        int reviewId;
                        if (!pendingIdByFrsId.TryGetValue(a.Id, out reviewId)) { reviewId = 0; }

                        rows.Add(BuildRow(a.Id, a.SiteId, a.SiteName, a.ZoneName, a.DivisionName,
                                          AlertTypeName(a.AlertTypeId), a.AssetName, a.AssetType,
                                          a.CauseCode, a.SetTimeStamp, a.ResetTimeStamp,
                                          a.Remark, a.MaintainerRemarks, "A", audit,
                                          approvalRemark, "", appr.AiVerdict, appr.DecidedAt,
                                          aiConfirmed, reviewId));
                    }
                }
            }
            catch (Exception ex)
            {
                counts.AckError = ex.GetType().Name + " " + ex.Message;
                AiLogBoard("acknowledged source failed: " + ex.ToString());
            }

            // ---------------- source 2 emit: discarded only ----------------
            // 1.0.0.7 still holds: an unprivileged caller gets approved alerts and no sign a
            // second source exists. Enforced HERE, not by hiding cards in the view.
            if (!AaPrivileged())
            {
                counts.PendSuppressed = true;
                AiLogBoard("pending source suppressed: caller is not sitekeeping");
                return;
            }
            if (pendLister == null || pendLister.mPendingAlerts == null) { return; }

            foreach (var a in pendLister.mPendingAlerts)
            {
                // 1.0.2.0: same allow-list as the approved side. Both sources or neither --
                // filtering one and not the other is how a site disappears from the green
                // cards and stays visible on the grey ones.
                if (!boardSites.Contains(a.SiteId)) { counts.PendSiteFiltered++; continue; }

                // Two independent ways to know this row was approved and promoted: the list
                // endpoint's FRSAlertId, and the table's own answer via FetchApprovalRemarks.
                // Either is enough to suppress it -- the approved card already stands for it.
                int promotedFrsId = a.FRSAlertId.GetValueOrDefault();
                if (promotedFrsId > 0 || promotedPendingIds.Contains(a.Id))
                {
                    if (promotedFrsId > 0 && seenFrsIds.Contains(promotedFrsId)) { counts.PendDuplicates++; }
                    continue;
                }

                // seenPendingIds is SEPARATE from seenFrsIds on purpose: unrelated sequences, and
                // a pending Id that happens to equal an unrelated FRSAlert Id must not be dropped.
                if (seenPendingIds.Contains(a.Id)) { continue; }
                seenPendingIds.Add(a.Id);

                //DateTime pendingSetTs = ReadTimestamp(a, new string[]
                //    { "SetTimeStamp", "DeviceTimestamp", "DeviceTimeStamp", "AlertDateTime" });
                //if (pendingSetTs == DateTime.MinValue) { counts.PendNoTimestamp++; }

                //DateTime pendingResetTs = ReadTimestamp(a, new string[] { "ResetTimeStamp" });
                //DateTime? pendingReset = (pendingResetTs == DateTime.MinValue)
                //    ? (DateTime?)null : pendingResetTs;

                //string discardRemark = ReadText(a, new string[] { "DiscardRemark" });
                //string discardValid = ReadText(a, new string[] { "DiscardWasValid" });
                //string aiVerdict = ReadText(a, new string[] { "AiVerdict" });
                //DateTime discardAt = ReadTimestamp(a, new string[]
                //    { "UpdatedAt", "ProcessedAt", "AiAnalyzedAt" });


                // SetTimeStamp is the set time wherever the mapping populates it; today it is
                // MinValue on this source and DeviceTimeStamp is what actually answers.
                DateTime pendingSetTs = a.SetTimeStamp != DateTime.MinValue
                    ? a.SetTimeStamp
                    : a.DeviceTimeStamp;
                if (pendingSetTs == DateTime.MinValue) { counts.PendNoTimestamp++; }

                // Nullable on the model, and the null case IS the still-open alert.
                DateTime? pendingReset = (a.ResetTimeStamp.HasValue
                                          && a.ResetTimeStamp.Value != DateTime.MinValue)
                    ? a.ResetTimeStamp
                    : (DateTime?)null;

                string discardRemark = (a.DiscardRemark ?? "").Trim();
                string discardValid = a.DiscardWasValid.HasValue
                    ? a.DiscardWasValid.Value.ToString()
                    : "";
                string aiVerdict = a.AiVerdict ?? "";
                DateTime discardAt = a.AiAnalyzedAt ?? DateTime.MinValue;

                // 0 by construction on this branch -- a pending row carrying an FRSAlertId was
                // promoted and was skipped above. Passed from the row rather than hard-coded
                // so that if that rule ever changes, the link follows the data.
                rows.Add(BuildRow(a.Id, a.SiteId, a.SiteName, a.ZoneName, a.DivisionName,
                                  AlertTypeName(a.AlertTypeId), a.AssetName, a.AssetType,
                                  a.CauseCode, pendingSetTs, pendingReset,
                                  a.DiscardRemark, a.MaintainerRemarks, "D", null,
                                  discardRemark, discardValid, aiVerdict, discardAt,
                                  "", promotedFrsId));
            }
        }

        private class ApprovalInfo
        {
            public string Remark = "";
            public DateTime DecidedAt = DateTime.MinValue;
            public string AiVerdict = "";
            public string AiConfirmed = "";
            public static readonly ApprovalInfo Empty = new ApprovalInfo();
        }

        // ------------------------------------------------------------------
        // 1.0.0.1: the date-window fetch is the ONLY one the board ever used, because the
        // board always sends a window. It reaches the API through ToShortDateString(),
        // which is culture-dependent -- dd-MM-yyyy on one server and MM-dd-yyyy on another
        // -- so an endpoint expecting the other order returns nothing and every alert
        // silently loses its verdict. When it comes back empty, fall back to the id-based
        // fetch, which sends no dates at all and therefore cannot be misread.
        // ------------------------------------------------------------------
        private List<Domain.AlertAudit> FetchAudits(DateTime fromDate, DateTime toDate,
                                                    List<int> ids, BoardCounts counts)
        {
            var audits = new List<Domain.AlertAudit>();
            try
            {
                if (fromDate != DateTime.MinValue)
                {
                    audits = GetAlertAudit(fromDate, toDate);
                    counts.AuditByDate = (audits == null) ? 0 : audits.Count;
                }

                if (audits == null || audits.Count == 0)
                {
                    audits = GetAlertAudit(ids);
                    counts.AuditById = (audits == null) ? 0 : audits.Count;
                }
            }
            catch (Exception ex)
            {
                counts.AuditError = ex.GetType().Name + " " + ex.Message;
                AiLogBoard("audit fetch failed: " + ex.ToString());
                audits = new List<Domain.AlertAudit>();
            }
            return audits ?? new List<Domain.AlertAudit>();
        }

        private static Dictionary<int, List<Domain.AlertAudit>> GroupAuditsByAlertId(
            List<Domain.AlertAudit> audits)
        {
            var map = new Dictionary<int, List<Domain.AlertAudit>>();
            if (audits == null)
            {
                return map;
            }
            foreach (var audit in audits)
            {
                if (audit == null || audit.Id <= 0)
                {
                    continue;
                }
                if (!map.ContainsKey(audit.AlertId))
                {
                    map[audit.AlertId] = new List<Domain.AlertAudit>();
                }
                map[audit.AlertId].Add(audit);
            }
            return map;
        }

        // ------------------------------------------------------------------
        // 1.0.0.1: join on ALERT ID, and use the timestamp only to choose between several
        // audit rows that share one id. The 1.0.6.6 join demanded Date, Hour AND Minute
        // agree exactly, so an audit written a minute after its alert -- or a UTC value in
        // one column and a local one in the other -- matched nothing at all. That produced
        // no error and no empty result anyone could see: it produced an alert with no
        // verdict, which this board renders as DISCARDED.
        // ------------------------------------------------------------------
        private static Domain.AlertAudit MatchAudit(Dictionary<int, List<Domain.AlertAudit>> byAlertId,
                                                    int alertId, DateTime setTs)
        {
            List<Domain.AlertAudit> candidates;
            if (byAlertId == null || !byAlertId.TryGetValue(alertId, out candidates) || candidates.Count == 0)
            {
                return null;
            }
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            // Several audit rows for one alert id: prefer an exact minute match, the way
            // 1.0.6.6 did, and otherwise take the one closest in time to the alert.
            foreach (var audit in candidates)
            {
                if (audit.AlertDate.Date == setTs.Date
                    && audit.AlertDate.Hour == setTs.Hour
                    && audit.AlertDate.Minute == setTs.Minute)
                {
                    return audit;
                }
            }

            Domain.AlertAudit best = candidates[0];
            double bestGap = Math.Abs((best.AlertDate - setTs).TotalSeconds);
            for (int i = 1; i < candidates.Count; i++)
            {
                double gap = Math.Abs((candidates[i].AlertDate - setTs).TotalSeconds);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = candidates[i];
                }
            }
            return best;
        }

        private static string AlertTypeName(int alertTypeId)
        {
            if (alertTypeId == (int)E7FRSAdvance.Utility.Utility.AlertType.Failure)
            {
                return "Failure";
            }
            if (alertTypeId == (int)E7FRSAdvance.Utility.Utility.AlertType.Predictive)
            {
                return "Predictive";
            }
            return "";
        }

        // Duration is computed HERE, not in the browser: both timestamps are server-side
        // DateTime, so formatting once avoids every client re-parsing ISO strings and
        // disagreeing about timezone. An alert with no reset is still OPEN -- it returns an
        // empty string, and the view renders a dash rather than inventing a duration.
        // Lifted out of the projection loop in 1.0.7.0 so BOTH sources format identically.
        private static string BuildDurationText(DateTime setTs, DateTime? resetTs)
        {
            if (!resetTs.HasValue)
            {
                return "";
            }
            DateTime reset = resetTs.Value;
            if (reset == DateTime.MinValue || setTs == DateTime.MinValue || reset < setTs)
            {
                return "";
            }
            TimeSpan span = reset - setTs;
            return ((int)span.TotalDays).ToString() + "-"
                 + span.Hours.ToString("00") + ":"
                 + span.Minutes.ToString("00") + ":"
                 + span.Seconds.ToString("00");
        }

        // ------------------------------------------------------------------
        // TEMPORARY SEAM -- see AuditText below.
        // The remark, reviewer and rating fields live on Domain.AlertAudit, but this
        // controller has only ever touched Id, AlertId, AlertDate, IsTest and IsAlert, so
        // the remaining field NAMES are not established anywhere in this file. Reading them
        // by name off a JObject compiles against any shape of that class and returns an
        // empty string when a field is absent; a guessed property name would not compile at
        // all. Replace all six calls with direct property access once the class is confirmed
        // -- this is the ONLY place that needs to change.
        // ------------------------------------------------------------------
        // ------------------------------------------------------------------
        // 1.0.0.3: read a timestamp by property name, trying each in turn.
        // Same reasoning as AuditText: spGetPendingAlertWithPagination selects
        // pa.DeviceTimestamp, but which property that lands on in Domain.PendingAlert --
        // and whether it is DateTime or DateTime? -- is not visible from this file, and a
        // wrong guess is a compile error rather than a graceful miss. PropertyInfo is
        // cached per type, so this costs one dictionary hit per row after the first.
        // ------------------------------------------------------------------
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Reflection.PropertyInfo>
            timestampProperties = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Reflection.PropertyInfo>();

        private static DateTime ReadTimestamp(object row, string[] names)
        {
            if (row == null)
            {
                return DateTime.MinValue;
            }
            Type rowType = row.GetType();
            for (int i = 0; i < names.Length; i++)
            {
                System.Reflection.PropertyInfo prop;
                string key = rowType.FullName + "|" + names[i];
                if (!timestampProperties.TryGetValue(key, out prop))
                {
                    prop = rowType.GetProperty(names[i]);
                    timestampProperties[key] = prop;
                }
                if (prop == null)
                {
                    continue;
                }

                object raw;
                try
                {
                    raw = prop.GetValue(row, null);
                }
                catch (Exception)
                {
                    continue;
                }
                if (raw == null)
                {
                    continue;
                }
                if (raw is DateTime)
                {
                    DateTime value = (DateTime)raw;
                    if (value != DateTime.MinValue)
                    {
                        return value;
                    }
                    continue;
                }
                DateTime parsed;
                if (DateTime.TryParse(raw.ToString(),
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out parsed)
                    && parsed != DateTime.MinValue)
                {
                    return parsed;
                }
            }
            return DateTime.MinValue;
        }

        // 1.0.0.4: the same by-name read, for text and for integers. Six columns were
        // added to spGetPendingAlertWithPagination in this cycle; until the EDMX is
        // regenerated the properties do not exist, and a direct reference would not
        // compile. These return "" / 0 in the meantime, which is what the board already
        // renders today, so the two halves of the change can ship independently.
        private static object ReadProperty(object row, string[] names)
        {
            if (row == null)
            {
                return null;
            }
            Type rowType = row.GetType();
            for (int i = 0; i < names.Length; i++)
            {
                System.Reflection.PropertyInfo prop;
                string key = rowType.FullName + "|" + names[i];
                if (!timestampProperties.TryGetValue(key, out prop))
                {
                    prop = rowType.GetProperty(names[i]);
                    timestampProperties[key] = prop;
                }
                if (prop == null)
                {
                    continue;
                }
                try
                {
                    object raw = prop.GetValue(row, null);
                    if (raw != null)
                    {
                        return raw;
                    }
                }
                catch (Exception)
                {
                }
            }
            return null;
        }

        // Set by FetchApprovalRemarks, read by LiveAlertsDiag. Static because the fetch is
        // static; last-write-wins is fine for a diagnostic.
        private static string LastApprovalRemarkError;
        // SELECT in one round trip. PendingAlertApproval.FRSAlertId links back to the
        // FRSAlert that was promoted from this pending row. DiscardRemark carries the
        // engineer's remark regardless of whether the outcome was approve or discard.
        // Returns only rows that have a non-empty remark -- the caller defaults to "" for
        // any id absent from the dictionary.
        // GET {FRSApiBase}/PendingAlertApproval/GetByFRSAlertId?frsAlertId={id}
        //
        // 1.0.1.0 -- THIS WAS THE BOARD'S SLOWEST STEP.
        // It ran one SEQUENTIAL HTTP GET per approved alert, and built a NEW
        // HttpClientFactory for each one. A window with 200 approved alerts was therefore
        // 200 round trips end to end, plus 200 client/handler allocations -- the load time
        // was latency multiplied by alert count, which is why a wide window crawled.
        //
        // Now: distinct ids only, run with bounded parallelism, and ONE client per worker
        // rather than one per id. Same requests, same endpoint, same result shape -- they
        // simply no longer wait in single file.
        //
        // The degree is deliberately modest. This is someone else's API and the point is to
        // stop serialising, not to flood it.
        //
        // 1.0.2.0: it also hands back WHICH pending row each approved alert came through
        // (pendingIdByFrsId), because this query is already reading exactly that pair and
        // the board needs it on the row. The alternative was a second query over the same
        // table on the same load to learn something this one had in hand and discarded.
        private static Dictionary<int, string> FetchApprovalRemarks(List<int> frsAlertIds,
            HashSet<int> approvedPendingIds, Dictionary<int, int> pendingIdByFrsId = null)
        {
            var result = new Dictionary<int, string>();
            if (frsAlertIds == null || frsAlertIds.Count == 0) { return result; }

            var ids = frsAlertIds.Where(x => x > 0).Distinct().ToList();
            if (ids.Count == 0) { return result; }

            var cs = ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"];
            if (cs == null || string.IsNullOrWhiteSpace(cs.ConnectionString))
            {
                AiLogBoard("FetchApprovalRemarks: E7MRIV2DB_SQL connection string is missing.");
                return result;
            }

            try
            {
                using (var cn = new System.Data.SqlClient.SqlConnection(cs.ConnectionString))
                {
                    cn.Open();

                    const int batchSize = 500;
                    for (int start = 0; start < ids.Count; start += batchSize)
                    {
                        var batch = ids.Skip(start).Take(batchSize).ToList();
                        if (batch.Count == 0) { continue; }

                        using (var cmd = cn.CreateCommand())
                        {
                            cmd.CommandTimeout = 10;
                            var parameterNames = new List<string>();

                            for (int i = 0; i < batch.Count; i++)
                            {
                                string name = "@F" + i;
                                parameterNames.Add(name);
                                cmd.Parameters.Add(name, System.Data.SqlDbType.Int).Value = batch[i];
                            }

                            cmd.CommandText =
                                "SELECT Id, FRSAlertId, ApprovalRemark " +
                                "FROM dbo.PendingAlertApproval " +
                                "WHERE FRSAlertId IN (" + string.Join(",", parameterNames) + ") " +
                                "ORDER BY Id DESC;";

                            using (var rd = cmd.ExecuteReader())
                            {
                                while (rd.Read())
                                {
                                    if (rd["FRSAlertId"] == DBNull.Value) { continue; }

                                    int pendingId = Convert.ToInt32(rd["Id"]);
                                    int frsAlertId = Convert.ToInt32(rd["FRSAlertId"]);

                                    // Pending row linked to an FRSAlert has already been approved.
                                    // Keep the FRSAlert row and suppress this pending row from Discarded.
                                    if (pendingId > 0 && approvedPendingIds != null)
                                    {
                                        approvedPendingIds.Add(pendingId);
                                    }

                                    // 1.0.2.0: the review-stage id for this approved alert.
                                    // Query is newest-first and the newest row is the review
                                    // this alert actually came through, so the first one
                                    // seen is kept -- ContainsKey, not assignment.
                                    if (pendingId > 0 && pendingIdByFrsId != null
                                        && !pendingIdByFrsId.ContainsKey(frsAlertId))
                                    {
                                        pendingIdByFrsId[frsAlertId] = pendingId;
                                    }

                                    // Query is newest-first; keep the first non-empty ApprovalRemark.
                                    if (!result.ContainsKey(frsAlertId))
                                    {
                                        string remark = rd["ApprovalRemark"] == DBNull.Value
                                            ? ""
                                            : (Convert.ToString(rd["ApprovalRemark"]) ?? "").Trim();

                                        if (remark.Length > 0)
                                        {
                                            result[frsAlertId] = remark;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 1.0.1.7: this catch is why the blank remark was invisible for so long. A
                // wrong database, a missing grant and a timeout all produced the same empty
                // dictionary and the same silent "" on every approved card. The message now
                // travels back to LiveAlertsDiag so the board can be asked what went wrong.
                LastApprovalRemarkError = ex.GetType().Name + " " + ex.Message;
                AiLogBoard("FetchApprovalRemarks failed: " + ex.ToString());
            }

            AiLogBoard("FetchApprovalRemarks: approvedFRS=" + ids.Count
                       + " promotedPending=" + (approvedPendingIds == null ? 0 : approvedPendingIds.Count)
                       + " remarks=" + result.Count);

            return result;
        }

        private static string ReadText(object row, string[] names)
        {
            object raw = ReadProperty(row, names);
            if (raw == null)
            {
                return "";
            }
            string value = raw.ToString();
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }

        private static int ReadInt(object row, string[] names)
        {
            object raw = ReadProperty(row, names);
            if (raw == null)
            {
                return 0;
            }
            int value;
            if (int.TryParse(raw.ToString(), out value))
            {
                return value;
            }
            return 0;
        }

        // 1.0.2.9: the APPROVED side is scored from a BIT, not from verdict text. The
        // column is new, so it is read BY NAME for the same reason AuditText exists (see
        // 1.0.0.4): a direct property reference would not compile until the EDMX is
        // regenerated, and this starts working the moment any source projects it.
        private static readonly string[] AI_CONFIRMED_NAMES =
            new string[] { "AiConfirmed", "AIConfirmed", "IsAiConfirmed", "AiIsConfirmed" };

        // Returns "1", "0" or "" and nothing else. "" means NOBODY RECORDED AN ANSWER,
        // which is not the same fact as 0, and the panel has to be able to tell them
        // apart -- exactly the DiscardWasValid rule, same accepted shapes, same reason.
        // A bit that serialises as true/1/yes or false/0/no depending on the provider is
        // normalised here rather than in four places downstream.
        private static string BitText(object src, string[] names)
        {
            if (src == null) { return ""; }
            try
            {
                var jo = Newtonsoft.Json.Linq.JObject.FromObject(src);
                for (int i = 0; i < names.Length; i++)
                {
                    var tok = jo[names[i]];
                    if (tok == null || tok.Type == Newtonsoft.Json.Linq.JTokenType.Null) { continue; }
                    string v = tok.ToString().Trim();
                    if (v.Length == 0) { continue; }
                    if (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || v.Equals("yes", StringComparison.OrdinalIgnoreCase)) { return "1"; }
                    if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)
                        || v.Equals("no", StringComparison.OrdinalIgnoreCase)) { return "0"; }
                }
            }
            catch (Exception)
            {
            }
            return "";
        }

        private static string AuditText(Domain.AlertAudit audit, string[] names)
        {
            if (audit == null)
            {
                return "";
            }
            try
            {
                var jo = Newtonsoft.Json.Linq.JObject.FromObject(audit);
                for (int i = 0; i < names.Length; i++)
                {
                    var tok = jo[names[i]];
                    if (tok != null && tok.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                    {
                        string val = tok.ToString();
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            return val.Trim();
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return "";
        }

        // A rating outside 1..5 is not a rating. Returning 0 makes the card render
        // "not rated", which is the truth, rather than a star count nobody chose.
        private static int AuditRating(Domain.AlertAudit audit, string[] names)
        {
            string raw = AuditText(audit, names);
            int val;
            if (int.TryParse(raw, out val) && val >= 0 && val <= 5)
            {
                return val;
            }
            return 0;
        }

        // ------------------------------------------------------------------
        // ONE row shape for BOTH sources. A field added here appears on approved and
        // discarded rows at the same moment, which is the whole point of the merge: the
        // two pages drifted precisely because each had its own copy of this projection.
        // ------------------------------------------------------------------
        // 1.0.0.4: the last four parameters come from PendingAlertApproval and are empty
        // for acknowledged rows. They are passed IN rather than merged on afterwards
        // because this returns an anonymous type -- there is no "add a property later".
        private static object BuildRow(int id, int siteId, string siteName, string zoneName,
            string divisionName, string alertType, string assetName, string assetType,
            string causeCode, DateTime setTs, DateTime? resetTs, string remark,
            string maintainerRemarks, string decision, Domain.AlertAudit audit,
            string pendingDiscardRemark, string pendingDiscardWasValid,
                        string pendingAiVerdict, DateTime pendingDecisionAt, string aiConfirmed = "",
                        int linkedId = 0)
        {
            // ResetTimeStamp is NULLABLE, and the null case is not an edge case: it IS the
            // "still open" alert, which the Duration and Rectification columns render as a
            // dash. Read it through the nullable, never assign it straight to a DateTime.
            DateTime reset = resetTs.HasValue ? resetTs.Value : DateTime.MinValue;
            bool hasReset = resetTs.HasValue
                            && reset != DateTime.MinValue && setTs != DateTime.MinValue
                            && reset >= setTs;

            return new
            {
                Id = id,

                // ---- 1.0.2.0: BOTH OF THE ALERT'S IDS, NAMED ----
                // Id alone is ambiguous by design: it is an FRSAlert.Id on an approved row
                // and a PendingAlertApproval.Id on a discarded one, and the two sequences
                // overlap (1.0.0.4). These two say plainly which is which, and on a promoted
                // alert they give BOTH -- which is what lets the Detail panel ask for the
                // analyses made before approval as well as those made after.
                // 0 means "no id in that space": a discarded alert has no FRSAlert row, and
                // an alert that never went through review has no PendingAlertApproval row.
                FrsAlertId = (decision == "A") ? id : linkedId,
                PendingAlertApprovalId = (decision == "A") ? linkedId : id,

                SiteId = siteId,
                SiteName = siteName,
                ZoneName = zoneName ?? "",
                DivisionName = divisionName ?? "",
                AlertType = alertType,
                AssetName = assetName,
                AssetType = assetType,
                CauseCode = causeCode,
                TimeText = setTs != DateTime.MinValue ? setTs.ToString("dd/MM/yyyy HH:mm:ss") : "-",
                TimeSort = setTs != DateTime.MinValue ? setTs.ToString("yyyy-MM-ddTHH:mm:ss") : "",
                ResetText = hasReset ? reset.ToString("dd/MM/yyyy HH:mm:ss") : "",
                DurationText = BuildDurationText(setTs, resetTs),

                // The maintainer's own words -- the only GROUND TRUTH the system holds.
                Remark = remark ?? "",
                MaintainerRemarks = maintainerRemarks ?? "",

                // ---- 1.0.7.0: the review decision and the remark that belongs to it ----
                Decision = decision,

                // 1.0.0.1: whether an audit row was actually found for this alert. Without
                // it, "reviewed and rejected" and "no audit row matched" are the same "D"
                // on the wire, which is exactly how 1.0.0.0 hid a broken join.
                AuditMatched = audit != null,

                DecisionBy = AuditText(audit, new string[] { "ApprovedBy", "AuditBy", "UserName", "CreatedBy" }),

                // The audit row is the source for an approved alert; for a discarded one the
                // timestamp comes off PendingAlertApproval itself, because there is no audit
                // row on that side at all.
                //DecisionAt = !string.IsNullOrEmpty(pendingDiscardRemark) || pendingDecisionAt != DateTime.MinValue
                //    ? (pendingDecisionAt != DateTime.MinValue
                //        ? pendingDecisionAt.ToString("dd/MM/yyyy HH:mm:ss")
                //        : "")
                //    : AuditText(audit, new string[] { "AuditDate", "ApprovedDate", "ModifiedDate", "CreatedDate" }),


                // 1.0.1.2: keyed on the TIMESTAMP alone. It used to fire on the remark being
                // non-empty, which was harmless while only discarded rows carried one. Approved
                // rows now carry ApprovalRemark, so that test sent every approved row down the
                // pending branch and blanked its DecisionAt. The remark says nothing about which
                // source holds the date.
                DecisionAt = pendingDecisionAt != DateTime.MinValue
                    ? pendingDecisionAt.ToString("dd/MM/yyyy HH:mm:ss")
                    : AuditText(audit, new string[] { "AuditDate", "ApprovedDate", "ModifiedDate", "CreatedDate" }),

                RailwayRemark = AuditText(audit, new string[] { "RailwayRemark", "ApprovedRemark", "Remark" }),

                // 1.0.0.4: PendingAlertApproval.DiscardRemark first -- that is where the
                // discard reason actually lives. The AlertAudit lookup stays as a fallback
                // for any row whose reason is recorded on that side instead.
                //DiscardRemark = !string.IsNullOrEmpty(pendingDiscardRemark)
                //    ? pendingDiscardRemark
                //    : AuditText(audit, new string[] { "DiscardRemark", "RejectRemark", "Reason" }),

                DiscardRemark = !string.IsNullOrEmpty(pendingDiscardRemark)
                    ? pendingDiscardRemark
                    : AuditText(audit, new string[] { "DiscardRemark", "RejectRemark", "Reason" }),

                // 1.0.1.5: PendingAlertApproval.ApprovalRemark, in its OWN field. It used to
                // travel inside DiscardRemark because that slot already carried the
                // pending-side remark -- so the view could not tell an approval remark from a
                // discard reason, guessed with a fallback chain, and showed FRSAlert.Remark
                // whenever the guess came up empty. Empty on a discarded row by construction:
                // a row that was never approved has no approval remark.
                ApprovalRemark = decision == "A" ? (pendingDiscardRemark ?? "") : "",

                // "was the discarded alert genuine after all" -- the field's own judgement,
                // not mine. Passed through as text; the view shows it beside the remark.
                DiscardWasValid = pendingDiscardWasValid ?? "",

                // The AI's own conclusion on this alert (CONFIRMED / etc). NOT the star
                // rating -- that is a HUMAN rating OF the AI and still has no store.
                AiVerdict = pendingAiVerdict ?? "",

                // 1.0.2.9: "1" / "0" / "". APPROVED rows are scored from THIS and never
                // from AiVerdict; discarded rows are scored from AiVerdict and never from
                // this. Empty on the discarded side by construction, and empty on an
                // approved row means no source recorded the bit -- which the panel counts
                // as NOT ANALYSED, not as "not confirmed".
                AiConfirmed = aiConfirmed ?? "",

                // Read-only until a rating store exists. Absent fields give 0 and "", so the
                // card shows "not rated" instead of inventing a score.
                AiRating = AuditRating(audit, new string[] { "AiRating", "AnalysisRating", "Rating" }),
                AiRemark = AuditText(audit, new string[] { "AiRemark", "AnalysisRemark", "AiFeedback" })
            };
        }

        // 1.0.6.3: current values for one asset, on demand. The AI popup's snapshot is taken at
        // ANALYSIS time; on an alert two days old that reading is two days stale and the reader had
        // no way to refresh it. Mirrors GetFRSAlertById's HttpClientFactory pattern -- same auth,
        // same error shape.
        [HttpPost]
        public JsonResult LiveValues(int assetId, int siteId, int alertId = 0, string runId = null)
        {
            var outRows = new List<object>();
            string stamp = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            try
            {
                // 1.0.6.5: LiveValue lives on the EdgeX DataAPI, NOT the FRS API. 1.0.6.4 mirrored
                // GetFRSAlertById's HttpClientFactory -- which carries the FRS base
                // (APIBaseUrl, ...:90/api/) -- so the request went to a host that has no LiveValue
                // route at all. It 404'd, the catch swallowed it, and the card reported
                // "No readings returned" on every click. Copying a pattern copied its BASE URL too.
                string apiBase = (ConfigurationManager.AppSettings["DataApiBaseUrl"] ?? "").Trim();
                if (apiBase.Length == 0)
                { apiBase = (ConfigurationManager.AppSettings["ProxyBaseUrl"] ?? "").Trim(); }
                if (apiBase.Length == 0)
                { AiLogLive(runId, alertId, "LiveValues: no DataApiBaseUrl/ProxyBaseUrl configured"); return Json(new { retrievedAt = stamp, values = outRows, error = "not configured" }); }
                if (!apiBase.EndsWith("/")) { apiBase += "/"; }
                string url = apiBase + "api/LiveValue/" + assetId.ToString();
                {
                    string jsonString = "";
                    HttpStatusCode code = HttpStatusCode.OK;
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                    req.Method = "GET";
                    req.Timeout = 8000;
                    req.Accept = "application/json";
                    // the DataAPI is https with an internal certificate on some hosts
                    System.Net.ServicePointManager.SecurityProtocol =
                        System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
                    using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                    {
                        code = resp.StatusCode;
                        using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                        { jsonString = sr.ReadToEnd(); }
                    }
                    if (code == HttpStatusCode.OK && !string.IsNullOrEmpty(jsonString))
                    {
                        var arr = JsonConvert.DeserializeObject<List<Newtonsoft.Json.Linq.JObject>>(jsonString);
                        if (arr != null)
                        {
                            for (int i = 0; i < arr.Count && outRows.Count < 16; i++)
                            {
                                var r = arr[i];
                                if (r == null) { continue; }
                                // 1.0.6.4: LiveValue returns Value / AssetAttributeId / TagUnit /
                                // TimestampDevice. It does NOT return CurrentValue,
                                // AssetAttributeName, MinValue or MaxValue -- parsing those made
                                // every row fail and the grid come back empty on every click.
                                double val;
                                string raw = r["Value"] != null ? r["Value"].ToString() : "";
                                if (!double.TryParse(raw, System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out val)) { continue; }
                                double mn = 0, mx = 0; bool hasMn = false, hasMx = false;
                                if (r["MinValue"] != null && double.TryParse(r["MinValue"].ToString(),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out mn)) { hasMn = true; }
                                if (r["MaxValue"] != null && double.TryParse(r["MaxValue"].ToString(),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out mx)) { hasMx = true; }
                                string state = "";
                                if (hasMn || hasMx)
                                {
                                    bool below = hasMn && val < mn, above = hasMx && val > mx;
                                    state = below ? "low" : (above ? "high" : "ok");
                                }
                                string at = "";
                                if (r["TimestampDevice"] != null)
                                {
                                    DateTime td;
                                    if (DateTime.TryParse(r["TimestampDevice"].ToString(),
                                            System.Globalization.CultureInfo.InvariantCulture,
                                            System.Globalization.DateTimeStyles.None, out td))
                                    { at = td.ToString("dd/MM HH:mm"); }
                                }
                                // Safe range is NOT in this response; the client already holds it
                                // from the analysis snapshot and merges it by attribute id.
                                outRows.Add(new
                                {
                                    attrId = r["AssetAttributeId"] != null ? r["AssetAttributeId"].ToString() : "",
                                    attr = r["AssetAttributeName"] != null
                                             ? r["AssetAttributeName"].ToString().Trim() : "",
                                    unit = r["TagUnit"] != null ? r["TagUnit"].ToString().Trim() : "",
                                    value = Math.Round(val, 3),
                                    minSafe = hasMn ? (object)mn : null,
                                    maxSafe = hasMx ? (object)mx : null,
                                    state = state,
                                    at = at
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 1.0.6.5: SAY SO. The 1.0.6.4 catch discarded the reason, so a 404 from the wrong
                // host looked identical to an asset with no tags -- which is why the real fault took
                // two rounds to find.
                AiLogLive(runId, alertId, "LiveValues failed for asset " + assetId + ": " + ex.GetType().Name + " " + ex.Message);
                outRows = new List<object>();
                return Json(new { retrievedAt = stamp, values = outRows, error = "fetch failed" });
            }
            AiLogLive(runId, alertId, "LiveValues asset " + assetId + " returned " + outRows.Count + " readings");
            return Json(new { retrievedAt = stamp, values = outRows });
        }
        // ==================================================================
        // v1.0.0.6: AI-ANALYSIS RATING via the AIRating proxy API.
        //
        //   POST {ProxyBaseUrl}/api/AIRating/Upsert
        //   { AnalysisId, UserName, UserRole, Rating, Comment }
        //
        // AnalysisId is NOT the alert id. It is the analysis-run id, and it is resolved
        // from dbo.FRSAlert_AIAnalysis -- by FRSAlertId for an approved card, by
        // PendingAlertApprovalId for a discarded one. Two id spaces, one lookup each, the
        // same split this board applies everywhere else.
        //
        // CONSEQUENCE WORTH KNOWING: an alert with no row in that table has no AnalysisId,
        // so it CANNOT be rated -- there is no analysis run to attach the rating to. That
        // is not a bug, it is what "rate this analysis" means. The board says so rather
        // than failing at the API.
        // ==================================================================

        private const string _ratingLookupSql =
            "SELECT TOP 1 AnalysisId FROM dbo.FRSAlert_AIAnalysis " +
            "WHERE (@FRSAlertId > 0 AND FRSAlertId = @FRSAlertId) " +
            "   OR (@PendingId > 0 AND PendingAlertApprovalId = @PendingId) " +
            "ORDER BY Id DESC;";

        // ==================================================================
        // 1.0.2.0: THE LINK BETWEEN THE TWO ID SPACES.
        //
        // dbo.PendingAlertApproval is the ONLY place an alert's review-stage id and its
        // promoted FRSAlert id are recorded together: the row's own Id is the first, its
        // FRSAlertId column is the second, and that column is written only when the alert
        // was approved and promoted.
        //
        // WHY THE CALLER MUST SAY WHICH SPACE ITS ID IS IN. The two sequences are unrelated
        // and they overlap: 3010 is a valid PendingAlertApproval.Id AND a valid FRSAlert.Id,
        // and those are two different alerts. A lookup written as "Id = @x OR FRSAlertId =
        // @x" therefore returns a link that may belong to a completely unrelated alert, and
        // the caller has no way to tell which. That is exactly the fault 1.0.0.4 fixed on
        // the board, and it is not being reintroduced here to save a parameter. `pending`
        // is not a convenience flag -- it is what makes the answer meaningful.
        //
        // Returns an EMPTY list both when there is no link and when SQL could not be asked.
        // Callers cannot tell those apart from the list alone, which is why every one of
        // them treats an empty result as "no extra id to try" and never as proof of
        // anything: the primary id is still queried, so the worst case is what this board
        // already did before 1.0.2.0.
        // ==================================================================
        private const string _linkFromPendingSql =
            "SELECT TOP 5 FRSAlertId FROM dbo.PendingAlertApproval " +
            "WHERE Id = @Id AND FRSAlertId IS NOT NULL AND FRSAlertId > 0 " +
            "ORDER BY Id DESC;";

        // Newest pending row first: a re-review writes another row against the same
        // FRSAlert, and the most recent one is the review this alert actually came through.
        private const string _linkFromFrsSql =
            "SELECT TOP 5 Id FROM dbo.PendingAlertApproval " +
            "WHERE FRSAlertId = @Id ORDER BY Id DESC;";

        private static List<int> LinkedAlertIds(int alertId, bool pending, out string error)
        {
            error = "";
            var outIds = new List<int>();
            if (alertId <= 0) { return outIds; }

            var cs = System.Configuration.ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"];
            if (cs == null || string.IsNullOrEmpty(cs.ConnectionString))
            {
                error = "E7MRIV2DB_SQL connection string is missing.";
                return outIds;
            }
            try
            {
                using (var cn = new System.Data.SqlClient.SqlConnection(cs.ConnectionString))
                using (var cmd = new System.Data.SqlClient.SqlCommand(
                           pending ? _linkFromPendingSql : _linkFromFrsSql, cn))
                {
                    // Short. This runs on a deliberate click (Detail) or once per write, and
                    // a link that cannot be read must degrade to "no link" rather than hold
                    // the panel open.
                    cmd.CommandTimeout = 8;
                    // INT on both columns. AiChatController bound a BigInt against the INT
                    // PendingAlertApprovalId and its MERGE silently did nothing; not repeated.
                    cmd.Parameters.Add("@Id", System.Data.SqlDbType.Int).Value = alertId;
                    cn.Open();
                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            if (rd.IsDBNull(0)) { continue; }
                            int v = Convert.ToInt32(rd.GetValue(0));
                            // A self-link would make the merge ask the same endpoint twice
                            // for the same id; harmless but pointless, and it would double
                            // every run in the de-dup input.
                            if (v > 0 && v != alertId && !outIds.Contains(v)) { outIds.Add(v); }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            return outIds;
        }

        // The bulk form, for the board-load gate. One query for the whole window instead of
        // one per alert -- AlertRatings is already the expensive call on a wide window and
        // N more round trips there is how 1.0.1.0 got slow in the first place.
        private static Dictionary<int, List<int>> LinkedPendingIdsByFrsIds(List<int> frsIds, out string error)
        {
            error = "";
            var map = new Dictionary<int, List<int>>();
            if (frsIds == null || frsIds.Count == 0) { return map; }

            var ids = frsIds.Where(x => x > 0).Distinct().ToList();
            if (ids.Count == 0) { return map; }

            var cs = System.Configuration.ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"];
            if (cs == null || string.IsNullOrEmpty(cs.ConnectionString))
            {
                error = "E7MRIV2DB_SQL connection string is missing.";
                return map;
            }
            try
            {
                using (var cn = new System.Data.SqlClient.SqlConnection(cs.ConnectionString))
                {
                    cn.Open();
                    const int batchSize = 500;
                    for (int start = 0; start < ids.Count; start += batchSize)
                    {
                        var batch = ids.Skip(start).Take(batchSize).ToList();
                        if (batch.Count == 0) { continue; }

                        using (var cmd = cn.CreateCommand())
                        {
                            cmd.CommandTimeout = 10;
                            var names = new List<string>();
                            for (int i = 0; i < batch.Count; i++)
                            {
                                string n = "@F" + i;
                                names.Add(n);
                                cmd.Parameters.Add(n, System.Data.SqlDbType.Int).Value = batch[i];
                            }
                            cmd.CommandText =
                                "SELECT Id, FRSAlertId FROM dbo.PendingAlertApproval " +
                                "WHERE FRSAlertId IN (" + string.Join(",", names) + ") " +
                                "ORDER BY Id DESC;";

                            using (var rd = cmd.ExecuteReader())
                            {
                                while (rd.Read())
                                {
                                    if (rd.IsDBNull(0) || rd.IsDBNull(1)) { continue; }
                                    int pendingId = Convert.ToInt32(rd.GetValue(0));
                                    int frsId = Convert.ToInt32(rd.GetValue(1));
                                    if (pendingId <= 0 || frsId <= 0) { continue; }
                                    if (!map.ContainsKey(frsId)) { map[frsId] = new List<int>(); }
                                    if (!map[frsId].Contains(pendingId)) { map[frsId].Add(pendingId); }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + " " + ex.Message;
                AiLogBoard("LinkedPendingIdsByFrsIds failed: " + ex.Message);
            }
            return map;
        }

        // The LATEST analysis is the one on screen, so AnalysisId (overwritten per re-run)
        // is the right column -- not FirstAnalysisId, which anchors the original.
        //
        // 1.0.2.0: BOTH COLUMNS, FOR THE SAME ALERT. A run made while the alert was in
        // review wrote PendingAlertApprovalId and left FRSAlertId null; after approval the
        // board asks by FRSAlertId and found nothing, so the rating chip and the validity
        // toggle reported "not analysed" on an alert whose analysis the Detail panel could
        // show. The linked id is filled into the other parameter, and ORDER BY Id DESC
        // still picks the most recent run across both stages -- which is the one on screen.
        private static long ResolveAnalysisId(int alertId, bool pending, out string error)
        {
            error = "";
            var cs = System.Configuration.ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"];
            if (cs == null || string.IsNullOrEmpty(cs.ConnectionString))
            {
                error = "E7MRIV2DB_SQL connection string is missing.";
                return 0;
            }

            // Read on the SAME connection string this method already needs, so a host that
            // cannot reach SQL fails once here rather than twice. The error is deliberately
            // NOT propagated: no link simply means the lookup stays as it was before 1.0.2.0.
            string linkErr;
            var linked = LinkedAlertIds(alertId, pending, out linkErr);
            int linkedId = linked.Count > 0 ? linked[0] : 0;

            try
            {
                using (var cn = new System.Data.SqlClient.SqlConnection(cs.ConnectionString))
                using (var cmd = new System.Data.SqlClient.SqlCommand(_ratingLookupSql, cn))
                {
                    cmd.CommandTimeout = 8;
                    // Both columns are INT. AiChatController bound a BigInt against the INT
                    // PendingAlertApprovalId and its MERGE silently did nothing; not repeated.
                    cmd.Parameters.Add("@FRSAlertId", System.Data.SqlDbType.Int).Value = pending ? linkedId : alertId;
                    cmd.Parameters.Add("@PendingId", System.Data.SqlDbType.Int).Value = pending ? alertId : linkedId;
                    cn.Open();
                    object o = cmd.ExecuteScalar();
                    if (o == null || o == DBNull.Value) { return 0; }
                    long v = 0;
                    long.TryParse(Convert.ToString(o), out v);
                    return v;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return 0;
            }
        }

        // ------------------------------------------------------------------
        // POST /FRS25/UnifiedAlertAnalysis/SaveAiRating
        //   { id, source: "pending" | "frs", rating: 1..5, remark }
        // ------------------------------------------------------------------
        [HttpPost]
        public JsonResult SaveAiRating(int id, string source, int rating, string remark, long analysisId = 0)
        {
            if (id <= 0) { return Json(new { ok = false, error = "No alert id." }); }
            if (rating < 1 || rating > 5)
            { return Json(new { ok = false, error = "Rating must be between 1 and 5." }); }

            bool pending = !string.Equals((source ?? "").Trim(), "frs", StringComparison.OrdinalIgnoreCase);

            // 1.0.0.7: rating a discarded alert means rating something you may not see.
            // Refused with the SAME wording an unanalysed alert gets, so the response does
            // not reveal whether the row exists.
            if (pending && !AaPrivileged())
            {
                AiLogBoard("SaveAiRating refused: pending " + id + ", caller is not sitekeeping");
                return Json(new { ok = false, notAnalysed = true, error = "This alert has no stored analysis yet." });
            }

            // 1.0.1.0: THE RUN ON SCREEN, not the latest run for the alert.
            // The browser has the exact AnalysisId from the analysis_id SSE event -- that IS
            // the analysis being judged. ResolveAnalysisId returns FRSAlert_AIAnalysis's
            // current AnalysisId, which is the LATEST run and only coincidentally the same
            // one: re-run Insight, or have someone else re-run it while the popup is open,
            // and the rating lands on a run the operator never saw.
            //
            // The client id is VALIDATED rather than trusted -- confirmed to belong to this
            // alert before use. Ignoring it (which is what 1.0.0.6 did) traded a real
            // correctness bug for a spoofing risk that one query closes.
            string lookupErr;
            long ownId = ResolveAnalysisId(id, pending, out lookupErr);

            if (analysisId > 0 && analysisId != ownId)
            {
                if (!AnalysisBelongsTo(analysisId, id, pending))
                {
                    AiLogBoard("SaveAiRating refused: analysisId " + analysisId
                               + " does not belong to " + (pending ? "pending " : "frs ") + id);
                    return Json(new { ok = false, error = "That analysis does not belong to this alert." });
                }
            }
            if (analysisId <= 0) { analysisId = ownId; }

            // 1.0.1.0: A RATING IS NOW EDITABLE.
            // 1.0.0.9 refused any second save outright ("A rating cannot be changed"), which
            // meant a mis-click was permanent and a reviewer who learned more later had no
            // way to correct the record. The endpoint's own verb is Upsert, so revising is
            // what it was built to do; the restriction was ours, and it is lifted here.
            //
            // The PREVIOUS values are read first and returned to the caller as previousRating
            // / previousRemark, so the UI can show what the rating was before this edit
            // rather than silently replacing history with the newest answer.
            int prevRating = 0;
            string prevRemark = "";
            string prevBy = "";
            string prevAt = "";
            if (analysisId > 0)
            {
                var existing = FetchRating(analysisId);
                if (existing != null)
                {
                    var et = existing.GetType();
                    try { prevRating = Convert.ToInt32(et.GetProperty("rating").GetValue(existing, null)); }
                    catch (Exception) { }
                    try { prevRemark = Convert.ToString(et.GetProperty("remark").GetValue(existing, null)) ?? ""; }
                    catch (Exception) { }
                    try { prevBy = Convert.ToString(et.GetProperty("by").GetValue(existing, null)) ?? ""; }
                    catch (Exception) { }
                    try { prevAt = Convert.ToString(et.GetProperty("at").GetValue(existing, null)) ?? ""; }
                    catch (Exception) { }
                    if (prevRating > 0)
                    {
                        AiLogBoard("SaveAiRating: analysis " + analysisId + " revised from "
                                   + prevRating + " to " + rating);
                    }
                }
            }
            if (analysisId <= 0)
            {
                // Two different situations, told apart -- "the lookup broke" and "this alert
                // has never been analysed" both used to look like a failed save.
                if (lookupErr.Length > 0)
                {
                    AiLogBoard("SaveAiRating: AnalysisId lookup failed for "
                               + (pending ? "pending " : "frs ") + id + ": " + lookupErr);
                    return Json(new { ok = false, error = "Could not look up the analysis: " + lookupErr });
                }
                AiLogBoard("SaveAiRating: no FRSAlert_AIAnalysis row for "
                           + (pending ? "pending " : "frs ") + id);
                return Json(new
                {
                    ok = false,
                    notAnalysed = true,
                    error = "This alert has no stored analysis yet. Run Insight first, then rate it."
                });
            }

            string comment = (remark ?? "").Trim();
            // 1.0.1.0: the remark box now accepts 2000 characters (was 1000). Enforced here
            // as well as by the textarea's maxlength, because maxlength is a browser courtesy
            // -- it constrains typing, not what arrives on the wire.
            const int MaxRemarkChars = 2000;
            if (comment.Length > MaxRemarkChars) { comment = comment.Substring(0, MaxRemarkChars); }

            string userName = "";
            string userRole = "CLIENT";
            try
            {
                var lu = ClsHttpContent.LoginUser;
                if (lu != null)
                {
                    userName = ((lu.FirstName ?? "") + " " + (lu.LastName ?? "")).Trim();
                    if (lu.IsSiteKeeping) { userRole = "SITEKEEPING"; }
                }
            }
            catch (Exception)
            {
            }
            if (userName.Length == 0) { userName = "operator"; }

            string apiBase = (ConfigurationManager.AppSettings["ProxyBaseUrl"] ?? "").Trim();
            if (apiBase.Length == 0)
            { apiBase = (ConfigurationManager.AppSettings["DataApiBaseUrl"] ?? "").Trim(); }
            if (apiBase.Length == 0)
            {
                AiLogBoard("SaveAiRating: no ProxyBaseUrl/DataApiBaseUrl configured");
                return Json(new { ok = false, error = "Rating API is not configured on this server." });
            }
            if (!apiBase.EndsWith("/")) { apiBase += "/"; }
            string url = apiBase + "api/AIRating/Upsert";

            try
            {
                var body = new Newtonsoft.Json.Linq.JObject();
                body["AnalysisId"] = analysisId;
                body["UserName"] = userName;
                body["UserRole"] = userRole;
                body["Rating"] = rating;
                // The API takes null for "no comment". An empty string would store a remark
                // that reads as blank rather than absent.
                body["Comment"] = (comment.Length == 0)
                    ? (Newtonsoft.Json.Linq.JToken)Newtonsoft.Json.Linq.JValue.CreateNull()
                    : comment;
                byte[] payload = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));

                // HttpWebRequest, matching LiveValues. Deliberately NOT HttpClient: the
                // 1.0.3.0 note in this file records that the System.Net.Http
                // reference/binding-redirect can stop the app pool at startup.
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Accept = "application/json";
                req.Timeout = 8000;
                req.ContentLength = payload.Length;
                using (var rs = req.GetRequestStream()) { rs.Write(payload, 0, payload.Length); }

                string respText = "";
                HttpStatusCode code;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                {
                    code = resp.StatusCode;
                    using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                    { respText = sr.ReadToEnd(); }
                }

                if (code != HttpStatusCode.OK && code != HttpStatusCode.Created)
                {
                    AiLogBoard("SaveAiRating: AIRating/Upsert -> " + (int)code + " " + respText);
                    return Json(new { ok = false, error = "Rating API returned " + (int)code + "." });
                }

                AiLogBoard("rating saved: " + (pending ? "pending " : "frs ") + id
                           + " analysisId=" + analysisId + " rating=" + rating
                           + (prevRating > 0 ? (" (was " + prevRating + ")") : "")
                           + " commentChars=" + comment.Length + " by=" + userName + "/" + userRole);
                return Json(new
                {
                    ok = true,
                    rating = rating,
                    remark = comment,
                    ratedBy = userName,
                    analysisId = analysisId,
                    // 1.0.1.0: what it was BEFORE this save. The UI shows the latest rating
                    // and remark as the live values, and uses these to say what they replaced
                    // -- an edit that leaves no trace of the previous judgement is how a
                    // review record stops being a record.
                    revised = (prevRating > 0),
                    previousRating = prevRating,
                    previousRemark = prevRemark,
                    previousBy = prevBy,
                    previousAt = prevAt
                });
            }
            catch (System.Net.WebException wex)
            {
                // Read the body. A 400 from this API carries the reason, and swallowing it is
                // how "save failed" became unanswerable in earlier rounds.
                string detail = wex.Message;
                try
                {
                    if (wex.Response != null)
                    {
                        using (var sr = new System.IO.StreamReader(wex.Response.GetResponseStream()))
                        { detail = sr.ReadToEnd(); }
                    }
                }
                catch (Exception) { }
                AiLogBoard("SaveAiRating failed for " + id + " (analysisId " + analysisId + "): " + detail);
                return Json(new { ok = false, error = "Could not save: " + detail });
            }
            catch (Exception ex)
            {
                AiLogBoard("SaveAiRating failed for " + id + ": " + ex.ToString());
                return Json(new { ok = false, error = "Could not save: " + ex.Message });
            }
        }

        // ------------------------------------------------------------------
        // POST /FRS25/UnifiedAlertAnalysis/AlertRatings
        //   { frsIds: [...], pendingIds: [...] }
        //
        // Returns which alerts HAVE an analysis, keyed "F:<id>" / "P:<id>" because the two
        // id spaces overlap, and for each one the two human judgements recorded against
        // that run:
        //   ratings     -- the star rating of the AI ANALYSIS      (api/AIRating/Get)
        //   assessments -- the REVIEWER mark on the ALERT itself   (api/AIAssessment/Get)
        // 1.0.0.8: `assessments` is new and shares this action deliberately. Both answers
        // hang off the same AnalysisId, which is resolved here from one SQL pass; a second
        // endpoint would resolve it again and leave a window where the board shows a rating
        // without the validity mark that belongs beside it.
        // ------------------------------------------------------------------
        [HttpPost]
        public JsonResult AlertRatings(List<int> frsIds, List<int> pendingIds)
        {
            var map = new Dictionary<string, object>();
            // 1.0.0.8: the reviewer's validity mark, keyed the same "F:"/"P:" way and
            // returned beside the ratings rather than behind a second board-load request.
            // Both come from the same AnalysisId, so asking for them separately would mean
            // resolving that id twice and leaving a window where the board shows a rating
            // but not the judgement written next to it.
            var amap = new Dictionary<string, object>();
            var wanted = new List<long>();
            // 1.0.0.9: this is a WALL-CLOCK bound, not a count. A count silently drops the
            // 61st alert's rating -- the same class of fault as the 400 cap above. Time-boxing
            // degrades differently: analysisId is already in the map from SQL, so a truncated
            // run still leaves every chip ENABLED and only the star counts arrive late.
            int budget = 400;
            var ratingClock = System.Diagnostics.Stopwatch.StartNew();
            const int ratingBudgetMs = 5000;
            try
            {
                string frs = JoinIds(frsIds);
                // 1.0.0.7: a non-privileged caller never legitimately holds a pending id, so
                // ignore the list rather than confirming which of them have been analysed.
                string pend = AaPrivileged() ? JoinIds(pendingIds) : "";
                if (frs.Length == 0 && pend.Length == 0)
                { return Json(new { ok = true, ratings = map, assessments = amap }); }

                // ==========================================================
                // 1.0.2.0: AN APPROVED ALERT'S ANALYSIS MAY BE FILED UNDER ITS REVIEW ID.
                //
                // An alert analysed while it was in review has its FRSAlert_AIAnalysis row
                // keyed by PendingAlertApprovalId; once approved, the board asks about it by
                // FRSAlertId and the query below matched nothing. The gate then said "not
                // analysed" -- a dead rating chip, a dead validity toggle and, since 1.0.2.8,
                // a dead Detail button -- on the very alerts whose analysis is now on the
                // Detail panel. That is the exact contradiction 1.0.1.2 set out to end, and
                // the promotion is the case it did not cover.
                //
                // So each requested FRS id is expanded with the pending ids linked to it in
                // dbo.PendingAlertApproval, those ids join the query, and a row that matches
                // only on PendingAlertApprovalId is ALSO written to the "F:" key the board
                // looks up. One extra query for the whole window, not one per alert.
                //
                // NOT gated on AaPrivileged: these pending ids are reached only through an
                // FRS id the caller already asked about and already sees as an approved
                // alert. The answer is "this approved alert has an analysis", which is the
                // same answer the approved side always gave -- no discarded alert is
                // revealed, because a pending row that was promoted is not a discarded one.
                // ==========================================================
                var pendingByFrs = new Dictionary<int, List<int>>();
                var frsByPending = new Dictionary<int, int>();
                if (frsIds != null && frsIds.Count > 0)
                {
                    string linkErr;
                    pendingByFrs = LinkedPendingIdsByFrsIds(frsIds, out linkErr);
                    if ((linkErr ?? "").Length > 0)
                    {
                        // Not fatal, and said once. The gate then behaves exactly as it did
                        // before 1.0.2.0 rather than failing the whole board load.
                        AiLogBoard("AlertRatings: promotion link unavailable (" + linkErr
                                   + ") -- pre-approval analyses will not be found");
                    }
                    var extra = new List<int>();
                    foreach (var kv in pendingByFrs)
                    {
                        foreach (int p in kv.Value)
                        {
                            // First writer wins: if two FRSAlerts somehow claim one pending
                            // row, fanning it to both keys would credit an analysis to an
                            // alert that does not own it.
                            if (!frsByPending.ContainsKey(p)) { frsByPending[p] = kv.Key; }
                            extra.Add(p);
                        }
                    }
                    if (extra.Count > 0)
                    {
                        string extraJoined = JoinIds(extra);
                        if (extraJoined.Length > 0)
                        { pend = (pend.Length > 0) ? (pend + "," + extraJoined) : extraJoined; }
                        AiLogBoard("AlertRatings: " + pendingByFrs.Count + " of "
                                   + frsIds.Count + " approved alerts carry a review-stage id");
                    }
                }

                // ==========================================================
                // 1.0.1.2: SQL IS NOW AN OPTIMISATION, NOT THE GATE.
                //
                // The rule this has to satisfy is the one the page owner stated: if the
                // Detail panel can find an analysis for an alert, the validity toggle
                // must be enabled for that alert. Detail reads AIAnalysis/Get over the
                // proxy API. This action read dbo.FRSAlert_AIAnalysis over a direct SQL
                // connection. TWO SOURCES, and every way they can disagree showed up on
                // screen as "Detail shows an AnalysisId but the toggle is dead":
                //   - the SQL connection is unavailable from this host
                //   - the row is keyed by an id this board does not use
                //   - the analysis exists at the API but was never written to that table
                // Chasing which one it was is the wrong fix. The gate now asks the SAME
                // question Detail asks, of the SAME service, and SQL is kept only because
                // one query answers the whole board far cheaper than N HTTP calls.
                //
                // So: a SQL failure is no longer fatal here. It logs, leaves the map
                // empty, and the API fallback below fills it in.
                // ==========================================================
                var cs = System.Configuration.ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"];
                if (cs == null || string.IsNullOrEmpty(cs.ConnectionString))
                {
                    AiLogBoard("AlertRatings: E7MRIV2DB_SQL missing -- falling back to AIAnalysis/Get");
                }
                else
                {
                    try
                    {
                        // The id lists are rebuilt from parsed ints, so nothing but digits and
                        // commas reaches this statement -- no request string is concatenated in.
                        string sql =
                            "SELECT FRSAlertId, PendingAlertApprovalId, AnalysisId "
                          + "FROM dbo.FRSAlert_AIAnalysis WHERE "
                          + (frs.Length > 0 ? ("FRSAlertId IN (" + frs + ")") : "1=0")
                          + " OR "
                          + (pend.Length > 0 ? ("PendingAlertApprovalId IN (" + pend + ")") : "1=0");

                        using (var cn = new System.Data.SqlClient.SqlConnection(cs.ConnectionString))
                        using (var cmd = new System.Data.SqlClient.SqlCommand(sql, cn))
                        {
                            cmd.CommandTimeout = 10;
                            cn.Open();
                            using (var rd = cmd.ExecuteReader())
                            {
                                while (rd.Read())
                                {
                                    // 1.0.1.1: BOTH keys when the row carries both ids. An
                                    // analysis row can hold an FRSAlertId AND a
                                    // PendingAlertApprovalId -- that is a promoted pending
                                    // alert. Taking only the first branch left the map holding
                                    // "F:<frsId>" while the board looked up "P:<pendingId>".
                                    long aid = rd.IsDBNull(2) ? 0L : rd.GetInt64(2);
                                    var entry = new
                                    {
                                        analysisId = aid,
                                        rating = 0,
                                        remark = "",
                                        by = "",
                                        at = ""
                                    };
                                    if (!rd.IsDBNull(0)) { map["F:" + rd.GetInt32(0)] = entry; }
                                    if (!rd.IsDBNull(1))
                                    {
                                        int pid = rd.GetInt32(1);

                                        // 1.0.2.0: the "P:" key is for a card the board keys by
                                        // a pending id, and only a sitekeeping caller ever has
                                        // one. The ids added by the promotion expansion above
                                        // were never asked for by an unprivileged caller, and
                                        // handing them back would tell them which review record
                                        // each approved alert came from -- a fact 1.0.0.7 keeps
                                        // off this response. They still get the answer that
                                        // matters to them, under the "F:" key.
                                        if (AaPrivileged()) { map["P:" + pid] = entry; }

                                        // 1.0.2.0: a run recorded ONLY against the review id,
                                        // fanned out to the approved alert it was promoted
                                        // into. Written only if that key is still empty: a
                                        // row carrying the FRSAlertId itself is the direct
                                        // answer and must not be displaced by a linked one.
                                        int ownerFrs;
                                        if (rd.IsDBNull(0)
                                            && frsByPending.TryGetValue(pid, out ownerFrs)
                                            && !map.ContainsKey("F:" + ownerFrs))
                                        {
                                            map["F:" + ownerFrs] = entry;
                                        }
                                    }
                                    if (aid > 0) { wanted.Add(aid); }
                                }
                            }
                        }
                    }
                    catch (Exception sqlEx)
                    {
                        // NOT fatal. Say so once and let the API answer instead.
                        AiLogBoard("AlertRatings: SQL lookup failed (" + sqlEx.Message
                                   + ") -- falling back to AIAnalysis/Get");
                    }
                }

                // ---- API FALLBACK -------------------------------------------------
                // Every requested alert that SQL did not account for is asked of the same
                // endpoint the Detail panel uses. Capped and time-boxed: if SQL is down and
                // the window holds 300 alerts this would otherwise be 300 sequential HTTP
                // calls on a board load.
                //
                // 1.0.2.0: each key carries the ids WORTH ASKING ABOUT, in order, rather
                // than one id. For an approved alert that is its FRS id and then its
                // review id -- the probe stops at the first that answers. Carried as a
                // list rather than as two entries sharing a key because two entries would
                // race in the result dictionary and the winner would be whichever thread
                // finished last.
                var missing = new List<KeyValuePair<string, List<long>>>();
                if (frsIds != null)
                {
                    foreach (var id in frsIds)
                    {
                        if (id <= 0) { continue; }
                        if (map.ContainsKey("F:" + id)) { continue; }
                        var probeIds = new List<long> { id };
                        List<int> linkedPending;
                        if (pendingByFrs.TryGetValue(id, out linkedPending))
                        {
                            foreach (int p in linkedPending)
                            { if (p > 0 && !probeIds.Contains(p)) { probeIds.Add(p); } }
                        }
                        missing.Add(new KeyValuePair<string, List<long>>("F:" + id, probeIds));
                    }
                }
                if (AaPrivileged() && pendingIds != null)
                {
                    foreach (var id in pendingIds)
                    {
                        if (id <= 0) { continue; }
                        if (!map.ContainsKey("P:" + id))
                        { missing.Add(new KeyValuePair<string, List<long>>("P:" + id, new List<long> { id })); }
                    }
                }
                if (missing.Count > 0)
                {
                    if (missing.Count > AnalysisProbeCap) { missing = missing.GetRange(0, AnalysisProbeCap); }
                    var probed = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();
                    var probeClock = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        System.Threading.Tasks.Parallel.ForEach(
                            missing,
                            new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 8 },
                            pair =>
                            {
                                if (probeClock.ElapsedMilliseconds > AnalysisProbeBudgetMs) { return; }
                                // First id that answers wins, and the alert's own id is
                                // first in the list -- a run made after approval is the
                                // current one and must not be displaced by a pre-approval
                                // run just because the linked id was asked as well.
                                foreach (long probeId in pair.Value)
                                {
                                    if (probeClock.ElapsedMilliseconds > AnalysisProbeBudgetMs) { return; }
                                    long aid = LatestAnalysisIdForAlert(probeId);
                                    if (aid > 0) { probed[pair.Key] = aid; break; }
                                }
                            });
                    }
                    catch (Exception ex)
                    {
                        AiLogBoard("AlertRatings: analysis probe failed: " + ex.Message);
                    }
                    foreach (var kv in probed)
                    {
                        map[kv.Key] = new
                        {
                            analysisId = kv.Value,
                            rating = 0,
                            remark = "",
                            by = "",
                            at = ""
                        };
                        wanted.Add(kv.Value);
                    }
                    if (probed.Count > 0)
                    {
                        AiLogBoard("AlertRatings: AIAnalysis/Get recovered " + probed.Count
                                   + " of " + missing.Count + " alerts SQL did not account for");
                    }
                }

                // 1.0.0.8: now fetch what people actually RATED. AIRating/Get takes one
                // AnalysisId at a time, so this is one call per analysed alert -- acceptable
                // because only analysed alerts are in the list, and capped so a wide window
                // cannot turn a board load into two hundred sequential HTTP calls.
                // 1.0.1.0: AIRating/Get still takes one AnalysisId at a time, but the calls
                // no longer wait in single file. Sequentially, a 200-alert board spent
                // 200 x latency here and then hit the budget and returned the REMAINDER
                // UNRATED -- stars silently missing on a wide window, which reads as "nobody
                // rated these" rather than "we gave up asking". Run 8 at a time the same
                // work finishes inside the budget instead of being cut off by it.
                // FetchRating uses HttpWebRequest and touches no HttpContext, so it is safe
                // off the request thread; the budget stays as a backstop.
                // 1.0.1.1: one entry per DISTINCT AnalysisId, not per key. Dual-keying a
                // promoted alert (see above) puts the same analysis under both "F:" and
                // "P:", and keying the fetch list by map-key would then ask AIRating and
                // AIAssessment for that same run TWICE per board load. keysById keeps the
                // one result pointed at every key that shares the id.
                var keysById = new Dictionary<long, List<string>>();
                foreach (var key in new List<string>(map.Keys))
                {
                    long aid = 0;
                    var cur = map[key];
                    try { aid = Convert.ToInt64(cur.GetType().GetProperty("analysisId").GetValue(cur, null)); }
                    catch (Exception) { }
                    if (aid <= 0) { continue; }
                    if (!keysById.ContainsKey(aid)) { keysById[aid] = new List<string>(); }
                    keysById[aid].Add(key);
                }
                var toFetch = new List<KeyValuePair<string, long>>();
                foreach (var kb in keysById)
                { toFetch.Add(new KeyValuePair<string, long>(kb.Value[0], kb.Key)); }
                if (toFetch.Count > budget) { toFetch = toFetch.GetRange(0, budget); }

                var fetched = new System.Collections.Concurrent.ConcurrentDictionary<string, object>();
                // 1.0.0.8: the validity marks, gathered in the SAME pass. A second
                // Parallel.ForEach over the same list would double the wall-clock cost of a
                // board load for no benefit -- each worker already holds the AnalysisId.
                var assessed = new System.Collections.Concurrent.ConcurrentDictionary<string, object>();
                try
                {
                    System.Threading.Tasks.Parallel.ForEach(
                        toFetch,
                        new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 8 },
                        pair =>
                        {
                            // The clock is read, never written, so no lock is needed -- a
                            // worker that starts late simply does not start at all.
                            if (ratingClock.ElapsedMilliseconds > ratingBudgetMs) { return; }
                            var got = FetchRating(pair.Value);
                            if (got != null) { fetched[pair.Key] = got; }
                            // Checked again between the two calls. The rating is the more
                            // expensive answer to lose, so it goes first and the validity
                            // read is what the budget drops if the API is slow -- a toggle
                            // that loads as unset is recoverable, a missing star is not
                            // visibly different from an unrated analysis.
                            if (ratingClock.ElapsedMilliseconds > ratingBudgetMs) { return; }
                            var asm = FetchAssessment(pair.Value);
                            if (asm != null) { assessed[pair.Key] = asm; }
                        });
                }
                catch (Exception ex)
                {
                    AiLogBoard("AlertRatings: parallel rating fetch failed: " + ex.Message);
                }

                // 1.0.1.1: fan each result out to EVERY key that shares its AnalysisId.
                // The fetch list holds one key per id, so writing back only that key would
                // leave a dual-keyed alert with the rating on "F:" and nothing on "P:" --
                // the same half-populated map the dual-key fix above exists to prevent.
                foreach (var kv in fetched)
                {
                    long aid = 0;
                    try { aid = Convert.ToInt64(kv.Value.GetType().GetProperty("analysisId").GetValue(kv.Value, null)); }
                    catch (Exception) { }
                    List<string> keys;
                    if (aid > 0 && keysById.TryGetValue(aid, out keys))
                    { foreach (var k in keys) { map[k] = kv.Value; } }
                    else { map[kv.Key] = kv.Value; }
                }
                foreach (var kv in assessed)
                {
                    long aid = 0;
                    try { aid = Convert.ToInt64(kv.Value.GetType().GetProperty("analysisId").GetValue(kv.Value, null)); }
                    catch (Exception) { }
                    List<string> keys;
                    if (aid > 0 && keysById.TryGetValue(aid, out keys))
                    { foreach (var k in keys) { amap[k] = kv.Value; } }
                    else { amap[kv.Key] = kv.Value; }
                }

                // 1.0.0.9: ids to names, HERE and not in the worker above. The user lookup
                // goes through HttpClientFactory, which reads HttpContext.Current and the
                // session token -- neither exists on a Parallel.ForEach thread, so doing it
                // inside the fetch would throw on every mark. One lookup per DISTINCT
                // reviewer, cached for half an hour.
                ResolveMarkNames(amap);

                if (fetched.Count < toFetch.Count)
                {
                    AiLogBoard("AlertRatings: " + fetched.Count + " of " + toFetch.Count
                               + " ratings fetched; the rest returned analysis ids only");
                }

                return Json(new { ok = true, ratings = map, assessments = amap });
            }
            catch (Exception ex)
            {
                AiLogBoard("AlertRatings failed: " + ex.Message);
                return Json(new { ok = false, ratings = map, assessments = amap, error = ex.Message });
            }
        }

        // Does this analysis id belong to this alert? Checks both FirstAnalysisId and
        // AnalysisId, because a re-run leaves the original in the first column and the
        // operator may still be looking at it.
        //
        // 1.0.2.0: and both ID COLUMNS, via the promotion link. The Detail panel now offers
        // the pre-approval runs of a promoted alert, so the browser can legitimately send
        // back an analysisId recorded against PendingAlertApprovalId while the card's own id
        // is an FRSAlertId. Without the link that is indistinguishable from a forged id and
        // the write is refused -- the operator would see the run on screen and be told it
        // belongs to a different alert.
        private static bool AnalysisBelongsTo(long analysisId, int alertId, bool pending)
        {
            var cs = System.Configuration.ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"];
            if (cs == null || string.IsNullOrEmpty(cs.ConnectionString)) { return false; }
            string linkErr;
            var linkedIds = LinkedAlertIds(alertId, pending, out linkErr);
            int linked = linkedIds.Count > 0 ? linkedIds[0] : 0;
            const string sql =
                "SELECT COUNT(1) FROM dbo.FRSAlert_AIAnalysis " +
                "WHERE ((@FRSAlertId > 0 AND FRSAlertId = @FRSAlertId) " +
                "    OR (@PendingId > 0 AND PendingAlertApprovalId = @PendingId)) " +
                "  AND (AnalysisId = @AnalysisId OR FirstAnalysisId = @AnalysisId);";
            try
            {
                using (var cn = new System.Data.SqlClient.SqlConnection(cs.ConnectionString))
                using (var cmd = new System.Data.SqlClient.SqlCommand(sql, cn))
                {
                    cmd.CommandTimeout = 8;
                    cmd.Parameters.Add("@FRSAlertId", System.Data.SqlDbType.Int).Value = pending ? linked : alertId;
                    cmd.Parameters.Add("@PendingId", System.Data.SqlDbType.Int).Value = pending ? alertId : linked;
                    cmd.Parameters.Add("@AnalysisId", System.Data.SqlDbType.BigInt).Value = analysisId;
                    cn.Open();
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------
        // GET {ProxyBaseUrl}/api/AIRating/Get?AnalysisId=1292
        // Returns an ARRAY -- one row per rater, newest last in the sample given. The most
        // recently updated is taken as the current rating, because Upsert overwrites per
        // user and the board shows one number.
        // Returns null on any failure, so a rating that cannot be read leaves the chip
        // showing the analysis exists and unrated, rather than blanking it.
        // ------------------------------------------------------------------
        private static object FetchRating(long analysisId)
        {
            try
            {
                string apiBase = (ConfigurationManager.AppSettings["ProxyBaseUrl"] ?? "").Trim();
                if (apiBase.Length == 0)
                { apiBase = (ConfigurationManager.AppSettings["DataApiBaseUrl"] ?? "").Trim(); }
                if (apiBase.Length == 0) { return null; }
                if (!apiBase.EndsWith("/")) { apiBase += "/"; }

                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(
                    apiBase + "api/AIRating/Get?AnalysisId=" + analysisId);
                req.Method = "GET";
                req.Accept = "application/json";
                // Short on purpose. This runs once per analysed alert; a slow API must degrade
                // to "unrated" rather than hold the whole board load open.
                req.Timeout = 1500;

                string body;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK) { return null; }
                    using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                    { body = sr.ReadToEnd(); }
                }
                if (string.IsNullOrWhiteSpace(body)) { return null; }

                var arr = Newtonsoft.Json.Linq.JArray.Parse(body);
                if (arr.Count == 0)
                {
                    return new { analysisId = analysisId, rating = 0, remark = "", by = "", at = "" };
                }

                // Newest by UpdatedAtUtc, falling back to RatedAtUtc. Not simply the last
                // element: array order is the API's choice, not a contract.
                Newtonsoft.Json.Linq.JToken best = null;
                DateTime bestAt = DateTime.MinValue;
                foreach (var it in arr)
                {
                    DateTime at;
                    string stamp = (string)(it["UpdatedAtUtc"] ?? it["RatedAtUtc"]);
                    if (!DateTime.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture,
                                           System.Globalization.DateTimeStyles.None, out at))
                    { at = DateTime.MinValue; }
                    if (best == null || at >= bestAt) { best = it; bestAt = at; }
                }
                if (best == null) { return null; }

                int rating = 0;
                int.TryParse(Convert.ToString(best["Rating"]), out rating);

                return new
                {
                    analysisId = analysisId,
                    rating = rating,
                    remark = (string)(best["Comment"] ?? "") ?? "",
                    by = (string)(best["UserName"] ?? "") ?? "",
                    at = (bestAt == DateTime.MinValue) ? "" : bestAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    role = (string)(best["UserRole"] ?? "") ?? "",
                    count = arr.Count
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Digits only, capped. The cap is not decoration: an unbounded IN list over a wide        // window would build a statement long enough to be refused, and the board must not
        // lose its ratings because someone opened a 30-day view.
        private static string JoinIds(List<int> ids)
        {
            if (ids == null || ids.Count == 0) { return ""; }
            var sb = new StringBuilder();
            int n = 0;
            // 1.0.0.9: 500 per column would truncate a wide window exactly as the client's 400
            // did, and just as invisibly. Raised, not removed -- an unbounded IN list can build
            // a statement long enough to be refused. If windows routinely pass this, the right
            // answer is a table-valued parameter, not a bigger number.
            for (int i = 0; i < ids.Count && n < 2000; i++)
            {
                if (ids[i] <= 0) { continue; }
                if (sb.Length > 0) { sb.Append(','); }
                sb.Append(ids[i]);
                n++;
            }
            return sb.ToString();
        }

        // ==================================================================
        // v1.0.0.8: WAS THE ALERT VALID? -- the reviewer's judgement, via AIAssessment.
        //
        //   GET  {ProxyBaseUrl}/api/AIAssessment/Get?AnalysisId=1600
        //   POST {ProxyBaseUrl}/api/AIAssessment/Upsert
        //        { AnalysisId, AssessedBy, AssessmentRole, WasAlertUseful }
        //
        // This is NOT the star rating. The stars say what a person thought of the AI's
        // analysis; this says whether the ALERT itself was worth raising. Two different
        // questions about the same run, which is why they are two API surfaces and two
        // controls rather than one chip carrying both.
        //
        // The API keys a row on (AnalysisId, AssessmentRole), so one analysis holds at
        // most one CLIENT row and one REVIEWER row. This page writes REVIEWER only --
        // AlertLiveController writes the CLIENT row from the alert-validity screen, and
        // the two must not overwrite each other.
        //
        // AssessedBy is the LOGIN USER ID. The CLIENT rows store a display name
        // ("ApprovalDashboard", "Ankit Sharma"); a reviewer judgement has to be traceable
        // to an account, and two people can share a name. The column is free text, so
        // nothing at the API enforces this -- it is enforced here.
        // v1.0.1.2: "DOES THIS ALERT HAVE AN ANALYSIS?", asked of the service the Detail
        // panel asks. One source of truth for the whole feature:
        //   - the board's toggle gate (AlertRatings, when SQL did not account for a row)
        //   - the write itself (SaveAlertValidity, when SQL cannot resolve the id)
        // Before this, the gate read SQL and Detail read the API, and every disagreement
        // between them surfaced as a dead toggle over an alert whose AnalysisId was
        // plainly visible one click away.
        // ==================================================================

        // How many alerts a single board load may probe over HTTP, and for how long. Only
        // reached for alerts SQL did not answer for, so on a healthy box this is zero.
        private const int AnalysisProbeCap = 120;
        private const int AnalysisProbeBudgetMs = 6000;

        // Every AnalysisId the API holds for this alert, newest first. Empty on failure --
        // callers cannot tell "none" from "could not ask" from this alone, which is why
        // the gate treats an empty result as "leave it as SQL found it" rather than as a
        // positive "no analysis".
        private static List<long> AnalysisIdsForAlert(long alertId)
        {
            var ids = new List<long>();
            if (alertId <= 0) { return ids; }
            try
            {
                string apiBase = (ConfigurationManager.AppSettings["ProxyBaseUrl"] ?? "").Trim();
                if (apiBase.Length == 0)
                { apiBase = (ConfigurationManager.AppSettings["DataApiBaseUrl"] ?? "").Trim(); }
                if (apiBase.Length == 0) { return ids; }
                if (!apiBase.EndsWith("/")) { apiBase += "/"; }

                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
                // IncludeJson=0: this only needs the ids, and the analysis blobs are the
                // expensive part of that payload. A server that ignores the flag still
                // answers correctly, just larger.
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(
                    apiBase + "api/AIAnalysis/Get?AlertId=" + alertId + "&IncludeJson=0");
                req.Method = "GET";
                req.Accept = "application/json";
                req.Timeout = 2500;

                string body;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK) { return ids; }
                    using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                    { body = sr.ReadToEnd(); }
                }
                if (string.IsNullOrWhiteSpace(body)) { return ids; }

                var arr = Newtonsoft.Json.Linq.JArray.Parse(body);
                var stamped = new List<KeyValuePair<DateTime, long>>();
                foreach (var it in arr)
                {
                    long aid = 0;
                    long.TryParse(DetailStr(it, "AnalysisId"), out aid);
                    if (aid <= 0) { continue; }
                    DateTime at;
                    string stamp = DetailStr(it, "AnalysisCompletedUtc");
                    if (stamp.Length == 0) { stamp = DetailStr(it, "CreatedAtUtc"); }
                    if (!DateTime.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture,
                                           System.Globalization.DateTimeStyles.None, out at))
                    { at = DateTime.MinValue; }
                    stamped.Add(new KeyValuePair<DateTime, long>(at, aid));
                }
                stamped.Sort(function_CompareDesc);
                foreach (var s in stamped) { if (!ids.Contains(s.Value)) { ids.Add(s.Value); } }
            }
            catch (Exception)
            {
                // Deliberately silent per alert: this runs up to AnalysisProbeCap times on a
                // board load and one log line each would bury the log. The caller logs the
                // aggregate.
            }
            return ids;
        }

        // Newest first. A named comparer rather than a lambda so the sort reads the same
        // way in the stack trace if it ever throws.
        private static int function_CompareDesc(KeyValuePair<DateTime, long> x, KeyValuePair<DateTime, long> y)
        {
            return y.Key.CompareTo(x.Key);
        }

        private static long LatestAnalysisIdForAlert(long alertId)
        {
            var ids = AnalysisIdsForAlert(alertId);
            return (ids.Count > 0) ? ids[0] : 0L;
        }

        // 1.0.2.0: every AnalysisId the API holds for this alert UNDER EITHER OF ITS IDS.
        // The alert's own id first, so the newest run of the stage the caller is actually
        // looking at still leads the list and ids[0] keeps meaning what it meant before --
        // the linked stage only ADDS runs that would otherwise be invisible.
        private static List<long> AnalysisIdsAcrossStages(int alertId, bool pending)
        {
            var ids = AnalysisIdsForAlert(alertId);
            string linkErr;
            var linked = LinkedAlertIds(alertId, pending, out linkErr);
            foreach (int other in linked)
            {
                foreach (long aid in AnalysisIdsForAlert(other))
                {
                    if (aid > 0 && !ids.Contains(aid)) { ids.Add(aid); }
                }
            }
            return ids;
        }

        // ==================================================================
        // 1.0.0.9: a real type, not an anonymous one. The mark is now built in one place,
        // filled in at another (the display name arrives after the parallel fetch, on the
        // request thread) and read in a third -- and an anonymous type can only be read
        // back out by reflection, which 1.0.0.8 did with three try/catch blocks per field.
        // ==================================================================
        // Property names stay lower-case so the JSON the board already parses is unchanged.
        private class AssessMark
        {
            public long analysisId { get; set; }
            public bool? useful { get; set; }
            // EXACTLY what the API stored: a user id for our REVIEWER rows, and possibly
            // a name for a row some other client wrote. Kept as-is so the board can fall
            // back to it when the account behind it cannot be resolved.
            public string by { get; set; }
            // The person, in words. Empty when unresolved -- never guessed, and never
            // silently filled with the id, because "1134" IS a meaningful answer to
            // "which account" and a misleading one to "who".
            public string byName { get; set; }
            public string at { get; set; }
            public string role { get; set; }
        }

        // Returns the REVIEWER row's mark for this analysis, or an unset one when nobody
        // has judged it. Returns null only when the read FAILED -- the board tells the two
        // apart, because "not marked" and "we could not ask" must not look the same on a
        // control whose whole job is to record a judgement.
        //
        // NOTE: this runs on a worker thread from AlertRatings, so it touches no
        // HttpContext and resolves no names. Name resolution needs the session token and
        // happens on the request thread afterwards.
        private static AssessMark FetchAssessment(long analysisId)
        {
            try
            {
                string apiBase = (ConfigurationManager.AppSettings["ProxyBaseUrl"] ?? "").Trim();
                if (apiBase.Length == 0)
                { apiBase = (ConfigurationManager.AppSettings["DataApiBaseUrl"] ?? "").Trim(); }
                if (apiBase.Length == 0) { return null; }
                if (!apiBase.EndsWith("/")) { apiBase += "/"; }

                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(
                    apiBase + "api/AIAssessment/Get?AnalysisId=" + analysisId);
                req.Method = "GET";
                req.Accept = "application/json";
                // Same 1500ms as FetchRating and for the same reason: this runs once per
                // analysed alert and must degrade to "unknown" rather than hold the board.
                req.Timeout = 1500;

                string body;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK) { return null; }
                    using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                    { body = sr.ReadToEnd(); }
                }
                if (string.IsNullOrWhiteSpace(body)) { return null; }

                var arr = Newtonsoft.Json.Linq.JArray.Parse(body);
                return PickReviewerMark(arr, analysisId);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // The REVIEWER row, newest first by UpdatedAtUtc then AssessedAtUtc. Array order is
        // the API's choice, not a contract, and the CLIENT row sits in the same array -- a
        // "last element wins" read would hand this board the client's answer to a different
        // question whenever the client happened to save second.
        private static AssessMark PickReviewerMark(Newtonsoft.Json.Linq.JArray arr, long analysisId)
        {
            var unset = new AssessMark
            {
                analysisId = analysisId,
                useful = null,
                by = "",
                byName = "",
                at = "",
                role = "REVIEWER"
            };
            if (arr == null || arr.Count == 0) { return unset; }

            Newtonsoft.Json.Linq.JToken best = null;
            DateTime bestAt = DateTime.MinValue;
            foreach (var it in arr)
            {
                string role = Convert.ToString(it["AssessmentRole"] ?? "");
                if (!string.Equals(role.Trim(), "REVIEWER", StringComparison.OrdinalIgnoreCase)) { continue; }
                DateTime at;
                string stamp = (string)(it["UpdatedAtUtc"] ?? it["AssessedAtUtc"]);
                if (!DateTime.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture,
                                       System.Globalization.DateTimeStyles.None, out at))
                { at = DateTime.MinValue; }
                if (best == null || at >= bestAt) { best = it; bestAt = at; }
            }
            if (best == null) { return unset; }

            // WasAlertUseful is nullable at the API: a REVIEWER row can exist with the
            // column still null because some other field was filled in. That is UNSET, not
            // false -- reading it as false would show a judgement nobody made.
            bool? useful = null;
            var tok = best["WasAlertUseful"];
            if (tok != null && tok.Type != Newtonsoft.Json.Linq.JTokenType.Null)
            {
                bool b;
                if (bool.TryParse(Convert.ToString(tok), out b)) { useful = b; }
            }

            return new AssessMark
            {
                analysisId = analysisId,
                useful = useful,
                by = Convert.ToString(best["AssessedBy"] ?? "") ?? "",
                byName = "",
                at = (bestAt == DateTime.MinValue) ? "" : bestAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                role = "REVIEWER"
            };
        }

        // ==================================================================
        // v1.0.0.9: WHO MARKED IT -- id in the record, name on the screen.
        //
        //   GET User/GetUserById/{id}   (the FRS API, via HttpClientFactory)
        //
        // AssessedBy stores the USER ID and that does not change: an id is the only thing
        // that stays true if someone is renamed, and two people can share a name. But
        // "Marked VALID by 1134" tells a reviewer nothing, so the id is resolved to a
        // person for display only. The stored record is untouched.
        //
        // THREAD: HttpClientFactory reads HttpContext.Current in its constructor and the
        // token comes off the session, so this can only run on the REQUEST thread -- never
        // inside the Parallel.ForEach that gathers the marks. That is why AlertRatings
        // fetches first and names second rather than doing both in the worker.
        // ==================================================================

        // id -> (resolved at, display name). Names change rarely and this is display-only,
        // so a short TTL is enough; without the cache a board showing one reviewer's marks
        // on forty alerts would ask the user API forty times per load.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, KeyValuePair<DateTime, string>>
            _userNameCache = new System.Collections.Concurrent.ConcurrentDictionary<int, KeyValuePair<DateTime, string>>();
        private const int UserNameCacheMinutes = 30;

        // "" when the id cannot be resolved -- a failed lookup must not be cached as a
        // blank name, or one API hiccup would blank that reviewer for the next half hour.
        private string ResolveUserName(int userId)
        {
            if (userId <= 0) { return ""; }

            KeyValuePair<DateTime, string> hit;
            if (_userNameCache.TryGetValue(userId, out hit)
                && (DateTime.UtcNow - hit.Key).TotalMinutes < UserNameCacheMinutes)
            { return hit.Value; }

            // The caller is already this user often enough to be worth short-circuiting,
            // and it saves a round trip on the common single-reviewer board.
            try
            {
                var me = ClsHttpContent.LoginUser;
                if (me != null && me.Id == userId)
                {
                    string mine = ((me.FirstName ?? "") + " " + (me.LastName ?? "")).Trim();
                    if (mine.Length > 0)
                    {
                        _userNameCache[userId] = new KeyValuePair<DateTime, string>(DateTime.UtcNow, mine);
                        return mine;
                    }
                }
            }
            catch (Exception) { }

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("User/GetUserById/" + userId).Result;
                    if (response.StatusCode != HttpStatusCode.OK) { return ""; }
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (string.IsNullOrWhiteSpace(jsonString)) { return ""; }

                    var u = JsonConvert.DeserializeObject<Domain.User>(jsonString);
                    if (u == null) { return ""; }

                    string name = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim();
                    // Email is a poor label but a true one, and better than falling back to
                    // the id we are trying to replace.
                    if (name.Length == 0) { name = (u.EmailAddress ?? "").Trim(); }
                    if (name.Length == 0) { return ""; }

                    _userNameCache[userId] = new KeyValuePair<DateTime, string>(DateTime.UtcNow, name);
                    return name;
                }
            }
            catch (Exception ex)
            {
                AiLogBoard("ResolveUserName failed for " + userId + ": " + ex.Message);
                return "";
            }
        }

        // Fills byName on every mark in the map, one lookup per DISTINCT id. A board where
        // one reviewer marked forty alerts costs one call, not forty.
        //
        // A `by` that is not a number is left in place as the name: rows written by another
        // client store a display name in that column, and re-resolving it is neither
        // possible nor needed.
        private void ResolveMarkNames(Dictionary<string, object> marks)
        {
            if (marks == null || marks.Count == 0) { return; }

            var names = new Dictionary<int, string>();
            foreach (var kv in marks)
            {
                var m = kv.Value as AssessMark;
                if (m == null || string.IsNullOrEmpty(m.by)) { continue; }

                int uid;
                if (!int.TryParse(m.by.Trim(), out uid) || uid <= 0)
                {
                    // Already a name, not an id.
                    m.byName = m.by.Trim();
                    continue;
                }
                if (!names.ContainsKey(uid)) { names[uid] = ResolveUserName(uid); }
                m.byName = names[uid];
            }
        }

        // ------------------------------------------------------------------
        // POST /FRS25/UnifiedAlertAnalysis/SaveAlertValidity
        //   { id, source: "pending" | "frs", useful: true|false, analysisId }
        //
        // The same three gates as SaveAiRating, in the same order and for the same reasons:
        // a discarded alert is sitekeeping-only, a client-supplied analysisId is validated
        // rather than trusted, and an alert with no analysis run has nothing to attach a
        // judgement to.
        // ------------------------------------------------------------------
        [HttpPost]
        public JsonResult SaveAlertValidity(int id, string source, bool useful, long analysisId = 0)
        {
            if (id <= 0) { return Json(new { ok = false, error = "No alert id." }); }

            bool pending = !string.Equals((source ?? "").Trim(), "frs", StringComparison.OrdinalIgnoreCase);

            if (pending && !AaPrivileged())
            {
                AiLogBoard("SaveAlertValidity refused: pending " + id + ", caller is not sitekeeping");
                return Json(new { ok = false, notAnalysed = true, error = "This alert has no stored analysis yet." });
            }

            string lookupErr;
            long ownId = ResolveAnalysisId(id, pending, out lookupErr);

            // v1.0.1.2: when SQL cannot answer, ASK THE API -- the same one the Detail
            // panel and the board's toggle gate now use. Without this the gate would
            // enable a toggle that the write then refused with "no stored analysis",
            // which is a worse failure than the dead toggle it replaced: the operator
            // would believe the board was broken rather than the alert unanalysed.
            var apiIds = (List<long>)null;
            if (ownId <= 0)
            {
                // 1.0.2.0: and the promoted alert's OTHER id with it. An approved alert
                // analysed only while it was in review has its runs under the pending id,
                // so asking the API for the FRS id alone answers "never analysed" for an
                // alert whose Detail panel is showing three runs.
                apiIds = AnalysisIdsAcrossStages(id, pending);
                if (apiIds.Count > 0)
                {
                    ownId = apiIds[0];
                    AiLogBoard("SaveAlertValidity: SQL had no AnalysisId for "
                               + (pending ? "pending " : "frs ") + id
                               + "; AIAnalysis/Get returned " + ownId);
                }
            }

            if (analysisId > 0 && analysisId != ownId)
            {
                // Validate the client's id against SQL first, then against the API. Both
                // are asked because either may be the one that knows: SQL owns the
                // FRSAlertId/PendingAlertApprovalId linkage, the API owns the alert's
                // analysis history, and this board has now seen environments where only
                // one of the two answers.
                bool ok = AnalysisBelongsTo(analysisId, id, pending);
                if (!ok)
                {
                    if (apiIds == null) { apiIds = AnalysisIdsAcrossStages(id, pending); }
                    ok = apiIds.Contains(analysisId);
                }
                if (!ok)
                {
                    AiLogBoard("SaveAlertValidity refused: analysisId " + analysisId
                               + " does not belong to " + (pending ? "pending " : "frs ") + id);
                    return Json(new { ok = false, error = "That analysis does not belong to this alert." });
                }
            }
            if (analysisId <= 0) { analysisId = ownId; }

            if (analysisId <= 0)
            {
                if (lookupErr.Length > 0)
                {
                    AiLogBoard("SaveAlertValidity: AnalysisId lookup failed for "
                               + (pending ? "pending " : "frs ") + id + ": " + lookupErr);
                    return Json(new { ok = false, error = "Could not look up the analysis: " + lookupErr });
                }
                AiLogBoard("SaveAlertValidity: no FRSAlert_AIAnalysis row for "
                           + (pending ? "pending " : "frs ") + id);
                return Json(new
                {
                    ok = false,
                    notAnalysed = true,
                    error = "This alert has no stored analysis yet. Run Insight first, then mark it."
                });
            }

            // The USER ID, as text -- the column is a string and the API is given exactly
            // what the sample REVIEWER payload sends. A session with no user is refused
            // rather than writing an anonymous judgement: an unattributed row in a review
            // record is worse than no row, because it cannot be questioned later.
            string assessedBy = "";
            try
            {
                var lu = ClsHttpContent.LoginUser;
                if (lu != null && lu.Id > 0) { assessedBy = lu.Id.ToString(System.Globalization.CultureInfo.InvariantCulture); }
            }
            catch (Exception) { }
            if (assessedBy.Length == 0)
            {
                AiLogBoard("SaveAlertValidity: no login user id on the session");
                return Json(new { ok = false, error = "Your session has expired. Sign in again to record this." });
            }

            // What it was BEFORE, so the board can say what this replaced rather than
            // silently overwriting an earlier reviewer's answer. 1.0.0.9: read straight
            // off the typed mark -- the three reflection blocks this replaced could each
            // fail silently and leave a revision looking like a first answer.
            bool? prevUseful = null;
            string prevBy = "", prevByName = "", prevAt = "";
            var existing = FetchAssessment(analysisId);
            if (existing != null)
            {
                prevUseful = existing.useful;
                prevBy = existing.by ?? "";
                prevAt = existing.at ?? "";
                int prevUid;
                prevByName = int.TryParse(prevBy.Trim(), out prevUid) && prevUid > 0
                    ? ResolveUserName(prevUid)
                    : prevBy.Trim();
            }

            string apiBase = (ConfigurationManager.AppSettings["ProxyBaseUrl"] ?? "").Trim();
            if (apiBase.Length == 0)
            { apiBase = (ConfigurationManager.AppSettings["DataApiBaseUrl"] ?? "").Trim(); }
            if (apiBase.Length == 0)
            {
                AiLogBoard("SaveAlertValidity: no ProxyBaseUrl/DataApiBaseUrl configured");
                return Json(new { ok = false, error = "Assessment API is not configured on this server." });
            }
            if (!apiBase.EndsWith("/")) { apiBase += "/"; }
            string url = apiBase + "api/AIAssessment/Upsert";

            try
            {
                var body = new Newtonsoft.Json.Linq.JObject();
                body["AnalysisId"] = analysisId;
                body["AssessedBy"] = assessedBy;
                body["AssessmentRole"] = "REVIEWER";
                body["WasAlertUseful"] = useful;
                // Nothing else is sent. The endpoint is an Upsert over a wide row, and a
                // field this page does not ask about must not be written -- posting nulls
                // for the other reviewer columns would erase whatever a fuller review form
                // had already recorded against the same (AnalysisId, REVIEWER) key.
                byte[] payload = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));

                // HttpWebRequest, matching SaveAiRating. The 1.0.3.0 note in this file
                // records why System.Net.Http is avoided on this path.
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Accept = "application/json";
                req.Timeout = 8000;
                req.ContentLength = payload.Length;
                using (var rs = req.GetRequestStream()) { rs.Write(payload, 0, payload.Length); }

                string respText = "";
                HttpStatusCode code;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                {
                    code = resp.StatusCode;
                    using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                    { respText = sr.ReadToEnd(); }
                }

                if (code != HttpStatusCode.OK && code != HttpStatusCode.Created)
                {
                    AiLogBoard("SaveAlertValidity: AIAssessment/Upsert -> " + (int)code + " " + respText);
                    return Json(new { ok = false, error = "Assessment API returned " + (int)code + "." });
                }

                AiLogBoard("validity saved: " + (pending ? "pending " : "frs ") + id
                           + " analysisId=" + analysisId + " useful=" + useful
                           + (prevUseful.HasValue ? (" (was " + prevUseful.Value + ")") : "")
                           + " by=" + assessedBy + "/REVIEWER");

                return Json(new
                {
                    ok = true,
                    analysisId = analysisId,
                    useful = useful,
                    by = assessedBy,
                    // 1.0.0.9: the name goes back with the save so the switch can say who
                    // marked it without waiting for the next board load to resolve it.
                    // ResolveUserName short-circuits on the caller's own session, so this
                    // costs no round trip.
                    byName = ResolveUserName(int.Parse(assessedBy, System.Globalization.CultureInfo.InvariantCulture)),
                    at = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                    revised = prevUseful.HasValue,
                    previousUseful = prevUseful,
                    previousBy = prevBy,
                    previousByName = prevByName,
                    previousAt = prevAt
                });
            }
            catch (System.Net.WebException wex)
            {
                string detail = wex.Message;
                try
                {
                    if (wex.Response != null)
                    {
                        using (var sr = new System.IO.StreamReader(wex.Response.GetResponseStream()))
                        { detail = sr.ReadToEnd(); }
                    }
                }
                catch (Exception) { }
                AiLogBoard("SaveAlertValidity failed for " + id + " (analysisId " + analysisId + "): " + detail);
                return Json(new { ok = false, error = "Could not save: " + detail });
            }
            catch (Exception ex)
            {
                AiLogBoard("SaveAlertValidity failed for " + id + ": " + ex.ToString());
                return Json(new { ok = false, error = "Could not save: " + ex.Message });
            }
        }
        // ==================================================================
        // v1.0.1.0: ANALYSIS DETAIL -- the full history for one alert.
        //
        //   GET {ProxyBaseUrl}/api/AIAnalysis/Get?AlertId={id}&IncludeJson=1
        //
        // The AlertId this passes is the id the board carries on the row and the same one
        // the Insight/analyze flow sends (the view builds ctx = { alertId: id } from a.id).
        // So AIAnalysis.AlertId == a.id for an analysed alert -- no FRSAlert_AIAnalysis
        // lookup is needed to find the RUNS, unlike the rating/validity writes which key on
        // AnalysisId.
        //
        // ------------------------------------------------------------------
        // 1.0.2.0: ONE ALERT CAN HAVE TWO SUCH IDS, AND THE RUNS ARE SPLIT BETWEEN THEM.
        //
        // "a.id" is whatever id the board held AT THE TIME OF THE RUN. A card in review is
        // keyed by PendingAlertApproval.Id; once the alert is approved it is promoted to an
        // FRSAlert and the board keys the same physical alert by FRSAlert.Id. Those are
        // unrelated sequences (1.0.0.4), so an alert that was analysed in review and then
        // approved has its early runs stored under one id and any later ones under another.
        //
        // Asking only for the id on the card therefore returned HALF the history -- and on
        // the common case, where the analysis was what informed the approval decision and
        // nobody re-ran it afterwards, it returned NONE of it: the panel printed "No
        // analysis has been run on this alert yet" over an alert that had been analysed.
        //
        // This action now asks for the alert's OTHER id as well and merges:
        //   - the link comes from dbo.PendingAlertApproval (LinkedAlertIds), the only place
        //     the two spaces are tied together;
        //   - `source` says which space the caller's id is in, and it MUST, because 3010 is
        //     a valid id in both and an unqualified link would attach another alert's
        //     analyses to this panel. A caller that omits it gets the link only when the
        //     lookup is unambiguous -- see the resolution below;
        //   - `linkedId` is an optional hint from the board, which already holds both ids
        //     on the row. It is used ONLY when SQL produced no link, so the panel still
        //     merges on a web host that cannot reach E7MRIV2DB_SQL. It grants nothing: this
        //     endpoint already accepts any alertId, so passing one as a hint is no more
        //     powerful than asking for it directly;
        //   - every run is tagged with the STAGE it was made in, "pending" or "approved",
        //     and the runs are de-duped on AnalysisId, because a promoted alert whose
        //     FRSAlert_AIAnalysis row carries both ids can return the same run twice.
        //
        // The stage is not decoration. A verdict reached while the alert was still in
        // review was reached WITHOUT the approval, often without the reset, and sometimes
        // without the maintainer's remark; one reached afterwards saw all three. Collapsing
        // them into one undated list is how the panel would show two contradictory verdicts
        // with nothing to explain the contradiction -- the same reasoning as the hadReset
        // note in 1.0.1.3.
        // ------------------------------------------------------------------
        //
        // The API returns EVERY analysis run for an alert, each with a large
        // AnalysisOutputJson and AlertInputJson blob. This action parses server-side and
        // returns only the fields the detail panel shows, so a click does not ship a
        // quarter-megabyte of evidence and snapshot arrays to the browser per alert.
        // ==================================================================

        // At most this many linked ids are followed. There is normally exactly one; the cap
        // stops a row that somehow links to many from turning one click into a fan-out.
        private const int DetailLinkedIdCap = 3;

        [HttpGet]
        public JsonResult AlertAnalysisDetail(long alertId, string source = "", long linkedId = 0)
        {
            if (alertId <= 0)
            { return Json(new { ok = false, error = "No alert id." }, JsonRequestBehavior.AllowGet); }

            string apiBase = (ConfigurationManager.AppSettings["ProxyBaseUrl"] ?? "").Trim();
            if (apiBase.Length == 0)
            { apiBase = (ConfigurationManager.AppSettings["DataApiBaseUrl"] ?? "").Trim(); }
            if (apiBase.Length == 0)
            {
                AiLogBoard("AlertAnalysisDetail: no ProxyBaseUrl/DataApiBaseUrl configured");
                return Json(new { ok = false, error = "Analysis API is not configured on this server." },
                            JsonRequestBehavior.AllowGet);
            }
            if (!apiBase.EndsWith("/")) { apiBase += "/"; }

            // ---------- which ids to ask for, and what each one means ----------
            string src = (source ?? "").Trim();
            bool pendingPrimary = string.Equals(src, "pending", StringComparison.OrdinalIgnoreCase);
            bool sourceGiven = src.Length > 0;
            bool linkAmbiguous = false;
            bool linkHinted = false;
            string linkError = "";
            var linked = new List<long>();

            // PendingAlertApproval.Id and FRSAlert.Id are INT columns. An id outside that
            // range cannot be in either table, so it simply has no link to look up.
            int idAsInt = (alertId > 0 && alertId <= int.MaxValue) ? (int)alertId : 0;

            if (idAsInt > 0 && sourceGiven)
            {
                string le;
                foreach (int v in LinkedAlertIds(idAsInt, pendingPrimary, out le))
                {
                    if (linked.Count >= DetailLinkedIdCap) { break; }
                    linked.Add(v);
                }
                linkError = le ?? "";
            }
            else if (idAsInt > 0)
            {
                // A caller that did not say which space its id is in. Both directions are
                // asked and the answer is used ONLY if exactly one of them matched: if both
                // did, this id names a real row in each table and there is no way to tell
                // which alert the caller meant. Guessing would put another alert's analyses
                // on this panel, so the id is used alone and the response says why.
                string le1, le2;
                var asFrs = LinkedAlertIds(idAsInt, false, out le1);
                var asPending = LinkedAlertIds(idAsInt, true, out le2);
                linkError = (le1 ?? "").Length > 0 ? le1 : (le2 ?? "");

                if (asFrs.Count > 0 && asPending.Count > 0)
                {
                    linkAmbiguous = true;
                    AiLogBoard("AlertAnalysisDetail: id " + alertId + " is valid in BOTH id spaces and "
                               + "no source was given -- merging skipped. Send source=frs|pending.");
                }
                else if (asFrs.Count > 0)
                {
                    pendingPrimary = false;
                    foreach (int v in asFrs) { if (linked.Count < DetailLinkedIdCap) { linked.Add(v); } }
                }
                else if (asPending.Count > 0)
                {
                    pendingPrimary = true;
                    foreach (int v in asPending) { if (linked.Count < DetailLinkedIdCap) { linked.Add(v); } }
                }
            }

            // A link found in the table also settles WHICH DIRECTION the id runs in, so a
            // caller that gave no source still gets a named stage when the lookup was
            // unambiguous. Captured before the hint below, which settles nothing.
            bool stageKnown = sourceGiven || linked.Count > 0;

            // The board's own hint, used only where SQL gave nothing. Never used to OVERRIDE
            // a link SQL did find -- the table is the authority, the hint is the fallback.
            if (linked.Count == 0 && !linkAmbiguous && linkedId > 0 && linkedId != alertId)
            {
                linked.Add(linkedId);
                linkHinted = true;
            }

            // "" rather than a guess. Without a source and without a link there is nothing
            // that says which table this id came from, and labelling every run "after
            // approval" on that basis would be the panel asserting something it does not
            // know -- on a discarded alert it would assert the opposite of the truth. An
            // empty stage draws no chip and counts toward neither total, so the panel shows
            // the history and says nothing about the stage, which is exactly what is known.
            string primaryStage = stageKnown ? (pendingPrimary ? "pending" : "approved") : "";
            string linkedStage = !stageKnown ? "" : (pendingPrimary ? "approved" : "pending");

            // ---------- ask, once per id ----------
            var plan = new List<KeyValuePair<long, string>>();
            plan.Add(new KeyValuePair<long, string>(alertId, primaryStage));
            foreach (long other in linked)
            { plan.Add(new KeyValuePair<long, string>(other, linkedStage)); }

            var bodies = new System.Collections.Concurrent.ConcurrentDictionary<long, string>();
            var errors = new System.Collections.Concurrent.ConcurrentDictionary<long, string>();

            try
            {
                if (plan.Count == 1)
                {
                    // The overwhelmingly common case. Kept off the thread pool so a single-id
                    // click behaves exactly as it did before 1.0.2.0.
                    string err;
                    string body = FetchAnalysisBody(apiBase, plan[0].Key, out err);
                    if (err.Length > 0) { errors[plan[0].Key] = err; }
                    else { bodies[plan[0].Key] = body ?? ""; }
                }
                else
                {
                    // At most DetailLinkedIdCap + 1 requests, run together rather than in
                    // single file: sequentially this would add a full timeout per linked id
                    // to a click the operator is waiting on. FetchAnalysisBody uses
                    // HttpWebRequest and touches no HttpContext, so it is safe off the
                    // request thread -- the same reasoning as the rating fetch in 1.0.1.0.
                    System.Threading.Tasks.Parallel.ForEach(
                        plan,
                        new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 4 },
                        step =>
                        {
                            string err;
                            string body = FetchAnalysisBody(apiBase, step.Key, out err);
                            if (err.Length > 0) { errors[step.Key] = err; }
                            else { bodies[step.Key] = body ?? ""; }
                        });
                }
            }
            catch (Exception ex)
            {
                AiLogBoard("AlertAnalysisDetail: fetch failed for alert " + alertId + ": " + ex.Message);
                return Json(new { ok = false, error = "Could not load analysis: " + ex.Message },
                            JsonRequestBehavior.AllowGet);
            }

            // Every id failed -- report the PRIMARY id's error, because that is the alert the
            // operator clicked and the linked id is an implementation detail to them.
            if (bodies.Count == 0)
            {
                string primaryErr;
                if (!errors.TryGetValue(alertId, out primaryErr) || primaryErr.Length == 0)
                { primaryErr = "the analysis service did not answer."; }
                AiLogBoard("AlertAnalysisDetail failed for alert " + alertId + ": " + primaryErr);
                return Json(new { ok = false, error = "Could not load analysis: " + primaryErr },
                            JsonRequestBehavior.AllowGet);
            }

            // ---------- merge ----------
            var outList = new List<object>();
            // De-dup on AnalysisId. A promoted alert whose FRSAlert_AIAnalysis row carries
            // both ids can return the SAME run under both, and two identical cards on the
            // panel would read as two separate analyses that happened to agree.
            var seenAnalysisIds = new HashSet<string>();
            int fromPending = 0, fromApproved = 0, rawCount = 0;
            bool truncated = false;

            // 100 runs is far more than any alert legitimately holds; the cap only stops a
            // runaway from turning one click into a giant response. Applied ACROSS the merge,
            // not per id, so the response size is bounded however many ids were asked.
            const int cap = 100;

            foreach (var step in plan)
            {
                string body;
                if (!bodies.TryGetValue(step.Key, out body)) { continue; }
                if (string.IsNullOrWhiteSpace(body)) { continue; }

                Newtonsoft.Json.Linq.JArray arr;
                try { arr = Newtonsoft.Json.Linq.JArray.Parse(body); }
                catch (Exception pe)
                {
                    // One unparseable payload must not lose the other id's history.
                    AiLogBoard("AlertAnalysisDetail: unparseable payload for id " + step.Key
                               + ": " + pe.Message);
                    continue;
                }

                rawCount += arr.Count;
                foreach (var it in arr)
                {
                    if (outList.Count >= cap) { truncated = true; break; }

                    string aid = DetailStr(it, "AnalysisId");
                    if (aid.Length > 0)
                    {
                        if (seenAnalysisIds.Contains(aid)) { continue; }
                        seenAnalysisIds.Add(aid);
                    }

                    outList.Add(ProjectAnalysis(it, step.Value, step.Key));
                    // An unnamed stage counts toward NEITHER total. The two counts are what
                    // the panel uses to decide whether this alert spans both stages, and a
                    // run whose stage is unknown is not evidence either way.
                    if (step.Value == "pending") { fromPending++; }
                    else if (step.Value == "approved") { fromApproved++; }
                }
                // No trailing cap test on purpose: `truncated` is set at the point a run is
                // actually turned away, so a merge that lands on exactly 100 with nothing
                // left to add does not report runs as withheld. The next id, if there is
                // one, trips the test on its first row.
            }

            // Newest first, by CompletedAtUtc then CreatedAtUtc, so the run on screen leads
            // the list rather than trusting the API's array order. Across a merge this is
            // load-bearing rather than cosmetic: the two ids arrive as two separate blocks
            // and only the timestamp puts the alert's history back in order.
            outList.Sort((x, y) =>
            {
                DateTime ax = DetailSortStamp(x), ay = DetailSortStamp(y);
                return ay.CompareTo(ax);
            });

            if (linked.Count > 0)
            {
                AiLogBoard("AlertAnalysisDetail: alert " + alertId + " (" + primaryStage + ") merged with "
                           + string.Join(",", linked) + " (" + linkedStage + ")"
                           + (linkHinted ? " [hint]" : "")
                           + " -> " + outList.Count + " runs (" + fromApproved + " approved, "
                           + fromPending + " pending)");
            }

            return Json(new
            {
                ok = true,
                alertId = alertId,
                // What the panel needs to caption the merge. linkedAlertIds is a LIST because
                // one FRSAlert can sit behind several review rows -- a re-review writes
                // another -- and naming only the first would misreport where a run came from.
                stage = primaryStage,
                linkedAlertIds = linked,
                linkedStage = linked.Count > 0 ? linkedStage : "",
                linkFromHint = linkHinted,
                linkAmbiguous = linkAmbiguous,
                linkError = linkError ?? "",
                // Per-stage counts, so the panel can say "2 while in review, 1 after
                // approval" without recounting the list it was just handed.
                pendingStageCount = fromPending,
                approvedStageCount = fromApproved,
                // Ids that could not be reached at all. Non-empty with ok=true means the
                // panel is showing PART of the history, and it must be able to say so
                // rather than presenting a partial list as the whole record.
                unreachableIds = errors.Keys.ToList(),
                count = outList.Count,
                // Set only when the 100-run cap actually cut the merge off. It is NOT
                // derived from the raw row count: de-duplication legitimately shrinks the
                // list, and reporting that as "most recent 100" would tell the reader runs
                // were withheld when none were.
                truncated = truncated,
                // What the two ids returned before de-duplication, for the diagnostics
                // line only -- rawCount > count is normal on a promoted alert.
                rawCount = rawCount,
                analyses = outList
            }, JsonRequestBehavior.AllowGet);
        }

        // One GET to AIAnalysis/Get, returning the raw body. Split out of
        // AlertAnalysisDetail in 1.0.2.0 because that action now calls it once per id and a
        // failure on one id must not lose the other's history -- so the error comes back as
        // a value rather than as an exception that would unwind the whole merge.
        //
        // A 404 is NOT an error here: this endpoint answers 404 for an alert with no stored
        // analysis, which is an ordinary empty result. The same distinction AiChatController
        // draws for its cache reads -- treating it as a failure is what would turn "nothing
        // recorded under this id" into a red error box on a panel that has the other id's
        // runs to show.
        private static string FetchAnalysisBody(string apiBase, long alertId, out string error)
        {
            error = "";
            string url = apiBase + "api/AIAnalysis/Get?AlertId=" + alertId + "&IncludeJson=1";
            try
            {
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = "GET";
                req.Accept = "application/json";
                // Longer than the per-alert rating read: this is a deliberate click, one
                // alert at a time, and the payload is large. 12s so a slow model-history
                // table still lands rather than a click that silently does nothing.
                req.Timeout = 12000;

                string body;
                HttpStatusCode code;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                {
                    code = resp.StatusCode;
                    using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                    { body = sr.ReadToEnd(); }
                }
                if (code == HttpStatusCode.NotFound) { return ""; }
                if (code != HttpStatusCode.OK)
                {
                    AiLogBoard("AlertAnalysisDetail: AIAnalysis/Get -> " + (int)code + " for alert " + alertId);
                    error = "Analysis API returned " + (int)code + ".";
                    return null;
                }
                return body;
            }
            catch (System.Net.WebException wex)
            {
                var http = wex.Response as System.Net.HttpWebResponse;
                if (http != null && http.StatusCode == HttpStatusCode.NotFound)
                {
                    // Nothing stored under this id. Not a failure.
                    return "";
                }
                string detail = wex.Message;
                try
                {
                    if (wex.Response != null)
                    {
                        using (var sr = new System.IO.StreamReader(wex.Response.GetResponseStream()))
                        { detail = sr.ReadToEnd(); }
                    }
                }
                catch (Exception) { }
                AiLogBoard("AlertAnalysisDetail fetch failed for alert " + alertId + ": " + detail);
                error = detail;
                return null;
            }
            catch (Exception ex)
            {
                AiLogBoard("AlertAnalysisDetail fetch failed for alert " + alertId + ": " + ex.Message);
                error = ex.Message;
                return null;
            }
        }

        // One analysis row -> the compact shape the detail panel renders. The verdict block
        // is the important part and it lives INSIDE AnalysisOutputJson as a nested JSON
        // STRING, so it is parsed here rather than shipped whole. FinalVerdict is used when
        // set (the runs that actually called the model), and the parsed verdict fills in for
        // the cache-served rows where the top-level Final* columns are null.
        //
        // 1.0.2.0: `stage` and `sourceAlertId` say WHICH OF THE ALERT'S TWO IDS this run was
        // stored under -- "pending" for a run made while the alert was in review, "approved"
        // for one made after it was promoted. Carried per run rather than per response
        // because a merged panel holds both, and a reader comparing two verdicts has to be
        // able to see that the earlier one was reached before the approval.
        private static object ProjectAnalysis(Newtonsoft.Json.Linq.JToken it, string stage, long sourceAlertId)
        {
            string verdict = DetailStr(it, "FinalVerdict");
            string confidence = DetailStr(it, "FinalConfidence");
            string headline = DetailStr(it, "FinalReasoning");
            string likelyCause = "", category = "", recommended = "", caveats = "";
            var evidence = new List<string>();
            bool servedFromCache = false;
            string cachedAt = "";
            // Declared out here, assigned inside the parse. A malformed blob leaves them null
            // and the view omits those sections rather than failing the whole row.
            object viz = null, assetHistory = null, snapshot = null;

            // Dig the verdict block out of the nested output json. Wrapped so a malformed or
            // absent blob degrades to "no parsed verdict" rather than failing the whole row.
            try
            {
                string outJson = DetailStr(it, "AnalysisOutputJson");
                if (outJson.Length > 0)
                {
                    var oj = Newtonsoft.Json.Linq.JObject.Parse(outJson);
                    var vd = oj["verdict"] as Newtonsoft.Json.Linq.JObject;
                    if (vd != null)
                    {
                        if (verdict.Length == 0) { verdict = Convert.ToString(vd["verdict"] ?? ""); }
                        if (confidence.Length == 0) { confidence = Convert.ToString(vd["confidence"] ?? ""); }
                        if (headline.Length == 0) { headline = Convert.ToString(vd["headline"] ?? ""); }
                        likelyCause = Convert.ToString(vd["likely_cause"] ?? "");
                        category = Convert.ToString(vd["category"] ?? "");
                        recommended = Convert.ToString(vd["recommended_action"] ?? "");
                        caveats = Convert.ToString(vd["caveats"] ?? "");
                        var ev = vd["evidence"] as Newtonsoft.Json.Linq.JArray;
                        if (ev != null)
                        {
                            foreach (var e in ev)
                            {
                                string s = Convert.ToString(e ?? "");
                                if (s.Length > 0) { evidence.Add(s); }
                            }
                        }
                        servedFromCache = string.Equals(Convert.ToString(vd["served_from_cache"] ?? ""), "True",
                                                        StringComparison.OrdinalIgnoreCase);
                        cachedAt = DetailLocal(Convert.ToString(vd["cached_at"] ?? ""));
                    }
                    // served_from_cache sits at the top of the analysis object on some rows,
                    // beside verdict rather than inside it.
                    if (!servedFromCache)
                    {
                        servedFromCache = string.Equals(Convert.ToString(oj["served_from_cache"] ?? ""), "True",
                                                        StringComparison.OrdinalIgnoreCase);
                    }

                    // 1.0.1.3: everything below was already sitting in the blob and was being
                    // thrown away. The panel was rendering a verdict and eleven lines of build
                    // metadata; the measurement that produced the verdict, the relay states, the
                    // asset's recent history and the live snapshot were all parsed out and
                    // dropped on the floor.
                    viz = DetailViz(vd);
                    assetHistory = DetailHistory(vd);
                    snapshot = DetailSnapshot(oj);
                }
            }
            catch (Exception) { }

            object card = DetailCard(DetailStr(it, "AlertInputJson"));

            return new
            {
                analysisId = DetailStr(it, "AnalysisId"),

                // 1.0.2.0: the review stage this run belongs to, and the id it is filed
                // under. Both are needed: the stage is what the panel labels, the id is
                // what a rating or validity write has to be traced back to.
                stage = stage ?? "",
                sourceAlertId = sourceAlertId,

                verdict = verdict,
                confidence = confidence,
                headline = headline,
                likelyCause = likelyCause,
                category = category,
                recommendedAction = recommended,
                caveats = caveats,
                evidence = evidence,

                // What the verdict was actually computed from.
                viz = viz,
                assetHistory = assetHistory,
                snapshot = snapshot,
                card = card,

                status = DetailStr(it, "AnalysisStatus"),
                startedAt = DetailLocal(DetailStr(it, "AnalysisStartedUtc")),
                completedAt = DetailLocal(DetailStr(it, "AnalysisCompletedUtc")),
                createdAt = DetailLocal(DetailStr(it, "CreatedAtUtc")),
                updatedAt = DetailLocal(DetailStr(it, "UpdatedAtUtc")),
                triggeredAt = DetailLocal(DetailStr(it, "AlertTriggeredAtUtc")),

                // 1.0.1.3: engine, provider, model, thinking, turns, tokens, cost, total/model
                // time, verification tier, server node, controller version and user role are NO
                // LONGER PROJECTED. They are build and billing metadata: an operator acts on the
                // verdict and the evidence, never on which model produced it. This is the same
                // call the owner made twice already -- .104 pulled the engine/model segment off
                // the REUSED badge ("model never display -- just cache/fresh") and .108 removed
                // the run-log button because it was "still showing deepseek". Redacting at the
                // PROJECTION rather than in the view means the names do not reach the browser at
                // all, so they cannot come back through a devtools panel or a copied payload.
                by = DetailStr(it, "UserName"),

                servedCount = DetailStr(it, "ServedCount"),
                lastServedTo = DetailStr(it, "LastServedTo"),
                lastServedAt = DetailLocal(DetailStr(it, "LastServedAtUtc")),

                isLatest = string.Equals(DetailStr(it, "IsLatest"), "True", StringComparison.OrdinalIgnoreCase),
                isStale = string.Equals(DetailStr(it, "IsStale"), "True", StringComparison.OrdinalIgnoreCase),

                servedFromCache = servedFromCache,
                cachedAt = cachedAt,

                assetName = DetailStr(it, "AssetName"),
                causeCode = DetailStr(it, "CauseCode"),
                alertType = DetailStr(it, "AlertType")
            };
        }

        /* 1.0.1.3 -- the measurement behind the verdict. viz carries the parameter that
        breached, what it read, what it should have read and where it sits against the
        baseline. Without it the panel asserts a conclusion and shows nothing of the number
        that produced it. Values stay STRINGS: they are printed, never arithmetic, and a
        double round-trip turns 0.04 into 0.040000000000000001 on some rows. */
        private static object DetailViz(Newtonsoft.Json.Linq.JObject vd)
        {
            // A row with no verdict block reaches here with vd null. Guarded rather than
            // called under an if: the snapshot is read off the ROOT object and is still
            // there on such a row, and one throw inside the shared try would lose it too.
            if (vd == null) { return null; }
            var z = vd["viz"] as Newtonsoft.Json.Linq.JObject;
            if (z == null) { return null; }
            object secondary = null;
            var s = z["secondary"] as Newtonsoft.Json.Linq.JObject;
            if (s != null)
            {
                secondary = new
                {
                    label = Convert.ToString(s["label"] ?? ""),
                    value = Convert.ToString(s["value"] ?? ""),
                    unit = Convert.ToString(s["unit"] ?? "")
                };
            }
            return new
            {
                metric = Convert.ToString(z["metric"] ?? ""),
                unit = Convert.ToString(z["unit"] ?? ""),
                value = Convert.ToString(z["value"] ?? ""),
                threshold = Convert.ToString(z["threshold"] ?? ""),
                baseline = Convert.ToString(z["baseline"] ?? ""),
                direction = Convert.ToString(z["direction"] ?? ""),
                pctChange = Convert.ToString(z["pct_change"] ?? ""),
                phaseNote = Convert.ToString(z["phase_note"] ?? ""),
                secondary = secondary
            };
        }

        /* Has this asset been misbehaving? sameCause is the one that matters: nine alerts
        under nine DIFFERENT cause codes is a different story from nine of the same, and the
        panel should let the reader see which they are looking at. */
        private static object DetailHistory(Newtonsoft.Json.Linq.JObject vd)
        {
            if (vd == null) { return null; }
            var ah = vd["asset_history"] as Newtonsoft.Json.Linq.JObject;
            if (ah == null) { return null; }
            var byCause = new List<object>();
            var bc = ah["byCause"] as Newtonsoft.Json.Linq.JObject;
            if (bc != null)
            {
                foreach (var p in bc.Properties())
                {
                    byCause.Add(new { code = p.Name, n = Convert.ToString(p.Value ?? "") });
                }
            }
            return new
            {
                windowDays = Convert.ToString(ah["windowDays"] ?? ""),
                total = Convert.ToString(ah["total"] ?? ""),
                sameCause = Convert.ToString(ah["sameCause"] ?? ""),
                span = Convert.ToString(ah["span"] ?? ""),
                byCause = byCause
            };
        }

        /* The live readings taken when the analysis ran, with each value's own safe band. The
        state field is the server's own verdict per reading (ok / low / high); it is carried
        through rather than recomputed here, so the panel and the analysis cannot disagree
        about which readings were out of band. */
        private static object DetailSnapshot(Newtonsoft.Json.Linq.JObject oj)
        {
            var sn = oj["snapshot"] as Newtonsoft.Json.Linq.JObject;
            if (sn == null) { return null; }
            var vals = new List<object>();
            var arr = sn["values"] as Newtonsoft.Json.Linq.JArray;
            if (arr != null)
            {
                foreach (var v in arr)
                {
                    vals.Add(new
                    {
                        attr = Convert.ToString(v["attr"] ?? ""),
                        value = Convert.ToString(v["value"] ?? ""),
                        state = Convert.ToString(v["state"] ?? ""),
                        minSafe = Convert.ToString(v["minSafe"] ?? ""),
                        maxSafe = Convert.ToString(v["maxSafe"] ?? ""),
                        minFail = Convert.ToString(v["minFail"] ?? ""),
                        avg = Convert.ToString(v["avg"] ?? ""),
                        at = Convert.ToString(v["at"] ?? "")
                    });
                }
            }
            return new
            {
                retrievedAt = Convert.ToString(sn["retrievedAt"] ?? ""),
                ageSec = Convert.ToString(sn["ageFromIncidenceSec"] ?? ""),
                values = vals
            };
        }

        /* The alert card off AlertInputJson: what fired, against which thresholds, and the
        relay states either side of the event.

        hadReset is the field that earns this method. A run made while the alert was still
        open judged it WITHOUT knowing it recovered, and on this data that is exactly why an
        earlier run said NOT_CONFIRMED and a later one said CONFIRMED. Without the flag the
        panel shows two contradictory verdicts and no way to tell which one saw more. */
        private static object DetailCard(string inputJson)
        {
            if (string.IsNullOrEmpty(inputJson)) { return null; }
            try
            {
                var ij = Newtonsoft.Json.Linq.JObject.Parse(inputJson);
                var ac = ij["alertCard"] as Newtonsoft.Json.Linq.JObject;
                var rc = ij["resetCard"] as Newtonsoft.Json.Linq.JObject;

                string resetAt = Convert.ToString(ij["resetTime"] ?? "");
                if (resetAt.Length == 0 && rc != null) { resetAt = Convert.ToString(rc["resetTimestamp"] ?? ""); }

                return new
                {
                    firedAt = ac != null ? Convert.ToString(ac["time"] ?? "") : "",
                    station = ac != null ? Convert.ToString(ac["stationName"] ?? ac["stationCode"] ?? "") : "",
                    description = ac != null ? Convert.ToString(ac["description"] ?? "") : "",
                    resetAt = resetAt,
                    hadReset = (resetAt.Length > 0),
                    trigger = DetailConds(ac, "triggeredConditions"),
                    relays = DetailRelays(ac),
                    relaysAtReset = DetailRelays(rc),
                    triggerAtReset = DetailConds(rc, "triggeredConditions")
                };
            }
            catch (Exception) { return null; }
        }

        private static List<object> DetailRelays(Newtonsoft.Json.Linq.JObject card)
        {
            var outp = new List<object>();
            if (card == null) { return outp; }
            var arr = card["enablingConditions"] as Newtonsoft.Json.Linq.JArray;
            if (arr == null) { return outp; }
            foreach (var c in arr)
            {
                outp.Add(new
                {
                    label = Convert.ToString(c["label"] ?? ""),
                    // "DROP ?" / "PICKUP ?" -- the trailing marker is an encoding artefact on the
                    // wire, not a value, and printing it makes every relay look uncertain.
                    value = Convert.ToString(c["value"] ?? "").Replace("?", "").Trim(),
                    at = Convert.ToString(c["time"] ?? "")
                });
            }
            return outp;
        }

        private static List<object> DetailConds(Newtonsoft.Json.Linq.JObject card, string key)
        {
            var outp = new List<object>();
            if (card == null) { return outp; }
            var arr = card[key] as Newtonsoft.Json.Linq.JArray;
            if (arr == null) { return outp; }
            foreach (var c in arr)
            {
                var ths = new List<object>();
                var ta = c["thresholds"] as Newtonsoft.Json.Linq.JArray;
                if (ta != null)
                {
                    foreach (var t in ta)
                    {
                        ths.Add(new
                        {
                            label = Convert.ToString(t["label"] ?? ""),
                            value = Convert.ToString(t["value"] ?? ""),
                            unit = Convert.ToString(t["unit"] ?? ""),
                            status = Convert.ToString(t["status"] ?? ""),
                            comparison = Convert.ToString(t["comparison"] ?? "")
                        });
                    }
                }
                // An 'ips' style condition carries ONE flat threshold instead of a list. Folded
                // into the same shape here so the view has one thing to render, not two.
                if (ths.Count == 0 && Convert.ToString(c["threshold"] ?? "").Length > 0)
                {
                    ths.Add(new
                    {
                        label = Convert.ToString(c["thresholdLabel"] ?? c["rule"] ?? ""),
                        value = Convert.ToString(c["threshold"] ?? ""),
                        unit = Convert.ToString(c["unit"] ?? ""),
                        status = Convert.ToString(c["status"] ?? ""),
                        comparison = Convert.ToString(c["rule"] ?? "")
                    });
                }
                outp.Add(new
                {
                    type = Convert.ToString(c["type"] ?? ""),
                    label = Convert.ToString(c["paramLabel"] ?? c["paramName"] ?? ""),
                    measured = Convert.ToString(c["measured"] ?? ""),
                    unit = Convert.ToString(c["unit"] ?? ""),
                    at = Convert.ToString(c["time"] ?? ""),
                    status = Convert.ToString(c["status"] ?? ""),
                    thresholds = ths
                });
            }
            return outp;
        }

        // A field off the raw analysis token as text, "" for null/missing. One helper so the
        // projection stays flat instead of a null-check per field.
        private static string DetailStr(Newtonsoft.Json.Linq.JToken it, string name)
        {
            if (it == null) { return ""; }
            var t = it[name];
            if (t == null || t.Type == Newtonsoft.Json.Linq.JTokenType.Null) { return ""; }
            return Convert.ToString(t) ?? "";
        }

        // A UTC/offset stamp -> local "dd/MM/yyyy HH:mm:ss", or "" when it will not parse.
        // The stamps carry a +05:30 offset already; parsing to local keeps one clock on the
        // panel rather than mixing the server offset with the browser's.
        private static string DetailLocal(string stamp)
        {
            if (string.IsNullOrWhiteSpace(stamp)) { return ""; }
            DateTime dt;
            if (DateTime.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture,
                                  System.Globalization.DateTimeStyles.None, out dt))
            { return dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"); }
            return "";
        }

        // Sort key for the projected list. Reads the already-formatted completedAt/createdAt
        // back to a DateTime; the format is fixed (dd/MM/yyyy HH:mm:ss) so this is safe.
        private static DateTime DetailSortStamp(object projected)
        {
            try
            {
                var t = projected.GetType();
                string c = Convert.ToString(t.GetProperty("completedAt").GetValue(projected, null)) ?? "";
                if (c.Length == 0) { c = Convert.ToString(t.GetProperty("createdAt").GetValue(projected, null)) ?? ""; }
                DateTime dt;
                if (DateTime.TryParseExact(c, "dd/MM/yyyy HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out dt))
                { return dt; }
            }
            catch (Exception) { }
            return DateTime.MinValue;
        }


        public ActionResult GetFRSAlertById(int id)
        {
            var mFRSAlert = new Domain.FRSAlert();
            try
            {
                mFRSAlert = GetFRSAlert(id);

            }
            catch (Exception)
            {
                mFRSAlert = new Domain.FRSAlert();
            }
            return Json(mFRSAlert);
        }

        private Domain.FRSAlert GetFRSAlert(int id)
        {

            var mFRSAlert = new Domain.FRSAlert();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("FRSAlert/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFRSAlert = JsonConvert.DeserializeObject<Domain.FRSAlert>(jsonString);
                        if (mFRSAlert == null)
                            mFRSAlert = new Domain.FRSAlert();
                    }
                }
            }
            catch (Exception ex)
            {
                mFRSAlert = new Domain.FRSAlert();
            }

            return mFRSAlert;
        }

        // ------------------------------------------------------------------
        // POST /FRS25/UnifiedAlertAnalysis/VerdictDryRun
        // Dry-run the SERVICE verdict contract against an already-triggered
        // alert. The browser cannot call AnalyzeAlertJson directly because the
        // shared key must never reach client script, so this action forwards
        // the request server-to-server with the key from config -- the same
        // call the alert service will make. The reply carries BOTH the request
        // payload and the verbatim envelope, so accuracy and contract can be
        // checked side by side.
        // Config: AnalyzeJsonUrl, InternalApiKey.
        // ------------------------------------------------------------------
        // NOTE: deliberately uses HttpWebRequest (System.dll) rather than
        // HttpClient. System.Net.Http needs an assembly reference and, on
        // .NET Framework web apps, an assemblyBinding redirect -- a missing
        // redirect stops the app pool at startup ("HTTP 503 The service is
        // unavailable"). HttpWebRequest has no such dependency.

        private static bool LooksLikeJson(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) { return false; }
            string x = t.TrimStart();
            return x.Length > 0 && (x[0] == '{' || x[0] == '[');
        }

        private static string LimitBody(string t)
        {
            if (t == null) { return ""; }
            t = t.Trim();
            return t.Length <= 400 ? t : t.Substring(0, 400) + " ...";
        }

        // minimal JSON string encoder -- no serializer dependency
        private static string JsonStr(string t)
        {
            if (t == null) { return "null"; }
            StringBuilder b = new StringBuilder(t.Length + 16);
            b.Append('"');
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c == '"') { b.Append("\\\""); }
                else if (c == '\\') { b.Append("\\\\"); }
                else if (c == '\n') { b.Append("\\n"); }
                else if (c == '\r') { b.Append("\\r"); }
                else if (c == '\t') { b.Append("\\t"); }
                else if (c < 32) { b.Append("\\u").Append(((int)c).ToString("x4")); }
                else { b.Append(c); }
            }
            b.Append('"');
            return b.ToString();
        }

        [HttpPost]
        public async Task<ActionResult> VerdictDryRun()
        {
            // Without this, IIS httpErrors (existingResponse="Replace") throws away
            // our JSON body on any non-2xx and substitutes its own page -- which is
            // how a plain "not_configured" message surfaced as the opaque IIS text
            // "The service is unavailable." and looked like a dead app pool.
            try { Response.TrySkipIisCustomErrors = true; } catch { }
            string ctxJson;
            using (StreamReader sr = new StreamReader(Request.InputStream, Encoding.UTF8))
            {
                ctxJson = await sr.ReadToEndAsync().ConfigureAwait(false);
            }
            if (string.IsNullOrWhiteSpace(ctxJson))
            {
                Response.StatusCode = 400;
                return Content("{\"dryRun\":\"bad_request\",\"detail\":\"empty context\"}", "application/json");
            }

            string url = ConfigurationManager.AppSettings["AnalyzeJsonUrl"];
            string key = ConfigurationManager.AppSettings["InternalApiKey"];
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key))
            {
                string missing = string.IsNullOrEmpty(url)
                    ? (string.IsNullOrEmpty(key) ? "AnalyzeJsonUrl and InternalApiKey are" : "AnalyzeJsonUrl is")
                    : "InternalApiKey is";
                Response.StatusCode = 503;
                return Content("{\"dryRun\":\"not_configured\",\"detail\":"
                    + JsonStr(missing + " missing from E7FRSAdvance web.config appSettings.")
                    + ",\"hint\":"
                    + JsonStr("Add AnalyzeJsonUrl (the AnalyzeAlertJson URL on E7FRSAdvance) and InternalApiKey (identical to the value on E7FRSAdvance), then retry.")
                    + "}", "application/json");
            }

            int timeoutSec;
            if (!int.TryParse(ConfigurationManager.AppSettings["VerdictDryRunTimeoutSec"], out timeoutSec) || timeoutSec <= 0)
            {
                timeoutSec = 180;
            }

            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            string body = null;
            int status = 0;
            string failNote = null;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Accept = "application/json";
                req.Timeout = timeoutSec * 1000;
                req.ReadWriteTimeout = timeoutSec * 1000;
                req.Headers.Add("X-Internal-Api-Key", key);
                req.Headers.Add("X-Request-Id", "dryrun-" + Guid.NewGuid().ToString("N").Substring(0, 12));

                byte[] payload = Encoding.UTF8.GetBytes(ctxJson);
                req.ContentLength = payload.Length;
                using (Stream rs = await req.GetRequestStreamAsync().ConfigureAwait(false))
                {
                    await rs.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                }

                using (HttpWebResponse res = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                using (StreamReader rd = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                {
                    status = (int)res.StatusCode;
                    body = await rd.ReadToEndAsync().ConfigureAwait(false);
                }
            }
            catch (WebException wex)
            {
                // a non-2xx still carries the envelope we want to show
                HttpWebResponse er = wex.Response as HttpWebResponse;
                if (er != null)
                {
                    status = (int)er.StatusCode;
                    try
                    {
                        using (StreamReader rd = new StreamReader(er.GetResponseStream(), Encoding.UTF8))
                        {
                            body = rd.ReadToEnd();
                        }
                    }
                    catch { body = null; }
                    er.Close();
                }
                else
                {
                    failNote = wex.Status.ToString();
                }
                System.Diagnostics.Trace.TraceError("[VerdictDryRun] " + wex.ToString());
            }
            catch (Exception ex)
            {
                sw.Stop();
                System.Diagnostics.Trace.TraceError("[VerdictDryRun] " + ex.ToString());
                Response.StatusCode = 502;
                return Content("{\"dryRun\":\"unreachable\",\"detail\":\"verdict endpoint could not be reached\"}", "application/json");
            }
            if (status == 0)
            {
                sw.Stop();
                Response.StatusCode = 502;
                return Content("{\"dryRun\":\"unreachable\",\"detail\":\"verdict endpoint could not be reached ("
                    + (failNote ?? "no response") + ")\"}", "application/json");
            }
            sw.Stop();

            // The upstream body is only JSON when the verdict endpoint itself
            // answered. An infrastructure reply (IIS "The service is
            // unavailable.", an HTML error page, a proxy notice) is NOT JSON and
            // must be encoded as a string -- splicing it raw produced invalid
            // JSON, which the browser could not parse, hiding the real cause.
            bool upstreamJson = LooksLikeJson(body);
            bool upstreamOk = status >= 200 && status < 300;
            string state = (upstreamOk && upstreamJson) ? "ok" : "upstream_error";

            StringBuilder outp = new StringBuilder();
            outp.Append("{\"dryRun\":\"").Append(state).Append("\"");
            outp.Append(",\"httpStatus\":").Append(status);
            outp.Append(",\"roundTripMs\":").Append(sw.ElapsedMilliseconds);
            if (!upstreamJson)
            {
                outp.Append(",\"detail\":").Append(JsonStr(
                    "The verdict endpoint did not return JSON (HTTP " + status +
                    "). This is an infrastructure reply, not a verdict."));
                outp.Append(",\"hint\":").Append(JsonStr(
                    status == 503
                        ? "HTTP 503 with this body is IIS on the E7FRSAdvance side: its application pool is stopped or failing to start. Start that pool and browse the AnalyzeAlertJson URL directly."
                        : "Check AnalyzeJsonUrl points at the AnalyzeAlertJson action on E7FRSAdvance and that the app is running."));
                outp.Append(",\"rawBody\":").Append(JsonStr(LimitBody(body)));
            }
            outp.Append(",\"request\":").Append(ctxJson);
            outp.Append(",\"response\":").Append(upstreamJson ? body : "null");
            outp.Append("}");
            return Content(outp.ToString(), "application/json");
        }

        public List<Domain.AlertAudit> GetAlertAudit(List<int> ids)
        {
            var mAlertAudits = new List<Domain.AlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var jsonStr = JsonConvert.SerializeObject(ids);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AlertAudit/GetByAlertId"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertAudits = JsonConvert.DeserializeObject<List<Domain.AlertAudit>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
                mAlertAudits = new List<Domain.AlertAudit>();
            }
            return mAlertAudits;
        }

        private List<AlertAudit> GetAlertAudit(DateTime FromDate, DateTime ToDate)
        {
            List<AlertAudit> mAlertAudits = new List<AlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var fromDate = FromDate.ToShortDateString().Replace("/", "-");
                    var toDate = ToDate.ToShortDateString().Replace("/", "-");

                    var response = hcf.client.GetAsync($"AlertAudit/GetByAlertDate/StartDate/{fromDate}/EndDate/{toDate}").Result;
                    string jsonStr = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertAudits = JsonConvert.DeserializeObject<List<AlertAudit>>(jsonStr);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mAlertAudits;
        }
    }
}