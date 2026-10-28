// Звуковая часть Микшера для виджета «Сейчас играет»: громкость Windows, ползунки, карточки приложений.
// Скопировано из SoundMixer\Mixer.cs (класс Ui переименован в MixUi). Правки звука — сначала в Mixer.cs.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace NowPlaying
{
    // ---------------- Core Audio (WASAPI) ----------------

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr props);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid ctx);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid param);
        [PreserveSig] int SetGroupingParam(ref Guid param, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notify);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notify);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    static class Native
    {
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);

        public static string ProcessPath(uint pid)
        {
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }
    }

    class AudioSession
    {
        public string Key;
        public string Name;
        public string ExePath;
        public bool IsSystem;
        public ISimpleAudioVolume Volume;
    }

    class Audio
    {
        static Guid ctx = Guid.Empty;
        readonly IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        readonly Dictionary<string, string> names = new Dictionary<string, string>();
        string deviceId;
        IAudioEndpointVolume endpoint;
        IAudioSessionManager2 manager;

        // Подхватывает смену устройства вывода по умолчанию; true — если устройство сменилось.
        public bool EnsureDevice()
        {
            IMMDevice device;
            string id = null;
            if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device) != 0) device = null;
            if (device != null) device.GetId(out id);
            if (id == deviceId) return false;

            deviceId = id;
            endpoint = null;
            manager = null;
            if (device != null)
            {
                object o;
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out o) == 0) endpoint = o as IAudioEndpointVolume;
                iid = typeof(IAudioSessionManager2).GUID;
                if (device.Activate(ref iid, 23, IntPtr.Zero, out o) == 0) manager = o as IAudioSessionManager2;
            }
            return true;
        }

        public bool GetMaster(out float level, out bool muted)
        {
            level = 0; muted = false;
            return endpoint != null && endpoint.GetMasterVolumeLevelScalar(out level) == 0 && endpoint.GetMute(out muted) == 0;
        }

        public void SetMaster(float level) { if (endpoint != null) endpoint.SetMasterVolumeLevelScalar(level, ref ctx); }
        public void SetMasterMute(bool mute) { if (endpoint != null) endpoint.SetMute(mute, ref ctx); }

        public List<AudioSession> GetSessions()
        {
            var result = new List<AudioSession>();
            IAudioSessionEnumerator sessions;
            if (manager == null || manager.GetSessionEnumerator(out sessions) != 0 || sessions == null) return result;

            int count;
            sessions.GetCount(out count);
            for (int i = 0; i < count; i++)
            {
                IAudioSessionControl2 control;
                int state;
                if (sessions.GetSession(i, out control) != 0 || control == null) continue;
                if (control.GetState(out state) != 0 || state == 2 /* Expired */) continue;

                var s = new AudioSession { Volume = control as ISimpleAudioVolume };
                if (s.Volume == null) continue;
                s.IsSystem = control.IsSystemSoundsSession() == 0;
                if (s.IsSystem)
                {
                    s.Key = "#system";
                    s.Name = L.T("Системные звуки", "System sounds");
                }
                else
                {
                    uint pid;
                    control.GetProcessId(out pid);
                    s.ExePath = Native.ProcessPath(pid);
                    if (s.ExePath == null) continue; // процесс уже закрыт
                    s.Key = s.ExePath.ToLowerInvariant();
                    s.Name = FriendlyName(s.ExePath);
                }
                result.Add(s);
            }
            return result;
        }

        string FriendlyName(string path)
        {
            string name;
            if (names.TryGetValue(path, out name)) return name;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                name = info.FileDescription;
                if (string.IsNullOrWhiteSpace(name) || name.Length > 30) name = info.ProductName;
            }
            catch { }
            if (string.IsNullOrWhiteSpace(name) || name.Length > 30)
            {
                name = Path.GetFileNameWithoutExtension(path);
                if (name.Length > 0) name = char.ToUpper(name[0]) + name.Substring(1);
            }
            name = name.Trim();
            names[path] = name;
            return name;
        }
    }

    // ---------------- Внешний вид ----------------

    static class MixUi
    {
        public static readonly FontFamily Text = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public static SolidColorBrush Brush(uint argb)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            b.Freeze();
            return b;
        }

        public static readonly SolidColorBrush Panel = Brush(0xF51C1C1E);
        public static readonly SolidColorBrush PanelBorder = Brush(0x22FFFFFF);
        public static readonly SolidColorBrush Card = Brush(0xFF2C2C2E);
        public static readonly SolidColorBrush Track = Brush(0xFF48484A);
        public static readonly SolidColorBrush Fill = Brush(0xFFFFFFFF);
        public static readonly SolidColorBrush FillMuted = Brush(0xFF6C6C70);
        public static readonly SolidColorBrush Secondary = Brush(0xFF8E8E93);
        public static readonly SolidColorBrush ButtonBg = Brush(0x2EFFFFFF);
        public static readonly SolidColorBrush ButtonHover = Brush(0x4DFFFFFF);
        // Включённая кнопка — белая с тёмным значком, как переключатели в «Пункте управления» iOS.
        public static readonly SolidColorBrush ActiveBg = Brush(0xFFFFFFFF);
        public static readonly SolidColorBrush ActiveGlyph = Brush(0xFF1C1C1E);

        public static TextBlock Glyph(string glyph, double size, Brush fg)
        {
            return new TextBlock
            {
                Text = glyph, FontFamily = Icons, FontSize = size, Foreground = fg,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
        }

        public const string LockGlyph = "";
        public const string UnlockGlyph = "";

        public static string VolumeGlyph(double v, bool muted)
        {
            if (muted) return "";
            if (v < 0.01) return "";
            if (v < 0.34) return "";
            if (v < 0.67) return "";
            return "";
        }

        public static string Percent(double v) { return Math.Round(v * 100) + "%"; }
    }

    // Векторный значок динамика: волны по уровню громкости, крестик — звук выключен.
    // Нарисован линиями, а не шрифтом, поэтому остаётся чётким при любом размере виджета.
    class VolumeIcon : Viewbox
    {
        static readonly Geometry Body = Geometry.Parse("M 1.5,7.5 H 5 L 9.5,3.5 V 16.5 L 5,12.5 H 1.5 Z");
        static readonly Geometry[] Waves =
        {
            Geometry.Parse("M 12.5,7.6 Q 14.1,10 12.5,12.4"),
            Geometry.Parse("M 14.8,5.4 Q 17.8,10 14.8,14.6"),
            Geometry.Parse("M 17.1,3.2 Q 21.5,10 17.1,16.8")
        };
        static readonly Geometry Cross = Geometry.Parse("M 13.5,7.5 L 18.5,12.5 M 18.5,7.5 L 13.5,12.5");

        readonly System.Windows.Shapes.Path[] waves = new System.Windows.Shapes.Path[3];
        readonly System.Windows.Shapes.Path cross;

        public VolumeIcon(double size, Brush color)
        {
            Width = size;
            Height = size;
            var canvas = new Canvas { Width = 22, Height = 20 };
            canvas.Children.Add(new System.Windows.Shapes.Path { Data = Body, Fill = color, Stroke = color, StrokeThickness = 1.3, StrokeLineJoin = PenLineJoin.Round });
            for (int i = 0; i < 3; i++)
            {
                waves[i] = Line(Waves[i], color);
                canvas.Children.Add(waves[i]);
            }
            cross = Line(Cross, color);
            canvas.Children.Add(cross);
            Child = canvas;
            Set(1, false);
        }

        static System.Windows.Shapes.Path Line(Geometry g, Brush color)
        {
            return new System.Windows.Shapes.Path
            {
                Data = g, Stroke = color, StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            };
        }

        public void Set(double level, bool muted)
        {
            int count = muted || level < 0.01 ? 0 : level < 0.34 ? 1 : level < 0.67 ? 2 : 3;
            for (int i = 0; i < 3; i++) waves[i].Visibility = i < count ? Visibility.Visible : Visibility.Hidden;
            cross.Visibility = muted ? Visibility.Visible : Visibility.Hidden;
        }
    }

    // Толстый ползунок как в «Пункте управления» iOS.
    class PillSlider : Grid
    {
        readonly Border fill;
        readonly ScaleTransform scale = new ScaleTransform(0, 1);
        readonly VolumeIcon glyph;
        readonly double radius;
        double value;
        bool muted, dragging;

        public event Action<double> ValueChanged;
        public bool IsDragging { get { return dragging; } }
        public double Value { get { return value; } }

        public bool Muted
        {
            get { return muted; }
            set { if (muted != value) { muted = value; ApplyLook(); } }
        }

        public PillSlider(double height, double cornerRadius, bool showGlyph)
        {
            Height = height;
            radius = cornerRadius;
            Background = MixUi.Track;
            Cursor = Cursors.Hand;

            fill = new Border { Background = MixUi.Fill, RenderTransform = scale, RenderTransformOrigin = new Point(0, 0.5) };
            Children.Add(fill);
            if (showGlyph)
            {
                glyph = new VolumeIcon(height * 0.42, MixUi.Secondary);
                glyph.HorizontalAlignment = HorizontalAlignment.Left;
                glyph.VerticalAlignment = VerticalAlignment.Center;
                glyph.Margin = new Thickness(height * 0.36, 0, 0, 0);
                glyph.IsHitTestVisible = false;
                Children.Add(glyph);
            }

            SizeChanged += delegate { Clip = new RectangleGeometry(new Rect(RenderSize), radius, radius); };
            MouseLeftButtonDown += (s, e) => { dragging = true; CaptureMouse(); SetFromMouse(e); e.Handled = true; };
            MouseMove += (s, e) => { if (dragging) SetFromMouse(e); };
            MouseLeftButtonUp += (s, e) => { dragging = false; ReleaseMouseCapture(); };
            LostMouseCapture += delegate { dragging = false; };
            MouseWheel += (s, e) => { Change(value + (e.Delta > 0 ? 0.05 : -0.05), true); e.Handled = true; };
        }

        // Значение, пришедшее извне (например, громкость сменили клавишами).
        public void Set(double v)
        {
            if (dragging || Math.Abs(v - value) < 0.002) return;
            Change(v, false);
        }

        void SetFromMouse(MouseEventArgs e)
        {
            if (ActualWidth > 0) Change(e.GetPosition(this).X / ActualWidth, true, false);
        }

        void Change(double v, bool fromUser, bool animate = true)
        {
            value = Math.Max(0, Math.Min(1, v));
            ApplyLook();
            if (animate)
            {
                var a = new DoubleAnimation(value, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            }
            else
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.ScaleX = value;
            }
            if (fromUser && ValueChanged != null) ValueChanged(value);
        }

        void ApplyLook()
        {
            fill.Background = muted ? MixUi.FillMuted : MixUi.Fill;
            if (glyph != null) glyph.Set(value, muted);
        }
    }

    // Карточка одного приложения.
    class AppRow : Border
    {
        static Guid ctx = Guid.Empty;
        public readonly string DisplayName;
        public readonly bool IsSystem;
        List<ISimpleAudioVolume> volumes = new List<ISimpleAudioVolume>();
        readonly PillSlider slider;
        readonly TextBlock percent;
        readonly FrameworkElement icon;
        bool muted;

        public AppRow(string name, ImageSource image, bool isSystem)
        {
            DisplayName = name;
            IsSystem = isSystem;
            Background = MixUi.Card;
            CornerRadius = new CornerRadius(20);
            Padding = new Thickness(14, 12, 14, 14);
            Margin = new Thickness(0, 0, 0, 8);

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            icon = MakeIcon(name, image, isSystem);
            var iconHost = new Border { Child = icon, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = L.T("Выключить или включить звук", "Mute or unmute") };
            iconHost.MouseLeftButtonUp += delegate { ToggleMute(); };
            g.Children.Add(iconHost);

            var title = new TextBlock
            {
                Text = name, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0)
            };
            Grid.SetColumn(title, 1);
            g.Children.Add(title);

            percent = new TextBlock { Foreground = MixUi.Secondary, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(percent, 2);
            g.Children.Add(percent);

            slider = new PillSlider(30, 15, false) { Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetRow(slider, 1);
            Grid.SetColumnSpan(slider, 3);
            g.Children.Add(slider);
            slider.ValueChanged += v =>
            {
                foreach (var vol in volumes)
                {
                    vol.SetMasterVolume((float)v, ref ctx);
                    if (muted && v > 0) vol.SetMute(false, ref ctx);
                }
                if (muted && v > 0) muted = false;
                ShowState();
            };
            Child = g;
        }

        public void SetVolumes(List<ISimpleAudioVolume> list) { volumes = list; }

        public void Update()
        {
            float v;
            bool m;
            if (volumes.Count == 0 || volumes[0].GetMasterVolume(out v) != 0 || volumes[0].GetMute(out m) != 0) return;
            muted = m;
            slider.Set(v);
            ShowState();
        }

        void ToggleMute()
        {
            muted = !muted;
            foreach (var vol in volumes) vol.SetMute(muted, ref ctx);
            ShowState();
        }

        void ShowState()
        {
            slider.Muted = muted;
            percent.Text = muted ? L.T("Выкл.", "Off") : MixUi.Percent(slider.Value);
            icon.Opacity = muted ? 0.4 : 1;
        }

        static FrameworkElement MakeIcon(string name, ImageSource image, bool isSystem)
        {
            if (image != null)
            {
                var img = new Image { Source = image, Width = 30, Height = 30 };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                return img;
            }
            var b = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8) };
            if (isSystem)
            {
                b.Background = new LinearGradientBrush(Color.FromRgb(0x98, 0x98, 0x9D), Color.FromRgb(0x63, 0x63, 0x66), 90);
                b.Child = MixUi.Glyph("", 15, Brushes.White);
            }
            else
            {
                b.Background = MixUi.Track;
                b.Child = new TextBlock
                {
                    Text = name.Length > 0 ? name.Substring(0, 1).ToUpper() : "?", Foreground = Brushes.White, FontSize = 15,
                    FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
            }
            return b;
        }
    }

}
