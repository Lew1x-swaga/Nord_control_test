using System;
using System.Windows;
using NordControl.Student.Services;
using WpfApplication = System.Windows.Application;

namespace NordControl.Student;

public partial class TeacherNoticeWindow : Window
{
    private static TeacherNoticeWindow? _instance;
    private static readonly object Lock = new();

    public TeacherNoticeWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Reposition();
    }

    public void SetMessage(string text)
    {
        BodyTextBlock.Text = text;
        if (IsLoaded)
        {
            UpdateLayout();
            Reposition();
        }
    }

    public void Reposition()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + Math.Max(0, (workArea.Width - ActualWidth) / 2);
        Top = workArea.Top + 96;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public static void ShowNotice(string id, string text)
    {
        WpfApplication.Current?.Dispatcher.InvokeAsync(() =>
        {
            lock (Lock)
            {
                if (_instance == null)
                {
                    _instance = new TeacherNoticeWindow();
                    _instance.Closed += (_, _) =>
                    {
                        lock (Lock)
                        {
                            _instance = null;
                        }
                    };
                    _instance.SetMessage(text);
                    _instance.Show();
                }
                else
                {
                    _instance.SetMessage(text);
                    if (!_instance.IsVisible)
                    {
                        _instance.Show();
                    }
                }
            }

            SoundNotification.PlayDing(id);
        });
    }

    public static void CloseNotice()
    {
        WpfApplication.Current?.Dispatcher.InvokeAsync(() =>
        {
            lock (Lock)
            {
                if (_instance == null)
                {
                    return;
                }

                var current = _instance;
                _instance = null;
                try
                {
                    current.Close();
                }
                catch
                {
                }
            }
        });
    }
}
