using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NordControl.Core;
using NordControl.Core.Policies;
using NordControl.Protocol;

namespace NordControl.Teacher;

public class StudentItemViewModel : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private string _hostname = string.Empty;
    private StudentHubStatus _status;

    public string Id { get; init; } = string.Empty;

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName != value)
            {
                _displayName = value;
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Hostname
    {
        get => _hostname;
        set
        {
            if (_hostname != value)
            {
                _hostname = value;
                OnPropertyChanged(nameof(Hostname));
            }
        }
    }

    public StudentHubStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusForeground));
                OnPropertyChanged(nameof(StatusBackground));
            }
        }
    }

    public string StatusText => Status switch
    {
        StudentHubStatus.Online => "онлайн",
        StudentHubStatus.Reconnecting => "переподключение",
        _ => "нет"
    };

    public Brush StatusForeground => Status switch
    {
        StudentHubStatus.Online => new SolidColorBrush(Color.FromRgb(16, 185, 129)),       // Emerald-500
        StudentHubStatus.Reconnecting => new SolidColorBrush(Color.FromRgb(217, 119, 6)),  // Amber-600
        _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))                             // Slate-400
    };

    public Brush StatusBackground => Status switch
    {
        StudentHubStatus.Online => new SolidColorBrush(Color.FromRgb(236, 253, 245)),      // Emerald-50
        StudentHubStatus.Reconnecting => new SolidColorBrush(Color.FromRgb(254, 243, 199)),// Amber-50
        _ => new SolidColorBrush(Color.FromRgb(241, 245, 249))                             // Slate-100
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public partial class MainWindow : Window
{
    private readonly ClassHub _hub;
    private readonly ObservableCollection<StudentItemViewModel> _students = new();
    private readonly ObservableCollection<ProcessItemInfo> _processes = new();
    private readonly ObservableCollection<InstalledAppInfo> _quickApps = new();
    private readonly ObservableCollection<string> _blockedApps = new();
    private readonly ObservableCollection<InstalledAppInfo> _selectedStudentHints = new();
    private readonly ConcurrentDictionary<string, List<InstalledAppInfo>> _hintsByStudent = new();

    private bool _isClosingInProgress;

    public MainWindow()
    {
        InitializeComponent();

        _hub = new ClassHub();
        _hub.StudentJoined += OnStudentJoined;
        _hub.StudentStatusChanged += OnStudentStatusChanged;
        _hub.StudentLeft += OnStudentLeft;
        _hub.ScreenFrameReceived += OnScreenFrameReceived;
        _hub.ProcessListReceived += OnProcessListReceived;
        _hub.InstalledHintsReceived += OnInstalledHintsReceived;

        StudentsListBox.ItemsSource = _students;
        ProcessesListView.ItemsSource = _processes;
        QuickAppsListBox.ItemsSource = _quickApps;
        BlockedAppsListBox.ItemsSource = _blockedApps;
        HintsListView.ItemsSource = _selectedStudentHints;

        LoadPreset();
        GenerateNewPin();
        UpdateUiState(isRunning: false);
    }

    private void LoadPreset()
    {
        try
        {
            var preset = TeacherPresetManager.Load();
            _quickApps.Clear();
            if (preset.QuickApps != null)
            {
                foreach (var app in preset.QuickApps)
                {
                    _quickApps.Add(app);
                }
            }

            _blockedApps.Clear();
            if (preset.BlockedApps != null)
            {
                foreach (var exe in preset.BlockedApps)
                {
                    _blockedApps.Add(exe);
                }
            }
        }
        catch
        {
            // Fallback gracefully on read errors
        }
    }

    private void SavePreset()
    {
        try
        {
            var preset = new TeacherPreset
            {
                QuickApps = _quickApps.ToList(),
                BlockedApps = _blockedApps.ToList()
            };
            TeacherPresetManager.Save(preset);
        }
        catch
        {
            // Suppress preset save errors
        }
    }

    private void GenerateNewPin()
    {
        var pin = Random.Shared.Next(ProtocolConstants.PinMin, ProtocolConstants.PinMax + 1).ToString();
        PinTextBlock.Text = pin;
    }

    private void NewPinButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_hub.IsRunning)
        {
            GenerateNewPin();
        }
    }

    private async void StartClassButton_Click(object sender, RoutedEventArgs e)
    {
        var className = ClassNameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(className))
        {
            className = "Класс";
            ClassNameTextBox.Text = className;
        }

        var pin = PinTextBlock.Text.Trim();

        try
        {
            await _hub.StartClassAsync(className, pin);
            UpdateUiState(isRunning: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка запуска класса", MessageBoxButton.OK, MessageBoxImage.Warning);
            UpdateUiState(isRunning: false);
        }
    }

    private async void StopClassButton_Click(object sender, RoutedEventArgs e)
    {
        StopClassButton.IsEnabled = false;
        try
        {
            await _hub.StopClassAsync();
        }
        finally
        {
            Dispatcher.Invoke(() =>
            {
                StudentsListBox.SelectedItem = null;
                PlaceholderBorder.Visibility = Visibility.Visible;
                StudentDetailGrid.Visibility = Visibility.Collapsed;
                ScreenImage.Source = null;
                _processes.Clear();
                _selectedStudentHints.Clear();
            });
            UpdateUiState(isRunning: false);
        }
    }

    private async void StudentsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selected = StudentsListBox.SelectedItem as StudentItemViewModel;
        if (selected == null || !_hub.IsRunning)
        {
            PlaceholderBorder.Visibility = Visibility.Visible;
            StudentDetailGrid.Visibility = Visibility.Collapsed;
            ScreenImage.Source = null;
            _processes.Clear();
            _selectedStudentHints.Clear();
            ActiveAppTextBlock.Text = "—";
            await _hub.SelectStudentAsync(null);
            return;
        }

        PlaceholderBorder.Visibility = Visibility.Collapsed;
        StudentDetailGrid.Visibility = Visibility.Visible;
        SelectedStudentNameTextBlock.Text = selected.DisplayName;
        ScreenImage.Source = null;
        WaitingForFrameTextBlock.Visibility = Visibility.Visible;
        _processes.Clear();
        ActiveAppTextBlock.Text = "—";

        // Refresh hints for selected student
        _selectedStudentHints.Clear();
        if (_hintsByStudent.TryGetValue(selected.Id, out var hints))
        {
            foreach (var h in hints)
            {
                _selectedStudentHints.Add(h);
            }
        }

        await _hub.SelectStudentAsync(selected.Id);
    }

    private void OnScreenFrameReceived(string studentId, JpegFrame frame)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (studentId != _hub.SelectedStudentId)
                return;

            try
            {
                using var ms = new MemoryStream(frame.Data);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                ScreenImage.Source = bitmap;
                WaitingForFrameTextBlock.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // Ignore rendering errors on broken frames
            }
        });
    }

    private void OnProcessListReceived(string studentId, WireMessage msg)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (studentId != _hub.SelectedStudentId)
                return;

            _processes.Clear();
            if (msg.Items != null)
            {
                foreach (var item in msg.Items)
                {
                    _processes.Add(item);
                }
            }

            ActiveAppTextBlock.Text = string.IsNullOrWhiteSpace(msg.ActiveExe) ? "—" : msg.ActiveExe;
        });
    }

    private void OnInstalledHintsReceived(string studentId, IReadOnlyList<InstalledAppInfo> apps)
    {
        _hintsByStudent[studentId] = apps.ToList();

        Dispatcher.InvokeAsync(() =>
        {
            if (studentId == _hub.SelectedStudentId)
            {
                _selectedStudentHints.Clear();
                foreach (var app in apps)
                {
                    _selectedStudentHints.Add(app);
                }
            }
        });
    }

    private void UpdateUiState(bool isRunning)
    {
        ClassNameTextBox.IsEnabled = !isRunning;
        NewPinButton.IsEnabled = !isRunning;
        StartClassButton.IsEnabled = !isRunning;
        StopClassButton.IsEnabled = isRunning;

        if (isRunning)
        {
            StatusTextBlock.Text = $"класс запущен · порт {_hub.TcpPort} · учеников: {_students.Count(s => s.Status == StudentHubStatus.Online)}";
        }
        else
        {
            StatusTextBlock.Text = "класс остановлен";
        }
    }

    private void OnStudentJoined(ConnectedStudent student)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var existing = _students.FirstOrDefault(s => s.Id == student.Id);
            if (existing == null)
            {
                _students.Add(new StudentItemViewModel
                {
                    Id = student.Id,
                    DisplayName = student.DisplayName,
                    Hostname = student.Hostname,
                    Status = student.Status
                });
            }
            else
            {
                existing.DisplayName = student.DisplayName;
                existing.Hostname = student.Hostname;
                existing.Status = student.Status;
            }

            if (_hub.IsRunning)
            {
                StatusTextBlock.Text = $"класс запущен · порт {_hub.TcpPort} · учеников: {_students.Count(s => s.Status == StudentHubStatus.Online)}";
            }
        });
    }

    private void OnStudentStatusChanged(ConnectedStudent student)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var existing = _students.FirstOrDefault(s => s.Id == student.Id);
            if (existing != null)
            {
                existing.DisplayName = student.DisplayName;
                existing.Hostname = student.Hostname;
                existing.Status = student.Status;
            }

            if (_hub.IsRunning)
            {
                StatusTextBlock.Text = $"класс запущен · порт {_hub.TcpPort} · учеников: {_students.Count(s => s.Status == StudentHubStatus.Online)}";
            }
        });
    }

    private void OnStudentLeft(ConnectedStudent student)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var existing = _students.FirstOrDefault(s => s.Id == student.Id);
            if (existing != null)
            {
                existing.Status = StudentHubStatus.Disconnected;
            }

            if (_hub.IsRunning)
            {
                StatusTextBlock.Text = $"класс запущен · порт {_hub.TcpPort} · учеников: {_students.Count(s => s.Status == StudentHubStatus.Online)}";
            }
        });
    }

    // --- Quick Apps Section Handlers ---

    private void AddQuickApp_Click(object sender, RoutedEventArgs e)
    {
        var name = NewAppNameTextBox.Text.Trim();
        var exe = NewAppExeTextBox.Text.Trim();
        var path = NewAppPathTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(exe))
        {
            MessageBox.Show(this, "Укажите название и имя exe-файла программы", "Добавление программы", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            exe += ".exe";
        }

        _quickApps.Add(new InstalledAppInfo
        {
            Name = name,
            Exe = exe,
            LaunchTarget = string.IsNullOrWhiteSpace(path) ? null : path
        });

        NewAppNameTextBox.Clear();
        NewAppExeTextBox.Clear();
        NewAppPathTextBox.Clear();

        SavePreset();
    }

    private void RemoveQuickApp_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAppsListBox.SelectedItem is InstalledAppInfo app)
        {
            _quickApps.Remove(app);
            SavePreset();
        }
    }

    private async void LaunchSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_hub.IsRunning)
        {
            MessageBox.Show(this, "Сначала начните класс", "Быстрый запуск", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrEmpty(_hub.SelectedStudentId))
        {
            MessageBox.Show(this, "Выберите ученика из списка слева", "Быстрый запуск", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (QuickAppsListBox.SelectedItem is not InstalledAppInfo app)
        {
            MessageBox.Show(this, "Выберите программу из списка быстрого запуска", "Быстрый запуск", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sent = await _hub.SendLaunchAppAsync(_hub.SelectedStudentId, app.Exe, app.LaunchTarget);
        StatusTextBlock.Text = sent ? $"Запущено «{app.Name}» у выбранного ученика" : "Ошибка отправки команды запуска";
    }

    private async void LaunchAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_hub.IsRunning)
        {
            MessageBox.Show(this, "Сначала начните класс", "Быстрый запуск", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (QuickAppsListBox.SelectedItem is not InstalledAppInfo app)
        {
            MessageBox.Show(this, "Выберите программу из списка быстрого запуска", "Быстрый запуск", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var count = await _hub.BroadcastLaunchAppAsync(app.Exe, app.LaunchTarget);
        StatusTextBlock.Text = $"Запущено «{app.Name}» у {count} учеников";
    }

    // --- RAM Block List Handlers ---

    private void AddBlockedApp_Click(object sender, RoutedEventArgs e)
    {
        var exe = NewBlockedExeTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            exe += ".exe";
        }

        if (!_blockedApps.Any(a => string.Equals(a, exe, StringComparison.OrdinalIgnoreCase)))
        {
            _blockedApps.Add(exe);
            SavePreset();
        }

        NewBlockedExeTextBox.Clear();
    }

    private void RemoveBlockedApp_Click(object sender, RoutedEventArgs e)
    {
        if (BlockedAppsListBox.SelectedItem is string exe)
        {
            _blockedApps.Remove(exe);
            SavePreset();
        }
    }

    private async void ApplyBlockListAll_Click(object sender, RoutedEventArgs e)
    {
        if (!_hub.IsRunning)
        {
            MessageBox.Show(this, "Сначала начните класс", "Блокировка приложений", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var count = await _hub.BroadcastBlockListAsync(_blockedApps.ToList());
        StatusTextBlock.Text = $"Блоклист ({_blockedApps.Count} программ) применен к {count} ученикам";
    }

    private async void ApplyBlockListSelected_Click(object sender, RoutedEventArgs e)
    {
        if (!_hub.IsRunning)
        {
            MessageBox.Show(this, "Сначала начните класс", "Блокировка приложений", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrEmpty(_hub.SelectedStudentId))
        {
            MessageBox.Show(this, "Выберите ученика из списка слева", "Блокировка приложений", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sent = await _hub.SendBlockListAsync(_hub.SelectedStudentId, _blockedApps.ToList());
        StatusTextBlock.Text = sent ? $"Блоклист ({_blockedApps.Count} программ) применен к выбранному ученику" : "Ошибка отправки блоклиста";
    }

    private async void ClearBlockListAll_Click(object sender, RoutedEventArgs e)
    {
        if (!_hub.IsRunning)
        {
            MessageBox.Show(this, "Сначала начните класс", "Блокировка приложений", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var count = await _hub.BroadcastBlockListAsync(Array.Empty<string>());
        StatusTextBlock.Text = $"Все блокировки сняты у {count} учеников";
    }

    private async void BlockProcessMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await BlockSelectedProcessCoreAsync();
    }

    private async void BlockSelectedProcessButton_Click(object sender, RoutedEventArgs e)
    {
        await BlockSelectedProcessCoreAsync();
    }

    private async Task BlockSelectedProcessCoreAsync()
    {
        if (ProcessesListView.SelectedItem is not ProcessItemInfo proc || string.IsNullOrWhiteSpace(proc.Exe))
        {
            MessageBox.Show(this, "Выберите процесс из таблицы", "Блокировка процесса", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var exe = proc.Exe.Trim();
        if (!_blockedApps.Any(a => string.Equals(a, exe, StringComparison.OrdinalIgnoreCase)))
        {
            _blockedApps.Add(exe);
            SavePreset();
        }

        if (_hub.IsRunning)
        {
            var count = await _hub.BroadcastBlockListAsync(_blockedApps.ToList());
            StatusTextBlock.Text = $"Процесс «{exe}» добавлен в блоклист и заблокирован у {count} учеников";
        }
        else
        {
            StatusTextBlock.Text = $"Процесс «{exe}» добавлен в список блокировок пресета";
        }
    }

    // --- Hints Section Handlers ---

    private void AddHintToQuickApps_Click(object sender, RoutedEventArgs e)
    {
        if (HintsListView.SelectedItem is not InstalledAppInfo hint)
        {
            MessageBox.Show(this, "Выберите программу из списка подсказок", "Быстрый запуск", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_quickApps.Any(a => string.Equals(a.Exe, hint.Exe, StringComparison.OrdinalIgnoreCase)))
        {
            _quickApps.Add(new InstalledAppInfo
            {
                Name = hint.Name,
                Exe = hint.Exe,
                LaunchTarget = hint.LaunchTarget
            });
            SavePreset();
            StatusTextBlock.Text = $"«{hint.Name}» добавлена в быстрый запуск";
        }
    }

    private async void AddHintToBlockList_Click(object sender, RoutedEventArgs e)
    {
        if (HintsListView.SelectedItem is not InstalledAppInfo hint)
        {
            MessageBox.Show(this, "Выберите программу из списка подсказок", "Блокировка приложений", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var exe = hint.Exe.Trim();
        if (!_blockedApps.Any(a => string.Equals(a, exe, StringComparison.OrdinalIgnoreCase)))
        {
            _blockedApps.Add(exe);
            SavePreset();

            if (_hub.IsRunning)
            {
                var count = await _hub.BroadcastBlockListAsync(_blockedApps.ToList());
                StatusTextBlock.Text = $"«{exe}» добавлена в блоклист и заблокирована у {count} учеников";
            }
            else
            {
                StatusTextBlock.Text = $"«{exe}» добавлена в блоклист пресета";
            }
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        SavePreset();

        if (_isClosingInProgress)
        {
            base.OnClosing(e);
            return;
        }

        if (_hub.IsRunning)
        {
            e.Cancel = true;
            _isClosingInProgress = true;
            IsEnabled = false;

            try
            {
                await _hub.StopClassAsync();
            }
            catch
            {
                // Ignore errors on close
            }

            Close();
            return;
        }

        base.OnClosing(e);
    }
}
