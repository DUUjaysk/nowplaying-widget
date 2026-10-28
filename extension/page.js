// Работает внутри самой страницы SoundCloud (world: MAIN, до её скриптов): запоминает аудио-элементы,
// через которые играет музыка, чтобы виджет мог менять громкость только трека, а не всей Opera.
(() => {
  const media = new Set();
  let wanted = null;          // громкость, выставленная виджетом (0..1)
  let setAt = 0;              // когда виджет её выставил

  const play = HTMLMediaElement.prototype.play;
  HTMLMediaElement.prototype.play = function () {
    media.add(this);
    if (wanted !== null) this.volume = wanted;   // новый трек — та же громкость
    return play.apply(this, arguments);
  };

  window.addEventListener('np-set-volume', (e) => {
    const v = Number(e.detail);
    if (!(v >= 0 && v <= 1)) return;
    wanted = v;
    setAt = Date.now();
    media.forEach((m) => { m.volume = v; });
  });

  // раз в полсекунды сообщаем громкость играющего трека; если её поменяли на самом сайте — принимаем
  setInterval(() => {
    let cur = null;
    media.forEach((m) => { if (!m.paused) cur = m.volume; });
    if (cur === null) media.forEach((m) => { if (cur === null) cur = m.volume; });
    if (cur !== null && wanted !== null && Math.abs(cur - wanted) > 0.01 && Date.now() - setAt > 1500) wanted = cur;
    window.dispatchEvent(new CustomEvent('np-volume', { detail: cur === null ? -1 : cur }));
    // где сейчас играет трек — для статистики (повтор трека = позиция вернулась в начало)
    let pos = -1;
    media.forEach((m) => { if (!m.paused) pos = m.currentTime; });
    window.dispatchEvent(new CustomEvent('np-position', { detail: pos }));
  }, 500);
})();
