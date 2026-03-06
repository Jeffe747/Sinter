const state = {
  dashboard: null,
  selectedNodeId: null,
  selectedAppId: null,
  mode: 'node',
  authUsersVisible: false,
  editingAuthUserId: null,
  lastAction: null,
  flash: null,
  toastTimer: null,
  progress: null
};

async function api(path, options = {}) {
  const response = await fetch(path, {
    headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
    ...options
  });

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

async function loadDashboard() {
  state.dashboard = await api('/api/state');
  render();
}

function render() {
  renderTopbar();
  renderStatusStrip();
  renderProgressDialog();
  renderNodes();
  renderApps();
  renderDetail();
}

function renderTopbar() {
  const selection = document.getElementById('header-selection');
  const status = document.getElementById('header-status');
  const statusLabel = document.getElementById('header-status-label');
  const nodeCount = state.dashboard?.nodes?.length ?? 0;
  const appCount = state.dashboard?.applications?.length ?? 0;
  const onlineCount = (state.dashboard?.nodes ?? []).filter(node => node.healthStatus === 'Online').length;

  if (state.authUsersVisible) {
    selection.textContent = `Auth users • ${state.dashboard?.authUsers?.length ?? 0} configured`;
  } else if (state.mode === 'app') {
    const app = (state.dashboard?.applications ?? []).find(item => item.id === state.selectedAppId);
    selection.textContent = app ? `Application • ${app.name}` : 'Application • none selected';
  } else {
    const node = (state.dashboard?.nodes ?? []).find(item => item.id === state.selectedNodeId);
    selection.textContent = node ? `Node • ${node.name}` : 'Node • none selected';
  }

  let statusClass = 'live';
  let label = `${onlineCount}/${nodeCount} nodes online • ${appCount} apps`;
  if (nodeCount === 0) {
    statusClass = 'reconnecting';
    label = 'No nodes configured';
  } else if (onlineCount === 0) {
    statusClass = 'error';
    label = 'All nodes offline';
  } else if (onlineCount < nodeCount) {
    statusClass = 'reconnecting';
    label = `${onlineCount}/${nodeCount} nodes online`;
  }

  status.className = `status ${statusClass}`;
  statusLabel.textContent = label;
}

function renderStatusStrip() {
  const element = document.getElementById('status-strip');
  if (!state.flash?.message) {
    element.hidden = true;
    element.innerHTML = '';
    element.className = 'toast';
    return;
  }

  element.hidden = false;
  element.innerHTML = `<div class="toast-body">${escapeHtml(state.flash.message)}</div><div class="toast-progress"></div>`;
  element.className = `toast${state.flash.kind === 'success' ? ' success' : ''}`;
}

function renderProgressDialog() {
  const element = document.getElementById('progress-dialog');
  const progress = state.progress;
  if (!progress) {
    element.hidden = true;
    element.innerHTML = '';
    element.className = 'progress-dialog';
    return;
  }

  const lines = (progress.events || [])
    .map(evt => `[${String(evt.type || 'info').toUpperCase()}] ${evt.message}`)
    .join('\n');
  const statusLabel = progress.status === 'running'
    ? 'Running'
    : progress.status === 'success'
      ? 'Finished'
      : 'Failed';
  const hint = progress.status === 'running'
    ? 'Visible while the action runs.'
    : 'Click to dismiss. Auto-dismiss pauses while hovered.';

  element.hidden = false;
  element.className = `progress-dialog ${progress.status}`;
  element.innerHTML = `
    <div class="progress-dialog-header">
      <div class="progress-dialog-title">${escapeHtml(progress.title)}</div>
      <div class="progress-dialog-status ${progress.status}">${escapeHtml(statusLabel)}</div>
    </div>
    <div class="progress-dialog-body">
      <div class="progress-dialog-summary">${escapeHtml(progress.summary || '')}</div>
      <div class="progress-dialog-meta">${escapeHtml(progress.meta || '')}</div>
      <div class="progress-dialog-log">${escapeHtml(lines || 'Waiting for action events…')}</div>
      <div class="progress-dialog-hint">${escapeHtml(hint)}</div>
    </div>`;
}

function renderNodes() {
  const container = document.getElementById('nodes-list');
  container.innerHTML = '';
  for (const node of state.dashboard?.nodes ?? []) {
    const button = document.createElement('button');
    button.className = `item ${state.mode === 'node' && state.selectedNodeId === node.id ? 'active' : ''}`;
    button.innerHTML = `
      <div><strong>${escapeHtml(node.name)}</strong></div>
      <div class="path">${escapeHtml(node.url)}</div>
      <div style="margin-top:8px;">${badge(node.healthStatus)}<small>${node.snapshot?.managedAppsCount ?? 0} apps • ${node.snapshot?.servicesCount ?? 0} services</small></div>`;
    button.addEventListener('click', () => {
      state.mode = 'node';
      state.selectedNodeId = node.id;
      render();
    });
    container.appendChild(button);
  }

  if (!container.childElementCount) {
    container.innerHTML = '<div class="empty">No nodes registered yet.</div>';
  }
}

function renderApps() {
  const container = document.getElementById('apps-list');
  container.innerHTML = '';
  for (const app of state.dashboard?.applications ?? []) {
    const button = document.createElement('button');
    button.className = `item ${state.mode === 'app' && state.selectedAppId === app.id ? 'active' : ''}`;
    button.innerHTML = `
      <div><strong>${escapeHtml(app.name)}</strong></div>
      <div class="path">${escapeHtml(app.repoUrl)}</div>
      <div style="margin-top:8px;">${badge(app.deploymentStatus)}<small>${escapeHtml(app.nodeName || 'Unassigned')}</small></div>`;
    button.addEventListener('click', () => {
      state.mode = 'app';
      state.selectedAppId = app.id;
      render();
    });
    container.appendChild(button);
  }

  if (!container.childElementCount) {
    container.innerHTML = '<div class="empty">No applications configured yet.</div>';
  }
}

function renderDetail() {
  const pane = document.getElementById('detail-pane');
  if (!state.dashboard) {
    pane.innerHTML = '<div class="empty">Loading…</div>';
    return;
  }

  if (state.authUsersVisible) {
    pane.innerHTML = renderAuthUsers();
    wireAuthUsers();
    return;
  }

  if (state.mode === 'app') {
    const app = state.dashboard.applications.find(item => item.id === state.selectedAppId) || state.dashboard.applications[0];
    if (app) {
      state.selectedAppId = app.id;
      pane.innerHTML = renderAppDetail(app);
      wireAppDetail(app);
      return;
    }
  }

  const node = state.dashboard.nodes.find(item => item.id === state.selectedNodeId) || state.dashboard.nodes[0];
  if (node) {
    state.mode = 'node';
    state.selectedNodeId = node.id;
    pane.innerHTML = renderNodeDetail(node);
    wireNodeDetail(node);
    return;
  }

  pane.innerHTML = '<div class="empty">Add a node or application to begin.</div>';
}

function renderNodeDetail(node) {
  const snapshot = node.snapshot || {};
  const serviceItems = (node.services || []).map(service => `
    <div class="inventory-item">
      <strong>${escapeHtml(service.name)}</strong>
      <div class="subtle">${escapeHtml(service.description || '<no description>')}</div>
      <div class="subtle">Unit: ${escapeHtml(service.unitPath)}</div>
      <div>${badge(service.isManagedByNode ? 'Managed' : 'External')}${badge(service.isActive ? 'Started' : 'Stopped')}${badge(service.isEnabled ? 'Enabled' : 'Disabled')}${badge(service.hasOverride ? 'Override' : 'No override')}</div>
      ${service.overrideWarnings?.length ? `<div class="subtle">Warnings: ${escapeHtml(service.overrideWarnings.join(', '))}</div>` : ''}
      <div class="actions compact-actions">
        <button class="secondary node-service-action" data-node-id="${node.id}" data-service-name="${escapeHtmlAttribute(service.name)}" data-action="start">Start</button>
        <button class="secondary node-service-action" data-node-id="${node.id}" data-service-name="${escapeHtmlAttribute(service.name)}" data-action="stop">Stop</button>
        <button class="secondary node-service-action" data-node-id="${node.id}" data-service-name="${escapeHtmlAttribute(service.name)}" data-action="enable">Enable</button>
        <button class="secondary node-service-action" data-node-id="${node.id}" data-service-name="${escapeHtmlAttribute(service.name)}" data-action="disable">Disable</button>
      </div>
    </div>`).join('');
  const managedAppItems = (node.managedApplications || []).map(app => `
    <div class="inventory-item">
      <strong>${escapeHtml(app.appName)}</strong>
      <div class="subtle">Repo: ${escapeHtml(app.repoUrl)}</div>
      <div class="subtle">Service: ${escapeHtml(app.serviceName)}</div>
      <div class="subtle">Branch: ${escapeHtml(app.branch)} • Releases: ${app.releaseCount}</div>
      <div>${badge(app.currentReleaseExists ? 'Active release' : 'No active release')}</div>
    </div>`).join('');

  return `
    <div class="detail-card">
      <h3>Node settings</h3>
      <div class="field"><label>Name</label><input id="node-name" value="${escapeHtml(node.name)}"></div>
      <div class="field"><label>URL</label><input id="node-url" value="${escapeHtml(node.url)}"></div>
      <div class="field"><label>API key</label><input id="node-key" value="" placeholder="Leave blank to keep current key"></div>
      <div class="actions">
        <button id="save-node">Save</button>
        <button class="secondary" id="refresh-node">Refresh</button>
        <button class="secondary" id="reload-daemon">Reload daemon</button>
        <button class="destructive" id="delete-node">Delete</button>
      </div>
    </div>
    <div class="detail-card">
      <h3>Node info</h3>
      <div class="detail-grid">
        <div><div class="muted">Status</div><div>${badge(node.healthStatus)}</div></div>
        <div><div class="muted">Hostname</div><div>${escapeHtml(snapshot.hostname || '<unavailable>')}</div></div>
        <div><div class="muted">OS</div><div>${escapeHtml(snapshot.osDescription || '<unavailable>')}</div></div>
        <div><div class="muted">Architecture</div><div>${escapeHtml(snapshot.processArchitecture || '<unavailable>')}</div></div>
        <div><div class="muted">Framework</div><div>${escapeHtml(snapshot.frameworkDescription || '<unavailable>')}</div></div>
        <div><div class="muted">Version</div><div>${escapeHtml(snapshot.version || '<unavailable>')}</div></div>
        <div><div class="muted">Uptime</div><div>${escapeHtml(snapshot.uptime || '<unavailable>')}</div></div>
        <div><div class="muted">Listen URLs</div><div>${escapeHtml((snapshot.environment?.listenUrls || []).join(', ') || '<unavailable>')}</div></div>
        <div><div class="muted">Managed apps</div><div>${snapshot.managedAppsCount ?? 0}</div></div>
        <div><div class="muted">Services</div><div>${snapshot.servicesCount ?? 0}</div></div>
      </div>
      ${node.lastError ? `<p class="muted" style="margin-top:12px;color:#ff9898;">${escapeHtml(node.lastError)}</p>` : ''}
    </div>
    <div class="detail-card">
      <h3>Synced services</h3>
      <div class="inventory-list">${serviceItems || '<div class="empty">No synced services on this node yet.</div>'}</div>
    </div>
    <div class="detail-card">
      <h3>Synced managed apps</h3>
      <div class="inventory-list">${managedAppItems || '<div class="empty">No managed applications reported by this node yet.</div>'}</div>
    </div>
    ${renderLastAction()}`;
}

function renderAppDetail(app) {
  const authOptions = ['<option value="">No auth user</option>']
    .concat((state.dashboard.authUsers || []).map(user => `<option value="${user.id}" ${app.gitCredentialId === user.id ? 'selected' : ''}>${escapeHtml(user.name)}</option>`))
    .join('');
  const nodeOptions = ['<option value="">Unassigned</option>']
    .concat((state.dashboard.nodes || []).map(node => `<option value="${node.id}" ${app.nodeId === node.id ? 'selected' : ''}>${escapeHtml(node.name)}</option>`))
    .join('');

  return `
    <div class="detail-card">
      <h3>Application settings</h3>
      <div class="field"><label>Name</label><input id="app-name" value="${escapeHtml(app.name)}"></div>
      <div class="field"><label>Repository</label><input id="app-repo" value="${escapeHtml(app.repoUrl)}"></div>
      <div class="field"><label>Project path</label><input id="app-project" value="${escapeHtml(app.projectPath)}"></div>
      <div class="field"><label>Service name</label><input id="app-service-name" value="${escapeHtml(app.serviceName || '')}" placeholder="Defaults to app name"></div>
      <div class="field"><label>Auth user</label><select id="app-auth-user">${authOptions}</select></div>
      <div class="field"><label>Assigned node</label><select id="app-node">${nodeOptions}</select></div>
      <div class="actions">
        <button id="save-app">Save</button>
        <button class="secondary" id="assign-app">Assign</button>
        <button class="destructive" id="delete-app">Delete</button>
      </div>
    </div>
    <div class="detail-card">
      <h3>Deployment state</h3>
      <div class="detail-grid">
        <div><div class="muted">Node</div><div>${escapeHtml(app.nodeName || '<unassigned>')}</div></div>
        <div><div class="muted">Status</div><div>${badge(app.deploymentStatus)}</div></div>
        <div><div class="muted">Base URL</div><div>${escapeHtml(app.activeBaseUrl)}</div></div>
        <div><div class="muted">Port</div><div>${escapeHtml(app.activePort)}</div></div>
        <div><div class="muted">Last deploy</div><div>${escapeHtml(app.lastDeploymentUtc || '<never>')}</div></div>
        <div><div class="muted">Auth user</div><div>${escapeHtml(app.gitCredentialName || '<none>')}</div></div>
      </div>
      <div class="actions">
        <button id="deploy-app">Deploy</button>
        <button class="secondary" id="redeploy-app">Redeploy</button>
        <button class="secondary" id="restart-service">Restart service</button>
        <button class="secondary" id="reload-daemon-app">Reload daemon</button>
        <button class="destructive" id="uninstall-app">Delete deployment</button>
      </div>
    </div>
    <div class="detail-card">
      <h3>Service unit</h3>
      <div class="field"><textarea id="service-unit">${escapeHtml(app.serviceUnitContent || '')}</textarea></div>
      <div class="actions"><button class="secondary" id="refresh-service-unit">Refresh unit</button><button id="save-service-unit">Save unit</button></div>
    </div>
    <div class="detail-card">
      <h3>Override file</h3>
      <div class="field"><textarea id="override-file">${escapeHtml(app.overrideContent || '')}</textarea></div>
      <div class="actions"><button class="secondary" id="refresh-override">Refresh override</button><button id="save-override">Save override</button></div>
    </div>
    ${renderLastAction()}`;
}

function renderAuthUsers() {
  const editing = (state.dashboard.authUsers || []).find(user => user.id === state.editingAuthUserId) || null;
  const items = (state.dashboard.authUsers || []).map(user => `
    <div class="detail-card">
      <h3>${escapeHtml(user.name)}</h3>
      <p class="muted">Username: ${escapeHtml(user.username || '<none>')} • Used by ${user.usageCount} apps</p>
      <div class="actions">
        <button class="secondary auth-edit" data-id="${user.id}">Edit</button>
        <button class="destructive auth-delete" data-id="${user.id}">Delete</button>
      </div>
    </div>`).join('');
  return `
    <div class="detail-card">
      <h3>${editing ? 'Edit auth user' : 'Add auth user'}</h3>
      <div class="field"><label>Name</label><input id="auth-name" value="${escapeHtml(editing?.name || '')}"></div>
      <div class="field"><label>Username</label><input id="auth-username" value="${escapeHtml(editing?.username || '')}"></div>
      <div class="field"><label>Access token</label><input id="auth-token" type="password" placeholder="${editing ? 'Leave blank to keep existing token' : 'Required'}"></div>
      <div class="actions">
        <button id="save-auth-user">${editing ? 'Save changes' : 'Save auth user'}</button>
        ${editing ? '<button class="secondary" id="cancel-auth-edit">Cancel edit</button>' : ''}
        <button class="secondary" id="close-auth-users">Close</button>
      </div>
    </div>
    ${items || '<div class="empty">No auth users configured yet.</div>'}`;
}

function renderLastAction() {
  if (!state.lastAction) {
    return '';
  }

  const lines = (state.lastAction.events || []).map(evt => `[${evt.type}] ${evt.message}`).join('\n');
  return `<div class="detail-card"><h3>Last action</h3><p class="muted">${escapeHtml(state.lastAction.summary || '')}</p><div class="event-log">${escapeHtml(lines || state.lastAction.summary || '')}</div></div>`;
}

function wireNodeDetail(node) {
  document.getElementById('save-node').onclick = () => runTask(async () => {
    await api(`/api/nodes/${node.id}`, { method: 'PUT', body: JSON.stringify({ name: value('node-name'), url: value('node-url'), apiKey: value('node-key') }) });
    setFlash('Node updated.', 'success');
    await loadDashboard();
  });

  document.getElementById('refresh-node').onclick = () => runTask(async () => {
    state.lastAction = { summary: 'Node refreshed.', events: [] };
    await api(`/api/nodes/${node.id}/refresh`, { method: 'POST' });
    setFlash('Node refresh completed.', 'success');
    await loadDashboard();
  });

  document.getElementById('reload-daemon').onclick = () => runTask(async () => {
    state.lastAction = await api(`/api/nodes/${node.id}/daemon-reload`, { method: 'POST' });
    completeProgress(state.lastAction);
    setFlash(state.lastAction.summary, state.lastAction.status === 'Success' ? 'success' : 'error');
    render();
  }, 'Reloading daemon');

  document.getElementById('delete-node').onclick = () => runTask(async () => {
    if (!confirm('Delete this node?')) {
      return;
    }

    await api(`/api/nodes/${node.id}`, { method: 'DELETE' });
    state.selectedNodeId = null;
    setFlash('Node deleted.', 'success');
    await loadDashboard();
  });

  document.querySelectorAll('.node-service-action').forEach(button => {
    button.addEventListener('click', () => runTask(async () => {
      const serviceName = button.dataset.serviceName;
      const actionName = button.dataset.action;
      state.lastAction = await api(`/api/nodes/${node.id}/services/${actionName}`, {
        method: 'POST',
        body: JSON.stringify({ serviceName })
      });
      completeProgress(state.lastAction);
      setFlash(state.lastAction.summary, state.lastAction.status === 'Success' ? 'success' : 'error');
      await loadDashboard();
    }, describeAction(`/api/nodes/${node.id}/services/${button.dataset.action}`, 'POST')));
  });
}

function wireAppDetail(app) {
  document.getElementById('save-app').onclick = () => runTask(async () => {
    await api(`/api/apps/${app.id}`, { method: 'PUT', body: JSON.stringify(readAppForm()) });
    setFlash('Application updated.', 'success');
    await loadDashboard();
  });

  document.getElementById('assign-app').onclick = () => runTask(async () => {
    await api(`/api/apps/${app.id}/assign`, { method: 'POST', body: JSON.stringify({ nodeId: nullableGuid(value('app-node')) }) });
    setFlash('Assignment updated.', 'success');
    await loadDashboard();
  });

  document.getElementById('delete-app').onclick = () => runTask(async () => {
    if (!confirm('Delete this app definition?')) {
      return;
    }

    await api(`/api/apps/${app.id}`, { method: 'DELETE' });
    state.selectedAppId = null;
    setFlash('Application deleted.', 'success');
    await loadDashboard();
  });

  document.getElementById('deploy-app').onclick = () => action(`/api/apps/${app.id}/deploy`, 'POST');
  document.getElementById('redeploy-app').onclick = () => action(`/api/apps/${app.id}/redeploy`, 'POST');
  document.getElementById('restart-service').onclick = () => action(`/api/apps/${app.id}/restart-service`, 'POST');
  document.getElementById('reload-daemon-app').onclick = () => action(`/api/nodes/${app.nodeId}/daemon-reload`, 'POST');
  document.getElementById('uninstall-app').onclick = () => action(`/api/apps/${app.id}/deployment`, 'DELETE');
  document.getElementById('refresh-service-unit').onclick = () => runTask(async () => {
    await api(`/api/apps/${app.id}/service-unit`);
    setFlash('Service unit refreshed.', 'success');
    await loadDashboard();
  });
  document.getElementById('save-service-unit').onclick = () => action(`/api/apps/${app.id}/service-unit`, 'PUT', { content: value('service-unit'), allowOverwriteUnmanaged: true });
  document.getElementById('refresh-override').onclick = () => runTask(async () => {
    await api(`/api/apps/${app.id}/override`);
    setFlash('Override refreshed.', 'success');
    await loadDashboard();
  });
  document.getElementById('save-override').onclick = () => action(`/api/apps/${app.id}/override`, 'PUT', { content: value('override-file') });
}

function wireAuthUsers() {
  document.getElementById('save-auth-user').onclick = () => runTask(async () => {
    const payload = {
      name: value('auth-name'),
      username: value('auth-username') || null,
      accessToken: value('auth-token') || null
    };
    const path = state.editingAuthUserId ? `/api/auth-users/${state.editingAuthUserId}` : '/api/auth-users';
    const method = state.editingAuthUserId ? 'PUT' : 'POST';
    await api(path, { method, body: JSON.stringify(payload) });
    setFlash(state.editingAuthUserId ? 'Auth user updated.' : 'Auth user created.', 'success');
    state.editingAuthUserId = null;
    await loadDashboard();
    state.authUsersVisible = true;
    render();
  });

  document.getElementById('close-auth-users').onclick = () => {
    state.authUsersVisible = false;
    state.editingAuthUserId = null;
    render();
  };

  const cancelButton = document.getElementById('cancel-auth-edit');
  if (cancelButton) {
    cancelButton.onclick = () => {
      state.editingAuthUserId = null;
      render();
    };
  }

  document.querySelectorAll('.auth-edit').forEach(button => {
    button.addEventListener('click', () => {
      state.editingAuthUserId = button.dataset.id;
      render();
    });
  });

  document.querySelectorAll('.auth-delete').forEach(button => {
    button.addEventListener('click', () => runTask(async () => {
      if (!confirm('Delete this auth user?')) {
        return;
      }

      await api(`/api/auth-users/${button.dataset.id}`, { method: 'DELETE' });
      state.editingAuthUserId = null;
      setFlash('Auth user deleted.', 'success');
      await loadDashboard();
      state.authUsersVisible = true;
      render();
    }));
  });
}

function readAppForm() {
  return {
    name: value('app-name'),
    repoUrl: value('app-repo'),
    projectPath: value('app-project'),
    serviceName: value('app-service-name') || null,
    gitCredentialId: nullableGuid(value('app-auth-user'))
  };
}

async function action(path, method, body) {
  await runTask(async () => {
    state.lastAction = await api(path, { method, body: body ? JSON.stringify(body) : undefined });
    completeProgress(state.lastAction);
    setFlash(state.lastAction.summary, state.lastAction.status === 'Success' ? 'success' : 'error');
    await loadDashboard();
    render();
  }, describeAction(path, method));
}

async function runTask(task, progressTitle = null) {
  try {
    clearFlash();
    if (progressTitle) {
      beginProgress(progressTitle);
    }
    await task();
    if (progressTitle && !state.progress?.completedAt) {
      completeProgress({ status: 'Success', summary: `${progressTitle} completed.`, events: [{ type: 'info', message: `${progressTitle} completed.` }] });
    }
  } catch (error) {
    const message = normalizeError(error);
    if (progressTitle) {
      completeProgress({ status: 'Error', summary: message, events: [{ type: 'error', message }] });
    }
    setFlash(message || 'The request failed.', 'error');
    render();
  }
}

function beginProgress(title, summary = 'Request started. Waiting for node response…') {
  clearProgressDismissTimer();
  state.progress = {
    id: Date.now() + Math.random(),
    title,
    status: 'running',
    summary,
    meta: 'Action is running. Feedback will appear here as soon as it is available.',
    events: [{ type: 'info', message: summary }],
    completedAt: null,
    dismissAt: null,
    dismissRemainingMs: 10000,
    hovered: false,
    dismissTimer: null
  };
  renderProgressDialog();
}

function completeProgress(result) {
  if (!state.progress) {
    return;
  }

  clearProgressDismissTimer();
  const events = Array.isArray(result?.events) && result.events.length
    ? result.events
    : [{ type: result?.status === 'Success' ? 'success' : 'error', message: result?.summary || 'Action finished.' }];
  state.progress = {
    ...state.progress,
    status: result?.status === 'Success' ? 'success' : 'error',
    summary: result?.summary || state.progress.summary,
    meta: `Received ${events.length} event${events.length === 1 ? '' : 's'}.`,
    events,
    completedAt: Date.now(),
    dismissRemainingMs: 10000,
    dismissAt: Date.now() + 10000,
    dismissTimer: null
  };
  scheduleProgressDismiss(10000);
  renderProgressDialog();
}

function scheduleProgressDismiss(durationMs) {
  if (!state.progress || state.progress.status === 'running') {
    return;
  }

  clearProgressDismissTimer();
  state.progress.dismissRemainingMs = durationMs;
  state.progress.dismissAt = Date.now() + durationMs;
  state.progress.dismissTimer = setTimeout(() => {
    dismissProgress();
  }, durationMs);
}

function clearProgressDismissTimer() {
  if (state.progress?.dismissTimer) {
    clearTimeout(state.progress.dismissTimer);
    state.progress.dismissTimer = null;
  }
}

function dismissProgress() {
  clearProgressDismissTimer();
  state.progress = null;
  renderProgressDialog();
}

function pauseProgressDismiss() {
  if (!state.progress || state.progress.status === 'running' || state.progress.hovered) {
    return;
  }

  state.progress.hovered = true;
  if (state.progress.dismissAt) {
    state.progress.dismissRemainingMs = Math.max(0, state.progress.dismissAt - Date.now());
  }
  clearProgressDismissTimer();
}

function resumeProgressDismiss() {
  if (!state.progress || state.progress.status === 'running' || !state.progress.hovered) {
    return;
  }

  state.progress.hovered = false;
  scheduleProgressDismiss(Math.max(1, state.progress.dismissRemainingMs || 10000));
}

function setFlash(message, kind) {
  if (state.toastTimer) {
    clearTimeout(state.toastTimer);
  }

  const id = Date.now() + Math.random();
  state.flash = { id, message, kind };
  state.toastTimer = setTimeout(() => {
    if (state.flash?.id !== id) {
      return;
    }

    state.flash = null;
    state.toastTimer = null;
    renderStatusStrip();
  }, 2000);
}

function clearFlash() {
  if (state.toastTimer) {
    clearTimeout(state.toastTimer);
    state.toastTimer = null;
  }

  state.flash = null;
}

function normalizeError(error) {
  return error?.message || 'The request failed.';
}

function describeAction(path, method) {
  const normalizedMethod = String(method || 'POST').toUpperCase();
  if (path.includes('/services/start')) return 'Starting service';
  if (path.includes('/services/stop')) return 'Stopping service';
  if (path.includes('/services/enable')) return 'Enabling service';
  if (path.includes('/services/disable')) return 'Disabling service';
  if (path.includes('/daemon-reload')) return 'Reloading daemon';
  if (path.includes('/redeploy')) return 'Redeploying application';
  if (path.includes('/deployment') && normalizedMethod === 'DELETE') return 'Deleting deployment';
  if (path.includes('/deploy')) return 'Deploying application';
  if (path.includes('/restart-service')) return 'Restarting application service';
  if (path.includes('/service-unit') && normalizedMethod === 'PUT') return 'Saving service unit';
  if (path.includes('/override') && normalizedMethod === 'PUT') return 'Saving override';
  if (path.includes('/system/self-update')) return 'Updating SinterServer';
  return 'Running action';
}

function value(id) { return document.getElementById(id).value.trim(); }
function nullableGuid(value) { return value ? value : null; }

function badge(status) {
  const normalized = String(status || '').toLowerCase();
  const cls = normalized.includes('online') || normalized.includes('active') || normalized.includes('success') || normalized.includes('managed')
    ? 'good'
    : normalized.includes('error') || normalized.includes('offline')
      ? 'bad'
      : normalized.includes('inactive') || normalized.includes('warn') || normalized.includes('override')
        ? 'warn'
        : 'neutral';
  return `<span class="badge ${cls}">${escapeHtml(status || 'Unknown')}</span>`;
}

function escapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function escapeHtmlAttribute(value) {
  return escapeHtml(value).replaceAll('`', '&#96;');
}

document.getElementById('refresh-button').addEventListener('click', () => runTask(async () => {
  await loadDashboard();
  setFlash('Dashboard refreshed.', 'success');
  render();
}));

document.getElementById('progress-dialog').addEventListener('click', () => {
  if (state.progress?.status !== 'running') {
    dismissProgress();
  }
});

document.getElementById('progress-dialog').addEventListener('mouseenter', () => {
  pauseProgressDismiss();
});

document.getElementById('progress-dialog').addEventListener('mouseleave', () => {
  resumeProgressDismiss();
});

document.getElementById('show-auth-users-button').addEventListener('click', () => {
  state.authUsersVisible = true;
  state.editingAuthUserId = null;
  render();
});

document.getElementById('self-update-button').addEventListener('click', () => runTask(async () => {
  if (!confirm('Update SinterServer now? This will pull the latest changes and restart the server if the update succeeds.')) {
    return;
  }

  state.lastAction = await api('/api/system/self-update', { method: 'POST' });
  completeProgress(state.lastAction);
  setFlash(state.lastAction.summary, state.lastAction.status === 'Success' ? 'success' : 'error');
  render();
}, 'Updating SinterServer'));

document.getElementById('add-node-button').addEventListener('click', () => runTask(async () => {
  const name = prompt('Node name');
  const url = prompt('Node URL');
  const apiKey = prompt('Node API key');
  if (!name || !url || !apiKey) {
    return;
  }

  await api('/api/nodes', { method: 'POST', body: JSON.stringify({ name, url, apiKey }) });
  setFlash('Node created.', 'success');
  await loadDashboard();
}));

document.getElementById('add-app-button').addEventListener('click', () => runTask(async () => {
  const name = prompt('Application name');
  const repoUrl = prompt('Git repository URL');
  const projectPath = prompt('Path to .csproj');
  if (!name || !repoUrl || !projectPath) {
    return;
  }

  await api('/api/apps', { method: 'POST', body: JSON.stringify({ name, repoUrl, projectPath, serviceName: null, gitCredentialId: null }) });
  setFlash('Application created.', 'success');
  await loadDashboard();
}));

loadDashboard().catch(error => {
  setFlash(error.message || 'Unable to load dashboard.', 'error');
  render();
});