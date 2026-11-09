// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

// Farm office. Renders the host's /farm/api/snapshot. It does not compute
// guidance and it does not send a steer, rate, or section command.

const SCREENS = [
  ['Office', [
    ['overview', 'Summary'],
    ['assistant', 'Assistant'],
    ['costs', 'Costs'],
    ['inventory', 'Inventory'],
    ['maintenance', 'Maintenance'],
    ['weather', 'Work windows'],
  ]],
  ['Fields', [
    ['fields', 'Fields'],
    ['yield', 'Yield'],
    ['soil', 'Soil and zones'],
    ['operations', 'Planting and application'],
    ['rainfall', 'Rainfall'],
    ['scouting', 'Scouting and nitrogen'],
    ['rocks', 'Rocks'],
    ['strips', 'Strip guidance'],
  ]],
  ['Fleet', [
    ['map', 'Fleet map'],
    ['machines', 'Machines and roles'],
    ['telemetry', 'Live and replay'],
    ['trucks', 'Trucks'],
    ['comms', 'Comms'],
  ]],
  ['Grain', [
    ['grain', 'Grain books'],
    ['harvest', 'Harvest schedule'],
    ['marketing', 'Marketing'],
    ['elevators', 'Destinations'],
    ['even', 'Even fill'],
  ]],
  ['Data', [
    ['import', 'Import'],
    ['sync', 'Farm server'],
  ]],
];

const LAYERS = [
  ['fields', 'Fields'],
  ['yield', 'Yield'],
  ['zones', 'Zones'],
  ['soil', 'Soil'],
  ['places', 'Places'],
  ['rocks', 'Rocks'],
  ['scout', 'Scout'],
  ['nitrogen', 'N estimate'],
  ['strips', 'Strips'],
  ['machines', 'Machines'],
  ['trucks', 'Trucks'],
  ['destinations', 'Destinations'],
  ['route', 'Haul line'],
];

const ui = {
  screen: 'overview',
  fieldId: 'north-80',
  machineId: 'combine-pro600',
  destId: 'county',
  channelId: 'harvest',
  from: 'Office',
  talking: false,
  layers: Object.fromEntries(LAYERS.map(([id]) => [id, true])),
  error: '',
};
let snap = null;
const seenPtt = new Set();

function $(id) { return document.getElementById(id); }
function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}
function num(v, d) {
  if (v == null || Number.isNaN(Number(v))) return '—';
  return Number(v).toLocaleString('en-US', { maximumFractionDigits: d == null ? 0 : d, minimumFractionDigits: d == null ? 0 : d });
}
function tonnes(kg) { return num(kg / 1000, 2) + ' t'; }
function field() { return (snap.book.fields || []).find(f => f.id === ui.fieldId) || snap.book.fields[0]; }
function machine() { return (snap.book.machines || []).find(m => m.id === ui.machineId) || snap.book.machines[0]; }

async function pull() {
  try {
    const res = await fetch('/farm/api/snapshot', { cache: 'no-store' });
    snap = await res.json();
    paint();
  } catch (err) {
    ui.error = String(err);
  }
}

async function act(body) {
  ui.error = '';
  const res = await fetch('/farm/api/act', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const data = await res.json();
  if (!data.ok) { ui.error = data.error || 'Refused'; if (snap) paint(); return data; }
  snap = data.view;
  paint();
  return data;
}

function paint() {
  if (!snap) return;
  if (ui.talking) return;
  const active = document.activeElement;
  if (active && ['INPUT', 'TEXTAREA', 'SELECT'].includes(active.tagName)) return;
  $('farm-name').textContent = snap.book.identity.name;
  $('farm-sub').textContent = snap.book.identity.season + ' · ' + snap.importerVersion;
  $('safety').textContent = snap.safetyBanner;
  $('fixture').textContent = snap.fixtureBanner;
  const s = snap.summary;
  $('sync-pill').textContent = s.online + ' online · ' + s.stale + ' stale · ' + s.offline + ' offline';
  renderNav();
  renderPhone();
  const main = $('main');
  main.innerHTML = '';
  if (ui.error) main.appendChild(el('p', 'error', ui.error));
  const fn = RENDER[ui.screen] || RENDER.overview;
  fn(main);
  playNewPtt();
}

function renderNav() {
  const nav = $('nav');
  nav.innerHTML = '';
  for (const [group, items] of SCREENS) {
    nav.appendChild(el('div', 'group', group));
    for (const [id, title] of items) {
      const b = el('button', id === ui.screen ? 'on' : '', title);
      b.addEventListener('click', () => { ui.screen = id; paint(); });
      nav.appendChild(b);
    }
  }
}

function renderPhone() {
  const bar = $('phone');
  bar.innerHTML = '';
  const picks = [['overview', 'Office'], ['map', 'Map'], ['grain', 'Grain'], ['comms', 'Talk'], ['more', 'More']];
  for (const [id, title] of picks) {
    const on = id === 'more' ? ui.screen === 'more' : id === ui.screen;
    const b = el('button', on ? 'on' : '', title);
    b.addEventListener('click', () => { ui.screen = id; paint(); });
    bar.appendChild(b);
  }
}

function h2(parent, title, lead) {
  parent.appendChild(el('h2', '', title));
  if (lead) parent.appendChild(el('p', 'lead', lead));
}

function cards(parent, items) {
  const g = el('div', 'grid');
  for (const item of items) {
    const c = el('div', 'card' + (item.wide ? ' wide' : ''));
    c.appendChild(el('h3', '', item.title));
    if (item.big != null) c.appendChild(el('div', 'big', item.big));
    if (item.sub) c.appendChild(el('div', 'sub', item.sub));
    if (item.node) c.appendChild(item.node);
    g.appendChild(c);
  }
  parent.appendChild(g);
  return g;
}

function table(headers, rows) {
  const t = document.createElement('table');
  const hr = document.createElement('tr');
  for (const h of headers) hr.appendChild(el('th', '', h));
  const thead = document.createElement('thead');
  thead.appendChild(hr);
  t.appendChild(thead);
  const body = document.createElement('tbody');
  for (const row of rows) {
    const tr = document.createElement('tr');
    row.forEach((cell, i) => {
      const td = el('td', i > 0 ? 'num' : '', cell == null ? '' : String(cell));
      if (i === 0) td.className = '';
      tr.appendChild(td);
    });
    body.appendChild(tr);
  }
  t.appendChild(body);
  return t;
}

function tag(text, kind) {
  return el('span', 'tag' + (kind ? ' ' + kind : ''), text);
}

function fieldSelect(parent) {
  const sel = document.createElement('select');
  for (const f of snap.book.fields) {
    const o = el('option', '', f.name + (f.crop ? ' · ' + f.crop : '') + (f.cropYear ? ' · ' + f.cropYear : ''));
    o.value = f.id;
    if (f.id === ui.fieldId) o.selected = true;
    sel.appendChild(o);
  }
  sel.addEventListener('change', () => { ui.fieldId = sel.value; paint(); });
  parent.appendChild(sel);
  return sel;
}

const RENDER = {};

RENDER.more = function (main) {
  h2(main, 'All screens', 'Same data as the side list. The phone bar keeps the five you tap most.');
  const g = el('div', 'grid');
  for (const [group, items] of SCREENS) {
    for (const [id, title] of items) {
      const b = el('button', 'act', group + ' — ' + title);
      b.addEventListener('click', () => { ui.screen = id; paint(); });
      g.appendChild(b);
    }
  }
  main.appendChild(g);
};

RENDER.overview = function (main) {
  h2(main, 'Cross-machine summary', 'One pass over the books. Open a card’s screen for the working table.');
  const s = snap.summary;
  cards(main, [
    { title: 'Land', big: num(s.hectares, 1) + ' ha', sub: s.fields + ' fields in this season' },
    { title: 'Machines', big: String(s.online), sub: s.stale + ' stale, ' + s.offline + ' offline, ' + s.pending + ' records waiting' },
    { title: 'Marketable grain', big: tonnes(s.marketableDryKg), sub: 'Bins minus grain still on a contract. Yield map is ' + tonnes(s.yieldDryKg) + ' dry.' },
    { title: 'Trucks', big: s.trucksComing + ' / ' + s.trucksGoing + ' / ' + s.trucksEnroute, sub: 'Coming, going, en route' },
    { title: 'Downtime on the books', big: num(s.downtimeHours, 1) + ' h', sub: num(s.downtimeUsd, 0) + ' USD logged. Not a purchase.' },
  ]);
  const sync = el('div', 'card');
  sync.appendChild(el('h3', '', 'Who is current'));
  sync.appendChild(table(['Machine', 'State', 'Age (s)', 'Queued'], snap.sync.map(m => [m.name, m.state, num(m.ageSeconds, 0), String(m.pendingRecords)])));
  main.appendChild(sync);
};

RENDER.assistant = function (main) {
  h2(main, 'Assistant', snap.liveLlmNote);
  for (const card of snap.assistant) {
    const c = el('div', 'card');
    c.style.marginBottom = '10px';
    c.appendChild(el('h3', '', card.title));
    c.appendChild(el('p', '', card.recommendation));
    const ul = el('ul', 'evidence');
    for (const line of card.evidence) ul.appendChild(el('li', '', line));
    c.appendChild(ul);
    c.appendChild(el('p', 'sub', 'Uncertainty: ' + card.uncertainty));
    c.appendChild(el('p', 'sub', card.willNot));
    main.appendChild(c);
  }
};

RENDER.costs = function (main) {
  h2(main, 'Cost and downtime', 'Dollars are a ledger. Per-acre is a display conversion from square metres (1 acre = 4046.8564224 m²).');
  const rows = snap.costs.map(c => [c.fieldName, num(c.usd, 0), num(c.usdPerM2, 4) + ' USD/m²', num(c.usdPerHectare, 0) + ' USD/ha', num(c.usdPerAcre, 0) + ' USD/ac']);
  main.appendChild(table(['Field', 'USD', 'per m²', 'per ha', 'per acre'], rows));
  main.appendChild(el('h3', '', 'Downtime'));
  main.appendChild(table(['Machine', 'When', 'Reason', 'Hours', 'USD'],
    snap.book.downtime.map(d => [d.machineId, d.when, d.reason, num(d.hours, 1), num(d.costUsd, 0)])));
};

RENDER.inventory = function (main) {
  h2(main, 'Seed, fertilizer, chemical, fuel', 'Available is on hand minus what a job already claims. A refill line is a suggestion, not an order.');
  main.appendChild(table(['Item', 'Kind', 'On hand', 'Committed', 'Available', 'Planned', 'Unit', 'Refill'],
    snap.inventory.map(i => [i.name, i.kind, num(i.onHand, 0), num(i.committed, 0), num(i.available, 0), num(i.plannedNeed, 0), i.unit, i.refill ? 'yes' : 'no'])));
  for (const i of snap.inventory) if (i.note) main.appendChild(el('p', 'sub', i.name + ': ' + i.note));
};

RENDER.maintenance = function (main) {
  h2(main, 'Maintenance and breakdown watches', 'Hours and the sensor-trend lines are fixtures. They raise a flag. They do not stop a machine.');
  main.appendChild(table(['Machine', 'Item', 'Hours', 'Interval', 'Trend', 'Flag'],
    snap.book.maintenance.map(m => [m.machineId, m.item, num(m.hours, 0), num(m.intervalHours, 0), m.trend, m.severity])));
};

RENDER.weather = function (main) {
  h2(main, 'Spray, plant, and tillage windows', snap.book.weather.source + '. A pass is the limits in the Why column, not a forecast subscription.');
  main.appendChild(table(['Hour', 'Spray', 'Plant', 'Till', 'Why it failed, if it failed'],
    snap.weather.map(w => [w.when, w.spray ? 'open' : 'closed', w.plant ? 'open' : 'closed', w.tillage ? 'open' : 'closed',
      [w.spray ? '' : 'Spray: ' + w.sprayWhy, w.plant ? '' : 'Plant: ' + w.plantWhy, w.tillage ? '' : 'Till: ' + w.tillageWhy].filter(Boolean).join(' ')])));
};

RENDER.fields = function (main) {
  h2(main, 'Fields, crop year, provenance', 'Layers on the other screens use this boundary and this crop year. An import writes a new guidance folder and does not move an existing origin.');
  fieldSelect(main);
  const f = field();
  if (!f) return;
  cards(main, [
    { title: f.name, big: num(f.hectares, 2) + ' ha', sub: (f.cropYear || 'year not set') + ' · ' + (f.crop || 'crop not set') },
    { title: 'Provenance', big: f.provenance, sub: f.guidanceFolder || 'Fixture only — not a guidance field folder' },
    { title: 'Origin', big: num(f.originLonLat[1], 5) + ', ' + num(f.originLonLat[0], 5), sub: 'Latitude, longitude. Not moved by the office.' },
  ]);
  const places = el('div', 'card');
  places.appendChild(el('h3', '', 'Entrances, tile, wet spots, hazards'));
  places.appendChild(el('p', 'sub', 'Visible here and on the fleet map for every operator.'));
  places.appendChild(table(['Kind', 'Name', 'Note'], f.places.map(p => [p.kind, p.name, p.note])));
  main.appendChild(places);
  const form = el('div', 'form');
  const year = document.createElement('input'); year.value = f.cropYear || ''; year.placeholder = '2026';
  const crop = document.createElement('input'); crop.value = f.crop || ''; crop.placeholder = 'corn';
  const save = el('button', 'act primary', 'Save crop year');
  save.addEventListener('click', () => act({ op: 'set-crop', fieldId: f.id, cropYear: year.value, crop: crop.value }));
  form.appendChild(labelWrap('Crop year', year));
  form.appendChild(labelWrap('Crop', crop));
  form.appendChild(save);
  main.appendChild(form);
};

function labelWrap(text, node) {
  const l = el('label', '', text);
  l.appendChild(node);
  return l;
}

RENDER.yield = function (main) {
  h2(main, 'Yield map', 'Fixture cells, kilograms wet and a moisture fraction. The grain screen is where this total is reconciled with scales and bins.');
  fieldSelect(main);
  const f = field();
  if (!f) return;
  const wet = f.yield.reduce((s, c) => s + c.wetKg, 0);
  cards(main, [
    { title: 'Wet mass on the map', big: tonnes(wet), sub: f.yield.length + ' cells · source ' + (f.yield[0] ? f.yield[0].source : 'none') },
    { title: 'Mean rate', big: f.yield.length ? num(wet / f.yield.reduce((s, c) => s + c.areaM2, 0), 3) + ' kg/m²' : '—', sub: 'Canonical mass per area' },
  ]);
  const canvas = document.createElement('canvas');
  canvas.className = 'chart';
  main.appendChild(canvas);
  drawYield(canvas, f);
  main.appendChild(table(['Lat', 'Lon', 'Wet kg', 'Area m²', 'kg/m²', 'Moisture kg/kg'],
    f.yield.map(c => [num(c.lat, 5), num(c.lon, 5), num(c.wetKg, 0), num(c.areaM2, 0), num(c.wetKg / c.areaM2, 3), num(c.moisture, 3)])));
};

function drawYield(canvas, f) {
  const ctx = canvas.getContext('2d');
  const w = canvas.width = canvas.clientWidth * 2, h = canvas.height = 360;
  ctx.fillStyle = '#0e120c'; ctx.fillRect(0, 0, w, h);
  if (!f.yield.length) return;
  const rates = f.yield.map(c => c.wetKg / c.areaM2);
  const lo = Math.min(...rates), hi = Math.max(...rates);
  const cols = 4;
  f.yield.forEach((c, i) => {
    const col = i % cols, row = Math.floor(i / cols);
    const t = hi === lo ? 0.5 : (c.wetKg / c.areaM2 - lo) / (hi - lo);
    ctx.fillStyle = `rgb(${Math.round(80 + 140 * t)}, ${Math.round(90 + 40 * (1 - t))}, 40)`;
    const cw = w / cols, ch = h / Math.ceil(f.yield.length / cols);
    ctx.fillRect(col * cw + 4, row * ch + 4, cw - 8, ch - 8);
  });
}

RENDER.soil = function (main) {
  h2(main, 'Soil tests and zones', 'Points carry the lab unit on each value. Zones are the management polygons those points sit in.');
  fieldSelect(main);
  const f = field();
  if (!f) return;
  main.appendChild(el('h3', '', 'Points'));
  main.appendChild(table(['Sample', 'pH', 'P', 'K', 'Organic matter', 'Lab'],
    f.soil.map(s => [s.id, si(s.ph), si(s.phosphorus), si(s.potassium), si(s.organicMatter), s.lab + ' ' + s.sampled])));
  main.appendChild(el('h3', '', 'Zones'));
  main.appendChild(table(['Zone', 'Management', 'Corners'], f.zones.map(z => [z.name, z.management, String(z.ring.length)])));
};
function si(v) { return v ? num(v.value, 3) + ' ' + v.unit : '—'; }

RENDER.operations = function (main) {
  h2(main, 'As-planted and as-applied', 'Fixture records with an explicit rate unit. The planter’s row file is the Precision Planting extension point, not a proprietary decoder.');
  fieldSelect(main);
  const f = field();
  if (!f) return;
  main.appendChild(table(['When', 'Kind', 'Product', 'Rate', 'Machine', 'Note'],
    f.operations.map(o => [o.when, o.kind, o.product, si(o.rate), o.machineId, o.note])));
  main.appendChild(el('h3', '', 'Precision Planting row fixture'));
  main.appendChild(el('p', 'lead', snap.precisionPlanting.note));
  main.appendChild(el('p', 'sub', 'JSON fields: ' + snap.precisionPlanting.fields.join(', ')));
  main.appendChild(table(['Row', 'Spacing m', 'Downforce N', 'Singulation'],
    snap.precisionPlanting.rows.map(r => [String(r.row), num(r.spacingM, 3), num(r.downforceN, 0), num(r.singulation, 3)])));
};

RENDER.rainfall = function (main) {
  h2(main, 'Field rainfall', 'Gauge and radar are different sources. A season total adds them in their own columns and does not average them into one truth.');
  fieldSelect(main);
  const f = field();
  if (!f) return;
  cards(main, [
    { title: 'Season, gauge', big: num(f.rain.seasonGaugeM * 1000, 0) + ' mm', sub: num(f.rain.seasonGaugeM, 3) + ' m of water' },
    { title: 'Season, radar estimate', big: num(f.rain.seasonRadarM * 1000, 0) + ' mm', sub: 'Not the gauge' },
  ]);
  main.appendChild(el('h3', '', 'Storms'));
  main.appendChild(table(['When', 'Gauge m', 'Radar m', 'Gauge source', 'Radar source'],
    f.rain.storms.map(s => [s.when, num(s.gaugeM, 3), num(s.radarM, 3), s.gaugeSource, s.radarSource])));
  main.appendChild(el('h3', '', 'Days'));
  main.appendChild(table(['Day', 'Gauge m', 'Radar m'], f.rain.days.map(d => [d.day, num(d.gaugeM, 3), num(d.radarM, 3)])));
};

RENDER.scouting = function (main) {
  h2(main, 'Drone scouting and nitrogen estimate',
    'Photos are geotagged captions. There is no image file. The nitrogen grid is an estimate from a fixture index, not a tissue measurement. ' +
    'Separating nitrogen stress from drought or disease is still an unsolved calibration problem — the reference strip and the tissue test are empty placeholders.');
  fieldSelect(main);
  const f = field();
  if (!f) return;
  main.appendChild(table(['Flag', 'When', 'Caption'], f.scouting.map(s => [s.flag, s.when, s.caption + ' (' + s.source + ')'])));
  const canvas = document.createElement('canvas');
  canvas.className = 'chart';
  main.appendChild(canvas);
  drawNitrogen(canvas, f);
  main.appendChild(el('p', 'sub', 'Calibration placeholders: tissue-test sample — none. Reference strip — none. Do not treat a cell as pounds of N.'));
};

function drawNitrogen(canvas, f) {
  const ctx = canvas.getContext('2d');
  const w = canvas.width = canvas.clientWidth * 2, h = canvas.height = 360;
  ctx.fillStyle = '#0e120c'; ctx.fillRect(0, 0, w, h);
  const cols = 4;
  f.nitrogen.forEach((c, i) => {
    const col = i % cols, row = Math.floor(i / cols);
    const t = c.sufficiencyEstimate;
    ctx.fillStyle = `rgb(${Math.round(180 - 120 * t)}, ${Math.round(70 + 140 * t)}, 50)`;
    const cw = w / cols, ch = h / Math.ceil(f.nitrogen.length / cols);
    ctx.fillRect(col * cw + 4, row * ch + 4, cw - 8, ch - 8);
    ctx.fillStyle = '#111';
    ctx.font = '28px sans-serif';
    ctx.fillText(num(t, 2), col * cw + 16, row * ch + 48);
  });
}

RENDER.rocks = function (main) {
  h2(main, 'Rock map', 'GPS tags from a cultivator survey fixture, plus a nearest-neighbour side-by-side pickup order. GPR is a data placeholder. No hardware is attached and none is recommended.');
  fieldSelect(main);
  const f = field();
  if (!f) return;
  const route = (snap.rockRoutes || []).find(r => r.fieldId === f.id);
  main.appendChild(el('p', 'lead', route ? route.note + ' Length ' + num(route.metres, 0) + ' m.' : 'No rocks.'));
  main.appendChild(table(['Order', 'Id', 'Note', 'Source'],
    (route ? route.order : []).map((id, i) => {
      const rock = f.rocks.find(r => r.id === id);
      return [String(i + 1), id, rock ? rock.note : '', rock ? rock.source : ''];
    })));
  const g = el('div', 'card');
  g.appendChild(el('h3', '', 'GPR on the cultivator'));
  g.appendChild(el('p', '', f.gpr.note));
  g.appendChild(el('p', 'sub', 'Sensor id ' + f.gpr.sensor + ' · status ' + f.gpr.status + ' · samples ' + f.gpr.samples.length));
  main.appendChild(g);
  const go = el('button', 'act', 'Show the pickup on the map');
  go.addEventListener('click', () => { ui.screen = 'map'; ui.layers.rocks = true; paint(); });
  main.appendChild(go);
};

RENDER.strips = function (main) {
  h2(main, 'Strip-till lines for next year', '');
  fieldSelect(main);
  const f = field();
  if (!f) return;
  const banner = el('div', 'safety', f.strips.banner);
  main.appendChild(banner);
  main.appendChild(el('p', 'lead', f.strips.rtkNote));
  main.appendChild(table(['Line', 'Heading rad', 'Surveyed', 'Base'],
    f.strips.lines.map(l => [l.name, num(l.headingRadians, 3), l.surveyedUtc, l.baseId])));
  main.appendChild(el('p', 'sub', 'Headings are radians, 0 = north, clockwise, the same sense as field.geojson. Nothing here offsets a GPS receiver.'));
};

RENDER.map = function (main) {
  h2(main, 'Fleet map', 'Top-down GPS. Tap a machine. Destinations, haul lines, rocks, hazards, and the nitrogen estimate are layers. Routes are straight-line estimates.');
  const layers = el('div', 'layers');
  for (const [id, title] of LAYERS) {
    const b = el('button', ui.layers[id] ? 'on' : '', title);
    b.addEventListener('click', () => { ui.layers[id] = !ui.layers[id]; paint(); });
    layers.appendChild(b);
  }
  main.appendChild(layers);
  const row = el('div', 'form');
  const fs = document.createElement('select');
  for (const f of snap.book.fields) {
    const o = el('option', '', f.name); o.value = f.id; if (f.id === ui.fieldId) o.selected = true; fs.appendChild(o);
  }
  fs.addEventListener('change', () => { ui.fieldId = fs.value; paint(); });
  const ds = document.createElement('select');
  for (const d of snap.book.destinations) {
    const o = el('option', '', d.name); o.value = d.id; if (d.id === ui.destId) o.selected = true; ds.appendChild(o);
  }
  ds.addEventListener('change', () => { ui.destId = ds.value; paint(); });
  row.appendChild(labelWrap('Field for the haul line', fs));
  row.appendChild(labelWrap('Destination pin', ds));
  main.appendChild(row);
  const canvas = document.createElement('canvas');
  canvas.className = 'map';
  main.appendChild(canvas);
  const hits = drawMap(canvas);
  canvas.addEventListener('click', ev => {
    const rect = canvas.getBoundingClientRect();
    const x = (ev.clientX - rect.left) * (canvas.width / rect.width);
    const y = (ev.clientY - rect.top) * (canvas.height / rect.height);
    let best = null, bestD = 36 * (canvas.width / rect.width);
    for (const hit of hits) {
      const d = Math.hypot(hit.x - x, hit.y - y);
      if (d < bestD) { best = hit; bestD = d; }
    }
    if (best) { ui.machineId = best.id; ui.screen = 'machines'; paint(); }
  });
  const m = machine();
  if (m) {
    const cam = el('div', 'card');
    cam.appendChild(el('h3', '', m.name + ' — camera'));
    cam.appendChild(el('p', 'sub', m.cameraNote));
    const c2 = document.createElement('canvas');
    c2.className = 'cam';
    cam.appendChild(c2);
    drawCamera(c2, m);
    main.appendChild(cam);
  }
};

function bounds() {
  const pts = [];
  const push = (lon, lat) => { if (lon != null && lat != null) pts.push([lon, lat]); };
  for (const f of snap.book.fields) for (const p of f.boundary) push(p[0], p[1]);
  for (const d of snap.book.destinations) push(d.lon, d.lat);
  for (const m of snap.book.machines) push(m.lon, m.lat);
  for (const t of snap.book.trucks) push(t.lon, t.lat);
  let west = Math.min(...pts.map(p => p[0])), east = Math.max(...pts.map(p => p[0]));
  let south = Math.min(...pts.map(p => p[1])), north = Math.max(...pts.map(p => p[1]));
  const padX = (east - west) * 0.08 || 0.01, padY = (north - south) * 0.08 || 0.01;
  return { west: west - padX, east: east + padX, south: south - padY, north: north + padY };
}

function drawMap(canvas) {
  const ctx = canvas.getContext('2d');
  const dpr = 2;
  const cssW = canvas.clientWidth || 800;
  const cssH = canvas.clientHeight || 480;
  const w = canvas.width = cssW * dpr;
  const h = canvas.height = cssH * dpr;
  const b = bounds();
  const proj = (lon, lat) => [
    (lon - b.west) / (b.east - b.west) * w,
    (b.north - lat) / (b.north - b.south) * h,
  ];
  ctx.fillStyle = '#10150e';
  ctx.fillRect(0, 0, w, h);
  const hits = [];
  if (ui.layers.fields) {
    for (const f of snap.book.fields) {
      ctx.beginPath();
      f.boundary.forEach((p, i) => {
        const [x, y] = proj(p[0], p[1]);
        if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
      });
      ctx.closePath();
      ctx.fillStyle = f.id === ui.fieldId ? 'rgba(158, 196, 106, 0.18)' : 'rgba(158, 196, 106, 0.08)';
      ctx.strokeStyle = '#9bc46a';
      ctx.lineWidth = 2;
      ctx.fill(); ctx.stroke();
    }
  }
  const f = field();
  if (f && ui.layers.zones) {
    ctx.strokeStyle = '#e2b45c';
    for (const z of f.zones) {
      ctx.beginPath();
      z.ring.forEach((p, i) => { const [x, y] = proj(p[0], p[1]); if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y); });
      ctx.closePath(); ctx.stroke();
    }
  }
  if (f && ui.layers.yield) {
    for (const c of f.yield) {
      const [x, y] = proj(c.lon, c.lat);
      ctx.fillStyle = 'rgba(224, 161, 90, 0.85)';
      ctx.fillRect(x - 6, y - 6, 12, 12);
    }
  }
  if (f && ui.layers.nitrogen) {
    for (const c of f.nitrogen) {
      const [x, y] = proj(c.lon, c.lat);
      ctx.fillStyle = `rgba(${Math.round(180 - 120 * c.sufficiencyEstimate)}, ${Math.round(70 + 140 * c.sufficiencyEstimate)}, 50, 0.9)`;
      ctx.beginPath(); ctx.arc(x + 10, y, 5, 0, Math.PI * 2); ctx.fill();
    }
  }
  if (f && ui.layers.soil) {
    ctx.fillStyle = '#8eb8cc';
    for (const s of f.soil) { const [x, y] = proj(s.lon, s.lat); ctx.fillRect(x - 4, y - 4, 8, 8); }
  }
  if (f && ui.layers.places) {
    for (const p of f.places) {
      const [x, y] = proj(p.lon, p.lat);
      ctx.fillStyle = p.kind === 'hazard' ? '#e07a62' : p.kind === 'wet' ? '#8eb8cc' : '#e2b45c';
      ctx.beginPath(); ctx.arc(x, y, 6, 0, Math.PI * 2); ctx.fill();
    }
  }
  if (f && ui.layers.rocks) {
    const route = (snap.rockRoutes || []).find(r => r.fieldId === f.id);
    ctx.strokeStyle = '#f3efe4'; ctx.setLineDash([6, 6]); ctx.beginPath();
    const entrance = f.places.find(p => p.kind === 'entrance');
    if (entrance && route) {
      let [x, y] = proj(entrance.lon, entrance.lat); ctx.moveTo(x, y);
      for (const id of route.order) {
        const rock = f.rocks.find(r => r.id === id);
        if (!rock) continue;
        [x, y] = proj(rock.lon, rock.lat); ctx.lineTo(x, y);
      }
      ctx.stroke();
    }
    ctx.setLineDash([]);
    ctx.fillStyle = '#f3efe4';
    for (const rock of f.rocks) { const [x, y] = proj(rock.lon, rock.lat); ctx.fillRect(x - 3, y - 3, 6, 6); }
  }
  if (f && ui.layers.scout) {
    for (const s of f.scouting) {
      const [x, y] = proj(s.lon, s.lat);
      ctx.strokeStyle = '#e07a62'; ctx.strokeRect(x - 7, y - 7, 14, 14);
    }
  }
  if (f && ui.layers.strips) {
    ctx.strokeStyle = '#e2b45c'; ctx.lineWidth = 2;
    for (const line of f.strips.lines) {
      const [x1, y1] = proj(line.lonA, line.latA);
      const [x2, y2] = proj(line.lonB, line.latB);
      ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke();
    }
  }
  if (ui.layers.destinations) {
    for (const d of snap.book.destinations) {
      const [x, y] = proj(d.lon, d.lat);
      ctx.fillStyle = d.id === ui.destId ? '#e2b45c' : '#8eb8cc';
      ctx.beginPath(); ctx.moveTo(x, y - 12); ctx.lineTo(x + 8, y + 8); ctx.lineTo(x - 8, y + 8); ctx.fill();
    }
  }
  if (ui.layers.route && f) {
    const dest = snap.book.destinations.find(d => d.id === ui.destId);
    if (dest && f.boundary.length) {
      const lon = f.boundary.reduce((s, p) => s + p[0], 0) / f.boundary.length;
      const lat = f.boundary.reduce((s, p) => s + p[1], 0) / f.boundary.length;
      const [x1, y1] = proj(lon, lat), [x2, y2] = proj(dest.lon, dest.lat);
      ctx.strokeStyle = '#f3efe4'; ctx.setLineDash([2, 8]); ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke(); ctx.setLineDash([]);
    }
  }
  if (ui.layers.trucks) {
    ctx.fillStyle = '#e2b45c';
    for (const t of snap.book.trucks) {
      const [x, y] = proj(t.lon, t.lat);
      ctx.fillRect(x - 7, y - 4, 14, 8);
    }
  }
  if (ui.layers.machines) {
    for (const m of snap.book.machines) {
      const [x, y] = proj(m.lon, m.lat);
      hits.push({ id: m.id, x, y });
      ctx.save();
      ctx.translate(x, y);
      ctx.rotate(m.headingRadians);
      ctx.fillStyle = m.id === ui.machineId ? '#f3efe4' : '#9bc46a';
      ctx.beginPath(); ctx.moveTo(0, -12); ctx.lineTo(8, 10); ctx.lineTo(-8, 10); ctx.fill();
      ctx.restore();
      ctx.fillStyle = '#f3efe4'; ctx.font = '22px sans-serif'; ctx.fillText(m.name, x + 12, y - 8);
    }
  }
  return hits;
}

function drawCamera(canvas, m) {
  const ctx = canvas.getContext('2d');
  const w = canvas.width = canvas.clientWidth * 2, h = canvas.height = 320;
  const t = Date.now() / 400;
  for (let y = 0; y < h; y += 16) {
    ctx.fillStyle = (Math.floor(y / 16 + t + m.name.length) % 2) ? '#24301a' : '#1a2214';
    ctx.fillRect(0, y, w, 16);
  }
  ctx.fillStyle = '#f3efe4';
  ctx.font = '28px sans-serif';
  ctx.fillText(m.camera + ' · ' + m.name, 24, 60);
  ctx.font = '22px sans-serif';
  ctx.fillText('Fixture frame. Not a camera.', 24, 100);
}

RENDER.machines = function (main) {
  h2(main, 'Machines and what each role may do',
    'Every command flag in this office is off. Full guidance means the existing AgOpen host, which this page does not arm. Logging cannot be switched into control.');
  const sel = document.createElement('select');
  for (const m of snap.book.machines) {
    const o = el('option', '', m.name + ' — ' + m.display); o.value = m.id; if (m.id === ui.machineId) o.selected = true; sel.appendChild(o);
  }
  sel.addEventListener('change', () => { ui.machineId = sel.value; paint(); });
  main.appendChild(sel);
  const m = machine();
  const role = (snap.roles || []).find(r => r.role === m.role) || {};
  cards(main, [
    { title: m.name, big: m.role, sub: m.display },
    { title: 'Software', big: 'unnamed', sub: m.software },
    { title: 'Operator', big: m.operator, sub: 'Fuel ' + num(m.fuelFraction * 100, 0) + '% · ' + num(m.engineHours, 0) + ' h' },
    { title: 'Command path', wide: true, sub: role.commandPath || '' },
  ]);
  const flags = ['mayReadGps', 'mayReadPgn250', 'mayReadPgn253', 'mayReadPgn211', 'mayForwardLogs', 'mayRecordGuidanceLines', 'mayVisualizeGuidance', 'mayCommandSteer', 'mayCommandRate', 'mayCommandSections', 'maySendPgn254', 'maySendPgn239', 'maySendPgn229', 'maySendPgn241'];
  main.appendChild(table(['Permission', 'Allowed'], flags.map(k => [k, role[k] ? 'yes' : 'no'])));
  if (m.id === 'planter') {
    main.appendChild(el('p', 'lead', snap.precisionPlanting.note));
  }
  const cam = document.createElement('canvas');
  cam.className = 'cam';
  main.appendChild(cam);
  drawCamera(cam, m);
};

RENDER.telemetry = function (main) {
  h2(main, 'Live log and fixture replay',
    'The host sample is a read of GPS and of PGNs 250, 253, and 211 already parsed by guidance. It is taken when this page asks, not inside the 100 Hz loop. Fixture machines move on a scripted path.');
  const rows = (snap.book.telemetry || []).slice(-12).reverse();
  main.appendChild(table(['UTC', 'Machine', 'Source', 'Lat', 'Lon', 'PGN 250 %', 'PGN 253 °', 'PGN 211 roll'],
    rows.map(r => [r.utc, r.machineId, r.source, num(r.latitude, 5), num(r.longitude, 5), num(r.pgn250SensorPercent, 1), num(r.pgn253SteerAngleDeg, 2), num(r.pgn211RollDegrees, 2)])));
  main.appendChild(el('p', 'sub', rows.length ? rows[0].note : 'No sample yet. Open this page on a host that has a GPS fix, or use the guidance screen’s Farm summary, which posts the status frame once a second.'));
  main.appendChild(el('h3', '', 'Refusals'));
  const refusals = snap.book.refusals || [];
  main.appendChild(refusals.length
    ? table(['UTC', 'What'], refusals.slice(-8).reverse().map(r => [r.utc, r.what]))
    : el('p', 'sub', 'None. A command PGN posted to this office shows up here and is not forwarded.'));
};

RENDER.trucks = function (main) {
  h2(main, 'Trucks', 'Coming, going, and en route. Positions are fixtures. Turnaround on the assistant uses the same speed.');
  main.appendChild(table(['Truck', 'State', 'From', 'To', 'Load dry kg', 'Progress', 'Note'],
    snap.book.trucks.map(t => [t.name, t.state, t.from, t.to, num(t.loadDryKg, 0), num(t.progress, 2), t.note])));
};

RENDER.comms = function (main) {
  h2(main, 'Cabs, office, phone', snap.book.comms.note);
  const channels = el('div', 'layers');
  for (const c of snap.book.comms.channels) {
    const b = el('button', c.id === ui.channelId ? 'on' : '', c.name + (c.kind === 'direct' ? ' (direct)' : ''));
    b.addEventListener('click', () => { ui.channelId = c.id; paint(); });
    channels.appendChild(b);
  }
  main.appendChild(channels);
  const posts = el('div', 'posts');
  for (const p of snap.book.comms.posts.filter(p => p.channelId === ui.channelId)) {
    const box = el('div', 'post');
    box.appendChild(el('div', 'who', p.from + ' · ' + p.kind + ' · ' + p.utc));
    box.appendChild(el('div', '', p.text));
    if (p.audioBase64) {
      const audio = document.createElement('audio');
      audio.controls = true;
      audio.src = 'data:' + (p.audioType || 'audio/webm') + ';base64,' + p.audioBase64;
      box.appendChild(audio);
    }
    posts.appendChild(box);
  }
  main.appendChild(posts);
  const form = el('div', 'form');
  const from = document.createElement('input'); from.value = ui.from;
  from.addEventListener('change', () => { ui.from = from.value; });
  const text = document.createElement('input'); text.placeholder = 'Message';
  const send = el('button', 'act primary', 'Send');
  send.addEventListener('click', () => act({ op: 'send-message', channelId: ui.channelId, from: from.value || 'Office', text: text.value }));
  form.appendChild(labelWrap('From', from));
  form.appendChild(labelWrap('Text', text));
  form.appendChild(send);
  main.appendChild(form);
  const hold = el('button', 'act hold', 'Hold to talk');
  hold.addEventListener('pointerdown', ev => { ev.preventDefault(); beginPtt(hold); });
  hold.addEventListener('pointerup', () => endPtt(hold));
  hold.addEventListener('pointerleave', () => endPtt(hold));
  main.appendChild(hold);
  main.appendChild(el('p', 'sub', 'Push-to-talk uses the microphone when the browser allows it, and a local tone plus a talk marker when it does not. It stays on this LAN. Radios stay primary.'));
};

let pttRec = null;
let pttChunks = [];
let pttStarted = 0;
function beginPtt(button) {
  ui.talking = true;
  button.classList.add('on');
  button.textContent = 'Talking…';
  pttStarted = Date.now();
  pttChunks = [];
  beep(880, 0.08);
  if (!navigator.mediaDevices || !window.MediaRecorder) return;
  navigator.mediaDevices.getUserMedia({ audio: true }).then(stream => {
    if (!ui.talking) { stream.getTracks().forEach(t => t.stop()); return; }
    pttRec = new MediaRecorder(stream);
    pttRec.ondataavailable = e => { if (e.data.size) pttChunks.push(e.data); };
    pttRec.start();
  }).catch(() => { pttRec = null; });
}
function endPtt(button) {
  if (!ui.talking) return;
  ui.talking = false;
  button.classList.remove('on');
  button.textContent = 'Hold to talk';
  const ms = Date.now() - pttStarted;
  const finish = audio => act({
    op: 'ptt', channelId: ui.channelId, from: ui.from || 'Office',
    text: 'Push-to-talk ' + Math.round(ms / 100) / 10 + ' s',
    audioBase64: audio || '', audioType: audio ? 'audio/webm' : '',
  });
  if (pttRec && pttRec.state !== 'inactive') {
    pttRec.onstop = () => {
      pttRec.stream.getTracks().forEach(t => t.stop());
      const blob = new Blob(pttChunks, { type: pttRec.mimeType || 'audio/webm' });
      const reader = new FileReader();
      reader.onload = () => {
        const data = String(reader.result || '');
        const comma = data.indexOf(',');
        finish(comma >= 0 ? data.slice(comma + 1) : '');
      };
      reader.readAsDataURL(blob);
    };
    pttRec.stop();
    pttRec = null;
  } else finish('');
}
function beep(freq, seconds) {
  const Ctx = window.AudioContext || window.webkitAudioContext;
  if (!Ctx) return;
  const ctx = new Ctx();
  const o = ctx.createOscillator();
  const g = ctx.createGain();
  o.frequency.value = freq; o.connect(g); g.connect(ctx.destination); g.gain.value = 0.05;
  o.start(); o.stop(ctx.currentTime + seconds);
}
function playNewPtt() {
  for (const p of snap.book.comms.posts) {
    if (p.kind !== 'ptt' || seenPtt.has(p.id)) continue;
    seenPtt.add(p.id);
    if (p.audioBase64) {
      const audio = new Audio('data:' + (p.audioType || 'audio/webm') + ';base64,' + p.audioBase64);
      audio.play().catch(() => {});
    }
  }
}

RENDER.grain = function (main) {
  h2(main, 'Yield, cart, bin', snap.book.grain.note);
  const g = snap.grain;
  cards(main, [
    { title: 'Yield map, adjusted dry', big: tonnes(g.yieldMapAdjustedDryKg), sub: 'Wet ' + tonnes(g.yieldMapWetKg) + ' at ' + num(g.yieldMapMoisture, 3) + ' kg/kg, calibration × ' + num(g.yieldCalibration, 3) },
    { title: 'Carts, dry', big: tonnes(g.cartScaleDryKg), sub: 'Wet ' + tonnes(g.cartScaleWetKg) + ' across the tickets' },
    { title: 'Still in the tank', big: tonnes(g.combineTankDryKg), sub: 'Accounted off the combine ' + tonnes(g.accountedOffCombineDryKg) },
    { title: 'Bins', big: tonnes(g.binDryKg), sub: 'Free ' + tonnes(g.binFreeKg) + ' of ' + tonnes(g.binCapacityKg) + '. Not the yield map.' },
    { title: 'Gap, yield minus accounted', big: tonnes(g.yieldMinusAccountedKg), sub: g.residualNote },
    { title: 'Marketable', big: tonnes(g.marketableDryKg), sub: 'Commitment left ' + tonnes(g.remainingCommitmentDryKg) },
  ]);
  const steps = el('ol', 'evidence');
  for (const step of g.steps) steps.appendChild(el('li', '', step));
  main.appendChild(steps);
  main.appendChild(table(['Bin', 'Crop', 'Dry kg', 'Capacity kg', 'Moisture'],
    snap.book.grain.bins.map(b => [b.name, b.crop, num(b.dryKg, 0), num(b.capacityKg, 0), num(b.moisture, 3)])));
};

RENDER.harvest = function (main) {
  h2(main, 'Harvest schedule', 'Drier fields first, wet fields over 0.22 kg/kg held, and the schedule stops when bin space runs out. Dryer rate is 12000 kg/h dry, a fixture.');
  main.appendChild(table(['Order', 'Field', 'Moisture', 'Unharvested dry kg', 'Dryer hours', 'Decision'],
    snap.harvest.map(h => [String(h.order), h.fieldName, num(h.moisture, 3), num(h.unharvestedDryKg, 0), num(h.dryerHours, 1), h.decision])));
};

RENDER.marketing = function (main) {
  h2(main, 'Off-season marketing', snap.book.market.source + ' Analysis only. Committed grain is excluded. Nothing is sold.');
  const canvas = document.createElement('canvas');
  canvas.className = 'chart';
  main.appendChild(canvas);
  drawMarket(canvas);
  main.appendChild(table(['Scenario', 'kg', 'USD', 'Price USD/kg', 'Note'],
    snap.marketScenarios.map(s => [s.name, num(s.kg, 0), num(s.usd, 0), num(s.priceUsdPerKg, 4), s.note])));
};

function drawMarket(canvas) {
  const ctx = canvas.getContext('2d');
  const w = canvas.width = canvas.clientWidth * 2, h = canvas.height = 360;
  ctx.fillStyle = '#0e120c'; ctx.fillRect(0, 0, w, h);
  const pts = snap.book.market.points;
  if (pts.length < 2) return;
  const lo = Math.min(...pts.map(p => p.usdPerKg)), hi = Math.max(...pts.map(p => p.usdPerKg));
  ctx.strokeStyle = '#e2b45c'; ctx.lineWidth = 3; ctx.beginPath();
  pts.forEach((p, i) => {
    const x = 40 + (w - 80) * i / (pts.length - 1);
    const y = 30 + (h - 60) * (1 - (p.usdPerKg - lo) / ((hi - lo) || 1));
    if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y);
  });
  ctx.stroke();
  ctx.fillStyle = '#c3b9a4'; ctx.font = '22px sans-serif';
  ctx.fillText(pts[0].day + '  ' + num(lo, 4) + '–' + num(hi, 4) + ' USD/kg', 40, h - 16);
}

RENDER.elevators = function (main) {
  h2(main, 'Elevators and other haul destinations', 'Bids, hours, and drying are fixtures. Net is bid minus drying minus a straight-line haul. Pick one to pin it on the map.');
  main.appendChild(table(['Name', 'Hours', 'Crops', 'Bid USD/kg', 'Dry USD / point', 'Source'],
    snap.book.destinations.map(d => [d.name, d.hours, d.crops.join(', '), num(d.bidUsdPerKg, 4), num(d.dryingUsdPerMoisturePoint, 4), d.bidSource])));
  const f = field();
  const rows = snap.hauls.filter(h => !f || h.fieldId === f.id);
  main.appendChild(el('h3', '', 'From ' + (f ? f.name : 'the field')));
  main.appendChild(table(['Destination', 'Metres', 'Minutes', 'Drying USD/kg', 'Haul USD/kg', 'Net USD/kg'],
    rows.map(h => {
      const d = snap.book.destinations.find(x => x.id === h.destinationId);
      return [d ? d.name : h.destinationId, num(h.metres, 0), num(h.seconds / 60, 0), num(h.dryingUsdPerKg, 4), num(h.haulUsdPerKg, 4), num(h.netUsdPerKg, 4)];
    })));
  const buttons = el('div', 'rowbtns');
  for (const d of snap.book.destinations) {
    const b = el('button', d.id === ui.destId ? 'on' : '', 'Pin ' + d.name);
    b.addEventListener('click', () => { ui.destId = d.id; ui.screen = 'map'; ui.layers.destinations = true; ui.layers.route = true; paint(); });
    buttons.appendChild(b);
  }
  main.appendChild(buttons);
  main.appendChild(el('p', 'sub', 'Minutes use 15 m/s and ignore roads.'));
};

RENDER.even = function (main) {
  h2(main, 'Grain-cart even fill', '');
  const plan = snap.book.evenFill;
  main.appendChild(el('div', 'safety', plan.banner));
  cards(main, [
    { title: 'Combine tank', big: num(plan.combineTankFraction * 100, 0) + '%', sub: 'Fixture feedback, replaying' },
    { title: 'Cart left', big: num(plan.cartLeft * 100, 0) + '%', sub: 'Side of the cart' },
    { title: 'Cart right', big: num(plan.cartRight * 100, 0) + '%', sub: 'Side of the cart' },
  ]);
  const canvas = document.createElement('canvas');
  canvas.className = 'chart';
  main.appendChild(canvas);
  drawFill(canvas, plan);
  main.appendChild(el('p', '', plan.suggestion));
};

function drawFill(canvas, plan) {
  const ctx = canvas.getContext('2d');
  const w = canvas.width = canvas.clientWidth * 2, h = canvas.height = 360;
  ctx.fillStyle = '#0e120c'; ctx.fillRect(0, 0, w, h);
  const bars = [['Tank', plan.combineTankFraction], ['Left', plan.cartLeft], ['Right', plan.cartRight]];
  bars.forEach((b, i) => {
    const x = 80 + i * (w / 3);
    ctx.fillStyle = '#24301a'; ctx.fillRect(x, 40, 80, h - 100);
    ctx.fillStyle = '#e2b45c'; ctx.fillRect(x, 40 + (h - 100) * (1 - b[1]), 80, (h - 100) * b[1]);
    ctx.fillStyle = '#f3efe4'; ctx.font = '24px sans-serif'; ctx.fillText(b[0], x, h - 30);
  });
}

RENDER.import = function (main) {
  h2(main, 'Geometry import',
    'ISOXML TASKDATA.XML and KML/KMZ go through the existing parsers into a new field folder with import-provenance.json. The source bytes stay put. AgOpenGPS folders are refused so the delete-on-open path is never used.');
  const row = el('div', 'rowbtns');
  const iso = el('button', 'act primary', 'Import bundled TASKDATA sample');
  iso.addEventListener('click', () => act({ op: 'import-sample', kind: 'isoxml', label: 'bundled sample from the office' }));
  const kml = el('button', 'act', 'Import bundled KML sample');
  kml.addEventListener('click', () => act({ op: 'import-sample', kind: 'kml', label: 'bundled KML from the office' }));
  row.appendChild(iso); row.appendChild(kml);
  main.appendChild(row);
  const file = document.createElement('input');
  file.type = 'file';
  file.accept = '.xml,.kml,.kmz';
  const label = document.createElement('input');
  label.placeholder = 'Where this file came from';
  const go = el('button', 'act primary', 'Import chosen file');
  go.addEventListener('click', () => {
    const chosen = file.files && file.files[0];
    if (!chosen) { ui.error = 'Choose a file first.'; paint(); return; }
    const reader = new FileReader();
    reader.onload = () => {
      const data = String(reader.result || '');
      const comma = data.indexOf(',');
      act({ op: 'import-upload', fileName: chosen.name, label: label.value || chosen.name, base64: data.slice(comma + 1) });
    };
    reader.readAsDataURL(chosen);
  });
  const form = el('div', 'form');
  form.appendChild(labelWrap('File', file));
  form.appendChild(labelWrap('Source label', label));
  form.appendChild(go);
  main.appendChild(form);
  main.appendChild(el('p', 'sub', 'Round-trip budget against this app’s own ISOXML export is ' + snap.localPlaneToleranceM + ' m on the original LocalPlane. ISOXML here is not a claim of Case IH, Trimble, John Deere, or Precision Planting support.'));
  const reset = el('button', 'act', 'Reset the sample book');
  reset.addEventListener('click', () => act({ op: 'reset-fixture' }));
  main.appendChild(reset);
  main.appendChild(el('p', 'sub', 'Reset restores the sample book. Field folders already written under Fields stay on disk.'));
};

RENDER.sync = function (main) {
  h2(main, 'Local farm server',
    'This host is the server. A machine set offline keeps its queue and the rest of the office still runs. Sync clears the queue only when the link is online. Stale means the link is up and the last sample is older than 45 seconds.');
  main.appendChild(table(['Machine', 'Link', 'State', 'Queued', 'Last sync'],
    snap.sync.map(m => [m.name, m.link, m.state, String(m.pendingRecords), m.lastSyncUtc || '—'])));
  const m = machine();
  const row = el('div', 'rowbtns');
  const off = el('button', 'act', 'Mark ' + m.name + ' offline');
  off.addEventListener('click', () => act({ op: 'set-link', machineId: m.id, link: 'offline' }));
  const on = el('button', 'act', 'Mark ' + m.name + ' online');
  on.addEventListener('click', () => act({ op: 'set-link', machineId: m.id, link: 'online' }));
  const queue = el('button', 'act', 'Queue a record');
  queue.addEventListener('click', () => act({ op: 'queue-record', machineId: m.id }));
  const sync = el('button', 'act primary', 'Sync now');
  sync.addEventListener('click', () => act({ op: 'sync', machineId: m.id }));
  row.appendChild(off); row.appendChild(on); row.appendChild(queue); row.appendChild(sync);
  main.appendChild(row);
  main.appendChild(el('p', 'sub', 'Pick the machine on Machines first. The east FM-750 starts stale on purpose. The FmX starts offline.'));
};

pull();
setInterval(pull, 1500);
