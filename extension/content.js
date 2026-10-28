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
