'use strict';

/* ═════════════ Alapok ═════════════ */
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const hasHost = !!(window.chrome && window.chrome.webview);

function send(cmd, data = {}) {
  if (hasHost) window.chrome.webview.postMessage({ cmd, ...data });
  else demoHandle(cmd, data);
}

let S = null;          // beállítások (a C# oldaltól)
let st = null;         // élő állapot (másodpercenként)
let selProfileId = null;
let saveTimer = null;
let seeking = false;

function save() {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(saveNow, 300);
}
function saveNow() {
  clearTimeout(saveTimer);
  send('saveSettings', { settings: S });
}

const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const base = p => String(p || '').split(/[\\/]/).pop();
const noExt = p => base(p).replace(/\.[^.]+$/, '');
const dir = p => String(p || '').split(/[\\/]/).slice(0, -1).join('\\');
const pad = n => String(n).padStart(2, '0');
const fmt = s => { s = Math.max(0, Math.floor(s || 0)); return Math.floor(s / 60) + ':' + pad(s % 60); };
const fmtCount = sec => {
  sec = Math.max(0, Math.floor(sec));
  const h = Math.floor(sec / 3600), m = Math.floor(sec % 3600 / 60), s = sec % 60;
  return h ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`;
};
const toMin = t => { const [h, m] = t.split(':').map(Number); return h * 60 + m; };
const fromMin = m => { m = Math.max(0, Math.min(23 * 60 + 59, m)); return pad(Math.floor(m / 60)) + ':' + pad(m % 60); };
const uid = () => Math.random().toString(36).slice(2, 10);
const typeName = { be: 'Becsengő', ki: 'Kicsengő', egyeb: 'Egyéb', utemezett: 'Ütemezett' };

const ICON = {
  play: '<svg viewBox="0 0 24 24"><path d="M7 5v14l12-7L7 5Z"/></svg>',
  pause: '<svg viewBox="0 0 24 24"><path d="M8 5v14M16 5v14"/></svg>',
  trash: '<svg viewBox="0 0 24 24"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/></svg>',
  folder: '<svg viewBox="0 0 24 24"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7Z"/></svg>',
  x: '<svg viewBox="0 0 24 24"><path d="M6 6l12 12M18 6 6 18"/></svg>',
  bell: '<svg viewBox="0 0 24 24"><path d="M12 22a2.5 2.5 0 0 0 2.45-2h-4.9A2.5 2.5 0 0 0 12 22Zm7-6V11a7 7 0 0 0-5.5-6.84V3.5a1.5 1.5 0 0 0-3 0v.66A7 7 0 0 0 5 11v5l-2 2v1h18v-1l-2-2Z"/></svg>',
};

function setRangeFill(el) {
  const min = +el.min || 0, max = +el.max || 100;
  el.style.setProperty('--p', ((+el.value - min) / (max - min || 1) * 100) + '%');
}

/* ═════════════ Navigáció ═════════════ */
function showPage(name) {
  $$('.nav-item').forEach(b => b.classList.toggle('active', b.dataset.page === name));
  $$('.page').forEach(p => p.classList.toggle('active', p.id === 'page-' + name));
  try { localStorage.setItem('page', name); } catch { }
}
$$('.nav-item').forEach(b => b.addEventListener('click', () => showPage(b.dataset.page)));
try { const p = localStorage.getItem('page'); if (p && $('#page-' + p)) showPage(p); } catch { }

/* ═════════════ Általános gombok ═════════════ */
document.addEventListener('click', e => {
  const c = e.target.closest('[data-cmd]');
  if (c) { send(c.dataset.cmd); return; }
  const r = e.target.closest('[data-ring]');
  if (r) { send('ring', { kind: r.dataset.ring }); return; }
  const p = e.target.closest('[data-pick]');
  if (p) { saveNow(); send('pickSound', { target: p.dataset.pick }); return; }
  const v = e.target.closest('[data-preview]');
  if (v) {
    const isIn = v.dataset.preview === 'in';
    send('preview', { kind: isIn ? 'be' : 'ki', file: isIn ? S.soundIn : S.soundOut });
  }
});

/* ═════════════ Toast + modal ═════════════ */
function toast(text, kind = 'ok') {
  const t = document.createElement('div');
  t.className = 'toast ' + kind;
  t.textContent = text;
  $('#toasts').appendChild(t);
  setTimeout(() => { t.style.transition = 'opacity .3s'; t.style.opacity = '0'; }, 2600);
  setTimeout(() => t.remove(), 3000);
}

function modal({ title, text = '', input = null, ok = 'OK', danger = false }) {
  return new Promise(resolve => {
    const m = $('#modal'), inp = $('#modalInput'), okBtn = $('#modalOk');
    $('#modalTitle').textContent = title;
    $('#modalText').textContent = text;
    $('#modalText').hidden = !text;
    inp.hidden = input === null;
    inp.value = input ?? '';
    okBtn.textContent = ok;
    okBtn.style.background = danger ? 'var(--danger)' : '';
    okBtn.style.borderColor = danger ? 'var(--danger)' : '';
    m.hidden = false;
    if (input !== null) setTimeout(() => { inp.focus(); inp.select(); }, 30);
    const done = val => { m.hidden = true; okBtn.onclick = $('#modalCancel').onclick = inp.onkeydown = null; resolve(val); };
    okBtn.onclick = () => done(input === null ? true : inp.value.trim());
    $('#modalCancel').onclick = () => done(null);
    inp.onkeydown = e => { if (e.key === 'Enter') okBtn.click(); if (e.key === 'Escape') done(null); };
  });
}

/* ═════════════ Üzenetek a programtól ═════════════ */
if (hasHost) {
  window.chrome.webview.addEventListener('message', e => {
    const m = e.data;
    if (m.type === 'settings') { S = m.settings; renderAll(); }
    else if (m.type === 'state') { st = m; renderLive(); }
    else if (m.type === 'toast') toast(m.text, m.kind);
  });
}

const activeProfile = () => S.profiles.find(p => p.id === S.activeProfileId) || S.profiles[0];
const selProfile = () => S.profiles.find(p => p.id === selProfileId) || activeProfile();

function renderAll() {
  if (!S) return;
  if (!S.profiles.some(p => p.id === selProfileId)) selProfileId = S.activeProfileId;
  renderSchedule();
  renderSounds();
  renderRadioSettings();
  $('#startWithWindows').checked = S.startWithWindows;
  renderLive();
}

/* ═════════════ Áttekintés (élő) ═════════════ */
$('#enabledToggle').addEventListener('change', e => send('setEnabled', { value: e.target.checked }));

function periodText(hm) {
  if (!st.dayActive) return `Ma ${st.dayName} van – automatikusan nem csenget`;
  const bells = activeProfile().bells.filter(b => b.type !== 'egyeb');
  let prev = null, next = null;
  for (const b of bells) { if (b.time <= hm) prev = b; else { next = b; break; } }
  if (!bells.length) return 'Nincs csengetés beállítva';
  if (!prev) return 'Tanítás előtt';
  if (prev.type === 'be') {
    const n = bells.filter(b => b.type === 'be' && b.time <= hm).length;
    return prev.label ? `${prev.label} folyik` : `${n}. óra folyik`;
  }
  if (next && next.type === 'be') return 'Szünet';
  if (!next) return 'A mai tanításnak vége';
  return 'Óraközi idő';
}

function renderLive() {
  if (!S || !st) return;
  const hm = st.time.slice(0, 5);
  const secNow = toMin(hm) * 60 + Number(st.time.slice(6, 8));

  $('#heroClock').innerHTML = `${hm}<span class="sec">:${st.time.slice(6, 8)}</span>`;
  $('#sideClock').textContent = hm;
  $("#heroDate").textContent = `${st.date}, ${st.dayName}`;
  $('#heroPeriod').textContent = periodText(hm);

  const tg = $('#enabledToggle');
  tg.checked = st.enabled;
  $('#enabledLabel').textContent = st.enabled ? 'Automatikus csengetés: BE' : 'Automatikus csengetés: KI';

  const sp = $('#sideStatus');
  if (!st.enabled) { sp.className = 'status-pill off'; sp.textContent = 'Kikapcsolva'; }
  else if (!st.dayActive) { sp.className = 'status-pill rest'; sp.textContent = 'Ma nem csenget'; }
  else { sp.className = 'status-pill'; sp.textContent = 'Aktív'; }

  // következő csengetés
  const bells = activeProfile().bells;
  if (st.next) {
    const note = (!st.enabled || !st.dayActive) ? ' <span class="muted small">(automatikusan nem szól)</span>' : '';
    $('#nextWhat').innerHTML = `<span class="type-pill ${st.next.type}">${typeName[st.next.type]}</span> ${st.next.time}${st.next.label ? ' · ' + esc(st.next.label) : ''}${note}`;
    $('#nextCount').textContent = fmtCount(st.countdown);
    let prev = null;
    for (const b of bells) if (b.time <= hm) prev = b;
    const start = prev ? toMin(prev.time) * 60 : secNow;
    const end = toMin(st.next.time) * 60;
    $('#periodBar').style.width = prev ? Math.min(100, (secNow - start) / Math.max(1, end - start) * 100) + '%' : '0%';
  } else {
    $('#nextWhat').textContent = 'Ma nincs több csengetés';
    $('#nextCount').textContent = '—';
    $('#periodBar').style.width = '0%';
  }

  // mai lista
  const nextTime = st.next?.time;
  $('#profileNameHome').textContent = activeProfile().name;
  const sched = (S.scheduledSounds || []).filter(x => x.enabled && x.days.includes(st.dow))
    .map(x => ({ time: x.time, type: 'utemezett', label: x.name }));
  const items = (st.dayActive ? bells : []).concat(sched).sort((a, b) => a.time.localeCompare(b.time));
  $('#timeline').innerHTML = items.length ? items.map(b => `
      <div class="tl-row ${b.time <= hm ? 'past' : ''} ${b.time === nextTime && b.type !== 'utemezett' ? 'next' : ''}">
        <span class="tl-time">${b.time}</span>
        <span class="tl-label">${esc(b.label || '')}</span>
        <span class="type-pill ${b.type}">${typeName[b.type]}</span>
      </div>`).join('') : (st.dayActive ? '<div class="tl-empty">Nincs csengetés ebben a rendben.</div>' : '<div class="tl-empty">Ma nincs automatikus csengetés.</div>');

  // csengő szól
  const ring = $('#ringing');
  ring.hidden = !st.bell.playing;
  if (st.bell.playing) {
    $('#ringingTitle').textContent = st.bell.title + ' szól';
    $('#ringingTime').textContent = `${fmt(st.bell.elapsed)} / ${fmt(st.bell.total)}`;
    $('#ringingBar').style.width = Math.min(100, st.bell.elapsed / Math.max(1, st.bell.total) * 100) + '%';
  }

  renderPlayer();
}

/* ═════════════ Csengetési rend ═════════════ */
const DAYS = [[1, 'H'], [2, 'K'], [3, 'Sze'], [4, 'Cs'], [5, 'P'], [6, 'Szo'], [0, 'V']];

function sortBells(p) { p.bells.sort((a, b) => a.time.localeCompare(b.time)); }

function renderSchedule() {
  const p = selProfile();
  selProfileId = p.id;

  $('#profiles').innerHTML = S.profiles.map(x => `
    <button class="profile-chip ${x.id === p.id ? 'sel' : ''}" data-profile="${x.id}">
      ${x.id === S.activeProfileId ? '<span class="live" title="Ez szól"></span>' : ''}${esc(x.name)}
      <span class="muted small">${x.bells.length}</span>
    </button>`).join('') + '<button class="profile-chip add" id="btnNewProfile">+ Új rend</button>';

  const nameInput = $('#profileName');
  if (document.activeElement !== nameInput) nameInput.value = p.name;
  const isActive = p.id === S.activeProfileId;
  const tag = $('#profileActiveTag');
  tag.textContent = isActive ? 'Aktív – ez szól' : 'Nem aktív';
  tag.className = 'tag' + (isActive ? ' active' : '');
  $('#btnActivate').hidden = isActive;
  $('#btnDeleteProfile').disabled = S.profiles.length < 2;

  $('#dayChips').innerHTML = DAYS.map(([d, n]) =>
    `<button class="day-chip ${S.activeDays.includes(d) ? 'on' : ''}" data-day="${d}">${n}</button>`).join('');

  const rows = $('#bellRows');
  if (!p.bells.length) {
    rows.innerHTML = '<div class="bell-empty">Még nincs csengetés. Adj hozzá egy tanórát lent!</div>';
    return;
  }
  rows.innerHTML = p.bells.map((b, i) => {
    const def = b.type === 'ki' ? S.soundOut : S.soundIn;
    return `
    <div class="bell-row" data-i="${i}">
      <input type="time" value="${b.time}" data-f="time">
      <div class="seg">${['be', 'ki', 'egyeb'].map(t =>
        `<button class="${t} ${b.type === t ? 'on' : ''}" data-type="${t}">${typeName[t]}</button>`).join('')}</div>
      <input value="${esc(b.label || '')}" placeholder="pl. 3. óra" data-f="label">
      <div class="sound-cell">
        <span class="name ${b.sound ? '' : 'default'}" title="${esc(b.sound || def)}">${b.sound ? esc(base(b.sound)) : 'Alap (' + esc(noExt(def)) + ')'}</span>
        <button class="icon-btn sm" data-act="pick" title="Saját hang ehhez a csengetéshez">${ICON.folder}</button>
        ${b.sound ? `<button class="icon-btn sm" data-act="clearSound" title="Vissza az alap hangra">${ICON.x}</button>` : ''}
      </div>
      <div class="row-actions">
        <button class="icon-btn sm" data-act="test" title="Kipróbálás">${ICON.play}</button>
        <button class="icon-btn sm danger" data-act="del" title="Törlés">${ICON.trash}</button>
      </div>
    </div>`;
  }).join('');
}

$('#profiles').addEventListener('click', async e => {
  if (e.target.closest('#btnNewProfile')) {
    const name = await modal({ title: 'Új csengetési rend', text: 'Pl. „Rövidített órák”, „Vizsgaidőszak”', input: '', ok: 'Létrehozás' });
    if (!name) return;
    const p = { id: uid(), name, bells: [] };
    S.profiles.push(p);
    selProfileId = p.id;
    save(); renderSchedule();
    return;
  }
  const c = e.target.closest('[data-profile]');
  if (c) { selProfileId = c.dataset.profile; renderSchedule(); }
});

$('#profileName').addEventListener('input', e => {
  selProfile().name = e.target.value;
  save();
});
$('#profileName').addEventListener('change', () => { renderSchedule(); renderLive(); });

$('#btnActivate').addEventListener('click', () => {
  S.activeProfileId = selProfileId;
  saveNow(); renderSchedule(); renderLive();
  toast('Mostantól ez a csengetési rend szól');
});
$('#btnDuplicate').addEventListener('click', () => {
  const p = selProfile();
  const copy = JSON.parse(JSON.stringify(p));
  copy.id = uid(); copy.name = p.name + ' (másolat)';
  S.profiles.push(copy);
  selProfileId = copy.id;
  save(); renderSchedule();
});
$('#btnDeleteProfile').addEventListener('click', async () => {
  if (S.profiles.length < 2) return;
  const p = selProfile();
  if (!await modal({ title: 'Rend törlése', text: `Biztosan törlöd: „${p.name}”?`, ok: 'Törlés', danger: true })) return;
  S.profiles = S.profiles.filter(x => x.id !== p.id);
  if (S.activeProfileId === p.id) S.activeProfileId = S.profiles[0].id;
  selProfileId = S.activeProfileId;
  saveNow(); renderSchedule(); renderLive();
});

$('#dayChips').addEventListener('click', e => {
  const d = e.target.closest('[data-day]'); if (!d) return;
  const n = +d.dataset.day;
  S.activeDays = S.activeDays.includes(n) ? S.activeDays.filter(x => x !== n) : [...S.activeDays, n];
  save(); renderSchedule();
});

const rowsEl = $('#bellRows');
rowsEl.addEventListener('click', async e => {
  const row = e.target.closest('.bell-row'); if (!row) return;
  const p = selProfile(), i = +row.dataset.i, b = p.bells[i];
  const t = e.target.closest('[data-type]');
  if (t) { b.type = t.dataset.type; save(); renderSchedule(); renderLive(); return; }
  const a = e.target.closest('[data-act]'); if (!a) return;
  switch (a.dataset.act) {
    case 'del': p.bells.splice(i, 1); save(); renderSchedule(); renderLive(); break;
    case 'pick': saveNow(); send('pickSound', { target: 'bell', profileId: p.id, index: i }); break;
    case 'clearSound': b.sound = null; save(); renderSchedule(); break;
    case 'test': send('preview', { kind: b.type, file: b.sound || (b.type === 'ki' ? S.soundOut : S.soundIn) }); break;
  }
});
rowsEl.addEventListener('input', e => {
  const row = e.target.closest('.bell-row'); if (!row || e.target.dataset.f !== 'label') return;
  selProfile().bells[+row.dataset.i].label = e.target.value || null;
  save();
});
rowsEl.addEventListener('change', e => {
  const row = e.target.closest('.bell-row'); if (!row || e.target.dataset.f !== 'time') return;
  if (!e.target.value) return;
  const p = selProfile();
  p.bells[+row.dataset.i].time = e.target.value.slice(0, 5);
  sortBells(p); save(); renderSchedule(); renderLive();
});

$('#btnAddBell').addEventListener('click', () => {
  const p = selProfile(), last = p.bells[p.bells.length - 1];
  p.bells.push({ time: last ? fromMin(toMin(last.time) + 5) : '08:00', type: last?.type === 'be' ? 'ki' : 'be', label: null, sound: null });
  sortBells(p); save(); renderSchedule(); renderLive();
});
$('#btnAddLesson').addEventListener('click', () => {
  const p = selProfile(), last = p.bells[p.bells.length - 1];
  const len = Math.max(1, +$('#lessonLen').value || 45), brk = Math.max(0, +$('#breakLen').value || 0);
  const start = last ? toMin(last.time) + (last.type === 'ki' ? brk : len + brk) : 8 * 60;
  const n = p.bells.filter(b => b.type === 'be').length + 1;
  p.bells.push({ time: fromMin(start), type: 'be', label: `${n}. óra`, sound: null });
  p.bells.push({ time: fromMin(start + len), type: 'ki', label: null, sound: null });
  sortBells(p); save(); renderSchedule(); renderLive();
});

/* ═════════════ Hangok ═════════════ */
function renderSounds() {
  $('#soundInName').textContent = noExt(S.soundIn);
  $('#soundOutName').textContent = noExt(S.soundOut);
  const d = $('#bellDuration'), v = $('#bellVolume');
  d.value = S.bellDuration; v.value = S.bellVolume;
  setRangeFill(d); setRangeFill(v);
  $('#bellDurationOut').textContent = S.bellDuration ? S.bellDuration + ' mp' : 'teljes';
  $('#bellVolumeOut').textContent = S.bellVolume + '%';
  $('#fadeOut').checked = S.fadeOut;

  $('#extraList').innerHTML = S.extraSounds.length ? S.extraSounds.map((x, i) => `
    <div class="list-item" data-i="${i}">
      <button class="icon-btn sm" data-act="play" title="Lejátszás">${ICON.play}</button>
      <input value="${esc(x.name)}" data-f="name">
      <span class="muted small" style="max-width:200px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(x.file)}</span>
      <div class="acts"><button class="icon-btn sm danger" data-act="del" title="Törlés">${ICON.trash}</button></div>
    </div>`).join('') : '<div class="list-empty">Nincs egyéb jelzés.</div>';

  $('#homeExtras').innerHTML = S.extraSounds.map(x =>
    `<button class="chip-btn" data-extra="${x.id}">${ICON.play}${esc(x.name)}</button>`).join('');
}

$('#bellDuration').addEventListener('input', e => {
  S.bellDuration = +e.target.value; setRangeFill(e.target);
  $('#bellDurationOut').textContent = S.bellDuration ? S.bellDuration + ' mp' : 'teljes';
  save();
});
$('#bellVolume').addEventListener('input', e => {
  S.bellVolume = +e.target.value; setRangeFill(e.target);
  $('#bellVolumeOut').textContent = S.bellVolume + '%';
  save();
});
$('#fadeOut').addEventListener('change', e => { S.fadeOut = e.target.checked; save(); });

$('#extraList').addEventListener('click', e => {
  const it = e.target.closest('.list-item'); if (!it) return;
  const a = e.target.closest('[data-act]'); if (!a) return;
  const i = +it.dataset.i;
  if (a.dataset.act === 'play') send('ringExtra', { id: S.extraSounds[i].id });
  if (a.dataset.act === 'del') { S.extraSounds.splice(i, 1); save(); renderSounds(); }
});
$('#extraList').addEventListener('input', e => {
  const it = e.target.closest('.list-item'); if (!it) return;
  S.extraSounds[+it.dataset.i].name = e.target.value;
  save();
});
$('#extraList').addEventListener('change', () => renderSounds());
$('#homeExtras').addEventListener('click', e => {
  const b = e.target.closest('[data-extra]'); if (b) send('ringExtra', { id: b.dataset.extra });
});

/* ═════════════ Iskolarádió ═════════════ */
function renderRadioSettings() {
  const r = S.radio;
  const vol = $('#radioVolume'); vol.value = r.volume; setRangeFill(vol);
  $('#autoBreak').checked = r.autoBreak;
  const fs = $('#fadeSeconds'); fs.value = r.fadeSeconds; setRangeFill(fs);
  $('#fadeSecondsOut').textContent = r.fadeSeconds ? String(r.fadeSeconds).replace('.', ',') + ' mp' : 'azonnal';
  renderScheduled();
  $('#shuffle').checked = r.shuffle;

  const sel = $('#autoSource');
  sel.innerHTML = `<option value="playlist">Lejátszási lista (${r.tracks.length} zene)</option>` +
    r.streams.map((s, i) => `<option value="stream:${i}">Online rádió: ${esc(s.name)}</option>`).join('');
  sel.value = r.autoSource;
  if (sel.value !== r.autoSource) sel.value = 'playlist';

  $('#trackCount').textContent = r.tracks.length ? `(${r.tracks.length})` : '';
  $('#trackList').innerHTML = r.tracks.length ? r.tracks.map((t, i) => `
    <div class="list-item" data-i="${i}">
      <span class="track-no">${i + 1}</span>
      <button class="icon-btn sm" data-act="play" title="Lejátszás">${ICON.play}</button>
      <div class="grow"><div class="title">${esc(noExt(t))}</div><div class="sub">${esc(dir(t))}</div></div>
      <div class="acts"><button class="icon-btn sm danger" data-act="del" title="Eltávolítás a listáról">${ICON.x}</button></div>
    </div>`).join('') : '<div class="list-empty">Még nincs zene. Adj hozzá fájlokat vagy egy egész mappát!</div>';

  $('#streamList').innerHTML = r.streams.length ? r.streams.map((s, i) => `
    <div class="list-item" data-i="${i}">
      <button class="icon-btn sm" data-act="play" title="Hallgatás">${ICON.play}</button>
      <div class="grow"><div class="title">${esc(s.name)}</div><div class="sub">${esc(s.url)}</div></div>
      <div class="acts"><button class="icon-btn sm danger" data-act="del" title="Törlés">${ICON.trash}</button></div>
    </div>`).join('') : '<div class="list-empty">Nincs online rádió felvéve.</div>';
}

/* ═════════════ Ütemezett hangok ═════════════ */
function renderScheduled() {
  const list = S.scheduledSounds || [];
  $('#schedList').innerHTML = list.length ? list.map((x, i) => `
    <div class="sched-row ${x.enabled ? '' : 'off'}" data-i="${i}">
      <label class="switch small-sw" title="Be/ki"><input type="checkbox" data-f="enabled" ${x.enabled ? 'checked' : ''}><span class="track"><span class="thumb"></span></span></label>
      <input type="time" value="${x.time}" data-f="time">
      <input value="${esc(x.name)}" placeholder="Megnevezés" data-f="name">
      <div class="file"><button class="icon-btn sm" data-act="pick" title="Másik fájl">${ICON.folder}</button><span title="${esc(x.file)}">${esc(base(x.file))}</span></div>
      <div class="mini-days">${DAYS.map(([d, n]) => `<button class="${x.days.includes(d) ? 'on' : ''}" data-day="${d}">${n}</button>`).join('')}</div>
      <div class="row-actions">
        <button class="icon-btn sm" data-act="play" title="Lejátszás most">${ICON.play}</button>
        <button class="icon-btn sm danger" data-act="del" title="Törlés">${ICON.trash}</button>
      </div>
    </div>`).join('') : '<div class="list-empty">Nincs ütemezett hang. Adj hozzá egyet a fenti gombbal, utána állítsd be az időpontot és a napokat.</div>';
}

const schedEl = $('#schedList');
schedEl.addEventListener('click', e => {
  const row = e.target.closest('.sched-row'); if (!row) return;
  const i = +row.dataset.i, x = S.scheduledSounds[i];
  const d = e.target.closest('[data-day]');
  if (d) {
    const n = +d.dataset.day;
    x.days = x.days.includes(n) ? x.days.filter(v => v !== n) : [...x.days, n];
    save(); renderScheduled(); renderLive(); return;
  }
  const a = e.target.closest('[data-act]'); if (!a) return;
  if (a.dataset.act === 'play') send('playScheduled', { id: x.id });
  if (a.dataset.act === 'pick') { saveNow(); send('pickSound', { target: 'scheduled', index: i }); }
  if (a.dataset.act === 'del') { S.scheduledSounds.splice(i, 1); saveNow(); renderScheduled(); renderLive(); }
});
schedEl.addEventListener('input', e => {
  const row = e.target.closest('.sched-row'); if (!row || e.target.dataset.f !== 'name') return;
  S.scheduledSounds[+row.dataset.i].name = e.target.value; save();
});
schedEl.addEventListener('change', e => {
  const row = e.target.closest('.sched-row'); if (!row) return;
  const x = S.scheduledSounds[+row.dataset.i], f = e.target.dataset.f;
  if (f === 'enabled') x.enabled = e.target.checked;
  else if (f === 'time' && e.target.value) {
    x.time = e.target.value.slice(0, 5);
    S.scheduledSounds.sort((a, b) => a.time.localeCompare(b.time));
  } else if (f !== 'name') return;
  save(); renderScheduled(); renderLive();
});

$('#fadeSeconds').addEventListener('input', e => {
  S.radio.fadeSeconds = +e.target.value; setRangeFill(e.target);
  $('#fadeSecondsOut').textContent = S.radio.fadeSeconds ? String(S.radio.fadeSeconds).replace('.', ',') + ' mp' : 'azonnal';
  save();
});

function renderPlayer() {
  const r = st.radio;
  const active = r.playing || r.paused || r.loading;
  $('#playerArt').classList.toggle('on', r.playing);

  let src = 'Nem szól';
  if (r.loading) src = 'Csatlakozás…';
  else if (r.pausedForBell) src = 'Szünetel – csengetés';
  else if (r.mode === 'playlist') src = `Lejátszási lista · ${r.index + 1} / ${S.radio.tracks.length}`;
  else if (r.mode === 'stream') src = 'Online rádió · élő adás';
  if (r.paused && !r.pausedForBell) src += ' · szünetel';
  if (r.auto) src += ' · automatikus szünetrádió';

  $('#playerSource').textContent = src;
  $('#playerTitle').textContent = active ? r.title : 'Válassz zenét vagy rádiót';
  $('#playerError').textContent = r.error || '';
  $('#miniRadioTitle').textContent = active ? r.title : 'Nem szól';

  const seekWrap = $('.seek'), seek = $('#seek');
  seekWrap.classList.toggle('live', r.mode !== 'playlist');
  $('#posNow').textContent = r.mode === 'stream' ? 'ÉLŐ' : fmt(r.position);
  $('#posLen').textContent = r.mode === 'playlist' ? fmt(r.length) : '';
  if (!seeking) { seek.max = Math.max(1, Math.floor(r.length)); seek.value = Math.floor(r.position); setRangeFill(seek); }

  const icon = r.playing ? ICON.pause : ICON.play;
  $('#playerPlay').innerHTML = icon;
  $('#miniPlay').innerHTML = icon;

  $$('#trackList .list-item').forEach(it => it.classList.toggle('playing', r.mode === 'playlist' && +it.dataset.i === r.index));
}

function playPause() {
  const r = st?.radio;
  if (r?.playing) send('radioPause');
  else if (r?.paused) send('radioResume');
  else send('radioPlay');
}
$('#playerPlay').addEventListener('click', playPause);
$('#miniPlay').addEventListener('click', playPause);

let volTimer;
$('#radioVolume').addEventListener('input', e => {
  S.radio.volume = +e.target.value; setRangeFill(e.target);
  clearTimeout(volTimer);
  volTimer = setTimeout(() => send('radioVolume', { value: S.radio.volume }), 60);
});
const seekEl = $('#seek');
seekEl.addEventListener('pointerdown', () => seeking = true);
seekEl.addEventListener('input', e => { setRangeFill(e.target); $('#posNow').textContent = fmt(+e.target.value); });
seekEl.addEventListener('change', e => { send('radioSeek', { value: +e.target.value }); seeking = false; });

$('#autoBreak').addEventListener('change', e => {
  S.radio.autoBreak = e.target.checked; saveNow();
  toast(e.target.checked ? 'Szünetrádió bekapcsolva' : 'Szünetrádió kikapcsolva');
});
$('#autoSource').addEventListener('change', e => { S.radio.autoSource = e.target.value; save(); });
$('#shuffle').addEventListener('change', e => { S.radio.shuffle = e.target.checked; save(); });

$('#trackList').addEventListener('click', e => {
  const it = e.target.closest('.list-item'); if (!it) return;
  const i = +it.dataset.i, a = e.target.closest('[data-act]');
  if (a?.dataset.act === 'del') { S.radio.tracks.splice(i, 1); saveNow(); renderRadioSettings(); return; }
  if (a?.dataset.act === 'play' || e.detail === 2) send('radioPlay', { index: i });
});
$('#btnClearTracks').addEventListener('click', async () => {
  if (!S.radio.tracks.length) return;
  if (!await modal({ title: 'Lista ürítése', text: 'Az összes zene lekerül a listáról (a fájlok megmaradnak a gépen).', ok: 'Ürítés', danger: true })) return;
  S.radio.tracks = []; saveNow(); renderRadioSettings();
});

$('#streamList').addEventListener('click', e => {
  const it = e.target.closest('.list-item'); if (!it) return;
  const i = +it.dataset.i, a = e.target.closest('[data-act]');
  if (a?.dataset.act === 'play') send('radioStream', { index: i });
  if (a?.dataset.act === 'del') {
    S.radio.streams.splice(i, 1);
    if (S.radio.autoSource.startsWith('stream:')) {
      const k = +S.radio.autoSource.slice(7);
      S.radio.autoSource = k === i ? 'playlist' : k > i ? 'stream:' + (k - 1) : S.radio.autoSource;
    }
    saveNow(); renderRadioSettings();
  }
});
$('#streamForm').addEventListener('submit', e => {
  e.preventDefault();
  const name = $('#streamName').value.trim(), url = $('#streamUrl').value.trim();
  if (!name || !url) return;
  if (!/^https?:\/\//i.test(url)) { toast('A címnek http:// vagy https:// kezdetűnek kell lennie', 'error'); return; }
  S.radio.streams.push({ name, url });
  $('#streamName').value = ''; $('#streamUrl').value = '';
  saveNow(); renderRadioSettings();
  toast('Rádió hozzáadva: ' + name);
});

/* ═════════════ Beállítások ═════════════ */
$('#startWithWindows').addEventListener('change', e => { S.startWithWindows = e.target.checked; saveNow(); });

/* ═════════════ Indulás ═════════════ */
if (hasHost) send('ready');
else demoStart();

/* ═════════════ Demó mód (böngészőben, program nélkül – csak a felület kipróbálásához) ═════════════ */
function demoHandle(cmd, data) {
  if (cmd === 'saveSettings') return;
  if (cmd === 'setEnabled') { S.bellEnabled = data.value; }
  if (cmd === 'ring') { demoBell = { t: 0, title: data.kind === 'ki' ? 'Kicsengő' : 'Becsengő' }; }
  if (cmd === 'stopBell') demoBell = null;
  if (cmd === 'radioPlay' || cmd === 'radioResume') demoRadio = true;
  if (cmd === 'radioPause' || cmd === 'radioStop') demoRadio = false;
  toast('(demó) ' + cmd);
}
var demoBell = null, demoRadio = true;
function demoStart() {
  const bells = [['08:00', 'be', '1. óra'], ['08:45', 'ki'], ['08:55', 'be', '2. óra'], ['09:40', 'ki'], ['10:00', 'be', '3. óra'], ['10:45', 'ki'],
  ['10:55', 'be', '4. óra'], ['11:40', 'ki'], ['11:50', 'be', '5. óra'], ['12:35', 'ki'], ['12:45', 'be', '6. óra'], ['13:30', 'ki'], ['13:50', 'be', '7. óra'], ['14:35', 'ki'], ['14:45', 'be', '8. óra'], ['15:30', 'ki']]
    .map(([time, type, label]) => ({ time, type, label: label || null, sound: null }));
  S = {
    bellEnabled: true, bellDuration: 42, bellVolume: 100, fadeOut: true, soundIn: 'becsengo.wav', soundOut: 'kicsengo.wav',
    scheduledSounds: [{ id: 's1', time: '07:45', name: 'Reggeli zene', file: 'reggel.mp3', days: [1, 2, 3, 4, 5], enabled: true },
      { id: 's2', time: '08:00', name: 'Himnusz', file: 'himnusz.mp3', days: [1], enabled: false }],
    extraSounds: [{ id: 'a', name: 'Himnusz', file: 'himnusz.mp3' }, { id: 'b', name: 'Tűzriadó', file: 'riado.mp3' }],
    profiles: [{ id: 'normal', name: 'Normál csengetési rend', bells }, { id: 'r', name: 'Rövidített órák', bells: bells.slice(0, 8) }],
    activeProfileId: 'normal', activeDays: [1, 2, 3, 4, 5], startWithWindows: true,
    radio: { tracks: ['D:\\Zene\\Iskolarádió\\Reggeli dal.mp3', 'D:\\Zene\\Iskolarádió\\Szünet mix.mp3', 'D:\\Zene\\Iskolarádió\\Péntek.mp3'],
      streams: [{ name: 'Iskolai adás', url: 'http://example.com/stream.mp3' }], volume: 70, shuffle: false, autoBreak: true, autoSource: 'playlist', fadeSeconds: 2 },
  };
  renderAll();
  const tick = () => {
    const d = new Date();
    const hm = pad(d.getHours()) + ':' + pad(d.getMinutes());
    const next = activeProfile().bells.find(b => b.time > hm);
    if (demoBell) { demoBell.t++; if (demoBell.t > 42) demoBell = null; }
    st = {
      time: hm + ':' + pad(d.getSeconds()),
      date: d.toLocaleDateString('hu-HU', { year: 'numeric', month: 'long', day: 'numeric' }),
      dayName: d.toLocaleDateString('hu-HU', { weekday: 'long' }),
      dayActive: true, dow: 1, enabled: S.bellEnabled,
      next: next || null,
      countdown: next ? (toMin(next.time) * 60 - (d.getHours() * 3600 + d.getMinutes() * 60 + d.getSeconds())) : null,
      bell: demoBell ? { playing: true, title: demoBell.title, elapsed: demoBell.t, total: 42 } : { playing: false, title: '', elapsed: 0, total: 0 },
      radio: { mode: 'playlist', playing: demoRadio, paused: !demoRadio, loading: false, title: 'Szünet mix', index: 1, position: 83, length: 214, error: null, auto: true, pausedForBell: false },
    };
    renderLive();
  };
  tick(); setInterval(tick, 1000);
}
