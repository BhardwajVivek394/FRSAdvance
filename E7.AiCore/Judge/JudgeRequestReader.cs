using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeRequestReader -- turns the HTTP body into a JudgeRequest, or says why it cannot.
    //
    // CHANGE HERE to change what counts as a valid request or which fields are required.
    //
    // Two body shapes are accepted:
    //   (1) the AnalyzeAlertJson context:  { alertId, causeCode, station, siteId, assetId, time, resetTime,
    //       provider, alertCard: {...}, judge: { mode, useAi, engine, familyTrackIds, trace } }
    //   (2) the old AlertJudge body:       { alertId, card: {...}, options: { useAi, engine, familyTrackIds } }
    // The card is the source the rules read; the top-level fields only fill blanks the card does not carry.
    // ============================================================================================
    internal static class JudgeRequestReader
    {
        internal static JudgeRequest Read(string body, string headerRequestId, out string badReason)
        {
            badReason = null;
            if (body == null)
            {
                badReason = "empty body";
                return null;
            }

            JToken root;
            try
            {
                root = JudgeUtil.Parse(body);
            }
            catch (Exception ex)
            {
                badReason = "body is not JSON: " + ex.Message;
                return null;
            }

            if (!JudgeUtil.IsObject(root))
            {
                badReason = "body must be a JSON object";
                return null;
            }

            JObject ctx = (JObject)root;
            JudgeRequest req = new JudgeRequest();
            req.Ctx = ctx;

            // scalar fields must be scalars (same rule as AnalyzeAlertJson's ValidateCtxShape)
            foreach (string k in new[] { "alertId", "causeCode", "assetName", "station", "time", "resetTime", "alertType", "description", "siteId", "assetId", "provider" })
            {
                JToken t = ctx[k];
                if (t == null || t.Type == JTokenType.Null)
                {
                    continue;
                }

                if (t.Type == JTokenType.Object || t.Type == JTokenType.Array)
                {
                    badReason = "ctx." + k + " must be a scalar";
                    return null;
                }

                if (t.ToString().Length > 2000)
                {
                    badReason = "ctx." + k + " too long (max 2000)";
                    return null;
                }
            }

            JToken card = ctx["alertCard"];
            if (card == null || card.Type == JTokenType.Null)
            {
                card = ctx["card"];
            }

            if (card != null && card.Type != JTokenType.Null && card.Type != JTokenType.Object)
            {
                badReason = "ctx.alertCard must be an object";
                return null;
            }

            long alertId = 0;
            JToken idTok = ctx["alertId"];
            if (idTok != null && idTok.Type != JTokenType.Null)
            {
                long.TryParse(idTok.ToString().Trim(), out alertId);
            }

            if (alertId == 0 && JudgeUtil.IsObject(card))
            {
                int? fromCard = JudgeUtil.Int(card, "alertId", "AlertId", "id");
                if (fromCard != null)
                {
                    alertId = fromCard.Value;
                }
            }

            if ((card == null || card.Type == JTokenType.Null) && alertId == 0)
            {
                badReason = "alertCard (or card) and alertId both missing";
                return null;
            }

            req.AlertId = alertId;
            req.CardRaw = card;
            req.Card = JudgeUtil.IsObject(card) ? FillCardFromCtx((JObject)card.DeepClone(), ctx) : card;

            // options: "judge" (new) or "options" (old)
            JObject opt = ctx["judge"] as JObject ?? ctx["options"] as JObject;
            JudgeOptions o = req.Options;
            if (opt != null)
            {
                string mode = JudgeUtil.Str(opt, "mode");
                if (!string.IsNullOrWhiteSpace(mode))
                {
                    o.Mode = mode.Trim().ToLowerInvariant() == "rules" ? "rules" : "full";
                }

                JToken useAi = opt["useAi"];
                if (useAi != null && useAi.Type == JTokenType.Boolean)
                {
                    o.UseAi = useAi.Value<bool>();
                }
                else if (useAi != null && useAi.Type == JTokenType.String)
                {
                    bool b;
                    if (bool.TryParse((string)useAi, out b))
                    {
                        o.UseAi = b;
                    }
                }

                o.Engine = JudgeUtil.Str(opt, "engine");
                o.Sensitivity = JudgeUtil.Str(opt, "sensitivity") ?? o.Sensitivity;
                JToken trace = opt["trace"];
                if (trace != null && trace.Type == JTokenType.Boolean)
                {
                    o.Trace = trace.Value<bool>();
                }

                JToken fam = opt["familyTrackIds"];
                if (JudgeUtil.IsArray(fam))
                {
                    foreach (JToken t in fam)
                    {
                        int id;
                        if (JudgeUtil.TryInt32(t, out id) || (t.Type == JTokenType.String && int.TryParse((string)t, out id)))
                        {
                            o.FamilyTrackIds.Add(id);
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(o.Engine))
            {
                o.Engine = JudgeUtil.Str(ctx, "provider");
            }

            string rid = headerRequestId;
            if (string.IsNullOrEmpty(rid))
            {
                rid = JudgeUtil.Str(ctx, "requestId");
            }

            if (!string.IsNullOrEmpty(rid) && rid.Length > 128)
            {
                rid = null;
            }

            req.RequestId = !string.IsNullOrEmpty(rid) ? rid : alertId + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            return req;
        }

        /// <summary>The AnalyzeAlertJson top-level fields fill blanks on the card; a card value always wins.</summary>
        private static JObject FillCardFromCtx(JObject card, JObject ctx)
        {
            Fill(card, ctx, "causeCode", "causeCode", "alertCode", "CauseCode");
            Fill(card, ctx, "alertType", "alertType", "AlertType");
            Fill(card, ctx, "assetName", "assetName", "AssetName");
            Fill(card, ctx, "siteId", "siteId", "SiteId", "stationId");
            Fill(card, ctx, "assetId", "assetId", "AssetId");
            Fill(card, ctx, "time", "time", "triggeredAt", "alertTime", "SetTime");
            Fill(card, ctx, "resetTime", "resetAt", "resetAt", "resetTimestamp");
            Fill(card, ctx, "station", "stationName", "stationName", "station");
            Fill(card, ctx, "assetType", "assetType", "AssetType");
            Fill(card, ctx, "provider", "provider", "Provider");

            // 2.7 -- AND THE RESET CARD ITSELF, not only its timestamp.
            //
            // This method copied ctx.resetTime into card.resetAt and stopped there, so the judge
            // knew an alert HAD reset but never saw what was measured at the reset. The alert body
            // carries resetCard as a sibling of alertCard:
            //
            //   { alertId, causeCode, ..., alertCard: {...}, resetTime: "...", resetCard: {...} }
            //
            // and JudgeCardParser looks for resetCard on the CARD. It was never put there, so every
            // reset card ever sent was dropped in this method, before any logic that wanted it could
            // run. JudgeResetCheck depends on this line: without it the corroboration step is dead
            // code that reports "the alert is still open" for alerts that reset days ago.
            if (card["resetCard"] == null && ctx["resetCard"] is JObject)
            {
                card["resetCard"] = ctx["resetCard"].DeepClone();
            }

            return card;
        }

        private static void Fill(JObject card, JObject ctx, string ctxKey, string cardKey, params string[] existingKeys)
        {
            JToken v = ctx[ctxKey];
            if (v == null || v.Type == JTokenType.Null || v.ToString().Trim().Length == 0)
            {
                return;
            }

            List<string> keys = new List<string>(existingKeys);
            keys.Insert(0, cardKey);
            foreach (string k in keys)
            {
                JToken have = card[k];
                if (have != null && have.Type != JTokenType.Null && have.ToString().Trim().Length > 0)
                {
                    return;
                }
            }

            card[cardKey] = v.DeepClone();
        }
    }
}
