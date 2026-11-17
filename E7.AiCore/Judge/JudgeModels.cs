using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeModels -- every request, intermediate and response class of the judge.
    //
    // CHANGE HERE when a field is added, renamed or removed. The JSON a caller sees is written
    // in JudgeEnvelope.cs (response) and read in JudgeRequestReader.cs (request): a new field
    // goes here first, then into one of those two files.
    //
    // Ported from AlertJudge v1.1.2.0 Contracts/Models.cs + Contracts/Flow.cs (same members,
    // public fields instead of auto-properties, Newtonsoft JToken instead of JsonElement).
    // New in 2.0: JudgeCardCondition, JudgeCardThreshold, JudgeConditionResult, JudgeLiveResult.
    // ============================================================================================

    // ---------------------------------------------------------------------------------------------
    // Request
    // ---------------------------------------------------------------------------------------------

    internal sealed class JudgeRequest
    {
        public long AlertId;

        /// <summary>The whole body, as AnalyzeAlertJson receives it (alertId, causeCode, station, ..., alertCard).</summary>
        public JObject Ctx;

        /// <summary>ctx.alertCard (or the old AlertJudge body's "card") with blanks filled from the ctx fields.</summary>
        public JToken Card;

        /// <summary>The card exactly as sent (what the AI and the trace see).</summary>
        public JToken CardRaw;

        public JudgeOptions Options = new JudgeOptions();

        public string RequestId;

        /// <summary>
        /// 1.0.203.0 -- the outcome being built, set by RunAsync as soon as it starts. When a step
        /// throws, HandleAsync still has the card and the trace lines produced so far, so the
        /// failed judgement gets its log block instead of one ERROR line. Never serialised.
        /// </summary>
        public JudgeOutcome Partial;
    }

    internal sealed class JudgeOptions
    {
        /// <summary>"full" (rules + history + live re-check + AI) or "rules" (card + catalog only).</summary>
        public string Mode = "full";

        public bool UseAi = true;

        /// <summary>Provider alias (quantum10 = the configured model, quantum11 = the flash model).</summary>
        public string Engine;

        /// <summary>Adjacent / family track asset ids for the TC adjacency check.</summary>
        public List<int> FamilyTrackIds = new List<int>();

        /// <summary>Adds the full five-stage trace to the response (for the Log button).</summary>
        public bool Trace;

        /// <summary>Kept from AlertJudge 1.1.2.0; read nowhere (it never was).</summary>
        public string Sensitivity = "medium";
    }

    // ---------------------------------------------------------------------------------------------
    // Normalised card
    // ---------------------------------------------------------------------------------------------

    internal sealed class JudgeCard
    {
        public long AlertId;
        public string CauseCode = "";
        public int? CauseId;
        public string CauseTitle;
        public int SiteId;
        public int AssetId;
        public string AssetName;
        public string AssetType = "";
        public int AssetTypeId;
        public string AlertType = "FAILURE";
        public DateTimeOffset TriggeredAt;
        public DateTimeOffset? ResetAt;
        public string Provider;
        public string StationName;

        public Dictionary<string, double> Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Relays = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> Thresholds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> AttributeIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DateTimeOffset> ValueTimes = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Comparisons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<string> EnablingConditions = new List<string>();
        public List<string> TriggeredConditions = new List<string>();
        public string PrimaryMetric;
        public JToken Raw;

        // ---- new in 2.0: the card's conditions as structured objects, for the live re-check ----
        public List<JudgeCardCondition> Conditions = new List<JudgeCardCondition>();

        /// <summary>card.meta.conditionLogic: "AND" (default) or "OR" -- how the triggered conditions combine.</summary>
        public string ConditionLogic = "AND";

        /// <summary>card.meta.checkWindow, in seconds (null when the card does not say).</summary>
        public int? CheckWindowSec;

        public string CheckWindowText;

        // ---- new in 2.7: the RESET card's conditions, parsed the same way -------------------------
        //
        // The alert service writes a second card when the alert clears, carrying the same conditions
        // re-measured at the reset instant. For 1137362 that is ITC RELAY END(mA) = 148.61 at set and
        // 332.12 at reset against the same thresholds (80% Avg 261.26, Min Safe 210) -- an excursion
        // and a recovery, both recorded by the service at the two ends of the event.
        //
        // The judge read only the reset TIMESTAMP and threw the measurements away, which is why a
        // closed alert that proves itself from its own two cards could still be discarded for want of
        // historian data. See JudgeResetCheck.
        public List<JudgeCardCondition> ResetConditions = new List<JudgeCardCondition>();

        /// <summary>True once a reset card with at least one usable condition has been parsed.</summary>
        public bool HasResetCard;
    }

    /// <summary>One condition exactly as the card states it. The card's measured value is kept for display only.</summary>
    internal sealed class JudgeCardCondition
    {
        /// <summary>relay | analog | derived</summary>
        public string Kind = "analog";

        /// <summary>enabling | main | derived | gate</summary>
        public string Role = "main";

        public string Label = "";

        /// <summary>Label without the unit: "ITC RELAY END(mA)" -> "ITC RELAY END".</summary>
        public string Name = "";

        public string Unit;
        public string CardValueText;
        public double? CardValue;
        public DateTimeOffset? CardTime;

        /// <summary>Relay conditions: UP or DN.</summary>
        public string WantState;

        public List<JudgeCardThreshold> Thresholds = new List<JudgeCardThreshold>();

        /// <summary>The card's own formula text for a derived value ("IBALST(mA) = ITC FEED END(mA) - ITC RELAY END(mA)").</summary>
        public string Formula;

        /// <summary>
        /// 2.2 -- the card's own combining logic for this condition: "OR" when it is one alternative
        /// of a group, null/"AND" otherwise. AlertV3 already writes this (SignalAlertProcessor's
        /// SigCardMain(..., logic: "OR", group: "g1") -> AlertCardJsonBuilder.Main); the judge simply
        /// read neither field until now and evaluated every OR group as AND.
        /// </summary>
        public string Logic;

        /// <summary>2.2 -- group key ("g1"). Conditions sharing a group combine by their Logic; groups AND together.</summary>
        public string Group;
    }

    internal sealed class JudgeCardThreshold
    {
        public string Label = "";
        public double Value;
        public string Comparison = "";
        public string Status;
    }

    internal sealed class JudgeCardSummary
    {
        public bool Valid;
        public List<string> Problems = new List<string>();
        public string CauseCode;
        public string AssetType;
        public string AlertType;
        public DateTimeOffset? TriggeredAt;
        public int ValueCount;
        public int RelayCount;
    }

    // ---------------------------------------------------------------------------------------------
    // Verifier (catalog gates) -- unchanged from AlertJudge
    // ---------------------------------------------------------------------------------------------

    internal sealed class JudgeCardCatalogMatch
    {
        /// <summary>
        /// 2.4 -- TRUE when not one catalogue attribute could be lined up against a card condition:
        /// no agreements AND no disagreements. That is a naming gap on our side, not evidence that
        /// the card is wrong, and the policy must not suppress a FAILURE for it.
        /// </summary>
        public bool NothingRecognised;


        public bool Match;
        public List<string> Agree = new List<string>();
        public List<string> Disagree = new List<string>();
        public List<string> NotOnCard = new List<string>();
    }

    internal sealed class JudgeGate
    {
        public string Name = "";

        /// <summary>PASS | FAIL | NO_DATA | SKIPPED</summary>
        public string Result = "NO_DATA";

        public string Detail;
    }

    internal struct JudgeSample
    {
        public DateTimeOffset Ts;
        public double Value;

        public JudgeSample(DateTimeOffset ts, double value)
        {
            Ts = ts;
            Value = value;
        }
    }

    /// <summary>State of one tag across the frame: seeded at frame start (carry-forward), then transitions.</summary>
    internal sealed class JudgeStepSeries
    {
        public string Attribute = "";
        public int Attid;
        public long TagId;
        public char RoleType;
        public double? StateAtStart;
        public DateTimeOffset? StateAtStartTs;

        /// <summary>history | card | none</summary>
        public string StateSource = "none";

        public List<JudgeSample> Transitions = new List<JudgeSample>();

        public double? Last
        {
            get
            {
                if (Transitions.Count > 0)
                {
                    return Transitions[Transitions.Count - 1].Value;
                }

                return StateAtStart;
            }
        }

        /// <summary>Value in effect at instant t (step function).</summary>
        public double? At(DateTimeOffset t)
        {
            double? v = StateAtStart;
            foreach (JudgeSample s in Transitions)
            {
                if (s.Ts <= t)
                {
                    v = s.Value;
                }
                else
                {
                    break;
                }
            }

            return v;
        }

        /// <summary>Every distinct value in effect during [from, to]: the carried-forward state plus each transition inside.</summary>
        public List<JudgeSample> Segments(DateTimeOffset from, DateTimeOffset to)
        {
            List<JudgeSample> list = new List<JudgeSample>();
            double? v0 = At(from);
            if (v0 != null)
            {
                list.Add(new JudgeSample(from, v0.Value));
            }

            foreach (JudgeSample s in Transitions)
            {
                if (s.Ts > from && s.Ts <= to)
                {
                    list.Add(s);
                }
            }

            return list;
        }
    }

    internal sealed class JudgeAdjacency
    {
        /// <summary>PASS | FAIL | SKIPPED</summary>
        public string Result = "SKIPPED";

        public string Detail;
        public List<int> FamilyTrackIds = new List<int>();
        public List<string> Findings = new List<string>();
    }

    internal sealed class JudgeVerifierOutcome
    {
        /// <summary>1.0.208.0 -- the attributes whose tag was not found, for the no-data reason.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public List<string> NotFound = new List<string>();

        /// <summary>1.0.208.0 -- the card's printed (value, time) pairs validated against history.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public JudgeCardDataResult CardData;

        public string AlertState = "SET_ONLY";
        public DateTimeOffset SetTime;
        public DateTimeOffset? ResetTime;
        public string EvalMode = "";
        public bool ResetBeforeSustain;
        public double? HeldForSec;
        public JudgeCardCatalogMatch CardMatch;
        /// <summary>2.2 -- the cause's trigger is decided outside RDPMS (field-device flag).</summary>
        public bool ExternallyDetermined;

        public JudgeGate Relay = NewGate("DataLogger relays");
        public JudgeGate Analog = NewGate("RDPMS analog");
        public JudgeAdjacency Adjacency = new JudgeAdjacency();

        /// <summary>PROCEED | SUPPRESSED | NO_DATA</summary>
        public string FlowResult = "NO_DATA";

        public string CauseCode = "";
        public int Seq;
        public string AlertType = "";

        /// <summary>CONFIRMED | NOT_CONFIRMED | NO_RULE</summary>
        public string Verdict = "NOT_CONFIRMED";

        public bool Coverage;
        public List<string> CoverageNotes = new List<string>();
        public int Confidence;
        public string ReclassifiedCause;
        public string Stage1;
        public int SustainSec;
        public DateTimeOffset WindowFrom;
        public DateTimeOffset WindowTo;
        public List<JudgeAttrResult> Attrs = new List<JudgeAttrResult>();
        public List<string> Fired = new List<string>();
        public string RuleRaw;
        public string VerdictLogic;

        private static JudgeGate NewGate(string name)
        {
            JudgeGate g = new JudgeGate();
            g.Name = name;
            return g;
        }
    }

    internal sealed class JudgeAttrResult
    {
        public string Name = "";
        public int Attid;
        public string Class = "";
        public string Gate;

        /// <summary>HELD | LAPSED | SKIPPED | NO_DATA | UNSUPPORTED</summary>
        public string Status = "COVERAGE";

        public string Detail;
        public double? Threshold;
        public string ThresholdFormula;
        public double? Observed;
        public int Samples;
        public long? TagId;
        public double? StateAtSet;
        public DateTimeOffset? StateAtSetTs;
        public string StateSource;
        public double? HeldForSec;
        public int TransitionsInFrame;
    }

    // ---------------------------------------------------------------------------------------------
    // Evidence (MCP)
    // ---------------------------------------------------------------------------------------------

    internal sealed class JudgeEvidenceBundle
    {
        /// <summary>2.3 -- attids skipped because they are DERIVED (computed, no sensor).
        /// Distinct from TagPresent==false, which means the asset really has no such tag.</summary>
        public HashSet<int> Derived = new HashSet<int>();

        /// <summary>1.0.208.0 -- "attid 'name'" for every attribute whose tag was not found.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public List<string> NotFound = new List<string>();

        public DateTimeOffset From;
        public DateTimeOffset To;
        public Dictionary<int, JudgeStepSeries> Steps = new Dictionary<int, JudgeStepSeries>();
        public Dictionary<int, JudgeStepSeries> AdjacentTpr = new Dictionary<int, JudgeStepSeries>();
        public bool Degraded;
        public List<string> DegradedInputs = new List<string>();
        public Dictionary<string, JudgeTagSeries> Series = new Dictionary<string, JudgeTagSeries>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, bool> TagPresent = new Dictionary<int, bool>();
        public Dictionary<int, double> BaselineAvg = new Dictionary<int, double>();
        public JToken FrsAlert;
    }

    internal sealed class JudgeTagSeries
    {
        public string Attribute = "";
        public int Attid;
        public long TagId;
        public char RoleType;
        public List<JudgeSample> Samples = new List<JudgeSample>();

        public double? Min
        {
            get
            {
                if (Samples.Count == 0)
                {
                    return null;
                }

                return Samples.Min(s => s.Value);
            }
        }

        public double? Max
        {
            get
            {
                if (Samples.Count == 0)
                {
                    return null;
                }

                return Samples.Max(s => s.Value);
            }
        }

        public double? Avg
        {
            get
            {
                if (Samples.Count == 0)
                {
                    return null;
                }

                return Samples.Average(s => s.Value);
            }
        }

        public double? Last
        {
            get
            {
                if (Samples.Count == 0)
                {
                    return null;
                }

                return Samples[Samples.Count - 1].Value;
            }
        }

        public double? First
        {
            get
            {
                if (Samples.Count == 0)
                {
                    return null;
                }

                return Samples[0].Value;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Live re-check (new in 2.0)
    // ---------------------------------------------------------------------------------------------

    internal sealed class JudgeConditionResult
    {
        public JudgeCardCondition Condition;

        /// <summary>
        /// HELD | LAPSED | NO_DATA | UNSUPPORTED | IGNORED | UNMAPPED
        /// IGNORED (listed in Live.IgnoreConditions) and UNMAPPED (2.3 -- no tag for the attribute on
        /// this asset) are reported but NOT counted towards the combined live result.
        /// </summary>
        public string Status = "NO_DATA";

        public int Attid;
        public long? TagId;

        /// <summary>tag | formula | none -- where the live value came from.</summary>
        public string Source = "none";

        public string Formula;
        public double? LiveAtSet;
        public double? LiveMin;
        public double? LiveMax;
        public double? LiveLast;
        public int Samples;
        public double? HeldForSec;
        public DateTimeOffset? FirstBreakAt;
        public double? FirstBreakValue;
        public string Detail;

        /// <summary>
        /// 1.0.190.0 -- the ONE number and direction the breach was decided on, collapsed from the
        /// card's clauses by JudgeUtil.EffectiveThreshold (MAX for a low rule, MIN for a high one).
        /// Carried into the AI pack so the model cannot pick the wrong half of an OR: on 1137362 it
        /// read the "&lt; 210" clause and called 217.8 mA a recovery when the effective threshold was
        /// 261.26. Null when the card's clauses do not collapse to one number (a band).
        /// </summary>
        public double? EffThreshold;

        /// <summary>1.0.190.0 -- "&lt;" or "&gt;", the single direction that goes with EffThreshold.</summary>
        public string EffDirection;

        /// <summary>
        /// 1.0.191.0 -- true when Live.FailureOnset located the condition's onset INSIDE the check
        /// window rather than at its first instant, so the sustain-from-set result for this same
        /// attribute in attrsAtSet is superseded and must be marked as such in the AI pack.
        ///
        /// On 1263751 the pack carried both, with no precedence between them:
        ///   verify:attid2  ITC Relay End: LAPSED  (sustain from set, value 108.801 at set)
        ///   live:tag       ITC RELAY END: HELD    (onset at 08:38:35, 5s in, held to window end)
        /// The AI quoted the LAPSED one and suppressed on it. It was not being careless: nothing in
        /// the pack said which of the two governs.
        /// </summary>
        public bool OnsetUsed;
    }

    internal sealed class JudgeLiveResult
    {
        /// <summary>PASS | FAIL | NO_DATA | UNSUPPORTED | SKIPPED</summary>
        public string Result = "SKIPPED";

        public string Detail;
        public DateTimeOffset WindowFrom;
        public DateTimeOffset WindowTo;
        public int WindowSec;

        /// <summary>card | catalog | default | fixed</summary>
        public string WindowSource = "";

        public string ConditionLogic = "AND";
        public double WaitedSec;
        public List<JudgeConditionResult> Conditions = new List<JudgeConditionResult>();

        /// <summary>
        /// 2.3 -- TRUE when the re-check obtained no independent evidence either way: nothing HELD and
        /// nothing LAPSED, because every condition was unmapped, empty or not re-checkable.
        /// This is NOT the same finding as "the live data contradicts the card" (Result == "FAIL"),
        /// and the two must never share a verdict: the first says the judge looked and found nothing
        /// to look at, the second says it looked and the card was wrong.
        /// </summary>
        public bool NoEvidence;

        /// <summary>2.3 -- how many conditions actually held on independent data.</summary>
        public int HeldCount;

        /// <summary>2.3 -- how many conditions were re-checkable at all (counted, not IGNORED/UNMAPPED).</summary>
        public int CountedCount;
    }

    // ---------------------------------------------------------------------------------------------
    // Reset-card corroboration (2.7)
    //
    // Evidence the judge already had and was not reading: the alert service's own set and reset
    // cards, which measure the same conditions at the two ends of the event. This needs no historian
    // and no tag mapping, so it works on an alert from any date -- which is exactly the case where
    // the live re-check cannot help.
    // ---------------------------------------------------------------------------------------------

    internal sealed class JudgeResetCondResult
    {
        public JudgeCardCondition Condition;

        /// <summary>
        /// CONFIRMED_RECOVERED -- the set value breached the card's own rule and the reset value
        ///                        no longer does. The excursion happened and cleared.
        /// CONFIRMED_PERSISTS  -- the set value breached and the reset value still breaches. The
        ///                        alert was real; the reset was raised for some other reason.
        /// CARD_CONTRADICTS    -- the set value does NOT breach the card's own threshold. The card
        ///                        argues against itself, and no outside data is needed to see it.
        /// NO_RESET_ROW        -- the reset card carries no measurement for this condition.
        /// NOT_COMPARABLE      -- no numeric value or no threshold to compare against.
        /// IGNORED             -- listed in Live.IgnoreConditions; reported, not counted.
        /// </summary>
        public string Status = "NOT_COMPARABLE";

        public double? SetValue;
        public double? ResetValue;

        /// <summary>The rule as the SET card stated it, e.g. "&lt; 261.26 (80% Avg) OR &lt; 210 (Min Safe)".</summary>
        public string Rule;

        public string Detail;
    }

    internal sealed class JudgeResetResult
    {
        /// <summary>
        /// CONFIRMED    -- at least one condition confirmed and none contradicted.
        /// CONTRADICTED -- at least one condition's set value does not breach its own threshold.
        /// INCONCLUSIVE -- nothing comparable either way.
        /// SKIPPED      -- no reset card, or Live.UseResetCard = false.
        /// </summary>
        public string Result = "SKIPPED";

        public string Detail;
        public DateTimeOffset? ResetAt;
        public int ConfirmedCount;
        public int ContradictedCount;
        public int ComparedCount;
        public List<JudgeResetCondResult> Conditions = new List<JudgeResetCondResult>();
    }

    // ---------------------------------------------------------------------------------------------
    // AI
    // ---------------------------------------------------------------------------------------------

    internal sealed class JudgeEvidenceItem
    {
        /// <summary>record | fetched | computed</summary>
        public string Tag = "record";

        public string Text = "";
        public string Source;

        public static JudgeEvidenceItem Of(string tag, string text, string source)
        {
            JudgeEvidenceItem e = new JudgeEvidenceItem();
            e.Tag = tag;
            e.Text = text;
            e.Source = source;
            return e;
        }
    }

    internal sealed class JudgeAiInputSummary
    {
        public string Model = "";

        /// <summary>
        /// 1.0.200.0 -- the vendor the call was actually SENT to (anthropic | openai | deepseek),
        /// as resolved from the AiModelConfig row. meta.provider carries what the caller ASKED for,
        /// which is a different thing: a service caller asking for QUANTUM while the table's default
        /// is a nexus* row produced envelopes reading provider=QUANTUM with aiModel=claude-haiku-5-5,
        /// and that pair reads as a contradiction rather than as the override it is.
        /// </summary>
        public string Provider = "";
        public int SystemChars;
        public int UserChars;
        public List<string> Sections = new List<string>();
        public int SeriesSent;
        public int SamplesSent;
    }

    internal sealed class JudgeAiVerdict
    {
        public bool Ran;

        /// <summary>PASS | SUPPRESS</summary>
        public string Recommendation;

        /// <summary>New in 2.0: the AI's answer to "is the logic correct?" and "is the data correct?".</summary>
        public bool? LogicCorrect;

        public bool? DataCorrect;
        public string Verdict;
        public int Confidence;
        public string Headline;
        public string LikelyCause;
        public string Category;
        public List<JudgeEvidenceItem> Evidence = new List<JudgeEvidenceItem>();
        public string RecommendedAction;
        public string Caveats;
        public List<string> DegradedInputs = new List<string>();
        public string Error;
        public JToken RawJson;
        public JudgeAiInputSummary Input;
    }

    // ---------------------------------------------------------------------------------------------
    // Policy + trace
    // ---------------------------------------------------------------------------------------------

    internal sealed class JudgePolicyOutcome
    {
        /// <summary>PASS | SUPPRESSED -- the binary outcome the alert pipeline acts on.</summary>
        public string FinalDecision = "SUPPRESSED";

        /// <summary>ACCEPT | DISCARD | REVIEW</summary>
        public string Decision = "REVIEW";

        public string DecidedBy = "";
        public int Confidence;
        public List<string> Reasons = new List<string>();
    }

    // ---- prior human decisions on this asset + cause (2.1) ----

    /// <summary>
    /// One dbo.PendingAlertApproval row a PERSON discarded (Status=2, UpdatedBy NOT NULL).
    /// System-driven discards (the sibling sweep, the active-conflict auto-discard) are never
    /// loaded -- they carry no human judgement. WasValid is the reviewer's own answer to "was the
    /// alert valid?" (Patch 47): false = they said it was not, the only value that suppresses.
    /// </summary>
    internal sealed class JudgePriorDiscard
    {
        public int Id;
        public string CauseCode;
        public int AlertType;
        public DateTimeOffset? RaisedAt;
        public DateTimeOffset? DiscardedAt;
        public int DiscardedBy;
        public bool? WasValid;
        public string Remark;
        public string AiVerdict;
    }

    /// <summary>
    /// The lookup result. Checked=false means the question could not be asked (lookup disabled, no
    /// connection string, Patch 47 not applied, SQL down) -- the judgement then runs exactly as it
    /// did before this step existed. RejectedBy is the most recent row whose reviewer said the
    /// alert was NOT valid; non-null is what makes JudgePolicy suppress.
    /// </summary>
    internal sealed class JudgePriorDiscardResult
    {
        public bool Checked;
        public int LookbackDays;
        public string Error;
        public List<JudgePriorDiscard> Rows = new List<JudgePriorDiscard>();
        public JudgePriorDiscard RejectedBy;
    }

    internal sealed class JudgeTrace
    {
        public JudgeCardSummary Card;
        public JudgeVerifierOutcome Verifier;
        public JudgeEvidenceBundle EvidenceBundle;
        public JudgeLiveResult Live;
        public JudgeAiVerdict Ai;
        public JudgePriorDiscardResult PriorDiscards;
        public JudgePolicyOutcome Policy;
        public List<string> Log = new List<string>();
    }

    /// <summary>Everything one judgement produced; JudgeEnvelope turns it into the response JSON.</summary>
    internal sealed class JudgeOutcome
    {
        public long AlertId;
        public JudgeCard Card;
        public JudgePolicyOutcome Policy;
        public JudgeVerifierOutcome Verifier;
        public JudgeLiveResult Live;

        /// <summary>2.7 -- set and reset cards compared against each other. Null when mode != full.</summary>
        public JudgeResetResult Reset;

        public JudgeAiVerdict Ai;
        public JudgePriorDiscardResult PriorDiscards;

        /// <summary>1.0.202.0 -- maintenance window + similar past cases. Null when the card was unreadable.</summary>
        public JudgeLearnedResult Learned;

        public string LikelyCause;
        public string Category;
        public List<string> RulesFired = new List<string>();
        public List<JudgeEvidenceItem> Evidence = new List<JudgeEvidenceItem>();
        public string Caveats;
        public bool Degraded;
        public string AuditId;
        public string Mode = "";
        public string Engine;
        public JudgeTrace Trace;
    }

    /// <summary>What the web action passes in. The action reads nothing else.</summary>
    public sealed class JudgeHttpRequest
    {
        public string ConfiguredKey;
        public string SuppliedKey;
        public string RequestId;
        public long ContentLength;
        public string Body;
    }

    /// <summary>What the web action sends back: HTTP status, JSON body, optional audit id header.</summary>
    public sealed class JudgeHttpResult
    {
        public int HttpStatus = 200;
        public string Json = "{}";
        public string AuditId;
    }
}
