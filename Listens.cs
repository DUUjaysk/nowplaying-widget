// Статистика прослушиваний SoundCloud. Расширение раз в 0,7 с сообщает, какой трек играет и на какой
// он секунде; ListenLog копит, сколько трек реально играл, и записывает прослушивание (от 30 секунд)
// строкой JSON в listens.jsonl рядом с виджетом, а переключённый на 3–30 секунде трек — как пропуск ("skip").
// ListenStats строит из этого файла страницу soundcloud-stats.html и открывает её в браузере.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace NowPlaying
{
    class ListenLog
    {
        public const int MinSeconds = 30;            // меньше — пролистал, а не послушал
        public const int MinSkipSeconds = 3;         // меньше 3 секунд — просто проскочил трек, даже не пропуск
        const int SettleMinutes = 15;                // треки из истории свежее этого пока не трогаем
        const int SameListenMinutes = 5;             // время в истории примерное: столько разницы с записью компьютера — то же прослушивание
        public static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "listens.jsonl");

        readonly object sync = new object();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly Timer idle;
        string url, title, artist, artistUrl;
        DateTime started, lastTick;
        double seconds, lastPosition = -1;

        public ListenLog()
        {
            // вкладку закрыли или Opera вылетела — отчёты перестали приходить, прослушивание всё равно записываем
            idle = new Timer(delegate { lock (sync) { if (url != null && DateTime.Now - lastTick > TimeSpan.FromSeconds(15)) Flush(); } }, null, 10000, 10000);
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : "";
        }

        // Очередной отчёт активной вкладки SoundCloud.
        public void Tick(Dictionary<string, object> track, bool playing, double position)
        {
            lock (sync)
            {
                var now = DateTime.Now;
                string u = Str(track, "url");
                if (u.Length == 0) u = null;
                // другой трек или этот же заиграл сначала (повтор) — прошлое прослушивание закончено
                bool restarted = u != null && u == url && lastPosition > MinSeconds && position >= 0 && position < 5;
                if (u != url || restarted)
                {
                    Flush();
                    if (u != null)
                    {
                        url = u;
                        title = Str(track, "title");
                        artist = Str(track, "artist");
                        artistUrl = Str(track, "artistUrl");
                        started = now;
                    }
                }
                else if (playing && url != null)
                {
                    double dt = (now - lastTick).TotalSeconds;
                    if (dt > 0 && dt < 5) seconds += dt;     // большой разрыв — компьютер спал, не считаем
                }
                if (position >= 0) lastPosition = position;
                lastTick = now;
            }
        }

        // Трек, который играет сейчас, если уже наслушал 30 секунд, — строкой JSON (иначе null).
        // Для страницы статистики: прослушивание при этом не заканчивается.
        public string CurrentLine()
        {
            lock (sync)
            {
                if (url == null || seconds < MinSeconds) return null;
                return json.Serialize(new Dictionary<string, object>
                {
                    { "t", started.ToString("yyyy-MM-ddTHH:mm:ss") },
                    { "title", title }, { "artist", artist }, { "url", url }, { "artistUrl", artistUrl },
                    { "sec", (int)Math.Round(seconds) }
                });
            }
        }

        public void Flush()
        {
            lock (sync)
            {
                var line = CurrentLine();
                // переключил на 3–30 секунде — пропуск: в прослушивания не идёт, но для статистики пропусков пишем
                if (line == null && url != null && seconds >= MinSkipSeconds)
                    line = json.Serialize(new Dictionary<string, object>
                    {
                        { "t", started.ToString("yyyy-MM-ddTHH:mm:ss") },
                        { "title", title }, { "artist", artist }, { "url", url }, { "artistUrl", artistUrl },
                        { "sec", (int)Math.Round(seconds) }, { "skip", true }
                    });
                if (line != null)
                    try { File.AppendAllText(FilePath, line + "\n", new UTF8Encoding(false)); } catch (Exception ex) { Debug.WriteLine(ex); }
                url = null;
                seconds = 0;
                lastPosition = -1;
            }
        }

        class ComputerListen { public string Url; public DateTime From, To; }

        static long Number(Dictionary<string, object> d, string key)
        {
            object v;
            try { return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture) : 0; }
            catch { return 0; }
        }

        static DateTime FromMs(long ms)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime();
        }

        // История аккаунта SoundCloud (расширение 1.10+). Треки, которых нет в записях этого компьютера, идут в файл
        // с пометкой "src":"phone". Секунды примерные: сайт не сообщает, сколько трек играл, берём время до следующего трека,
        // но не больше длины трека. Возвращает, сколько строк добавлено.
        public int AddHistory(System.Collections.IEnumerable raw)
        {
            lock (sync)
            {
                var known = new HashSet<string>();
                var computer = new List<ComputerListen>();
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        Dictionary<string, object> d;
                        try { d = json.Deserialize<Dictionary<string, object>>(line); } catch { continue; }
                        if (d == null) continue;
                        if (Str(d, "src") == "phone") { known.Add(Str(d, "url") + "|" + Number(d, "at")); continue; }
                        DateTime t;
                        if (!DateTime.TryParse(Str(d, "t"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t)) continue;
                        computer.Add(new ComputerListen { Url = Str(d, "url"), From = t, To = t.AddSeconds(Number(d, "sec")) });
                    }
                if (url != null) computer.Add(new ComputerListen { Url = url, From = started, To = DateTime.Now });

                var items = raw.OfType<Dictionary<string, object>>().Where(i => Number(i, "at") > 0 && Str(i, "url").Length > 0).OrderBy(i => Number(i, "at")).ToList();
                var lines = new StringBuilder();
                int added = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    long at = Number(items[i], "at");
                    string u = Str(items[i], "url");
                    DateTime when = FromMs(at);
                    if (DateTime.Now - when < TimeSpan.FromMinutes(SettleMinutes)) continue;     // свежий трек мог ещё не дойти до записей компьютера
                    if (known.Contains(u + "|" + at)) continue;
                    if (computer.Any(c => c.Url == u && when >= c.From.AddMinutes(-SameListenMinutes) && when <= c.To.AddMinutes(SameListenMinutes))) continue;

                    double length = Number(items[i], "dur") / 1000.0;
                    double gap = i + 1 < items.Count ? (Number(items[i + 1], "at") - at) / 1000.0 : -1;
                    // телефон часто отправляет треки пачкой, и промежутка меньше 2 секунд ничего не значит
                    bool gapKnown = gap >= 2;
                    double sec = gapKnown ? Math.Min(length > 0 ? length : gap, gap) : length;
                    var row = new Dictionary<string, object>
                    {
                        { "t", when.ToString("yyyy-MM-ddTHH:mm:ss") },
                        { "title", Str(items[i], "title") }, { "artist", Str(items[i], "artist") }, { "url", u }, { "artistUrl", Str(items[i], "artistUrl") },
                        { "sec", (int)Math.Round(sec) }, { "src", "phone" }, { "at", at }
                    };
                    if (gapKnown && gap < MinSeconds) row["skip"] = true;
                    lines.Append(json.Serialize(row)).Append('\n');
                    known.Add(u + "|" + at);
                    added++;
                }
                if (added > 0)
                    try { File.AppendAllText(FilePath, lines.ToString(), new UTF8Encoding(false)); }
                    catch (Exception ex) { Debug.WriteLine(ex); return 0; }
                return added;
            }
        }
    }

    static class ListenStats
    {
        // Собирает страницу статистики из listens.jsonl и открывает её в браузере по умолчанию.
        public static void Open(ListenLog log)
        {
            var lines = (File.Exists(ListenLog.FilePath) ? File.ReadAllLines(ListenLog.FilePath, Encoding.UTF8) : new string[0])
                .Where(l => l.Trim().StartsWith("{")).ToList();
            var current = log.CurrentLine();   // текущий трек, если уже наслушал 30 секунд, тоже попадёт в статистику
            if (current != null) lines.Add(current);
            string data = "[" + string.Join(",", lines) + "]";
            string page = StatsPage.Html.Replace("/*DATA*/[]", data.Replace("</", "<\\/")).Replace("/*LANG*/'ru'", "'" + L.Code + "'");
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "soundcloud-stats.html");
            File.WriteAllText(path, page, new UTF8Encoding(false));
            Process.Start(path);
        }
    }
}
