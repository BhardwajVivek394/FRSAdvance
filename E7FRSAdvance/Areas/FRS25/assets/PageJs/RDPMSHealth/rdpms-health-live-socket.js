/* ============================================================================
 * rdpms-health-live-socket.js
 *
 * Live device-health subscription for RDPMSHealthLive.
 *
 *   ws://{base}/subscribe/deviceHealth/{siteId}
 *
 * Additive module: defines window.RdpmsHealthLive and nothing else. It does not
 * touch, wrap, or override any existing function in _List.cshtml. Wire it up by
 * calling RdpmsHealthLive.connect(...) and either supplying handlers or tagging
 * existing markup with data-rdpms-health attributes (see bindDom below).
 *
 * ---------------------------------------------------------------------------
 * FLAG DECODING (verified against the siteId=155 snapshot payload)
 *
 *   category  bits  Healthy      Partial   Faulty
 *   IOT       3     flagValue 7  1..6      0
 *   NW        4     flagValue 15 1..14     0
 *   GW        2     flagValue 3  1..2      0
 *
 * Bit width is HARD-CODED per category on purpose. The wire format pads
 * inconsistently -- nwFlag arrives as "0011" (padded to 4) while iotFlag
 * arrives as "110" (unpadded) -- so deriving width from flag.length would
 * misread an IoT value of 3 as fully healthy. flagValue is authoritative;
 * flag and flagBits are treated as advisory only.
 *
 * The server vocabulary observed so far is "Healthy" / "Degraded" only. This
 * module normalizes to Healthy / Partial / Faulty and then folds Partial into
 * Faulty for the categories flagged below, so that healthy + faulty == total.
 *
 * ---------------------------------------------------------------------------
 * IOT TILE IS NETWORK-GATED (added)
 *
 * The IoT tile does NOT use the generic classify() rule above. It is gated on
 * the NETWORK flag first, because an IoT/A10 flag we can't reach over any
 * network is not a verdict worth showing:
 *
 *   1. Look at the first three bits of nwFlagValue (mask IOT_GATE.nwMask).
 *        no gated bit set  -> IoT tile = Unknown (N/A / gray)
 *   2. Otherwise read the IoT (A10) flag:
 *        iotFlagValue == 0            -> Faulty
 *        iotFlagValue  > 0 (any bit)  -> Healthy
 *
 * So for IoT specifically, ANY set bit means Healthy (only 000 is Faulty) --
 * this deliberately overrides the "requires fullMask" rule used by NW and GW.
 * ==========================================================================*/

(function (window, $) {
    'use strict';

    if (window.RdpmsHealthLive) { return; }

    /* ---------------------------------------------------------------- config */

    var CATEGORY_SPEC = {
        IOT: { key: 'iot', label: 'IoT', bits: 3, fullMask: 7, treatPartialAsFaulty: true },
        NW: { key: 'nw', label: 'Network', bits: 4, fullMask: 15, treatPartialAsFaulty: true },
        GW: { key: 'gw', label: 'Station Gateway', bits: 2, fullMask: 3, treatPartialAsFaulty: false }
    };

    var STATUS = { HEALTHY: 'Healthy', PARTIAL: 'Partial', FAULTY: 'Faulty', UNKNOWN: 'Unknown' };

    /*
     * IoT tile gate (revised per site spec).
     *
     * The NETWORK flag is 4 bits, written MSB-first as bit1 bit2 bit3 bit4
     * (bit1 = value 8, bit2 = 4, bit3 = 2, bit4 = 1; 1 = up, 0 = down).
     *
     * The IoT (A10) verdict is only shown when the network flag's FIRST THREE
     * bits (bit1 bit2 bit3; bit4 / LSB ignored) form one of these patterns:
     *
     *     { 010, 011, 101, 110, 111 }  ==  decimal { 2, 3, 5, 6, 7 }
     *
     * Otherwise the IoT tile is N/A (gray). When the gate is open the IoT flag
     * decides: all-zero (000) -> Faulty, any bit set -> Healthy.
     *
     * If the feed turns out to pack the three gate bits in the LOW three bits
     * instead, change nwTop3 to `return nwFlagValue & 7;`.
     */
    var IOT_GATE = { reachable: [2, 3, 5, 6, 7] };

    function nwTop3(nwFlagValue) {
        return (nwFlagValue >> 1) & 7;
    }

    var CONFIG = {
        /*
         * Base is set in Index.cshtml before this file loads.
         *
         * HTTP  -> WebSocketBaseUrl
         * HTTPS -> WebSocketBaseUrlSSL
         */
        wsBase:
            window.RDPMS_HEALTH_WS_BASE ||
            (
                window.location.protocol === 'https:'
                    ? 'wss://proxy.energy7.org:8081'
                    : 'ws://proxy.energy7.org:8081'
            ),

        authToken:
            window.RDPMS_HEALTH_WS_TOKEN ||
            '',

        path:
            '/subscribe/deviceHealth/',

        sensorPath:
            '/subscribe/sensorHealth/',

        reconnectBaseMs:
            2000,

        reconnectMaxMs:
            30000,

        connectTimeoutMs:
            10000,

        maxReconnectAttempts:
            20   /* ~17 min at 30 s cap before giving up */
    };

    /* ------------------------------------------------------------ classify */

    function specFor(category) {
        if (!category) { return null; }
        return CATEGORY_SPEC[String(category).toUpperCase()] || null;
    }

    /*
     * Resolve a health state from flagValue. serverStatus is only consulted
     * when flagValue is absent or the category is unrecognised, so an
     * unexpected new category never silently reports as Healthy.
     */
    function classify(category, flagValue, serverStatus) {
        var spec = specFor(category);
        var v = (typeof flagValue === 'number' && isFinite(flagValue)) ? flagValue : null;

        if (!spec || v === null) {
            if (!serverStatus) { return STATUS.UNKNOWN; }
            var s = String(serverStatus).toLowerCase();

            /* The groupHealth frame uses a different vocabulary from the
               per-row deviceHealth frames: clusters and modems report "ok",
               A10 devices report "up"/"down", and a suppressed device reports
               "unknown". Only "healthy" was recognised here, so every "ok"
               modem and every "up" A10 fell through to UNKNOWN - which
               nfWsDeviceIsHealthy then reads as not-healthy, painting all four
               modems faulty while the feed reported modemWiredDown:0 and
               modemWirelessDown:0. */
            if (s === 'healthy' || s === 'ok' || s === 'up' || s === 'online') {
                return STATUS.HEALTHY;
            }
            if (s === 'faulty' || s === 'down' || s === 'offline') {
                return STATUS.FAULTY;
            }
            if (s === 'degraded' || s === 'partial') { return STATUS.PARTIAL; }
            return STATUS.UNKNOWN;
        }

        if (v >= spec.fullMask) { return STATUS.HEALTHY; }
        if (v <= 0) { return STATUS.FAULTY; }
        return STATUS.PARTIAL;
    }

    /* Collapse Partial per the category policy, for two-state UI slots. */
    function effectiveStatus(category, status) {
        var spec = specFor(category);
        if (status === STATUS.PARTIAL && spec && spec.treatPartialAsFaulty) {
            return STATUS.FAULTY;
        }
        return status;
    }

    /*
     * IoT tile status, gated on the network flag (see header + IOT_GATE).
     *
     *   network gate closed (no selected NW bit) -> Unknown (N/A / gray)
     *   gate open, iotFlagValue == 0             -> Faulty
     *   gate open, iotFlagValue  > 0 (any bit)   -> Healthy
     *
     * Either flag absent -> Unknown, so a missing frame never reports Healthy.
     */
    function classifyIotGated(iotFlagValue, nwFlagValue) {
        var iot = (typeof iotFlagValue === 'number' && isFinite(iotFlagValue)) ? iotFlagValue : null;
        var nw = (typeof nwFlagValue === 'number' && isFinite(nwFlagValue)) ? nwFlagValue : null;

        if (nw === null || IOT_GATE.reachable.indexOf(nwTop3(nw)) === -1) { return STATUS.UNKNOWN; }
        if (iot === null) { return STATUS.UNKNOWN; }
        return (iot === 0) ? STATUS.FAULTY : STATUS.HEALTHY;
    }

    /*
     * Network verdict from the 4-bit flag (MSB-first bit1 bit2 bit3 bit4;
     * 1 = up, 0 = down). Two independent channels, per site spec:
     *
     *   Wired    : Faulty when bit1 == 0 AND bit3 == 0, else Healthy
     *   Wireless : Faulty when bit2 == 0,               else Healthy
     *
     * Overall tile status: Healthy if either channel is Healthy, Faulty when
     * both channels are Faulty. Flag absent -> Unknown (never a false Healthy).
     */
    function classifyNetwork(nwFlagValue) {
        var nw = (typeof nwFlagValue === 'number' && isFinite(nwFlagValue)) ? nwFlagValue : null;
        if (nw === null) {
            return { wired: STATUS.UNKNOWN, wireless: STATUS.UNKNOWN, status: STATUS.UNKNOWN };
        }

        var bit1 = (nw & 8) ? 1 : 0;   /* MSB */
        var bit2 = (nw & 4) ? 1 : 0;
        var bit3 = (nw & 2) ? 1 : 0;

        var wired = (bit1 === 0 && bit3 === 0) ? STATUS.FAULTY : STATUS.HEALTHY;
        var wireless = (bit2 === 0) ? STATUS.FAULTY : STATUS.HEALTHY;
        var status = (wired === STATUS.HEALTHY || wireless === STATUS.HEALTHY)
            ? STATUS.HEALTHY : STATUS.FAULTY;

        return { wired: wired, wireless: wireless, status: status };
    }

    /* Overlay the wired/wireless verdict + tile status onto an NW rollup. */
    function applyNetworkChannels(nw) {
        if (!nw) { return; }
        var v = classifyNetwork(nw.flagValue);
        nw.wired = v.wired;
        nw.wireless = v.wireless;
        nw.status = v.status;
    }

    /*
     * Apply the gated IoT rule to a whole IoT rollup: every device is
     * reclassified with the site network flag as the gate, the counts are
     * recomputed, and the tile status is set. This replaces the generic
     * classify()/rollup() verdict for IoT only -- there is no "Partial" for
     * IoT under this rule, so partial is forced to 0 and healthy + faulty +
     * unknown always equals the device count.
     */
    function applyIotGate(iot, nwFlagValue) {
        if (!iot) { return; }
        var devs = iot.devices || [];
        var healthy = 0, faulty = 0, unknown = 0;

        for (var i = 0; i < devs.length; i++) {
            var s = classifyIotGated(devs[i].flagValue, nwFlagValue);
            devs[i].status = s;
            devs[i].effectiveStatus = s;
            if (s === STATUS.HEALTHY) { healthy++; }
            else if (s === STATUS.FAULTY) { faulty++; }
            else { unknown++; }
        }

        iot.healthy = healthy;
        iot.partial = 0;
        iot.faulty = faulty;
        iot.unknown = unknown;
        iot.faultyEffective = faulty;
        iot.status = classifyIotGated(iot.flagValue, nwFlagValue);
        iot.downBits = downBits('IOT', iot.flagValue);
    }

    /* NETWORK (Modem) IS GATEWAY-GATED (added)
 *
 * A modem sits BEHIND the station gateway: until the gateway link is
 * confirmed up, the modem is not genuinely reachable, so a would-be-Healthy
 * modem verdict is not worth showing. Mirrors the IoT-tile gate (IoT gated
 * on Network); here Network is gated on Gateway.
 *
 * The flat feed sends each device's Set/Reset as its own frame, and the
 * modem's link-restored Reset routinely lands BEFORE the gateway's own
 * GATEWAY_HEALTH Reset (observed: modem cleared 17:07:07, gateway 17:08:11).
 * Holding the modem at Unknown/Undetermined until the gateway is Healthy
 * guarantees the Station Gateway reaches Healthy FIRST. A real, un-masked
 * modem fault still shows Faulty; only a would-be-Healthy modem is held. */
    function applyGatewayGate(nw, gwStatus) {
        if (!nw || gwStatus === STATUS.HEALTHY) { return; }
        var devs = nw.devices || [];
        var healthy = 0, faulty = 0, unknown = 0;
        for (var i = 0; i < devs.length; i++) {
            if (devs[i].status === STATUS.FAULTY) { faulty++; continue; }
            devs[i].status = STATUS.UNKNOWN;
            devs[i].effectiveStatus = STATUS.UNKNOWN;
            devs[i].gatewayGated = true;
            unknown++;
        }
        nw.healthy = healthy;
        nw.faulty = faulty;
        nw.faultyEffective = faulty;
        nw.unknown = unknown;
        nw.status = faulty > 0 ? STATUS.FAULTY
            : (unknown > 0 ? STATUS.UNKNOWN : nw.status);
    }

    /* Which individual bits are down -- useful for tooltips / drilldown. */
    function downBits(category, flagValue) {
        var spec = specFor(category);
        if (!spec || typeof flagValue !== 'number') { return []; }
        var out = [];
        for (var i = 0; i < spec.bits; i++) {
            if (!((flagValue >> i) & 1)) { out.push(i); }
        }
        return out;
    }

    /* ----------------------------------------------------------- normalize */

    function normalizeDevice(raw) {
        if (!raw) { return null; }
        var category = raw.category || raw.Category || '';
        var flagValue = pickNum(raw, ['flagValue', 'FlagValue']);
        var serverStatus = pick(raw, ['status', 'Status']);
        var status = classify(category, flagValue, serverStatus);

        return {
            siteId: pickNum(raw, ['siteId', 'SiteId']),
            siteName: pick(raw, ['siteName', 'SiteName']),
            deviceId: String(pick(raw, ['deviceId', 'DeviceId']) || ''),
            deviceName: pick(raw, ['deviceName', 'DeviceName']),
            clusterId: pickNum(raw, ['clusterId', 'ClusterId']),
            clusterName: pick(raw, ['clusterName', 'ClusterName']) || '',
            category: String(category).toUpperCase(),
            flag: pick(raw, ['flag', 'Flag']),
            flagValue: flagValue,
            status: status,
            effectiveStatus: effectiveStatus(category, status),
            serverStatus: serverStatus,
            downBits: downBits(category, flagValue),
            lastChange: pick(raw, ['lastChange', 'LastChange']),
            updateTime: pick(raw, ['updateTime', 'UpdateTime'])
        };
    }

    /* Roll a device list into counts. Totals come from the explicit
       *DeviceCount fields when present, falling back to array length. */
    function rollup(category, devices, rollupFlagValue, rollupServerStatus, declaredTotal) {
        var list = $.isArray(devices) ? devices : [];
        var healthy = 0, partial = 0, faulty = 0, unknown = 0;

        for (var i = 0; i < list.length; i++) {
            switch (list[i].status) {
                case STATUS.HEALTHY: healthy++; break;
                case STATUS.PARTIAL: partial++; break;
                case STATUS.FAULTY: faulty++; break;
                default: unknown++; break;
            }
        }

        var spec = specFor(category);
        var foldPartial = !!(spec && spec.treatPartialAsFaulty);
        var total = (typeof declaredTotal === 'number' && declaredTotal >= 0)
            ? declaredTotal
            : list.length;

        return {
            category: String(category).toUpperCase(),
            label: spec ? spec.label : String(category),
            total: total,
            healthy: healthy,
            partial: partial,
            faulty: faulty,
            unknown: unknown,
            /* two-state view, guaranteed to sum to list.length */
            faultyEffective: foldPartial ? (faulty + partial) : faulty,
            /* rollup status straight off the *FlagValue for this category */
            status: classify(category, rollupFlagValue, rollupServerStatus),
            flagValue: rollupFlagValue,
            serverStatus: rollupServerStatus,
            downBits: downBits(category, rollupFlagValue),
            devices: list
        };
    }

    function normalizeSummary(raw) {
        if (!raw) { return null; }

        var allDevices = mapDevices(pick(raw, ['devices', 'Devices']));
        var iotDevices = mapDevices(pick(raw, ['iotDevices', 'IotDevices', 'IoTDevices']));
        var nwDevices = mapDevices(pick(raw, ['nwDevices', 'NwDevices']));

        /* GW has no dedicated array in the payload -- pull it out of devices. */
        var gwDevices = filterCategory(allDevices, 'GW');
        if (!iotDevices.length) { iotDevices = filterCategory(allDevices, 'IOT'); }
        if (!nwDevices.length) { nwDevices = filterCategory(allDevices, 'NW'); }

        var model = {
            siteId: pickNum(raw, ['siteId', 'SiteId']),
            siteName: pick(raw, ['siteName', 'SiteName']),
            totalAlerts: pickNum(raw, ['totalAlerts', 'TotalAlerts']) || 0,
            updateTime: pick(raw, ['updateTime', 'UpdateTime']),
            siteStatus: null,
            iot: rollup('IOT', iotDevices,
                pickNum(raw, ['iotFlagValue', 'IotFlagValue']),
                pick(raw, ['iotStatus', 'IotStatus']),
                pickNum(raw, ['iotDeviceCount', 'IotDeviceCount'])),
            nw: rollup('NW', nwDevices,
                pickNum(raw, ['nwFlagValue', 'NwFlagValue']),
                pick(raw, ['nwStatus', 'NwStatus']),
                pickNum(raw, ['nwDeviceCount', 'NwDeviceCount'])),
            gw: rollup('GW', gwDevices,
                pickNum(raw, ['gwFlagValue', 'GwFlagValue']),
                pick(raw, ['gwStatus', 'GwStatus']),
                gwDevices.length),
            devices: allDevices
        };

        /* IoT is network-gated: reclassify every IoT device and recompute the
           counts + tile status before feeding into the site status. */
        applyIotGate(model.iot, model.nw.flagValue);

        /* Network wired/wireless verdict from the flag bits. */
        applyNetworkChannels(model.nw);

        /* Site rollup: trust the server field, else derive worst-of. */
        var siteServer = pick(raw, ['status', 'Status']);
        model.siteStatus = siteServer
            ? normalizeStatusString(siteServer)
            : worstOf([model.iot.status, model.nw.status, model.gw.status]);

        return model;
    }

    function normalizeStatusString(s) {
        var v = String(s).toLowerCase();
        if (v === 'healthy' || v === 'ok' || v === 'up' || v === 'online') {
            return STATUS.HEALTHY;
        }
        if (v === 'degraded' || v === 'partial') { return STATUS.PARTIAL; }
        if (v === 'faulty' || v === 'down' || v === 'offline') { return STATUS.FAULTY; }
        return STATUS.UNKNOWN;
    }

    function worstOf(list) {
        var rank = {}; rank[STATUS.HEALTHY] = 0; rank[STATUS.PARTIAL] = 1;
        rank[STATUS.FAULTY] = 2; rank[STATUS.UNKNOWN] = -1;
        var worst = STATUS.HEALTHY;
        for (var i = 0; i < list.length; i++) {
            if ((rank[list[i]] || 0) > (rank[worst] || 0)) { worst = list[i]; }
        }
        return worst;
    }

    function mapDevices(arr) {
        if (!$.isArray(arr)) { return []; }
        var out = [];
        for (var i = 0; i < arr.length; i++) {
            var d = normalizeDevice(arr[i]);
            if (d) { out.push(d); }
        }
        return out;
    }

    function filterCategory(list, cat) {
        var out = [];
        for (var i = 0; i < list.length; i++) {
            if (list[i].category === cat) { out.push(list[i]); }
        }
        return out;
    }

    function pick(obj, names) {
        for (var i = 0; i < names.length; i++) {
            if (obj && obj[names[i]] !== undefined && obj[names[i]] !== null) {
                return obj[names[i]];
            }
        }
        return null;
    }

    function pickNum(obj, names) {
        var v = pick(obj, names);
        if (v === null || v === '') { return null; }
        var n = parseInt(v, 10);
        return isNaN(n) ? null : n;
    }

    /* ============================================================
     * NEW FEED — flat per-row device-health records
     * ============================================================
     * Snapshot  : many rows in one batch (every tracked channel).
     * Update    : exactly one row (MessagesCount == 1).
     * Verdict   : row.type (Set / Reset / Healthy) is authoritative;
     *             if type is anything else (e.g. "deviceHealth") we
     *             fall back to the documented `active` flag:
     *                 active == 1  ->  DOWN  (Faulty)
     *                 active == 0  ->  UP    (Healthy)
     * No flag bitmasks are read from the wire any more. Each row is
     * turned into a device shape _List.cshtml already understands.
     *
     * flagValue is emitted purely for backward compatibility with the
     * consumers in _List.cshtml that still bit-test it
     * (nfClassifyNetwork / nfNetworkIsUp / nfBuildIotLookup):
     *     DOWN -> 0            (all bits clear -> Faulty / gate down)
     *     UP   -> fullMask     (all bits set   -> Healthy / gate up)
     * It is NOT a real reading; it is a 2-state encoding.
     * ============================================================ */

    /* True when an envelope is one of the new flat rows (vs. the legacy
       summary/device envelope). Requires a device shape AND a recognised
       type so a stray object never gets mis-ingested. */
    function isDeviceHealthRow(env) {
        if (!env || typeof env !== 'object') { return false; }
        var t = pick(env, ['type', 'Type']);
        var typeOk =
            t === 'Set' || t === 'Reset' || t === 'Healthy' ||
            t === 'deviceHealth';
        var shapeOk =
            (env.deviceType !== undefined || env.DeviceType !== undefined) &&
            (env.deviceId !== undefined || env.DeviceId !== undefined);
        return typeOk && shapeOk;
    }

    /* LEGACY FLAG SNAPSHOT DETECTOR.
     * The superseded envelope { siteId, messageType:"snapshot",
     * summary:{...iotFlagValue/nwFlagValue/devices...}, device, siteSummary }
     * and its bare-summary variant. Per site directive these are IGNORED: the
     * flat per-row Set/Reset/Healthy feed is the only supported source, and
     * the bitmask verdicts this shape carries are no longer authoritative.
     * Checked AFTER isDeviceHealthRow(), so a real flat row always wins. */
    var _legacySnapshotWarned = false;

    /* GROUP HEALTH SNAPSHOT DETECTOR — per-site roll-up feed:
     { type:"groupHealth", siteId, siteName, summary:{ a10Total, a10Online,
       a10Down, modemTotal, modemWiredDown, modemWirelessDown, gatewayTotal,
       gatewayUp, gatewayDown, ... }, clusters:[...] }.
     Checked BEFORE isLegacySnapshotEnvelope(), which would otherwise drop it
     on the strength of its `summary` key. */
    function isGroupHealthSnapshot(env) {
        if (!env || typeof env !== 'object') { return false; }
        if (String(pick(env, ['type', 'Type']) || '') !== 'groupHealth') { return false; }
        return !!pick(env, ['summary', 'Summary']);
    }

    /* GROUPHEALTH MASK INDEX (GroupHealth Masking guide §2).
       window.__rdpmsGroupMask[siteId] = {
         at    : ms the frame landed,
         a10   : { '<adc>' and '<clusterId>|<adc>' : { all: bool, ch: { A10_OFC: true, ... } } },
         wired : { 'M:<MODEMID>' and 'C:<clusterId>' : true }   wired masked
       }
       A10   : per-channel masked[] (same order as channels[]); all = status
               "unknown" or every masked[] true.
       Modem : Wired is masked when wired.masked is true OR wired.status is
               "unknown" (DFI Gateway down). The two are independent - a leg
               can be {"status":"down","masked":true}, and the flag wins.
               Wireless is never masked.
       Rebuilt wholesale per frame so a mask that clears server-side clears here.
       Only masks are stored - up/down verdicts still come from flat rows. */
    var _groupMaskShapeWarned = {};

    function ghArray(obj, names) {
        if (!obj) { return []; }
        for (var i = 0; i < names.length; i++) {
            if ($.isArray(obj[names[i]])) { return obj[names[i]]; }
        }
        return [];
    }

    /* "cluster_9" -> "9", "0014" -> "14", "04EEE810C61A" -> unchanged. */
    function ghId(v) {
        if (v == null) { return ''; }
        var s = String(v).trim();
        var m = /(\d+)$/.exec(s);
        if (m && (m[1] === s || /[_-]\d+$/.test(s))) { return String(parseInt(m[1], 10)); }
        return s;
    }

    function groupMaskFor(siteId) {
        var all = window.__rdpmsGroupMask;
        return all ? (all[String(siteId)] || null) : null;
    }

    function ingestGroupHealthMask(siteId, env) {
        var sid = String(siteId);
        var mask = { at: Date.now(), a10: {}, wired: {}, netDown: {}, legDown: {} };

        /* Dashboard-derived Wired mask (ref: Index 53 shmDerivedMasks rule 1):
           a down Station Gateway makes every modem's OFC / Wired leg
           Undetermined until the Gateway itself is Healthy again. */
        var _ghSummary = pick(env, ['summary', 'Summary']) || {};
        var gatewayIsDown =
            (parseInt(pick(_ghSummary, ['gatewayDown', 'GatewayDown']), 10) || 0) > 0;
        var clusters = ghArray(env, ['clusters', 'Clusters']);
        var sawA10 = false;

        for (var i = 0; i < clusters.length; i++) {
            var c = clusters[i];
            if (!c) { continue; }
            var cid = ghId(pick(c, ['clusterId', 'ClusterId', 'id', 'Id']));

            /* ---- Cluster network fully down? (parent-link masking) ─────────
   Decide this BEFORE the A10 loop so a down parent forces its
   children to N/A. A modem is fully down when NEITHER medium is
   "ok" (each is "down" or "unknown"). With no live path, the A10s
   behind it are unreachable, so their real state is unknowable ->
   N/A, exactly as the backend already does by masking them. The
   modem itself is NOT masked here (that stays a "down" fault -> red);
   only its children become N/A. */
            var moPre = pick(c, ['modem', 'Modem']);
            var modemsPre = $.isArray(moPre) ? moPre : (moPre ? [moPre] : ghArray(c, ['modems', 'Modems']));
            var clusterNetDown = false;
            for (var mp = 0; mp < modemsPre.length; mp++) {
                var mdp = modemsPre[mp];
                if (!mdp) { continue; }
                var wPre = String(pick(pick(mdp, ['wired', 'Wired']), ['status', 'Status']) || '').toLowerCase();
                var lPre = String(pick(pick(mdp, ['wireless', 'Wireless']), ['status', 'Status']) || '').toLowerCase();
                /* Both media must be named and neither "ok". A blank/absent
                   status is not evidence of down, so it does not trip this. */
                if (wPre && lPre && wPre !== 'ok' && lPre !== 'ok') { clusterNetDown = true; }
            }
            if (clusterNetDown && cid) { mask.netDown[cid] = true; }

            /* ---- A10 devices ---- */
            var a10s = ghArray(c, ['a10', 'a10s', 'A10', 'A10s', 'a10Devices', 'devices', 'Devices']);
            for (var j = 0; j < a10s.length; j++) {
                var d = a10s[j];
                if (!d) { continue; }
                var dev = ghId(pick(d, ['deviceId', 'DeviceId']));
                if (!dev) { continue; }
                sawA10 = true;

                var chs = ghArray(d, ['channels', 'Channels']);
                var msk = ghArray(d, ['masked', 'Masked']);
                /* Rule: an A10 is MASKED only when ALL THREE channels
   (A10_TCP, A10_MQTT, A10_OFC) are masked:true. A partial
   mask is ignored - the device is evaluated normally. */
                var mch = {};
                for (var k = 0; k < msk.length; k++) {
                    if (msk[k] === true && chs[k] != null) {
                        mch[String(chs[k]).toUpperCase()] = true;
                    }
                }
                var allThree = chs.length
                    ? (mch.A10_TCP === true && mch.A10_MQTT === true && mch.A10_OFC === true)
                    : (msk.length === 3 && msk[0] === true && msk[1] === true && msk[2] === true);

                /* Parent link down -> child A10 is N/A even if the backend did
                   not set its per-channel masked[] (or sent no rows for it).
                   Force all three channels masked so the whole downstream
                   pipeline (state builder, tile count, seeding) treats it as
                   fully masked. */
                if (clusterNetDown) {
                    mch.A10_TCP = true; mch.A10_MQTT = true; mch.A10_OFC = true;
                    allThree = true;
                }
                /* all = device masked (N/A). ch = channels to leave out of
   fault evaluation. Partial masks are kept ONLY for ch - a
   masked A10_OFC with flag 0 must not make an "up" A10 Faulty. */
                var anyCh = false;
                for (var mk in mch) { if (mch.hasOwnProperty(mk)) { anyCh = true; break; } }
                if (!allThree && !anyCh) { continue; }   /* nothing masked -> no entry */

                /* Carry the device's OWN groupHealth verdict alongside the mask.
                   A masked device is normally left out of both header buckets -
                   the parent link is dark, so its silence proves nothing. But a
                   device the backend still reports as status:"down" (its fault
                   has a setTime and no resetTime, carried over from before the
                   parent went down) is genuinely faulty, and masking it hid a
                   real fault from the header count. Recorded separately from
                   `all` so only the header consumes it - the table, drawer and
                   pictorial keep painting it as masked / Undetermined. */
                var entry = {
                    all: allThree,
                    ch: mch,
                    down: (String(pick(d, ['status', 'Status']) || '')
                        .toLowerCase() === 'down')
                };

                var dcid = ghId(pick(d, ['clusterId', 'ClusterId'])) || cid;
                mask.a10[dev] = entry;
                if (dcid) { mask.a10[dcid + '|' + dev] = entry; }
            }

            /* ---- Modem (object, null, or array) ---- */
            var mo = pick(c, ['modem', 'Modem']);
            var modems = $.isArray(mo) ? mo : (mo ? [mo] : ghArray(c, ['modems', 'Modems']));
            /* One masked-ness test, applied to either leg. Masked when the
               backend SAYS so (masked:true), or when it reports the leg as
               "unknown". The explicit flag is checked independently of status:
               a frame can carry {"status":"down","masked":true}
               (cluster_9/FTU-1 on site 155), and testing status alone dropped
               that leg on the floor. masked[] is accepted as an array too,
               matching the A10 shape - all entries true. */
            function legState(leg) {
                var st = String(pick(leg, ['status', 'Status']) || '').toLowerCase();
                var mk = pick(leg, ['masked', 'Masked']);
                var isMasked = (mk === true) ||
                    ($.isArray(mk) && mk.length &&
                        mk.every(function (x) { return x === true; }));
                return { status: st, masked: isMasked };
            }

            for (var m = 0; m < modems.length; m++) {
                var md = modems[m];
                if (!md) { continue; }

                var mid = String(pick(md, ['deviceId', 'DeviceId']) || '').trim().toUpperCase();
                var w = legState(pick(md, ['wired', 'Wired']));
                var l = legState(pick(md, ['wireless', 'Wireless']));

                if (w.masked || w.status === 'unknown' || gatewayIsDown) {
                    if (mid) { mask.wired['M:' + mid] = true; }
                    if (cid) { mask.wired['C:' + cid] = true; }
                }

                /* A leg reported masked AND still status:"down" holds a real,
                   unreset fault carried over from before its parent went dark.
                   A masked modem usually stops producing rows, so the rolled-up
                   flagValue goes missing and computeNetworkCounts files the
                   cluster under `unknown` - which total-minus-faulty then counts
                   as ONLINE, hiding the fault. Recorded per leg so only the
                   header consumes it; the cells keep reading Undetermined. */
                var down = {
                    wired: !!(w.masked && w.status === 'down'),
                    wireless: !!(l.masked && l.status === 'down')
                };
                if (down.wired || down.wireless) {
                    if (cid) { mask.legDown['C:' + cid] = down; }
                    if (mid) { mask.legDown['M:' + mid] = down; }
                }
            }
        }

        /* Shape guard: clusters present but no A10 list under any known key. */
        if (clusters.length && !sawA10 && !_groupMaskShapeWarned[sid] && window.console) {
            _groupMaskShapeWarned[sid] = true;
            console.warn('[RdpmsHealthLive] groupHealth site ' + sid +
                ': no A10 list found on clusters[] - A10 masking inactive. Cluster keys:',
                Object.keys(clusters[0] || {}));
        }

        window.__rdpmsGroupMask = window.__rdpmsGroupMask || {};
        window.__rdpmsGroupMask[sid] = mask;

        /* ORDERING DIAGNOSTIC - log only. Records what this roll-up said
           about the gateway AND about each cluster's wired leg, so the two
           can be lined up against the flat rows above in arrival order. */
        if (window.rdpmsOrderLog) {
            var _lsum = pick(env, ['summary', 'Summary']) || {};
            var _lcl = [];

            for (var _li = 0; _li < clusters.length; _li++) {
                var _lc = clusters[_li];
                if (!_lc) { continue; }

                var _lmo = pick(_lc, ['modem', 'Modem']);
                var _lm1 = $.isArray(_lmo) ? _lmo[0] : _lmo;
                var _lw = _lm1 ? pick(_lm1, ['wired', 'Wired']) : null;

                _lcl.push({
                    cluster: ghId(pick(_lc, ['clusterId', 'ClusterId'])),
                    wiredStatus: _lw ? pick(_lw, ['status', 'Status']) : '(no modem)',
                    wiredMasked: _lw ? pick(_lw, ['masked', 'Masked']) : null
                });
            }

            window.rdpmsOrderLog.evt('GROUPHEALTH', sid, {
                updateTime:    pick(env, ['updateTime', 'UpdateTime']),
                gatewayUp:     pick(_lsum, ['gatewayUp', 'GatewayUp']),
                gatewayDown:   pick(_lsum, ['gatewayDown', 'GatewayDown']),
                wiredMaskKeys: (function () {
                    var ks = [];
                    for (var k in mask.wired) {
                        if (mask.wired.hasOwnProperty(k)) { ks.push(k); }
                    }
                    return ks;
                })(),
                clusters: _lcl
            });
        }
    }

    function isLegacySnapshotEnvelope(env) {
        if (!env || typeof env !== 'object') { return false; }
        if (pick(env, ['summary', 'Summary'])) { return true; }
        if (pick(env, ['device', 'Device'])) { return true; }
        if (pick(env, ['siteSummary', 'SiteSummary'])) { return true; }
        /* bare summary object with no envelope wrapper */
        return env.iotFlagValue !== undefined || env.IotFlagValue !== undefined ||
            env.nwFlagValue !== undefined || env.NwFlagValue !== undefined ||
            env.devices !== undefined || env.Devices !== undefined;
    }

    /* Row identity = deviceType|deviceId|channel (per the integration guide). */
    /* ══════════════════════════════════════════════════════════════════
       ORDERING DIAGNOSTIC  —  window.rdpmsOrderLog        (LOG ONLY)
       ──────────────────────────────────────────────────────────────────
       Answers one question from live traffic: when a station gateway
       recovers, does the Network "wired" cell leave Undetermined BEFORE
       or AFTER the Station Gateway cell leaves Faulty?

       Nothing here changes a verdict, a paint or a count. It records
       feed events in ARRIVAL ORDER and reports state CHANGES only, then
       prints an explicit RECOVERY ORDER line the first time both sides
       reach Healthy.

       Console:
         rdpmsOrderLog.off() / .on()   mute / unmute
         rdpmsOrderLog.table()         everything captured, as a table
         rdpmsOrderLog.dump()          raw array (copy out of DevTools)
         rdpmsOrderLog.clear()         reset buffer and the order marks
         rdpmsOrderLog.verdicts()      recovery-order verdicts, as a table
         rdpmsOrderLog.save()          DOWNLOAD .log  (verdicts + timeline)
         rdpmsOrderLog.csv()           DOWNLOAD .csv  (timeline for Excel)
         rdpmsOrderLog.json()          DOWNLOAD .json (raw buffer)
       ══════════════════════════════════════════════════════════════════ */
    (function () {
        if (window.rdpmsOrderLog) { return; }

        var seq = 0, buf = [], MAX = 3000;
        var last = {};    /* siteId -> { gateway: 'Healthy'|'Faulty', wired: ... } */
        var mark = {};    /* siteId -> { gateway: {n,t,from}, wired: {...} } Healthy */
        var amark = {};   /* siteId -> ARRIVAL marks, first healthy ROW per side  */
        var arriv = {};   /* siteId -> last completed arrival verdict, for pairing */
        var vbuf = [];    /* completed recovery-order verdicts (survives re-arm)   */
        var born = new Date();

        function stamp() {
            var d = new Date();
            function p(v, w) { v = String(v); while (v.length < (w || 2)) { v = '0' + v; } return v; }
            return p(d.getHours()) + ':' + p(d.getMinutes()) + ':' +
                   p(d.getSeconds()) + '.' + p(d.getMilliseconds(), 3);
        }

        function push(rec) {
            buf.push(rec);
            while (buf.length > MAX) { buf.shift(); }
            return rec;
        }

        /* ---- download helpers (console-triggered file save) ------------ */
        function fstamp(d) {
            function p(v, w) { v = String(v); while (v.length < (w || 2)) { v = '0' + v; } return v; }
            return d.getFullYear() + p(d.getMonth() + 1) + p(d.getDate()) + '-' +
                p(d.getHours()) + p(d.getMinutes()) + p(d.getSeconds());
        }

        function download(name, text, mime) {
            try {
                var U = window.URL || window.webkitURL;
                var blob = new Blob([text], { type: mime || 'text/plain;charset=utf-8' });
                var url = U.createObjectURL(blob);
                var a = document.createElement('a');
                a.href = url; a.download = name; a.style.display = 'none';
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                setTimeout(function () { try { U.revokeObjectURL(url); } catch (e) { } }, 2000);
                return '[RDPMS ORDER] saved ' + name + '  (' + text.length + ' bytes)';
            } catch (e) {
                if (window.console) { console.warn('[RDPMS ORDER] download failed', e); }
                return '[RDPMS ORDER] download FAILED - use dump() and copy manually';
            }
        }

        function cell(v) {
            var t = (v === undefined || v === null) ? '' : String(v);
            return '"' + t.replace(/"/g, '""') + '"';
        }

        /* Mirror of translateRow()'s verdict so the ARRIVAL side asks the same
           question the PAINT side answers. Log only - nothing consumes this. */
        function rowIsDown(d) {
            if (!d) { return null; }
            var ty = d.type, ac = d.active;
            ac = (ac === undefined || ac === null || ac === '') ? null : parseInt(ac, 10);
            if (ty === 'Set') { return true; }
            return (ac === 1);
        }

        var api = {
            enabled: true,

            on:  function () { api.enabled = true;  return '[RDPMS ORDER] ON'; },
            off: function () { api.enabled = false; return '[RDPMS ORDER] OFF'; },

            clear: function () {
                buf = []; seq = 0; last = {}; mark = {};
                amark = {}; arriv = {}; vbuf = []; born = new Date();
                return '[RDPMS ORDER] cleared';
            },

            dump: function () { return buf.slice(); },

            /* Completed recovery cycles: which side reached Healthy first. */
            verdicts: function () {
                if (window.console && console.table) { console.table(vbuf); }
                return vbuf.length + ' verdict(s)';
            },

            /* DOWNLOAD - human-readable report: the answer first, then the
               full arrival/paint timeline that backs it. */
            save: function () {
                var L = [];
                L.push('RDPMS HEALTH LIVE - GATEWAY vs MODEM RECOVERY ORDER');
                L.push('generated : ' + new Date().toString());
                L.push('capture   : ' + born.toString() + '  ->  now');
                L.push('page      : ' + (window.location ? window.location.href : '-'));
                L.push('events    : ' + buf.length + '   verdicts: ' + vbuf.length);
                L.push('');
                L.push('=== VERDICTS (which reached Healthy FIRST) ===================');
                if (!vbuf.length) {
                    L.push('(none yet - a verdict is written when BOTH the Station');
                    L.push(' Gateway cell and the Network wired cell reach Healthy');
                    L.push(' after a fault. Keep the page open across one recovery.)');
                } else {
                    for (var v = 0; v < vbuf.length; v++) {
                        var q = vbuf[v];
                        L.push('');
                        L.push('[' + (v + 1) + '] site ' + q.site + '   PAINT winner: ' +
                            q.paintFirst + '   delta ' + q.paintDeltaMs + ' ms');
                        L.push('      gateway  #' + q.gatewaySeq + ' ' + q.gatewayAt +
                            '   ' + q.gatewayFrom + ' -> Healthy');
                        L.push('      wired    #' + q.wiredSeq + ' ' + q.wiredAt +
                            '   ' + q.wiredFrom + ' -> Healthy');
                        L.push('      ARRIVAL winner: ' + q.arrivalFirst +
                            '   delta ' + q.arrivalDeltaMs + ' ms');
                        L.push('      gateway row ' + q.gatewayRowAt +
                            '    modem row ' + q.modemRowAt);
                    }
                }
                L.push('');
                L.push('=== TIMELINE (arrival order, newest last) ====================');
                for (var i = 0; i < buf.length; i++) {
                    var b = buf[i];
                    L.push('#' + b.n + '  ' + b.at + '  ' + b.kind +
                        '  site ' + b.siteId + '  ' +
                        ((typeof b.data === 'string') ? b.data : JSON.stringify(b.data)));
                }
                L.push('');
                return download('rdpms-order-' + fstamp(new Date()) + '.log',
                    L.join('\r\n'));
            },

            /* DOWNLOAD - timeline as CSV for Excel. */
            csv: function () {
                var H = ['n', 'time', 'epoch_ms', 'kind', 'site', 'what', 'from',
                    'to', 'why', 'device', 'channel', 'type', 'active',
                    'setTime', 'resetTime', 'raw'];
                var L = [H.join(',')];
                for (var i = 0; i < buf.length; i++) {
                    var b = buf[i], d = (b.data && typeof b.data === 'object') ? b.data : {};
                    L.push([
                        cell(b.n), cell(b.at), cell(b.t), cell(b.kind), cell(b.siteId),
                        cell(d.what), cell(d.from), cell(d.to), cell(d.why),
                        cell(d.device), cell(d.channel), cell(d.type), cell(d.active),
                        cell(d.setTime), cell(d.resetTime),
                        cell((typeof b.data === 'string') ? b.data : JSON.stringify(b.data))
                    ].join(','));
                }
                return download('rdpms-order-' + fstamp(new Date()) + '.csv',
                    L.join('\r\n'), 'text/csv;charset=utf-8');
            },

            /* DOWNLOAD - raw buffer + verdicts, for replay or attaching to a
               ticket. */
            json: function () {
                return download('rdpms-order-' + fstamp(new Date()) + '.json',
                    JSON.stringify({
                        generated: new Date().toISOString(),
                        capturedFrom: born.toISOString(),
                        page: (window.location ? window.location.href : ''),
                        verdicts: vbuf,
                        events: buf
                    }, null, 2), 'application/json;charset=utf-8');
            },
            table: function () {
                var rows = [];
                for (var i = 0; i < buf.length; i++) {
                    var b = buf[i];
                    rows.push({
                        '#': b.n, time: b.at, kind: b.kind, site: b.siteId,
                        detail: (typeof b.data === 'string')
                            ? b.data
                            : JSON.stringify(b.data)
                    });
                }
                if (window.console && console.table) { console.table(rows); }
                return rows.length + ' event(s)';
            },

            /* A frame as it ARRIVES off the socket. */
            evt: function (kind, siteId, data) {
                if (!api.enabled) { return; }
                var r = push({
                    n: ++seq, at: stamp(), t: Date.now(), kind: kind,
                    siteId: String(siteId), data: data
                });

                /* ARRIVAL ORDER - what the FEED delivered, before any paint.
                   Marks the first healthy Gateway row and the first healthy
                   Modem row of a recovery cycle; a down row re-arms that side. */
                var side = (kind === 'ROW Gateway') ? 'gateway'
                    : (kind === 'ROW Modem') ? 'modem' : null;
                if (side) {
                    var down = rowIsDown(data);
                    var am = amark[r.siteId] || (amark[r.siteId] = {});
                    if (down === false) {
                        if (!am[side]) { am[side] = { n: r.n, t: r.t, at: r.at }; }
                        if (am.gateway && am.modem) {
                            var aFirst = (am.modem.t < am.gateway.t) ? 'MODEM row'
                                : (am.modem.t > am.gateway.t) ? 'GATEWAY row'
                                    : 'BOTH (same ms)';
                            arriv[r.siteId] = {
                                first: aFirst,
                                deltaMs: Math.abs(am.modem.t - am.gateway.t),
                                gatewayAt: am.gateway.at, modemAt: am.modem.at
                            };
                            if (window.console && console.log) {
                                console.log(
                                    '%c[RDPMS ORDER] ---> ARRIVAL ORDER  site ' + r.siteId +
                                    ':  ' + aFirst + ' arrived FIRST   (+' +
                                    arriv[r.siteId].deltaMs + ' ms)   gateway ' +
                                    am.gateway.at + '  modem ' + am.modem.at,
                                    'color:#7c3aed;font-weight:bold');
                            }
                            amark[r.siteId] = {};   /* re-arm for the next cycle */
                        }
                    } else if (down === true) {
                        am[side] = 0;
                    }
                }

                if (window.console && console.log) {
                    console.log(
                        '%c[RDPMS ORDER] #' + r.n + ' ' + r.at + '  ' + kind +
                        '  site ' + r.siteId,
                        'color:#2563eb', data);
                }
                return r;
            },

            /* A PAINTED verdict. Logged only when it CHANGES, so the console
               shows transitions rather than every repaint. `what` is
               'gateway' or 'wired'. */
            state: function (siteId, what, value, why) {
                if (!api.enabled) { return; }

                var sid = String(siteId);
                var prev = last[sid] || (last[sid] = {});
                if (prev[what] === value) { return; }

                var from = (prev[what] === undefined) ? '(init)' : prev[what];
                prev[what] = value;

                var r = push({
                    n: ++seq, at: stamp(), t: Date.now(), kind: 'PAINT', siteId: sid,
                    data: { what: what, from: from, to: value, why: why || '' }
                });

                if (window.console && console.log) {
                    console.log(
                        '%c[RDPMS ORDER] #' + r.n + ' ' + r.at + '  PAINT  site ' +
                        sid + '  ' + what + ': ' + from + ' -> ' + value +
                        (why ? '   (' + why + ')' : ''),
                        'color:#b45309;font-weight:bold');
                }

                var m = mark[sid] || (mark[sid] = {});

                if (value === 'Healthy') {
                    if (!m[what]) { m[what] = { n: r.n, t: r.t, at: r.at, from: from }; }

                    if (m.gateway && m.wired) {
                        var first = (m.wired.n < m.gateway.n)
                            ? 'NETWORK wired'
                            : ((m.wired.n > m.gateway.n) ? 'STATION GATEWAY' : 'BOTH (same event)');
                        var av = arriv[sid] || {};
                        vbuf.push({
                            site: sid,
                            paintFirst: first,
                            paintDeltaMs: Math.abs(m.wired.t - m.gateway.t),
                            gatewaySeq: m.gateway.n, gatewayAt: m.gateway.at,
                            gatewayFrom: m.gateway.from,
                            wiredSeq: m.wired.n, wiredAt: m.wired.at,
                            wiredFrom: m.wired.from,
                            arrivalFirst: av.first || '(not captured)',
                            arrivalDeltaMs: (av.deltaMs === undefined) ? '' : av.deltaMs,
                            gatewayRowAt: av.gatewayAt || '-',
                            modemRowAt: av.modemAt || '-'
                        });
                        if (window.console && console.log) {
                            console.log(
                                '%c[RDPMS ORDER] ===> RECOVERY ORDER  site ' + sid +
                                ':  ' + first + ' reached Healthy FIRST   ' +
                                '(gateway #' + m.gateway.n + ' ' + m.gateway.at +
                                ', wired #' + m.wired.n + ' ' + m.wired.at + ', +' +
                                Math.abs(m.wired.t - m.gateway.t) + ' ms)   ' +
                                'rdpmsOrderLog.save() to download',
                                'color:#dc2626;font-weight:bold;font-size:12px');
                        }
                        mark[sid] = {};      /* re-arm for the next cycle */
                        arriv[sid] = null;
                    }
                } else {
                    /* Left Healthy - drop that side's mark so the NEXT
                       recovery is timed from a clean start. */
                    m[what] = 0;
                }
                return r;
            }
        };

        window.rdpmsOrderLog = api;
    })();

    function rowKey(r) {
        return String(pick(r, ['deviceType', 'DeviceType']) || '') + '|' +
            String(pick(r, ['deviceId', 'DeviceId']) || '') + '|' +
            String(pick(r, ['channel', 'Channel']) || '');
    }

    function ingestDeviceHealthRow(siteId, row) {
        var sid = String(siteId);
        /* Deferred clear: wipe on the FIRST row after a reconnect so the UI
           keeps showing correct state right up to the moment new data arrives,
           not at onopen where rawRows would be empty between clear and snapshot. */
        var entry = sockets[sid] || sockets[parseInt(sid, 10)];
        if (entry && entry.snapshotFlushed === false) {
            rawRows[sid] = {};
            entry.snapshotFlushed = true;
        }
        if (!rawRows[sid]) { rawRows[sid] = {}; }
        rawRows[sid][rowKey(row)] = row;

        /* ORDERING DIAGNOSTIC - log only, no behaviour change. Gateway and
           Modem rows are the two sides of the question being measured. */
        if (window.rdpmsOrderLog) {
            var _ldt = String(pick(row, ['deviceType', 'DeviceType']) || '');
            if (_ldt === 'Gateway' || _ldt === 'Modem') {
                window.rdpmsOrderLog.evt('ROW ' + _ldt, sid, {
                    device:    pick(row, ['deviceId', 'DeviceId']),
                    channel:   pick(row, ['channel', 'Channel']),
                    type:      pick(row, ['type', 'Type']),
                    active:    pick(row, ['active', 'Active']),
                    setTime:   pick(row, ['setTime', 'SetTime']),
                    resetTime: pick(row, ['resetTime', 'ResetTime'])
                });
            }
        }
    }

    /* deviceType -> internal category. */
    function categoryForDeviceType(deviceType) {
        var dt = String(deviceType || '');
        if (dt === 'A10') { return 'IOT'; }
        if (dt === 'Modem') { return 'NW'; }
        if (dt === 'Gateway') { return 'GW'; }
        return null;
    }

    /* Turn one raw row into the device object _List.cshtml consumes. */
    function translateRow(r) {
        var cat = categoryForDeviceType(pick(r, ['deviceType', 'DeviceType']));
        if (!cat) { return null; }

        var type = pick(r, ['type', 'Type']);
        var active = pickNum(r, ['active', 'Active']);
        var setTime = pick(r, ['setTime', 'SetTime']);
        var resetTime = pick(r, ['resetTime', 'ResetTime']);
        var isDown;
        if (type === 'Set') {
            isDown = true;
        } else if (type === 'Reset' || type === 'Healthy') {
            /* Reset / Healthy normally means the alert cleared. But active is
               the authoritative field: when a publisher keeps the type string
               static and only flips active, trusting the type alone pins the
               device healthy forever. If active says 1, believe active. */
            isDown = (active === 1);
        } else {
            isDown = (active === 1);   /* legacy / "deviceHealth" fallback */
        }


        var status = isDown ? STATUS.FAULTY : STATUS.HEALTHY;

        var spec = specFor(cat);
        var upValue = (spec && typeof spec.fullMask === 'number')
            ? spec.fullMask : 1;

        var updTime = pick(r, ['updateTime', 'UpdateTime']);

        return {
            siteId: pickNum(r, ['siteId', 'SiteId']),
            siteName: pick(r, ['siteName', 'SiteName']),
            deviceId: String(pick(r, ['deviceId', 'DeviceId']) || ''),
            deviceName: pick(r, ['deviceName', 'DeviceName']),
            clusterId: pickNum(r, ['clusterId', 'ClusterId']),
            clusterName: pick(r, ['clusterName', 'ClusterName']) || '',
            channel: pick(r, ['channel', 'Channel']),
            cardLineId: pickNum(r, ['cardLineId', 'CardLineId']),
            category: cat,
            status: status,
            effectiveStatus: status,        /* two-state: no Partial in new feed */
            flagValue: isDown ? 0 : upValue,/* 0 => Faulty, fullMask => Healthy  */
            /* fault time on down, recovery time on up (guide's set/reset rule) */
            lastChange: isDown ? setTime : resetTime,
            updateTime: updTime,
            setTime: setTime,
            resetTime: resetTime,
            rowType: type
        };
    }

    /* Count DISTINCT devices (worst-of across channels), not raw rows.
       A single A10/Modem spans several channel rows; any down channel makes
       the device down. */
    function rollupRows(category, devs) {
        var byDevice = {};
        for (var i = 0; i < devs.length; i++) {
            var d = devs[i];
            /* GW: the flat feed sends BOTH gateways (GATEWAY_HEALTH and
               EXGT_HEALTH) with deviceId == siteId, so keying on deviceId
               collapsed the two into one device -- gw.total read 1 for two
               gateways and gw.faulty could never exceed 1. Key GW by channel so
               each gateway family counts once. */
            var k = (category === 'GW')
                ? (d.deviceId + '|' + String(d.channel || ''))
                : d.deviceId;
            if (!byDevice[k]) {
                byDevice[k] = d;
            } else if (byDevice[k].status === STATUS.HEALTHY &&
                d.status === STATUS.FAULTY) {
                byDevice[k] = d;
            }
        }

        var healthy = 0, faulty = 0;
        for (var key in byDevice) {
            if (!byDevice.hasOwnProperty(key)) { continue; }
            if (byDevice[key].status === STATUS.FAULTY) { faulty++; }
            else { healthy++; }
        }

        var spec = specFor(category);
        return {
            category: category,
            label: spec ? spec.label : category,
            total: healthy + faulty,
            healthy: healthy,
            partial: 0,
            faulty: faulty,
            unknown: 0,
            faultyEffective: faulty,
            /* GW is Faulty only when EVERY configured gateway of the station is
   down. `faulty >= 2` hard-coded a two-gateway station and, combined
   with the deviceId collapse above, could never be true -- so
   model.gw.status was pinned Healthy and the fast flat-feed path
   never reported a gateway fault. This form also handles a station
   with a single configured gateway. */
            status: category === 'GW'
                ? ((healthy + faulty) === 0
                    ? STATUS.UNKNOWN
                    : (faulty > 0 && faulty >= (healthy + faulty)
                        ? STATUS.FAULTY : STATUS.HEALTHY))
                : (faulty > 0 ? STATUS.FAULTY : (healthy > 0 ? STATUS.HEALTHY : STATUS.UNKNOWN)),
            flagValue: null,
            serverStatus: null,
            downBits: [],
            devices: devs   /* per-channel rows kept for _List row painting */
        };
    }

    /* OFC = wired, 4G = wireless (per site spec). Transport suffix
       (_TCP / _MQTT) is ignored — it is not a medium.

       NW (Modem) ROWS ONLY. Do not call this for A10 or Gateway rows: the
       A10 channel vocabulary includes A10_OFC, which contains "OFC" and would
       be misread as a wired MEDIUM. Only collapseNetworkToClusterVerdict()
       (fed exclusively nwDevs) may call this. */
    function mediumOfChannel(channel) {
        var c = String(channel || '').toUpperCase();
        if (c.indexOf('OFC') !== -1) { return 'wired'; }
        if (c.indexOf('4G') !== -1) { return 'wireless'; }
        return 'other';
    }

    /* Collapse Modem channels to a per-cluster WIRED / WIRELESS verdict and
       encode both into each row's flagValue, so the two Network consumers in
       _List.cshtml stay consistent and keep their wired/wireless split:
         - computeNetworkCounts() picks ONE row per cluster (latest-wins) and
           runs nfClassifyNetwork() on its flagValue;
         - fnApplyWsDeviceHealthToRows() paints each row by ModemId.

       Bit layout expected by nfClassifyNetwork / nfNetworkIsUp:
         wired    up -> bits 8 | 2   (either wired leg set)
         wireless up -> bit 4
       so:
         wired up + wireless up   -> 14
         wired up + wireless down -> 10
         wired down + wireless up ->  4
         both down                ->  0

       Rules:
         - A medium is UP if ANY of its channels is up (reachable via any
           transport), matching the old bit-OR wired-leg behaviour.
         - A medium with NO rows for the cluster is left UP, so missing data
           never manufactures a fault (see the totals requirement: no live
           data must not be reported as faulty). */
    function collapseNetworkToClusterVerdict(nwDevs, siteId) {
        var byCluster = {};
        var gm = groupMaskFor(siteId);   /* groupHealth masks (may be null) */

        for (var i = 0; i < nwDevs.length; i++) {
            var d = nwDevs[i];
            var ck = (d.clusterId != null) ? String(d.clusterId) : '';
            var rec = byCluster[ck] || (byCluster[ck] = {
                wiredSeen: false, wiredUp: false,
                wirelessSeen: false, wirelessUp: false,
                wiredMasked: false
            });

            /* Wired masked (groupHealth wired.status "unknown") by modem id. */
            if (gm && gm.wired &&
                gm.wired['M:' + String(d.deviceId || '').trim().toUpperCase()]) {
                rec.wiredMasked = true;
            }

            var up = (d.status === STATUS.HEALTHY);
            var medium = mediumOfChannel(d.channel);

            if (medium === 'wired') {
                rec.wiredSeen = true;
                if (up) { rec.wiredUp = true; }
            } else if (medium === 'wireless') {
                rec.wirelessSeen = true;
                if (up) { rec.wirelessUp = true; }
            } else {
                /* Unknown medium feeds BOTH legs so it is neither dropped nor
                   able to force a phantom single-leg fault. */
                rec.wiredSeen = true;
                rec.wirelessSeen = true;
                if (up) { rec.wiredUp = true; rec.wirelessUp = true; }
            }
        }

        for (var c in byCluster) {
            if (!byCluster.hasOwnProperty(c)) { continue; }
            var r = byCluster[c];
            if (gm && gm.wired && c && gm.wired['C:' + c]) { r.wiredMasked = true; }
            var wiredUp = r.wiredSeen ? r.wiredUp : true;
            var wirelessUp = r.wirelessSeen ? r.wirelessUp : true;
            /* Masked Wired is not a Wired fault (guide §2.2: wired.tcp/mqtt are
               reported up while masked), so it never lands in wiredFaulty.
               The overall verdict must not borrow that suppressed "up": with
               Wired masked, the cluster is up only if Wireless is up. */
            var wiredUpForFlag = r.wiredMasked ? true : wiredUp;
            var wiredUpForOverall = r.wiredMasked ? false : wiredUp;
            r.flagValue = (wiredUpForFlag ? (8 | 2) : 0) | (wirelessUp ? 4 : 0);
            r.overall = (wiredUpForOverall || wirelessUp) ? STATUS.HEALTHY : STATUS.FAULTY;
        }

        for (var j = 0; j < nwDevs.length; j++) {
            var dev = nwDevs[j];
            var key = (dev.clusterId != null) ? String(dev.clusterId) : '';
            var v = byCluster[key];
            if (!v) { continue; }
            dev.flagValue = v.flagValue;
            dev.status = v.overall;
            dev.effectiveStatus = v.overall;
            dev.wiredMasked = !!v.wiredMasked;
        }
    }

    /* Rebuild the whole model for a site from its accumulated rows. */
    function buildModelFromRows(siteId) {
        var sid = String(siteId);
        var map = rawRows[sid] || {};
        var iotDevs = [], nwDevs = [], gwDevs = [];
        var latestUpdate = null;
        var siteName = null;

        for (var k in map) {
            if (!map.hasOwnProperty(k)) { continue; }
            var dev = translateRow(map[k]);
            if (!dev) { continue; }

            if (dev.category === 'IOT') { iotDevs.push(dev); }
            else if (dev.category === 'NW') { nwDevs.push(dev); }
            else if (dev.category === 'GW') { gwDevs.push(dev); }

            if (dev.siteName && !siteName) { siteName = dev.siteName; }
            if (dev.updateTime) {
                var devT = new Date(String(dev.updateTime).replace(' ', 'T'));
                if (!isNaN(devT.getTime())) {
                    if (!latestUpdate) {
                        latestUpdate = dev.updateTime;
                    } else {
                        var curT = new Date(String(latestUpdate).replace(' ', 'T'));
                        if (isNaN(curT.getTime()) || devT > curT) {
                            latestUpdate = dev.updateTime;
                        }
                    }
                }
            }
        }

        collapseNetworkToClusterVerdict(nwDevs, sid);

        var iot = rollupRows('IOT', iotDevs);
        var nw = rollupRows('NW', nwDevs);
        var gw = rollupRows('GW', gwDevs);

        /* Gate Network(modem) on the Station Gateway so a modem can never
           read Healthy before its parent gateway does. */
        applyGatewayGate(nw, gw.status);

        var numericSiteId = parseInt(siteId, 10);

        return {
            siteId: isNaN(numericSiteId) ? siteId : numericSiteId,
            siteName: siteName,
            totalAlerts: iot.faulty + nw.faulty + gw.faulty,
            updateTime: latestUpdate,
            iot: iot,
            nw: nw,
            gw: gw,
            devices: iotDevs.concat(nwDevs).concat(gwDevs),
            siteStatus: worstOf([iot.status, nw.status, gw.status])
        };
    }

    /* Rebuild + emit once for every site touched during the current frame. */
    function flushDirtySites() {
        for (var sid in _dirtySites) {
            if (!_dirtySites.hasOwnProperty(sid)) { continue; }
            var model = buildModelFromRows(sid);
            cache[model.siteId] = model;
            fire('onSummary', [model.siteId, model, null]);
        }
        _dirtySites = {};
    }

    /* ------------------------------------------------------- socket manager */

    var sockets = {};   /* siteId -> { ws, attempts, timer, closing } */
    var cache = {};     /* siteId -> last normalized summary model */
    var handlers = {};

    /* ---- new flat per-row feed (Set / Reset / Healthy) state ---------------
     * rawRows[siteId] holds one raw row per (deviceType|deviceId|channel).
     * A snapshot fills it in one batch; each live update overwrites one key.
     * The model rebuild is deferred to flushDirtySites() (called once when the
     * outermost handleMessage() unwinds) so a whole snapshot rebuilds once,
     * not once per row. */
    var rawRows = {};       /* siteId -> { rowKey: rawRow } */
    var _handleDepth = 0;   /* re-entrancy guard for batch recursion */
    var _dirtySites = {};   /* siteId -> true, sites touched this frame */

    function wsUrlFor(siteId, path) {
        var baseUrl =
            String(CONFIG.wsBase || '')
                .replace(/\/+$/, '');

        var url =
            baseUrl +
            (path || CONFIG.path) +
            encodeURIComponent(siteId);

        /*
         * Same secure WebSocket token logic used by Telemetry Live.
         */
        if (
            window.location.protocol === 'https:' &&
            CONFIG.authToken
        ) {
            var separator =
                url.indexOf('?') >= 0
                    ? '&'
                    : '?';

            url +=
                separator +
                'token=' +
                encodeURIComponent(CONFIG.authToken);
        }

        return url;
    }

    /*
     * A ws:// socket cannot be opened from an https:// page -- the browser
     * blocks it as mixed content with no visible error beyond the console.
     * Surface it loudly so it is not mistaken for a dead backend.
     */
    function checkMixedContent(url) {
        if (window.location.protocol === 'https:' && /^ws:\/\//i.test(url)) {
            var msg = 'Blocked: cannot open an insecure ws:// socket from an ' +
                'https:// page. Serve the feed over wss:// or proxy it ' +
                'through the application server. (' + url + ')';
            if (window.console) { console.error('[RdpmsHealthLive] ' + msg); }
            fire('onError', [null, msg]);
            return false;
        }
        return true;
    }

    function fire(name, args) {
        var fn = handlers[name];
        if (typeof fn !== 'function') { return; }
        try { fn.apply(null, args || []); }
        catch (e) { if (window.console) { console.error('[RdpmsHealthLive] handler ' + name, e); } }
    }

    function openSocket(siteId) {
        if (!window.WebSocket) {
            fire('onError', [siteId, 'WebSocket not supported by this browser']);
            return;
        }

        var url = wsUrlFor(siteId);
        if (!checkMixedContent(url)) { return; }

        //var entry = sockets[siteId] || (sockets[siteId] = { ws: null, attempts: 0, timer: null, closing: false });
        //entry.closing = false;

        //var ws;
        //try { ws = new WebSocket(url); }
        //catch (e) {
        //    fire('onError', [siteId, 'WebSocket construction failed: ' + e.message]);
        //    scheduleReconnect(siteId);
        //    return;
        //}
        //entry.ws = ws;

        //ws.onopen = function () {
        //    entry.attempts = 0;
        var entry = sockets[siteId] || (sockets[siteId] = { ws: null, attempts: 0, timer: null, closing: false });
        entry.closing = false;

        /* Same generation guard as openSensorSocket - see discardSocket(). */
        if (entry.timer) { clearTimeout(entry.timer); entry.timer = null; }
        if (socketIsLive(entry)) { return; }
        discardSocket(entry.ws);
        entry.ws = null;

        var ws;
        try { ws = new WebSocket(url); }
        catch (e) {
            fire('onError', [siteId, 'WebSocket construction failed: ' + e.message]);
            scheduleReconnect(siteId);
            return;
        }
        entry.ws = ws;

        /*
         * Never allow a connection attempt to occupy a browser socket slot forever.
         */
        if (entry.connectTimer) {
            clearTimeout(entry.connectTimer);
            entry.connectTimer = null;
        }

        entry.connectTimer = setTimeout(function () {
            if (entry.ws !== ws || ws.readyState !== WebSocket.CONNECTING) { return; }

            entry.connectTimer = null;
            entry.ws = null;

            discardSocket(ws);

            fire('onError', [siteId, 'deviceHealth connection timeout']);

            if (!entry.closing) {
                scheduleReconnect(siteId);
            }
        }, CONFIG.connectTimeoutMs);

        ws.onopen = function () {
            if (entry.ws !== ws) { return; }

            if (entry.connectTimer) {
                clearTimeout(entry.connectTimer);
                entry.connectTimer = null;
            }

            entry.attempts = 0;
            /* Stamp a deferred-clear flag instead of deleting rawRows here.
               Between onopen and the first snapshot row the store would be
               empty, causing buildModelFromRows() to emit an all-Healthy model
               with 0 faulty — the "flash to healthy on reconnect" symptom.
               ingestDeviceHealthRow() will wipe and replace on the first row. */
            entry.snapshotFlushed = false;
            fire('onOpen', [siteId]);
        };

        ws.onmessage = function (evt) {
            if (entry.ws !== ws) { return; }
            handleMessage(siteId, evt.data);
        };

        ws.onerror = function () {
            if (entry.ws !== ws) { return; }
            fire('onError', [siteId, 'socket error']);
        };

        ws.onclose = function () {
            if (entry.ws !== ws) { return; }

            if (entry.connectTimer) {
                clearTimeout(entry.connectTimer);
                entry.connectTimer = null;
            }

            entry.ws = null;

            fire('onClose', [siteId]);

            if (!entry.closing) {
                scheduleReconnect(siteId);
            }
        };
    }

    /* ── sensorHealth transport ─────────────────────────────────────────
   Reuses wsUrlFor() and checkMixedContent() so this feed resolves its
   host, token and protocol EXACTLY as deviceHealth does. Raw frames go
   to the page untouched; parsing and the set/reset health rule live in
   the caller (_List.cshtml). ────────────────────────────────────── */
    var sensorSockets = {};      /* siteId -> { ws, attempts, timer, closing } */
    var sensorOnFrame = null;    /* function(siteId, rawString) */

    //function openSensorSocket(siteId) {
    //    if (!window.WebSocket) {
    //        fire('onError', [siteId, 'WebSocket not supported by this browser']);
    //        return;
    //    }

    //    var url = wsUrlFor(siteId, CONFIG.sensorPath);
    //    if (!checkMixedContent(url)) { return; }

    //    var entry = sensorSockets[siteId] ||
    //        (sensorSockets[siteId] = {
    //            ws: null, attempts: 0, timer: null, closing: false
    //        });
    //    entry.closing = false;

    //    var ws;
    //    try { ws = new WebSocket(url); }
    //    catch (e) {
    //        fire('onError', [siteId, 'sensorHealth construction failed: ' + e.message]);
    //        scheduleSensorReconnect(siteId);
    //        return;
    //    }
    //    entry.ws = ws;

    //    ws.onopen = function () { entry.attempts = 0; };

    //    ws.onmessage = function (evt) {
    //        if (typeof sensorOnFrame === 'function') {
    //            try { sensorOnFrame(siteId, evt.data); }
    //            catch (e2) {
    //                if (window.console) {
    //                    console.error('[RdpmsHealthLive] sensor frame', e2);
    //                }
    //            }
    //        }
    //    };

    //    ws.onerror = function () {
    //        fire('onError', [siteId, 'sensorHealth socket error']);
    //    };

    //    ws.onclose = function () {
    //        if (!entry.closing) { scheduleSensorReconnect(siteId); }
    //    };
    //}

    //function scheduleSensorReconnect(siteId) {
    //    var entry = sensorSockets[siteId];
    //    if (!entry || entry.closing) { return; }
    //    if (entry.attempts >= CONFIG.maxReconnectAttempts) {
    //        fire('onError', [siteId,
    //            'sensorHealth: giving up after ' + entry.attempts + ' attempts']);
    //        return;
    //    }
    //    entry.attempts++;
    //    var delay = Math.min(
    //        CONFIG.reconnectBaseMs * Math.pow(2, entry.attempts - 1),
    //        CONFIG.reconnectMaxMs);
    //    if (entry.timer) { clearTimeout(entry.timer); }
    //    entry.timer = setTimeout(function () { openSensorSocket(siteId); }, delay);
    //}


    /* ── Socket generation guard ────────────────────────────────────────
   Handlers below close over `entry`, NOT over the socket they were
   bound to. Without this helper an old socket that closes later still
   reaches the live `entry`, sees closing === false and schedules a
   reconnect - so one stale socket spawns a second live socket, whose
   own stale predecessor spawns another, and the count doubles until
   the browser refuses with "Insufficient resources".

   discardSocket() strips the handlers off the superseded socket and
   closes it, so it can never call back into the entry it no longer
   owns. Every handler additionally re-checks entry.ws === ws before
   acting, which covers sockets discarded by any other path. */
    function discardSocket(ws) {
        if (!ws) { return; }

        /*
         * Mark it dead FIRST. No caller may consider this socket reusable after
         * teardown has started.
         */
        ws.__rdpmsDiscarded = true;

        /*
         * Detach every handler before closing so an intentionally discarded
         * socket can never schedule another reconnect.
         */
        try {
            ws.onopen = null;
            ws.onmessage = null;
            ws.onerror = null;
            ws.onclose = null;
        } catch (e) { }

        /*
         * IMPORTANT:
         * close() is valid for both CONNECTING and OPEN WebSockets.
         *
         * Waiting for CONNECTING -> OPEN before closing is exactly what leaked
         * sockets: a connection whose handshake never completes can remain alive
         * indefinitely and continue consuming a Chrome socket resource.
         */
        try {
            if (ws.readyState === WebSocket.CONNECTING ||
                ws.readyState === WebSocket.OPEN) {
                ws.close();
            }
        }
        catch (e2) {
            /*
             * Very defensive fallback for an unusual browser implementation.
             * Do not restore error/close handlers because this socket no longer
             * belongs to any active entry.
             */
            try {
                ws.onopen = function () {
                    try { ws.close(); } catch (ignored) { }
                };
            }
            catch (ignored2) { }
        }
    }

    /* True when the entry already owns a usable socket, so opening another
       would just orphan one. */
    function socketIsLive(entry) {
        /* A socket that discardSocket() has already given up on is NOT live,
           even while it finishes its handshake. Counting it as live suppressed
           the real reconnect; leaving it in the slot let a duplicate socket
           open. Either way the one-shot snapshot went to the wrong socket. */
        return !!(entry && entry.ws && !entry.ws.__rdpmsDiscarded &&
            (entry.ws.readyState === 0 || entry.ws.readyState === 1));
    }

    /* Bumped by disconnectSensor(). Every socket records the generation it was
    opened under; handlers act only while it still matches. */
    var _sensorGen = 0;

    function openSensorSocket(siteId) {
        if (!window.WebSocket) {
            fire('onError', [siteId, 'WebSocket not supported by this browser']);
            return;
        }

        var myGen = _sensorGen;

        var url = wsUrlFor(siteId, CONFIG.sensorPath);
        if (!checkMixedContent(url)) { return; }

        var entry = sensorSockets[siteId] ||
            (sensorSockets[siteId] = {
                ws: null, attempts: 0, timer: null, closing: false
            });
        entry.closing = false;

        /* A pending reconnect timer must not survive an explicit open. */
        if (entry.timer) { clearTimeout(entry.timer); entry.timer = null; }

        /* Never stack two sockets on one station. */
        if (socketIsLive(entry)) { return; }
        discardSocket(entry.ws);
        entry.ws = null;

        var ws;
        try { ws = new WebSocket(url); }
        catch (e) {
            fire('onError', [siteId, 'sensorHealth construction failed: ' + e.message]);
            scheduleSensorReconnect(siteId);
            return;
        }
        entry.ws = ws;

        /*
         * sensorHealth gets the same CONNECTING timeout as deviceHealth.
         */
        if (entry.connectTimer) {
            clearTimeout(entry.connectTimer);
            entry.connectTimer = null;
        }

        entry.connectTimer = setTimeout(function () {
            if (entry.ws !== ws || ws.readyState !== WebSocket.CONNECTING) { return; }

            entry.connectTimer = null;
            entry.ws = null;

            discardSocket(ws);

            fire('onError', [siteId, 'sensorHealth connection timeout']);

            if (!entry.closing) {
                scheduleSensorReconnect(siteId);
            }
        }, CONFIG.connectTimeoutMs);

        ws.onopen = function () {
            if (entry.ws !== ws) { return; }

            if (entry.connectTimer) {
                clearTimeout(entry.connectTimer);
                entry.connectTimer = null;
            }

            entry.attempts = 0;
        };

        ws.onmessage = function (evt) {
            if (entry.ws !== ws) { return; }
            if (typeof sensorOnFrame === 'function') {
                try { sensorOnFrame(siteId, evt.data); }
                catch (e2) {
                    if (window.console) {
                        console.error('[RdpmsHealthLive] sensor frame', e2);
                    }
                }
            }
        };

        ws.onerror = function () {
            if (entry.ws !== ws) { return; }
            fire('onError', [siteId, 'sensorHealth socket error']);
        };

        ws.onclose = function () {
            /* Superseded socket - the entry has moved on, stay silent. */
            if (entry.ws !== ws) { return; }

            if (entry.connectTimer) {
                clearTimeout(entry.connectTimer);
                entry.connectTimer = null;
            }

            /*
             * Opened under an older search generation.
             * Never allow it to reconnect.
             */
            if (myGen !== _sensorGen) {
                entry.ws = null;
                return;
            }

            entry.ws = null;

            if (!entry.closing) {
                scheduleSensorReconnect(siteId);
            }
        };
    }

    function scheduleSensorReconnect(siteId) {
        var entry = sensorSockets[siteId];
        if (!entry || entry.closing) { return; }
        /* A live socket already exists - nothing to reconnect. */
        if (socketIsLive(entry)) { return; }
        if (entry.attempts >= CONFIG.maxReconnectAttempts) {
            fire('onError', [siteId,
                'sensorHealth: giving up after ' + entry.attempts + ' attempts']);
            return;
        }
        entry.attempts++;
        var delay = Math.min(
            CONFIG.reconnectBaseMs * Math.pow(2, entry.attempts - 1),
            CONFIG.reconnectMaxMs);
        if (entry.timer) { clearTimeout(entry.timer); }
        entry.timer = setTimeout(function () {
            entry.timer = null;
            openSensorSocket(siteId);
        }, delay);
    }


    function scheduleReconnect(siteId) {
        var entry = sockets[siteId];
        if (!entry || entry.closing) { return; }

        /* Do not schedule another connection when this site already has
           a CONNECTING or OPEN socket. */
        if (socketIsLive(entry)) { return; }

        if (entry.attempts >= CONFIG.maxReconnectAttempts) {
            fire('onError', [siteId, 'giving up after ' + entry.attempts + ' reconnect attempts']);
            return;
        }

        entry.attempts++;

        var delay = Math.min(
            CONFIG.reconnectBaseMs * Math.pow(2, entry.attempts - 1),
            CONFIG.reconnectMaxMs
        );

        if (entry.timer) {
            clearTimeout(entry.timer);
            entry.timer = null;
        }

        entry.timer = setTimeout(function () {
            entry.timer = null;

            if (entry.closing || socketIsLive(entry)) { return; }

            openSocket(siteId);
        }, delay);
    }

    /*
     * Envelope shape: { siteId, messageType, summary, device, siteSummary }
     * and the feed may wrap it in an array. Dispatch on which payload field is
     * populated rather than on messageType, since only "snapshot" has been
     * observed and the other type strings are unknown.
     */
    //function handleMessage(siteId, raw) {
    //    var parsed;
    //    try { parsed = (typeof raw === 'string') ? JSON.parse(raw) : raw; }
    //    catch (e) {
    //        fire('onError', [siteId, 'unparseable frame']);
    //        return;
    //    }
    //    if (!parsed) { return; }

    //    var envelopes = $.isArray(parsed) ? parsed : [parsed];
    //    for (var i = 0; i < envelopes.length; i++) {
    //        var env = envelopes[i];
    //        if (!env) { continue; }

    //        var envSiteId = pickNum(env, ['siteId', 'SiteId']) || siteId;
    //        var summary = pick(env, ['summary', 'Summary']);
    //        var device = pick(env, ['device', 'Device']);
    //        var siteSummary = pick(env, ['siteSummary', 'SiteSummary']);

    //        /* Bare summary object with no envelope wrapper. */
    //        if (!summary && !device && !siteSummary &&
    //            (env.iotFlagValue !== undefined || env.devices !== undefined)) {
    //            summary = env;
    //        }

    //        if (summary) {
    //            var model = normalizeSummary(summary);
    //            if (model) {
    //                if (model.siteId === null) { model.siteId = envSiteId; }
    //                cache[model.siteId] = model;
    //                fire('onSummary', [model.siteId, model, env]);
    //            }
    //        }

    //        if (device) {
    //            var dev = normalizeDevice(device);
    //            if (dev) {
    //                fire('onDevice', [envSiteId, dev, env]);
    //                var patched = applyDeviceDelta(envSiteId, dev);
    //                if (patched) { fire('onSummary', [envSiteId, patched, env]); }
    //            }
    //        }

    //        if (siteSummary) {
    //            fire('onSiteSummary', [envSiteId, siteSummary, env]);
    //        }
    //    }
    //}


    /* Public entry: depth-guarded so a batch (which recurses through
       dispatchMessage -> handleMessage per row) only flushes the rebuilt
       model once, when the outermost call unwinds. */
    function handleMessage(siteId, raw) {
        /* Diagnostic tap - see window.rdpmsWsLog in _List.cshtml.
           Placed before parsing so malformed frames are captured too.
           No-op unless rdpmsWsLog.on() has been called. */
        if (window.rdpmsWsLog && window.rdpmsWsLog._push) {
            try { window.rdpmsWsLog._push(siteId, raw); } catch (e) { }
        }

        _handleDepth++;
        try {
            dispatchMessage(siteId, raw);
        } finally {
            _handleDepth--;
            if (_handleDepth <= 0) {
                _handleDepth = 0;
                flushDirtySites();
            }
        }
    }

    function dispatchMessage(siteId, raw) {
        var parsed;

        try {
            parsed =
                typeof raw === 'string'
                    ? JSON.parse(raw)
                    : raw;
        }
        catch (e) {
            fire('onError', [
                siteId,
                'unparseable frame: ' + e.message
            ]);

            return;
        }

        if (!parsed) {
            return;
        }

        /*
         * FRS Advance WebSocket batch format:
         *
         * {
         *     "MessageType": "Batch",
         *     "Topic": "deviceHealth/155",
         *     "MessagesCount": 1,
         *     "Messages": [
         *         "{\"siteId\":155,\"messageType\":\"snapshot\",...}"
         *     ]
         * }
         *
         * Each Messages entry can itself be:
         *   1. A JSON string.
         *   2. An already-parsed object.
         *   3. Another array or batch wrapper.
         *
         * Process every inner entry through the same handler so both the
         * FRS Advance batch format and the previous direct-envelope format
         * remain supported.
         */
        var batchMessages = pick(parsed, [
            'Messages',
            'messages'
        ]);

        if ($.isArray(batchMessages)) {
            for (
                var batchIndex = 0;
                batchIndex < batchMessages.length;
                batchIndex++
            ) {
                var batchMessage =
                    batchMessages[batchIndex];

                if (
                    batchMessage === undefined ||
                    batchMessage === null ||
                    batchMessage === ''
                ) {
                    continue;
                }

                handleMessage(
                    siteId,
                    batchMessage
                );
            }

            return;
        }

        var envelopes =
            $.isArray(parsed)
                ? parsed
                : [parsed];

        for (
            var i = 0;
            i < envelopes.length;
            i++
        ) {
            var env = envelopes[i];

            /*
             * Some feeds can return an array containing JSON strings.
             */
            if (typeof env === 'string') {
                handleMessage(
                    siteId,
                    env
                );

                continue;
            }

            if (!env) {
                continue;
            }

            var envSiteId =
                pickNum(env, [
                    'siteId',
                    'SiteId'
                ]) || siteId;

            /*
             * NEW FEED: flat per-row device-health record. Accumulate it and
             * defer the model rebuild to flushDirtySites() so a full snapshot
             * batch rebuilds once. This short-circuits the legacy envelope
             * handling below (flag/summary based), which no longer applies.
             */
            if (isDeviceHealthRow(env)) {
                ingestDeviceHealthRow(envSiteId, env);
                _dirtySites[String(envSiteId)] = true;
                continue;
            }

            /* Per-site roll-up: hand the raw snapshot to the page and stop.
               It does not feed rawRows, so the grid keeps using flat rows. */
            if (isGroupHealthSnapshot(env)) {
                /* Index masks BEFORE the page handler runs, so its repaint
                   already sees them; rebuild the flat model too (only when this
                   site has rows) so Network counts pick up masked Wired. */
                ingestGroupHealthMask(envSiteId, env);
                if (rawRows[String(envSiteId)]) {
                    _dirtySites[String(envSiteId)] = true;
                }
                fire('onGroupHealth', [envSiteId, env]);
                continue;
            }

            /*
             * LEGACY FLAG SNAPSHOT -- IGNORED (site directive).
             * Dropped before it can reach normalizeSummary(); the legacy
             * handling below is retained but is now unreachable for this
             * shape. Warned once per session so a server still emitting the
             * old format is visible in DevTools rather than silently dead.
             */
            if (isLegacySnapshotEnvelope(env)) {
                if (!_legacySnapshotWarned) {
                    _legacySnapshotWarned = true;
                    if (window.console) {
                        console.warn('[RdpmsHealthLive] legacy flag snapshot ignored (site ' +
                            envSiteId + ') -- flat deviceHealth rows are the only supported source');
                    }
                }
                continue;
            }

            var summary =
                pick(env, [
                    'summary',
                    'Summary'
                ]);

            var device =
                pick(env, [
                    'device',
                    'Device'
                ]);

            var siteSummary =
                pick(env, [
                    'siteSummary',
                    'SiteSummary'
                ]);

            /*
             * Also support a bare summary object without the normal envelope.
             */
            if (
                !summary &&
                !device &&
                !siteSummary &&
                (
                    env.iotFlagValue !== undefined ||
                    env.IotFlagValue !== undefined ||
                    env.nwFlagValue !== undefined ||
                    env.NwFlagValue !== undefined ||
                    env.devices !== undefined ||
                    env.Devices !== undefined
                )
            ) {
                summary = env;
            }

            if (summary) {
                var model =
                    normalizeSummary(summary);

                if (model) {
                    if (
                        model.siteId === null ||
                        model.siteId === undefined
                    ) {
                        model.siteId =
                            envSiteId;
                    }

                    cache[model.siteId] =
                        model;

                    fire(
                        'onSummary',
                        [
                            model.siteId,
                            model,
                            env
                        ]
                    );
                }
            }

            if (device) {
                var dev =
                    normalizeDevice(device);

                if (dev) {
                    fire(
                        'onDevice',
                        [
                            envSiteId,
                            dev,
                            env
                        ]
                    );

                    var patched =
                        applyDeviceDelta(
                            envSiteId,
                            dev
                        );

                    if (patched) {
                        fire(
                            'onSummary',
                            [
                                envSiteId,
                                patched,
                                env
                            ]
                        );
                    }
                }
            }

            if (siteSummary) {
                fire(
                    'onSiteSummary',
                    [
                        envSiteId,
                        siteSummary,
                        env
                    ]
                );
            }
        }
    }

    /*
     * Merge a single-device update into the cached snapshot and recompute
     * rollups, so incremental frames keep the counters correct without
     * waiting for the next full snapshot.
     */
    function applyDeviceDelta(siteId, dev) {
        var model = cache[siteId];
        if (!model || !dev.deviceId) { return null; }

        var replaced = false;
        for (var i = 0; i < model.devices.length; i++) {
            if (model.devices[i].deviceId === dev.deviceId &&
                model.devices[i].category === dev.category) {
                model.devices[i] = dev; replaced = true; break;
            }
        }
        if (!replaced) { model.devices.push(dev); }

        model.iot = rollup('IOT', filterCategory(model.devices, 'IOT'),
            model.iot.flagValue, model.iot.serverStatus, null);
        model.nw = rollup('NW', filterCategory(model.devices, 'NW'),
            model.nw.flagValue, model.nw.serverStatus, null);
        model.gw = rollup('GW', filterCategory(model.devices, 'GW'),
            model.gw.flagValue, model.gw.serverStatus, null);
        applyIotGate(model.iot, model.nw.flagValue);
        applyNetworkChannels(model.nw);
        model.updateTime = dev.updateTime || model.updateTime;
        model.siteStatus = worstOf([model.iot.status, model.nw.status, model.gw.status]);

        cache[siteId] = model;
        return model;
    }

    /* ------------------------------------------------------------ DOM bind */

    /*
     * Convention-based binder, so existing markup can be wired without
     * renaming anything. Tag elements like:
     *
     *   <span data-rdpms-health="iot.healthy"></span>
     *   <span data-rdpms-health="nw.faulty"></span>
     *   <span data-rdpms-health="gw.partial"></span>
     *   <span data-rdpms-health="gw.status"></span>
     *   <span data-rdpms-health="site.totalAlerts"></span>
     *
     * Add data-rdpms-site="155" to scope an element to one station; elements
     * without it are updated by every site's frames (fine for single-site).
     *
     * Paths: {iot|nw|gw}.{total|healthy|partial|faulty|faultyEffective|status}
     *        site.{status|totalAlerts|siteName|updateTime}
     */
    function bindDom(siteId, model, rootSelector) {
        var $root = $(rootSelector || document);

        $root.find('[data-rdpms-health]').each(function () {
            var $el = $(this);
            var scoped = $el.attr('data-rdpms-site');
            if (scoped && String(scoped) !== String(siteId)) { return; }

            var val = resolvePath(model, $el.attr('data-rdpms-health'));
            if (val === undefined || val === null) { return; }
            $el.text(val);

            /* status slots also get a class for colouring */
            if (/\.status$/.test($el.attr('data-rdpms-health'))) {
                $el.removeClass('rdpms-healthy rdpms-partial rdpms-faulty rdpms-unknown')
                    .addClass('rdpms-' + String(val).toLowerCase());
            }
        });
    }

    function resolvePath(model, path) {
        if (!path) { return null; }
        var parts = String(path).split('.');
        if (parts.length !== 2) { return null; }
        var group = parts[0], field = parts[1];

        if (group === 'site') {
            if (field === 'status') { return model.siteStatus; }
            return model[field];
        }
        var g = model[group];
        if (!g) { return null; }
        return g[field];
    }

    /* ---------------------------------------------------------------- public */

    function connect(siteIds, opts) {
        disconnect();   /* never leak sockets across a re-search */

        opts = opts || {};
        handlers = {
            onOpen: opts.onOpen,
            onClose: opts.onClose,
            onError: opts.onError,
            onSummary: opts.onSummary,
            onGroupHealth: opts.onGroupHealth,
            onDevice: opts.onDevice,
            onSiteSummary: opts.onSiteSummary
        };
        if (opts.wsBase) { CONFIG.wsBase = opts.wsBase; }

        var ids = $.isArray(siteIds) ? siteIds : [siteIds];
        for (var i = 0; i < ids.length; i++) {
            var id = parseInt(ids[i], 10);
            if (!isNaN(id) && id > 0) { openSocket(id); }
        }
    }

    function disconnect() {
        for (var id in sockets) {
            if (!sockets.hasOwnProperty(id)) { continue; }

            var e = sockets[id];
            e.closing = true;

            if (e.timer) {
                clearTimeout(e.timer);
                e.timer = null;
            }

            if (e.connectTimer) {
                clearTimeout(e.connectTimer);
                e.connectTimer = null;
            }

            /*
             * IMPORTANT:
             * Always use discardSocket().
                         *
                         * Direct ws.close() is unsafe while readyState === CONNECTING.
                         * discardSocket() marks that socket discarded and closes it as soon
                         * as the handshake finishes, preventing orphan connections from
                         * accumulating after Search/reload.
                         */
            if (e.ws) {
                discardSocket(e.ws);
                e.ws = null;
            }
        }

        sockets = {};
        cache = {};
        rawRows = {};
        _dirtySites = {};
        _handleDepth = 0;
        window.__rdpmsGroupMask = {};   /* masks are per-search, like rawRows */

        /*
         * disconnectSensor() already uses discardSocket(), so both pools now use
         * the same safe teardown path.
         */
        if (window.RdpmsHealthLive &&
            typeof window.RdpmsHealthLive.disconnectSensor === 'function') {
            window.RdpmsHealthLive.disconnectSensor();
        }
    }

    $(window).on('beforeunload', disconnect);

    window.RdpmsHealthLive = {
        connect: connect,
        disconnect: disconnect,
        bindDom: bindDom,
        classify: classify,
        classifyIotGated: classifyIotGated,
        classifyNetwork: classifyNetwork,
        effectiveStatus: effectiveStatus,
        downBits: downBits,
        normalizeSummary: normalizeSummary,
        normalizeDevice: normalizeDevice,
        /* new flat-feed helpers (exposed for debugging / unit checks) */
        translateRow: translateRow,
        buildModelFromRows: buildModelFromRows,
        getRawRows: function (siteId) { return rawRows[String(siteId)] || null; },

        /* Open the sensorHealth feed for one or more stations.
           onFrame receives (siteId, rawString) for every frame. */
        connectSensor: function (siteIds, onFrame) {
            this.disconnectSensor();
            sensorOnFrame = onFrame || null;

            var list = (Object.prototype.toString.call(siteIds) === '[object Array]')
                ? siteIds : [siteIds];

            for (var i = 0; i < list.length; i++) {
                if (list[i] == null || list[i] === '') { continue; }
                openSensorSocket(String(list[i]));
            }
        },

        /* True only while at least one sensor socket is genuinely OPEN or
   CONNECTING and has not been discarded. shConnect() must test this
   instead of trusting a local "_shSocket = true" flag that is never
   cleared when a socket dies — that stale flag made every reconnect
   attempt (including the 60 s timer) return early, so once the socket
   was gone the sensor feed never came back. */
        isSensorLive: function () {
            for (var sid in sensorSockets) {
                if (!sensorSockets.hasOwnProperty(sid)) { continue; }
                var e = sensorSockets[sid];
                if (e && e.ws && !e.ws.__rdpmsDiscarded &&
                    (e.ws.readyState === 0 || e.ws.readyState === 1)) {
                    return true;
                }
            }
            return false;
        },

        disconnectSensor: function () {
            for (var sid in sensorSockets) {
                if (!sensorSockets.hasOwnProperty(sid)) { continue; }
                var e = sensorSockets[sid];
                if (!e) { continue; }
                e.closing = true;

                if (e.timer) {
                    clearTimeout(e.timer);
                    e.timer = null;
                }

                if (e.connectTimer) {
                    clearTimeout(e.connectTimer);
                    e.connectTimer = null;
                }

                /* Detach handlers before closing: a socket still in
                                   CONNECTING holds a slot and would otherwise fire onclose
                                   after this entry is gone. */
                discardSocket(e.ws);
                e.ws = null;
            }
            sensorSockets = {};
            sensorOnFrame = null;
            /* Invalidate every socket opened before this point, including any
               still in CONNECTING whose onclose has not fired yet. */
            _sensorGen++;
        },
        getCached: function (siteId) { return cache[siteId] || null; },
        getGroupMask: groupMaskFor,
        STATUS: STATUS,
        CATEGORY_SPEC: CATEGORY_SPEC,
        CONFIG: CONFIG
    };

})(window, jQuery);