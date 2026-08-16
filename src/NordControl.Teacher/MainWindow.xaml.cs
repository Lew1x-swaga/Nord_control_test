using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using NordControl.Core;
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
    private bool _isClosingInProgress;

    public MainWindow()
    {
        InitializeComponent();

        _hub = new ClassHub();
        _hub.StudentJoined += OnStudentJoined;
        _hub.StudentStatusChanged += OnStudentStatusChanged;
        _hub.StudentLeft += OnStudentLeft;

        StudentsListBox.ItemsSource = _students;

        GenerateNewPin();
        UpdateUiState(isRunning: false);
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
            UpdateUiState(isRunning: false);
        }
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

    protected override async void OnClosing(CancelEventArgs e)
    {
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
