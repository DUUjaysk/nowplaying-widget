// Установщик «Сейчас играет» / Now Playing: один exe, внутри которого программа, значок и расширение для SoundCloud.
//   NowPlaying-Setup.exe               — окно установки (вверху выбор языка: Русский / English)
//   NowPlaying-Setup.exe --silent [--lang ru|en] — установить без окна (для проверок)
//   Uninstall.exe --uninstall [--silent] [--purge] — удаление (--purge стирает и настройки)
// Ставит в %LOCALAPPDATA%\Programs\NowPlaying без прав администратора, регистрирует в «Приложениях».
// Выбранный язык записывается в HKCU\Software\NowPlayingWidget → Language — его читает и сам виджет.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace NowPlayingSetup
{
    static class Product
    {
        public static string Name { get { return L.T("Сейчас играет", "Now Playing"); } }
        public static readonly string[] AllNames = { "Сейчас играет", "Now Playing" };   // ярлыки на обоих языках
        public const string Version = "1.1";
        public const string Publisher = "DUUjaysk";
        public const string ExeName = "NowPlaying.exe";
        public const string ProcessName = "NowPlaying";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NowPlayingWidget";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValue = "NowPlayingWidget";
        public const string SettingsKey = @"Software\NowPlayingWidget";

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\NowPlaying"); } }
        public static string Exe { get { return Path.Combine(Dir, ExeName); } }
        public static string DesktopLink(string name) { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name + ".lnk"); }
        public static string StartLink(string name) { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), name + ".lnk"); }
    }

    // Язык установщика и виджета. По умолчанию — уже выбранный раньше, иначе язык Windows.
    static class L
    {
        public static bool En = Load() == "en";
        public static string Code { get { return En ? "en" : "ru"; } }
        public static string T(string ru, string en) { return En ? en : ru; }

        static string Load()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Product.SettingsKey))
                {
                    var v = k == null ? null : k.GetValue("Language") as string;
                    if (v == "ru" || v == "en") return v;
                }
            }
            catch { }
            return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
        }
    }

    // ---------------- Установка и удаление (без интерфейса) ----------------
    static class Installer
    {
        // Файлы программы лежат внутри установщика как ресурсы: «payload/путь/к/файлу».
        static IEnumerable<string> PayloadNames()
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n => n.StartsWith("payload/"));
        }

        public static void CloseRunning()
        {
            foreach (var p in Process.GetProcessesByName(Product.ProcessName))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }

        static void DeleteLinks()
        {
            foreach (var n in Product.AllNames)
                foreach (var link in new[] { Product.DesktopLink(n), Product.StartLink(n) })
                    try { if (File.Exists(link)) File.Delete(link); } catch { }
        }

        public static void Install(bool desktopLink, bool autostart, Action<string> status)
        {
            status(L.T("Закрываю запущенный виджет…", "Closing the running widget…"));
            CloseRunning();

            status(L.T("Копирую файлы…", "Copying files…"));
            Directory.CreateDirectory(Product.Dir);
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in PayloadNames())
            {
                var target = Path.Combine(Product.Dir, name.Substring("payload/".Length).Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var src = asm.GetManifestResourceStream(name))
                using (var dst = File.Create(target))
                    src.CopyTo(dst);
            }
            // сам установщик становится программой удаления
            File.Copy(asm.Location, Path.Combine(Product.Dir, "Uninstall.exe"), true);
            using (var k = Registry.CurrentUser.CreateSubKey(Product.SettingsKey)) k.SetValue("Language", L.Code);

            status(L.T("Создаю ярлыки…", "Creating shortcuts…"));
            DeleteLinks();                        // в том числе ярлыки прошлой установки на другом языке
            Shortcut(Product.StartLink(Product.Name));
            if (desktopLink) Shortcut(Product.DesktopLink(Product.Name));

            using (var run = Registry.CurrentUser.CreateSubKey(Product.RunKey))
            {
                if (autostart) run.SetValue(Product.RunValue, "\"" + Product.Exe + "\" --hidden");
                else run.DeleteValue(Product.RunValue, false);
            }

            status(L.T("Регистрирую в «Приложениях»…", "Registering in Installed apps…"));
            using (var k = Registry.CurrentUser.CreateSubKey(Product.UninstallKey))
            {
                var uninstall = "\"" + Path.Combine(Product.Dir, "Uninstall.exe") + "\" --uninstall";
                k.SetValue("DisplayName", Product.Name);
                k.SetValue("DisplayVersion", Product.Version);
                k.SetValue("Publisher", Product.Publisher);
                k.SetValue("DisplayIcon", Product.Exe + ",0");
                k.SetValue("InstallLocation", Product.Dir);
                k.SetValue("UninstallString", uninstall);
                k.SetValue("QuietUninstallString", uninstall + " --silent");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(DirSize(Product.Dir) / 1024), RegistryValueKind.DWord);
            }
            status(L.T("Готово", "Done"));
        }

        public static void Uninstall(bool purgeSettings)
        {
            CloseRunning();
            DeleteLinks();
            using (var run = Registry.CurrentUser.CreateSubKey(Product.RunKey)) run.DeleteValue(Product.RunValue, false);
            Registry.CurrentUser.DeleteSubKeyTree(Product.UninstallKey, false);
            if (purgeSettings) Registry.CurrentUser.DeleteSubKeyTree(Product.SettingsKey, false);
            // Uninstall.exe сейчас запущен из этой папки (копией из %TEMP%), поэтому папку можно стереть
            for (int i = 0; i < 10 && Directory.Exists(Product.Dir); i++)
            {
                try { Directory.Delete(Product.Dir, true); }
                catch { Thread.Sleep(300); }
            }
        }

        static long DirSize(string dir)
        {
            return new DirectoryInfo(dir).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }

        // Ярлык через WScript.Shell (позднее связывание: без лишних библиотек)
        static void Shortcut(string path)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type);
            object link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            var lt = link.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Product.Exe });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Product.Dir });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { Product.Exe + ",0" });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { L.T("Виджет плеера и микшера в стиле iOS", "iOS-style player and mixer widget") });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
    }

    // ---------------- Окно в стиле виджета ----------------
    static class Ui
    {
        public static SolidColorBrush Brush(uint argb)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            b.Freeze();
            return b;
        }
        public static readonly FontFamily Text = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly Brush Faint = Brush(0x99FFFFFF);
        public static readonly Brush Glass = Brush(0x1FFFFFFF);
        public static readonly Brush Edge = Brush(0x33FFFFFF);
    }

    class SetupWindow : Window
    {
        readonly bool uninstallMode;
        readonly StackPanel body = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        CheckBox desktopBox, autostartBox, purgeBox;
        bool desktopOn = true, autostartOn = true, purgeOn;
        TextBlock statusText;
        Border mainButton;
        TextBlock mainLabel;

        public SetupWindow(bool uninstall)
        {
            uninstallMode = uninstall;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 440;
            FontFamily = Ui.Text;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;

            var card = new Border
            {
                Margin = new Thickness(20), CornerRadius = new CornerRadius(28), BorderBrush = Ui.Edge, BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Color.FromRgb(0x3A, 0x2A, 0x4A), Color.FromRgb(0x16, 0x16, 0x1A), 60),
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.5 },
                Child = body
            };
            Content = card;
            MouseLeftButtonDown += delegate { try { DragMove(); } catch (InvalidOperationException) { } };
            Build();
        }

        // Всё содержимое окна — заново при смене языка (галочки сохраняются).
        void Build()
        {
            if (desktopBox != null) desktopOn = desktopBox.IsChecked == true;
            if (autostartBox != null) autostartOn = autostartBox.IsChecked == true;
            if (purgeBox != null) purgeOn = purgeBox.IsChecked == true;
            body.Children.Clear();
            Title = (uninstallMode ? L.T("Удаление: ", "Uninstall: ") : L.T("Установка: ", "Setup: ")) + Product.Name;

            // шапка: значок, название, версия, крестик
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = LoadIcon();
            if (icon != null)
            {
                var img = new Image { Source = icon, Width = 56, Height = 56, Margin = new Thickness(0, 0, 16, 0) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                head.Children.Add(img);
            }
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = Product.Name, Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold });
            names.Children.Add(new TextBlock { Text = L.T("Версия ", "Version ") + Product.Version + L.T(" · плеер и микшер в стиле iOS", " · iOS-style player and mixer"), Foreground = Ui.Faint, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(names, 1);
            head.Children.Add(names);
            var close = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Ui.Glass, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top };
            close.Child = new TextBlock { Text = "", FontFamily = Ui.Icons, FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            close.MouseLeftButtonDown += (s, e) => e.Handled = true;
            close.MouseLeftButtonUp += (s, e) => Close();
            Grid.SetColumn(close, 2);
            head.Children.Add(close);
            body.Children.Add(head);

            // язык: Русский | English (пока установка не прошла)
            if (!done)
            {
                var langs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
                foreach (var lang in new[] { new[] { "ru", "Русский" }, new[] { "en", "English" } })
                {
                    bool en = lang[0] == "en", on = en == L.En;
                    var pill = new Border
                    {
                        CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand,
                        Background = on ? Brushes.White : Ui.Glass, BorderBrush = Ui.Edge, BorderThickness = new Thickness(on ? 0 : 1),
                        Child = new TextBlock { Text = lang[1], FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = on ? Ui.Brush(0xFF1C1C1E) : Brushes.White }
                    };
                    pill.MouseLeftButtonDown += (s, e) => e.Handled = true;
                    pill.MouseLeftButtonUp += (s, e) => { e.Handled = true; if (L.En != en) { L.En = en; Build(); } };
                    langs.Children.Add(pill);
                }
                body.Children.Add(langs);
            }

            var info = new Border { Background = Ui.Glass, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 14, 0, 0) };
            var infoStack = new StackPanel();
            if (uninstallMode)
            {
                infoStack.Children.Add(Line(L.T("Виджет будет закрыт, ярлыки, автозапуск и файлы — удалены.", "The widget will be closed; shortcuts, autostart and files will be removed.")));
                purgeBox = Check(L.T("Удалить и настройки (место, размер, размытие)", "Also remove settings (position, size, blur)"), purgeOn);
                infoStack.Children.Add(purgeBox);
            }
            else
            {
                infoStack.Children.Add(Line(L.T("Обложка трека, перемотка, лайки и повтор для SoundCloud, громкость каждого приложения, статистика прослушиваний.",
                                                "Cover art, seeking, likes and repeat for SoundCloud, per-app volume, listening stats.")));
                infoStack.Children.Add(new TextBlock { Text = L.T("Папка: ", "Folder: ") + Product.Dir, Foreground = Ui.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
                desktopBox = Check(L.T("Ярлык на рабочем столе", "Desktop shortcut"), desktopOn);
                autostartBox = Check(L.T("Запускать вместе с Windows", "Start with Windows"), autostartOn);
                infoStack.Children.Add(desktopBox);
                infoStack.Children.Add(autostartBox);
            }
            info.Child = infoStack;
            body.Children.Add(info);

            statusText = new TextBlock { Foreground = Ui.Faint, FontSize = 12, Margin = new Thickness(4, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
            body.Children.Add(statusText);

            mainLabel = new TextBlock { Text = uninstallMode ? L.T("Удалить", "Uninstall") : L.T("Установить", "Install"), Foreground = uninstallMode ? Brushes.White : Ui.Brush(0xFF1C1C1E), FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            mainButton = new Border { Height = 46, CornerRadius = new CornerRadius(23), Background = uninstallMode ? Ui.Brush(0xFFFF453A) : Brushes.White, Cursor = Cursors.Hand, Margin = new Thickness(0, 14, 0, 0), Child = mainLabel };
            mainButton.MouseLeftButtonDown += (s, e) => e.Handled = true;
            mainButton.MouseLeftButtonUp += (s, e) => { e.Handled = true; Run(); };
            body.Children.Add(mainButton);
        }

        TextBlock Line(string s)
        {
            return new TextBlock { Text = s, Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.Wrap };
        }

        CheckBox Check(string text, bool on)
        {
            return new CheckBox
            {
                IsChecked = on, Margin = new Thickness(0, 8, 0, 0), Cursor = Cursors.Hand,
                Content = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 13 }
            };
        }

        bool done;

        void Run()
        {
            if (done)
            {
                if (!uninstallMode) Process.Start(Product.Exe);
                Close();
                return;
            }
            mainButton.IsEnabled = false;
            mainButton.Opacity = 0.6;
            try
            {
                if (uninstallMode)
                {
                    statusText.Text = L.T("Удаляю…", "Uninstalling…");
                    Installer.Uninstall(purgeBox.IsChecked == true);
                    statusText.Text = L.T("«" + Product.Name + "» удалён.", Product.Name + " has been removed.");
                    mainLabel.Text = L.T("Закрыть", "Close");
                }
                else
                {
                    Installer.Install(desktopBox.IsChecked == true, autostartBox.IsChecked == true, s => statusText.Text = s);
                    statusText.Text = L.T("Готово! Ярлык «" + Product.Name + "» — в меню «Пуск». Для повтора, перемешивания, лайков и статистики SoundCloud " +
                                          "загрузите в Opera или Chrome расширение из папки soundcloud-extension (opera://extensions или chrome://extensions → «Режим разработчика» → «Загрузить распакованное»).",
                                          "Done! The " + Product.Name + " shortcut is in the Start menu. For SoundCloud repeat, shuffle, likes and stats, load the extension " +
                                          "from the soundcloud-extension folder in Opera or Chrome (opera://extensions or chrome://extensions → Developer mode → Load unpacked).");
                    mainLabel.Text = L.T("Запустить", "Launch");
                }
                done = true;
            }
            catch (Exception ex)
            {
                statusText.Text = L.T("Не получилось: ", "Something went wrong: ") + ex.Message;
            }
            mainButton.IsEnabled = true;
            mainButton.Opacity = 1;
        }

        static ImageSource LoadIcon()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload/nowplaying.ico"))
                {
                    var dec = new IconBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    return dec.Frames.OrderByDescending(f => f.PixelWidth).First();
                }
            }
            catch { return null; }
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool silent = args.Contains("--silent");
            bool uninstall = args.Contains("--uninstall");
            int langAt = Array.IndexOf(args, "--lang");                 // --lang ru | --lang en
            if (langAt >= 0 && langAt + 1 < args.Length) L.En = args[langAt + 1] == "en";

            // Удаление запускается из папки программы — перезапускаемся из %TEMP%, чтобы папку можно было стереть.
            // Оригинал сразу закрывается, а копия ждёт его закрытия: работающий файл стереть нельзя.
            if (uninstall && !args.Contains("--from-temp"))
            {
                var self = Assembly.GetExecutingAssembly().Location;
                if (self.StartsWith(Product.Dir, StringComparison.OrdinalIgnoreCase))
                {
                    var tmp = Path.Combine(Path.GetTempPath(), "NowPlaying-Uninstall.exe");
                    File.Copy(self, tmp, true);
                    Process.Start(tmp, string.Join(" ", args) + " --from-temp --parent " + Process.GetCurrentProcess().Id);
                    return 0;
                }
            }
            int parentAt = Array.IndexOf(args, "--parent");
            if (parentAt >= 0 && parentAt + 1 < args.Length)
            {
                try { Process.GetProcessById(int.Parse(args[parentAt + 1])).WaitForExit(10000); } catch { }   // уже закрылся
            }

            if (silent)
            {
                try
                {
                    if (uninstall) Installer.Uninstall(args.Contains("--purge"));
                    else Installer.Install(!args.Contains("--no-desktop"), !args.Contains("--no-autostart"), s => Console.WriteLine(s));
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                    return 1;
                }
            }

            var app = new Application();
            return app.Run(new SetupWindow(uninstall));
        }
    }
}
