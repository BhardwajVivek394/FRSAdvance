/**
 * Energy7 AI Assistant — Chat Sidebar (Final)
 * Domain: Indian Railways RDPMS — Track Circuits, Signals, Point Machines
 * POST /FRS25/AiChat/Chat  |  GET /FRS25/AiChat/Status
 * Uses: ai-chat-sidebar.css
 */
(function () {
    'use strict';

    const CHAT_ENDPOINT = '/FRS25/AiChat/Chat';
    const STATUS_ENDPOINT = '/FRS25/AiChat/Status';
    const STORAGE_KEY = 'e7ai_history';
    const MAX_HISTORY = 20;

    /* ══════════════════════════════════════════════════════════
       SCOPE — only RDPMS domain allowed
    ══════════════════════════════════════════════════════════ */
    const OUT_OF_SCOPE = [
        'weather', 'recipe', 'cook', 'poem', 'joke', 'capital of',
        'javascript', 'python', '2+2', 'math', 'history of india',
        'news', 'stock', 'bitcoin', 'translate', 'who is', 'president',
        'cricket', 'football', 'movie', 'song', 'write a story',
        'write an essay', 'email', 'time in'
    ];

    /* ══════════════════════════════════════════════════════════
       PROMPT CATEGORIES
       All prompts are concrete, tool-callable, scope-safe.
       Each requires: asset name OR site name from user input.
    ══════════════════════════════════════════════════════════ */
    const PROMPT_CATEGORIES = [
        {
            id: 'alerts',
            icon: '🚨',
            label: 'Alerts',
            prompts: [
                { label: 'Active alerts on a site', ask: 'site', tpl: 'Show all active alerts for site {input}' },
                { label: 'Active alerts for an asset', ask: 'asset', tpl: 'Show active alerts for asset {input}' },
                { label: 'Alert history — last 7 days', ask: 'asset', tpl: 'Show alert history for asset {input} over the last 7 days' },
                { label: 'Alert summary — site', ask: 'site', tpl: 'Give an alert summary for site {input}' },
            ]
        },
        {
            id: 'live',
            icon: '📊',
            label: 'Live Values',
            prompts: [
                { label: 'All current values — asset', ask: 'asset', tpl: 'Show all current tag values for asset {input}' },
                { label: 'All current values — site', ask: 'site', tpl: 'Show all current values for site {input}' },
                { label: 'TPR state', ask: 'asset', tpl: 'What is the current TPR state of {input}?' },
                { label: 'Feed current (If mA)', ask: 'asset', tpl: 'What is the current If mA of {input}?' },
                { label: 'Relay voltage (Vr)', ask: 'asset', tpl: 'What is the current Vr voltage of {input}?' },
                { label: 'Charger voltage', ask: 'asset', tpl: 'What is the current charger voltage of {input}?' },
            ]
        },
        {
            id: 'history',
            icon: '📈',
            label: 'History',
            prompts: [
                { label: 'Last 10 readings', ask: 'asset', tpl: 'Show the last 10 readings for asset {input}' },
                { label: 'If mA — last 10 readings', ask: 'asset', tpl: 'Show last 10 If mA readings for {input}' },
                { label: 'TPR history — today', ask: 'asset', tpl: "Show today's TPR history for {input}" },
                { label: 'History — today', ask: 'asset', tpl: "Show today's history for asset {input}" },
            ]
        },
        {
            id: 'timestamps',
            icon: '⏰',
            label: 'Timestamps',
            prompts: [
                { label: 'Last updated (device time)', ask: 'asset', tpl: 'When was asset {input} last updated? Show TimestampDevice.' },
                { label: 'Last channel timestamp', ask: 'asset', tpl: 'Show TimestampChannel for all tags of asset {input}' },
                { label: 'All timestamp fields', ask: 'asset', tpl: 'Show all timestamp fields for asset {input}' },
            ]
        },
        {
            id: 'search',
            icon: '🔍',
            label: 'Search',
            prompts: [
                { label: 'Find a site', ask: 'site', tpl: 'Find site information for {input}' },
                { label: 'Find an asset', ask: 'asset', tpl: 'Find asset information for {input}' },
                { label: 'All tags for an asset', ask: 'asset', tpl: 'List all measurement tags available for asset {input}' },
                { label: 'All assets on a site', ask: 'site', tpl: 'List all assets at site {input}' },
            ]
        },
        {
            id: 'health',
            icon: '🛠️',
            label: 'Health',
            prompts: [
                { label: 'Health summary — asset', ask: 'asset', tpl: 'Give a health summary for asset {input}' },
                { label: 'Health index score', ask: 'asset', tpl: 'What is the health index score for asset {input}?' },
                { label: 'Leakage current', ask: 'asset', tpl: 'What is the leakage current for asset {input}?' },
                { label: 'Choke resistance', ask: 'asset', tpl: 'What is the choke resistance for asset {input}?' },
            ]
        },
    ];

    /* ══════════════════════════════════════════════════════════
       STATE
    ══════════════════════════════════════════════════════════ */
    let isOpen = false;
    let isFullscreen = false;
    let isStreaming = false;
    let chatHistory = [];
    let _botMsgEl = null;

    // Prompt wizard state
    let activeCategory = null;  // which category is open
    let activePrompt = null;  // which prompt was selected (waiting for input)

    /* ── DOM refs ── */
    let panel, trigger, overlay, messagesEl, inputEl, sendBtn,
        statusDot, statusText;

    /* ── Public API ── */
    window.E7AI = {
        open: () => openChat(),
        close: () => closeChat(),
        clear: () => clearChat(),
        setPassword: () => { },
    };

    /* ══════════════════════════════════════════════════════════
       INIT
    ══════════════════════════════════════════════════════════ */
    function init() {
        buildHTML();
        addStyles();
        bindRefs();
        bindEvents();
        loadHistory();
        pingStatus();
    }

    async function pingStatus() {
        setStatus('connecting', 'Connecting…');
        try {
            const r = await fetch(STATUS_ENDPOINT, { credentials: 'same-origin' });
            if (r.status === 401) { setStatus('error', 'Not signed in'); return; }
            const d = await r.json();
            setStatus(d.ok ? 'online' : 'error', d.ok ? 'Ready' : (d.error || 'Unavailable'));
        } catch {
            setStatus('error', 'Unavailable');
        }
    }

    /* ══════════════════════════════════════════════════════════
       HTML
    ══════════════════════════════════════════════════════════ */
    function buildHTML() {
        document.body.insertAdjacentHTML('beforeend', `
<button id="aiChatTrigger" title="AI Assistant (Alt+C)" aria-label="Open AI Assistant">
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
    <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>
    <circle cx="9" cy="10" r="0.8" fill="currentColor"/>
    <circle cx="12" cy="10" r="0.8" fill="currentColor"/>
    <circle cx="15" cy="10" r="0.8" fill="currentColor"/>
  </svg>
</button>

<div id="aiChatOverlay"></div>

<aside id="aiChatPanel" role="complementary" aria-label="AI Assistant">

  <div class="ai-chat-header">
    <div class="ai-chat-avatar">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
        <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/>
      </svg>
    </div>
    <div class="ai-chat-header-info">
      <div class="ai-chat-header-title">Railway AI Assistant</div>
      <div class="ai-chat-header-status">
        <span class="ai-chat-status-dot" id="aiStatusDot"></span>
        <span id="aiStatusText">Connecting…</span>
      </div>
    </div>
    <div class="ai-chat-header-actions">
      <button class="ai-chat-header-btn" id="aiFullscreenBtn" title="Full screen">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" id="aiFsIcon">
          <path d="M8 3H5a2 2 0 0 0-2 2v3m18 0V5a2 2 0 0 0-2-2h-3m0 18h3a2 2 0 0 0 2-2v-3M3 16v3a2 2 0 0 0 2 2h3"/>
        </svg>
      </button>
      <button class="ai-chat-header-btn" id="aiClearBtn" title="Clear chat">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <polyline points="3 6 5 6 21 6"/>
          <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/>
        </svg>
      </button>
      <button class="ai-chat-header-btn" id="aiCloseBtn" title="Close (Alt+C)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
        </svg>
      </button>
    </div>
  </div>

  <div class="ai-chat-messages" id="aiMessages"></div>

  <!-- Prompt selector panel -->
  <div class="e7-prompt-panel" id="e7PromptPanel"></div>

  <!-- Input box with prompt label -->
  <div class="ai-chat-input-area">
    <div class="e7-input-label" id="e7InputLabel" style="display:none"></div>
    <div class="ai-input-wrapper">
      <textarea id="aiChatInput" placeholder="Type asset or site name, or ask a question…" rows="1" maxlength="10000" aria-label="Message"></textarea>
      <button class="ai-send-btn" id="aiSendBtn" disabled title="Send (Enter)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/>
        </svg>
      </button>
    </div>
    <div class="e7-input-hint">Enter to send · Shift+Enter new line · Esc to cancel</div>
  </div>

</aside>`);
    }

    /* ══════════════════════════════════════════════════════════
       STYLES
    ══════════════════════════════════════════════════════════ */
    function addStyles() {
        if (document.getElementById('e7ai-styles')) return;
        const s = document.createElement('style');
        s.id = 'e7ai-styles';
        s.textContent = `
/* Fullscreen */
#aiChatPanel.e7-fullscreen {
  width: 100vw !important;
  border-left: none !important;
}

/* ── Prompt panel ── */
.e7-prompt-panel {
  flex-shrink: 0;
  overflow-y: auto;
  max-height: 55vh;
  border-top: 1px solid rgba(255,255,255,0.07);
  background: rgba(0,0,0,0.15);
}
.e7-prompt-panel:empty { display: none; }

/* Category tabs */
.e7-cat-tabs {
  display: flex;
  overflow-x: auto;
  gap: 2px;
  padding: 8px 12px 0;
  scrollbar-width: none;
  flex-shrink: 0;
}
.e7-cat-tabs::-webkit-scrollbar { display: none; }
.e7-cat-tab {
  padding: 5px 13px;
  border-radius: 8px 8px 0 0;
  font-size: 12px;
  font-weight: 500;
  color: rgba(255,255,255,0.45);
  cursor: pointer;
  white-space: nowrap;
  border: 1px solid transparent;
  border-bottom: none;
  background: none;
  font-family: var(--chat-font, system-ui);
  transition: color .14s, background .14s;
  flex-shrink: 0;
}
.e7-cat-tab:hover { color: rgba(255,255,255,0.75); background: rgba(255,255,255,0.04); }
.e7-cat-tab.active {
  color: #22d3ee;
  background: rgba(34,211,238,0.08);
  border-color: rgba(34,211,238,0.18);
}

/* Prompt list */
.e7-prompt-list {
  padding: 6px 10px 10px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.e7-prompt-btn {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  border-radius: 8px;
  border: none;
  background: none;
  color: rgba(255,255,255,0.72);
  font-size: 13px;
  font-family: var(--chat-font, system-ui);
  cursor: pointer;
  text-align: left;
  transition: background .13s, color .13s;
  width: 100%;
}
.e7-prompt-btn:hover {
  background: rgba(34,211,238,0.08);
  color: rgba(255,255,255,0.95);
}
.e7-prompt-btn.active {
  background: rgba(34,211,238,0.14);
  color: #22d3ee;
}
.e7-prompt-arrow {
  margin-left: auto;
  font-size: 10px;
  color: rgba(34,211,238,0.5);
  flex-shrink: 0;
}

/* Input label (shows selected prompt) */
.e7-input-label {
  font-size: 11px;
  color: rgba(34,211,238,0.75);
  padding: 6px 4px 4px;
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 6px;
}
.e7-label-cancel {
  margin-left: auto;
  font-size: 11px;
  color: rgba(255,255,255,0.30);
  cursor: pointer;
  padding: 2px 6px;
  border-radius: 4px;
  background: none;
  border: none;
  font-family: var(--chat-font, system-ui);
  transition: color .12s;
}
.e7-label-cancel:hover { color: rgba(255,255,255,0.70); }

/* Input hint */
.e7-input-hint {
  font-size: 10.5px;
  color: rgba(255,255,255,0.18);
  margin-top: 5px;
  padding: 0 2px;
}

/* Tool badge */
.e7-tool-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 11px;
  border-radius: 20px;
  background: rgba(251,191,36,0.09);
  border: 1px solid rgba(251,191,36,0.20);
  color: rgba(251,191,36,0.85);
  font-size: 11px;
  margin: 2px 0 2px 37px;
  font-family: var(--chat-font, system-ui);
}
.e7-spinner {
  width: 9px; height: 9px;
  border: 1.5px solid rgba(251,191,36,0.7);
  border-top-color: transparent;
  border-radius: 50%;
  animation: e7spin .55s linear infinite;
  display: inline-block;
  flex-shrink: 0;
}
@keyframes e7spin { to { transform: rotate(360deg); } }

/* Welcome */
.e7-welcome {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  padding: 24px 20px 16px;
  gap: 8px;
  animation: aiMsgIn .25s ease-out;
}
.e7-welcome-icon {
  width: 50px; height: 50px;
  border-radius: 14px;
  background: linear-gradient(135deg, rgba(34,211,238,.14), rgba(99,102,241,.14));
  border: 1px solid rgba(34,211,238,.18);
  display: flex; align-items: center; justify-content: center;
  margin-bottom: 4px;
}
.e7-welcome-icon svg { width: 24px; height: 24px; color: #22d3ee; }
.e7-welcome h3 { font-size: 14px; font-weight: 600; color: rgba(255,255,255,.88); margin: 0; }
.e7-welcome p  { font-size: 12px; color: rgba(255,255,255,.42); line-height: 1.6; margin: 0; }
.e7-welcome-hint {
  font-size: 11.5px;
  color: rgba(34,211,238,0.60);
  margin-top: 4px;
}

/* Follow-up chips */
.e7-followup {
  display: flex;
  flex-wrap: wrap;
  gap: 5px;
  margin-top: 8px;
}
.e7-followup-chip {
  padding: 4px 11px;
  border-radius: 16px;
  background: rgba(34,211,238,0.08);
  border: 1px solid rgba(34,211,238,0.18);
  font-size: 11.5px;
  color: rgba(34,211,238,0.78);
  cursor: pointer;
  transition: all .13s;
  font-family: var(--chat-font, system-ui);
}
.e7-followup-chip:hover { background: rgba(34,211,238,0.16); color: #22d3ee; }
`;
        document.head.appendChild(s);
    }

    /* ══════════════════════════════════════════════════════════
       BIND
    ══════════════════════════════════════════════════════════ */
    function bindRefs() {
        panel = document.getElementById('aiChatPanel');
        trigger = document.getElementById('aiChatTrigger');
        overlay = document.getElementById('aiChatOverlay');
        messagesEl = document.getElementById('aiMessages');
        inputEl = document.getElementById('aiChatInput');
        sendBtn = document.getElementById('aiSendBtn');
        statusDot = document.getElementById('aiStatusDot');
        statusText = document.getElementById('aiStatusText');
    }

    function bindEvents() {
        trigger.addEventListener('click', openChat);
        document.getElementById('aiCloseBtn').addEventListener('click', closeChat);
        overlay.addEventListener('click', closeChat);
        document.getElementById('aiClearBtn').addEventListener('click', clearChat);
        document.getElementById('aiFullscreenBtn').addEventListener('click', toggleFullscreen);
        sendBtn.addEventListener('click', sendMessage);

        inputEl.addEventListener('input', function () {
            autoResize(this);
            sendBtn.disabled = !this.value.trim() || isStreaming;
        });

        inputEl.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                if (!sendBtn.disabled) sendMessage();
            }
            if (e.key === 'Escape') cancelPrompt();
        });

        document.addEventListener('keydown', function (e) {
            if (e.altKey && e.key === 'c') isOpen ? closeChat() : openChat();
            if (e.key === 'Escape' && isFullscreen) toggleFullscreen();
        });
    }

    /* ══════════════════════════════════════════════════════════
       PANEL
    ══════════════════════════════════════════════════════════ */
    function openChat() {
        isOpen = true;
        panel.classList.add('open');
        trigger.classList.add('chat-open');
        if (window.innerWidth <= 991) overlay.classList.add('show');
        if (!messagesEl.children.length) renderWelcome();
        renderPromptPanel();
        setTimeout(() => inputEl.focus(), 320);
    }

    function closeChat() {
        isOpen = false;
        panel.classList.remove('open');
        trigger.classList.remove('chat-open');
        overlay.classList.remove('show');
        if (isFullscreen) toggleFullscreen();
    }

    function clearChat() {
        chatHistory = [];
        saveHistory();
        messagesEl.innerHTML = '';
        cancelPrompt();
        renderWelcome();
    }

    function toggleFullscreen() {
        isFullscreen = !isFullscreen;
        panel.classList.toggle('e7-fullscreen', isFullscreen);
        document.getElementById('aiFsIcon').innerHTML = isFullscreen
            ? '<path d="M8 3v3a2 2 0 0 1-2 2H3m18 0h-3a2 2 0 0 1-2-2V3m0 18v-3a2 2 0 0 0 2-2h3M3 16h3a2 2 0 0 0 2 2v3"/>'
            : '<path d="M8 3H5a2 2 0 0 0-2 2v3m18 0V5a2 2 0 0 0-2-2h-3m0 18h3a2 2 0 0 0 2-2v-3M3 16v3a2 2 0 0 0 2 2h3"/>';
    }

    /* ══════════════════════════════════════════════════════════
       WELCOME
    ══════════════════════════════════════════════════════════ */
    function renderWelcome() {
        const el = document.createElement('div');
        el.className = 'e7-welcome';
        el.innerHTML = `
          <div class="e7-welcome-icon">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">
              <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/>
            </svg>
          </div>
          <h3>Railway AI Assistant</h3>
          <p>Track circuits · Signals · Point machines<br>Live data, alerts, history and timestamps</p>
          <div class="e7-welcome-hint">↓ Select a category below to get started</div>`;
        messagesEl.appendChild(el);
    }

    /* ══════════════════════════════════════════════════════════
       PROMPT PANEL — category tabs + prompt list
    ══════════════════════════════════════════════════════════ */
    function renderPromptPanel(openCatId) {
        const panel = document.getElementById('e7PromptPanel');

        // Default to first category
        if (!openCatId) openCatId = activeCategory || PROMPT_CATEGORIES[0].id;
        activeCategory = openCatId;

        const cat = PROMPT_CATEGORIES.find(c => c.id === openCatId);

        panel.innerHTML = `
          <div class="e7-cat-tabs">
            ${PROMPT_CATEGORIES.map(c => `
              <button class="e7-cat-tab ${c.id === openCatId ? 'active' : ''}"
                      data-cat="${c.id}">
                ${c.icon} ${c.label}
              </button>`).join('')}
          </div>
          <div class="e7-prompt-list">
            ${cat.prompts.map((p, i) => `
              <button class="e7-prompt-btn" data-cat="${openCatId}" data-idx="${i}">
                <span>${p.label}</span>
                <span class="e7-prompt-arrow">▶</span>
              </button>`).join('')}
          </div>`;

        // Tab clicks
        panel.querySelectorAll('.e7-cat-tab').forEach(tab => {
            tab.addEventListener('click', () => {
                cancelPrompt();
                renderPromptPanel(tab.dataset.cat);
            });
        });

        // Prompt clicks
        panel.querySelectorAll('.e7-prompt-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const cat = PROMPT_CATEGORIES.find(c => c.id === btn.dataset.cat);
                const pmt = cat.prompts[parseInt(btn.dataset.idx)];
                selectPrompt(pmt, btn);
            });
        });
    }

    function selectPrompt(prompt, btnEl) {
        // Highlight selected button
        document.querySelectorAll('.e7-prompt-btn').forEach(b => b.classList.remove('active'));
        if (btnEl) btnEl.classList.add('active');

        activePrompt = prompt;

        // Show label above input
        const label = document.getElementById('e7InputLabel');
        const askText = prompt.ask === 'site' ? 'Enter site name' : 'Enter asset name (e.g. 2T, SH04, PM07)';
        label.style.display = 'flex';
        label.innerHTML = `
          <span>→ ${esc(prompt.label)}&nbsp;·&nbsp;<em style="color:rgba(255,255,255,0.4);font-style:normal">${askText}</em></span>
          <button class="e7-label-cancel" id="e7CancelBtn">✕ Cancel</button>`;
        document.getElementById('e7CancelBtn').addEventListener('click', cancelPrompt);

        // Update placeholder
        inputEl.placeholder = prompt.ask === 'site'
            ? 'Type site name, e.g. Arnetha, Patliputra…'
            : 'Type asset name, e.g. 2T, 11T, SH04, PM07…';

        inputEl.value = '';
        inputEl.dispatchEvent(new Event('input'));
        inputEl.focus();
    }

    function cancelPrompt() {
        activePrompt = null;
        document.querySelectorAll('.e7-prompt-btn').forEach(b => b.classList.remove('active'));
        const label = document.getElementById('e7InputLabel');
        if (label) label.style.display = 'none';
        inputEl.placeholder = 'Type asset or site name, or ask a question…';
        inputEl.value = '';
        inputEl.dispatchEvent(new Event('input'));
    }

    /* ══════════════════════════════════════════════════════════
       SEND
    ══════════════════════════════════════════════════════════ */
    async function sendMessage() {
        const raw = inputEl.value.trim();
        if (!raw || isStreaming) return;

        let text;

        if (activePrompt) {
            // Build prompt from template + user's input
            text = activePrompt.tpl.replace('{input}', raw);
            cancelPrompt();
        } else {
            // Free text — scope check
            if (isOutOfScope(raw)) {
                appendUserMsg(raw);
                inputEl.value = '';
                autoResize(inputEl);
                sendBtn.disabled = true;
                appendScopeRefusal();
                return;
            }
            text = raw;
        }

        inputEl.value = '';
        autoResize(inputEl);
        sendBtn.disabled = true;

        const welcome = messagesEl.querySelector('.e7-welcome');
        if (welcome) welcome.remove();

        appendUserMsg(text);
        chatHistory.push({ role: 'user', content: text });
        if (chatHistory.length > MAX_HISTORY * 2)
            chatHistory = chatHistory.slice(-MAX_HISTORY * 2);

        const typingEl = appendTyping();
        let activeBadge = null;
        let botText = '';
        _botMsgEl = null;
        isStreaming = true;
        setStatus('connecting', 'Fetching data…');

        try {
            const res = await fetch(CHAT_ENDPOINT, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ messages: chatHistory }),
            });

            if (res.status === 401) {
                typingEl.remove();
                appendBotMsg('Session expired — please sign in again.', true);
                setStatus('error', 'Session expired');
                return;
            }
            if (!res.ok) throw new Error('Server error ' + res.status);

            const reader = res.body.getReader();
            const decoder = new TextDecoder();
            let buf = '';
            typingEl.remove();

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;
                buf += decoder.decode(value, { stream: true });
                const lines = buf.split('\n');
                buf = lines.pop();

                for (const line of lines) {
                    if (!line.startsWith('data: ')) continue;
                    const raw2 = line.slice(6).trim();
                    if (raw2 === '[DONE]') continue;
                    let evt;
                    try { evt = JSON.parse(raw2); } catch { continue; }

                    if (evt.type === 'text') {
                        botText += evt.text;
                        streamBotBubble(botText);
                        if (activeBadge) { activeBadge.remove(); activeBadge = null; }
                    }
                    if (evt.type === 'tool_use') {
                        if (activeBadge) activeBadge.remove();
                        activeBadge = appendToolBadge(evt.name);
                        setStatus('connecting', 'Fetching data…');
                    }
                    if (evt.type === 'error') {
                        appendBotMsg('⚠ ' + (evt.message || 'Error'), true);
                        setStatus('error', 'Error');
                    }
                }
            }

            if (botText) {
                chatHistory.push({ role: 'assistant', content: botText });
                saveHistory();
                showFollowUps(text, botText);
            }
            setStatus('online', 'Ready');

        } catch (err) {
            if (typingEl.parentNode) typingEl.remove();
            appendBotMsg('Connection error — please try again.', true);
            setStatus('error', 'Error');
        } finally {
            if (activeBadge) activeBadge.remove();
            isStreaming = false;
            sendBtn.disabled = !inputEl.value.trim();
            inputEl.focus();
        }
    }

    /* ══════════════════════════════════════════════════════════
       FOLLOW-UP CHIPS — based on what was just answered
    ══════════════════════════════════════════════════════════ */
    function showFollowUps(question, answer) {
        const q = question.toLowerCase();
        const a = answer.toLowerCase();
        const chips = [];

        // Extract asset name from question if present
        const assetMatch = question.match(/\b(\d+T\d*|SH\d+|PM\d+|DL\d+|A10\d*)\b/i);
        const asset = assetMatch ? assetMatch[1] : null;

        if (asset) {
            if (q.includes('tpr') || q.includes('current') || q.includes('live') || q.includes('value')) {
                chips.push({ label: '📈 History today', prompt: `Show today's history for asset ${asset}` });
                chips.push({ label: '🚨 Active alerts', prompt: `Show active alerts for asset ${asset}` });
            }
            if (q.includes('alert') || a.includes('alert')) {
                chips.push({ label: '📋 Alert history 7d', prompt: `Show alert history for asset ${asset} over the last 7 days` });
                chips.push({ label: '📊 Current values', prompt: `Show all current tag values for asset ${asset}` });
            }
            if (q.includes('history') || q.includes('reading')) {
                chips.push({ label: '⏰ Last updated', prompt: `When was asset ${asset} last updated? Show TimestampDevice.` });
                chips.push({ label: '🛠️ Health summary', prompt: `Give a health summary for asset ${asset}` });
            }
        } else {
            // Site-level or general
            if (a.includes('alert') || q.includes('alert')) {
                chips.push({ label: '📋 All active alerts', prompt: 'Show all active alerts' });
            }
            chips.push({
                label: '🔍 Search asset', prompt: null, action: () => {
                    renderPromptPanel('search');
                    inputEl.focus();
                }
            });
        }

        if (!chips.length) return;

        // Append chips to the last bot message
        if (_botMsgEl) {
            const bubble = _botMsgEl.querySelector('.ai-msg-bubble');
            if (bubble) {
                const row = document.createElement('div');
                row.className = 'e7-followup';
                chips.slice(0, 4).forEach(chip => {
                    const btn = document.createElement('button');
                    btn.className = 'e7-followup-chip';
                    btn.textContent = chip.label;
                    btn.addEventListener('click', () => {
                        if (chip.action) { chip.action(); return; }
                        if (chip.prompt && !isStreaming) {
                            chatHistory.push({ role: 'user', content: chip.prompt });
                            inputEl.value = chip.prompt;
                            sendMessage();
                        }
                    });
                    row.appendChild(btn);
                });
                bubble.appendChild(row);
                scrollDown();
            }
        }
    }

    /* ══════════════════════════════════════════════════════════
       SCOPE
    ══════════════════════════════════════════════════════════ */
    function isOutOfScope(text) {
        const l = text.toLowerCase();
        return OUT_OF_SCOPE.some(k => l.includes(k));
    }

    function appendScopeRefusal() {
        const el = document.createElement('div');
        el.className = 'ai-msg ai';
        el.innerHTML = `
          <div class="ai-msg-avatar">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="14" height="14">
              <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/>
            </svg>
          </div>
          <div>
            <div class="ai-msg-bubble">
              I am not trained to answer that. I can only assist with EdgeX RDPMS railway monitoring data — asset values, history, alerts and timestamps.
            </div>
            <div class="ai-msg-time">${fmtTime()}</div>
          </div>`;
        messagesEl.appendChild(el);
        scrollDown();
    }

    /* ══════════════════════════════════════════════════════════
       MESSAGES
    ══════════════════════════════════════════════════════════ */
    function appendUserMsg(text) {
        _botMsgEl = null;
        const el = document.createElement('div');
        el.className = 'ai-msg user';
        el.innerHTML = `
          <div class="ai-msg-avatar">${getUserInitial()}</div>
          <div>
            <div class="ai-msg-bubble">${esc(text)}</div>
            <div class="ai-msg-time">${fmtTime()}</div>
          </div>`;
        messagesEl.appendChild(el);
        scrollDown();
    }

    function streamBotBubble(content) {
        if (!_botMsgEl || !_botMsgEl.parentNode) {
            _botMsgEl = document.createElement('div');
            _botMsgEl.className = 'ai-msg ai';
            _botMsgEl.innerHTML = `
              <div class="ai-msg-avatar">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="14" height="14">
                  <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/>
                </svg>
              </div>
              <div>
                <div class="ai-msg-bubble"></div>
                <div class="ai-msg-time">${fmtTime()}</div>
              </div>`;
            messagesEl.appendChild(_botMsgEl);
        }
        _botMsgEl.querySelector('.ai-msg-bubble').innerHTML = renderMarkdown(content);
        scrollDown();
    }

    function appendBotMsg(text, isErr) {
        const el = document.createElement('div');
        el.className = 'ai-msg ai' + (isErr ? ' ai-msg-error' : '');
        el.innerHTML = `
          <div class="ai-msg-avatar">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="14" height="14">
              <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/>
            </svg>
          </div>
          <div>
            <div class="ai-msg-bubble">${isErr ? esc(text) : renderMarkdown(text)}</div>
            <div class="ai-msg-time">${fmtTime()}</div>
          </div>`;
        messagesEl.appendChild(el);
        scrollDown();
    }

    function appendTyping() {
        _botMsgEl = null;
        const el = document.createElement('div');
        el.className = 'ai-msg ai';
        el.innerHTML = `
          <div class="ai-msg-avatar">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="14" height="14">
              <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/>
            </svg>
          </div>
          <div>
            <div class="ai-typing-bubble">
              <div class="ai-typing-dot"></div>
              <div class="ai-typing-dot"></div>
              <div class="ai-typing-dot"></div>
            </div>
          </div>`;
        messagesEl.appendChild(el);
        scrollDown();
        return el;
    }

    function appendToolBadge(name) {
        const TOOL_LABELS = {
            search_sites: 'Searching sites…',
            search_assets: 'Searching assets…',
            search_tags: 'Searching tags…',
            get_asset_tags: 'Loading tags…',
            get_tag_current: 'Reading live value…',
            get_live_values: 'Reading live values…',
            state_get: 'Getting state…',
            get_history: 'Loading history…',
            get_history_compact: 'Loading history…',
            get_limited_history: 'Loading recent readings…',
            get_timestamps: 'Reading timestamps…',
            get_alerts: 'Checking alerts…',
            get_alert_history: 'Loading alert history…',
            alert_smart: 'Summarising alerts…',
            summary_get: 'Calculating summary…',
            trend_get: 'Analysing trend…',
        };
        const label = TOOL_LABELS[name] || (name + '…');
        const el = document.createElement('div');
        el.className = 'e7-tool-badge';
        el.innerHTML = `<span class="e7-spinner"></span><span>${esc(label)}</span>`;
        messagesEl.appendChild(el);
        scrollDown();
        return el;
    }

    /* ══════════════════════════════════════════════════════════
       MARKDOWN
    ══════════════════════════════════════════════════════════ */
    function renderMarkdown(raw) {
        if (!raw) return '';
        let h = String(raw)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
        h = h.replace(/```[\w]*\n?([\s\S]*?)```/g,
            (_, c) => '<pre><code>' + c.trim() + '</code></pre>');
        h = h.replace(/`([^`\n]+)`/g, '<code>$1</code>');
        h = h.replace(/\*\*([^*\n]+)\*\*/g, '<strong>$1</strong>');
        h = h.replace(/\*([^*\n]+)\*/g, '<em>$1</em>');
        h = h.replace(/((\|[^\n]+\|\n?)+)/g, function (m) {
            const rows = m.trim().split('\n').filter(r => r.trim());
            if (rows.length < 2) return m;
            let t = '<table style="border-collapse:collapse;font-size:12px;margin:6px 0;width:100%">';
            rows.forEach((row, i) => {
                if (/^\|[\s\-:|]+\|$/.test(row.trim())) return;
                const cells = row.split('|').filter((c, ci, a) => ci > 0 && ci < a.length - 1);
                const tag = i === 0 ? 'th' : 'td';
                t += '<tr>' + cells.map(c =>
                    `<${tag} style="border:1px solid rgba(255,255,255,.10);padding:4px 8px">${c.trim()}</${tag}>`
                ).join('') + '</tr>';
            });
            return t + '</table>';
        });
        h = h.replace(/^[•\-] (.+)$/gm, '<li>$1</li>');
        h = h.replace(/(<li>[\s\S]*?<\/li>\n?)+/g,
            '<ul style="margin:4px 0 4px 16px;padding:0">$&</ul>');
        h = h.replace(/^\d+\. (.+)$/gm, '<li>$1</li>');
        h = h.replace(/\n/g, '<br>');
        return h;
    }

    /* ══════════════════════════════════════════════════════════
       UTILS
    ══════════════════════════════════════════════════════════ */
    function esc(s) {
        return String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }
    function scrollDown() { messagesEl.scrollTop = messagesEl.scrollHeight; }
    function fmtTime() { return new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }); }
    function autoResize(el) { el.style.height = ''; el.style.height = Math.min(el.scrollHeight, 100) + 'px'; }
    function getUserInitial() {
        const el = document.querySelector('.e7-user-name');
        return el ? (el.textContent.trim()[0] || 'U').toUpperCase() : 'U';
    }
    function setStatus(state, text) {
        if (!statusDot || !statusText) return;
        statusDot.className = 'ai-chat-status-dot' + (state === 'online' ? '' : ' ' + state);
        statusText.textContent = text;
    }
    function saveHistory() {
        try { sessionStorage.setItem(STORAGE_KEY, JSON.stringify(chatHistory.slice(-MAX_HISTORY * 2))); }
        catch (_) { }
    }
    function loadHistory() {
        try { const s = sessionStorage.getItem(STORAGE_KEY); if (s) chatHistory = JSON.parse(s); }
        catch (_) { }
    }

    /* ── Boot ── */
    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', init);
    else
        init();

})();
