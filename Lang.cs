// Язык интерфейса: русский или английский. Выбирается в установщике (или в меню значка в трее)
// и хранится в HKCU\Software\NowPlayingWidget → Language ("ru" / "en"). Если не выбран —
// русский для русской Windows, английский для остальных.
using System.Globalization;
using Microsoft.Win32;

namespace NowPlaying
{
    static class L
    {
        const string Key = @"Software\NowPlayingWidget";
        public static readonly bool En = Load() == "en";

        public static string Code { get { return En ? "en" : "ru"; } }

        // Надпись на нужном языке: L.T("Скрыть плеер", "Hide player").
        public static string T(string ru, string en) { return En ? en : ru; }

        public static string Load()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key))
                {
                    var v = k == null ? null : k.GetValue("Language") as string;
                    if (v == "ru" || v == "en") return v;
                }
            }
            catch { }
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
        }

        public static void Save(string code)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue("Language", code);
        }
    }
}
