using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NowPlaying
{
    class MiniChecks
    {
        static int checks;
        static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
        static T Field<T>(TaskbarMini mini, string name) { return (T)typeof(TaskbarMini).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mini); }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [STAThread] static void Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Rect bar = new Rect(0, 1032, 1920, 48), tray = new Rect(1600, 1032, 320, 48);
            var target = TaskbarMini.Placement(bar, tray, 144, 38, 0);
            Check(target.Right < tray.Left && target.Top >= bar.Top && target.Bottom <= bar.Bottom, "Fits left of tray within taskbar");
            Check(TaskbarMini.Placement(bar, tray, 144, 38, 100).Left == target.Left - 100, "Horizontal shift remains relative to tray");
            Check(TaskbarMini.Placement(bar, tray, 144, 38, 10000).Left >= bar.Left, "Large shift stays on screen");
            Check(TaskbarMini.Placement(new Rect(-1920,0,1920,48),new Rect(-300,0,300,48),144,38,0).Right < -300, "Negative monitor coordinates");
            Check(TaskbarMini.Placement(new Rect(0,0,3840,96),new Rect(3200,0,640,96),288,76,0).Bottom <= 96, "Geometry at 200 percent scale");
            int restored = 0, back = 0, pause = 0, forward = 0;
            var mini = new TaskbarMini(() => restored++, () => back++, () => pause++, () => forward++);
            mini.Update(null, "Test", false, false, false, false);
            Check(!Field<Button>(mini,"play").IsEnabled && !Field<Button>(mini,"previous").IsEnabled, "Controls disabled with no media");
            var drawing = new DrawingVisual();
            using(var dc = drawing.RenderOpen()) {
                dc.DrawRectangle(new LinearGradientBrush(Colors.OrangeRed, Colors.MidnightBlue,45),null,new Rect(0,0,64,64));
                dc.DrawEllipse(Brushes.Black,null,new Point(32,32),20,20);
                dc.DrawEllipse(Brushes.Orange,null,new Point(32,32),5,5);
            }
            var art = new RenderTargetBitmap(64,64,96,96,PixelFormats.Pbgra32); art.Render(drawing); art.Freeze();
            mini.Update(art,"Test track — test artist",true,true,true,true);
            Check(Field<Border>(mini,"cover").Child == null, "Artwork replaces placeholder");
            Check(Field<System.Windows.Shapes.Path>(mini,"playIcon").Data == Ui.Pause, "Playing state displays pause");
            foreach(string name in new [] {"previous","play","next"}) Field<Button>(mini,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(back==1 && pause==1 && forward==1, "All three buttons invoke the intended actions");
            Check(!mini.ShowInTaskbar && !mini.ShowActivated, "No taskbar button and no activation on show");
            IntPtr before = GetForegroundWindow(); mini.Present();
            mini.Measure(new Size(144,38)); mini.Arrange(new Rect(0,0,144,38)); mini.UpdateLayout();
            var preview = new RenderTargetBitmap(576,152,384,384,PixelFormats.Pbgra32); preview.Render(mini);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview));
            using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mini-preview.png"))) encoder.Save(file);
            var hwnd = new WindowInteropHelper(mini).Handle;
            Console.WriteLine("Live mini window visible: " + mini.IsVisible);
            Check((GetWindowLong(hwnd,-20)&0x08000000)!=0, "Native no-activate style is enabled");
            Check(GetForegroundWindow()==before, "Present preserves foreground application");
            mini.Dismiss(); Check(!mini.IsVisible,"Dismiss hides mini player");
            mini.Finish(); app.Shutdown();
            Console.WriteLine(checks+" checks passed. No real media commands were sent.");
        }
    }
}
