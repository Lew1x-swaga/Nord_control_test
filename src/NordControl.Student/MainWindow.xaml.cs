using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NordControl.Core;
using NordControl.Protocol;
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

    private static readonly SolidColorBrush BrushOnline = FreezeRgb(16, 185, 129);
    private static readonly SolidColorBrush BrushReconnect = FreezeRgb(217, 119, 6);
    private static readonly SolidColorBrush BrushIdle = FreezeRgb(148, 163, 184);
    private static readonly SolidColorBrush BrushSearch = FreezeRgb(37, 99, 235);

    private bool _hasJoinedClass;
    private bool _allowClose;
    private System.Windows.Forms.ToolStripMenuItem? _leaveLessonItem;

    private static SolidColorBrush FreezeRgb(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public MainWindow()
    {
        InitializeComponent();

        InitializeTrayIcon();

        Loaded += (_, _) =>
        {
            Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
            Top = 15;
            PinTextBox.MaxLength = ProtocolConstants.PinLength;
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
            Dispatcher.Invoke(ShowFromTray);
        };

        var leaveItem = new System.Windows.Forms.ToolStripMenuItem("Покинуть урок…");
        leaveItem.Enabled = false;
        leaveItem.Click += (_, _) =>
        {
            Dispatcher.Invoke(TryLeaveLesson);
        };
        _leaveLessonItem = leaveItem;

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Выйти из программы…");
        exitItem.Click += (_, _) =>
        {
            Dispatcher.Invoke(TryQuitApplication);
        };

        contextMenu.Items.Add(statusItem);
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add(leaveItem);
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) =>
        {
            Dispatcher.Invoke(ShowFromTray);
        };
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var pin = PinCode.Normalize(PinTextBox.Text);
        if (!PinCode.IsWellFormed(pin))
        {
            ErrorTextBlock.Text = "PIN: 3 буквы и 3 цифры";
            PinTextBox.Focus();
            return;
        }

        PinTextBox.Text = pin;

        ErrorTextBlock.Text = "";
        StatusCaptionTextBlock.Text = "Поиск учителя";
        StatusHeaderTextBlock.Text = "Nord Control — поиск учителя";
        StatusIndicator.Fill = BrushSearch;
        StatusHalo.Fill = BrushSearch;
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
                    Topmost = true;
                    StatusCaptionTextBlock.Text = "Урок идет";
                    StatusHeaderTextBlock.Text = "Урок · учитель на связи";
                    StatusIndicator.Fill = BrushOnline;
                    StatusHalo.Fill = BrushOnline;
                    PinTextBox.IsEnabled = false;
                    TeacherIpTextBox.IsEnabled = false;
                    ConnectButton.IsEnabled = false;
                    CloseButton.ToolTip = "Свернуть в трей";
                    ErrorTextBlock.Text = "";
                    if (_leaveLessonItem != null)
                    {
                        _leaveLessonItem.Enabled = true;
                    }
                    break;

                case SessionStatus.Reconnecting:
                    Topmost = true;
                    StatusCaptionTextBlock.Text = "Переподключение";
                    StatusHeaderTextBlock.Text = "Нет связи с учителем · переподключение…";
                    StatusIndicator.Fill = BrushReconnect;
                    StatusHalo.Fill = BrushReconnect;
                    break;

                case SessionStatus.Ended:
                case SessionStatus.Idle:
                default:
                    Topmost = false;
                    StatusCaptionTextBlock.Text = "Ожидание";
                    StatusHeaderTextBlock.Text = "Nord Control — ожидание класса";
                    StatusIndicator.Fill = BrushIdle;
                    StatusHalo.Fill = BrushIdle;
                    PinTextBox.IsEnabled = true;
                    TeacherIpTextBox.IsEnabled = true;
                    ConnectButton.IsEnabled = true;
                    CloseButton.ToolTip = "Закрыть";
                    _hasJoinedClass = false;
                    if (_leaveLessonItem != null)
                    {
                        _leaveLessonItem.Enabled = false;
                    }
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
                StatusCaptionTextBlock.Text = "Ожидание";
                StatusHeaderTextBlock.Text = "Nord Control — ожидание класса";
                StatusIndicator.Fill = BrushIdle;
                StatusHalo.Fill = BrushIdle;
            }
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            ShutdownAgent();
            base.OnClosing(e);
            return;
        }

        if (_hasJoinedClass)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _allowClose = true;
        ShutdownAgent();
        base.OnClosing(e);
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private bool Confirm(string title, string caption, string body, string confirmText)
    {
        ShowFromTray();
        var dialog = new ConfirmDialog(this, title, caption, body, confirmText);
        return dialog.ShowDialog() == true && dialog.Confirmed;
    }

    private void TryLeaveLesson()
    {
        if (!_hasJoinedClass)
        {
            return;
        }

        if (!Confirm(
                "Покинуть урок?",
                "Сессия с учителем",
                "Связь с классом прервётся, блокировки снимутся. Агент останется запущен — можно войти в другой класс.",
                "Покинуть"))
        {
            return;
        }

        DisconnectFromClass();
    }

    private void TryQuitApplication()
    {
        if (_hasJoinedClass)
        {
            if (!Confirm(
                    "Выйти из программы?",
                    "Агент ученика",
                    "Программа закроется. Если урок ещё идёт, учитель потеряет этого ученика, блокировки снимутся.",
                    "Выйти"))
            {
                return;
            }
        }

        _allowClose = true;
        Close();
    }

    private void DisconnectFromClass()
    {
        _clientCts?.Cancel();
        _clientCts?.Dispose();
        _clientCts = null;
        _client?.Dispose();
        _client = null;
        _hasJoinedClass = false;
        if (_leaveLessonItem != null)
        {
            _leaveLessonItem.Enabled = false;
        }

        Topmost = false;
        PinTextBox.IsEnabled = true;
        TeacherIpTextBox.IsEnabled = true;
        ConnectButton.IsEnabled = true;
        CloseButton.ToolTip = "Закрыть";
        StatusCaptionTextBlock.Text = "Ожидание";
        StatusHeaderTextBlock.Text = "Nord Control — ожидание класса";
        StatusIndicator.Fill = BrushIdle;
        StatusHalo.Fill = BrushIdle;
        ErrorTextBlock.Text = "";
        ShowFromTray();
    }

    private void ShutdownAgent()
    {
        _notifyIcon?.Dispose();
        _clientCts?.Cancel();
        _client?.Dispose();
        _screenCapturer.Dispose();
    }

    private void Capsule_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.TextBox)
            return;
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AdvancedToggle_Changed(object sender, RoutedEventArgs e)
    {
        AdvancedPanel.Visibility = AdvancedToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }
}
