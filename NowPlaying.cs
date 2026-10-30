// Виджет «Сейчас играет» в стиле экрана блокировки iOS.
// Показывает трек из любого плеера Windows (SoundCloud в браузере, Spotify, YouTube…)
// через системные медиа-сессии. Собирается штатным csc.exe (C# 5), см. build.ps1.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using RepeatMode = Windows.Media.MediaPlaybackAutoRepeatMode;

namespace NowPlaying
{
    static class WinRt
    {
        // Ожидание асинхронной операции Windows без System.Runtime.WindowsRuntime.
        public static Task<T> AsTask<T>(IAsyncOperation<T> op)
        {
            var tcs = new TaskCompletionSource<T>();
            op.Completed = (o, status) =>
            {
                if (status == AsyncStatus.Completed) tcs.TrySetResult(o.GetResults());
                else tcs.TrySetException(o.ErrorCode ?? new Exception(status.ToString()));
            };
            return tcs.Task;
        }

        public static async Task<byte[]> LoadBytes(IRandomAccessStreamReference reference)
        {
            using (var stream = await AsTask(reference.OpenReadAsync()))
            {
                uint size = (uint)stream.Size;
                if (size == 0) return null;
                var bytes = new byte[size];
                using (var reader = new DataReader(stream))
                {
                    await AsTask(reader.LoadAsync(size));
                    reader.ReadBytes(bytes);
                }
                return bytes;
            }
        }

        public static ImageSource Decode(byte[] bytes)
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = new MemoryStream(bytes);
            img.EndInit();
            img.Freeze();
            return img;
        }

        public static string Hash(byte[] bytes)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return Convert.ToBase64String(md5.ComputeHash(bytes));
        }
    }

    static class Ui
    {
        public static readonly FontFamily Text = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public static SolidColorBrush Brush(uint argb)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            b.Freeze();
            return b;
        }

        public static readonly SolidColorBrush Base = Brush(0xFF1C1C1E);
        public static readonly SolidColorBrush Soft = Brush(0xA6FFFFFF);
        public static readonly SolidColorBrush Faint = Brush(0x80FFFFFF);
        public static readonly SolidColorBrush TrackBg = Brush(0x40FFFFFF);
        public static readonly SolidColorBrush ButtonBg = Brush(0x26FFFFFF);
        public static readonly SolidColorBrush Hover = Brush(0x1FFFFFFF);
        // Включённая кнопка — белая с тёмным значком, как переключатели в «Пункте управления» iOS.
        public static readonly SolidColorBrush ActiveBg = Brush(0xFFFFFFFF);
        public static readonly SolidColorBrush ActiveGlyph = Brush(0xFF1C1C1E);
        public static readonly SolidColorBrush SoundCloud = Brush(0xFFFF5500);
        public static readonly SolidColorBrush ToggleOn = Brush(0x38FFFFFF);

        public const string ShuffleGlyph = "";
        public const string RepeatAllGlyph = "";
        public const string RepeatOneGlyph = "";
        public const string MoreGlyph = "";
        public const string LockGlyph = "";
        public const string UnlockGlyph = "";
        public const string HeartGlyph = "";
        public const string HeartFillGlyph = "";

        public static readonly Geometry Play = Geometry.Parse("M 3,1 L 3,21 L 19,11 Z");
        public static readonly Geometry Pause = Geometry.Parse("M 1,0 H 6 V 22 H 1 Z M 13,0 H 18 V 22 H 13 Z");
        public static readonly Geometry Next = Geometry.Parse("M 0,0 L 0,16 L 12,8 Z M 12,0 L 12,16 L 24,8 Z");
        public static readonly Geometry Prev = Geometry.Parse("M 24,0 L 24,16 L 12,8 Z M 12,0 L 12,16 L 0,8 Z");

        public static TextBlock Glyph(string glyph, double size, Brush fg)
        {
            return new TextBlock
            {
                Text = glyph, FontFamily = Icons, FontSize = size, Foreground = fg,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
        }

        // Рабочая область того монитора, где сейчас стоит окно (без панели задач), в единицах WPF.
        // SystemParameters.WorkArea — только основной монитор: с ним виджет со второго монитора
        // при любом изменении размера (например, от звука уведомления) прыгал на основной.
        public static Rect WorkAreaOf(Window w)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                var source = PresentationSource.FromVisual(w);
                if (hwnd == IntPtr.Zero || source == null || source.CompositionTarget == null) return SystemParameters.WorkArea;
                var px = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
                var m = source.CompositionTarget.TransformFromDevice;
                return new Rect(m.Transform(new Point(px.Left, px.Top)), m.Transform(new Point(px.Right, px.Bottom)));
            }
            catch { return SystemParameters.WorkArea; }
        }

        public static string Time(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                                     : string.Format("{0}:{1:00}", (int)t.TotalMinutes, t.Seconds);
        }
    }

    // Круглая кнопка с векторным значком (⏮ ⏯ ⏭).
    class ShapeButton : Border
    {
        readonly System.Windows.Shapes.Path path;
        public event Action Click;

        public ShapeButton(Geometry geometry, double size)
        {
            Width = size;
            Height = size;
            CornerRadius = new CornerRadius(size / 2);
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            path = new System.Windows.Shapes.Path
            {
                Data = geometry, Fill = Brushes.White, Stroke = Brushes.White, StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            Child = path;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = new ScaleTransform(1, 1);
            MouseEnter += delegate { Background = Ui.Hover; };
            MouseLeave += delegate { Background = Brushes.Transparent; Squeeze(1); };
            MouseLeftButtonDown += (s, e) => { e.Handled = true; Squeeze(0.88); };
            MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                Squeeze(1);
                if (IsEnabled && Click != null) Click();
            };
            IsEnabledChanged += delegate { Opacity = IsEnabled ? 1 : 0.35; };
        }

        public void SetGeometry(Geometry g)
        {
            if (path.Data != g) path.Data = g;
        }

        void Squeeze(double to)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120));
            RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
    }

    // Кнопка-переключатель 🔀 / 🔁: включённая подсвечивается, как в iOS.
    class GlyphToggle : Border
    {
        readonly TextBlock glyph;
        bool active;
        public event Action Click;

        public GlyphToggle(string g)
        {
            Width = 38;
            Height = 38;
            CornerRadius = new CornerRadius(11);
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            glyph = Ui.Glyph(g, 15, Brushes.White);
            Child = glyph;
            ToolTipService.SetShowOnDisabled(this, true);
            MouseLeftButtonDown += (s, e) => e.Handled = true;
            MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (IsEnabled && Click != null) Click();
            };
            IsEnabledChanged += delegate { ApplyLook(); };
            ApplyLook();
        }

        public void Set(string g, bool on)
        {
            glyph.Text = g;
            active = on;
            ApplyLook();
        }

        void ApplyLook()
        {
            Background = active && IsEnabled ? Ui.ToggleOn : Brushes.Transparent;
            glyph.Opacity = !IsEnabled ? 0.25 : active ? 1 : 0.55;
        }
    }

    // Мини-сервер на 127.0.0.1: через него расширение для Opera сообщает, включены ли
    // повтор и перемешивание на SoundCloud, и забирает команды нажать эти кнопки.
    class SoundCloudBridge
    {
        public const int Port = 48251;

        class TabState
        {
            public string Repeat;
            public bool Shuffle;
            public bool Playing;
            public bool CanLike;
            public bool Liked;
            public double MusicVolume = -1;      // громкость самого трека на странице (-1 — неизвестна)
            public DateTime Seen;
        }

        // Громкость трека на странице SoundCloud (только из расширения 1.2+).
        public bool TryGetMusicVolume(out double volume)
        {
            lock (sync)
            {
                var tab = ActiveTab();
                volume = tab != null ? tabs[tab.Value].MusicVolume : -1;
                return volume >= 0;
            }
        }

        // Команда громкости заменяет ещё не отправленную — пока тянешь ползунок, очередь не копится.
        public void SendVolume(double v)
        {
            lock (sync)
            {
                commands.RemoveAll(c => c.StartsWith("vol:"));
                commands.Add("vol:" + Math.Max(0, Math.Min(1, v)).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        public readonly ListenLog Listens = new ListenLog();   // статистика прослушиваний (расширение 1.3+)

        // Расширение хоть раз присылало отчёт — с этого запуска или раньше (помечено в реестре).
        public bool Connected
        {
            get
            {
                lock (sync) if (tabs.Count > 0) return true;
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\NowPlayingWidget"))
                        return k != null && "1".Equals(k.GetValue("ExtensionSeen"));
                }
                catch { return false; }
            }
        }
        bool seenSaved;
        readonly object sync = new object();
        readonly Dictionary<int, TabState> tabs = new Dictionary<int, TabState>();
        readonly List<string> commands = new List<string>();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public void Start()
        {
            new Thread(Listen) { IsBackground = true }.Start();
        }

        // Состояние вкладки SoundCloud, которая играет (или была активна последней).
        public bool TryGetState(out string repeat, out bool shuffle, out bool canLike, out bool liked)
        {
            lock (sync)
            {
                var tab = ActiveTab();
                var s = tab != null ? tabs[tab.Value] : new TabState { Repeat = "none" };
                repeat = s.Repeat;
                shuffle = s.Shuffle;
                canLike = s.CanLike;
                liked = s.Liked;
                return tab != null;
            }
        }

        public void Send(string command)
        {
            lock (sync)
            {
                if (commands.Count < 10) commands.Add(command);
            }
        }

        int? ActiveTab()
        {
            var fresh = tabs.Where(t => DateTime.Now - t.Value.Seen < TimeSpan.FromSeconds(3)).ToList();
            if (fresh.Count == 0) return null;
            var best = fresh.OrderByDescending(t => t.Value.Playing).ThenByDescending(t => t.Value.Seen).First();
            return best.Key;
        }

        void Listen()
        {
            TcpListener listener;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex); // порт занят — работаем без расширения
                return;
            }
            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); } catch { return; }
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 3000;
                    var stream = client.GetStream();
                    var reader = new StreamReader(stream, Encoding.UTF8);
                    var requestLine = (reader.ReadLine() ?? "").Split(' ');
                    int length = 0;
                    string line;
                    while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(line.Substring(15).Trim(), out length);
                    }
                    var body = new char[Math.Min(length, 4096)];
                    int read = 0;
                    while (read < body.Length)
                    {
                        int n = reader.Read(body, read, body.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }

                    string status = "200 OK", answer = "{\"ok\":true}";
                    if (requestLine.Length > 1 && requestLine[0] == "OPTIONS") { status = "204 No Content"; answer = ""; }
                    else if (requestLine.Length > 1 && requestLine[0] == "POST" && requestLine[1] == "/soundcloud")
                        answer = Report(new string(body, 0, read));
                    else if (requestLine.Length > 1 && requestLine[0] == "GET" && requestLine[1] == "/debug")
                        answer = DebugInfo();

                    var bytes = Encoding.UTF8.GetBytes(answer);
                    var head = "HTTP/1.1 " + status + "\r\nContent-Type: application/json\r\nAccess-Control-Allow-Origin: *\r\n" +
                               "Access-Control-Allow-Headers: *\r\nAccess-Control-Allow-Private-Network: true\r\n" +
                               "Content-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n";
                    var headBytes = Encoding.ASCII.GetBytes(head);
                    stream.Write(headBytes, 0, headBytes.Length);
                    stream.Write(bytes, 0, bytes.Length);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        // Для диагностики: что последним прислала каждая вкладка и какие команды ждут отправки.
        string DebugInfo()
        {
            lock (sync)
            {
                return json.Serialize(new Dictionary<string, object>
                {
                    { "active", ActiveTab() },
                    { "pending", commands.ToList() },
                    { "delivered", delivered.ToList() },
                    { "tabs", tabs.ToDictionary(t => t.Key.ToString(), t => (object)(lastBodies.ContainsKey(t.Key) ? lastBodies[t.Key] : "")) }
                });
            }
        }

        readonly Dictionary<int, string> lastBodies = new Dictionary<int, string>();
        readonly List<string> delivered = new List<string>();

        // Вкладка прислала своё состояние; если она активная — отдаём ей накопленные команды.
        string Report(string body)
        {
            var data = json.Deserialize<Dictionary<string, object>>(body);
            int tab = Convert.ToInt32(data["tab"]);
            lock (sync)
            {
                tabs[tab] = new TabState
                {
                    Repeat = Convert.ToString(data["repeat"]),
                    Shuffle = Convert.ToBoolean(data["shuffle"]),
                    Playing = Convert.ToBoolean(data["playing"]),
                    CanLike = data.ContainsKey("canLike") && Convert.ToBoolean(data["canLike"]), // старое расширение без лайков
                    Liked = data.ContainsKey("liked") && Convert.ToBoolean(data["liked"]),
                    MusicVolume = data.ContainsKey("musicVolume") && data["musicVolume"] != null ? Convert.ToDouble(data["musicVolume"], System.Globalization.CultureInfo.InvariantCulture) : -1,
                    Seen = DateTime.Now
                };
                lastBodies[tab] = DateTime.Now.ToString("HH:mm:ss.f") + " " + body;
                if (!seenSaved)
                {
                    seenSaved = true;   // запомнить, что расширение подключено, — инструкция больше не будет напрашиваться
                    try { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\NowPlayingWidget")) k.SetValue("ExtensionSeen", "1"); } catch { }
                }
                var reply = new List<string>();
                if (ActiveTab() == tab)
                {
                    object track, position;
                    data.TryGetValue("track", out track);
                    data.TryGetValue("position", out position);
                    Listens.Tick(track as Dictionary<string, object>, tabs[tab].Playing,
                        position != null ? Convert.ToDouble(position, System.Globalization.CultureInfo.InvariantCulture) : -1);
                    reply.AddRange(commands);
                    foreach (var c in commands) delivered.Add(DateTime.Now.ToString("HH:mm:ss.f") + " " + c + " → tab " + tab);
                    if (delivered.Count > 20) delivered.RemoveRange(0, delivered.Count - 20);
                    commands.Clear();
                }
                return json.Serialize(new Dictionary<string, object> { { "commands", reply } });
            }
        }
    }

    // Обёртка для фона: занимает всё место, которое ей дали, но сама размер не просит.
    // Иначе большая обложка растягивает виджет, когда окно подстраивается под содержимое.
    class FillOnly : Decorator
    {
        protected override Size MeasureOverride(Size constraint)
        {
            if (Child != null) Child.Measure(new Size(0, 0));
            return new Size(0, 0);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Child != null) Child.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }

    // Уголок ↘ для изменения размера: тянешь — виджет пропорционально растёт или сжимается.
    class ResizeGrip : Border
    {
        Point start;
        double startWidth;
        bool dragging;

        public event Action DragStarted;
        public event Action<double> Dragged;   // во сколько раз виджет шире, чем в начале перетаскивания
        public event Action DragFinished;

        public ResizeGrip()
        {
            Width = 24;
            Height = 24;
            Background = Brushes.Transparent;
            Cursor = Cursors.SizeNWSE;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Bottom;
            ToolTip = L.T("Потяните, чтобы изменить размер (или Ctrl + колёсико)", "Drag to resize (or Ctrl + mouse wheel)");
            Child = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 14,4 L 4,14 M 14,9 L 9,14"), Stroke = Ui.Brush(0x99FFFFFF), StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                var w = Window.GetWindow(this);
                start = w.PointToScreen(e.GetPosition(w));
                startWidth = w.ActualWidth;
                dragging = true;
                CaptureMouse();
                if (DragStarted != null) DragStarted();
            };
            MouseMove += (s, e) =>
            {
                if (!dragging) return;
                var w = Window.GetWindow(this);
                var p = w.PointToScreen(e.GetPosition(w));
                double dpi = PresentationSource.FromVisual(w).CompositionTarget.TransformToDevice.M11;
                double delta = ((p.X - start.X) + (p.Y - start.Y)) / 2 / dpi;
                if (Dragged != null) Dragged((startWidth + delta) / startWidth);
            };
            MouseLeftButtonUp += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                ReleaseMouseCapture();
                if (DragFinished != null) DragFinished();
            };
            LostMouseCapture += delegate { dragging = false; };
        }
    }

    // Полоска прогресса: тонкая, толстеет при наведении, по клику перематывает.
    class SeekBar : Grid
    {
        readonly Border track;
        readonly Border fill;
        readonly ScaleTransform scale = new ScaleTransform(0, 1);
        bool dragging;
        double value;

        public event Action<double> Seek;
        public bool IsDragging { get { return dragging; } }
        public bool Moved { get; private set; }   // в последнем нажатии ползунок тащили, а не просто кликнули
        double downX;

        public SeekBar()
        {
            Height = 16;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            track = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = Ui.TrackBg, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
            fill = new Border { Background = Brushes.White, RenderTransform = scale, RenderTransformOrigin = new Point(0, 0.5) };
            track.Child = fill;
            Children.Add(track);
            track.SizeChanged += delegate { track.Clip = new RectangleGeometry(new Rect(track.RenderSize), track.Height / 2, track.Height / 2); };

            MouseEnter += delegate { Grow(9); };
            MouseLeave += delegate { if (!dragging) Grow(5); };
            MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                dragging = true;
                Moved = false;
                downX = e.GetPosition(this).X;
                CaptureMouse();
                Drag(e);
            };
            MouseMove += (s, e) =>
            {
                if (!dragging) return;
                if (Math.Abs(e.GetPosition(this).X - downX) > 3) Moved = true;   // отличаем перетаскивание от клика
                Drag(e);
            };
            MouseLeftButtonUp += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                ReleaseMouseCapture();
                if (!IsMouseOver) Grow(5);
                if (Seek != null) Seek(value);
            };
            LostMouseCapture += delegate { dragging = false; };
        }

        public event Action<double> Preview;

        public void Set(double v)
        {
            if (dragging) return;
            value = Math.Max(0, Math.Min(1, v));
            scale.ScaleX = value;
        }

        void Drag(MouseEventArgs e)
        {
            if (ActualWidth <= 0) return;
            value = Math.Max(0, Math.Min(1, e.GetPosition(this).X / ActualWidth));
            scale.ScaleX = value;
            if (Preview != null) Preview(value);
        }

        void Grow(double h)
        {
            track.BeginAnimation(HeightProperty, new DoubleAnimation(h, TimeSpan.FromMilliseconds(140)));
        }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "NowPlayingWidget";

        public static bool Enabled
        {
            get
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(ValueName) != null;
            }
            set
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(ValueName, "\"" + Process.GetCurrentProcess().MainModule.FileName + "\" --hidden");
                    else k.DeleteValue(ValueName, false);
                }
            }
        }
    }

    class PlayerWindow : Window
    {
        const string SettingsKey = @"Software\NowPlayingWidget";

        readonly Image backdrop;
        readonly Border artwork;
        readonly TextBlock placeholder;
        readonly TextBlock sourceText;
        readonly TextBlock titleText;
        readonly TextBlock artistText;
        readonly SeekBar progress;
        readonly Grid timeRow;
        readonly TextBlock elapsedText;
        readonly TextBlock remainingText;
        readonly ShapeButton prevButton;
        readonly ShapeButton playButton;
        readonly ShapeButton nextButton;
        readonly GlyphToggle shuffleButton;
        readonly GlyphToggle repeatButton;
        readonly SoundCloudBridge bridge = new SoundCloudBridge();
        readonly Border heartButton;
        readonly TextBlock heartGlyph;
        readonly Border openButton;
        readonly StackPanel cornerButtons;
        readonly Border pinButton;
        readonly TextBlock pinGlyph;
        readonly Forms.NotifyIcon tray;
        readonly Forms.ToolStripMenuItem topmostItem;
        readonly Forms.ToolStripMenuItem lockItem;
        readonly Border lockButton;
        readonly TextBlock lockGlyph;
        readonly Border menuPanel;
        readonly SeekBar blurBar;
        TextBlock autoCheck;                       // галочка «Запускать вместе с Windows» в «Настройках»
        Forms.ToolStripMenuItem autoItem;          // тот же пункт в меню трея
        System.Windows.Controls.Primitives.Popup settingsPopup;   // «Настройки» — окошко справа от меню «⋯»
        DateTime settingsClosedAt;
        double topBeforeMixer = double.NaN;        // где был плеер до раскрытия микшера (NaN — не запомнено)
        double pendingTop = double.NaN;            // куда вернуть плеер, когда окно уменьшится после сворачивания
        readonly TextBlock blurText;
        readonly BlurEffect blur = new BlurEffect { Radius = 70, RenderingBias = RenderingBias.Performance };
        const double MaxBlur = 120;
        double blurAmount = 70 / MaxBlur, savedBlur = 70 / MaxBlur;
        readonly SoundPanel sound = new SoundPanel();
        readonly Grid card;
        readonly System.Windows.Shapes.Path shadowShape, edgeShape;
        Size lastCardSize;
        double lastSplit = -1;
        readonly ResizeGrip grip;
        readonly ScaleTransform zoom = new ScaleTransform(1, 1);
        const double BaseWidth = 400, MinScale = 0.6, MaxScale = 1.8;
        double scale = 1, dragStartScale, savedScale = 1;
        bool locked, savedLocked;

        GlobalSystemMediaTransportControlsSession session;
        GlobalSystemMediaTransportControlsSessionManager manager;
        string trackKey;
        bool playing, busy, hintShown, exiting;
        string artHash;
        DateTime artWatchUntil, nextArtCheck;
        TimeSpan duration;
        bool viaBridge, shuffleOn, liked;
        string repeatMode = "none";
        DateTime modesLockedUntil, likeLockedUntil;

        public PlayerWindow()
        {
            Title = L.T("Сейчас играет", "Now Playing");
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = Ui.Text;
            UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            RestorePosition();

            // Слои: тень → размытая обложка → затемнение → содержимое.
            // Тень и кайма повторяют форму карточки: одна целая или две (плеер + микшер) с зазором.
            shadowShape = new System.Windows.Shapes.Path
            {
                Fill = Ui.Base,
                Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 8, Direction = 270, Opacity = 0.5, Color = Colors.Black }
            };
            edgeShape = new System.Windows.Shapes.Path { Stroke = Ui.Brush(0x22FFFFFF), StrokeThickness = 1.5, IsHitTestVisible = false };
            var shadow = new FillOnly               // не влияет на размер окна: иначе форма «распирает» его при каждом пересчёте
            {
                Child = shadowShape
            };
            card = new Grid();
            card.LayoutUpdated += delegate { UpdateCardShape(); };
            card.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromRgb(0x3A, 0x2A, 0x4A), Color.FromRgb(0x1C, 0x1C, 0x1E), 60) });
            backdrop = new Image { Stretch = Stretch.UniformToFill, Effect = blur, Margin = new Thickness(-60) };
            card.Children.Add(new FillOnly { Child = backdrop });
            card.Children.Add(new Border
            {
                Background = new LinearGradientBrush(Color.FromArgb(0x85, 0, 0, 0), Color.FromArgb(0xB8, 0, 0, 0), 90),
                CornerRadius = new CornerRadius(32)
            });

            var content = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };

            // Верхняя строка: источник и кнопки «закрепить» / «скрыть».
            var top = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sourceText = new TextBlock { Foreground = Ui.Faint, FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            top.Children.Add(sourceText);
            cornerButtons = new StackPanel { Orientation = Orientation.Horizontal, Opacity = 0 };
            Grid.SetColumn(cornerButtons, 1);
            TextBlock closeGlyph;
            pinButton = CircleButton("", L.T("Поверх всех окон", "Always on top"), () => SetTopmost(!Topmost), out pinGlyph);
            cornerButtons.Children.Add(pinButton);
            lockButton = CircleButton(Ui.UnlockGlyph, L.T("Замок: заморозить место и размер", "Lock position and size"), () => SetLocked(!locked), out lockGlyph);
            cornerButtons.Children.Add(lockButton);
            cornerButtons.Children.Add(CircleButton(Ui.MoreGlyph, L.T("Ещё", "More"), ToggleMenu, out closeGlyph));
            top.Children.Add(cornerButtons);
            content.Children.Add(top);

            // Обложка и название.
            var info = new Grid();
            info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            info.ColumnDefinitions.Add(new ColumnDefinition());
            info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heartGlyph = Ui.Glyph(Ui.HeartGlyph, 18, Brushes.White);
            heartButton = new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = heartGlyph, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, -8, 0),
                Visibility = Visibility.Collapsed, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
            };
            heartButton.MouseEnter += delegate { heartButton.Background = Ui.Hover; };
            heartButton.MouseLeave += delegate { heartButton.Background = Brushes.Transparent; };
            heartButton.MouseLeftButtonDown += (s, e) => e.Handled = true;
            heartButton.MouseLeftButtonUp += (s, e) => { e.Handled = true; ToggleLike(); };
            Grid.SetColumn(heartButton, 2);
            info.Children.Add(heartButton);
            artwork = new Border { Width = 78, Height = 78, CornerRadius = new CornerRadius(14), Background = Ui.ButtonBg };
            // Тень обложки — отдельный слой, чтобы сама обложка оставалась чёткой при увеличении виджета.
            info.Children.Add(new Border
            {
                Width = 78, Height = 78, CornerRadius = new CornerRadius(14), Background = Brushes.Black,
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.45 }
            });
            placeholder = Ui.Glyph("", 30, Ui.Soft);
            artwork.Child = placeholder;
            info.Children.Add(artwork);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            titleText = new TextBlock { Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            artistText = new TextBlock { Foreground = Ui.Soft, FontSize = 14, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            names.Children.Add(titleText);
            names.Children.Add(artistText);
            Grid.SetColumn(names, 1);
            info.Children.Add(names);
            content.Children.Add(info);

            // Прогресс и время.
            progress = new SeekBar {Margin = new Thickness(0, 18, 0, 0) };
            progress.Preview += v => ShowTimes(TimeSpan.FromTicks((long)(duration.Ticks * v)));
            progress.Seek += v => SeekTo(v);
            content.Children.Add(progress);
            timeRow = new Grid();
            elapsedText = new TextBlock { Foreground = Ui.Faint, FontSize = 11.5 };
            remainingText = new TextBlock { Foreground = Ui.Faint, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right };
            timeRow.Children.Add(elapsedText);
            timeRow.Children.Add(remainingText);
            content.Children.Add(timeRow);

            // Кнопки управления.
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            shuffleButton = new GlyphToggle(Ui.ShuffleGlyph) { Margin = new Thickness(0, 0, 10, 0) };
            prevButton = new ShapeButton(Ui.Prev, 50);
            playButton = new ShapeButton(Ui.Play, 62) { Margin = new Thickness(14, 0, 14, 0) };
            nextButton = new ShapeButton(Ui.Next, 50);
            repeatButton = new GlyphToggle(Ui.RepeatAllGlyph) { Margin = new Thickness(10, 0, 0, 0) };
            prevButton.Click += () => Command(s => s.TrySkipPreviousAsync());
            nextButton.Click += () => Command(s => s.TrySkipNextAsync());
            playButton.Click += TogglePlay;
            shuffleButton.Click += ToggleShuffle;
            repeatButton.Click += CycleRepeat;
            controls.Children.Add(shuffleButton);
            controls.Children.Add(prevButton);
            controls.Children.Add(playButton);
            controls.Children.Add(nextButton);
            controls.Children.Add(repeatButton);
            content.Children.Add(controls);

            // Кнопка «Открыть SoundCloud», когда ничего не играет.
            openButton = new Border
            {
                Background = Ui.SoundCloud, CornerRadius = new CornerRadius(18), Height = 36, Margin = new Thickness(0, 12, 0, 2),
                Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(20, 0, 20, 0),
                Child = new TextBlock { Text = L.T("Открыть SoundCloud", "Open SoundCloud"), Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }
            };
            openButton.MouseLeftButtonDown += (s, e) => e.Handled = true;
            openButton.MouseLeftButtonUp += (s, e) => { e.Handled = true; OpenSoundCloud(); };
            content.Children.Add(openButton);
            content.Children.Add(sound);     // громкость играющего приложения и микшер под стрелочкой

            card.Children.Add(content);
            // Уголок для изменения размера появляется при наведении, пока замок открыт.
            grip = new ResizeGrip { Margin = new Thickness(0, 0, 8, 8), Opacity = 0 };
            grip.DragStarted += () => dragStartScale = scale;
            grip.Dragged += f => ApplyScale(dragStartScale * f);
            grip.DragFinished += () => { KeepOnScreen(); SavePosition(); };
            SizeChanged += (s, e) =>
            {
                // микшер свернули и окно уменьшилось — плеер возвращается туда, где был до раскрытия
                if (!double.IsNaN(pendingTop) && e.HeightChanged && e.NewSize.Height < e.PreviousSize.Height)
                {
                    Top = pendingTop;
                    pendingTop = double.NaN;
                    SavePosition();
                }
                if (Mouse.LeftButton != MouseButtonState.Pressed) KeepOnScreen();
            };
            card.Children.Add(grip);

            // Меню «⋯»: ползунок размытия фона и кнопка «Закрыть виджет».
            var menuStack = new StackPanel();
            var blurHeader = new Grid();
            blurHeader.Children.Add(new TextBlock { Text = L.T("Размытие фона", "Background blur"), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold });
            blurText = new TextBlock { Foreground = Ui.Faint, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            blurHeader.Children.Add(blurText);
            menuStack.Children.Add(blurHeader);
            blurBar = new SeekBar { Margin = new Thickness(0, 6, 0, 4) };
            blurBar.Preview += v => SetBlur(v);
            blurBar.Seek += v =>
            {
                SetBlur(v);
                SavePosition();
                if (blurBar.Moved) menuPanel.Visibility = Visibility.Collapsed;   // дотащили и отпустили — меню закрывается, простой клик его оставляет
            };
            menuStack.Children.Add(blurBar);
            menuStack.Children.Add(new Border { Height = 1, Background = Ui.ButtonBg, Margin = new Thickness(-14, 4, -14, 6) });
            // «Статистика»: страница с прослушиваниями SoundCloud в браузере
            var statsRow = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(-8, 0, -8, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var statsContent = new StackPanel { Orientation = Orientation.Horizontal };
            statsContent.Children.Add(Ui.Glyph("", 11, Brushes.White));
            statsContent.Children.Add(new TextBlock { Text = L.T("Статистика прослушиваний", "Listening stats"), Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(10, 0, 0, 0) });
            statsRow.Child = statsContent;
            statsRow.MouseEnter += delegate { statsRow.Background = Ui.Hover; };
            statsRow.MouseLeave += delegate { statsRow.Background = Brushes.Transparent; };
            statsRow.MouseLeftButtonDown += (s, e) => e.Handled = true;
            statsRow.MouseLeftButtonUp += (s, e) => { e.Handled = true; menuPanel.Visibility = Visibility.Collapsed; OpenStats(); };
            menuStack.Children.Add(statsRow);
            // «Скрыть плеер»: на экране остаётся только микшер (кнопка ♪ в нём или значок в трее вернут плеер)
            var hideRow = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(-8, 0, -8, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var hideContent = new StackPanel { Orientation = Orientation.Horizontal };
            hideContent.Children.Add(Ui.Glyph("", 11, Brushes.White));
            hideContent.Children.Add(new TextBlock { Text = L.T("Скрыть плеер", "Hide player"), Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(10, 0, 0, 0) });
            hideRow.Child = hideContent;
            hideRow.MouseEnter += delegate { hideRow.Background = Ui.Hover; };
            hideRow.MouseLeave += delegate { hideRow.Background = Brushes.Transparent; };
            hideRow.MouseLeftButtonDown += (s, e) => e.Handled = true;
            hideRow.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                menuPanel.Visibility = Visibility.Collapsed;
                sound.KeepMixerVisible();
                Hide();
            };
            menuStack.Children.Add(hideRow);

            // «Настройки»: раскрывающийся раздел — автозапуск и удаление с компьютера
            var settingsChevron = Ui.Glyph("", 9, Ui.Faint);
            var settingsRow = MenuRow("", L.T("Настройки", "Settings"), Brushes.White, settingsChevron);
            // сам раздел — отдельная карточка справа от меню, в том же «тёмном стекле»
            var settingsBox = new StackPanel();
            var settingsCard = new Border
            {
                Width = 230, Background = Ui.Brush(0xE61C1C20), BorderBrush = Glass.Edge, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 10, 14, 8), Child = settingsBox
            };
            settingsCard.MouseLeftButtonDown += (s, e) => e.Handled = true;
            settingsPopup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = settingsRow, Placement = System.Windows.Controls.Primitives.PlacementMode.Right,
                HorizontalOffset = 22, VerticalOffset = -10, AllowsTransparency = true, StaysOpen = false, Child = settingsCard
            };
            settingsPopup.Closed += delegate { settingsChevron.Text = ""; settingsClosedAt = DateTime.Now; };
            settingsBox.Children.Add(new TextBlock { Text = L.T("Настройки", "Settings"), Foreground = Ui.Faint, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            settingsRow.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                // клик по строке при открытом окне сначала закрывает его (StaysOpen = false) — не открываем заново
                if (settingsPopup.IsOpen || (DateTime.Now - settingsClosedAt).TotalMilliseconds < 300) { settingsPopup.IsOpen = false; return; }
                autoCheck.Text = Autostart.Enabled ? "" : "";
                settingsCard.LayoutTransform = zoom;    // тот же масштаб, что у виджета
                settingsChevron.Text = "";
                settingsPopup.IsOpen = true;
            };
            menuStack.Children.Add(settingsRow);
            autoCheck = Ui.Glyph(Autostart.Enabled ? "" : "", 11, Brushes.White);
            var autoRow = MenuRow("", L.T("Запускать вместе с Windows", "Start with Windows"), Brushes.White, autoCheck);
            autoRow.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                Autostart.Enabled = !Autostart.Enabled;
                autoCheck.Text = Autostart.Enabled ? "" : "";
                if (autoItem != null) autoItem.Checked = Autostart.Enabled;
            };
            settingsBox.Children.Add(autoRow);
            // «Расширение для SoundCloud»: справа галочка, если подключено, иначе восклицательный знак
            var extMark = Ui.Glyph("", 11, Brushes.White);
            var extRow = MenuRow("", L.T("Расширение для SoundCloud", "SoundCloud extension"), Brushes.White, extMark);
            extRow.MouseLeftButtonUp += (s, e) => { e.Handled = true; settingsPopup.IsOpen = false; menuPanel.Visibility = Visibility.Collapsed; ExtensionGuide.Show(bridge); };
            settingsBox.Children.Add(extRow);
            settingsCard.IsVisibleChanged += delegate
            {
                bool on = bridge.Connected;
                extMark.Text = on ? "" : "";
                extMark.Foreground = on ? Brushes.White : Ui.Brush(0xFFFFB340);
            };
            var removeColor = Ui.Brush(0xFFFF453A);
            var removeRow = MenuRow("", L.T("Удалить с компьютера", "Uninstall"), removeColor, null);
            removeRow.MouseEnter += delegate { removeRow.Background = Ui.Brush(0x26FF453A); };
            removeRow.MouseLeftButtonUp += (s, e) => { e.Handled = true; settingsPopup.IsOpen = false; menuPanel.Visibility = Visibility.Collapsed; UninstallApp(); };
            settingsBox.Children.Add(removeRow);

            var closeRow = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(-8, 0, -8, -6), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var closeContent = new StackPanel { Orientation = Orientation.Horizontal };
            closeContent.Children.Add(Ui.Glyph("", 11, Brushes.White));
            closeContent.Children.Add(new TextBlock { Text = L.T("Свернуть в трей", "Minimize to tray"), Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(10, 0, 0, 0) });
            closeRow.Child = closeContent;
            closeRow.MouseEnter += delegate { closeRow.Background = Ui.Hover; };
            closeRow.MouseLeave += delegate { closeRow.Background = Brushes.Transparent; };
            closeRow.MouseLeftButtonDown += (s, e) => e.Handled = true;
            closeRow.MouseLeftButtonUp += (s, e) => { e.Handled = true; menuPanel.Visibility = Visibility.Collapsed; HideWidget(); };
            closeRow.Margin = new Thickness(-8, 0, -8, 2);
            menuStack.Children.Add(closeRow);

            // «Выйти» — полностью выключает виджет (как «Выход» в трее); красная, как в iOS
            var exitColor = Ui.Brush(0xFFFF453A);
            var exitRow = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(-8, 0, -8, -6), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var exitContent = new StackPanel { Orientation = Orientation.Horizontal };
            exitContent.Children.Add(Ui.Glyph("", 11, exitColor));
            exitContent.Children.Add(new TextBlock { Text = L.T("Выйти", "Quit"), Foreground = exitColor, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0) });
            exitRow.Child = exitContent;
            exitRow.MouseEnter += delegate { exitRow.Background = Ui.Brush(0x26FF453A); };
            exitRow.MouseLeave += delegate { exitRow.Background = Brushes.Transparent; };
            exitRow.MouseLeftButtonDown += (s, e) => e.Handled = true;
            exitRow.MouseLeftButtonUp += (s, e) => { e.Handled = true; ExitApp(); };
            menuStack.Children.Add(exitRow);
            menuPanel = new Border
            {
                Width = 210, Background = Ui.Brush(0xB01C1C20), BorderBrush = Glass.Edge, BorderThickness = new Thickness(1),   // тёмное стекло
                CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 12, 14, 12),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 52, 18, 0), Visibility = Visibility.Collapsed, Child = menuStack
            };
            menuPanel.MouseLeftButtonDown += (s, e) => e.Handled = true;   // клик по меню не двигает окно
            menuPanel.IsVisibleChanged += delegate { if (!menuPanel.IsVisible) settingsPopup.IsOpen = false; };   // меню закрылось — и «Настройки» тоже
            card.Children.Add(menuPanel);
            var root = new Grid { Margin = new Thickness(18) };
            root.Children.Add(shadow);
            root.Children.Add(card);
            root.Children.Add(new FillOnly { Child = edgeShape, IsHitTestVisible = false });
            // Весь виджет рисуется в «базовом» размере и масштабируется целиком.
            Content = new Grid { Width = BaseWidth, LayoutTransform = zoom, Children = { root } };

            MouseEnter += delegate
            {
                cornerButtons.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
                if (!locked) grip.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
            };
            MouseLeave += delegate
            {
                cornerButtons.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
                grip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
            };
            MouseLeftButtonDown += delegate
            {
                if (menuPanel.Visibility == Visibility.Visible) { menuPanel.Visibility = Visibility.Collapsed; return; }
                if (locked) return;
                double before = Top;
                try { DragMove(); } catch (InvalidOperationException) { }
                if (Math.Abs(Top - before) > 1) topBeforeMixer = double.NaN;   // сам передвинул — после микшера не возвращаем
                SavePosition();
            };
            PreviewMouseWheel += (s, e) =>
            {
                if (locked || Keyboard.Modifiers != ModifierKeys.Control) return;
                e.Handled = true;
                ApplyScale(scale + (e.Delta > 0 ? 0.05 : -0.05));
                SavePosition();
            };

            // Значок в трее.
            tray = new Forms.NotifyIcon { Text = L.T("Сейчас играет", "Now Playing"), Icon = LoadIcon(), Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) Toggle(); };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(L.T("Показать / скрыть", "Show / hide"), null, delegate { Toggle(); });
            menu.Items.Add(L.T("Открыть SoundCloud", "Open SoundCloud"), null, delegate { OpenSoundCloud(); });
            menu.Items.Add(L.T("Статистика прослушиваний", "Listening stats"), null, delegate { OpenStats(); });
            menu.Items.Add(L.T("Как подключить расширение для SoundCloud", "How to connect the SoundCloud extension"), null, delegate { ExtensionGuide.Show(bridge); });
            topmostItem = new Forms.ToolStripMenuItem(L.T("Поверх всех окон", "Always on top")) { CheckOnClick = true };
            topmostItem.CheckedChanged += delegate { SetTopmost(topmostItem.Checked); };
            menu.Items.Add(topmostItem);
            lockItem = new Forms.ToolStripMenuItem(L.T("Замок (заморозить место и размер)", "Lock (freeze position and size)")) { CheckOnClick = true };
            lockItem.CheckedChanged += delegate { SetLocked(lockItem.Checked); };
            menu.Items.Add(lockItem);
            autoItem = new Forms.ToolStripMenuItem(L.T("Запускать вместе с Windows", "Start with Windows")) { Checked = Autostart.Enabled, CheckOnClick = true };
            autoItem.CheckedChanged += delegate { Autostart.Enabled = autoItem.Checked; };
            menu.Items.Add(autoItem);
            // язык: смена перезапускает виджет — так все надписи сразу на новом языке
            var langItem = new Forms.ToolStripMenuItem("Язык / Language");
            foreach (var lang in new[] { new[] { "ru", "Русский" }, new[] { "en", "English" } })
            {
                string code = lang[0];
                var item = new Forms.ToolStripMenuItem(lang[1]) { Checked = L.Code == code };
                item.Click += delegate { if (code != L.Code) { L.Save(code); Restart(); } };
                langItem.DropDownItems.Add(item);
            }
            menu.Items.Add(langItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(L.T("Выход", "Exit"), null, delegate { ExitApp(); });
            tray.ContextMenuStrip = menu;

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && menuPanel.Visibility == Visibility.Visible) menuPanel.Visibility = Visibility.Collapsed;
                else if (e.Key == Key.Escape) HideWidget();
                else if (e.Key == Key.Space && playButton.IsEnabled) TogglePlay();
            };
            Closing += (s, e) => { if (!exiting) { e.Cancel = true; HideWidget(); } };
            SetTopmost(true);
            ApplyScale(savedScale);
            SetLocked(savedLocked);
            SetBlur(savedBlur);
            sound.Setup(this, zoom);
            sound.Bridge = bridge;           // громкость только трека на SoundCloud — через расширение
            sound.ShowPlayerRequested += ShowWidget;
            sound.PlayerMoved += () => { KeepOnScreen(); SavePosition(); };
            sound.ExpandedChanged += OnMixerToggled;
            ShowEmpty();
            bridge.Start();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            int ticks = 0;
            timer.Tick += delegate
            {
                Refresh();
                if (ticks++ % 2 == 0) sound.Refresh(session != null ? session.SourceAppUserModelId : null);   // звук — раз в секунду
            };
            timer.Start();
            Refresh();
        }

        Border CircleButton(string glyph, string tip, Action click, out TextBlock text)
        {
            var t = Ui.Glyph(glyph, 11, Brushes.White);
            var b = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Ui.ButtonBg, Child = t, Cursor = Cursors.Hand, ToolTip = tip, Margin = new Thickness(8, 0, 0, 0) };
            b.MouseLeftButtonDown += (s, e) => e.Handled = true;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            text = t;
            return b;
        }

        void SetTopmost(bool on)
        {
            Topmost = on;
            pinGlyph.Text = on ? "" : "";
            pinButton.Background = on ? Ui.ActiveBg : Ui.ButtonBg;
            pinGlyph.Foreground = on ? Ui.ActiveGlyph : Brushes.White;
            if (topmostItem != null) topmostItem.Checked = on;
        }

        async void Refresh()
        {
            if (busy) return;
            busy = true;
            try
            {
                if (manager == null) manager = await WinRt.AsTask(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                session = PickSession();
                if (session == null) { ShowEmpty(); return; }

                var props = await WinRt.AsTask(session.TryGetMediaPropertiesAsync());
                if (session == null) return;
                var playback = session.GetPlaybackInfo();
                var timeline = session.GetTimelineProperties();

                string key = (props.Title ?? "") + "\n" + (props.Artist ?? "");
                bool newTrack = key != trackKey;
                if (newTrack)
                {
                    trackKey = key;
                    titleText.Text = string.IsNullOrWhiteSpace(props.Title) ? L.T("Без названия", "Untitled") : props.Title;
                    artistText.Text = !string.IsNullOrWhiteSpace(props.Artist) ? props.Artist : (props.AlbumArtist ?? "");
                    // Браузер присылает новое название раньше новой обложки — первые секунды
                    // перепроверяем картинку на каждом обновлении.
                    artWatchUntil = DateTime.Now.AddSeconds(8);
                }
                if (props.Thumbnail == null)
                {
                    if (artHash != null || newTrack) { artHash = null; SetArtwork(null, newTrack); }
                }
                else if (newTrack || DateTime.Now < artWatchUntil || DateTime.Now >= nextArtCheck)
                {
                    nextArtCheck = DateTime.Now.AddSeconds(3);
                    byte[] bytes = null;
                    try { bytes = await WinRt.LoadBytes(props.Thumbnail); } catch (Exception ex) { Debug.WriteLine(ex); }
                    if (bytes != null && key == trackKey)
                    {
                        string hash = WinRt.Hash(bytes);
                        if (hash != artHash)
                        {
                            artHash = hash;
                            SetArtwork(WinRt.Decode(bytes), true);
                        }
                    }
                }

                playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                playButton.SetGeometry(playing ? Ui.Pause : Ui.Play);
                var controls = playback.Controls;
                prevButton.IsEnabled = controls.IsPreviousEnabled;
                nextButton.IsEnabled = controls.IsNextEnabled;
                playButton.IsEnabled = true;
                openButton.Visibility = Visibility.Collapsed;
                UpdateModes(playback);
                sourceText.Text = L.T("СЕЙЧАС ИГРАЕТ · ", "NOW PLAYING · ") + AppName(session.SourceAppUserModelId) + (viaBridge ? " · SoundCloud" : "");

                duration = timeline.EndTime - timeline.StartTime;
                if (duration.TotalSeconds >= 1)
                {
                    var pos = timeline.Position - timeline.StartTime;
                    if (playing && timeline.LastUpdatedTime.Year > 2000) pos += DateTimeOffset.Now - timeline.LastUpdatedTime;
                    if (pos > duration) pos = duration;
                    progress.Visibility = timeRow.Visibility = Visibility.Visible;
                    if (!progress.IsDragging)
                    {
                        progress.Set(pos.TotalSeconds / duration.TotalSeconds);
                        ShowTimes(pos);
                    }
                }
                else
                {
                    // Длина неизвестна (обычно на долю секунды при смене трека): прячем полоску,
                    // но оставляем её место, чтобы виджет не прыгал по высоте.
                    progress.Visibility = timeRow.Visibility = Visibility.Hidden;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                manager = null;
            }
            finally
            {
                busy = false;
            }
        }

        GlobalSystemMediaTransportControlsSession PickSession()
        {
            var current = manager.GetCurrentSession();
            if (current != null) return current;
            var all = manager.GetSessions();
            return all.FirstOrDefault(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                ?? all.FirstOrDefault();
        }

        void ShowEmpty()
        {
            if (trackKey == "") return;
            trackKey = "";
            artHash = null;
            sourceText.Text = L.T("СЕЙЧАС ИГРАЕТ", "NOW PLAYING");
            titleText.Text = L.T("Ничего не играет", "Nothing is playing");
            artistText.Text = L.T("Включите музыку в браузере", "Play some music in your browser");
            SetArtwork(null, true);
            playing = false;
            playButton.SetGeometry(Ui.Play);
            prevButton.IsEnabled = nextButton.IsEnabled = playButton.IsEnabled = false;
            shuffleButton.IsEnabled = repeatButton.IsEnabled = false;
            viaBridge = false;
            heartButton.Visibility = Visibility.Collapsed;
            progress.Visibility = timeRow.Visibility = Visibility.Collapsed;
            openButton.Visibility = Visibility.Visible;
        }

        void SetArtwork(ImageSource art, bool animate)
        {
            if (art != null)
            {
                artwork.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
                artwork.Child = null;
            }
            else
            {
                artwork.Background = Ui.ButtonBg;
                artwork.Child = placeholder;
            }
            backdrop.Source = art;
            sound.SetArt(art);
            if (animate)
            {
                backdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)));
                artwork.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(300)));
            }
        }

        void ShowTimes(TimeSpan pos)
        {
            elapsedText.Text = Ui.Time(pos);
            remainingText.Text = "-" + Ui.Time(duration - pos);
        }

        // Повтор и перемешивание: для SoundCloud в браузере — через расширение, иначе — через Windows.
        void UpdateModes(GlobalSystemMediaTransportControlsSessionPlaybackInfo playback)
        {
            string scRepeat = "none";
            bool scShuffle = false, scCanLike = false, scLiked = false;
            viaBridge = IsBrowser(session.SourceAppUserModelId) && bridge.TryGetState(out scRepeat, out scShuffle, out scCanLike, out scLiked);
            bool locked = DateTime.Now < modesLockedUntil; // ждём, пока плеер применит наше нажатие
            heartButton.Visibility = viaBridge && scCanLike ? Visibility.Visible : Visibility.Collapsed;
            if (viaBridge && DateTime.Now >= likeLockedUntil) liked = scLiked;
            ShowLike();
            if (viaBridge)
            {
                shuffleButton.IsEnabled = repeatButton.IsEnabled = true;
                if (!locked) { repeatMode = scRepeat; shuffleOn = scShuffle; }
                shuffleButton.ToolTip = L.T("Перемешать", "Shuffle");
                repeatButton.ToolTip = L.T("Повтор: выкл → трек → плейлист", "Repeat: off → track → playlist");
            }
            else
            {
                var c = playback.Controls;
                shuffleButton.IsEnabled = c.IsShuffleEnabled;
                repeatButton.IsEnabled = c.IsRepeatEnabled;
                if (!locked)
                {
                    var r = playback.AutoRepeatMode;
                    repeatMode = r == RepeatMode.Track ? "one" : r == RepeatMode.List ? "all" : "none";
                    shuffleOn = playback.IsShuffleActive == true;
                }
                string hint = IsBrowser(session.SourceAppUserModelId)
                    ? L.T("Для SoundCloud установите расширение (меню значка в трее)", "For SoundCloud, install the extension (tray icon menu)") : L.T("Этот плеер не поддерживает", "Not supported by this player");
                shuffleButton.ToolTip = c.IsShuffleEnabled ? L.T("Перемешать", "Shuffle") : hint;
                repeatButton.ToolTip = c.IsRepeatEnabled ? L.T("Повтор: выкл → трек → плейлист", "Repeat: off → track → playlist") : hint;
            }
            ShowModes();
        }

        void ShowModes()
        {
            shuffleButton.Set(Ui.ShuffleGlyph, shuffleOn);
            repeatButton.Set(repeatMode == "one" ? Ui.RepeatOneGlyph : Ui.RepeatAllGlyph, repeatMode != "none");
        }

        void ShowLike()
        {
            heartGlyph.Text = liked ? Ui.HeartFillGlyph : Ui.HeartGlyph;
            heartGlyph.Foreground = liked ? Ui.SoundCloud : Brushes.White;
            heartButton.ToolTip = liked ? L.T("Убрать из лайков", "Unlike") : L.T("Лайкнуть на SoundCloud", "Like on SoundCloud");
        }

        void ToggleLike()
        {
            if (!viaBridge) return;
            liked = !liked;
            likeLockedUntil = DateTime.Now.AddSeconds(2);
            ShowLike();
            bridge.Send("like");
            if (liked)
            {
                // Сердечко «подпрыгивает», как в iOS.
                var pop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(320) };
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.35, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)), new BackEase { EasingMode = EasingMode.EaseOut }));
                heartButton.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                heartButton.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
        }

        void ToggleShuffle()
        {
            shuffleOn = !shuffleOn;
            modesLockedUntil = DateTime.Now.AddSeconds(1.5);
            ShowModes();
            if (viaBridge) bridge.Send("shuffle");
            else
            {
                bool on = shuffleOn;
                Command(s => s.TryChangeShuffleActiveAsync(on));
            }
        }

        void CycleRepeat()
        {
            // Тот же круг, что у SoundCloud: выкл → один трек → весь плейлист → выкл.
            repeatMode = repeatMode == "none" ? "one" : repeatMode == "one" ? "all" : "none";
            modesLockedUntil = DateTime.Now.AddSeconds(1.5);
            ShowModes();
            if (viaBridge) bridge.Send("repeat");
            else
            {
                var mode = repeatMode == "one" ? RepeatMode.Track : repeatMode == "all" ? RepeatMode.List : RepeatMode.None;
                Command(s => s.TryChangeAutoRepeatModeAsync(mode));
            }
        }

        static bool IsBrowser(string appId)
        {
            var name = AppName(appId);
            return name == "Opera GX" || name == "Chrome" || name == "Edge" || name == "Firefox" || name == L.T("Яндекс", "Yandex");
        }

        static void OpenExtensionFolder()
        {
            try { Process.Start("explorer.exe", "/select,\"" + System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "soundcloud-extension") + "\""); }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }

        void TogglePlay()
        {
            playing = !playing;
            playButton.SetGeometry(playing ? Ui.Pause : Ui.Play);
            Command(s => s.TryTogglePlayPauseAsync());
        }

        async void Command(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> action)
        {
            var s = session;
            if (s == null) return;
            try { await WinRt.AsTask(action(s)); } catch (Exception ex) { Debug.WriteLine(ex); }
            Refresh();
        }

        void SeekTo(double fraction)
        {
            var s = session;
            if (s == null || duration.TotalSeconds < 1) return;
            var start = s.GetTimelineProperties().StartTime;
            long ticks = (start + TimeSpan.FromTicks((long)(duration.Ticks * fraction))).Ticks;
            Command(x => x.TryChangePlaybackPositionAsync(ticks));
        }

        static string AppName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Windows";
            var low = id.ToLowerInvariant();
            if (low.Contains("opera")) return "Opera GX";
            if (low.Contains("chrome")) return "Chrome";
            if (low.Contains("msedge") || low.Contains("edge")) return "Edge";
            if (low.Contains("firefox")) return "Firefox";
            if (low.Contains("spotify")) return "Spotify";
            if (low.Contains("yandex")) return L.T("Яндекс", "Yandex");
            if (low.Contains("soundcloud")) return "SoundCloud";
            var name = id.Split('\\', '!').Last();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return name;
        }

        // SoundCloud открываем в Opera GX (там стоит расширение для лайков и повтора), а если её нет — в браузере по умолчанию.
        static void OpenSoundCloud()
        {
            const string url = "https://soundcloud.com";
            var candidates = new[]
            {
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Opera GX\opera.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Opera GX\opera.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Opera GX\launcher.exe"),
            };
            try
            {
                var opera = candidates.FirstOrDefault(File.Exists);
                if (opera != null) Process.Start(opera, url);
                else Process.Start(url);
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }

        // Замок: виджет нельзя сдвинуть и изменить в размере, но кнопки и перемотка работают.
        void SetLocked(bool on)
        {
            locked = on;
            lockGlyph.Text = on ? Ui.LockGlyph : Ui.UnlockGlyph;
            lockButton.Background = on ? Ui.ActiveBg : Ui.ButtonBg;
            lockGlyph.Foreground = on ? Ui.ActiveGlyph : Brushes.White;
            lockButton.ToolTip = on ? L.T("Замок закрыт: нажмите, чтобы снова двигать и менять размер", "Locked: click to move and resize again") : L.T("Замок: заморозить место и размер", "Lock position and size");
            grip.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (lockItem != null) lockItem.Checked = on;
            SavePosition();
        }

        // Форма виджета: одна скруглённая карточка, а при раскрытом микшере — две, с зазором между ними.
        void UpdateCardShape()
        {
            var size = card.RenderSize;
            double split = sound.SplitY(card);
            if (double.IsNaN(split) || split <= 40 || split >= size.Height - 20) split = double.NaN;
            if (size == lastCardSize && (split == lastSplit || (double.IsNaN(split) && double.IsNaN(lastSplit)))) return;
            lastCardSize = size;
            lastSplit = split;
            if (size.Width <= 0 || size.Height <= 0) return;

            Geometry shape;
            if (double.IsNaN(split))
                shape = new RectangleGeometry(new Rect(size), 32, 32);
            else
            {
                var top = new RectangleGeometry(new Rect(0, 0, size.Width, split), 32, 32);
                var bottom = new RectangleGeometry(new Rect(0, split + SoundPanel.Gap, size.Width, size.Height - split - SoundPanel.Gap), 24, 24);
                shape = new CombinedGeometry(GeometryCombineMode.Union, top, bottom);
            }
            shape.Freeze();
            card.Clip = shape;
            shadowShape.Data = shape;
            edgeShape.Data = shape;
        }

        // Строка меню «⋯»: значок, текст и, если нужно, что-то справа (галочка, стрелочка).
        static Border MenuRow(string glyph, string text, Brush color, UIElement right)
        {
            var row = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(-8, 0, -8, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(Ui.Glyph(glyph, 11, color));
            left.Children.Add(new TextBlock { Text = text, Foreground = color, FontSize = 13, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
            g.Children.Add(left);
            if (right != null)
            {
                var fe = right as FrameworkElement;
                if (fe != null) { fe.VerticalAlignment = VerticalAlignment.Center; fe.Margin = new Thickness(8, 0, 0, 0); }
                Grid.SetColumn(right, 1);
                g.Children.Add(right);
            }
            row.Child = g;
            row.MouseEnter += delegate { row.Background = Ui.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += (s, e) => e.Handled = true;
            return row;
        }

        // Микшер раскрыли — запоминаем, где был плеер (у нижнего края окно поднимется, чтобы микшер влез).
        // Свернули — возвращаем плеер туда же, если его за это время не двигали руками.
        void OnMixerToggled()
        {
            if (sound.Expanded) { topBeforeMixer = Top; pendingTop = double.NaN; }
            else if (!double.IsNaN(topBeforeMixer))
            {
                pendingTop = topBeforeMixer;   // поставим на место, когда окно уменьшится (SizeChanged)
                topBeforeMixer = double.NaN;
                return;
            }
            SavePosition();
        }

        // Инструкция по расширению: сразу после установки (force) или один раз при первом запуске —
        // через 4 секунды, и только если расширение за это время так и не отозвалось.
        public void OfferExtensionGuide(bool force)
        {
            if (force) { ExtensionGuide.Show(bridge); MarkGuideOffered(); return; }
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SettingsKey))
                    if (k != null && "1".Equals(k.GetValue("ExtensionGuideOffered"))) return;
            }
            catch { }
            var wait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            wait.Tick += delegate
            {
                wait.Stop();
                MarkGuideOffered();
                if (!bridge.Connected) ExtensionGuide.Show(bridge);
            };
            wait.Start();
        }

        void MarkGuideOffered()
        {
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SettingsKey)) k.SetValue("ExtensionGuideOffered", "1"); } catch { }
        }

        // «Удалить с компьютера»: запускаем программу удаления из установщика, у неё своё окно с подтверждением.
        void UninstallApp()
        {
            var uninstaller = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Uninstall.exe");
            if (File.Exists(uninstaller)) { Process.Start(uninstaller, "--uninstall"); return; }
            MessageBox.Show(L.T("Программа удаления не найдена. Удалить «Сейчас играет» можно в «Параметры → Приложения → Установленные приложения».",
                                "The uninstaller was not found. You can remove Now Playing in Settings → Apps → Installed apps."), L.T("Сейчас играет", "Now Playing"));
        }

        void ToggleMenu()
        {
            bool open = menuPanel.Visibility != Visibility.Visible;
            menuPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open) blurBar.Set(blurAmount);
        }

        // 0 — обложка на фоне чёткая, 1 — сильно размыта.
        void SetBlur(double v)
        {
            blurAmount = Math.Max(0, Math.Min(1, v));
            blur.Radius = blurAmount * MaxBlur;
            blurText.Text = Math.Round(blurAmount * 100) + "%";
        }

        void ApplyScale(double s)
        {
            scale = Math.Max(MinScale, Math.Min(MaxScale, Math.Round(s, 2)));
            zoom.ScaleX = zoom.ScaleY = scale;
        }

        // После изменения размера возвращаем виджет на экран — тот монитор, где он стоит (прозрачные поля по краям — 18 px).
        void KeepOnScreen()
        {
            var area = Ui.WorkAreaOf(this);
            double margin = 18 * scale;
            if (Left + ActualWidth - margin > area.Right) Left = area.Right - ActualWidth + margin;
            if (Top + ActualHeight - margin > area.Bottom) Top = area.Bottom - ActualHeight + margin;
            if (Left + margin < area.Left) Left = area.Left - margin;
            if (Top + margin < area.Top) Top = area.Top - margin;
        }

        void RestorePosition()
        {
            var area = SystemParameters.WorkArea;
            double left = area.Right - BaseWidth + 6, top = area.Bottom - 330;
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    if (k != null && k.GetValue("Left") != null)
                    {
                        double l = double.Parse((string)k.GetValue("Left"), System.Globalization.CultureInfo.InvariantCulture);
                        double t = double.Parse((string)k.GetValue("Top"), System.Globalization.CultureInfo.InvariantCulture);
                        if (l > SystemParameters.VirtualScreenLeft - 100 && l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
                            t > SystemParameters.VirtualScreenTop - 50 && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
                        { left = l; top = t; }
                    }
                    if (k != null && k.GetValue("Scale") != null)
                        savedScale = double.Parse((string)k.GetValue("Scale"), System.Globalization.CultureInfo.InvariantCulture);
                    if (k != null) savedLocked = "1".Equals(k.GetValue("Locked"));
                    if (k != null && k.GetValue("Blur") != null)
                        savedBlur = double.Parse((string)k.GetValue("Blur"), System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch { }
            Left = left;
            Top = top;
        }

        void SavePosition()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    k.SetValue("Left", Left.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    k.SetValue("Top", Top.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    k.SetValue("Scale", scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    k.SetValue("Locked", locked ? "1" : "0");
                    k.SetValue("Blur", blurAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            catch { }
        }

        Drawing.Icon LoadIcon()
        {
            try
            {
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nowplaying.ico");
                var decoder = new IconBitmapDecoder(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Icon = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                int want = Forms.SystemInformation.SmallIconSize.Width;
                var frame = decoder.Frames.Where(f => f.PixelWidth >= want).OrderBy(f => f.PixelWidth).FirstOrDefault() ?? decoder.Frames[0];
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(frame));
                var ms = new MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;
                using (var bmp = new Drawing.Bitmap(ms))
                    return Drawing.Icon.FromHandle(bmp.GetHicon());
            }
            catch
            {
                return Drawing.SystemIcons.Application;
            }
        }

        public void ShowWidget()
        {
            if (!IsVisible)
            {
                Show();
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            }
            Activate();
            Refresh();
        }

        void HideWidget()
        {
            Hide();
            if (!hintShown)
            {
                hintShown = true;
                tray.ShowBalloonTip(3000, L.T("Сейчас играет", "Now Playing"), L.T("Плеер свёрнут в трей. Нажмите на значок, чтобы открыть его снова.", "The player is in the tray. Click the icon to open it again."), Forms.ToolTipIcon.None);
            }
        }

        void Toggle()
        {
            if (IsVisible) HideWidget(); else ShowWidget();
        }

        void OpenStats()
        {
            try { ListenStats.Open(bridge.Listens); }
            catch (Exception ex) { MessageBox.Show(L.T("Не получилось открыть статистику: ", "Could not open the stats: ") + ex.Message, L.T("Сейчас играет", "Now Playing")); }
        }

        // Новый экземпляр ждёт, пока этот закроется (--restart), и открывается уже с новыми настройками.
        void Restart()
        {
            Process.Start(Process.GetCurrentProcess().MainModule.FileName, "--restart");
            ExitApp();
        }

        void ExitApp()
        {
            bridge.Listens.Flush();   // дослушанный до 30 секунд трек не пропадает
            exiting = true;
            tray.Visible = false;
            tray.Dispose();
            Application.Current.Shutdown();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            var mutex = new Mutex(true, @"Local\NowPlayingWidget", out created);
            if (!created && args.Contains("--restart"))   // перезапуск: ждём, пока старый экземпляр закроется
            {
                try { created = mutex.WaitOne(10000); }
                catch (AbandonedMutexException) { created = true; }
            }
            if (!created)
            {
                try { EventWaitHandle.OpenExisting(@"Local\NowPlayingWidgetShow").Set(); } catch { }
                return;
            }
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\NowPlayingWidgetShow");

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var window = new PlayerWindow();
            var listener = new Thread(() =>
            {
                while (true)
                {
                    showEvent.WaitOne();
                    window.Dispatcher.BeginInvoke(new Action(window.ShowWidget));
                }
            }) { IsBackground = true };
            listener.Start();

            if (!args.Contains("--hidden")) window.ShowWidget();
            if (!args.Contains("--hidden")) window.OfferExtensionGuide(args.Contains("--extension-guide"));
            app.Run();
            GC.KeepAlive(mutex);
        }
    }
}
