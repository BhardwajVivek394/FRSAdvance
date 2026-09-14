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

        reconnectBaseMs:
            2000,

        reconnectMaxMs:
            30000,

        maxReconnectAttempts:
            10
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
            if (s === 'healthy') { return STATUS.HEALTHY; }
            if (s === 'faulty' || s === 'down' || s === 'offline') { return STATUS.FAULTY; }
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
        if (v === 'healthy') { return STATUS.HEALTHY; }
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

    /* ------------------------------------------------------- socket manager */

    var sockets = {};   /* siteId -> { ws, attempts, timer, closing } */
    var cache = {};     /* siteId -> last normalized summary model */
    var handlers = {};

    function wsUrlFor(siteId) {
        var baseUrl =
            String(CONFIG.wsBase || '')
                .replace(/\/+$/, '');

        var url =
            baseUrl +
            CONFIG.path +
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

        var entry = sockets[siteId] || (sockets[siteId] = { ws: null, attempts: 0, timer: null, closing: false });
        entry.closing = false;

        var ws;
        try { ws = new WebSocket(url); }
        catch (e) {
            fire('onError', [siteId, 'WebSocket construction failed: ' + e.message]);
            scheduleReconnect(siteId);
            return;
        }
        entry.ws = ws;

        ws.onopen = function () {
            entry.attempts = 0;
            fire('onOpen', [siteId]);
        };

        ws.onmessage = function (evt) {
            handleMessage(siteId, evt.data);
        };

        ws.onerror = function () {
            fire('onError', [siteId, 'socket error']);
        };

        ws.onclose = function () {
            fire('onClose', [siteId]);
            if (!entry.closing) { scheduleReconnect(siteId); }
        };
    }

    function scheduleReconnect(siteId) {
        var entry = sockets[siteId];
        if (!entry || entry.closing) { return; }
        if (entry.attempts >= CONFIG.maxReconnectAttempts) {
            fire('onError', [siteId, 'giving up after ' + entry.attempts + ' reconnect attempts']);
            return;
        }
        entry.attempts++;
        var delay = Math.min(CONFIG.reconnectBaseMs * Math.pow(2, entry.attempts - 1),
            CONFIG.reconnectMaxMs);
        if (entry.timer) { clearTimeout(entry.timer); }
        entry.timer = setTimeout(function () { openSocket(siteId); }, delay);
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
    function handleMessage(siteId, raw) {
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
            if (e.timer) { clearTimeout(e.timer); e.timer = null; }
            if (e.ws) {
                e.ws.onopen = e.ws.onmessage = e.ws.onerror = e.ws.onclose = null;
                try { e.ws.close(); } catch (ignored) { }
                e.ws = null;
            }
        }
        sockets = {};
        cache = {};
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
        getCached: function (siteId) { return cache[siteId] || null; },
        STATUS: STATUS,
        CATEGORY_SPEC: CATEGORY_SPEC,
        CONFIG: CONFIG
    };

})(window, jQuery);