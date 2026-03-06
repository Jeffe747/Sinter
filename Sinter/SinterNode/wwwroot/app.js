const state = {
  dashboard: null,
  lastAction: null,
  flash: null,
  sessionApiKey: localStorage.getItem('sinter-node-api-key') || ''
};

async function api(path, options = {}) {
  const headers = { ...(options.headers || {}) };
  if (options.json !== false && !headers['Content-Type']) {
    headers['Content-Type'] = 'application/json';
  }
  if (options.useApiKey !== false && state.sessionApiKey) {
    headers['X-Sinter-Key'] = state.sessionApiKey;
  }

  const response = await fetch(path, { ...options, headers });
  const contentType = response.headers.get('content-type') || '';
  const payload = response.status === 204
    ? null
    : contentType.includes('application/json')
      ? await response.json()
      : await response.text();

  if (!response.ok) {
    const message = typeof payload === 'string'
      ? payload
      : payload?.error || payload?.Error || `HTTP ${response.status}`;
    throw new Error(message || `HTTP ${response.status}`);
  }

  return payload;
}

async function loadState() {
  state.dashboard = await api('/ui/state', { useApiKey: false });
  if (state.dashboard?.snapshot?.showApiKey && state.dashboard.snapshot.apiKey) {
    state.sessionApiKey = state.dashboard.snapshot.apiKey;
    localStorage.setItem('sinter-node-api-key', state.sessionApiKey);
  }
  render();
}

function render() {
  renderTopbar();
  renderStatusStrip();
  renderDetail();
}

function renderTopbar() {
  const selection = document.getElementById('header-selection');
  const status = document.getElementById('header-status');
  const statusLabel = document.getElementById('header-status-label');
  const dashboard = state.dashboard;
  if (!dashboard) {
    selection.textContent = 'Loading selection…';
    status.className = 'status reconnecting';
    statusLabel.textContent = 'Loading…';
    return;
  }
  const bootstrap = dashboard.snapshot?.state?.bootstrapCompleted ? 'ready' : 'bootstrap';
  selection.textContent = `${dashboard.hostname} • ${(dashboard.environment?.listenUrls || []).join(', ') || 'local only'}`;
  status.className = `status ${bootstrap === 'ready' ? 'live' : 'reconnecting'}`;
  statusLabel.textContent = `${bootstrap} • ${dashboard.services.length} services • ${dashboard.managedApplications.length} apps`;
}

function renderDetail() {
  const pane = document.getElementById('detail-pane');
  if (!state.dashboard) {
    pane.innerHTML = '<div class="empty">Loading…</div>';
    return;
  }
  pane.innerHTML = renderOverview();
  wireOverview();
}

function renderOverview() {
  const dashboard = state.dashboard;
  const snapshot = dashboard.snapshot || {};
  const bootstrap = snapshot.state?.bootstrapCompleted;
  const prefixes = (snapshot.state?.servicePrefixes || []).join('\n');
  return `
    <div class="detail-card">
      <h3>Bootstrap and API key</h3>
      ${snapshot.showApiKey ? `<div class="key-box">${escapeHtml(snapshot.apiKey)}</div>` : '<div class="empty">Bootstrap key hidden after setup. Use SinterServer for ongoing management.</div>'}
      <div class="field"><label>Bootstrap / config API key</label><input id="session-api-key" type="password" value="${escapeHtml(state.sessionApiKey)}" placeholder="Only needed to change local bootstrap config"></div>
      <div class="actions"><button id="save-session-key">Use key</button><button class="secondary" id="clear-session-key">Clear</button></div>
    </div>
    <div class="detail-card">
      <h3>Node overview</h3>
      <div class="detail-grid">
        ${renderStat('State', bootstrap ? 'Ready' : 'Bootstrap')}
        ${renderStat('Hostname', dashboard.hostname)}
        ${renderStat('Version', dashboard.version)}
        ${renderStat('Uptime', dashboard.uptime)}
        ${renderStat('OS', dashboard.osDescription)}
        ${renderStat('Runtime', dashboard.frameworkDescription)}
        ${renderStat('Listen', (dashboard.environment?.listenUrls || []).join(', ') || '<host>')}
        ${renderStat('Node ID', snapshot.state?.nodeId || '<none>')}
      </div>
    </div>
    <div class="detail-card">
      <h3>Service prefixes</h3>
      <div class="field"><label>Prefixes</label><textarea id="prefixes-input">${escapeHtml(prefixes)}</textarea></div>
      <div class="actions"><button id="save-prefixes">Save prefixes</button></div>
    </div>
    <div class="detail-card">
      <h3>Observed inventory</h3>
      <div class="inventory-list">
        <div class="inventory-item">
          <strong>Matched services</strong>
          <div class="subtle">${(dashboard.services || []).map(service => escapeHtml(service.name)).join(', ') || 'None'}</div>
        </div>
        <div class="inventory-item">
          <strong>Managed applications</strong>
          <div class="subtle">${(dashboard.managedApplications || []).map(app => escapeHtml(app.appName)).join(', ') || 'None'}</div>
        </div>
      </div>
    </div>
    ${renderLastAction()}`;
}

function renderStat(label, value) {
  return `<div class="stat"><div class="stat-label">${escapeHtml(label)}</div><div class="stat-value">${escapeHtml(value ?? '<none>')}</div></div>`;
}

function renderLastAction() {
  if (!state.lastAction) {
    return '';
  }
  const lines = (state.lastAction.events || []).map(evt => `[${evt.type}] ${evt.message}`).join('\n');
  return `<div class="detail-card"><h3>Last action</h3><div class="event-log">${escapeHtml(lines || state.lastAction.summary || '')}</div></div>`;
}

function wireOverview() {
  document.getElementById('save-session-key').onclick = () => {
    state.sessionApiKey = value('session-api-key');
    if (state.sessionApiKey) {
      localStorage.setItem('sinter-node-api-key', state.sessionApiKey);
      setFlash('Session key stored in this browser.', 'success');
    } else {
      localStorage.removeItem('sinter-node-api-key');
      setFlash('Session key cleared.', 'success');
    }
    render();
  };

  document.getElementById('clear-session-key').onclick = () => {
    state.sessionApiKey = '';
    localStorage.removeItem('sinter-node-api-key');
    setFlash('Session key cleared.', 'success');
    render();
  };

  document.getElementById('save-prefixes').onclick = () => runTask(async () => {
    const prefixes = value('prefixes-input').split(/\r?\n|,/).map(item => item.trim()).filter(Boolean);
    await api('/ui/configure', {
      method: 'POST',
      useApiKey: false,
      body: JSON.stringify({ prefixes, apiKey: state.sessionApiKey || null })
    });
    setFlash('Prefixes saved.', 'success');
    await loadState();
  });
}

async function runTask(task) {
  try {
    clearFlash();
    await task();
  } catch (error) {
    setFlash(normalizeError(error), 'error');
    render();
  }
}

function normalizeError(error) {
  const message = error?.message || String(error || 'Request failed.');
  try {
    const parsed = JSON.parse(message);
    return parsed.Error || parsed.error || message;
  } catch {
    return message;
  }
}

function value(id) {
  return document.getElementById(id).value.trim();
}

function renderStatusStrip() {
  const element = document.getElementById('status-strip');
  if (!state.flash?.message) {
    element.hidden = true;
    element.textContent = '';
    element.className = 'status-strip';
    return;
  }

  element.hidden = false;
  element.textContent = state.flash.message;
  element.className = `status-strip${state.flash.kind === 'success' ? ' success' : ''}`;
}

function badge(status) {
  const normalized = String(status || '').toLowerCase();
  const kind = normalized.includes('active') || normalized.includes('managed') || normalized.includes('ready') || normalized.includes('ok')
    ? 'good'
    : normalized.includes('warn') || normalized.includes('bootstrap') || normalized.includes('idle')
      ? 'warn'
      : normalized.includes('error') || normalized.includes('fail') || normalized.includes('offline')
        ? 'bad'
        : 'neutral';
  return `<span class="badge ${kind}">${escapeHtml(status || 'Unknown')}</span>`;
}

function escapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function setFlash(message, kind) {
  state.flash = { message, kind };
}

function clearFlash() {
  state.flash = null;
}

document.getElementById('refresh-button').addEventListener('click', () => {
  runTask(loadState);
});

document.getElementById('self-update-button').addEventListener('click', () => runTask(async () => {
  if (!confirm('Update SinterNode now? This will pull the latest changes and restart the node service if the update succeeds.')) {
    return;
  }

  state.lastAction = await api('/ui/self-update', {
    method: 'POST',
    useApiKey: false,
    body: JSON.stringify({ apiKey: state.sessionApiKey || null })
  });
  setFlash(state.lastAction.summary, state.lastAction.status === 'Success' ? 'success' : 'error');
  render();
}));

loadState();