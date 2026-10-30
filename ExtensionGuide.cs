// Окно «Расширение для SoundCloud»: по шагам и с кнопками объясняет, как подключить расширение,
// и само показывает, когда расширение заработало (виджет получил от него первый отчёт).
// Открывается после установки (--extension-guide), один раз при первом запуске и из «Настроек» и трея.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace NowPlaying
{
    class ExtensionGuide : Window
    {
        static ExtensionGuide open;                      // одно окно на всех: повторный вызов просто выводит его вперёд
        readonly SoundCloudBridge bridge;
        readonly TextBlock statusDot, statusText;
        readonly DispatcherTimer timer;
        public static string Folder { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "soundcloud-extension"); } }

        public static void Show(SoundCloudBridge bridge)
        {
            if (open != null) { open.Activate(); return; }
            open = new ExtensionGuide(bridge);
            open.Closed += delegate { open = null; };
            open.Show();
        }

        // Браузеры на Chromium, которые нашлись на компьютере: название, exe и адрес страницы расширений.
        static List<string[]> Browsers()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                   local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var known = new[]
            {
                new[] { "Opera GX", "opera://extensions", Path.Combine(pf, @"Opera GX\opera.exe"), Path.Combine(pf, @"Opera GX\launcher.exe"), Path.Combine(local, @"Programs\Opera GX\opera.exe"), Path.Combine(local, @"Programs\Opera GX\launcher.exe"), Path.Combine(pf86, @"Opera GX\opera.exe") },
                new[] { "Opera", "opera://extensions", Path.Combine(pf, @"Opera\opera.exe"), Path.Combine(pf, @"Opera\launcher.exe"), Path.Combine(local, @"Programs\Opera\opera.exe"), Path.Combine(local, @"Programs\Opera\launcher.exe"), Path.Combine(pf86, @"Opera\opera.exe") },
                new[] { "Google Chrome", "chrome://extensions", Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"), Path.Combine(local, @"Google\Chrome\Application\chrome.exe") },
                new[] { "Edge", "edge://extensions", Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe") },
            };
            // найденные браузеры; Chrome показываем всегда — он самый частый (если не найден, exe будет пустым)
            var found = new List<string[]>();
            foreach (var b in known)
            {
                var exe = b.Skip(2).FirstOrDefault(File.Exists);
                if (exe != null || b[0] == "Google Chrome") found.Add(new[] { b[0], exe ?? "", b[1] });
            }
            return found;
        }

        ExtensionGuide(SoundCloudBridge bridge)
        {
            this.bridge = bridge;
            Title = L.T("Расширение для SoundCloud", "SoundCloud extension");
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Height; Width = 500;
            FontFamily = Ui.Text; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true; UseLayoutRounding = true;
            ShowInTaskbar = true;

            var body = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
            Content = new Border
            {
                Margin = new Thickness(20), CornerRadius = new CornerRadius(26), BorderBrush = Glass.Edge, BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Color.FromRgb(0x3A, 0x2A, 0x4A), Color.FromRgb(0x16, 0x16, 0x1A), 60),
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.5 }, Child = body
            };
            MouseLeftButtonDown += delegate { try { DragMove(); } catch (InvalidOperationException) { } };

            // шапка
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var names = new StackPanel();
            names.Children.Add(new TextBlock { Text = L.T("Расширение для SoundCloud", "SoundCloud extension"), Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold });
            names.Children.Add(new TextBlock
            {
                Text = L.T("Нужно один раз — тогда в плеере заработают повтор, перемешивание, лайк, громкость только трека и статистика прослушиваний.",
                           "Needed once — then repeat, shuffle, like, track-only volume and listening stats will work in the player."),
                Foreground = Ui.Soft, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
            });
            head.Children.Add(names);
            var close = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Ui.ButtonBg, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0), Child = Centered(Ui.Glyph("", 11, Brushes.White)) };
            close.MouseLeftButtonDown += (s, e) => e.Handled = true;
            close.MouseLeftButtonUp += (s, e) => Close();
            Grid.SetColumn(close, 1);
            head.Children.Add(close);
            body.Children.Add(head);

            // состояние: подключено или ещё нет (обновляется само)
            var status = new Border { CornerRadius = new CornerRadius(12), Background = Ui.ButtonBg, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 16, 0, 6) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            statusDot = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            statusText = new TextBlock { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(statusDot); sp.Children.Add(statusText); status.Child = sp;
            body.Children.Add(status);

            // шаг 1 — страница расширений в браузере
            var browsers = Browsers();
            // Браузеры не дают открыть свою внутреннюю страницу по команде извне (адрес уходит в поиск),
            // поэтому кнопка копирует адрес и открывает браузер, а подсказка говорит, куда его вставить.
            var s1 = new StackPanel();
            var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            hint1 = new TextBlock { Foreground = Ui.Brush(0xFF30D158), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
            foreach (var b in browsers)
            {
                var name = b[0]; var exe = b[1]; var url = b[2];
                buttons.Children.Add(Button(name, () => OpenExtensionsPage(name, exe, url)));
            }
            s1.Children.Add(buttons);
            s1.Children.Add(hint1);
            body.Children.Add(Step(1, L.T("Открой страницу расширений браузера", "Open your browser's extensions page"),
                L.T("Нажми на свой браузер: адрес страницы скопируется, а браузер откроется. Останется вставить адрес в адресную строку.",
                    "Click your browser: the page address is copied and the browser opens. Then just paste the address into the address bar."),
                s1));

            // шаг 2 — режим разработчика
            body.Children.Add(Step(2, L.T("Включи «Режим разработчика»", "Turn on Developer mode"),
                L.T("Переключатель вверху справа страницы расширений (в Opera — тоже вверху справа).", "The switch in the top-right corner of the extensions page."), null));

            // шаг 3 — загрузить распакованное
            var s3 = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            s3.Children.Add(Button(L.T("Скопировать путь к папке", "Copy folder path"), () =>
            {
                try { Clipboard.SetText(Folder); Flash(L.T("Путь скопирован — вставь его в окне выбора папки (Ctrl+V) и нажми Enter.", "Path copied — paste it into the folder dialog (Ctrl+V) and press Enter.")); } catch { }
            }));
            s3.Children.Add(Button(L.T("Открыть папку", "Open folder"), () => { try { Process.Start("explorer.exe", "\"" + Folder + "\""); } catch { } }));
            body.Children.Add(Step(3, L.T("Нажми «Загрузить распакованное» и выбери папку", "Click “Load unpacked” and choose the folder"),
                Folder, s3));

            // шаг 4 — обновить вкладку
            body.Children.Add(Step(4, L.T("Обнови вкладку SoundCloud (F5) и включи трек", "Reload the SoundCloud tab (F5) and play a track"),
                L.T("Как только расширение заработает, здесь вверху загорится «Готово».", "As soon as the extension works, “Done” will light up at the top."), null));

            var done = Button(L.T("Закрыть", "Close"), Close);
            done.HorizontalAlignment = HorizontalAlignment.Right; done.Margin = new Thickness(0, 16, 0, 0);
            body.Children.Add(done);

            Refresh();
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += delegate { Refresh(); };
            timer.Start();
            Closed += delegate { timer.Stop(); };
        }

        TextBlock flash;
        void Flash(string text) { if (flash != null) { flash.Text = text; flash.Visibility = Visibility.Visible; } }

        void Refresh()
        {
            bool on = bridge.Connected;
            statusDot.Text = on ? "●" : "○";
            statusDot.Foreground = on ? Ui.Brush(0xFF30D158) : Ui.Faint;
            statusText.Text = on ? L.T("Готово — расширение работает!", "Done — the extension works!")
                                 : L.T("Расширение ещё не подключено", "The extension is not connected yet");
        }

        TextBlock hint1;

        void OpenExtensionsPage(string name, string exe, string url)
        {
            try { Clipboard.SetText(url); } catch { }
            bool started = false;
            if (exe.Length > 0) { try { Process.Start(exe); started = true; } catch { } }
            hint1.Text = started
                ? L.T("Адрес " + url + " скопирован. В " + name + " нажми Ctrl+L, потом Ctrl+V и Enter.", "Address " + url + " copied. In " + name + " press Ctrl+L, then Ctrl+V and Enter.")
                : L.T(name + " не нашёлся на компьютере. Адрес " + url + " скопирован — открой браузер сам и вставь его в адресную строку (Ctrl+L, Ctrl+V, Enter).",
                      name + " was not found. Address " + url + " copied — open the browser yourself and paste it into the address bar (Ctrl+L, Ctrl+V, Enter).");
            hint1.Visibility = Visibility.Visible;
        }

        UIElement Step(int n, string title, string hint, UIElement extra)
        {
            var g = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var num = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.SoundCloud, VerticalAlignment = VerticalAlignment.Top, Child = Centered(new TextBlock { Text = n.ToString(), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Bold }) };
            g.Children.Add(num);
            var col = new StackPanel { Margin = new Thickness(12, 2, 0, 0) };
            col.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            col.Children.Add(new TextBlock { Text = hint, Foreground = Ui.Faint, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (extra != null) col.Children.Add(extra);
            if (n == 3) { flash = new TextBlock { Foreground = Ui.Brush(0xFF30D158), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed }; col.Children.Add(flash); }
            Grid.SetColumn(col, 1);
            g.Children.Add(col);
            return g;
        }

        static Border Button(string text, Action click)
        {
            var b = new Border { CornerRadius = new CornerRadius(14), Background = Ui.ButtonBg, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 4, 8, 0), Cursor = Cursors.Hand, Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold } };
            b.MouseEnter += delegate { b.Background = Ui.Brush(0x40FFFFFF); };
            b.MouseLeave += delegate { b.Background = Ui.ButtonBg; };
            b.MouseLeftButtonDown += (s, e) => e.Handled = true;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            return b;
        }

        static UIElement Centered(FrameworkElement e) { e.HorizontalAlignment = HorizontalAlignment.Center; e.VerticalAlignment = VerticalAlignment.Center; return e; }
    }
}
