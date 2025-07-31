package api

import (
	"encoding/json"
	"html/template"
	"net/http"
	"os"
	"strings"
)

var baseTpl = template.Must(template.New("base").Parse(`
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Cronplus</title>
<style>
body { font-family: system-ui, -apple-system, Segoe UI, Roboto, Ubuntu, Cantarell, Noto Sans, Arial, sans-serif; margin: 0; background: #0b0f14; color: #e6edf3; }
header, footer { padding: 12px 16px; background: #111827; border-bottom: 1px solid #1f2937; }
footer { border-top: 1px solid #1f2937; border-bottom: none; color: #9ca3af; }
.container { padding: 16px; max-width: 1024px; margin: 0 auto; }
h1, h2, h3 { margin: 0 0 12px 0; }
.card { background: #111827; border: 1px solid #1f2937; border-radius: 8px; padding: 12px; margin-bottom: 16px; }
table { width: 100%; border-collapse: collapse; font-size: 14px; }
th, td { border-bottom: 1px solid #1f2937; padding: 8px; text-align: left; vertical-align: top; }
th { color: #9ca3af; font-weight: 600; }
code, pre { background: #0b1220; border: 1px solid #1f2937; border-radius: 6px; padding: 8px; display: block; overflow-x: auto; }
input[type="text"], input[type="number"], textarea, select { width: 100%; background: #0b1220; border: 1px solid #1f2937; color: #e6edf3; border-radius: 6px; padding: 8px; box-sizing: border-box; }
button, .btn { background: #2563eb; color: white; border: 0; padding: 8px 12px; border-radius: 6px; cursor: pointer; }
button.secondary { background: #374151; }
.grid { display: grid; grid-template-columns: 1fr; gap: 16px; }
@media (min-width: 900px) { .grid.two { grid-template-columns: 1fr 1fr; } .grid.three { grid-template-columns: 1fr 1fr 1fr; } }
.badge { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 12px; }
.badge.yes { background: #065f46; color: #d1fae5; }
.badge.no { background: #7f1d1d; color: #fee2e2; }
a.nav { color: #93c5fd; text-decoration: none; margin-right: 12px; }
a.nav:hover { text-decoration: underline; }
</style>
</head>
<body>
<header>
  <div class="container">
    <h1 style="display:inline-block;margin-right:16px;">Cronplus</h1>
    <nav style="display:inline-block">
      <a class="nav" href="/ui">Dashboard</a>
      <a class="nav" href="/ui/tasks">Tasks</a>
      <a class="nav" href="/ui/config">Config (raw)</a>
    </nav>
  </div>
</header>
<main class="container">
  {{ template "content" . }}
</main>
<footer>
  <div class="container">
    Cronplus UI — Backend v{{.BackendVersion}} — Frontend v{{.FrontendVersion}}
  </div>
</footer>
<script>
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
</script>
</body>
</html>
`))

var dashboardTpl = template.Must(template.Must(baseTpl.Clone()).New("content").Parse(`
<div class="grid two">
  <div class="card">
    <h2>Health</h2>
    <div id="health">Status: <span class="badge {{if .Healthy}}yes{{else}}no{{end}}">{{if .Healthy}}OK{{else}}DOWN{{end}}</span></div>
  </div>
  <div class="card">
    <h2>Actions</h2>
    <button id="reloadBtn" onclick="reloadConfig(event)">Reload</button>
    <a class="btn" href="/ui/tasks" style="margin-left:8px">Manage Tasks</a>
  </div>
</div>
<div class="card">
  <h2>Tasks (snapshot)</h2>
  <table>
    <thead><tr><th>ID</th><th>Enabled</th><th>Directory</th><th>Glob</th><th>Workers</th></tr></thead>
    <tbody>
      {{range .Tasks}}
      <tr>
        <td><code>{{.ID}}</code></td>
        <td>{{if .Enabled}}<span class="badge yes">yes</span>{{else}}<span class="badge no">no</span>{{end}}</td>
        <td><code>{{.Watch.Directory}}</code></td>
        <td><code>{{.Watch.Glob}}</code></td>
        <td>{{.Workers}}</td>
      </tr>
      {{else}}
      <tr><td colspan="5">No tasks</td></tr>
      {{end}}
    </tbody>
  </table>
</div>
`))

var configTpl = template.Must(template.Must(baseTpl.Clone()).New("content").Parse(`
<div class="card">
  <h2>Edit Configuration (raw JSON)</h2>
  <form onsubmit="saveConfig(event)">
    <textarea id="cfg" rows="20">{{.ConfigJSON}}</textarea>
    <div style="margin-top:8px">
      <button type="submit">Apply</button>
      <a href="/ui" class="btn" style="margin-left:8px">Back</a>
    </div>
  </form>
  <p style="color:#9ca3af;margin-top:8px">POSTs the JSON body to /config (server validates and applies).</p>
</div>
<script>
async function saveConfig(ev) {
  ev.preventDefault();
  const ta = document.getElementById('cfg');
  try {
    const body = ta.value;
    const res = await api('/config', { method: 'POST', headers: {'Content-Type': 'application/json'}, body });
    if (res.status === 204) {
      alert('Config applied');
      location.href = '/ui';
    } else {
      alert('Applied with message: ' + await res.text());
    }
  } catch (e) {
    alert('Apply failed: ' + e.message);
  }
}
</script>
`))

var tasksTpl = template.Must(template.Must(baseTpl.Clone()).New("content").Parse(`
<div class="card">
  <h2>Tasks</h2>
  <div style="margin-bottom:8px">
    <a class="btn" href="/ui/task/new">Add Task</a>
    <a class="btn" href="/ui" style="margin-left:8px">Back</a>
  </div>
  <table>
    <thead><tr><th>ID</th><th>Enabled</th><th>Directory</th><th>Glob</th><th>Actions</th></tr></thead>
    <tbody>
      {{range .Tasks}}
      <tr>
        <td><code>{{.ID}}</code></td>
        <td>{{if .Enabled}}<span class="badge yes">yes</span>{{else}}<span class="badge no">no</span>{{end}}</td>
        <td><code>{{.Watch.Directory}}</code></td>
        <td><code>{{.Watch.Glob}}</code></td>
        <td>
          <a class="btn" href="/ui/task/edit?id={{.ID}}">Edit</a>
          <button class="secondary" onclick="deleteTask('{{.ID}}')">Delete</button>
          <button onclick="toggleTask('{{.ID}}', {{.Enabled}})">{{if .Enabled}}Disable{{else}}Enable{{end}}</button>
        </td>
      </tr>
      {{else}}
      <tr><td colspan="5">No tasks</td></tr>
      {{end}}
    </tbody>
  </table>
</div>
<script>
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
</script>
`))

var taskFormTpl = template.Must(template.Must(baseTpl.Clone()).New("content").Parse(`
<div class="card">
  <h2>{{.Mode}} Task</h2>
  <form id="taskForm" onsubmit="return submitTask(event)">
    <div class="grid two">
      <div class="card">
        <h3>General</h3>
        <label>ID<br><input type="text" id="id" value="{{.Task.ID}}" {{if eq .Mode "Edit"}}readonly{{end}}></label><br><br>
        <label><input type="checkbox" id="enabled" {{if .Task.Enabled}}checked{{end}}> Enabled</label>
      </div>
      <div class="card">
        <h3>Watch</h3>
        <label>Directory<br><input type="text" id="watch_directory" value="{{.Task.Watch.Directory}}"></label><br><br>
        <label>Glob<br><input type="text" id="watch_glob" value="{{.Task.Watch.Glob}}"></label><br><br>
        <label>Debounce (ms)<br><input type="number" id="watch_debounce" value="{{.Task.Watch.DebounceMs}}"></label><br><br>
        <label>Stabilization (ms)<br><input type="number" id="watch_stabilization" value="{{.Task.Watch.StabilizationMs}}"></label>
      </div>
    </div>

    <div class="card">
      <h3>Pipeline</h3>
      <div id="steps"></div>
      <div style="margin-top:8px">
        <select id="newStepType">
          <option value="copy">copy</option>
          <option value="delete">delete</option>
          <option value="archive">archive</option>
          <option value="print">print</option>
        </select>
        <button type="button" onclick="addStep()">Add Step</button>
      </div>
    </div>

    <div style="margin-top:8px">
      <button type="submit">Save</button>
      <a href="/ui/tasks" class="btn" style="margin-left:8px">Back</a>
    </div>
  </form>
</div>

<script>
let pipeline = {{.PipelineJSON}};
try {
  if (typeof pipeline === 'string') {
    var parsed = JSON.parse(pipeline);
    if (parsed && (Array.isArray(parsed) || typeof parsed === 'object')) {
      pipeline = parsed;
    }
  }
} catch (e) {
  pipeline = [];
}

// Simple element builder without template literals or spread syntax
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

function render() {
  var container = document.getElementById('steps');
  container.innerHTML = '';
  (pipeline || []).forEach(function (s, idx) {
    var box = el('div', { class: 'card' });
    box.appendChild(el('div', null, el('strong', null, 'Step ' + (idx + 1).toString() + ' — ' + s.type)));
    var retry = {};
    if (s.type === 'copy' && s.copy && s.copy.retry) retry = s.copy.retry;
    else if (s.type === 'delete' && s.delete && s.delete.retry) retry = s.delete.retry;
    else if (s.type === 'archive' && s.archive && s.archive.retry) retry = s.archive.retry;
    else if (s.type === 'print' && s.print && s.print.retry) retry = s.print.retry;

    if (s.type === 'copy') {
      var c = s.copy || {};
      box.appendChild(el('label', null, 'Destination', el('br'), el('input', { type: 'text', value: c.destination || '', id: 'copy_dest_' + idx })));
      box.appendChild(el('br'));
      box.appendChild(el('label', null, checkbox('copy_atomic_' + idx, !!c.atomic), ' Atomic'));
      box.appendChild(el('br'));
      box.appendChild(el('label', null, checkbox('copy_verify_' + idx, !!c.verifyChecksum), ' Verify checksum'));
    } else if (s.type === 'delete') {
      var d = s.delete || {};
      box.appendChild(el('label', null, checkbox('delete_secure_' + idx, !!d.secure), ' Secure delete (placeholder)'));
    } else if (s.type === 'archive') {
      var a = s.archive || {};
      box.appendChild(el('label', null, 'Destination', el('br'), el('input', { type: 'text', value: a.destination || '', id: 'archive_dest_' + idx })));
      box.appendChild(el('br'));
      var sel = el('select', { id: 'archive_conflict_' + idx });
      ['rename', 'overwrite', 'skip'].forEach(function (v) {
        var opt = el('option', { value: v });
        opt.textContent = v;
        if ((a.conflictStrategy || 'rename') === v) opt.selected = true;
        sel.appendChild(opt);
      });
      box.appendChild(el('label', null, 'Conflict Strategy', el('br'), sel));
    } else if (s.type === 'print') {
      var p = s.print || {};
      box.appendChild(el('label', null, 'Printer Name', el('br'), el('input', { type: 'text', value: p.printerName || '', id: 'print_printer_' + idx })));
      var optStr = '';
      if (p.options) {
        var parts = [];
        for (var k in p.options) {
          if (!Object.prototype.hasOwnProperty.call(p.options, k)) continue;
          parts.push(k + '=' + p.options[k]);
        }
        optStr = parts.join(',');
      }
      box.appendChild(el('br'));
      box.appendChild(el('label', null, 'Options (key=value,key2=value2)', el('br'), el('input', { type: 'text', value: optStr, id: 'print_opts_' + idx })));
      box.appendChild(el('br'));
      box.appendChild(el('label', null, 'Timeout (sec)', el('br'), el('input', { type: 'number', value: (p.timeoutSec || 60), id: 'print_timeout_' + idx })));
      box.appendChild(el('br'));
      box.appendChild(el('label', null, 'Copies', el('br'), el('input', { type: 'number', value: (p.copies || 1), id: 'print_copies_' + idx })));
    }

    box.appendChild(el('br'));
    box.appendChild(el('label', null, 'Retry Max', el('br'), el('input', { type: 'number', value: (retry.max || 0), id: 'retry_max_' + idx })));
    box.appendChild(el('br'));
    box.appendChild(el('label', null, 'Retry Backoff (ms)', el('br'), el('input', { type: 'number', value: (retry.backoffMs || 1000), id: 'retry_backoff_' + idx })));

    box.appendChild(el('div', { style: 'margin-top:8px' }, el('button', { type: 'button', onclick: 'removeStep(' + idx + ')' }, 'Remove')));

    container.appendChild(box);
  });
}

function removeStep(idx) {
  pipeline.splice(idx, 1);
  render();
}

function addStep() {
  var t = document.getElementById('newStepType').value;
  var base = { type: t };
  if (t === 'copy') base.copy = { destination: '', atomic: true, verifyChecksum: false, retry: { max: 0, backoffMs: 1000 } };
  if (t === 'delete') base.delete = { secure: false, retry: { max: 0, backoffMs: 1000 } };
  if (t === 'archive') base.archive = { destination: '', conflictStrategy: 'rename', retry: { max: 0, backoffMs: 1000 } };
  if (t === 'print') base.print = { printerName: '', options: {}, timeoutSec: 60, copies: 1, retry: { max: 0, backoffMs: 1000 } };
  pipeline.push(base);
  render();
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

async function submitTask(ev) {
  ev.preventDefault();
  var id = document.getElementById('id').value.trim();
  var enabled = document.getElementById('enabled').checked;
  var dir = document.getElementById('watch_directory').value.trim();
  var glob = document.getElementById('watch_glob').value.trim();
  var debounce = parseInt(document.getElementById('watch_debounce').value || '0', 10);
  var stab = parseInt(document.getElementById('watch_stabilization').value || '0', 10);
  if (!id) { alert('ID required'); return; }
  if (!dir) { alert('Watch directory required'); return; }

  var steps = [];
  (pipeline || []).forEach(function (s, idx) {
    if (s.type === 'copy') {
      steps.push({ type: 'copy', copy: {
        destination: document.getElementById('copy_dest_' + idx).value.trim(),
        atomic: document.getElementById('copy_atomic_' + idx).checked,
        verifyChecksum: document.getElementById('copy_verify_' + idx).checked,
        retry: {
          max: parseInt(document.getElementById('retry_max_' + idx).value || '0', 10),
          backoffMs: parseInt(document.getElementById('retry_backoff_' + idx).value || '1000', 10)
        }
      }});
    } else if (s.type === 'delete') {
      steps.push({ type: 'delete', delete: {
        secure: document.getElementById('delete_secure_' + idx).checked,
        retry: {
          max: parseInt(document.getElementById('retry_max_' + idx).value || '0', 10),
          backoffMs: parseInt(document.getElementById('retry_backoff_' + idx).value || '1000', 10)
        }
      }});
    } else if (s.type === 'archive') {
      steps.push({ type: 'archive', archive: {
        destination: document.getElementById('archive_dest_' + idx).value.trim(),
        conflictStrategy: document.getElementById('archive_conflict_' + idx).value,
        retry: {
          max: parseInt(document.getElementById('retry_max_' + idx).value || '0', 10),
          backoffMs: parseInt(document.getElementById('retry_backoff_' + idx).value || '1000', 10)
        }
      }});
    } else if (s.type === 'print') {
      steps.push({ type: 'print', print: {
        printerName: document.getElementById('print_printer_' + idx).value.trim(),
        options: parseOptsCSV(document.getElementById('print_opts_' + idx).value),
        timeoutSec: parseInt(document.getElementById('print_timeout_' + idx).value || '60', 10),
        copies: parseInt(document.getElementById('print_copies_' + idx).value || '1', 10),
        retry: {
          max: parseInt(document.getElementById('retry_max_' + idx).value || '0', 10),
          backoffMs: parseInt(document.getElementById('retry_backoff_' + idx).value || '1000', 10)
        }
      }});
    }
  });

  var cfgRes = await api('/config');
  var cfg = await cfgRes.json();
  if (!Array.isArray(cfg.tasks)) cfg.tasks = [];

  var mode = '{{.Mode}}';
  if (mode === 'New') {
    for (var i = 0; i < cfg.tasks.length; i++) {
      if (cfg.tasks[i].id === id) { alert('Task id already exists'); return; }
    }
    cfg.tasks.push({
      id: id, enabled: enabled,
      watch: { directory: dir, glob: glob, debounceMs: debounce, stabilizationMs: stab },
      pipeline: steps
    });
  } else {
    for (var j = 0; j < cfg.tasks.length; j++) {
      if (cfg.tasks[j].id === id) {
        cfg.tasks[j] = {
          id: id, enabled: enabled,
          watch: { directory: dir, glob: glob, debounceMs: debounce, stabilizationMs: stab },
          pipeline: steps
        };
        break;
      }
    }
  }

  await api('/config', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(cfg) });
  alert('Task saved');
  location.href = '/ui/tasks';
}

render();
</script>
`))

// mountUI registers server-rendered HTML routes under /ui.
func (s *Server) mountUI() {
	// Dashboard
	s.mux.HandleFunc("/ui", func(w http.ResponseWriter, r *http.Request) {
		type dashboardData struct {
			Healthy         bool
			Tasks           any
			BackendVersion  string
			FrontendVersion string
		}
		// compute health using internal handler
		healthy := true
		rr := &responseRecorder{header: http.Header{}}
		s.handleHealth(rr, r.Clone(r.Context()))
		if rr.status >= 400 {
			healthy = false
		}
		data := dashboardData{
			Healthy:         healthy,
			BackendVersion:  versionFromBuild(),
			FrontendVersion: frontendVersion(),
		}
		if s.ctrl != nil {
			data.Tasks = s.ctrl.TasksSnapshot()
		}
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		_ = dashboardTpl.ExecuteTemplate(w, "base", data)
	})

	// Raw config editor
	s.mux.HandleFunc("/ui/config", func(w http.ResponseWriter, r *http.Request) {
		switch r.Method {
		case http.MethodGet:
			var cfg any
			if s.ctrl != nil {
				cfg = s.ctrl.GetConfig()
			}
			js := "{}"
			if cfg != nil {
				if b, err := json.MarshalIndent(cfg, "", "  "); err == nil {
					js = string(b)
				}
			}
			w.Header().Set("Content-Type", "text/html; charset=utf-8")
			_ = configTpl.ExecuteTemplate(w, "base", map[string]any{
				"ConfigJSON":      strings.TrimSpace(js),
				"BackendVersion":  versionFromBuild(),
				"FrontendVersion": frontendVersion(),
			})
		default:
			http.NotFound(w, r)
		}
	})

	// Tasks list
	s.mux.HandleFunc("/ui/tasks", func(w http.ResponseWriter, r *http.Request) {
		type data struct {
			Tasks           any
			BackendVersion  string
			FrontendVersion string
		}
		var tasks any
		if s.ctrl != nil {
			tasks = s.ctrl.TasksSnapshot()
		}
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		_ = tasksTpl.ExecuteTemplate(w, "base", data{
			Tasks:           tasks,
			BackendVersion:  versionFromBuild(),
			FrontendVersion: frontendVersion(),
		})
	})

	// Task editor (new/edit)
	s.mux.HandleFunc("/ui/task/new", func(w http.ResponseWriter, r *http.Request) {
		type taskEdit struct {
			Mode string
			Task struct {
				ID      string
				Enabled bool
				Watch   struct {
					Directory       string
					Glob            string
					DebounceMs      int
					StabilizationMs int
				}
			}
			PipelineJSON template.JS
		}
		var d taskEdit
		d.Mode = "New"
		d.PipelineJSON = template.JS("[]")
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		_ = taskFormTpl.ExecuteTemplate(w, "base", map[string]any{
			"Mode":            d.Mode,
			"Task":            d.Task,
			"PipelineJSON":    d.PipelineJSON,
			"BackendVersion":  versionFromBuild(),
			"FrontendVersion": frontendVersion(),
		})
	})
	s.mux.HandleFunc("/ui/task/edit", func(w http.ResponseWriter, r *http.Request) {
		type taskEdit struct {
			Mode string
			Task struct {
				ID      string
				Enabled bool
				Watch   struct {
					Directory       string
					Glob            string
					DebounceMs      int
					StabilizationMs int
				}
			}
			PipelineJSON template.JS
		}
		q := r.URL.Query()
		id := q.Get("id")
		// best-effort fetch config and find task
		var taskObj map[string]any
		var pipeline any = []any{}
		if s.ctrl != nil {
			// Robustly coerce config (possibly typed struct) into generic map
			rawCfg, _ := json.Marshal(s.ctrl.GetConfig())
			var cfg map[string]any
			_ = json.Unmarshal(rawCfg, &cfg)
			if arr, ok := cfg["tasks"].([]any); ok {
				for _, it := range arr {
					if m, ok := it.(map[string]any); ok && m["id"] == id {
						taskObj = m
						if pl, ok := m["pipeline"]; ok {
							pipeline = pl
						}
						break
					}
				}
			}
		}
		pj, _ := json.Marshal(pipeline)
		var d taskEdit
		d.Mode = "Edit"
		if taskObj != nil {
			// fill typed fields
			d.Task.ID, _ = taskObj["id"].(string)
			d.Task.Enabled, _ = taskObj["enabled"].(bool)
			if wv, ok := taskObj["watch"].(map[string]any); ok {
				if v, ok := wv["directory"].(string); ok {
					d.Task.Watch.Directory = v
				}
				if v, ok := wv["glob"].(string); ok {
					d.Task.Watch.Glob = v
				}
				if v, ok := wv["debounceMs"].(float64); ok {
					d.Task.Watch.DebounceMs = int(v)
				}
				if v, ok := wv["stabilizationMs"].(float64); ok {
					d.Task.Watch.StabilizationMs = int(v)
				}
			}
		} else {
			// task not found; display empty editor with id filled from query
			d.Task.ID = id
		}
		// Ensure valid JSON array or object is embedded and not empty
		js := "[]"
		if len(pj) > 0 && string(pj) != "null" {
			// Ensure we always embed a JSON array for the client script
			// If the stored value is not an array, wrap it.
			if len(pj) > 0 && pj[0] == '[' {
				js = string(pj)
			} else {
				js = "[" + string(pj) + "]"
			}
		}
		d.PipelineJSON = template.JS(js)
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		_ = taskFormTpl.ExecuteTemplate(w, "base", map[string]any{
			"Mode":            d.Mode,
			"Task":            d.Task,
			"PipelineJSON":    d.PipelineJSON,
			"BackendVersion":  versionFromBuild(),
			"FrontendVersion": frontendVersion(),
		})
	})
}

// responseRecorder is a minimal ResponseWriter to reuse handler logic internally.
type responseRecorder struct {
	header http.Header
	status int
	body   []byte
}

func (rr *responseRecorder) Header() http.Header        { return rr.header }
func (rr *responseRecorder) WriteHeader(statusCode int) { rr.status = statusCode }
func (rr *responseRecorder) Write(b []byte) (int, error) {
	rr.body = append(rr.body, b...)
	return len(b), nil
}

// versionFromBuild returns backend version injected by linker or "dev".
func versionFromBuild() string {
	// Backend version is provided via template data from handlers (see mountUI).
	// We keep this function for template compatibility.
	return ""
}

// frontendVersion reads persisted version/version.frontend file.
func frontendVersion() string {
	// Prefer file under version/frontend.version
	b, err := os.ReadFile("version/frontend.version")
	if err == nil {
		s := strings.TrimSpace(string(b))
		if s != "" {
			return s
		}
	}
	return "0.0.0"
}
