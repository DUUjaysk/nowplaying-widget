// Пересылает состояние вкладки SoundCloud виджету на этом компьютере
// и возвращает вкладке команды, которые виджет для неё накопил.
const WIDGET = 'http://127.0.0.1:48251/soundcloud';

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg.type !== 'state' || !sender.tab) return;
  const body = JSON.stringify(Object.assign({ tab: sender.tab.id }, msg.state));
  fetch(WIDGET, { method: 'POST', headers: { 'Content-Type': 'text/plain' }, body })
    .then((r) => r.json())
    .then((answer) => sendResponse(answer))
    .catch(() => sendResponse({ commands: [] })); // виджет не запущен — просто ждём
  return true; // ответ придёт асинхронно
});
