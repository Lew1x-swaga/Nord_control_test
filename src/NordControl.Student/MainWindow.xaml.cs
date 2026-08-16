using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using NordControl.Core;
using NordControl.Student.Capture;
using NordControl.Student.Services;
using MediaColor = System.Windows.Media.Color;

namespace NordControl.Student;

public partial class MainWindow : Window
{
    private ClassClient? _client;
    private CancellationTokenSource? _clientCts;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private readonly IScreenCapturer _screenCapturer = new DxgiScreenCapturer();
    private readonly ProcessMonitor _processMonitor = new();

    private bool _hasJoinedClass;
    private string? _currentClassPin;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        InitializeTrayIcon();

        Loaded += (_, _) =>
        {
            Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
            Top = 15;
            PinTextBox.Focus();
        };
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Nord Control — ученик"
        };

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();

        var statusItem = new System.Windows.Forms.ToolStripMenuItem("Статус");
        statusItem.Click += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });
        };

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Выйти…");
        exitItem.Click += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                Close();
            });
        };

        contextMenu.Items.Add(statusItem);
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });
        };
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var pin = PinTextBox.Text.Trim();
        if (string.IsNullOrEmpty(pin) || pin.Length != 4)
        {
            ErrorTextBlock.Text = "Введите 4-значный PIN";
            PinTextBox.Focus();
            return;
        }

        ErrorTextBlock.Text = "";
        var manualIp = TeacherIpTextBox.Text.Trim();
        if (string.IsNullOrEmpty(manualIp))
        {
            manualIp = null;
        }

        _clientCts?.Cancel();
        _clientCts?.Dispose();
        _client?.Dispose();

        _clientCts = new CancellationTokenSource();
        _client = new ClassClient(pin, manualTeacherIp: manualIp)
        {
            CaptureFrameCallback = (ct) => _screenCapturer.CaptureFrameAsync(maxDimension: 1280, quality: 70, ct: ct),
            ProcessListCallback = () => _processMonitor.CollectProcessList(ProcessMonitor.MaxItems)
        };
        _client.StatusChanged += OnClientStatusChanged;
        _client.Error += OnClientError;

        var token = _clientCts.Token;
        _ = Task.Run(() => _client.RunAsync(token), token);
    }

    private void OnClientStatusChanged(StudentSession session)
    {
        Dispatcher.InvokeAsync(() =>
        {
            switch (session.Status)
            {
                case SessionStatus.Online:
                    _hasJoinedClass = true;
                    _currentClassPin = _client?.Pin;
                    Topmost = true;
                    StatusHeaderTextBlock.Text = "Урок · учитель на связи";
                    StatusIndicator.Fill = new SolidColorBrush(MediaColor.FromRgb(16, 185, 129));
                    PinTextBox.IsEnabled = false;
                    TeacherIpTextBox.IsEnabled = false;
                    ConnectButton.IsEnabled = false;
                    ErrorTextBlock.Text = "";
                    break;

                case SessionStatus.Reconnecting:
                    Topmost = true;
                    StatusHeaderTextBlock.Text = "Нет связи с учителем · переподключение…";
                    StatusIndicator.Fill = new SolidColorBrush(MediaColor.FromRgb(217, 119, 6));
                    break;

                case SessionStatus.Ended:
                case SessionStatus.Idle:
                default:
                    Topmost = false;
                    StatusHeaderTextBlock.Text = "Nord Control — ожидание класса";
                    StatusIndicator.Fill = new SolidColorBrush(MediaColor.FromRgb(148, 163, 184));
                    PinTextBox.IsEnabled = true;
                    TeacherIpTextBox.IsEnabled = true;
                    ConnectButton.IsEnabled = true;
                    break;
            }
        });
    }

    private void OnClientError(string errorMessage)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ErrorTextBlock.Text = errorMessage;
            if (_client?.Session.Status == SessionStatus.Idle)
            {
                PinTextBox.IsEnabled = true;
                TeacherIpTextBox.IsEnabled = true;
                ConnectButton.IsEnabled = true;
            }
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            _notifyIcon?.Dispose();
            _clientCts?.Cancel();
            _client?.Dispose();
            _screenCapturer.Dispose();
            base.OnClosing(e);
            return;
        }

        if (_hasJoinedClass && !string.IsNullOrEmpty(_currentClassPin))
        {
            var pinDialog = new PinDialog(_currentClassPin, this);
            var result = pinDialog.ShowDialog();

            if (result == true && pinDialog.IsPinCorrect)
            {
                _allowClose = true;
                _notifyIcon?.Dispose();
                _clientCts?.Cancel();
                _client?.Dispose();
                _screenCapturer.Dispose();
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            return;
        }

        _allowClose = true;
        _notifyIcon?.Dispose();
        _clientCts?.Cancel();
        _client?.Dispose();
        _screenCapturer.Dispose();
        base.OnClosing(e);
    }
}
