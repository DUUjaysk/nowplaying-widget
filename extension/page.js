// Работает внутри самой страницы SoundCloud (world: MAIN, до её скриптов): запоминает аудио-элементы,
// через которые играет музыка, чтобы виджет мог менять громкость только трека, а не всей Opera.
//
// Главный — виджет. SoundCloud сам то и дело возвращает громкость своего ползунка (новый трек, пауза,
// перемотка), поэтому любое расхождение исправляем обратно. Новую громкость с сайта принимаем, только если
// человек сам только что трогал ползунок громкости на странице (блок .volume) или нажимал Shift + ↑/↓.
(() => {
  const media = new Set();
  const KEY = 'np-track-volume';          // громкость виджета переживает перезагрузку страницы
  let wanted = null;                      // громкость, выставленная виджетом (0..1)
  let touchedAt = 0;                      // когда человек трогал громкость на самом сайте
  try { const s = localStorage.getItem(KEY); if (s !== null && Number(s) >= 0 && Number(s) <= 1) wanted = Number(s); } catch (e) {}

  function remember(v) { wanted = v; try { localStorage.setItem(KEY, String(v)); } catch (e) {} }
  function apply(m) { if (wanted !== null && Math.abs(m.volume - wanted) > 0.01) m.volume = wanted; }
  const userTouching = () => Date.now() - touchedAt < 3000;

  const play = HTMLMediaElement.prototype.play;
  HTMLMediaElement.prototype.play = function () {
    media.add(this);
    apply(this);                                 // новый трек или продолжение после паузы — та же громкость
    return play.apply(this, arguments);
  };

  // человек трогает громкость на странице SoundCloud
  ['pointerdown', 'wheel'].forEach((type) => document.addEventListener(type, (e) => {
    if (e.target instanceof Element && e.target.closest('.volume')) touchedAt = Date.now();
  }, true));
  document.addEventListener('keydown', (e) => {
    if (e.shiftKey && (e.key === 'ArrowUp' || e.key === 'ArrowDown')) touchedAt = Date.now();
  }, true);
  document.addEventListener('pointermove', (e) => { if (e.buttons && touchedAt && userTouching()) touchedAt = Date.now(); }, true);

  window.addEventListener('np-set-volume', (e) => {
    const v = Number(e.detail);
    if (!(v >= 0 && v <= 1)) return;
    remember(v);
    touchedAt = 0;
    media.forEach(apply);
  });

  // часто держим громкость: SoundCloud подменяет её — через 0,2 с она снова наша
  setInterval(() => {
    let cur = null;
    media.forEach((m) => { if (!m.paused) cur = m.volume; });
    if (cur === null || wanted === null || Math.abs(cur - wanted) <= 0.01) return;
    if (userTouching()) remember(cur);          // двигал ползунок на сайте — это и есть новая громкость
    else media.forEach(apply);
  }, 200);

  // раз в полсекунды сообщаем виджету громкость играющего трека и где он сейчас играет
  setInterval(() => {
    let cur = null, pos = -1;
    media.forEach((m) => { if (!m.paused) { cur = m.volume; pos = m.currentTime; } });
    if (cur === null) media.forEach((m) => { if (cur === null) cur = m.volume; });
    window.dispatchEvent(new CustomEvent('np-volume', { detail: cur === null ? -1 : cur }));
    window.dispatchEvent(new CustomEvent('np-position', { detail: pos }));
  }, 500);
})();
