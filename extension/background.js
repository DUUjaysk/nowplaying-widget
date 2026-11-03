// Пересылает состояние вкладки SoundCloud виджету на этом компьютере
// и возвращает вкладке команды, которые виджет для неё накопил.
const WIDGET = 'http://127.0.0.1:48251/';

// только латиница: русские и другие буквы — как \uXXXX, чтобы число байт совпадало с числом символов
// (мини-сервер виджета читает тело по Content-Length)
function post(path, data, timeout) {
  const body = JSON.stringify(data).replace(/[\u007f-￿]/g, (c) => '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0'));
  return fetch(WIDGET + path, { method: 'POST', headers: { 'Content-Type': 'text/plain' }, body, signal: AbortSignal.timeout(timeout) })
    .then((r) => r.json());
}

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (!sender.tab) return;
  if (msg.type === 'state') {
    post('soundcloud', Object.assign({ tab: sender.tab.id }, msg.state), 2500)
      .then((answer) => sendResponse(answer))
      .catch(() => sendResponse({ commands: [] })); // виджет не запущен — просто ждём
    return true; // ответ придёт асинхронно
  }
  if (msg.type === 'history') {
    post('history', { items: msg.items }, 15000)
      .then((answer) => sendResponse(answer))
      .catch(() => sendResponse({})); // виджет не запущен — заберём историю в следующий раз
    return true;
  }
});
