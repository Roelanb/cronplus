async function api(path, opts) {
  const res = await fetch(path, opts || {});
  if (!res.ok) throw new Error(await res.text());
  return res;
}
async function reloadConfig(ev) {
  ev && ev.preventDefault && ev.preventDefault();
  try {
    await api('/reload', { method: 'POST' });
    alert('Reload requested');
  } catch (e) {
    alert('Reload failed: ' + e.message);
  }
}

// Task form helpers (only present on task form page)
function el(tag, attrs) {
  var e = document.createElement(tag);
  if (attrs) {
    for (var k in attrs) {
      if (!Object.prototype.hasOwnProperty.call(attrs, k)) continue;
      var v = attrs[k];
      if (k === 'class') e.className = v;
      else if (k === 'for') e.htmlFor = v;
      else e.setAttribute(k, v);
    }
  }
  for (var i = 2; i < arguments.length; i++) {
    var c = arguments[i];
    if (c == null) continue;
    if (typeof c === 'string') e.appendChild(document.createTextNode(c));
    else e.appendChild(c);
  }
  return e;
}

function checkbox(id, checked) {
  var input = el('input', { type: 'checkbox', id: id });
  if (checked) input.setAttribute('checked', 'checked');
  return input;
}

function pickDirectoryTo(targetInputId, fileInputEl) {
  var files = fileInputEl.files;
  if (files && files.length > 0) {
    var f = files[0];
    var dir = '';
    if (f.webkitRelativePath) {
      var rel = f.webkitRelativePath;
      var parts = rel.split('/');
      parts.pop();
      dir = '/' + parts.join('/');
    }
    var inp = document.getElementById(targetInputId);
    if (dir) {
      inp.value = dir;
    } else {
      alert('Browser did not expose the absolute directory path. Please paste the path manually.');
    }
  }
}
function pickFileTo(targetInputId, fileInputEl) {
  var files = fileInputEl.files;
  if (files && files.length > 0) {
    var name = files[0].name || '';
    var inp = document.getElementById(targetInputId);
    if (name) {
      inp.value = '/' + name;
    }
  }
}

async function loadConfig() {
  const res = await api('/config');
  return await res.json();
}
async function saveConfigObj(cfg) {
  const res = await api('/config', { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(cfg) });
  if (res.status !== 204) throw new Error(await res.text());
}
async function deleteTask(id) {
  if (!confirm('Delete task ' + id + '?')) return;
  const cfg = await loadConfig();
  cfg.tasks = (cfg.tasks || []).filter(t => t.id !== id);
  await saveConfigObj(cfg);
  location.reload();
}
async function toggleTask(id, enabled) {
  const cfg = await loadConfig();
  for (const t of (cfg.tasks||[])) {
    if (t.id === id) { t.enabled = !enabled; break; }
  }
  await saveConfigObj(cfg);
  location.reload();
}

function parseOptsCSV(s) {
  var out = {};
  if (!s) return out;
  var arr = s.split(',');
  for (var i = 0; i < arr.length; i++) {
    var part = arr[i];
    if (!part) continue;
    var kv = part.split('=');
    var k = (kv[0] || '').trim();
    var v = (kv[1] || '').trim();
    if (k) out[k] = v;
  }
  return out;
}

// Expose helpers for inline templates if needed
window.reloadConfig = reloadConfig;
window.pickDirectoryTo = pickDirectoryTo;
window.pickFileTo = pickFileTo;
window.deleteTask = deleteTask;
window.toggleTask = toggleTask;
window.parseOptsCSV = parseOptsCSV;
window.checkbox = checkbox;
window.el = el;
