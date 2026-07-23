'use strict';

// ── Config ────────────────────────────────────────────────────────────────
const params = new URLSearchParams(window.location.search);
const config = {
    host: params.get('host') || window.location.hostname || 'localhost',
    port: params.get('port') || '5099',
    group: params.get('group') || 'maria',
    agentId: params.get('agent_id') || 'maria',
};
const WS_URL = `ws://${config.host}:${config.port}/ws`;

// ── DOM ───────────────────────────────────────────────────────────────────
const $ = (id) => document.getElementById(id);
const dom = {
    headerTitle: $('header-title'),
    headerSubtitle: $('header-subtitle'),
    statusDot: $('status-dot'),
    statusText: $('status-text'),
    themeBtn: $('theme-btn'),
    helpBtn: $('help-btn'),
    chatLog: $('chat-log'),
    scrollBtn: $('scroll-btn'),
    input: $('input'),
    sendBtn: $('send-btn'),
    cancelBtn: $('cancel-btn'),
    modalOverlay: $('modal-overlay'),
    modalTitle: $('modal-title'),
    modalBody: $('modal-body'),
    modalClose: $('modal-close'),
    helpOverlay: $('help-overlay'),
    helpClose: $('help-close'),
};

// ── State ──────────────────────────────────────────────────────────────────
const state = {
    ws: null,
    connected: false,
    isStreaming: false,
    currentModel: null,
    pendingAssistantEl: null,
    reconnectAttempts: 0,
    reconnectTimer: null,
    inputHistory: [],
    inputHistoryIndex: -1,
};

const THEMES = ['forge', 'matrix', 'amber', 'mono', 'high-contrast'];

// ── Theme ──────────────────────────────────────────────────────────────────
function loadTheme() {
    const saved = localStorage.getItem('aether-theme') || 'forge';
    setTheme(saved);
}

function setTheme(name) {
    if (!THEMES.includes(name)) name = 'forge';
    document.documentElement.setAttribute('data-theme', name);
    localStorage.setItem('aether-theme', name);
    dom.themeBtn.textContent = `Theme: ${name}`;
}

function cycleTheme() {
    const current = document.documentElement.getAttribute('data-theme') || 'forge';
    const idx = THEMES.indexOf(current);
    const next = THEMES[(idx + 1) % THEMES.length];
    setTheme(next);
    addSystemMessage(`Theme switched to: ${next}`);
}

// ── WebSocket ──────────────────────────────────────────────────────────────
function connect() {
    setStatus('connecting');
    state.ws = new WebSocket(WS_URL);

    state.ws.onopen = () => {
        state.connected = true;
        state.reconnectAttempts = 0;
        setStatus('connected');
        dom.headerSubtitle.textContent = `Group: ${config.group}`;
        sendJson({ type: 'list_models' });
        sendJson({ type: 'get_history', group: config.group, limit: 50 });
    };

    state.ws.onmessage = (event) => {
        try {
            const msg = JSON.parse(event.data);
            handleMessage(msg);
        } catch (e) {
            console.error('Failed to parse message:', e);
        }
    };

    state.ws.onerror = (err) => {
        console.error('WebSocket error:', err);
    };

    state.ws.onclose = () => {
        state.connected = false;
        setStatus('disconnected');
        if (state.isStreaming) {
            state.isStreaming = false;
            toggleCancelButton();
        }
        scheduleReconnect();
    };
}

function scheduleReconnect() {
    if (state.reconnectTimer) return;
    const delay = Math.min(1000 * Math.pow(2, state.reconnectAttempts), 30000);
    state.reconnectAttempts++;
    addSystemMessage(`Reconnecting in ${Math.round(delay / 1000)}s...`);
    state.reconnectTimer = setTimeout(() => {
        state.reconnectTimer = null;
        connect();
    }, delay);
}

function sendJson(obj) {
    if (state.ws && state.ws.readyState === WebSocket.OPEN) {
        state.ws.send(JSON.stringify(obj));
        return true;
    }
    return false;
}

function setStatus(status) {
    dom.statusDot.className = `status-dot ${status}`;
    const labels = { connected: 'Connected', connecting: 'Connecting...', disconnected: 'Disconnected' };
    dom.statusText.textContent = labels[status] || status;
}

// ── Message handling ───────────────────────────────────────────────────────
function handleMessage(msg) {
    switch (msg.type) {
        case 'connected':
            addSystemMessage('Connected to Aether');
            break;
        case 'chunk':
        case 'streaming_chunk':
            handleStreamChunk(msg.text || '');
            break;
        case 'message':
        case 'complete':
            handleMessageComplete(msg.text || '', msg);
            break;
        case 'typing':
            handleTyping(msg.is_typing ?? msg.status === 'typing');
            break;
        case 'error':
            addErrorMessage(msg.message || msg.text || 'Unknown error');
            break;
        case 'models':
            handleModelsResponse(msg);
            break;
        case 'history':
            handleHistoryResponse(msg.messages || []);
            break;
        case 'goals':
            handleGoalsResponse(msg.goals || []);
            break;
        case 'skills':
            handleSkillsResponse(msg.skills || []);
            break;
        case 'metrics':
            handleMetricsResponse(msg);
            break;
        case 'telemetry':
            handleTelemetryResponse(msg);
            break;
        case 'git_status_response':
            handleGitStatusResponse(msg.files || []);
            break;
        case 'stage_file_response':
            addSystemMessage(`Stage ${msg.staged ? 'added' : 'reset'}: ${msg.file} (${msg.ok ? 'ok' : 'failed'})`);
            break;
        case 'context_updated':
            break;
        default:
            console.log('Unknown message type:', msg.type, msg);
    }
}

function handleStreamChunk(text) {
    if (!state.pendingAssistantEl) {
        state.pendingAssistantEl = addMessageElement('agent', '');
        state.isStreaming = true;
        toggleCancelButton();
    }
    const contentEl = state.pendingAssistantEl.querySelector('.content');
    if (contentEl) {
        contentEl.textContent += text;
        scrollToBottom();
    }
}

function handleMessageComplete(text, msg) {
    if (state.pendingAssistantEl) {
        const contentEl = state.pendingAssistantEl.querySelector('.content');
        if (contentEl) {
            contentEl.innerHTML = renderMarkdown(text || contentEl.textContent);
            highlightCodeBlocks(contentEl);
        }
        state.pendingAssistantEl = null;
    } else {
        const el = addMessageElement('agent', '');
        const contentEl = el.querySelector('.content');
        contentEl.innerHTML = renderMarkdown(text);
        highlightCodeBlocks(contentEl);
    }
    state.isStreaming = false;
    toggleCancelButton();
    scrollToBottom();
}

function handleTyping(isTyping) {
    if (isTyping && !state.pendingAssistantEl) {
        state.pendingAssistantEl = addMessageElement('agent', '');
        const contentEl = state.pendingAssistantEl.querySelector('.content');
        contentEl.textContent = '...';
    }
}

function handleModelsResponse(msg) {
    state.currentModel = msg.current;
    if (msg.current) {
        dom.headerSubtitle.textContent = `Model: ${msg.current} | Group: ${config.group}`;
    }
}

function handleHistoryResponse(messages) {
    dom.chatLog.innerHTML = '';
    for (const m of messages) {
        const role = m.role || 'user';
        const el = addMessageElement(role === 'assistant' ? 'agent' : role, '');
        const contentEl = el.querySelector('.content');
        if (role === 'assistant') {
            contentEl.innerHTML = renderMarkdown(m.content || '');
            highlightCodeBlocks(contentEl);
        } else {
            contentEl.textContent = m.content || '';
        }
        if (m.timestamp) {
            const ts = el.querySelector('.timestamp');
            if (ts) ts.textContent = formatTime(m.timestamp);
        }
    }
    scrollToBottom();
}

function handleGoalsResponse(goals) {
    showModal('Goals', '');
    const body = dom.modalBody;
    if (goals.length === 0) {
        body.innerHTML = '<p style="color: var(--fg-dim); padding: 8px;">No active goals.</p>';
        return;
    }
    body.innerHTML = goals.map(g => `
        <div class="model-item">
            <div>
                <div style="color: var(--fg-bright);">${escapeHtml(g.title)}</div>
                <div style="font-size: 11px; color: var(--fg-muted);">Priority: ${g.priority} | Status: ${g.status}</div>
            </div>
            <span style="font-size: 11px; color: var(--fg-dim);">${g.deadline ? formatTime(g.deadline) : ''}</span>
        </div>
    `).join('');
}

function handleSkillsResponse(skills) {
    showModal('Skills', '');
    const body = dom.modalBody;
    if (skills.length === 0) {
        body.innerHTML = '<p style="color: var(--fg-dim); padding: 8px;">No skills registered.</p>';
        return;
    }
    body.innerHTML = skills.map(s => `
        <div class="model-item">
            <div>
                <div style="color: var(--fg-bright);">${escapeHtml(s.name)}</div>
                <div style="font-size: 11px; color: var(--fg-muted);">${escapeHtml(s.description || '')}</div>
                <div style="font-size: 11px; color: var(--fg-muted);">Trigger: ${s.trigger_mode || 'Both'} | Auto: ${s.auto_apply ? 'Yes' : 'No'}</div>
            </div>
        </div>
    `).join('');
}

function handleMetricsResponse(msg) {
    showModal('Self-Improvement Metrics', '');
    const body = dom.modalBody;
    const states = msg.pipeline_states || {};
    const stateEntries = Object.entries(states).map(([k, v]) => `<div>${k}: ${v}</div>`).join('');
    body.innerHTML = `
        <div style="padding: 4px 0;">
            <div style="font-size: 11px; color: var(--fg-muted); text-transform: uppercase; margin-bottom: 4px;">Pipeline</div>
            ${stateEntries || '<div style="color: var(--fg-dim);">No data</div>'}
            <div style="margin-top: 4px;">Total: ${msg.total_candidates || 0} | Recidivism: ${msg.recidivism_count || 0}</div>
        </div>
        <div style="padding: 8px 0 4px;">
            <div style="font-size: 11px; color: var(--fg-muted); text-transform: uppercase; margin-bottom: 4px;">Recent Candidates</div>
            ${(msg.recent_candidates || []).map(c => `
                <div class="model-item">
                    <div style="font-size: 12px;">${escapeHtml(c.source || c.id)}</div>
                    <span style="font-size: 11px; color: var(--fg-dim);">${c.state}</span>
                </div>
            `).join('') || '<div style="color: var(--fg-dim);">None</div>'}
        </div>
    `;
}

function handleTelemetryResponse(msg) {
    addSystemMessage(`Telemetry: Heat=${msg.system_heat} Tension=${msg.tension_level} Hive=${msg.hive_active ? 'active' : 'idle'} Goals=${msg.active_goals} Skills=${msg.skill_count}`);
}

function handleGitStatusResponse(files) {
    showModal('Git Status', '');
    const body = dom.modalBody;
    if (files.length === 0) {
        body.innerHTML = '<p style="color: var(--fg-dim); padding: 8px;">Working tree clean.</p>';
        return;
    }
    body.innerHTML = files.map(f => `
        <div class="model-item">
            <span>${escapeHtml(f.path)}</span>
            <span style="color: var(--accent); font-size: 12px;">${escapeHtml(f.status)}</span>
        </div>
    `).join('');
}

// ── Rendering ──────────────────────────────────────────────────────────────
function addMessageElement(role, text) {
    const div = document.createElement('div');
    div.className = `message ${role}`;
    const roleLabel = document.createElement('div');
    roleLabel.className = 'role';
    roleLabel.textContent = role;
    const timestamp = document.createElement('span');
    timestamp.className = 'timestamp';
    roleLabel.appendChild(timestamp);
    const content = document.createElement('div');
    content.className = 'content';
    content.textContent = text;
    div.appendChild(roleLabel);
    div.appendChild(content);
    dom.chatLog.appendChild(div);
    return div;
}

function addSystemMessage(text) {
    const el = addMessageElement('system', '');
    el.querySelector('.content').textContent = text;
    scrollToBottom();
}

function addErrorMessage(text) {
    const el = addMessageElement('error', '');
    el.querySelector('.content').textContent = `Error: ${text}`;
    scrollToBottom();
}

function escapeHtml(s) {
    const div = document.createElement('div');
    div.textContent = s;
    return div.innerHTML;
}

function formatTime(iso) {
    try {
        const d = new Date(iso);
        return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    } catch { return ''; }
}

// ── Markdown (minimal) ─────────────────────────────────────────────────────
function renderMarkdown(text) {
    if (!text) return '';
    const escaped = escapeHtml(text);
    let html = escaped;

    // Code blocks (```lang ... ```)
    html = html.replace(/```(\w*)\n([\s\S]*?)```/g, (_, lang, code) =>
        `<pre><code class="language-${lang || 'text'}">${code.replace(/\n$/, '')}</code></pre>`);

    // Inline code
    html = html.replace(/`([^`]+)`/g, '<code>$1</code>');

    // Bold
    html = html.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');

    // Links
    html = html.replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2" target="_blank" rel="noopener">$1</a>');

    return html;
}

// ── Syntax highlighting (minimal) ──────────────────────────────────────────
const KEYWORDS = {
    rust: ['fn', 'let', 'mut', 'pub', 'struct', 'enum', 'impl', 'trait', 'use', 'mod', 'match', 'if', 'else', 'for', 'while', 'loop', 'return', 'async', 'await', 'move', 'ref', 'self', 'Self', 'crate', 'super', 'as', 'in', 'where', 'dyn', 'unsafe', 'const', 'static'],
    python: ['def', 'class', 'import', 'from', 'as', 'if', 'elif', 'else', 'for', 'while', 'return', 'yield', 'async', 'await', 'with', 'try', 'except', 'finally', 'raise', 'pass', 'None', 'True', 'False', 'and', 'or', 'not', 'in', 'is', 'lambda', 'global', 'nonlocal', 'del'],
    csharp: ['using', 'namespace', 'class', 'struct', 'enum', 'interface', 'public', 'private', 'protected', 'internal', 'static', 'void', 'int', 'string', 'bool', 'var', 'new', 'return', 'if', 'else', 'for', 'foreach', 'while', 'switch', 'case', 'break', 'continue', 'try', 'catch', 'finally', 'throw', 'async', 'await', 'Task', 'null', 'true', 'false', 'this', 'base', 'override', 'virtual', 'abstract', 'sealed', 'readonly', 'const', 'get', 'set', 'record'],
    javascript: ['const', 'let', 'var', 'function', 'return', 'if', 'else', 'for', 'while', 'switch', 'case', 'break', 'continue', 'try', 'catch', 'finally', 'throw', 'async', 'await', 'new', 'class', 'extends', 'super', 'this', 'import', 'export', 'from', 'default', 'null', 'undefined', 'true', 'false', 'typeof', 'instanceof', 'in', 'of', 'delete', 'void'],
    typescript: ['const', 'let', 'var', 'function', 'return', 'if', 'else', 'for', 'while', 'switch', 'case', 'break', 'continue', 'try', 'catch', 'finally', 'throw', 'async', 'await', 'new', 'class', 'extends', 'super', 'this', 'import', 'export', 'from', 'default', 'null', 'undefined', 'true', 'false', 'typeof', 'instanceof', 'in', 'of', 'delete', 'void', 'interface', 'type', 'enum', 'public', 'private', 'protected', 'readonly', 'as', 'is'],
    bash: ['if', 'then', 'else', 'elif', 'fi', 'for', 'in', 'do', 'done', 'while', 'case', 'esac', 'function', 'return', 'local', 'export', 'unset', 'echo', 'printf', 'read', 'set', 'shift', 'source', 'exit', 'cd', 'pwd', 'ls', 'cat', 'grep', 'sed', 'awk', 'find', 'mkdir', 'rm', 'cp', 'mv'],
};

function highlightCodeBlocks(container) {
    const blocks = container.querySelectorAll('pre code');
    blocks.forEach(block => {
        const langMatch = block.className.match(/language-(\w+)/);
        const lang = langMatch ? langMatch[1] : '';
        const keywords = KEYWORDS[lang] || KEYWORDS.csharp;
        let code = block.innerHTML;

        // Highlight strings
        code = code.replace(/("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*')/g, '<span style="color: var(--success);">$1</span>');
        // Highlight comments
        code = code.replace(/(\/\/[^\n]*|#[^\n]*)/g, '<span style="color: var(--fg-muted);">$1</span>');
        // Highlight numbers
        code = code.replace(/\b(\d+\.?\d*)\b/g, '<span style="color: var(--accent);">$1</span>');
        // Highlight keywords
        const kwRegex = new RegExp(`\\b(${keywords.join('|')})\\b`, 'g');
        code = code.replace(kwRegex, '<span style="color: var(--fg-bright); font-weight: bold;">$1</span>');

        block.innerHTML = code;
    });
}

// ── Sending messages ───────────────────────────────────────────────────────
function sendMessage() {
    const text = dom.input.value.trim();
    if (!text) return;
    if (!state.connected) {
        addErrorMessage('Not connected. Waiting for reconnection...');
        return;
    }

    if (text.startsWith('/')) {
        handleSlashCommand(text);
    } else {
        addMessageElement('user', '');
        const content = dom.chatLog.lastElementChild.querySelector('.content');
        content.textContent = text;
        sendJson({ type: 'message', text, group: config.group });
    }

    state.inputHistory.unshift(text);
    if (state.inputHistory.length > 100) state.inputHistory.pop();
    state.inputHistoryIndex = -1;

    dom.input.value = '';
    autoResize();
    scrollToBottom();
}

function handleSlashCommand(text) {
    const parts = text.slice(1).split(/\s+/);
    const cmd = parts[0].toLowerCase();
    const arg = parts.slice(1).join(' ');

    switch (cmd) {
        case 'clear':
            dom.chatLog.innerHTML = '';
            addSystemMessage('Chat cleared.');
            break;
        case 'help':
            showHelp();
            break;
        case 'theme':
            if (arg && THEMES.includes(arg.toLowerCase())) {
                setTheme(arg.toLowerCase());
                addSystemMessage(`Theme set to: ${arg.toLowerCase()}`);
            } else {
                cycleTheme();
            }
            break;
        case 'status':
            addSystemMessage(`Status: ${state.connected ? 'Connected' : 'Disconnected'} | Model: ${state.currentModel || 'unknown'} | Group: ${config.group}`);
            break;
        case 'models':
            sendJson({ type: 'list_models' });
            showModelPicker();
            break;
        case 'exit':
            addSystemMessage('Close the browser tab to exit.');
            break;
        case 'goals':
            sendJson({ type: 'get_goals', agent_id: config.agentId });
            break;
        case 'skills':
            sendJson({ type: 'get_skills' });
            break;
        case 'metrics':
            sendJson({ type: 'get_metrics' });
            break;
        case 'telemetry':
            sendJson({ type: 'get_telemetry' });
            break;
        case 'git':
            sendJson({ type: 'git_status' });
            break;
        default:
            sendJson({ type: 'command', text, group: config.group });
            break;
    }
}

// ── Model picker ───────────────────────────────────────────────────────────
let pendingModels = null;

function showModelPicker() {
    if (pendingModels) {
        renderModelPicker(pendingModels);
    } else {
        sendJson({ type: 'list_models' });
        showModal('Models', '<p style="color: var(--fg-dim); padding: 8px;">Loading...</p>');
    }
}

function renderModelPicker(msg) {
    pendingModels = msg;
    const providers = msg.providers || [];
    const current = msg.current;
    let html = '';
    for (const p of providers) {
        html += `<div class="provider-group"><div class="provider-name">${escapeHtml(p.name)}</div>`;
        for (const m of (p.models || [])) {
            const isCurrent = m === current;
            html += `<div class="model-item ${isCurrent ? 'current' : ''}" role="button" tabindex="0" data-model="${escapeHtml(m)}" data-provider="${escapeHtml(p.name)}">
                <span>${escapeHtml(m)}</span>
                ${isCurrent ? '<span style="color: var(--accent);">✓ current</span>' : ''}
            </div>`;
        }
        html += '</div>';
    }
    showModal('Models', html || '<p style="color: var(--fg-dim);">No models available.</p>');

    dom.modalBody.querySelectorAll('.model-item').forEach(item => {
        const select = () => {
            const model = item.dataset.model;
            addSystemMessage(`Selected model: ${model} (use /command to switch)`);
            closeModal();
        };
        item.addEventListener('click', select);
        item.addEventListener('keydown', (e) => { if (e.key === 'Enter') select(); });
    });
}

// Override handleModelsResponse to also update picker if open
const _origHandleModels = handleModelsResponse;
handleModelsResponse = function(msg) {
    _origHandleModels(msg);
    if (!dom.modalOverlay.classList.contains('hidden') && dom.modalTitle.textContent === 'Models') {
        renderModelPicker(msg);
    }
};

// ── Modal ──────────────────────────────────────────────────────────────────
function showModal(title, bodyHtml) {
    dom.modalTitle.textContent = title;
    dom.modalBody.innerHTML = bodyHtml;
    dom.modalOverlay.classList.remove('hidden');
    dom.modalClose.focus();
}

function closeModal() {
    dom.modalOverlay.classList.add('hidden');
    dom.input.focus();
}

function showHelp() {
    dom.helpOverlay.classList.remove('hidden');
    dom.helpClose.focus();
}

function closeHelp() {
    dom.helpOverlay.classList.add('hidden');
    dom.input.focus();
}

function toggleCancelButton() {
    if (state.isStreaming) {
        dom.sendBtn.classList.add('hidden');
        dom.cancelBtn.classList.remove('hidden');
    } else {
        dom.sendBtn.classList.remove('hidden');
        dom.cancelBtn.classList.add('hidden');
    }
}

// ── Auto-scroll ────────────────────────────────────────────────────────────
function scrollToBottom() {
    dom.chatLog.scrollTop = dom.chatLog.scrollHeight;
}

function isNearBottom() {
    const threshold = 100;
    return dom.chatLog.scrollHeight - dom.chatLog.scrollTop - dom.chatLog.clientHeight < threshold;
}

dom.chatLog.addEventListener('scroll', () => {
    if (isNearBottom()) {
        dom.scrollBtn.classList.add('hidden');
    } else {
        dom.scrollBtn.classList.remove('hidden');
    }
});

dom.scrollBtn.addEventListener('click', scrollToBottom);
dom.scrollBtn.addEventListener('keydown', (e) => { if (e.key === 'Enter') scrollToBottom(); });

// ── Input handling ─────────────────────────────────────────────────────────
function autoResize() {
    dom.input.style.height = 'auto';
    dom.input.style.height = Math.min(dom.input.scrollHeight, 120) + 'px';
}

dom.input.addEventListener('input', autoResize);

dom.input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey && !e.ctrlKey && !e.metaKey && !e.altKey) {
        e.preventDefault();
        sendMessage();
    } else if (e.key === 'ArrowUp' && e.ctrlKey) {
        e.preventDefault();
        if (state.inputHistory.length === 0) return;
        state.inputHistoryIndex = Math.min(state.inputHistoryIndex + 1, state.inputHistory.length - 1);
        dom.input.value = state.inputHistory[state.inputHistoryIndex] || '';
        autoResize();
    } else if (e.key === 'ArrowDown' && e.ctrlKey) {
        e.preventDefault();
        state.inputHistoryIndex = Math.max(state.inputHistoryIndex - 1, -1);
        dom.input.value = state.inputHistoryIndex === -1 ? '' : state.inputHistory[state.inputHistoryIndex] || '';
        autoResize();
    }
});

dom.sendBtn.addEventListener('click', sendMessage);
dom.cancelBtn.addEventListener('click', () => {
    sendJson({ type: 'cancel', group: config.group });
    state.isStreaming = false;
    toggleCancelButton();
});

dom.modalClose.addEventListener('click', closeModal);
dom.helpClose.addEventListener('click', closeHelp);
dom.modalOverlay.addEventListener('click', (e) => { if (e.target === dom.modalOverlay) closeModal(); });
dom.helpOverlay.addEventListener('click', (e) => { if (e.target === dom.helpOverlay) closeHelp(); });

dom.themeBtn.addEventListener('click', cycleTheme);
dom.helpBtn.addEventListener('click', showHelp);

// Global keyboard shortcuts
document.addEventListener('keydown', (e) => {
    if (e.ctrlKey && e.key === 'k') {
        e.preventDefault();
        showModelPicker();
    } else if (e.ctrlKey && e.key === 'h') {
        e.preventDefault();
        showHelp();
    } else if (e.key === 'Escape') {
        if (!dom.modalOverlay.classList.contains('hidden')) closeModal();
        else if (!dom.helpOverlay.classList.contains('hidden')) closeHelp();
        else if (state.isStreaming) {
            sendJson({ type: 'cancel', group: config.group });
        }
    }
});

// ── Init ───────────────────────────────────────────────────────────────────
loadTheme();
connect();
dom.input.focus();
autoResize();
