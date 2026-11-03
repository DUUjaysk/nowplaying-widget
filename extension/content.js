// Работает на страницах SoundCloud: раз в 0,7 с сообщает виджету, включены ли
// повтор, перемешивание и лайк, какой трек играет, и нажимает эти кнопки, когда виджет просит.
const POLL_MS = 700;
const LIKE = '.playbackSoundBadge__like';
let musicVolume = -1;                       // громкость трека от page.js (-1 — пока неизвестна)
window.addEventListener('np-volume', (e) => { musicVolume = Number(e.detail); });
let position = -1;                          // секунда трека от page.js (-1 — не играет)
window.addEventListener('np-position', (e) => { position = Number(e.detail); });

// Какой трек сейчас в плеере внизу страницы — для статистики прослушиваний.
function readTrack() {
  const link = document.querySelector('.playbackSoundBadge__titleLink');
  if (!link) return null;
  const artist = document.querySelector('.playbackSoundBadge__lightLink');
  const clean = (href) => (href || '').split('?')[0];
  return {
    title: (link.title || link.textContent || '').trim(),
    url: clean(link.href),
    artist: artist ? (artist.title || artist.textContent || '').trim() : '',
    artistUrl: artist ? clean(artist.href) : '',
  };
}

function readState() {
  const repeat = document.querySelector('.repeatControl');
  const shuffle = document.querySelector('.shuffleControl');
  const like = document.querySelector(LIKE);
  if (!repeat || !shuffle) return null;
  return {
    repeat: repeat.classList.contains('m-one') ? 'one' : repeat.classList.contains('m-all') ? 'all' : 'none',
    shuffle: shuffle.classList.contains('m-shuffling'),
    playing: !!document.querySelector('.playControl.playing'),
    canLike: !!like,
    liked: !!like && (like.classList.contains('sc-button-selected') || like.title === 'Unlike'),
    musicVolume,
    track: readTrack(),
    position,
  };
}

function run(command) {
  if (command.startsWith('vol:')) {         // громкость только трека — передаём в page.js
    window.dispatchEvent(new CustomEvent('np-set-volume', { detail: parseFloat(command.slice(4)) }));
    return;
  }
  const selector = { repeat: '.repeatControl', shuffle: '.shuffleControl', like: LIKE }[command];
  const button = selector && document.querySelector(selector);
  if (button) button.click();
}

// Ответ фоновой части ждём не дольше 3 с: если Opera усыпила её посреди запроса, ответ не придёт никогда,
// и без этого ограничения отчёты остановились бы до перезагрузки страницы.
function withTimeout(promise, ms) {
  return Promise.race([promise, new Promise((_, reject) => setTimeout(() => reject(new Error('timeout')), ms))]);
}

async function tick() {
  try {
    const state = readState();
    if (state) {
      const answer = await withTimeout(chrome.runtime.sendMessage({ type: 'state', state }), 3000);
      ((answer && answer.commands) || []).forEach(run);
    }
  } catch (e) {
    // нет ответа или расширение перезагрузили (тогда вкладка ждёт обновления страницы) — пробуем снова
  } finally {
    setTimeout(tick, POLL_MS);
  }
}

tick();

// ---------- история аккаунта: в неё попадают и прослушивания с телефона ----------
// Раз в 10 минут читаем её от имени открытой вкладки и отдаём виджету только сведения о треках: токен входа остаётся здесь.
const HISTORY_FIRST_MS = 20 * 1000;
const HISTORY_EVERY_MS = 10 * 60 * 1000;
const HISTORY_MAX_PAGES = 5;
const API = 'https://api-v2.soundcloud.com/';

// client_id сайта виден в адресах его собственных запросов; запоминаем, потому что список запросов страницы со временем очищается.
function clientId() {
  const known = performance.getEntriesByType('resource').map((e) => e.name).filter((n) => n.startsWith(API) && n.includes('client_id='));
  try {
    if (known.length) localStorage.setItem('np-client-id', new URL(known[known.length - 1]).searchParams.get('client_id'));
    return localStorage.getItem('np-client-id');
  } catch (e) {
    return known.length ? new URL(known[known.length - 1]).searchParams.get('client_id') : null;
  }
}

async function readHistory() {
  const cookie = document.cookie.match(/(?:^|;\s*)oauth_token=([^;]+)/);
  const id = clientId();
  if (!cookie || !id) return null;       // не вошли в аккаунт или сайт ещё ничего не запрашивал
  const headers = { Authorization: 'OAuth ' + decodeURIComponent(cookie[1]) };
  const items = [];
  let next = API + 'me/play-history/tracks?limit=100&client_id=' + encodeURIComponent(id);
  for (let page = 0; next && page < HISTORY_MAX_PAGES; page++) {
    const r = await fetch(next, { headers });
    if (!r.ok) return null;
    const data = await r.json();
    (data.collection || []).forEach((i) => {
      const track = i.track, user = (track && track.user) || {};
      if (track && i.played_at && track.permalink_url) items.push({ at: i.played_at, url: track.permalink_url, title: track.title || '', artist: user.username || '', artistUrl: user.permalink_url || '', dur: track.full_duration || 0 });
    });
    next = data.next_href && data.next_href.startsWith(API) ? data.next_href + (data.next_href.includes('client_id=') ? '' : '&client_id=' + encodeURIComponent(id)) : null;
  }
  return items;
}

async function syncHistory() {
  try {
    const items = await readHistory();
    if (items && items.length) await withTimeout(chrome.runtime.sendMessage({ type: 'history', items }), 20000);
  } catch (e) {
    // нет сети или виджет не запущен — попробуем в следующий раз
  } finally {
    setTimeout(syncHistory, HISTORY_EVERY_MS);
  }
}

setTimeout(syncHistory, HISTORY_FIRST_MS);
