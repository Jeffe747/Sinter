const state = {
  dashboard: null,
  selectedServiceName: null,
  selectedAppName: null,
  mode: 'overview',
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
  renderServices();
  renderApps();
  renderDetail();
}

function renderTopbar() {
  const meta = document.getElementById('topbar-meta');
  const dashboard = state.dashboard;
  if (!dashboard) {
    meta.textContent = 'Loading…';
    return;
  }
  const bootstrap = dashboard.snapshot?.state?.bootstrapCompleted ? 'ready' : 'bootstrap';
  meta.textContent = `${dashboard.hostname} • ${bootstrap} • ${dashboard.services.length} services • ${dashboard.managedApplications.length} apps`;
  document.getElementById('services-count').textContent = `${dashboard.services.length}`;
  document.getElementById('apps-count').textContent = `${dashboard.managedApplications.length}`;
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

function renderServices() {
  const container = document.getElementById('services-list');
  container.innerHTML = '';
  for (const service of state.dashboard?.services ?? []) {
    const button = document.createElement('button');
    button.className = `item ${state.mode === 'service' && state.selectedServiceName === service.name ? 'active' : ''}`;
    button.innerHTML = `
      <div class="item-title">${escapeHtml(service.name)}</div>
      <div class="path">${escapeHtml(service.description || '<no description>')}</div>
      <div>${badge(service.isManagedByNode ? 'Managed' : 'Observed')}${badge(service.hasOverride ? 'Override' : 'Plain')}</div>`;
    button.addEventListener('click', () => {
      state.mode = 'service';
      state.selectedServiceName = service.name;
      renderDetail();
      renderServices();
      renderApps();
    });
    container.appendChild(button);
  }

  if (!container.childElementCount) {
    container.innerHTML = '<div class="empty">No matched services.</div>';
  }
}

function renderApps() {
  const container = document.getElementById('apps-list');
  container.innerHTML = '';
  for (const app of state.dashboard?.managedApplications ?? []) {
    const button = document.createElement('button');
    button.className = `item ${state.mode === 'app' && state.selectedAppName === app.appName ? 'active' : ''}`;
    button.innerHTML = `
      <div class="item-title">${escapeHtml(app.appName)}</div>
      <div class="path">${escapeHtml(app.serviceName)}</div>
      <div>${badge(app.currentReleaseExists ? 'Active' : 'Idle')}<span class="path">${escapeHtml(app.branch)}</span></div>`;
    button.addEventListener('click', () => {
      state.mode = 'app';
      state.selectedAppName = app.appName;
      renderDetail();
      renderApps();
      renderServices();
    });
    container.appendChild(button);
  }

  if (!container.childElementCount) {
    container.innerHTML = '<div class="empty">No deployed apps.</div>';
  }
}

function renderDetail() {
  const pane = document.getElementById('detail-pane');
  if (!state.dashboard) {
    pane.innerHTML = '<div class="empty">Loading…</div>';
    return;
  }

  if (state.mode === 'service') {
    const service = state.dashboard.services.find(item => item.name === state.selectedServiceName);
    if (service) {
      pane.innerHTML = renderServiceDetail(service);
      wireServiceDetail(service);
      return;
    }
  }

  if (state.mode === 'app') {
    const app = state.dashboard.managedApplications.find(item => item.appName === state.selectedAppName);
    if (app) {
      pane.innerHTML = renderAppDetail(app);
      wireAppDetail(app);
      return;
    }
  }

  state.mode = 'overview';
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
      ${snapshot.showApiKey ? `<div class="key-box">${escapeHtml(snapshot.apiKey)}</div>` : '<div class="empty">Bootstrap key hidden. Paste it below for protected actions.</div>'}
      <div class="field"><label>Session API key</label><input id="session-api-key" type="password" value="${escapeHtml(state.sessionApiKey)}" placeholder="Required for service and app operations"></div>
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
      <div class="actions"><button id="save-prefixes">Save prefixes</button><button class="secondary" id="self-update">Self update</button></div>
    </div>
    ${renderLastAction()}`;
}

function renderServiceDetail(service) {
  return `
    <div class="detail-card">
      <h3>${escapeHtml(service.name)}</h3>
      <div class="detail-grid">
        ${renderStat('Description', service.description || '<none>')}
        ${renderStat('Unit path', service.unitPath)}
        ${renderStat('Managed', service.isManagedByNode ? 'Yes' : 'No')}
        ${renderStat('Override', service.hasOverride ? 'Present' : 'None')}
      </div>
      ${service.overrideWarnings?.length ? `<div class="inventory-list">${service.overrideWarnings.map(item => `<div class="inventory-item subtle">${escapeHtml(item)}</div>`).join('')}</div>` : ''}
      <div class="actions"><button id="restart-service">Restart</button><button class="secondary" id="daemon-reload">Daemon reload</button></div>
    </div>
    <div class="detail-card">
      <h3>Service unit</h3>
      <div class="field"><textarea id="service-unit"></textarea></div>
      <div class="actions"><button class="secondary" id="load-service-unit">Load unit</button><button id="save-service-unit">Save unit</button></div>
    </div>
    <div class="detail-card">
      <h3>Override</h3>
      <div class="field"><textarea id="override-file"></textarea></div>
      <div class="actions"><button class="secondary" id="load-override">Load override</button><button id="save-override">Save override</button></div>
    </div>
    ${renderLastAction()}`;
}

function renderAppDetail(app) {
  return `
    <div class="detail-card">
      <h3>${escapeHtml(app.appName)}</h3>
      <div class="detail-grid">
        ${renderStat('Repo', app.repoUrl)}
        ${renderStat('Branch', app.branch)}
        ${renderStat('Service', app.serviceName)}
        ${renderStat('Releases', String(app.releaseCount))}
        ${renderStat('Current', shortTail(app.currentRelease))}
        ${renderStat('Last deploy', app.lastDeploymentUtc || 'Never')}
      </div>
      <div class="actions"><button id="restart-app">Restart</button><button class="destructive" id="uninstall-app">Delete deployment</button></div>
    </div>
    <div class="detail-card">
      <h3>Deploy or redeploy</h3>
      <div class="field"><label>Repository</label><input id="deploy-repo" value="${escapeHtml(app.repoUrl)}"></div>
      <div class="field"><label>Project path</label><input id="deploy-project" value="${escapeHtml(app.projectPath || '')}"></div>
      <div class="field"><label>Branch</label><input id="deploy-branch" value="${escapeHtml(app.branch || 'main')}"></div>
      <div class="field"><label>Service name</label><input id="deploy-service" value="${escapeHtml(app.serviceName)}"></div>
      <div class="field"><label>Git token</label><input id="deploy-token" type="password" placeholder="Optional"></div>
      <div class="actions"><button id="deploy-app">Deploy</button></div>
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

  document.getElementById('self-update').onclick = () => runTask(async () => {
    state.lastAction = await ndjson('/api/node/self-update');
    setFlash(state.lastAction.summary, state.lastAction.status === 'Success' ? 'success' : 'error');
    await loadState();
  });
}

function wireServiceDetail(service) {
  document.getElementById('restart-service').onclick = () => runNdjsonAction(`/api/services/${encodeURIComponent(service.name)}/restart`);
  document.getElementById('daemon-reload').onclick = () => runNdjsonAction('/api/system/daemon-reload');
  document.getElementById('load-service-unit').onclick = () => runTask(async () => {
    const content = await api(`/api/services/${encodeURIComponent(service.name)}/unit`, { json: false });
    document.getElementById('service-unit').value = content || '';
    setFlash('Service unit loaded.', 'success');
  });
  document.getElementById('save-service-unit').onclick = () => runTask(async () => {
    await api(`/api/services/${encodeURIComponent(service.name)}/unit`, {
      method: 'PUT',
      body: JSON.stringify({ content: value('service-unit'), allowOverwriteUnmanaged: true })
    });
    setFlash('Service unit saved.', 'success');
    await loadState();
  });
  document.getElementById('load-override').onclick = () => runTask(async () => {
    const content = await api(`/api/services/${encodeURIComponent(service.name)}/override`, { json: false });
    document.getElementById('override-file').value = content || '';
    setFlash('Override loaded.', 'success');
  });
  document.getElementById('save-override').onclick = () => runTask(async () => {
    await api(`/api/services/${encodeURIComponent(service.name)}/override`, {
      method: 'PUT',
      body: JSON.stringify({ content: value('override-file') })
    });
    setFlash('Override saved.', 'success');
    await loadState();
  });
}

function wireAppDetail(app) {
  document.getElementById('restart-app').onclick = () => runNdjsonAction(`/api/apps/${encodeURIComponent(app.appName)}/restart`);
  document.getElementById('uninstall-app').onclick = () => runNdjsonAction(`/api/apps/${encodeURIComponent(app.appName)}`, null, 'DELETE');
  document.getElementById('deploy-app').onclick = () => runTask(async () => {
    state.lastAction = await ndjson('/api/apps/deploy', {
      repoUrl: value('deploy-repo'),
      appName: app.appName,
      branch: value('deploy-branch') || 'main',
      token: value('deploy-token') || null,
      projectPath: value('deploy-project') || null,
      serviceName: value('deploy-service') || null
    });
    setFlash(state.lastAction.summary, state.lastAction.status === 'Success' ? 'success' : 'error');
    await loadState();
  });
}

async function runNdjsonAction(path, body = null, method = 'POST') {
  await runTask(async () => {
    state.lastAction = await ndjson(path, body, method);
    setFlash(state.lastAction.summary, state.lastAction.status === 'Success' ? 'success' : 'error');
    await loadState();
  });
}

async function ndjson(path, body = null, method = 'POST', allowEmptyBody = false) {
  const headers = {};
  if (state.sessionApiKey) {
    headers['X-Sinter-Key'] = state.sessionApiKey;
  }
  if (body !== null || allowEmptyBody) {
    headers['Content-Type'] = 'application/json';
  }

  const response = await fetch(path, {
    method,
    headers,
    body: body === null ? (allowEmptyBody ? '{}' : undefined) : JSON.stringify(body)
  });

  const contentType = response.headers.get('content-type') || '';
  if (!response.ok && !contentType.includes('application/x-ndjson')) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  if (!contentType.includes('application/x-ndjson')) {
    const text = await response.text();
    return { status: response.ok ? 'Success' : 'Error', summary: text || 'Done.', events: [] };
  }

  const text = await response.text();
  const events = text.split(/\r?\n/).filter(Boolean).map(line => JSON.parse(line));
  const last = events[events.length - 1];
  return {
    status: response.ok && last?.type !== 'error' ? 'Success' : 'Error',
    summary: last?.message || 'Done.',
    events
  };
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

function shortTail(path) {
  if (!path) {
    return '<none>';
  }
  const parts = String(path).split(/[\\/]/);
  return parts[parts.length - 1] || path;
}

function badge(status) {
  const normalized = String(status || '').toLowerCase();
  const cls = normalized.includes('ready') || normalized.includes('managed') || normalized.includes('active') || normalized.includes('success')
    ? 'good'
    : normalized.includes('warn') || normalized.includes('override') || normalized.includes('observed') || normalized.includes('idle')
      ? 'warn'
      : normalized.includes('error') || normalized.includes('fail')
        ? 'bad'
        : 'neutral';
  return `<span class="badge ${cls}">${escapeHtml(status || 'Unknown')}</span>`;
}

function setFlash(message, kind) {
  state.flash = { message, kind };
}

function clearFlash() {
  state.flash = null;
}

function escapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

document.getElementById('show-overview-button').addEventListener('click', () => {
  state.mode = 'overview';
  renderDetail();
  renderServices();
  renderApps();
});

document.getElementById('refresh-button').addEventListener('click', () => runTask(async () => {
  await loadState();
  setFlash('Node state refreshed.', 'success');
  render();
}));

loadState().catch(error => {
  setFlash(normalizeError(error), 'error');
  render();
});