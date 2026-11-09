// Компактный плеер поверх панели задач. Не внедряется в Explorer и не меняет панель задач.
using System;

using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;


using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace NowPlaying
{
    class TaskbarMini : Window
    {
        const string SettingsKey = @"Software\NowPlayingWidget";
        readonly Border cover;
        ImageSource lastArt;
        readonly TextBlock empty;
        readonly Button previous, play, next;
        readonly System.Windows.Shapes.Path playIcon;
        readonly DispatcherTimer timer;
        readonly Action restore;
        bool wanted, dragging, closing;
        int startMouse;
        double offset, startOffset;
        IntPtr handle;

        public TaskbarMini(Action restore, Action back, Action pause, Action forward)
        {
            this.restore = restore;
            Title = L.T("Сейчас играет — мини", "Now Playing — mini");
            Width = 144; Height = 38;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                    if (key != null) offset = Math.Max(0, Math.Min(4000, Convert.ToDouble(key.GetValue("MiniTaskbarOffset", "0"), System.Globalization.CultureInfo.InvariantCulture)));
            }
            catch { offset = 0; }

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            empty = Ui.Glyph("♪", 17, Ui.Soft);
            cover = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(7),
                Background = Ui.ButtonBg, Child = empty, Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand,
                ToolTip = L.T("Открыть плеер · потяни обложку, чтобы сдвинуть мини-плеер", "Open player · drag the artwork to move the mini player") };
            cover.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true; startMouse = Forms.Cursor.Position.X; startOffset = offset;
                dragging = false; cover.CaptureMouse();
            };
            cover.MouseMove += (s, e) =>
            {
                if (!cover.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
                int delta = Forms.Cursor.Position.X - startMouse;
                if (Math.Abs(delta) > 4) dragging = true;
                if (!dragging) return;
                offset = Math.Max(0, startOffset - delta / DeviceScale());
                Position();
            };
            cover.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (!cover.IsMouseCaptured) return;
                cover.ReleaseMouseCapture();
                if (dragging) SaveOffset(); else restore();
            };
            row.Children.Add(cover);
            System.Windows.Shapes.Path ignored;
            previous = Control(Ui.Prev, L.T("Предыдущий трек", "Previous track"), back, out ignored);
            play = Control(Ui.Play, L.T("Пауза / воспроизведение", "Pause / play"), pause, out playIcon);
            next = Control(Ui.Next, L.T("Следующий трек", "Next track"), forward, out ignored);
            row.Children.Add(previous); row.Children.Add(play); row.Children.Add(next);
            // Почти прозрачная подложка сохраняет правый клик и пропускает градиент панели задач.
            Content = new Border { CornerRadius = new CornerRadius(11), Background = Ui.Brush(0x01000000),
                BorderBrush = Ui.Brush(0xFF777877), BorderThickness = new Thickness(1), Padding = new Thickness(5, 3, 5, 3), Child = row };

            var menu = new ContextMenu();
            var open = new MenuItem { Header = L.T("Открыть плеер", "Open player") };
            open.Click += delegate { restore(); }; menu.Items.Add(open);
            var reset = new MenuItem { Header = L.T("Вернуть к трею", "Reset next to tray") };
            reset.Click += delegate { offset = 0; SaveOffset(); Position(); }; menu.Items.Add(reset);
            var hide = new MenuItem { Header = L.T("Убрать мини-плеер в трей", "Hide mini player to tray") };
            hide.Click += delegate { Dismiss(); }; menu.Items.Add(hide);
            ContextMenu = menu;
            SourceInitialized += delegate
            {
                handle = new WindowInteropHelper(this).Handle;
                // Кнопки не забирают фокус у приложения, в котором пользователь работает.
                SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000 | 0x00000080);
                HwndSource.FromHwnd(handle).AddHook(NoActivate);
            };
            Closing += (s, e) => { if (!closing) { e.Cancel = true; Dismiss(); } };
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            timer.Tick += delegate { if (wanted) Position(); };
            Update(null, "", false, false, false, false);
        }

        static Button Control(Geometry geometry, string hint, Action action, out System.Windows.Shapes.Path icon)
        {
            icon = new System.Windows.Shapes.Path { Data = geometry, Fill = Brushes.White,
                Stroke = Brushes.White, StrokeThickness = 0.7, Stretch = Stretch.Uniform, Width = 15, Height = 15 };
            var button = new Button { Width = 32, Height = 30, Padding = new Thickness(8),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Content = icon,
                Cursor = Cursors.Hand, Focusable = false, ToolTip = hint };
            // Шаблон без системной рамки; фон остаётся скруглённым при наведении.
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            button.MouseEnter += delegate { button.Background = Ui.Hover; };
            button.MouseLeave += delegate { button.Background = Brushes.Transparent; };
            button.IsEnabledChanged += delegate { button.Opacity = button.IsEnabled ? 1 : 0.3; };
            button.Click += delegate { action(); };
            return button;
        }

        public void Update(ImageSource art, string title, bool playing, bool canPlay, bool canBack, bool canNext)
        {
            if (!object.ReferenceEquals(lastArt, art))
            {
                lastArt = art;
                cover.Background = art == null ? (Brush)Ui.ButtonBg : new ImageBrush(art) { Stretch = Stretch.UniformToFill };
                cover.Child = art == null ? empty : null;
            }
            playIcon.Data = playing ? Ui.Pause : Ui.Play;
            play.ToolTip = playing ? L.T("Пауза", "Pause") : L.T("Воспроизвести", "Play");
            previous.IsEnabled = canBack; play.IsEnabled = canPlay; next.IsEnabled = canNext;
            ToolTip = string.IsNullOrEmpty(title) ? L.T("Ничего не играет", "Nothing is playing") : title;
        }

        public void Present()
        {
            wanted = true;

            // Создаём HWND без показа и кражи фокуса; Position решит, можно ли показывать окно.
            new WindowInteropHelper(this).EnsureHandle();
            Position(); timer.Start();
        }

        public void Dismiss() { wanted = false; timer.Stop(); Hide(); }
        public void Finish() { wanted = false; timer.Stop(); closing = true; Close(); }

        void SaveOffset()
        {
            try { using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey)) key.SetValue("MiniTaskbarOffset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            catch { }
        }

        double DeviceScale()
        {
            var source = PresentationSource.FromVisual(this);
            return source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformToDevice.M11 : 1;
        }

        // Чистая геометрия: справа всегда остаётся область трея; окно не уходит за левый край.
        internal static Rect Placement(Rect bar, Rect tray, double width, double height, double offset)
        {
            double right = Math.Min(bar.Right - 6, tray.Left - 6);
            double left = Math.Max(bar.Left + 4, right - width - Math.Max(0, offset));
            return new Rect(left, bar.Top + (bar.Height - height) / 2, width, height);
        }

        void Position()
        {
            if (!wanted) return;
            var taskbar = FindWindow("Shell_TrayWnd", null);
            NativeRect bar;
            if (taskbar == IntPtr.Zero || !IsWindowVisible(taskbar) || !GetWindowRect(taskbar, out bar)) { Hide(); return; }
            var screen = Forms.Screen.FromHandle(taskbar).Bounds;
            // Скрытая автоматически панель и полноэкранные приложения: поверх них ничего не рисуем.
            int visibleHeight = Math.Min(bar.Bottom, screen.Bottom) - Math.Max(bar.Top, screen.Top);
            if (visibleHeight < 12 || IsFullscreen(screen)) { Hide(); return; }
            NativeRect tray = new NativeRect();
            var trayHandle = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            bool found = trayHandle != IntPtr.Zero && GetWindowRect(trayHandle, out tray);
            if (!found) tray = new NativeRect { Left = bar.Right, Right = bar.Right, Top = bar.Top, Bottom = bar.Bottom };
            double scale = DeviceScale(), width = Width * scale, height = Height * scale;
            Rect target;
            if (found && bar.Right - bar.Left > bar.Bottom - bar.Top && tray.Left - bar.Left > width + 10)
                target = Placement(new Rect(bar.Left, bar.Top, bar.Right - bar.Left, bar.Bottom - bar.Top),
                    new Rect(tray.Left, tray.Top, Math.Max(1, tray.Right - tray.Left), Math.Max(1, tray.Bottom - tray.Top)), width, height, offset * scale);
            else
            {
                // Нестандартная панель: безопасное место над панелью, без перекрытия системных значков.
                var work = Forms.Screen.FromHandle(taskbar).WorkingArea;
                target = new Rect(work.Right - width - 8, work.Bottom - height - 8, width, height);
            }
            if (!IsVisible) Show();
            SetWindowPos(handle, new IntPtr(-1), (int)Math.Round(target.Left), (int)Math.Round(target.Top),
                (int)Math.Round(target.Width), (int)Math.Round(target.Height), 0x0010 | 0x0040);
        }

        bool IsFullscreen(System.Drawing.Rectangle screen)
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == handle) return false;
            var name = new StringBuilder(128); GetClassName(foreground, name, name.Capacity);
            if (name.ToString() == "Progman" || name.ToString() == "WorkerW" || name.ToString() == "Shell_TrayWnd") return false;
            NativeRect bounds;
            return GetWindowRect(foreground, out bounds) && bounds.Left <= screen.Left && bounds.Top <= screen.Top
                && bounds.Right >= screen.Right && bounds.Bottom >= screen.Bottom;
        }

        IntPtr NoActivate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE, но щелчок доставляется кнопке.
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string name, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string name, string title);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder value, int count);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
