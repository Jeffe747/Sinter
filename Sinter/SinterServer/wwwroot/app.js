const state = {
  dashboard: null,
  selectedNodeId: null,
  selectedAppId: null,
  mode: 'node',
  telemetryHistory: {},
  telemetryHistoryLoading: {},
  telemetryRange: '21d',
  nodesCollapsed: localStorage.getItem('sinter-server:nodes-collapsed') === 'true',
  appsCollapsed: localStorage.getItem('sinter-server:apps-collapsed') === 'true',
  authUsersVisible: false,
  editingAuthUserId: null,
  lastAction: null,
  flash: null,
  toastTimer: null,
  progress: null,
  dialog: null
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
  renderLayout();
  renderTopbar();
  renderStatusStrip();
  renderProgressDialog();
  renderDialog();
  renderNodes();
  renderApps();
  renderDetail();
}

function renderLayout() {
  const layout = document.getElementById('server-layout');
  const nodesColumn = document.getElementById('nodes-column');
  const appsColumn = document.getElementById('apps-column');
  const toggleNodes = document.getElementById('toggle-nodes-column');
  const toggleApps = document.getElementById('toggle-apps-column');
  const isCompactViewport = window.matchMedia('(max-width: 980px)').matches;

  layout.classList.toggle('nodes-collapsed', state.nodesCollapsed && !isCompactViewport);
  layout.classList.toggle('apps-collapsed', state.appsCollapsed && !isCompactViewport);
  nodesColumn.classList.toggle('is-collapsed', state.nodesCollapsed && !isCompactViewport);
  appsColumn.classList.toggle('is-collapsed', state.appsCollapsed && !isCompactViewport);

  toggleNodes.textContent = state.nodesCollapsed && !isCompactViewport ? '▸' : '◂';
  toggleApps.textContent = state.appsCollapsed && !isCompactViewport ? '▸' : '◂';
  toggleNodes.setAttribute('aria-expanded', String(!state.nodesCollapsed || isCompactViewport));
  toggleApps.setAttribute('aria-expanded', String(!state.appsCollapsed || isCompactViewport));
  toggleNodes.title = state.nodesCollapsed && !isCompactViewport ? 'Expand Nodes' : 'Collapse Nodes';
  toggleApps.title = state.appsCollapsed && !isCompactViewport ? 'Expand Apps' : 'Collapse Apps';
}

function setColumnCollapsed(column, collapsed) {
  if (column === 'nodes') {
    state.nodesCollapsed = collapsed;
    localStorage.setItem('sinter-server:nodes-collapsed', String(collapsed));
  } else {
    state.appsCollapsed = collapsed;
    localStorage.setItem('sinter-server:apps-collapsed', String(collapsed));
  }

  renderLayout();
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

function renderDialog() {
  const element = document.getElementById('dialog-layer');
  const dialog = state.dialog;
  if (!dialog) {
    element.hidden = true;
    element.innerHTML = '';
    element.className = 'dialog-layer';
    return;
  }

  const description = dialog.description
    ? `<div class="dialog-description" id="dialog-description">${escapeHtml(dialog.description)}</div>`
    : '';
  const body = dialog.variant === 'confirm'
    ? renderConfirmDialogBody(dialog)
    : renderFormDialogBody(dialog);
  const closeButton = dialog.dismissible === false
    ? ''
    : '<button class="secondary icon-button dialog-close" type="button" data-dialog-cancel aria-label="Close dialog">x</button>';

  element.hidden = false;
  element.className = `dialog-layer${dialog.pending ? ' pending' : ''}`;
  element.innerHTML = `
    <div class="dialog-backdrop" data-dialog-backdrop></div>
    <section class="dialog-panel${dialog.tone ? ` ${dialog.tone}` : ''}" role="dialog" aria-modal="true" aria-labelledby="dialog-title" ${dialog.description ? 'aria-describedby="dialog-description"' : ''}>
      <div class="dialog-header">
        <div class="dialog-header-copy">
          <div class="dialog-title" id="dialog-title">${escapeHtml(dialog.title || 'Dialog')}</div>
          ${description}
        </div>
        ${closeButton}
      </div>
      ${body}
      <div class="dialog-footer">
        <button class="secondary" type="button" data-dialog-cancel ${dialog.pending ? 'disabled' : ''}>${escapeHtml(dialog.cancelLabel || 'Cancel')}</button>
        <button class="${escapeHtmlAttribute(dialog.submitClass || '')}" type="button" data-dialog-submit ${dialog.pending ? 'disabled' : ''}>${escapeHtml(dialog.submitLabel || 'Save')}</button>
      </div>
    </section>`;

  if (dialog.focusPending) {
    dialog.focusPending = false;
    requestAnimationFrame(() => {
      focusDialogField(dialog.focusFieldName);
    });
  }
}

function renderConfirmDialogBody(dialog) {
  const message = dialog.message
    ? `<div class="dialog-message">${escapeHtml(dialog.message)}</div>`
    : '';
  const details = dialog.details
    ? `<div class="dialog-note">${escapeHtml(dialog.details)}</div>`
    : '';
  return `<div class="dialog-body dialog-confirm-body">${message}${details}</div>`;
}

function renderFormDialogBody(dialog) {
  const fields = (dialog.fields || []).map(field => {
    const fieldId = `dialog-field-${field.name}`;
    const error = dialog.errors?.[field.name];
    const hint = field.hint
      ? `<div class="dialog-field-hint">${escapeHtml(field.hint)}</div>`
      : '';
    const errorMarkup = error
      ? `<div class="dialog-field-error" data-dialog-error="${escapeHtmlAttribute(field.name)}">${escapeHtml(error)}</div>`
      : '';
    const inputMarkup = field.type === 'textarea'
      ? `<textarea id="${fieldId}" data-dialog-field="${escapeHtmlAttribute(field.name)}" placeholder="${escapeHtmlAttribute(field.placeholder || '')}" ${dialog.pending ? 'disabled' : ''}>${escapeHtml(field.value || '')}</textarea>`
      : `<input id="${fieldId}" data-dialog-field="${escapeHtmlAttribute(field.name)}" type="${escapeHtmlAttribute(field.type || 'text')}" value="${escapeHtmlAttribute(field.value || '')}" placeholder="${escapeHtmlAttribute(field.placeholder || '')}" ${field.autocomplete ? `autocomplete="${escapeHtmlAttribute(field.autocomplete)}"` : ''} ${dialog.pending ? 'disabled' : ''}>`;

    return `
      <div class="field dialog-field${error ? ' has-error' : ''}">
        <label for="${fieldId}">${escapeHtml(field.label)}</label>
        ${inputMarkup}
        ${hint}
        ${errorMarkup}
      </div>`;
  }).join('');

  return `<form class="dialog-body dialog-form-body" id="dialog-form">${fields}</form>`;
}

function openDialog(config) {
  state.dialog = {
    id: Date.now() + Math.random(),
    variant: config.variant || 'confirm',
    title: config.title || 'Dialog',
    description: config.description || '',
    message: config.message || '',
    details: config.details || '',
    submitLabel: config.submitLabel || 'Save',
    submitClass: config.submitClass || '',
    cancelLabel: config.cancelLabel || 'Cancel',
    tone: config.tone || '',
    dismissible: config.dismissible !== false,
    allowBackdropClose: config.allowBackdropClose !== false,
    focusFieldName: config.focusFieldName || config.fields?.[0]?.name || null,
    focusPending: true,
    pending: false,
    errors: {},
    fields: (config.fields || []).map(field => ({
      ...field,
      value: field.value ?? ''
    })),
    onSubmit: config.onSubmit
  };
  renderDialog();
}

function openFormDialog(config) {
  openDialog({ ...config, variant: 'form' });
}

function openConfirmDialog(config) {
  openDialog({
    ...config,
    variant: 'confirm',
    focusFieldName: null
  });
}

function closeDialog(force = false) {
  if (!state.dialog) {
    return;
  }

  if (state.dialog.pending && !force) {
    return;
  }

  state.dialog = null;
  renderDialog();
}

function updateDialogField(name, value) {
  if (!state.dialog?.fields) {
    return;
  }

  const field = state.dialog.fields.find(item => item.name === name);
  if (!field) {
    return;
  }

  field.value = value;
  if (state.dialog.errors?.[name]) {
    delete state.dialog.errors[name];
    const input = document.querySelector(`[data-dialog-field="${cssEscape(name)}"]`);
    input?.closest('.dialog-field')?.classList.remove('has-error');
    document.querySelector(`[data-dialog-error="${cssEscape(name)}"]`)?.remove();
  }
}

function readDialogValues(dialog) {
  return Object.fromEntries((dialog.fields || []).map(field => [field.name, String(field.value ?? '').trim()]));
}

function validateDialog(dialog, values) {
  const errors = {};
  for (const field of dialog.fields || []) {
    if (field.required && !values[field.name]) {
      errors[field.name] = `${field.label} is required.`;
      continue;
    }

    if (typeof field.validate === 'function') {
      const message = field.validate(values[field.name], values);
      if (message) {
        errors[field.name] = message;
      }
    }
  }

  return errors;
}

async function submitDialog() {
  const dialog = state.dialog;
  if (!dialog || dialog.pending || typeof dialog.onSubmit !== 'function') {
    return;
  }

  const values = readDialogValues(dialog);
  const errors = dialog.variant === 'form' ? validateDialog(dialog, values) : {};
  if (Object.keys(errors).length) {
    state.dialog = { ...dialog, errors };
    renderDialog();
    focusDialogField(Object.keys(errors)[0]);
    return;
  }

  const dialogId = dialog.id;
  state.dialog = { ...dialog, pending: true, errors: {} };
  renderDialog();

  let shouldClose = false;
  try {
    shouldClose = await dialog.onSubmit(values) !== false;
  } finally {
    if (!state.dialog || state.dialog.id !== dialogId) {
      return;
    }

    if (shouldClose) {
      closeDialog(true);
      return;
    }

    state.dialog = { ...state.dialog, pending: false };
    renderDialog();
  }
}

function focusDialogField(name = null) {
  const selector = name
    ? `[data-dialog-field="${cssEscape(name)}"]`
    : '.dialog-footer [data-dialog-submit]';
  document.querySelector(selector)?.focus();
}

function showAddNodeDialog() {
  openFormDialog({
    title: 'Register node',
    description: 'Create a new node connection for SinterServer to monitor and control.',
    submitLabel: 'Create node',
    focusFieldName: 'name',
    fields: [
      { name: 'name', label: 'Node name', required: true, placeholder: 'e.g. Production EU 1' },
      { name: 'url', label: 'Node URL', required: true, placeholder: 'https://node.example.com:5001', validate: value => /^https?:\/\//i.test(value) ? '' : 'Enter a valid http or https URL.' },
      { name: 'apiKey', label: 'Node API key', type: 'password', required: true, placeholder: 'Paste the node API key', autocomplete: 'new-password' }
    ],
    onSubmit: async values => {
      let succeeded = false;
      await runTask(async () => {
        await api('/api/nodes', {
          method: 'POST',
          body: JSON.stringify({
            name: values.name,
            url: values.url,
            apiKey: values.apiKey
          })
        });
        setFlash('Node created.', 'success');
        await loadDashboard();
        succeeded = true;
      }, 'Creating node');
      return succeeded;
    }
  });
}

function showAddAppDialog() {
  openFormDialog({
    title: 'Create application',
    description: 'Add an application definition before assigning or deploying it to a node.',
    submitLabel: 'Create application',
    focusFieldName: 'name',
    fields: [
      { name: 'name', label: 'Application name', required: true, placeholder: 'e.g. Billing.Api' },
      { name: 'repoUrl', label: 'Git repository URL', required: true, placeholder: 'https://github.com/org/repo.git', validate: value => /^(https?:\/\/|git@)/i.test(value) ? '' : 'Enter a valid Git repository URL.' },
      { name: 'projectPath', label: 'Project path', required: true, placeholder: 'src/Billing.Api/Billing.Api.csproj', hint: 'Relative path to the .csproj inside the repository.' }
    ],
    onSubmit: async values => {
      let succeeded = false;
      await runTask(async () => {
        await api('/api/apps', {
          method: 'POST',
          body: JSON.stringify({
            name: values.name,
            repoUrl: values.repoUrl,
            projectPath: values.projectPath,
            serviceName: null,
            gitCredentialId: null
          })
        });
        setFlash('Application created.', 'success');
        await loadDashboard();
        succeeded = true;
      }, 'Creating application');
      return succeeded;
    }
  });
}

function showConfirmDialog(options) {
  openConfirmDialog({
    title: options.title,
    description: options.description,
    message: options.message,
    details: options.details,
    submitLabel: options.submitLabel || 'Confirm',
    submitClass: options.submitClass || '',
    tone: options.tone || '',
    onSubmit: async () => options.onConfirm()
  });
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
  const telemetry = snapshot.telemetry || null;
  const history = state.telemetryHistory[node.id] || null;
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
      <h3>Node telemetry</h3>
      <div class="detail-grid">
        <div><div class="muted">CPU usage</div><div>${escapeHtml(formatPercent(telemetry?.cpuUsagePercent))}</div></div>
        <div><div class="muted">CPU cores</div><div>${escapeHtml(formatCount(telemetry?.logicalCpuCount))}</div></div>
        <div><div class="muted">Load avg</div><div>${escapeHtml(formatLoadAverage(telemetry))}</div></div>
        <div><div class="muted">Memory used</div><div>${escapeHtml(formatMemoryUsage(telemetry))}</div></div>
        <div><div class="muted">Memory available</div><div>${escapeHtml(formatBytes(telemetry?.memoryAvailableBytes))}</div></div>
        <div><div class="muted">Disk used</div><div>${escapeHtml(formatDiskUsage(telemetry))}</div></div>
        <div><div class="muted">Disk free</div><div>${escapeHtml(formatBytes(telemetry?.diskFreeBytes))}</div></div>
        <div><div class="muted">Disk mount</div><div>${escapeHtml(telemetry?.diskMountPoint || '<unavailable>')}</div></div>
        <div><div class="muted">Open ports</div><div>${escapeHtml(formatCount(telemetry?.openPortCount))}</div></div>
        <div><div class="muted">Port list</div><div>${escapeHtml(formatPortList(telemetry))}</div></div>
      </div>
      <div style="margin-top:12px;">${renderHealthSignals(telemetry)}</div>
    </div>
    <div class="detail-card">
      <h3>Telemetry history</h3>
      ${renderTelemetryHistory(node.id, history)}
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
  const appInstalled = isApplicationInstalled(app);
  const deployLabel = appInstalled ? 'Redeploy' : 'Deploy';
  const deployButtonClass = appInstalled ? 'destructive' : '';
  const deployWarning = appInstalled
    ? `<div class="warning-banner">
        <strong>Warning</strong>
        <span>Redeploy replaces the current installation on the node and can interrupt the running service.</span>
      </div>`
    : '';
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
      ${deployWarning}
      <div class="actions">
        <button class="${deployButtonClass}" id="deploy-app">${deployLabel}</button>
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
  ensureNodeTelemetryHistory(node.id);

  document.querySelectorAll('.telemetry-range-button').forEach(button => {
    button.addEventListener('click', () => {
      state.telemetryRange = button.dataset.range || '21d';
      renderDetail();
    });
  });

  document.getElementById('save-node').onclick = () => runTask(async () => {
    await api(`/api/nodes/${node.id}`, { method: 'PUT', body: JSON.stringify({ name: value('node-name'), url: value('node-url'), apiKey: value('node-key') }) });
    setFlash('Node updated.', 'success');
    await loadDashboard();
  });

  document.getElementById('refresh-node').onclick = () => runTask(async () => {
    state.lastAction = { summary: 'Node refreshed.', events: [] };
    await api(`/api/nodes/${node.id}/refresh`, { method: 'POST' });
    invalidateNodeTelemetryHistory(node.id);
    setFlash('Node refresh completed.', 'success');
    await loadDashboard();
  });

  document.getElementById('reload-daemon').onclick = () => runTask(async () => {
    state.lastAction = await api(`/api/nodes/${node.id}/daemon-reload`, { method: 'POST' });
    completeProgress(state.lastAction);
    setFlash(state.lastAction.summary, state.lastAction.status === 'Success' ? 'success' : 'error');
    render();
  }, 'Reloading daemon');

  document.getElementById('delete-node').onclick = () => showConfirmDialog({
    title: 'Delete node',
    description: 'This removes the node registration from SinterServer.',
    message: `Delete ${node.name}?`,
    details: 'The node will stop appearing in the dashboard until it is added again.',
    submitLabel: 'Delete node',
    submitClass: 'destructive',
    tone: 'destructive',
    onConfirm: async () => {
      let succeeded = false;
      await runTask(async () => {
        await api(`/api/nodes/${node.id}`, { method: 'DELETE' });
        state.selectedNodeId = null;
        setFlash('Node deleted.', 'success');
        await loadDashboard();
        succeeded = true;
      });
      return succeeded;
    }
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
      invalidateNodeTelemetryHistory(node.id);
      await loadDashboard();
    }, describeAction(`/api/nodes/${node.id}/services/${button.dataset.action}`, 'POST')));
  });
}

function ensureNodeTelemetryHistory(nodeId) {
  if (state.telemetryHistory[nodeId] || state.telemetryHistoryLoading[nodeId]) {
    return;
  }

  state.telemetryHistoryLoading[nodeId] = true;
  api(`/api/nodes/${nodeId}/telemetry`)
    .then(history => {
      state.telemetryHistory[nodeId] = history;
    })
    .catch(error => {
      state.telemetryHistory[nodeId] = { error: normalizeError(error), samples: [] };
    })
    .finally(() => {
      delete state.telemetryHistoryLoading[nodeId];
      if (state.mode === 'node' && state.selectedNodeId === nodeId) {
        renderDetail();
      }
    });
}

function invalidateNodeTelemetryHistory(nodeId) {
  delete state.telemetryHistory[nodeId];
  delete state.telemetryHistoryLoading[nodeId];
}

function renderTelemetryHistory(nodeId, history) {
  if (state.telemetryHistoryLoading[nodeId] && !history) {
    return '<div class="empty">Loading retained telemetry history…</div>';
  }

  if (history?.error) {
    return `<div class="empty">${escapeHtml(history.error)}</div>`;
  }

  const samples = history?.samples || [];
  if (!samples.length) {
    return '<div class="empty">No retained telemetry samples yet. Refresh the node and wait for the next sampling window.</div>';
  }

  const filteredSamples = filterTelemetrySamples(samples, state.telemetryRange);
  if (!filteredSamples.length) {
    return `
      ${renderTelemetryRangeControls()}
      <div class="empty">No telemetry samples are available for the selected time range yet.</div>`;
  }

  const first = filteredSamples[0]?.capturedUtc;
  const last = filteredSamples[filteredSamples.length - 1]?.capturedUtc;

  return `
    ${renderTelemetryRangeControls()}
    <div class="telemetry-history-meta">
      <span>${filteredSamples.length} sample${filteredSamples.length === 1 ? '' : 's'}</span>
      <span>${escapeHtml(formatHistoryWindow(first, last))}</span>
      <span>${history.retentionDays}-day retention</span>
      <span>${Math.round(history.sampleIntervalSeconds / 60)} min cadence</span>
    </div>
    <div class="telemetry-chart-grid">
      ${renderTelemetryChart('CPU %', filteredSamples, sample => sample.cpuUsagePercent, value => formatPercent(value), 100)}
      ${renderTelemetryChart('Load 1m', filteredSamples, sample => sample.loadAverage1m, value => formatNumber(value), null)}
      ${renderTelemetryChart('Memory %', filteredSamples, sample => sample.memoryUsedPercent, value => formatPercent(value), 100)}
      ${renderTelemetryChart('Disk %', filteredSamples, sample => sample.diskUsedPercent, value => formatPercent(value), 100)}
      ${renderTelemetryChart('Open ports', filteredSamples, sample => sample.openPortCount, value => formatCount(value), null)}
    </div>`;
}

function renderTelemetryRangeControls() {
  const ranges = [
    { value: '24h', label: '24h' },
    { value: '7d', label: '7d' },
    { value: '21d', label: '21d' }
  ];

  return `
    <div class="telemetry-range-controls" role="group" aria-label="Telemetry history range">
      ${ranges.map(range => `<button class="secondary telemetry-range-button ${state.telemetryRange === range.value ? 'active' : ''}" type="button" data-range="${range.value}">${range.label}</button>`).join('')}
    </div>`;
}

function filterTelemetrySamples(samples, range) {
  const windowMs = range === '24h'
    ? 24 * 60 * 60 * 1000
    : range === '7d'
      ? 7 * 24 * 60 * 60 * 1000
      : 21 * 24 * 60 * 60 * 1000;

  const latestSample = samples[samples.length - 1];
  const latestTime = latestSample ? new Date(latestSample.capturedUtc).getTime() : Number.NaN;
  if (Number.isNaN(latestTime)) {
    return samples;
  }

  const cutoff = latestTime - windowMs;
  return samples.filter(sample => {
    const sampleTime = new Date(sample.capturedUtc).getTime();
    return !Number.isNaN(sampleTime) && sampleTime >= cutoff;
  });
}

function renderTelemetryChart(title, samples, selector, formatter, hardMax) {
  const points = samples.map(sample => ({ timestamp: sample.capturedUtc, value: selector(sample) }));
  const numericPoints = points.filter(point => typeof point.value === 'number' && Number.isFinite(point.value));
  if (!numericPoints.length) {
    return `
      <div class="telemetry-chart-card">
        <div class="telemetry-chart-head"><strong>${escapeHtml(title)}</strong><span class="muted">No data</span></div>
        <div class="empty">No retained values for this metric yet.</div>
      </div>`;
  }

  const values = numericPoints.map(point => point.value);
  const min = Math.min(...values);
  const max = hardMax == null ? Math.max(...values) : hardMax;
  const floor = hardMax == null ? Math.min(0, min) : 0;
  const ceiling = Math.max(max, floor + 1);
  const current = numericPoints[numericPoints.length - 1].value;

  return `
    <div class="telemetry-chart-card">
      <div class="telemetry-chart-head">
        <strong>${escapeHtml(title)}</strong>
        <span>${escapeHtml(formatter(current))}</span>
      </div>
      ${renderSvgLineChart(points, floor, ceiling)}
      <div class="telemetry-chart-foot">
        <span>Min ${escapeHtml(formatter(min))}</span>
        <span>Max ${escapeHtml(formatter(Math.max(...values)))}</span>
      </div>
    </div>`;
}

function renderSvgLineChart(points, min, max) {
  const width = 320;
  const height = 120;
  const paddingX = 10;
  const paddingY = 14;
  const usableWidth = width - paddingX * 2;
  const usableHeight = height - paddingY * 2;
  const numericPoints = points.filter(point => typeof point.value === 'number' && Number.isFinite(point.value));
  if (!numericPoints.length) {
    return '<div class="empty">No chart data</div>';
  }

  const denominator = Math.max(1, numericPoints.length - 1);
  const range = Math.max(1e-6, max - min);
  const coordinates = numericPoints.map((point, index) => {
    const x = paddingX + (usableWidth * (numericPoints.length === 1 ? 0.5 : index / denominator));
    const y = paddingY + usableHeight - (((point.value - min) / range) * usableHeight);
    return { x, y };
  });

  const path = coordinates.map((point, index) => `${index === 0 ? 'M' : 'L'} ${point.x.toFixed(2)} ${point.y.toFixed(2)}`).join(' ');
  const area = `${path} L ${coordinates[coordinates.length - 1].x.toFixed(2)} ${(height - paddingY).toFixed(2)} L ${coordinates[0].x.toFixed(2)} ${(height - paddingY).toFixed(2)} Z`;
  const firstLabel = points[0]?.timestamp;
  const lastLabel = points[points.length - 1]?.timestamp;

  return `
    <div class="telemetry-chart-shell">
      <svg class="telemetry-chart" viewBox="0 0 ${width} ${height}" preserveAspectRatio="none" role="img" aria-label="Telemetry history chart">
        <line x1="${paddingX}" y1="${height - paddingY}" x2="${width - paddingX}" y2="${height - paddingY}" class="telemetry-chart-axis"></line>
        <line x1="${paddingX}" y1="${paddingY}" x2="${paddingX}" y2="${height - paddingY}" class="telemetry-chart-axis"></line>
        <path d="${area}" class="telemetry-chart-area"></path>
        <path d="${path}" class="telemetry-chart-line"></path>
      </svg>
      <div class="telemetry-chart-labels">
        <span>${escapeHtml(formatChartTimestamp(firstLabel))}</span>
        <span>${escapeHtml(formatChartTimestamp(lastLabel))}</span>
      </div>
    </div>`;
}

function formatHistoryWindow(first, last) {
  if (!first || !last) {
    return 'History unavailable';
  }

  return `${formatChartTimestamp(first)} to ${formatChartTimestamp(last)}`;
}

function formatChartTimestamp(value) {
  if (!value) {
    return '--';
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? '--'
    : date.toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function formatNumber(value) {
  return typeof value === 'number' ? value.toFixed(2) : '<unavailable>';
}

function wireAppDetail(app) {
  const appInstalled = isApplicationInstalled(app);
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

  document.getElementById('delete-app').onclick = () => showConfirmDialog({
    title: 'Delete application',
    description: 'This removes the application definition from SinterServer.',
    message: `Delete ${app.name}?`,
    details: 'Deployment history on nodes is not changed by removing the saved definition here.',
    submitLabel: 'Delete application',
    submitClass: 'destructive',
    tone: 'destructive',
    onConfirm: async () => {
      let succeeded = false;
      await runTask(async () => {
        await api(`/api/apps/${app.id}`, { method: 'DELETE' });
        state.selectedAppId = null;
        setFlash('Application deleted.', 'success');
        await loadDashboard();
        succeeded = true;
      });
      return succeeded;
    }
  });

  document.getElementById('deploy-app').onclick = () => showConfirmDialog({
    title: appInstalled ? 'Redeploy application' : 'Deploy application',
    description: appInstalled
      ? 'This will replace the currently deployed application on the assigned node.'
      : 'This will install the application on the assigned node.',
    message: appInstalled
      ? `Redeploy ${app.name}?`
      : `Deploy ${app.name}?`,
    details: appInstalled
      ? 'Redeploying is destructive: the running service may restart and the current installation will be replaced.'
      : 'Deploying will clone, build, and install the application on the assigned node.',
    submitLabel: appInstalled ? 'Redeploy application' : 'Deploy application',
    submitClass: appInstalled ? 'destructive' : '',
    tone: appInstalled ? 'destructive' : '',
    onConfirm: () => action(`/api/apps/${app.id}/${appInstalled ? 'redeploy' : 'deploy'}`, 'POST')
  });
  document.getElementById('restart-service').onclick = () => action(`/api/apps/${app.id}/restart-service`, 'POST');
  document.getElementById('reload-daemon-app').onclick = () => action(`/api/nodes/${app.nodeId}/daemon-reload`, 'POST');
  document.getElementById('uninstall-app').onclick = () => showConfirmDialog({
    title: 'Delete deployment',
    description: 'This removes the deployed application from the assigned node.',
    message: `Delete deployment for ${app.name}?`,
    details: 'This is destructive: the installed service and deployment on the node will be removed.',
    submitLabel: 'Delete deployment',
    submitClass: 'destructive',
    tone: 'destructive',
    onConfirm: () => action(`/api/apps/${app.id}/deployment`, 'DELETE')
  });
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
    button.addEventListener('click', () => showConfirmDialog({
      title: 'Delete auth user',
      description: 'This removes the saved credential from SinterServer.',
      message: 'Delete this auth user?',
      details: 'Applications using it will need a different auth user before their next authenticated fetch.',
      submitLabel: 'Delete auth user',
      submitClass: 'destructive',
      tone: 'destructive',
      onConfirm: async () => {
        let succeeded = false;
        await runTask(async () => {
          await api(`/api/auth-users/${button.dataset.id}`, { method: 'DELETE' });
          state.editingAuthUserId = null;
          setFlash('Auth user deleted.', 'success');
          await loadDashboard();
          state.authUsersVisible = true;
          render();
          succeeded = true;
        });
        return succeeded;
      }
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
  let succeeded = false;
  await runTask(async () => {
    state.lastAction = await api(path, { method, body: body ? JSON.stringify(body) : undefined });
    completeProgress(state.lastAction);
    setFlash(state.lastAction.summary, state.lastAction.status === 'Success' ? 'success' : 'error');
    await loadDashboard();
    render();
    succeeded = true;
  }, describeAction(path, method));
  return succeeded;
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

function isApplicationInstalled(app) {
  return app?.deploymentStatus === 'Active';
}

function formatPercent(value) {
  return typeof value === 'number' ? `${value.toFixed(1)}%` : '<unavailable>';
}

function formatCount(value) {
  return typeof value === 'number' ? String(value) : '<unavailable>';
}

function formatBytes(value) {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '<unavailable>';
  }

  const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
  let size = value;
  let unitIndex = 0;
  while (size >= 1024 && unitIndex < units.length - 1) {
    size /= 1024;
    unitIndex += 1;
  }

  return `${size.toFixed(size >= 10 || unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`;
}

function formatLoadAverage(telemetry) {
  if (!telemetry) {
    return '<unavailable>';
  }

  const values = [telemetry.loadAverage1m, telemetry.loadAverage5m, telemetry.loadAverage15m];
  if (!values.some(value => typeof value === 'number')) {
    return '<unavailable>';
  }

  return values.map(value => typeof value === 'number' ? value.toFixed(2) : '--').join(' / ');
}

function formatMemoryUsage(telemetry) {
  if (!telemetry || typeof telemetry.memoryTotalBytes !== 'number' || typeof telemetry.memoryAvailableBytes !== 'number') {
    return '<unavailable>';
  }

  const usedBytes = Math.max(0, telemetry.memoryTotalBytes - telemetry.memoryAvailableBytes);
  return `${formatBytes(usedBytes)} / ${formatBytes(telemetry.memoryTotalBytes)} (${formatPercent(telemetry.memoryUsedPercent)})`;
}

function formatDiskUsage(telemetry) {
  if (!telemetry || typeof telemetry.diskTotalBytes !== 'number' || typeof telemetry.diskFreeBytes !== 'number') {
    return '<unavailable>';
  }

  const usedBytes = Math.max(0, telemetry.diskTotalBytes - telemetry.diskFreeBytes);
  return `${formatBytes(usedBytes)} / ${formatBytes(telemetry.diskTotalBytes)} (${formatPercent(telemetry.diskUsedPercent)})`;
}

function formatPortList(telemetry) {
  const ports = telemetry?.openPorts || [];
  if (!ports.length) {
    return '<none reported>';
  }

  const visible = ports.slice(0, 12);
  const suffix = ports.length > visible.length ? ` +${ports.length - visible.length} more` : '';
  return `${visible.join(', ')}${suffix}`;
}

function renderHealthSignals(telemetry) {
  const signals = telemetry?.healthSignals || [];
  if (!signals.length) {
    return '<span class="badge good">Stable</span>';
  }

  return signals.map(signal => `<span class="badge warn">${escapeHtml(signal)}</span>`).join('');
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

function cssEscape(value) {
  return typeof CSS !== 'undefined' && typeof CSS.escape === 'function'
    ? CSS.escape(value)
    : String(value).replaceAll('"', '\\"');
}

document.getElementById('refresh-button').addEventListener('click', () => runTask(async () => {
  await loadDashboard();
  setFlash('Dashboard refreshed.', 'success');
  render();
}));

document.getElementById('toggle-nodes-column').addEventListener('click', () => {
  setColumnCollapsed('nodes', !state.nodesCollapsed);
});

document.getElementById('toggle-apps-column').addEventListener('click', () => {
  setColumnCollapsed('apps', !state.appsCollapsed);
});

window.addEventListener('resize', () => {
  renderLayout();
});

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

document.getElementById('dialog-layer').addEventListener('click', event => {
  if (!state.dialog) {
    return;
  }

  if (event.target instanceof HTMLElement && event.target.matches('[data-dialog-submit]')) {
    submitDialog();
    return;
  }

  if (event.target instanceof HTMLElement && event.target.matches('[data-dialog-cancel]')) {
    closeDialog();
    return;
  }

  if (event.target instanceof HTMLElement && event.target.matches('[data-dialog-backdrop]') && state.dialog.allowBackdropClose) {
    closeDialog();
  }
});

document.getElementById('dialog-layer').addEventListener('input', event => {
  const target = event.target;
  if (!(target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement)) {
    return;
  }

  const fieldName = target.dataset.dialogField;
  if (!fieldName) {
    return;
  }

  updateDialogField(fieldName, target.value);
});

document.getElementById('dialog-layer').addEventListener('submit', event => {
  const target = event.target;
  if (!(target instanceof HTMLFormElement) || target.id !== 'dialog-form') {
    return;
  }

  event.preventDefault();
  submitDialog();
});

window.addEventListener('keydown', event => {
  if (event.key === 'Escape' && state.dialog?.dismissible !== false) {
    closeDialog();
  }
});

document.getElementById('show-auth-users-button').addEventListener('click', () => {
  state.authUsersVisible = true;
  state.editingAuthUserId = null;
  render();
});

document.getElementById('self-update-button').addEventListener('click', () => showConfirmDialog({
  title: 'Update SinterServer',
  description: 'This pulls the latest changes and restarts the server if the update succeeds.',
  message: 'Start the SinterServer self-update now?',
  details: 'The dashboard may disconnect briefly while the service restarts.',
  submitLabel: 'Start update',
  onConfirm: async () => {
    let succeeded = false;
    await runTask(async () => {
      state.lastAction = await api('/api/system/self-update', { method: 'POST' });
      completeProgress(state.lastAction);
      setFlash(state.lastAction.summary, state.lastAction.status === 'Success' ? 'success' : 'error');
      render();
      succeeded = true;
    }, 'Updating SinterServer');
    return succeeded;
  }
}));

document.getElementById('add-node-button').addEventListener('click', () => {
  showAddNodeDialog();
});

document.getElementById('add-app-button').addEventListener('click', () => {
  showAddAppDialog();
});

loadDashboard().catch(error => {
  setFlash(error.message || 'Unable to load dashboard.', 'error');
  render();
});