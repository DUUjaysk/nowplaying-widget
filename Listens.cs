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
