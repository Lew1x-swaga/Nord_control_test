using System;
using System.Windows;
using WpfApplication = System.Windows.Application;

namespace NordControl.Student;

public partial class ScreenWatcherBannerWindow : Window
{
    private static ScreenWatcherBannerWindow? _instance;
    private static readonly object Lock = new();

    public ScreenWatcherBannerWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Reposition();
    }

    public void Reposition()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 16;
        Top = workArea.Bottom - ActualHeight - 16;
    }

    public static void SetStreamingState(bool isStreaming)
    {
        WpfApplication.Current?.Dispatcher.InvokeAsync(() =>
        {
            lock (Lock)
            {
                if (isStreaming)
                {
                    if (_instance == null)
                    {
                        _instance = new ScreenWatcherBannerWindow();
                        _instance.Closed += (_, _) =>
                        {
                            lock (Lock)
                            {
                                _instance = null;
                            }
                        };
                        _instance.Show();
                    }
                    else
                    {
                        _instance.Reposition();
                        if (!_instance.IsVisible)
                        {
                            _instance.Show();
                        }
                    }
                }
                else
                {
                    if (_instance != null)
                    {
                        var current = _instance;
                        _instance = null;
                        current.Close();
                    }
                }
            }
        });
    }

    public static void CloseBanner()
    {
        SetStreamingState(false);
    }
}
