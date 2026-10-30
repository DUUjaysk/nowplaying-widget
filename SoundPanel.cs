// Блок «Звук» в виджете «Сейчас играет»: громкость приложения, где играет трек, и микшер всех звуков.
// Микшер раскрывается стрелочкой (тогда сверху остаётся полоска-разделитель), у него свой сильно
// размытый фон, и его можно отделить в отдельный виджет кнопкой ⧉ и вернуть обратно.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace NowPlaying
{
    // «Стекло» в стиле iOS: полупрозрачная заливка, светлее сверху, и тонкая светлая кайма.
    static class Glass
    {
        public static readonly Brush Fill = MakeFill();
        public static readonly Brush Edge = Ui.Brush(0x38FFFFFF);
        public static readonly Brush Track = Ui.Brush(0x40FFFFFF);

        static Brush MakeFill()
        {
            var b = new LinearGradientBrush(Color.FromArgb(0x30, 255, 255, 255), Color.FromArgb(0x14, 255, 255, 255), 90);
            b.Freeze();
            return b;
        }

        public static void Apply(Border b, double radius)
        {
            b.Background = Fill;
            b.BorderBrush = Edge;
            b.BorderThickness = new Thickness(1);
            b.CornerRadius = new CornerRadius(radius);
        }
    }

    // Компактная строка приложения: иконка (клик — выключить звук), название, ползунок, проценты.
    class MiniRow : Border
    {
        static Guid ctx = Guid.Empty;
        public readonly string DisplayName;
        public readonly bool IsSystem;
        List<ISimpleAudioVolume> volumes = new List<ISimpleAudioVolume>();
        readonly PillSlider slider;
        readonly TextBlock percent;
        readonly FrameworkElement icon;
        bool muted;

        public MiniRow(string name, ImageSource image, bool isSystem)
        {
            DisplayName = name;
            IsSystem = isSystem;
            Glass.Apply(this, 14);
            Padding = new Thickness(8, 6, 10, 6);
            Margin = new Thickness(0, 0, 0, 6);

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });

            icon = MakeIcon(name, image, isSystem);
            var iconHost = new Border { Child = icon, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = L.T("Выключить или включить звук", "Mute or unmute"), VerticalAlignment = VerticalAlignment.Center };
            iconHost.MouseLeftButtonUp += delegate { ToggleMute(); };
            g.Children.Add(iconHost);

            var title = new TextBlock
            {
                Text = name, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, ToolTip = name,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0)
            };
            Grid.SetColumn(title, 1);
            g.Children.Add(title);

            slider = new PillSlider(14, 7, false) { VerticalAlignment = VerticalAlignment.Center, Background = Glass.Track };
            Grid.SetColumn(slider, 2);
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

            percent = new TextBlock { Foreground = Ui.Faint, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(percent, 3);
            g.Children.Add(percent);
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
                var img = new Image { Source = image, Width = 22, Height = 22 };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                return img;
            }
            var b = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = Glass.Track };
            b.Child = isSystem ? (UIElement)MixUi.Glyph("", 11, Brushes.White)
                               : new TextBlock { Text = name.Length > 0 ? name.Substring(0, 1).ToUpper() : "?", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return b;
        }
    }

    // Минималистичная громкость под плеером, как на экране блокировки iOS: 🔈 ━━━━━━━ 🔊
    // Полоска та же, что у перемотки; тихий динамик слева выключает звук; имя и проценты — в подсказке.
    class MiniVolume : Grid
    {
        static Guid ctx = Guid.Empty;
        readonly SeekBar bar = new SeekBar();
        readonly VolumeIcon low;
        List<ISimpleAudioVolume> volumes = new List<ISimpleAudioVolume>();
        string appName = "";
        bool muted;
        double value;

        public MiniVolume()
        {
            Margin = new Thickness(0, 10, 0, 0);
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            low = new VolumeIcon(13, Ui.Faint) { VerticalAlignment = VerticalAlignment.Center };
            var lowHost = new Border { Child = low, Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(0, 2, 10, 2), ToolTip = L.T("Выключить или включить звук", "Mute or unmute") };
            lowHost.MouseLeftButtonUp += delegate { ToggleMute(); };
            low.Set(0, false);
            Children.Add(lowHost);

            Grid.SetColumn(bar, 1);
            bar.Preview += v => Apply(v);
            bar.Seek += v => Apply(v);
            Children.Add(bar);

            var high = new VolumeIcon(13, Ui.Faint) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), IsHitTestVisible = false };
            high.Set(1, false);
            Grid.SetColumn(high, 2);
            Children.Add(high);
        }

        // Адаптивно: если музыка играет на SoundCloud в браузере и расширение отдаёт громкость трека —
        // полоска меняет только трек; иначе — громкость всего приложения через Windows.
        public SoundCloudBridge Bridge;
        bool browser, musicMode;
        double beforeMute = 0.5;
        DateTime musicLockUntil;            // после нашего движения ждём, пока страница применит громкость

        public void SetApp(string name, List<ISimpleAudioVolume> list, bool isBrowser)
        {
            appName = name;
            volumes = list;
            browser = isBrowser;
        }

        public void Update()
        {
            double mv;
            musicMode = browser && Bridge != null && Bridge.TryGetMusicVolume(out mv);
            if (musicMode)
            {
                if (DateTime.Now < musicLockUntil || bar.IsDragging) { ShowState(); return; }
                Bridge.TryGetMusicVolume(out mv);
                value = mv;
                muted = mv < 0.005;
                bar.Set(mv);
                ShowState();
                return;
            }
            float v;
            bool m;
            if (volumes.Count == 0 || volumes[0].GetMasterVolume(out v) != 0 || volumes[0].GetMute(out m) != 0) return;
            muted = m;
            if (!bar.IsDragging) { value = v; bar.Set(v); }
            ShowState();
        }

        void Apply(double v)
        {
            value = v;
            if (musicMode)
            {
                Bridge.SendVolume(v);
                musicLockUntil = DateTime.Now.AddSeconds(1.5);
                muted = v < 0.005;
                ShowState();
                return;
            }
            foreach (var vol in volumes)
            {
                vol.SetMasterVolume((float)v, ref ctx);
                if (muted && v > 0) vol.SetMute(false, ref ctx);
            }
            if (muted && v > 0) muted = false;
            ShowState();
        }

        void ToggleMute()
        {
            if (musicMode)
            {
                // у трека нет отдельного «без звука» — ставим 0 и потом возвращаем прежнюю громкость
                if (value > 0.005) { beforeMute = value; Apply(0); }
                else Apply(beforeMute > 0.005 ? beforeMute : 0.5);
                bar.Set(value);
                return;
            }
            muted = !muted;
            foreach (var vol in volumes) vol.SetMute(muted, ref ctx);
            ShowState();
        }

        void ShowState()
        {
            bar.Opacity = muted ? 0.4 : 1;
            low.Set(0, muted);
            string what = musicMode ? L.T("Громкость трека (SoundCloud)", "Track volume (SoundCloud)") : L.T("Громкость: ", "Volume: ") + appName;
            bar.ToolTip = what + " — " + (muted ? L.T("звук выключен", "muted") : MixUi.Percent(value));
        }
    }

    class SoundPanel : StackPanel
    {
        const string SettingsKey = @"Software\NowPlayingWidget";
        const double AttachedMargin = 22;              // поля карточки виджета — микшер растягивается до её краёв

        readonly Audio audio = new Audio();
        readonly Dictionary<string, MiniRow> rows = new Dictionary<string, MiniRow>();
        readonly Dictionary<string, ImageSource> icons = new Dictionary<string, ImageSource>();
        readonly MiniVolume nowVolume = new MiniVolume();   // тонкая полоска громкости играющего приложения
        readonly Border nowHost = new Border();
        readonly StackPanel list = new StackPanel();
        readonly StackPanel outputs = new StackPanel();   // список устройств вывода под строкой «Общая»
        readonly Border outputsBox;
        readonly Popup outPopup;
        DateTime outClosedAt;
        readonly TextBlock outChevron;
        readonly TextBlock chevron;
        readonly Border expander;
        readonly Border joinButton;
        readonly Grid section;                          // микшер: свой сильно размытый фон + содержимое
        readonly Image sectionArt;
        readonly StackPanel sectionBody;
        readonly Border grabber;                        // полоска-разделитель сверху микшера
        readonly Border detachButton;
        readonly System.Windows.Shapes.Path detachIcon;
        static readonly Geometry TwoBars = Geometry.Parse("M 0,1.5 H 12 M 0,6.5 H 12");   // отделить: две палочки
        static readonly Geometry OneBar = Geometry.Parse("M 0,4 H 12");                  // вернуть: «слились» в одну
        public const double Gap = 8;                    // зазор между плеером и микшером
        readonly PillSlider master;
        readonly TextBlock masterPercent;
        readonly VolumeIcon masterIcon;
        Window owner, detachedWindow;
        Grid detachedHost;
        readonly Border pinButton, lockButton, playerButton;
        public event Action ShowPlayerRequested;
        readonly TextBlock pinGlyph, lockGlyph;
        readonly ScaleTransform dZoom = new ScaleTransform(1, 1);   // свой размер у отделённого микшера
        ResizeGrip dGrip;
        double dScale = 1, dDragStart;
        bool dLocked, dTopmost = true;
        ScaleTransform zoom;
        bool masterMuted, expanded, detached;

        public event Action ExpandedChanged;

        public SoundCloudBridge Bridge { set { nowVolume.Bridge = value; } }

        public bool Expanded
        {
            get { return expanded; }
            set { expanded = value; ApplyLayout(); }
        }

        public SoundPanel()
        {
            MouseLeftButtonDown += (s, e) => e.Handled = true;   // клики по звуку не двигают окно

            nowHost.Child = nowVolume;
            Children.Add(nowHost);

            chevron = Ui.Glyph("", 12, Ui.Faint);
            expander = new Border { Height = 22, Width = 60, CornerRadius = new CornerRadius(11), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = chevron, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), ToolTip = L.T("Все звуки", "All sounds") };
            expander.MouseEnter += delegate { expander.Background = Ui.Hover; };
            expander.MouseLeave += delegate { expander.Background = Brushes.Transparent; };
            expander.MouseLeftButtonUp += (s, e) => { e.Handled = true; SetExpanded(true); };
            Children.Add(expander);

            // пока микшер отделён — на месте стрелочки кнопка «соединить обратно»
            joinButton = new Border
            {
                Height = 22, Width = 60, CornerRadius = new CornerRadius(11), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), ToolTip = L.T("Вернуть микшер в плеер", "Put the mixer back into the player"),
                Child = new System.Windows.Shapes.Path
                {
                    Data = OneBar, Stroke = Ui.Faint, StrokeThickness = 3, Width = 12, Height = 8,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            joinButton.MouseEnter += delegate { joinButton.Background = Ui.Hover; };
            joinButton.MouseLeave += delegate { joinButton.Background = Brushes.Transparent; };
            joinButton.MouseLeftButtonUp += (s, e) => { e.Handled = true; Attach(false); };   // из плеера: микшер приезжает под плеер
            Children.Add(joinButton);

            // --- микшер ---
            section = new Grid { ClipToBounds = true };   // размытая картинка больше секции — обрезаем по её краям
            section.MouseLeftButtonDown += (s, e) => e.Handled = true;
            sectionArt = new Image { Stretch = Stretch.UniformToFill, Margin = new Thickness(-60), Effect = new BlurEffect { Radius = 70, RenderingBias = RenderingBias.Performance } };
            // такой же «глянец», как у плеера: тот же тёмный фон без обложки и то же затемнение сверху вниз
            section.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromRgb(0x3A, 0x2A, 0x4A), Color.FromRgb(0x1C, 0x1C, 0x1E), 60) });
            section.Children.Add(new FillOnly { Child = sectionArt });
            section.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0x85, 0, 0, 0), Color.FromArgb(0xB8, 0, 0, 0), 90) });

            sectionBody = new StackPanel();
            // «ручка» вверху микшера: клик сворачивает (сам раздел — зазор между карточками)
            grabber = new Border { Height = 14, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = L.T("Свернуть", "Collapse"), Margin = new Thickness(0, 0, 0, 2) };
            grabber.Child = new Border { Width = 36, Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Faint, VerticalAlignment = VerticalAlignment.Center };
            grabber.MouseLeftButtonUp += (s, e) => { e.Handled = true; SetExpanded(false); };
            sectionBody.Children.Add(grabber);

            var header = new Grid { Margin = new Thickness(2, 0, 0, 8), Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // ♪ — вернуть скрытый плеер; 📌 и 🔒 — только у отделённого микшера
            TextBlock playerGlyph;
            playerButton = HeaderButton("", () => { if (ShowPlayerRequested != null) ShowPlayerRequested(); }, out playerGlyph);
            playerButton.ToolTip = L.T("Показать плеер", "Show player");
            Grid.SetColumn(playerButton, 1);
            header.Children.Add(playerButton);
            pinButton = HeaderButton("", () => SetDetachedTopmost(!dTopmost), out pinGlyph);
            Grid.SetColumn(pinButton, 2);
            header.Children.Add(pinButton);
            lockButton = HeaderButton(Ui.UnlockGlyph, () => SetDetachedLocked(!dLocked), out lockGlyph);
            Grid.SetColumn(lockButton, 3);
            header.Children.Add(lockButton);
            header.Children.Add(new TextBlock { Text = L.T("ВСЕ ЗВУКИ", "ALL SOUNDS"), Foreground = Ui.Faint, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            detachIcon = new System.Windows.Shapes.Path
            {
                Data = TwoBars, Stroke = Brushes.White, StrokeThickness = 2, Width = 12, Height = 8,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            detachButton = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.ButtonBg, Child = detachIcon, Cursor = Cursors.Hand };
            detachButton.MouseLeftButtonDown += (s, e) => e.Handled = true;
            detachButton.MouseLeftButtonUp += (s, e) => { e.Handled = true; if (detached) Attach(true); else Detach(); };   // из микшера: плеер приезжает к микшеру
            Grid.SetColumn(detachButton, 4);
            header.Children.Add(detachButton);
            header.MouseLeftButtonDown += (s, e) =>          // в отдельном виджете за заголовок его можно тащить
            {
                e.Handled = true;
                if (!detached || detachedWindow == null || dLocked) return;
                try { detachedWindow.DragMove(); } catch (InvalidOperationException) { }
                Save();
            };
            sectionBody.Children.Add(header);

            // общая громкость
            var masterRow = new Border { Padding = new Thickness(8, 6, 10, 6), Margin = new Thickness(0, 0, 0, 6) };
            Glass.Apply(masterRow, 14);
            var mg = new Grid();
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            mg.ColumnDefinitions.Add(new ColumnDefinition());
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            masterIcon = new VolumeIcon(13, Brushes.White) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var masterButton = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Glass.Track, Child = masterIcon, Cursor = Cursors.Hand, ToolTip = L.T("Выключить или включить звук", "Mute or unmute") };
            masterButton.MouseLeftButtonUp += delegate
            {
                masterMuted = !masterMuted;
                audio.SetMasterMute(masterMuted);
                ShowMaster();
            };
            mg.Children.Add(masterButton);
            var masterTitle = new TextBlock { Text = L.T("Общая", "Master"), Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
            Grid.SetColumn(masterTitle, 1);
            mg.Children.Add(masterTitle);
            master = new PillSlider(14, 7, false) { VerticalAlignment = VerticalAlignment.Center, Background = Glass.Track };
            Grid.SetColumn(master, 2);
            mg.Children.Add(master);
            master.ValueChanged += v =>
            {
                audio.SetMaster((float)v);
                if (masterMuted && v > 0) { masterMuted = false; audio.SetMasterMute(false); }
                ShowMaster();
            };
            masterPercent = new TextBlock { Foreground = Ui.Faint, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(masterPercent, 3);
            mg.Children.Add(masterPercent);
            // стрелочка справа — выбор устройства вывода (наушники, монитор…)
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            outChevron = Ui.Glyph("", 10, Ui.Faint);
            var outButton = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Brushes.Transparent, Child = outChevron, Cursor = Cursors.Hand, Margin = new Thickness(4, 0, -4, 0), ToolTip = L.T("Устройство вывода", "Output device") };
            outButton.MouseEnter += delegate { outButton.Background = Ui.Hover; };
            outButton.MouseLeave += delegate { outButton.Background = Brushes.Transparent; };
            outButton.MouseLeftButtonDown += (s, e) => e.Handled = true;
            outButton.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                // клик по стрелочке при открытом списке сначала закрывает его (StaysOpen = false) — не открываем снова
                ShowOutputs(!outPopup.IsOpen && (DateTime.Now - outClosedAt).TotalMilliseconds > 300);
            };
            Grid.SetColumn(outButton, 4);
            mg.Children.Add(outButton);
            masterRow.Child = mg;
            sectionBody.Children.Add(masterRow);

            // список появляется справа от строки «Общая», отдельным всплывающим окошком в том же стиле
            outputsBox = new Border
            {
                Padding = new Thickness(4), MinWidth = 180, MaxWidth = 280, Child = outputs,
                Background = new LinearGradientBrush(Color.FromArgb(0xF2, 0x3A, 0x2A, 0x4A), Color.FromArgb(0xF2, 0x1C, 0x1C, 0x1E), 60),
                BorderBrush = Glass.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14)
            };
            outPopup = new Popup { PlacementTarget = masterRow, Placement = PlacementMode.Right, HorizontalOffset = 8, AllowsTransparency = true, StaysOpen = false, Child = outputsBox };
            outPopup.Closed += delegate { outChevron.Text = ""; outClosedAt = DateTime.Now; };

            sectionBody.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, MaxHeight = 240 });
            section.Children.Add(sectionBody);
            Children.Add(section);
            ApplyLayout();
        }

        // Связь с окном виджета: его масштаб и «поверх всех окон» достаются и отдельному микшеру.
        public void Setup(Window window, ScaleTransform scale)
        {
            owner = window;
            zoom = scale;
            owner.IsVisibleChanged += delegate { ApplyLayout(); };   // кнопка ♪ видна, только пока плеер скрыт
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    if (k != null)
                    {
                        if (k.GetValue("DScale") != null) SetDetachedScale(double.Parse((string)k.GetValue("DScale"), System.Globalization.CultureInfo.InvariantCulture));
                        dLocked = "1".Equals(k.GetValue("DLocked"));
                        dTopmost = !"0".Equals(k.GetValue("DTopmost"));
                    }
                    if (k != null && k.GetValue("DLeft") != null)
                    {
                        var inv = System.Globalization.CultureInfo.InvariantCulture;
                        savedLeft = double.Parse((string)k.GetValue("DLeft"), inv);
                        savedTop = double.Parse((string)k.GetValue("DTop"), inv);
                    }
                }
            }
            catch { }
            SetDetachedTopmost(dTopmost);
            SetDetachedLocked(dLocked);
        }

        double savedLeft = double.NaN, savedTop = double.NaN;

        public void SetArt(ImageSource art) { sectionArt.Source = art; }

        // Где по высоте начинается зазор между плеером и микшером (в координатах карточки), или NaN — если раздела нет.
        public double SplitY(UIElement card)
        {
            if (detached || !expanded || !section.IsVisible) return double.NaN;
            try { return section.TranslatePoint(new Point(0, 0), card).Y; }
            catch (InvalidOperationException) { return double.NaN; }
        }

        void SetExpanded(bool on)
        {
            if (!on) ShowOutputs(false);
            Expanded = on;
            if (ExpandedChanged != null) ExpandedChanged();
        }

        void ApplyLayout()
        {
            expander.Visibility = !detached && !expanded ? Visibility.Visible : Visibility.Collapsed;
            joinButton.Visibility = detached ? Visibility.Visible : Visibility.Collapsed;
            if (!detached) section.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            grabber.Visibility = detached ? Visibility.Collapsed : Visibility.Visible;
            // в виджете микшер растянут до краёв карточки, в отдельном окне — по своим полям
            section.Margin = detached ? new Thickness(0) : new Thickness(-AttachedMargin, 12, -AttachedMargin, -18);
            sectionBody.Margin = detached ? new Thickness(14, 12, 14, 8) : new Thickness(AttachedMargin, Gap + 2, AttachedMargin, 12);
            pinButton.Visibility = lockButton.Visibility = detached ? Visibility.Visible : Visibility.Collapsed;
            playerButton.Visibility = detached && owner != null && !owner.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            detachIcon.Data = detached ? OneBar : TwoBars;
            detachIcon.StrokeThickness = detached ? 3 : 2;
            detachButton.ToolTip = detached ? L.T("Вернуть в «Сейчас играет»", "Back into Now Playing") : L.T("Отделить в отдельный виджет", "Detach into a separate widget");
        }

        // Раскрывает или прячет список устройств вывода; выбранное становится устройством по умолчанию.
        void ShowOutputs(bool show)
        {
            outChevron.Text = show ? "" : "";
            if (!show) { outPopup.IsOpen = false; return; }
            outputsBox.LayoutTransform = detached ? dZoom : zoom;   // тот же масштаб, что у микшера
            outputs.Children.Clear();
            string current;
            var devices = OutputDevices.List(out current);
            foreach (var d in devices)
            {
                string id = d.Key;
                bool on = id == current;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(new TextBlock
                {
                    Text = d.Value, Foreground = on ? Brushes.White : Ui.Faint, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
                    FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center
                });
                if (on)
                {
                    var check = Ui.Glyph("", 11, Brushes.White);
                    check.Margin = new Thickness(8, 0, 0, 0);
                    Grid.SetColumn(check, 1);
                    g.Children.Add(check);
                }
                var row = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = g };
                row.MouseEnter += delegate { row.Background = Ui.Hover; };
                row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
                row.MouseLeftButtonDown += (s, e) => e.Handled = true;
                row.MouseLeftButtonUp += (s, e) => { e.Handled = true; OutputDevices.SetDefault(id); ShowOutputs(false); };
                outputs.Children.Add(row);
            }
            if (devices.Count == 0)
                outputs.Children.Add(new TextBlock { Text = L.T("Нет устройств вывода", "No output devices"), Foreground = Ui.Faint, FontSize = 12, Margin = new Thickness(8, 6, 8, 6) });
            outPopup.IsOpen = true;
        }

        Border HeaderButton(string glyph, Action click, out TextBlock text)
        {
            var t = Ui.Glyph(glyph, 10, Brushes.White);
            var b = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.ButtonBg, Child = t, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0) };
            b.MouseLeftButtonDown += (s, e) => e.Handled = true;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            text = t;
            return b;
        }

        // Включённая кнопка — белая с тёмным значком, как в остальных виджетах.
        static void Highlight(Border b, TextBlock t, bool on)
        {
            b.Background = on ? Ui.ActiveBg : Ui.ButtonBg;
            t.Foreground = on ? Ui.ActiveGlyph : Brushes.White;
        }

        void SetDetachedTopmost(bool on)
        {
            dTopmost = on;
            pinGlyph.Text = on ? "" : "";
            Highlight(pinButton, pinGlyph, on);
            pinButton.ToolTip = on ? L.T("Поверх всех окон: вкл", "Always on top: on") : L.T("Поверх всех окон: выкл", "Always on top: off");
            if (detachedWindow != null) detachedWindow.Topmost = on;
            Save();
        }

        void SetDetachedLocked(bool on)
        {
            dLocked = on;
            lockGlyph.Text = on ? Ui.LockGlyph : Ui.UnlockGlyph;
            Highlight(lockButton, lockGlyph, on);
            lockButton.ToolTip = on ? L.T("Замок закрыт: нажмите, чтобы снова двигать и менять размер", "Locked: click to move and resize again") : L.T("Замок: заморозить место и размер", "Lock position and size");
            if (dGrip != null) dGrip.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            Save();
        }

        void SetDetachedScale(double s)
        {
            dScale = Math.Max(0.6, Math.Min(1.8, Math.Round(s, 2)));
            dZoom.ScaleX = dZoom.ScaleY = dScale;
        }

        // после изменения размера возвращаем отделённый микшер на экран (прозрачные поля — 18 px)
        void KeepDetachedOnScreen()
        {
            var w = detachedWindow;
            var area = Ui.WorkAreaOf(w);           // монитор, где стоит микшер, а не только основной
            double m = 18 * dScale;
            if (w.Left + w.ActualWidth - m > area.Right) w.Left = area.Right - w.ActualWidth + m;
            if (w.Top + w.ActualHeight - m > area.Bottom) w.Top = area.Bottom - w.ActualHeight + m;
            if (w.Left + m < area.Left) w.Left = area.Left - m;
            if (w.Top + m < area.Top) w.Top = area.Top - m;
        }

        void Detach()
        {
            if (detached) return;
            detached = true;
            Children.Remove(section);
            if (detachedWindow == null) CreateDetachedWindow();
            detachedHost.Children.Add(section);
            section.Visibility = Visibility.Visible;
            ApplyLayout();
            if (double.IsNaN(detachedWindow.Left) && !double.IsNaN(savedLeft)) { detachedWindow.Left = savedLeft; detachedWindow.Top = savedTop; }
            bool place = double.IsNaN(detachedWindow.Left);
            detachedWindow.Topmost = dTopmost;
            detachedWindow.Show();
            if (place && owner != null)
            {
                // первый раз — сразу под плеером; ждём, пока плеер нарисуется и узнает свою высоту
                owner.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var area = Ui.WorkAreaOf(owner);
                    detachedWindow.Left = owner.Left;
                    detachedWindow.Top = Math.Min(owner.Top + owner.ActualHeight - 30, area.Bottom - detachedWindow.ActualHeight + 18);
                    Save();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            Save();
        }

        // «Скрыть плеер»: микшер должен остаться на экране — отделяем его, если он внутри плеера.
        public void KeepMixerVisible()
        {
            if (!detached) Detach();
        }

        public event Action PlayerMoved;

        // playerComesToMixer: соединяют из микшера — микшер остаётся на месте, плеер встаёт над ним;
        // соединяют из плеера — плеер на месте, микшер появляется под ним.
        void Attach(bool playerComesToMixer)
        {
            if (!detached) return;
            Point? anchor = null;
            if (playerComesToMixer && section.IsVisible)
                try { anchor = section.PointToScreen(new Point(0, 0)); } catch (InvalidOperationException) { }
            // возвращаемся в скрытый плеер — сначала показываем его, иначе микшер пропадёт вместе с ним
            if (owner != null && !owner.IsVisible && ShowPlayerRequested != null) ShowPlayerRequested();
            detached = false;
            detachedHost.Children.Remove(section);
            detachedWindow.Hide();
            Children.Add(section);
            expanded = true;
            ApplyLayout();
            Save();
            if (ExpandedChanged != null) ExpandedChanged();
            if (anchor == null || owner == null) return;

            // когда плеер перерисуется, сдвигаем его так, чтобы микшер оказался там же, где стоял
            var target = anchor.Value;
            owner.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var now = section.PointToScreen(new Point(0, 0));
                    double k = PresentationSource.FromVisual(owner).CompositionTarget.TransformToDevice.M11;
                    owner.Left += (target.X - now.X) / k;
                    owner.Top += (target.Y - now.Y) / k;
                    if (PlayerMoved != null) PlayerMoved();
                }
                catch (InvalidOperationException) { }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        void CreateDetachedWindow()
        {
            var w = new Window
            {
                Title = L.T("Звук", "Sound"), WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight,
                FontFamily = Ui.Text, UseLayoutRounding = true, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = double.NaN, Top = double.NaN
            };
            var holder = new Grid { Margin = new Thickness(18) };
            holder.Children.Add(new Border
            {
                Background = Ui.Base, CornerRadius = new CornerRadius(24),
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.5, Color = Colors.Black }
            });
            detachedHost = new Grid();
            detachedHost.SizeChanged += delegate { detachedHost.Clip = new RectangleGeometry(new Rect(detachedHost.RenderSize), 24, 24); };
            holder.Children.Add(detachedHost);
            holder.Children.Add(new Border { BorderBrush = Glass.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(24), IsHitTestVisible = false });
            // уголок ↘ для изменения размера, как у основных виджетов
            dGrip = new ResizeGrip { Margin = new Thickness(0, 0, 6, 6), Opacity = 0, Visibility = dLocked ? Visibility.Collapsed : Visibility.Visible };
            dGrip.DragStarted += () => dDragStart = dScale;
            dGrip.Dragged += f => SetDetachedScale(dDragStart * f);
            dGrip.DragFinished += () => { KeepDetachedOnScreen(); Save(); };
            holder.Children.Add(dGrip);
            w.MouseEnter += delegate { if (!dLocked) dGrip.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(150))); };
            w.MouseLeave += delegate { dGrip.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250))); };
            w.PreviewMouseWheel += (s, e) =>
            {
                if (dLocked || Keyboard.Modifiers != ModifierKeys.Control) return;
                e.Handled = true;
                SetDetachedScale(dScale + (e.Delta > 0 ? 0.05 : -0.05));
                Save();
            };
            w.SizeChanged += delegate { if (Mouse.LeftButton != MouseButtonState.Pressed) KeepDetachedOnScreen(); };
            var root = new Grid { Width = 360, LayoutTransform = dZoom, Children = { holder } };
            w.Content = root;
            w.Closing += (s, e) => { e.Cancel = true; Attach(true); };   // Alt+F4 в микшере — плеер приезжает к нему
            detachedWindow = w;
        }

        void Save()
        {
            try
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    k.SetValue("DScale", dScale.ToString(inv));
                    k.SetValue("DLocked", dLocked ? "1" : "0");
                    k.SetValue("DTopmost", dTopmost ? "1" : "0");
                    if (detachedWindow != null && !double.IsNaN(detachedWindow.Left))
                    {
                        k.SetValue("DLeft", detachedWindow.Left.ToString(inv));
                        k.SetValue("DTop", detachedWindow.Top.ToString(inv));
                    }
                }
            }
            catch { }
        }

        void ShowMaster()
        {
            master.Muted = masterMuted;
            masterPercent.Text = masterMuted ? L.T("Выкл.", "Off") : MixUi.Percent(master.Value);
            masterIcon.Set(master.Value, masterMuted);
        }

        // Как приложение-плеер называется среди звуковых процессов Windows.
        static string ExeHint(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return null;
            var low = appId.ToLowerInvariant();
            if (low.Contains("opera")) return "opera";
            if (low.Contains("chrome")) return "chrome";
            if (low.Contains("msedge") || low.Contains("edge")) return "msedge";
            if (low.Contains("firefox")) return "firefox";
            if (low.Contains("spotify")) return "spotify";
            if (low.Contains("yandex")) return "browser";
            var name = low.Split('\\', '!').Last();
            return name.EndsWith(".exe") ? name.Substring(0, name.Length - 4) : name;
        }

        MiniRow MakeRow(AudioSession first)
        {
            return new MiniRow(first.Name, first.IsSystem ? null : GetIcon(first.ExePath), first.IsSystem);
        }

        // Вызывается из таймера виджета. appId — приложение, где сейчас играет трек (или null).
        public void Refresh(string appId)
        {
            try
            {
                if (audio.EnsureDevice()) rows.Clear();

                float level;
                bool muted;
                if (audio.GetMaster(out level, out muted))
                {
                    masterMuted = muted;
                    master.Set(level);
                    ShowMaster();
                }

                var groups = new Dictionary<string, List<AudioSession>>();
                foreach (var s in audio.GetSessions())
                {
                    List<AudioSession> g;
                    if (!groups.TryGetValue(s.Key, out g)) groups[s.Key] = g = new List<AudioSession>();
                    g.Add(s);
                }

                // строка приложения, где играет трек
                string hint = ExeHint(appId), key = null;
                if (hint != null)
                    foreach (var kv in groups)
                    {
                        var exe = kv.Value[0].ExePath == null ? "" : Path.GetFileNameWithoutExtension(kv.Value[0].ExePath).ToLowerInvariant();
                        if (exe.Length > 0 && (exe.Contains(hint) || hint.Contains(exe))) { key = kv.Key; break; }
                    }
                nowHost.Visibility = key == null ? Visibility.Collapsed : Visibility.Visible;
                if (key != null)
                {
                    nowVolume.SetApp(groups[key][0].Name, groups[key].Select(x => x.Volume).ToList(),
                                     hint == "opera" || hint == "chrome" || hint == "msedge" || hint == "firefox" || hint == "browser");
                    nowVolume.Update();
                }

                // микшер: все приложения со звуком
                bool changed = false;
                foreach (var k in rows.Keys.ToList())
                    if (!groups.ContainsKey(k)) { rows.Remove(k); changed = true; }
                foreach (var kv in groups)
                {
                    MiniRow row;
                    if (!rows.TryGetValue(kv.Key, out row)) { rows[kv.Key] = row = MakeRow(kv.Value[0]); changed = true; }
                    row.SetVolumes(kv.Value.Select(x => x.Volume).ToList());
                    row.Update();
                }
                if (changed)
                {
                    list.Children.Clear();
                    foreach (var r in rows.Values.OrderBy(r => r.IsSystem).ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                        list.Children.Add(r);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        ImageSource GetIcon(string path)
        {
            ImageSource image;
            if (path == null) return null;
            if (icons.TryGetValue(path, out image)) return image;
            try
            {
                var handles = new IntPtr[1];
                var ids = new uint[1];
                uint n = Native.PrivateExtractIcons(path, 0, 64, 64, handles, ids, 1, 0);
                if (n > 0 && n != uint.MaxValue && handles[0] != IntPtr.Zero)
                {
                    var bs = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bs.Freeze();
                    image = bs;
                    Native.DestroyIcon(handles[0]);
                }
            }
            catch { }
            icons[path] = image;
            return image;
        }
    }

    // ---------------- Устройства вывода (как в утилите AudioOut) ----------------

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOutEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IOutCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IOutDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOutCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IOutDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOutDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IOutProperties props);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct OutPropertyKey { public Guid Format; public int Id; }

    [StructLayout(LayoutKind.Sequential)]
    struct OutPropVariant
    {
        public short Type;
        short reserved1, reserved2, reserved3;
        public IntPtr Value;
        IntPtr value2;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOutProperties
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out OutPropertyKey key);
        [PreserveSig] int GetValue(ref OutPropertyKey key, out OutPropVariant value);
    }

    // Недокументированный, но стабильный с Windows 7 интерфейс, которым пользуется сама панель «Звук».
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    class PolicyConfigCom { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
    }

    static class OutputDevices
    {
        static OutPropertyKey friendlyName = new OutPropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref OutPropVariant pvar);

        // Подключённые устройства вывода: id → название; current — id устройства по умолчанию.
        public static List<KeyValuePair<string, string>> List(out string current)
        {
            var result = new List<KeyValuePair<string, string>>();
            current = null;
            try
            {
                var enumerator = (IOutEnumerator)new MMDeviceEnumeratorCom();
                IOutDevice def;
                if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out def) == 0) def.GetId(out current);
                IOutCollection devices;
                if (enumerator.EnumAudioEndpoints(0, 1 /* только включённые */, out devices) != 0) return result;
                uint count;
                devices.GetCount(out count);
                for (uint i = 0; i < count; i++)
                {
                    IOutDevice d;
                    string id;
                    if (devices.Item(i, out d) != 0 || d.GetId(out id) != 0) continue;
                    result.Add(new KeyValuePair<string, string>(id, Name(d)));
                }
            }
            catch { }
            return result;
        }

        // Делает устройство основным для всех ролей (игры, музыка, звонки) — как выбор в панели «Звук».
        public static void SetDefault(string id)
        {
            try
            {
                var policy = (IPolicyConfig)new PolicyConfigCom();
                for (int role = 0; role < 3; role++) policy.SetDefaultEndpoint(id, role);
            }
            catch { }
        }

        static string Name(IOutDevice d)
        {
            IOutProperties store;
            if (d.OpenPropertyStore(0 /* STGM_READ */, out store) != 0) return "?";
            OutPropVariant v;
            if (store.GetValue(ref friendlyName, out v) != 0) return "?";
            string name = v.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(v.Value) : "?";
            PropVariantClear(ref v);
            return name;
        }
    }
}
