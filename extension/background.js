// Пересылает состояние вкладки SoundCloud виджету на этом компьютере
// и возвращает вкладке команды, которые виджет для неё накопил.
const WIDGET = 'http://127.0.0.1:48251/soundcloud';

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg.type !== 'state' || !sender.tab) return;
  // только латиница: русские и другие буквы — как \uXXXX, чтобы число байт совпадало с числом символов
  // (мини-сервер виджета читает тело по Content-Length)
  const body = JSON.stringify(Object.assign({ tab: sender.tab.id }, msg.state))
    .replace(/[\u007f-￿]/g, (c) => '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0'));
  fetch(WIDGET, { method: 'POST', headers: { 'Content-Type': 'text/plain' }, body, signal: AbortSignal.timeout(2500) })
    .then((r) => r.json())
    .then((answer) => sendResponse(answer))
    .catch(() => sendResponse({ commands: [] })); // виджет не запущен — просто ждём
  return true; // ответ придёт асинхронно
});
