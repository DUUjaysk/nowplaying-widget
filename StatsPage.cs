// Шаблон страницы «Статистика прослушиваний SoundCloud». ListenStats подставляет данные вместо /*DATA*/[].
// В тексте ниже нет двойных кавычек — так шаблон помещается в обычную C#-строку @"..." без экранирования.
// Разделы переключаются адресом после #: обзор, #history, #artist=<ключ>, #track=<ссылка>.
namespace NowPlaying
{
    static class StatsPage
    {
        public const string Html = @"<!doctype html>
<html lang='ru'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>Мой SoundCloud</title>
<style>
:root { --bg:#141416; --card:#1f1f23; --line:#2c2c32; --text:#f2f2f5; --muted:#9a9aa4; --accent:#ff5500; --accent2:#ff8a3d; --bar:#3a3a42; }
@media (prefers-color-scheme: light) { :root { --bg:#f4f4f6; --card:#ffffff; --line:#e4e4e8; --text:#17171a; --muted:#6b6b75; --bar:#e2e2e8; } }
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--text); font:15px/1.4 'Segoe UI', system-ui, sans-serif; }
a { color:inherit; }
.wrap { max-width:980px; margin:0 auto; padding:28px 16px 60px; }
header { display:flex; flex-wrap:wrap; align-items:center; justify-content:space-between; gap:14px; margin-bottom:22px; }
h1 { margin:0; font-size:26px; font-weight:700; overflow-wrap:anywhere; }
h1 span { color:var(--accent); }
.sub { color:var(--muted); font-size:13px; margin-top:2px; }
.sub a { color:var(--accent2); text-decoration:none; }
.bar-top { display:flex; flex-wrap:wrap; gap:8px; }
.pills { display:flex; gap:4px; background:var(--card); padding:4px; border-radius:12px; border:1px solid var(--line); }
.pills button, .pills a { border:0; background:none; color:var(--muted); font:inherit; font-size:13px; padding:7px 12px; border-radius:9px; cursor:pointer; text-decoration:none; }
.pills .on { background:var(--accent); color:#fff; font-weight:600; }
.back { color:var(--muted); text-decoration:none; font-size:14px; cursor:pointer; display:inline-block; margin-bottom:10px; }
.back:hover { color:var(--text); }
.cards { display:grid; grid-template-columns:repeat(auto-fit, minmax(150px, 1fr)); gap:12px; margin-bottom:16px; }
.card { background:var(--card); border:1px solid var(--line); border-radius:16px; padding:14px 16px; }
.card .n { font-size:24px; font-weight:700; }
.card .l { color:var(--muted); font-size:13px; }
.grid { display:grid; grid-template-columns:1fr 1fr; gap:16px; }
@media (max-width:760px) { .grid { grid-template-columns:1fr; } }
section { background:var(--card); border:1px solid var(--line); border-radius:16px; padding:16px; margin-bottom:16px; min-width:0; }
section h2 { margin:0 0 12px; font-size:16px; }
section h2 small { color:var(--muted); font-weight:400; font-size:12px; margin-left:6px; }
.row { display:grid; grid-template-columns:26px 1fr auto; gap:10px; align-items:center; padding:7px 0; border-top:1px solid var(--line); }
.row.first, section h2 + .row { border-top:0; }
.rank { color:var(--muted); font-size:13px; text-align:right; }
.name { min-width:0; }
.name > a, .name > b { display:block; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; text-decoration:none; font-weight:600; }
.name > a:hover { color:var(--accent2); }
.name i { display:block; font-style:normal; color:var(--muted); font-size:12px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
.name i a { text-decoration:none; }
.name i a:hover { color:var(--accent2); }
.meter { height:4px; background:var(--bar); border-radius:2px; margin-top:5px; overflow:hidden; }
.meter div { height:100%; background:linear-gradient(90deg, var(--accent), var(--accent2)); border-radius:2px; }
.val { text-align:right; font-size:13px; white-space:nowrap; }
.val i { display:block; color:var(--muted); font-style:normal; font-size:12px; }
.chart { display:flex; align-items:flex-end; gap:3px; height:140px; padding-top:8px; }
.chart .c { flex:1; display:flex; flex-direction:column; justify-content:flex-end; align-items:center; height:100%; min-width:0; }
.chart .b { width:100%; background:linear-gradient(180deg, var(--accent2), var(--accent)); border-radius:4px 4px 1px 1px; min-height:2px; }
.chart .b.zero { background:var(--bar); }
.chart .x { color:var(--muted); font-size:10px; margin-top:4px; height:13px; white-space:nowrap; }
.facts { display:grid; grid-template-columns:1fr 1fr; gap:10px; }
.fact { background:var(--bg); border-radius:12px; padding:10px 12px; }
.fact b { display:block; font-size:18px; }
.fact span { color:var(--muted); font-size:12px; }
.empty { color:var(--muted); padding:8px 0; }
.big-empty { text-align:center; padding:60px 16px; color:var(--muted); }
.big-empty b { display:block; color:var(--text); font-size:18px; margin-bottom:6px; }
.search { width:100%; font:inherit; color:var(--text); background:var(--bg); border:1px solid var(--line); border-radius:10px; padding:9px 12px; margin-bottom:8px; outline:none; }
.search:focus { border-color:var(--accent); }
.dayhead { display:flex; justify-content:space-between; gap:10px; margin:16px 0 4px; font-weight:600; }
.dayhead span { color:var(--muted); font-weight:400; font-size:13px; white-space:nowrap; }
.hist { display:grid; grid-template-columns:44px 1fr auto; gap:10px; padding:6px 0; border-top:1px solid var(--line); align-items:center; }
.hist .tm { color:var(--muted); font-size:13px; }
.more { display:block; margin:14px auto 0; font:inherit; color:var(--text); background:var(--bg); border:1px solid var(--line); border-radius:10px; padding:8px 16px; cursor:pointer; }
.note { color:var(--muted); font-size:12px; margin-top:8px; }
footer { color:var(--muted); font-size:12px; text-align:center; margin-top:10px; }
</style>
</head>
<body>
<div class='wrap'>
  <div id='root'></div>
  <footer id='foot'></footer>
</div>
<script>
const ALL = /*DATA*/[];
const EN = /*LANG*/'ru' === 'en';          // язык выбирает виджет
const LOC = EN ? 'en-US' : 'ru-RU';
function t(ru, en) { return EN ? en : ru; }
const DAY = 86400000;
const PERIODS = [[t('7 дней', '7 days'), 7], [t('30 дней', '30 days'), 30], [t('Год', 'Year'), 365], [t('Всё время', 'All time'), 0]];
const WEEK = EN ? ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'] : ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс'];
// английские формы для plural(): ключ — русская форма для 1
const EN_FORMS = { 'раз': ['time', 'times'], 'трек': ['track', 'tracks'], 'прослушивание': ['play', 'plays'], 'пропуск': ['skip', 'skips'], 'артист': ['artist', 'artists'], 'день': ['day', 'days'] };
document.documentElement.lang = EN ? 'en' : 'ru';
document.title = t('Мой SoundCloud', 'My SoundCloud');
let period = 30;
try { const s = localStorage.getItem('period'), p = Number(s); if (s !== null && PERIODS.some(x => x[1] === p)) period = p; } catch (e) {}

const rows = ALL.map(r => Object.assign({}, r, { date: new Date(r.t), sec: Number(r.sec) || 0 }))
  .filter(r => !isNaN(r.date) && r.url).sort((a, b) => a.date - b.date);
const listens = rows.filter(r => !r.skip), skips = rows.filter(r => r.skip);
const artistKey = r => (r.artistUrl || r.artist || '?').toLowerCase();
const firstSeen = {}, lastSeen = {};
listens.forEach(r => { const k = artistKey(r); if (!(k in firstSeen)) firstSeen[k] = r.date; lastSeen[k] = r.date; });

// ---------- мелочи ----------
function el(tag, cls, text) { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }
function plural(n, one, few, many) {
  if (EN && EN_FORMS[one]) return n + ' ' + EN_FORMS[one][n === 1 ? 0 : 1];
  const a = n % 10, b = n % 100; return n + ' ' + (a === 1 && b !== 11 ? one : a >= 2 && a <= 4 && (b < 12 || b > 14) ? few : many);
}
function word(n, one, few, many) { return plural(n, one, few, many).replace(/^\d+ /, ''); }
function dur(sec) { const h = Math.floor(sec / 3600), m = Math.round(sec % 3600 / 60); return h ? h + t(' ч ', ' h ') + m + t(' мин', ' min') : m + t(' мин', ' min'); }
function mmss(sec) { return Math.floor(sec / 60) + ':' + String(sec % 60).padStart(2, '0'); }
function day(d) { return d.toLocaleDateString(LOC, { day: '2-digit', month: '2-digit', year: 'numeric' }); }
function dayKey(d) { return d.getFullYear() + '-' + (d.getMonth() + 1) + '-' + d.getDate(); }
function startOfDay(d) { return new Date(d.getFullYear(), d.getMonth(), d.getDate()); }
function daysAgo(d) { return Math.round((startOfDay(new Date()) - startOfDay(d)) / DAY); }
function ago(d) { const n = daysAgo(d); return n <= 0 ? t('сегодня', 'today') : n === 1 ? t('вчера', 'yesterday') : plural(n, 'день', 'дня', 'дней') + t(' назад', ' ago'); }
function ext(url, text) { const a = el('a', null, text); a.href = url; a.target = '_blank'; a.rel = 'noopener'; return a; }
function hashLink(hash, text) { const a = el('a', null, text); a.href = '#' + hash; return a; }
function artistLink(r) { return hashLink('artist=' + encodeURIComponent(artistKey(r)), r.artist || '?'); }
function trackLink(r) { return hashLink('track=' + encodeURIComponent(r.url), r.title || r.url); }

function group(list, key) {
  const m = new Map();
  list.forEach(r => { const k = key(r); const g = m.get(k) || { key: k, items: [], plays: 0, sec: 0 }; g.items.push(r); g.plays++; g.sec += r.sec; m.set(k, g); });
  return [...m.values()].sort((a, b) => b.plays - a.plays || b.sec - a.sec);
}

function row(rank, nameEl, sub, value, valueSub, share) {
  const r = el('div', 'row');
  r.append(el('div', 'rank', rank));
  const n = el('div', 'name'); n.append(nameEl);
  if (sub) { const i = el('i'); if (typeof sub === 'string') i.textContent = sub; else i.append(sub); n.append(i); }
  if (share != null) { const m = el('div', 'meter'); const f = el('div'); f.style.width = Math.max(3, share * 100) + '%'; m.append(f); n.append(m); }
  r.append(n);
  const v = el('div', 'val', value); if (valueSub) v.append(el('i', null, valueSub));
  r.append(v);
  return r;
}

function section(title, hint) { const s = el('section'); const h = el('h2', null, title); if (hint) h.append(el('small', null, hint)); s.append(h); return s; }
function cards(list) { const c = el('div', 'cards'); list.forEach(x => { const d = el('div', 'card'); d.append(el('div', 'n', String(x[0])), el('div', 'l', x[1])); c.append(d); }); return c; }
function grid() { const g = el('div', 'grid'); for (const s of arguments) g.append(s); return g; }

// ---------- графики ----------
function dayBins(n) {
  const now = new Date(), bins = [];
  for (let i = n - 1; i >= 0; i--) {
    const d = startOfDay(new Date(now - i * DAY));
    bins.push({ from: d, to: new Date(d.getTime() + DAY), x: n <= 7 ? WEEK[(d.getDay() + 6) % 7] : (i % 5 === 0 ? String(d.getDate()) : ''), title: day(d) });
  }
  return bins;
}
function monthBins(first) {
  const now = new Date(), bins = [];
  for (let d = new Date(first.getFullYear(), first.getMonth(), 1); d <= now; d = new Date(d.getFullYear(), d.getMonth() + 1, 1))
    bins.push({ from: d, to: new Date(d.getFullYear(), d.getMonth() + 1, 1), x: d.toLocaleDateString(LOC, { month: 'short' }).replace('.', ''), title: d.toLocaleDateString(LOC, { month: 'long', year: 'numeric' }) });
  return bins;
}
// для страниц артиста и трека: по дням, если слушаешь недавно, иначе по месяцам
function autoBins(list) {
  const span = list.length ? daysAgo(list[0].date) + 1 : 7;
  return span <= 45 ? dayBins(Math.max(7, span)) : monthBins(list[0].date);
}
function bars(values, labels, titles) {
  const max = Math.max(1, ...values), c = el('div', 'chart');
  values.forEach((v, i) => { const col = el('div', 'c'); const b = el('div', 'b' + (v ? '' : ' zero')); b.style.height = (v / max * 100) + '%'; b.title = titles[i] + ': ' + dur(v); col.append(b, el('div', 'x', labels[i])); c.append(col); });
  return c;
}
function chart(list, bins) {
  return bars(bins.map(b => list.filter(r => r.date >= b.from && r.date < b.to).reduce((s, r) => s + r.sec, 0)), bins.map(b => b.x), bins.map(b => b.title));
}
function hours(list) {
  const h = new Array(24).fill(0); list.forEach(r => h[r.date.getHours()] += r.sec);
  return bars(h, h.map((_, i) => i % 3 === 0 ? String(i) : ''), h.map((_, i) => i + ':00–' + (i + 1) + ':00'));
}
function weekdays(list) {
  const w = new Array(7).fill(0); list.forEach(r => w[(r.date.getDay() + 6) % 7] += r.sec);
  return bars(w, WEEK, WEEK);
}

// ---------- шапка ----------
function header(title, sub, withPeriods) {
  const h = el('header');
  const left = el('div'); left.append(title);
  if (sub) { const s = el('div', 'sub'); if (typeof sub === 'string') s.textContent = sub; else sub.forEach(x => s.append(x)); left.append(s); }
  h.append(left);
  const right = el('div', 'bar-top');
  const nav = el('div', 'pills');
  const view = location.hash.startsWith('#history') ? 'history' : location.hash.length > 1 ? '' : 'main';
  [[t('Обзор', 'Overview'), '', 'main'], [t('История', 'History'), 'history', 'history']].forEach(x => { const a = hashLink(x[1], x[0]); if (x[2] === view) a.className = 'on'; nav.append(a); });
  right.append(nav);
  if (withPeriods) {
    const pills = el('div', 'pills');
    PERIODS.forEach(p => { const b = el('button', p[1] === period ? 'on' : null, p[0]); b.onclick = () => { period = p[1]; try { localStorage.setItem('period', String(period)); } catch (e) {} render(); }; pills.append(b); });
    right.append(pills);
  }
  h.append(right);
  return h;
}
function brand() { const h = el('h1'); h.append(t('Мой ', 'My '), el('span', null, 'SoundCloud')); return h; }
function back() { const a = el('a', 'back', t('← Назад', '← Back')); a.onclick = () => { if (history.length > 1) history.back(); else location.hash = ''; }; return a; }

// ---------- обзор ----------
function streaks() {
  const days = [...new Set(listens.map(r => dayKey(startOfDay(r.date))))].map(k => { const p = k.split('-'); return new Date(p[0], p[1] - 1, p[2]); }).sort((a, b) => a - b);
  let best = 0, run = 0, prev = null;
  days.forEach(d => { run = prev && Math.round((d - prev) / DAY) === 1 ? run + 1 : 1; best = Math.max(best, run); prev = d; });
  const have = new Set(days.map(dayKey));
  let cur = 0, d = startOfDay(new Date());
  if (!have.has(dayKey(d))) d = new Date(d - DAY);           // сегодня ещё не слушал — серия не прервана
  while (have.has(dayKey(d))) { cur++; d = new Date(d - DAY); }
  return { best: best, cur: cur, days: days.length };
}

function renderMain(root) {
  const from = period ? new Date(startOfDay(new Date()) - (period - 1) * DAY) : new Date(0);
  const list = listens.filter(r => r.date >= from), skipped = skips.filter(r => r.date >= from);
  root.append(header(brand(), listens.length ? t('Статистика ведётся с ', 'Tracking since ') + day(listens[0].date) : '', true));
  if (!list.length) {
    const e = el('div', 'big-empty'); e.append(el('b', null, listens.length ? t('За этот период прослушиваний нет', 'No plays in this period') : t('Пока пусто', 'Nothing here yet')));
    e.append(el('div', null, listens.length ? t('Выбери период побольше.', 'Pick a longer period.') : t('Включи музыку на SoundCloud в Opera — каждый трек, который играл хотя бы 30 секунд, появится здесь.', 'Play music on SoundCloud in your browser — every track that plays for at least 30 seconds will show up here.')));
    root.append(e); return;
  }
  const artists = group(list, artistKey), tracks = group(list, r => r.url);
  const total = list.reduce((s, r) => s + r.sec, 0);
  root.append(cards([[list.length, word(list.length, 'прослушивание', 'прослушивания', 'прослушиваний')], [dur(total), t('музыки', 'of music')], [artists.length, word(artists.length, 'артист', 'артиста', 'артистов')], [tracks.length, word(tracks.length, 'трек', 'трека', 'треков')]]));

  const act = section(t('Сколько слушал', 'How much you listened'), period && period <= 30 ? t('по дням', 'by day') : t('по месяцам', 'by month'));
  act.append(chart(list, period && period <= 30 ? dayBins(period) : monthBins(period ? new Date(new Date().getFullYear(), new Date().getMonth() - 11, 1) : list[0].date)));
  root.append(act);

  const sa = section(t('Топ артистов', 'Top artists'));
  artists.slice(0, 15).forEach((g, i) => sa.append(row(i + 1, artistLink(g.items[0]), t('слушаешь с ', 'listening since ') + day(firstSeen[g.key]), plural(g.plays, 'раз', 'раза', 'раз'), dur(g.sec), g.plays / artists[0].plays)));
  const st = section(t('Топ треков', 'Top tracks'));
  tracks.slice(0, 15).forEach((g, i) => { const r = g.items[g.items.length - 1]; st.append(row(i + 1, trackLink(r), artistLink(r), plural(g.plays, 'раз', 'раза', 'раз'), dur(g.sec), g.plays / tracks[0].plays)); });
  root.append(grid(sa, st));

  const sd = section(t('Открытия', 'Discoveries'), t('кого впервые услышал за этот период', 'artists you first heard in this period'));
  const found = artists.filter(g => firstSeen[g.key] >= from).sort((a, b) => firstSeen[b.key] - firstSeen[a.key]);
  if (!found.length) sd.append(el('div', 'empty', t('Новых артистов за этот период нет.', 'No new artists in this period.')));
  const maxFound = Math.max(1, ...found.map(g => g.plays));
  found.slice(0, 15).forEach((g, i) => sd.append(row(i + 1, artistLink(g.items[0]), t('впервые ', 'first ') + day(firstSeen[g.key]) + ' · ' + ago(firstSeen[g.key]), plural(g.plays, 'раз', 'раза', 'раз'), null, g.plays / maxFound)));
  const sh = section(t('Когда слушаешь', 'When you listen'), t('по часам', 'by hour')); sh.append(hours(list));
  root.append(grid(sd, sh));

  // привычки
  const s = streaks();
  const byDay = group(list, r => dayKey(r.date)).map(g => Object.assign(g, { date: g.items[0].date })).sort((a, b) => b.sec - a.sec);
  const sp = section(t('Привычки', 'Habits'));
  const facts = el('div', 'facts');
  [[plural(s.cur, 'день', 'дня', 'дней'), t('серия подряд сейчас', 'current streak')], [plural(s.best, 'день', 'дня', 'дней'), t('самая длинная серия', 'longest streak')],
   [dur(byDay[0].sec), t('рекорд за день — ', 'best day — ') + day(byDay[0].date)], [dur(Math.round(total / byDay.length)), t('в среднем в день, когда слушал', 'average on days you listened')]]
    .forEach(f => { const d = el('div', 'fact'); d.append(el('b', null, f[0]), el('span', null, f[1])); facts.append(d); });
  sp.append(facts);
  const sw = section(t('По дням недели', 'By day of week')); sw.append(weekdays(list));
  root.append(grid(sw, sp));

  // заело: один трек 4+ раза за день
  const stuck = group(list, r => r.url + '|' + dayKey(r.date)).filter(g => g.plays >= 4).sort((a, b) => b.plays - a.plays || b.items[0].date - a.items[0].date);
  const sz = section(t('Заело', 'On repeat'), t('один трек 4 раза и больше за день', 'same track 4+ times in a day'));
  if (!stuck.length) sz.append(el('div', 'empty', t('За этот период ничего не заедало.', 'Nothing was on repeat in this period.')));
  stuck.slice(0, 10).forEach((g, i) => { const r = g.items[0]; sz.append(row(i + 1, trackLink(r), (r.artist || '?') + ' · ' + day(r.date), plural(g.plays, 'раз', 'раза', 'раз'), null, null)); });

  // забытые: слушал 5+ раз, но больше месяца не включал (за всё время)
  const forgotten = group(listens, artistKey).filter(g => g.plays >= 5 && daysAgo(lastSeen[g.key]) > 30).sort((a, b) => b.plays - a.plays);
  const sf = section(t('Забытые', 'Forgotten'), t('слушал много, но больше месяца не включал', 'played a lot, but not in over a month'));
  if (!forgotten.length) sf.append(el('div', 'empty', t('Забытых артистов нет — или статистика ещё молодая.', 'No forgotten artists — or your stats are still young.')));
  forgotten.slice(0, 10).forEach((g, i) => sf.append(row(i + 1, artistLink(g.items[0]), t('последний раз ', 'last played ') + day(lastSeen[g.key]) + ' · ' + ago(lastSeen[g.key]), plural(g.plays, 'раз', 'раза', 'раз'), null, null)));
  root.append(grid(sz, sf));

  // пропуски
  const sk = section(t('Пропуски', 'Skips'), t('чаще всего переключаешь на 3–30 секунде', 'tracks you most often skip at 3–30 seconds'));
  if (!skipped.length) sk.append(el('div', 'empty', t('Пропусков за этот период нет.', 'No skips in this period.')));
  else {
    const rate = Math.round(skipped.length / (skipped.length + list.length) * 100);
    sk.append(el('div', 'note', t('Всего ', 'In total ') + plural(skipped.length, 'пропуск', 'пропуска', 'пропусков') + ' — ' + rate + t('% от всех включённых треков.', '% of all tracks you started.')));
    const top = group(skipped, r => r.url);
    top.slice(0, 10).forEach((g, i) => { const r = g.items[0]; sk.append(row(i + 1, trackLink(r), artistLink(r), plural(g.plays, 'раз', 'раза', 'раз'), null, g.plays / top[0].plays)); });
  }
  root.append(sk);
}

// ---------- артист ----------
function renderArtist(root, key) {
  const list = listens.filter(r => artistKey(r) === key), skipped = skips.filter(r => artistKey(r) === key);
  const any = list[0] || skipped[0];
  root.append(back());
  if (!any) { root.append(el('div', 'big-empty', t('Такого артиста в статистике нет.', 'This artist is not in your stats.'))); return; }
  const sub = [];
  if (list.length) sub.push(t('слушаешь с ', 'listening since ') + day(list[0].date) + t(' · последний раз ', ' · last played ') + ago(list[list.length - 1].date));
  if (any.artistUrl) { if (sub.length) sub.push(' · '); sub.push(ext(any.artistUrl, t('открыть на SoundCloud ↗', 'open on SoundCloud ↗'))); }
  root.append(header(el('h1', null, any.artist || '?'), sub, false));
  const tracks = group(list, r => r.url), total = list.reduce((s, r) => s + r.sec, 0);
  root.append(cards([[list.length, word(list.length, 'прослушивание', 'прослушивания', 'прослушиваний')], [dur(total), t('музыки', 'of music')], [tracks.length, word(tracks.length, 'трек', 'трека', 'треков')], [skipped.length, word(skipped.length, 'пропуск', 'пропуска', 'пропусков')]]));
  if (!list.length) return;
  const sc = section(t('Как слушал', 'Listening over time')); sc.append(chart(list, autoBins(list))); root.append(sc);
  const st = section(t('Треки', 'Tracks'));
  tracks.forEach((g, i) => { const r = g.items[g.items.length - 1]; st.append(row(i + 1, trackLink(r), t('впервые ', 'first ') + day(g.items[0].date), plural(g.plays, 'раз', 'раза', 'раз'), dur(g.sec), g.plays / tracks[0].plays)); });
  const sr = section(t('Последние прослушивания', 'Recent plays'));
  list.slice(-20).reverse().forEach((r, i) => sr.append(row(i + 1, trackLink(r), day(r.date) + ' ' + r.date.toLocaleTimeString(LOC, { hour: '2-digit', minute: '2-digit' }), mmss(r.sec), null, null)));
  root.append(grid(st, sr));
}

// ---------- трек ----------
function renderTrack(root, url) {
  const list = listens.filter(r => r.url === url), skipped = skips.filter(r => r.url === url);
  const any = list[list.length - 1] || skipped[skipped.length - 1];
  root.append(back());
  if (!any) { root.append(el('div', 'big-empty', t('Такого трека в статистике нет.', 'This track is not in your stats.'))); return; }
  const sub = [artistLink(any), ' · ', ext(any.url, t('открыть на SoundCloud ↗', 'open on SoundCloud ↗'))];
  root.append(header(el('h1', null, any.title || any.url), sub, false));
  const total = list.reduce((s, r) => s + r.sec, 0);
  root.append(cards([[list.length, word(list.length, 'прослушивание', 'прослушивания', 'прослушиваний')], [dur(total), t('музыки', 'of music')], [skipped.length, word(skipped.length, 'пропуск', 'пропуска', 'пропусков')], [list.length ? ago(list[0].date) : '—', list.length ? t('впервые, ', 'first, ') + day(list[0].date) : t('ещё не дослушивал', 'not finished yet')]]));
  if (!list.length) return;
  const sc = section(t('Как слушал', 'Listening over time')); sc.append(chart(list, autoBins(list))); root.append(sc);
  const sr = section(t('Все прослушивания', 'All plays'), list.length > 100 ? t('последние 100', 'last 100') : null);
  list.slice(-100).reverse().forEach((r, i) => sr.append(row(i + 1, el('b', null, day(r.date) + ', ' + r.date.toLocaleDateString(LOC, { weekday: 'short' })), r.date.toLocaleTimeString(LOC, { hour: '2-digit', minute: '2-digit' }), mmss(r.sec), null, null)));
  root.append(sr);
}

// ---------- история ----------
function renderHistory(root) {
  root.append(header(brand(), t('Всё, что ты слушал, — от новых к старым', 'Everything you played, newest first'), false));
  const s = section(t('История', 'History'));
  const input = el('input', 'search'); input.placeholder = t('Поиск по треку или артисту', 'Search by track or artist'); s.append(input);
  const box = el('div'); s.append(box);
  root.append(s);
  let limit = 300;
  function fill() {
    box.textContent = '';
    const q = input.value.trim().toLowerCase();
    const found = listens.filter(r => !q || (r.title || '').toLowerCase().includes(q) || (r.artist || '').toLowerCase().includes(q)).reverse();
    if (!found.length) { box.append(el('div', 'empty', q ? t('Ничего не нашлось.', 'Nothing found.') : t('Пока пусто.', 'Nothing here yet.'))); return; }
    const byDay = new Map();
    found.forEach(r => { const k = dayKey(r.date); if (!byDay.has(k)) byDay.set(k, []); byDay.get(k).push(r); });
    let shown = 0;
    for (const items of byDay.values()) {
      if (shown >= limit) break;
      const d = items[0].date, h = el('div', 'dayhead', day(d) + ', ' + d.toLocaleDateString(LOC, { weekday: 'long' }));
      h.append(el('span', null, plural(items.length, 'трек', 'трека', 'треков') + ' · ' + dur(items.reduce((a, r) => a + r.sec, 0))));
      box.append(h);
      for (const r of items) {
        if (shown++ >= limit) break;
        const line = el('div', 'hist');
        line.append(el('div', 'tm', r.date.toLocaleTimeString(LOC, { hour: '2-digit', minute: '2-digit' })));
        const n = el('div', 'name'); n.append(trackLink(r)); const i = el('i'); i.append(artistLink(r)); n.append(i);
        line.append(n, el('div', 'val', mmss(r.sec)));
        box.append(line);
      }
    }
    if (found.length > limit) { const b = el('button', 'more', t('Показать ещё', 'Show more')); b.onclick = () => { limit += 300; fill(); }; box.append(b); }
  }
  input.oninput = () => { limit = 300; fill(); };
  fill();
}

// ---------- переключение разделов ----------
function render() {
  const root = document.getElementById('root'); root.textContent = '';
  const h = location.hash.slice(1);
  if (h.startsWith('artist=')) renderArtist(root, decodeURIComponent(h.slice(7)));
  else if (h.startsWith('track=')) renderTrack(root, decodeURIComponent(h.slice(6)));
  else if (h === 'history') renderHistory(root);
  else renderMain(root);
}
document.getElementById('foot').textContent = t('Считаются прослушивания на этом компьютере от 30 секунд; переключённые на 3–30 секунде треки — пропуски. Страница собрана виджетом «Сейчас играет».', 'Plays on this computer of at least 30 seconds are counted; tracks switched at 3–30 seconds are skips. Page built by the Now Playing widget.');
window.addEventListener('hashchange', () => { render(); window.scrollTo(0, 0); });
render();
</script>
</body>
</html>";
    }
}
