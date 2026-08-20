using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using NordControl.Student.Services;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace NordControl.Student;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer;

    public ToastWindow(string title, string message, bool isAlert = true)
    {
        InitializeComponent();

        ToastTitleTextBlock.Text = title;
        ToastMessageTextBlock.Text = message;

        if (!isAlert)
        {
            try
            {
                if (TryFindResource("Icon.Monitor") is Geometry monitorGeom)
                {
                    ToastIcon.Data = monitorGeom;
                }
                if (TryFindResource("Brush.Emerald") is WpfBrush emeraldBrush)
                {
                    ToastIcon.Fill = emeraldBrush;
                }

                ToastIconChrome.Background = new SolidColorBrush(MediaColor.FromRgb(6, 78, 59));
                ToastTitleTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(167, 243, 208));
                ToastChrome.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(16, 185, 129));
            }
            catch
            {
            }
        }

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(isAlert ? 4.5 : 5.0)
        };
        _timer.Tick += (s, e) =>
        {
            _timer.Stop();
            Close();
        };

        Loaded += ToastWindow_Loaded;
    }

    private void ToastWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + Math.Max(0, (workArea.Width - ActualWidth) / 2);
        Top = workArea.Top + 110;
        _timer.Start();
    }

    public static void ShowToast(string title, string message, bool isAlert = true, string? soundSubject = null)
    {
        WpfApplication.Current?.Dispatcher.InvokeAsync(() =>
        {
            var toast = new ToastWindow(title, message, isAlert);
            toast.Show();
            SoundNotification.PlayDing(soundSubject);
        });
    }
}
