using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Facepunch.Steps;

/// <summary>
/// <c>index.html</c> beside a render-tests run: every preset as a tile, failures first, and an inspector that compares
/// each capture with its golden truth image - split view, either image alone, a heatmap of the difference and the
/// pixels over threshold, with the values under the cursor. One file with the images inside it, so it works from a
/// downloaded CI artifact.
/// </summary>
internal static class RenderTestsReport
{
	public static string Write( string output, string goldenTruth, IEnumerable<(string Preset, string Suite)> presets, string run )
	{
		var tests = new JsonArray();
		foreach ( var (preset, suite) in presets )
		{
			// A GameObject preset with a change has a second capture after it, which gets an entry of its own
			var stem = Regex.Replace( preset, "[^A-Za-z0-9]+", "_" ).Trim( '_' ).ToLowerInvariant();
			foreach ( var changed in new[] { false, true } )
			{
				var name = changed ? $"{stem}_changed" : stem;
				var capture = Path.Combine( output, $"{name}.png" );
				var resultsPath = Path.Combine( output, $"{name}.results.json" );
				if ( changed && !File.Exists( resultsPath ) ) continue;
				var results = File.Exists( resultsPath ) ? JsonNode.Parse( File.ReadAllText( resultsPath ) ) : null;

				// The golden truth it matched, or else the closest one it was compared with
				var attempts = results?["attempts"] as JsonArray;
				var closest = attempts?.OfType<JsonNode>().OrderByDescending( x => x["passed"]?.GetValue<bool>() ?? false ).ThenBy( x => x["maxDiff"]?.GetValue<int>() ?? 255 ).FirstOrDefault();
				var goldenPath = (string)results?["matched"] ?? (string)closest?["goldenTruth"];
				var golden = goldenPath is null ? null : Path.Combine( goldenTruth, goldenPath );

				tests.Add( new JsonObject
				{
					["preset"] = changed ? $"{preset} · changed" : preset,
					["accept"] = preset,
					["suite"] = suite,
					["outcome"] = (string)results?["outcome"] ?? "unchecked",
					["golden"] = goldenPath,
					["maxDiff"] = closest?["maxDiff"]?.DeepClone(),
					["different"] = closest?["different"]?.DeepClone(),
					["psnr"] = closest?["psnr"]?.DeepClone(),
					["error"] = closest?["error"]?.DeepClone(),
					["diffs"] = closest?["diffs"]?.DeepClone(),
					["thresholds"] = closest?["thresholds"]?.DeepClone(),
					["validation"] = results?["validationMessages"]?.DeepClone(),
					["allowed"] = results?["allowedValidationMessages"]?.DeepClone(),
					["captureImage"] = DataUri( capture ),
					["goldenImage"] = DataUri( golden ),
				} );
			}
		}

		// What rendered them, from the first results that say
		var first = Directory.EnumerateFiles( output, "*.results.json" ).Select( x => JsonNode.Parse( File.ReadAllText( x ) ) ).FirstOrDefault();
		if ( first is not null ) run = $"{run}   {(string)first["gpu"]}   {(string)first["cpuFeatures"]}";

		var data = new JsonObject { ["run"] = run, ["tests"] = tests };
		var path = Path.Combine( output, "index.html" );
		File.WriteAllText( path, Page.Replace( "/*DATA*/null", data.ToJsonString() ) );
		return path;
	}

	static string DataUri( string path ) => path is not null && File.Exists( path ) ? $"data:image/png;base64,{Convert.ToBase64String( File.ReadAllBytes( path ) )}" : null;

	const string Page = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Render tests</title>
<style>
:root { --bg: #1b1b1b; --pane: #242424; --band: #2e2e2e; --line: #3a3a3a; --text: #cfcfcf; --dim: #8a8a8a;
        --pass: #5d8a4a; --fail: #b8423a; --warn: #b88a2e; }
* { box-sizing: border-box; }
html, body { margin: 0; height: 100%; background: var(--bg); color: var(--text); font: 12px/1.4 system-ui, sans-serif; }
body { display: flex; flex-direction: column; }
.mono, .num { font-family: ui-monospace, Consolas, monospace; }
.num { text-align: right; }
header { flex: none; background: var(--band); border-bottom: 1px solid var(--line); padding: 6px 10px; display: flex; gap: 18px; flex-wrap: wrap; }
header b { font-weight: 600; }
.fail-t { color: var(--fail); } .pass-t { color: var(--pass); } .warn-t { color: var(--warn); }
#grid { padding: 8px 10px; border-bottom: 1px solid var(--line); max-height: 38vh; overflow-y: auto; flex: none; }
.suite { display: flex; align-items: flex-start; gap: 8px; margin-bottom: 6px; }
.suite > span { width: 70px; color: var(--dim); padding-top: 2px; }
.tiles { display: flex; flex-wrap: wrap; gap: 3px; flex: 1; }
.tile { width: 112px; cursor: pointer; background: var(--pane); border: 1px solid var(--line); }
.tile img { display: block; width: 100%; aspect-ratio: 16 / 9; object-fit: cover; }
.tile div { padding: 1px 4px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; border-top: 3px solid var(--pass); }
.tile.fail div { border-top-color: var(--fail); } .tile.warn div { border-top-color: var(--warn); }
.tile.sel { outline: 1px solid var(--text); }
#main { display: flex; flex: 1; min-height: 360px; }
#list { width: 230px; border-right: 1px solid var(--line); overflow-y: auto; background: var(--pane); }
#list .opts { padding: 6px 8px; border-bottom: 1px solid var(--line); display: flex; flex-direction: column; gap: 3px; }
.row { display: flex; gap: 6px; padding: 3px 6px; cursor: pointer; border-left: 3px solid var(--pass); align-items: center; }
.row.fail { border-left-color: var(--fail); background: #3a2422; } .row.warn { border-left-color: var(--warn); }
.row.sel { background: #3d3d3d; }
.row img { width: 56px; aspect-ratio: 16 / 9; object-fit: cover; }
.row .n { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
#view { flex: 1; display: flex; flex-direction: column; min-width: 0; min-height: 0; }
#bar { background: var(--band); border-bottom: 1px solid var(--line); padding: 4px 8px; display: flex; gap: 4px; flex-wrap: wrap; align-items: center; }
button { background: var(--pane); color: var(--text); border: 1px solid var(--line); padding: 2px 8px; font: inherit; cursor: pointer; border-radius: 0; }
button.on { background: #4a4a4a; border-color: #6a6a6a; }
#readout { margin-left: auto; color: var(--dim); }
#stage { flex: 1; overflow: auto; position: relative; background: #111; min-height: 0; }
#stage canvas { display: block; image-rendering: pixelated; cursor: crosshair; }
#facts { flex: none; border-top: 1px solid var(--line); background: var(--pane); padding: 6px 10px; display: grid; grid-template-columns: max-content minmax(0, 1fr) max-content minmax(0, 1fr); gap: 2px 18px; }
#facts > span { overflow-wrap: anywhere; }
#facts .k { color: var(--dim); }
#msgs { grid-column: 1 / -1; white-space: pre-wrap; color: var(--fail); max-height: 120px; overflow: auto; }
.cmd { grid-column: 1 / -1; color: var(--dim); }
.cmd code { color: var(--text); }
</style>
</head>
<body>
<header id="head"></header>
<div id="grid"></div>
<div id="main">
  <div id="list">
    <div class="opts">
      <label><input type="checkbox" id="failingOnly"> Failing only <span class="mono">(f)</span></label>
      <label><input type="checkbox" id="sortPsnr"> Sort by PSNR</label>
    </div>
    <div id="rows"></div>
  </div>
  <div id="view">
    <div id="bar">
      <button data-mode="compare">1 Compare</button>
      <button data-mode="capture">2 Capture</button>
      <button data-mode="golden">3 Golden truth</button>
      <button data-mode="heat">4 Heatmap</button>
      <button data-mode="over">5 Over threshold</button>
      <span style="width:10px"></span>
      <button id="zoom">z 1:1</button>
      <span id="readout" class="mono"></span>
    </div>
    <div id="stage"><canvas id="canvas"></canvas></div>
    <div id="facts"></div>
  </div>
</div>
<script>
const data = /*DATA*/null;
const tests = data.tests;
const state = { sel: 0, mode: 'compare', split: 0.5, zoom: 2, failingOnly: false, sortPsnr: false };
const failed = t => t.outcome !== 'pass';
const cls = t => t.outcome === 'pass' ? '' : (t.outcome === 'missing' || t.outcome === 'unchecked') ? 'warn' : 'fail';
const esc = s => String(s ?? '').replace(/[&<>]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' })[c]);
const loaded = new Map();

function head() {
  const f = tests.filter(failed).length;
  const presets = new Set(tests.map(t => t.accept)).size;
  document.getElementById('head').innerHTML =
    `<b>Render tests</b><span class="mono">${presets} presets, ${tests.length} frames</span>` +
    `<span class="mono pass-t">${tests.length - f} passed</span>` +
    `<span class="mono ${f ? 'fail-t' : ''}">${f} failed</span>` +
    `<span class="mono">${esc(data.run)}</span>`;
}

function grid() {
  const suites = [...new Set(tests.map(t => t.suite))];
  document.getElementById('grid').innerHTML = suites.map(s =>
    `<div class="suite"><span>${esc(s)}</span><div class="tiles">` +
    tests.map((t, i) => t.suite !== s ? '' :
      `<div class="tile ${cls(t)}" data-i="${i}" title="${esc(t.preset)}"><img src="${t.captureImage || t.goldenImage || ''}" alt=""><div>${esc(t.preset)}</div></div>`).join('') +
    `</div></div>`).join('');
  document.querySelectorAll('.tile').forEach(e => e.onclick = () => select(+e.dataset.i));
}

function order() {
  let idx = tests.map((t, i) => i);
  if (state.failingOnly) idx = idx.filter(i => failed(tests[i]));
  idx.sort((a, b) => (failed(tests[b]) - failed(tests[a])) || (state.sortPsnr ? (tests[a].psnr ?? 1e9) - (tests[b].psnr ?? 1e9) : a - b));
  return idx;
}

function list() {
  document.getElementById('rows').innerHTML = order().map(i => {
    const t = tests[i];
    const stat = t.outcome === 'pass' ? (t.maxDiff ? `±${t.maxDiff}` : '') : esc(t.outcome);
    return `<div class="row ${cls(t)} ${i === state.sel ? 'sel' : ''}" data-i="${i}"><img src="${t.captureImage || t.goldenImage || ''}" alt=""><span class="n">${esc(t.preset)}</span><span class="mono">${stat}</span></div>`;
  }).join('');
  document.querySelectorAll('.row').forEach(e => e.onclick = () => select(+e.dataset.i));
  document.querySelectorAll('.tile').forEach(e => e.classList.toggle('sel', +e.dataset.i === state.sel));
}

function pixels(src) {
  if (!src) return Promise.resolve(null);
  if (loaded.has(src)) return loaded.get(src);
  const p = new Promise(res => {
    const img = new Image();
    img.onload = () => {
      const c = document.createElement('canvas'); c.width = img.width; c.height = img.height;
      const g = c.getContext('2d'); g.drawImage(img, 0, 0);
      res(g.getImageData(0, 0, img.width, img.height));
    };
    img.onerror = () => res(null);
    img.src = src;
  });
  loaded.set(src, p);
  return p;
}

let cur = null;
async function select(i) {
  state.sel = i;
  list();
  const t = tests[i];
  const [a, b] = await Promise.all([pixels(t.captureImage), pixels(t.goldenImage)]);
  if (state.sel !== i) return;
  cur = { t, a, b };
  facts();
  draw();
}

function facts() {
  const t = cur.t;
  const diffs = Object.entries(t.diffs || {}).map(([k, v]) => `${k}:${v}`).join('  ') || '-';
  const th = Object.entries(t.thresholds || {}).map(([k, v]) => `[${k}] ≤ ${v}`).join('  ') || '-';
  const msgs = (t.validation || []).join('\n\n');
  document.getElementById('facts').innerHTML =
    `<span class="k">preset</span><span>${esc(t.preset)}</span><span class="k">outcome</span><span class="${cls(t) ? 'fail-t' : 'pass-t'} mono">${esc(t.outcome)}</span>` +
    `<span class="k">max diff</span><span class="mono">${t.maxDiff ?? '-'}</span><span class="k">pixels differ</span><span class="mono">${t.different ?? '-'}</span>` +
    `<span class="k">PSNR</span><span class="mono">${t.psnr != null ? t.psnr.toFixed(1) + ' dB' : (t.maxDiff === 0 ? 'inf' : '-')}</span><span class="k">golden truth</span><span class="mono">${esc(t.golden || 'none')}</span>` +
    `<span class="k">histogram</span><span class="mono">${esc(diffs)}</span><span class="k">thresholds</span><span class="mono">${esc(th)}</span>` +
    (t.error ? `<span class="k">error</span><span class="mono fail-t" style="grid-column: 2 / -1">${esc(t.error)}</span>` : '') +
    (msgs ? `<div id="msgs" class="mono">${esc(msgs)}</div>` : '') +
    `<div class="cmd">accept this capture: <code>dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- render-tests --update --preset "${esc(t.accept)}"</code></div>`;
}

function maxDiff(a, b, o) {
  return Math.max(Math.abs(a[o] - b[o]), Math.abs(a[o + 1] - b[o + 1]), Math.abs(a[o + 2] - b[o + 2]));
}

function draw() {
  const canvas = document.getElementById('canvas');
  if (!cur || !(cur.a || cur.b)) { canvas.width = canvas.height = 0; return; }
  const { a, b } = cur;
  const base = a || b;
  const w = base.width, h = base.height;
  const out = new ImageData(w, h);
  const same = a && b && a.width === b.width && a.height === b.height;
  const splitX = Math.round(state.split * w);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const o = (y * w + x) * 4;
    let r, g, bl;
    if (state.mode === 'capture' || !b) { [r, g, bl] = [base.data[o], base.data[o + 1], base.data[o + 2]]; if (a) [r, g, bl] = [a.data[o], a.data[o + 1], a.data[o + 2]]; }
    else if (state.mode === 'golden' || !a || !same) { [r, g, bl] = [b.data[o], b.data[o + 1], b.data[o + 2]]; }
    else if (state.mode === 'compare') { const s = x < splitX ? a : b; [r, g, bl] = [s.data[o], s.data[o + 1], s.data[o + 2]]; if (x === splitX) [r, g, bl] = [255, 255, 255]; }
    else {
      const d = maxDiff(a.data, b.data, o);
      if (state.mode === 'heat') { const v = Math.min(255, d * 16); [r, g, bl] = [v, Math.max(0, v - 128) * 2, 0]; }
      else { const dim = (a.data[o] + a.data[o + 1] + a.data[o + 2]) / 9; [r, g, bl] = d >= 3 ? [255, 40, 40] : [dim, dim, dim]; }
    }
    out.data[o] = r; out.data[o + 1] = g; out.data[o + 2] = bl; out.data[o + 3] = 255;
  }
  canvas.width = w; canvas.height = h;
  canvas.getContext('2d').putImageData(out, 0, 0);
  canvas.style.width = state.zoom === 2 ? '' : (w * state.zoom) + 'px';
  if (state.zoom === 2) { const s = document.getElementById('stage'); const f = Math.min(s.clientWidth / w, s.clientHeight / h); canvas.style.width = Math.floor(w * Math.max(f, 0.1)) + 'px'; }
  document.querySelectorAll('#bar button[data-mode]').forEach(e => e.classList.toggle('on', e.dataset.mode === state.mode));
  document.getElementById('zoom').textContent = state.zoom === 2 ? 'z fit' : 'z 1:1';
}

function at(ev) {
  const c = ev.currentTarget, r = c.getBoundingClientRect();
  return [Math.floor((ev.clientX - r.left) / r.width * c.width), Math.floor((ev.clientY - r.top) / r.height * c.height)];
}

const canvas = document.getElementById('canvas');
canvas.addEventListener('mousemove', ev => {
  if (!cur) return;
  const [x, y] = at(ev);
  if (state.mode === 'compare' && ev.buttons) { state.split = x / canvas.width; draw(); }
  const px = (img, o) => img ? `${img.data[o]} ${img.data[o + 1]} ${img.data[o + 2]}` : '-';
  const o = (y * canvas.width + x) * 4;
  const d = cur.a && cur.b ? maxDiff(cur.a.data, cur.b.data, o) : '-';
  document.getElementById('readout').textContent = `${x},${y}  capture ${px(cur.a, o)}  golden ${px(cur.b, o)}  diff ${d}`;
});
canvas.addEventListener('mousedown', ev => { if (state.mode === 'compare') { state.split = at(ev)[0] / canvas.width; draw(); } });
document.querySelectorAll('#bar button[data-mode]').forEach(e => e.onclick = () => { state.mode = e.dataset.mode; draw(); });
document.getElementById('zoom').onclick = () => { state.zoom = state.zoom === 2 ? 1 : 2; draw(); };
document.getElementById('failingOnly').onchange = e => { state.failingOnly = e.target.checked; list(); };
document.getElementById('sortPsnr').onchange = e => { state.sortPsnr = e.target.checked; list(); };
window.addEventListener('resize', () => draw());
window.addEventListener('keydown', ev => {
  const modes = { '1': 'compare', '2': 'capture', '3': 'golden', '4': 'heat', '5': 'over' };
  if (modes[ev.key]) { state.mode = modes[ev.key]; draw(); }
  else if (ev.key === 'z') { state.zoom = state.zoom === 2 ? 1 : 2; draw(); }
  else if (ev.key === 'f') { const c = document.getElementById('failingOnly'); c.checked = !c.checked; c.onchange({ target: c }); }
  else if (ev.key === 'ArrowDown' || ev.key === 'j' || ev.key === 'ArrowUp' || ev.key === 'k') {
    const o = order(), p = o.indexOf(state.sel), n = (ev.key === 'ArrowDown' || ev.key === 'j') ? p + 1 : p - 1;
    if (n >= 0 && n < o.length) { select(o[n]); ev.preventDefault(); }
  }
});

head(); grid();
const first = order()[0];
select(first ?? 0);
</script>
</body>
</html>
""";
}
