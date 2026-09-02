using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NordControl.Core;
using NordControl.Core.Helpers;
using NordControl.Core.Policies;
using NordControl.Protocol;

namespace NordControl.Teacher;

public sealed class ProcessRowViewModel
{
    public ProcessRowViewModel(ProcessItemInfo item, bool isActive)
    {
        Item = item;
        IsActive = isActive;
    }

    public ProcessItemInfo Item { get; }
    public string Exe => Item.Exe;
    public int Pid => Item.Pid;
    public string Title => Item.Title;
    public bool IsActive { get; }
}

public class StudentItemViewModel : INotifyPropertyChanged
{
    private static readonly SolidColorBrush OnlineFg = Freeze(16, 185, 129);
    private static readonly SolidColorBrush ReconnectFg = Freeze(217, 119, 6);
    private static readonly SolidColorBrush OfflineFg = Freeze(100, 116, 139);
    private static readonly SolidColorBrush GridOfflineFg = Freeze(225, 29, 72);
    private static readonly SolidColorBrush OnlineBg = Freeze(236, 253, 245);
    private static readonly SolidColorBrush ReconnectBg = Freeze(254, 243, 199);
    private static readonly SolidColorBrush OfflineBg = Freeze(226, 232, 240);

    private string _displayName = string.Empty;
    private string _hostname = string.Empty;
    private string _groupName = string.Empty;
    private StudentHubStatus _status;
    private ImageSource? _previewImage;

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

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

    public string GroupName
    {
        get => _groupName;
        set
        {
            var next = value ?? string.Empty;
            if (_groupName != next)
            {
                _groupName = next;
                OnPropertyChanged(nameof(GroupName));
                OnPropertyChanged(nameof(HasGroup));
            }
        }
    }

    public bool HasGroup => !string.IsNullOrEmpty(_groupName);

    public StudentHubStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                if (_status != StudentHubStatus.Online)
                {
                    PreviewImage = null;
                }

                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusForeground));
                OnPropertyChanged(nameof(StatusBackground));
                OnPropertyChanged(nameof(GridStatusDot));
                OnPropertyChanged(nameof(PreviewPlaceholderText));
            }
        }
    }

    public ImageSource? PreviewImage
    {
        get => _previewImage;
        set
        {
            if (!ReferenceEquals(_previewImage, value))
            {
                _previewImage = value;
                OnPropertyChanged(nameof(PreviewImage));
                OnPropertyChanged(nameof(HasPreviewImage));
                OnPropertyChanged(nameof(ShowPreviewPlaceholder));
            }
        }
    }

    public bool HasPreviewImage => _previewImage != null;

    public bool ShowPreviewPlaceholder => _previewImage == null;

    public string PreviewPlaceholderText => Status switch
    {
        StudentHubStatus.Online => "ожидание…",
        StudentHubStatus.Reconnecting => "переподключение",
        _ => "нет"
    };

    public string StatusText => Status switch
    {
        StudentHubStatus.Online => "онлайн",
        StudentHubStatus.Reconnecting => "переподключение",
        _ => "нет"
    };

    public Brush StatusForeground => Status switch
    {
        StudentHubStatus.Online => OnlineFg,
        StudentHubStatus.Reconnecting => ReconnectFg,
        _ => OfflineFg
    };

    public Brush GridStatusDot => Status == StudentHubStatus.Online ? OnlineFg : GridOfflineFg;

    public Brush StatusBackground => Status switch
    {
        StudentHubStatus.Online => OnlineBg,
        StudentHubStatus.Reconnecting => ReconnectBg,
        _ => OfflineBg
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class GroupItemViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;

    public string Id { get; init; } = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class TeacherSentMessageViewModel
{
    public string TimeText { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public bool Ok { get; init; }
}

public partial class MainWindow : Window
{
    private readonly ClassHub _hub;
    private readonly ObservableCollection<StudentItemViewModel> _students = new();
    private readonly ObservableCollection<GroupItemViewModel> _groupItems = new();
    private readonly ObservableCollection<TeacherSentMessageViewModel> _sentMessages = new();
    private readonly ObservableCollection<ProcessRowViewModel> _processes = new();
    private readonly ObservableCollection<InstalledAppInfo> _quickApps = new();
    private readonly ObservableCollection<string> _blockedApps = new();
    private readonly ObservableCollection<InstalledAppInfo> _selectedStudentHints = new();
    private readonly ConcurrentDictionary<string, List<InstalledAppInfo>> _hintsByStudent = new();

    private bool _isClosingInProgress;
    private TaskCompletionSource<bool>? _appNoticeTcs;
    private DispatcherTimer? _appNoticeTimer;
    private bool _appNoticeIsEndClass;
    private TaskCompletionSource<string?>? _groupPromptTcs;
    private TaskCompletionSource<GroupItemViewModel?>? _groupPickTcs;
    private int _messageFeedbackSeq;
    private string _studentFilter = string.Empty;
    private StudentListLayout _studentListLayout = StudentListLayout.List;
    private readonly ListCollectionView _previewStudentsView;
    private bool _screenGridMode;
    private bool _previewBroadcastOn;
    private bool _suppressStudentListSelection;
    private bool _suppressGroupListUnselect;
    private bool _streamFullscreen;
    private WindowState _restoreWindowState = WindowState.Normal;
    private int _framesInWindow;
    private ulong _fpsWindowStartMs;
    private int _currentFps;
    private readonly object _frameDecodeGate = new();
    private int _frameDecodeBusy;
    private string? _pendingFrameStudentId;
    private JpegFrame _pendingFrame;
    private readonly object _previewDecodeGate = new();
    private readonly Dictionary<string, JpegFrame> _pendingPreviewFrames = new(StringComparer.Ordinal);
    private int _previewDecodeBusy;
    private UniformGrid? _studentListUniformGrid;
    private int _lastStudentListColumns = int.MinValue;
    private UniformGrid? _previewUniformGrid;
    private int _lastPreviewColumns = int.MinValue;

    public MainWindow()
    {
        InitializeComponent();

        _hub = new ClassHub();
        _hub.StudentJoined += OnStudentJoined;
        _hub.StudentStatusChanged += OnStudentStatusChanged;
        _hub.StudentLeft += OnStudentLeft;
        _hub.ScreenFrameReceived += OnScreenFrameReceived;
        _hub.PreviewFrameReceived += OnPreviewFrameReceived;
        _hub.ProcessListReceived += OnProcessListReceived;
        _hub.InstalledHintsReceived += OnInstalledHintsReceived;

        StudentsListBox.ItemsSource = _students;
        GroupsListBox.ItemsSource = _groupItems;
        CollectionViewSource.GetDefaultView(_students).Filter = FilterStudent;
        _previewStudentsView = new ListCollectionView(_students) { Filter = FilterStudent };
        ScreenPreviewItemsControl.ItemsSource = _previewStudentsView;
        ProcessesListView.ItemsSource = _processes;
        QuickAppsListBox.ItemsSource = _quickApps;
        BlockedAppsListBox.ItemsSource = _blockedApps;
        HintsListView.ItemsSource = _selectedStudentHints;
        TeacherMessageHistoryListBox.ItemsSource = _sentMessages;

        LoadPreset();
        LoadUiSettings();
        GenerateNewPin();
        RefreshLanIpDisplay();
        UpdateUiState(isRunning: false);
        RefreshStudentCount();

        TeacherMessageTextBox.MaxLength = ProtocolConstants.MaxTeacherMessageChars;

        LaunchAppSuggestBox.Placeholder = "Найти программу для запуска…";
        BlockAppSuggestBox.Placeholder = "Найти программу для блокировки…";
        LaunchAppSuggestBox.SuggestionChosen += app =>
        {
            if (!string.IsNullOrWhiteSpace(app.LaunchTarget))
            {
                NewAppPathTextBox.Text = app.LaunchTarget;
            }
        };
        LaunchAppSuggestBox.Submitted += async app =>
        {
            var target = !string.IsNullOrWhiteSpace(app?.LaunchTarget)
                ? app.LaunchTarget
                : (!string.IsNullOrWhiteSpace(NewAppPathTextBox.Text) ? NewAppPathTextBox.Text.Trim() : null);
            var exe = app?.Exe ?? LaunchAppSuggestBox.QueryText;
            var name = app?.Name;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                EnsureQuickAppListed(name, exe, target);
                await LaunchSingleAppCoreAsync(exe, target, name);
            }
        };
        BlockAppSuggestBox.Submitted += async app => await AddBlockedAppCoreAsync(app);
        RefreshAppSuggestions();
    }

    private bool FilterStudent(object obj)
    {
        if (obj is not StudentItemViewModel student)
            return false;

        if (!string.IsNullOrEmpty(_hub.SelectedStudentId) && student.Id == _hub.SelectedStudentId)
            return true;

        if (string.IsNullOrWhiteSpace(_studentFilter))
            return true;

        return student.DisplayName.Contains(_studentFilter, StringComparison.OrdinalIgnoreCase)
            || student.Hostname.Contains(_studentFilter, StringComparison.OrdinalIgnoreCase);
    }

    private void StudentSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _studentFilter = StudentSearchTextBox.Text.Trim();
        CollectionViewSource.GetDefaultView(_students).Refresh();
        _previewStudentsView.Refresh();
    }

    private void LoadUiSettings()
    {
        var settings = TeacherUiSettingsManager.Load();
        ApplyStudentListLayout(settings.Layout, persist: false);
    }

    private void SaveUiSettings()
    {
        TeacherUiSettingsManager.Save(new TeacherUiSettings { Layout = _studentListLayout });
    }

    private void StudentListLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyStudentListLayout(StudentListLayout.List, persist: true);
    }

    private void StudentGridLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyStudentListLayout(StudentListLayout.Grid, persist: true);
    }

    private void StudentsListBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateStudentGridColumns();
    }

    private void ApplyStudentListLayout(StudentListLayout layout, bool persist)
    {
        var panelChanged = layout != _studentListLayout;
        _studentListLayout = layout;
        RefreshLayoutToggleAppearance();

        if (panelChanged)
        {
            _studentListUniformGrid = null;
            _lastStudentListColumns = int.MinValue;
            var selected = StudentsListBox.SelectedItem;
            _suppressStudentListSelection = true;
            StudentsListBox.ItemsPanel = layout == StudentListLayout.Grid
                ? (ItemsPanelTemplate)FindResource("StudentGridItemsPanel")
                : (ItemsPanelTemplate)FindResource("StudentListItemsPanel");
            StudentsListBox.ItemContainerStyle = layout == StudentListLayout.Grid
                ? (Style)FindResource("Style.StudentGridItem")
                : (Style)FindResource("Style.StudentListItem");
            StudentsListBox.ItemTemplate = layout == StudentListLayout.Grid
                ? (DataTemplate)FindResource("StudentGridItemTemplate")
                : (DataTemplate)FindResource("StudentListItemTemplate");
            VirtualizingPanel.SetIsVirtualizing(StudentsListBox, layout == StudentListLayout.List);
            ScrollViewer.SetCanContentScroll(StudentsListBox, layout == StudentListLayout.List);

            _ = StudentsListBox.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    StudentsListBox.SelectedItem = selected;
                    UpdateStudentGridColumns();
                }
                finally
                {
                    _suppressStudentListSelection = false;
                }
            }, DispatcherPriority.Loaded);
        }
        else
        {
            UpdateStudentGridColumns();
        }

        if (persist)
        {
            SaveUiSettings();
        }
    }

    private void RefreshLayoutToggleAppearance()
    {
        var grid = _studentListLayout == StudentListLayout.Grid;
        StudentListLayoutButton.Style = (Style)FindResource(grid ? "Style.GhostButton" : "Style.AccentButton");
        StudentGridLayoutButton.Style = (Style)FindResource(grid ? "Style.AccentButton" : "Style.GhostButton");
    }

    private void UpdateStudentGridColumns()
    {
        if (_studentListLayout != StudentListLayout.Grid)
        {
            return;
        }

        var columns = StudentGridLayout.ColumnCount(
            StudentsListBox.ActualWidth, StudentGridLayout.StudentListMinCardWidth);
        ApplyCachedUniformGridColumns(ref _studentListUniformGrid, ref _lastStudentListColumns, StudentsListBox, columns);
    }

    private void RefreshScreenModeToggleAppearance()
    {
        OneScreenModeButton.Style = (Style)FindResource(_screenGridMode ? "Style.GhostButton" : "Style.AccentButton");
        ScreenGridModeButton.Style = (Style)FindResource(_screenGridMode ? "Style.AccentButton" : "Style.GhostButton");
    }

    private async void OneScreenModeButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyScreenGridModeAsync(false);
    }

    private async void ScreenGridModeButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyScreenGridModeAsync(true);
    }

    private async void PreviewTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not StudentItemViewModel vm)
        {
            return;
        }

        await ApplyScreenGridModeAsync(false);

        if (!ReferenceEquals(StudentsListBox.SelectedItem, vm) || StudentsListBox.SelectedItems.Count != 1)
        {
            _suppressStudentListSelection = true;
            StudentsListBox.UnselectAll();
            _suppressStudentListSelection = false;
            StudentsListBox.SelectedItem = vm;
        }
    }

    private async Task ApplyScreenGridModeAsync(bool grid)
    {
        _screenGridMode = grid;
        RefreshScreenModeToggleAppearance();

        if (grid && _hub.IsRunning)
        {
            ExitStreamFullscreenIfNeeded();
            PlaceholderBorder.Visibility = Visibility.Collapsed;
            StudentDetailGrid.Visibility = Visibility.Collapsed;
            ScreenPreviewGridHost.Visibility = Visibility.Visible;
            ScreenImage.Source = null;
            FullscreenImage.Source = null;
            _ = ScreenPreviewItemsControl.Dispatcher.InvokeAsync(
                UpdatePreviewGridColumns, DispatcherPriority.Loaded);
            await _hub.SelectStudentAsync(null);
            await EnablePreviewBroadcastAsync();
            return;
        }

        await DisablePreviewBroadcastAsync();
        ClearAllPreviewBitmaps();
        ScreenPreviewGridHost.Visibility = Visibility.Collapsed;

        if (!grid && _hub.IsRunning && StudentsListBox.SelectedItem is StudentItemViewModel selected)
        {
            PlaceholderBorder.Visibility = Visibility.Collapsed;
            StudentDetailGrid.Visibility = Visibility.Visible;
            ScreenImage.Source = null;
            FullscreenImage.Source = null;
            WaitingForFrameTextBlock.Visibility = Visibility.Visible;
            await _hub.SelectStudentAsync(selected.Id);
        }
        else
        {
            PlaceholderBorder.Visibility = Visibility.Visible;
            StudentDetailGrid.Visibility = Visibility.Collapsed;
        }
    }

    private async Task EnablePreviewBroadcastAsync()
    {
        if (!_hub.IsRunning)
        {
            _previewBroadcastOn = false;
            return;
        }

        await _hub.BroadcastPreviewEnableAsync();
        _previewBroadcastOn = true;
    }

    private async Task DisablePreviewBroadcastAsync()
    {
        if (_previewBroadcastOn && _hub.IsRunning)
        {
            try
            {
                await _hub.BroadcastPreviewDisableAsync();
            }
            catch
            {
                // leave grid anyway
            }
        }

        _previewBroadcastOn = false;
    }

    private void ClearAllPreviewBitmaps()
    {
        lock (_previewDecodeGate)
        {
            _pendingPreviewFrames.Clear();
        }

        foreach (var student in _students)
        {
            student.PreviewImage = null;
        }
    }

    private void ScreenPreviewItemsControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePreviewGridColumns();
    }

    private void UpdatePreviewGridColumns()
    {
        if (!_screenGridMode || ScreenPreviewGridHost.Visibility != Visibility.Visible)
        {
            return;
        }

        var columns = StudentGridLayout.ColumnCount(
            ScreenPreviewItemsControl.ActualWidth, StudentGridLayout.ScreenPreviewMinCardWidth);
        ApplyCachedUniformGridColumns(ref _previewUniformGrid, ref _lastPreviewColumns, ScreenPreviewItemsControl, columns);
    }

    private async Task SyncPreviewEnableIfGridAsync()
    {
        if (_screenGridMode && _hub.IsRunning)
        {
            await EnablePreviewBroadcastAsync();
        }
    }

    private void RefreshStudentCount()
    {
        var online = _students.Count(s => s.Status == StudentHubStatus.Online);
        StudentCountTextBlock.Text = $"Онлайн: {online} / Всего: {_students.Count}";
    }

    private void UpdateGroupsEnabled()
    {
        var running = _hub.IsRunning;
        GroupsListBox.IsEnabled = running;
        NewGroupButton.IsEnabled = running;
        RenameGroupButton.IsEnabled = running;
        DisbandGroupButton.IsEnabled = running;
        AssignGroupButton.IsEnabled = running;
        RemoveFromGroupButton.IsEnabled = running;
        LaunchGroupButton.IsEnabled = running;
        ApplyBlockListGroupButton.IsEnabled = running;
        SendSelectedMessageButton.IsEnabled = running;
        SendGroupMessageButton.IsEnabled = running;
        SendAllMessageButton.IsEnabled = running;
    }

    private string ResolveGroupName(string studentId)
    {
        var ids = _hub.GetStudentGroupIds(studentId);
        if (ids.Count == 0)
        {
            return string.Empty;
        }

        var names = new List<string>();
        foreach (var groupId in ids)
        {
            var local = _groupItems.FirstOrDefault(g => g.Id == groupId);
            if (local != null)
            {
                names.Add(local.Name);
                continue;
            }

            var hubGroup = _hub.Groups.FirstOrDefault(g => g.Id == groupId);
            if (hubGroup != null)
            {
                names.Add(hubGroup.Name);
            }
        }

        names.Sort(StringComparer.CurrentCultureIgnoreCase);
        return string.Join(", ", names);
    }

    private void RefreshStudentGroupNames()
    {
        foreach (var student in _students)
        {
            student.GroupName = ResolveGroupName(student.Id);
        }
    }

    private void RefreshGroupsFromHub()
    {
        var hubGroups = _hub.IsRunning ? _hub.Groups.ToList() : new List<ClassGroup>();
        var hubIds = new HashSet<string>(hubGroups.Select(g => g.Id), StringComparer.Ordinal);

        for (var i = _groupItems.Count - 1; i >= 0; i--)
        {
            if (!hubIds.Contains(_groupItems[i].Id))
            {
                _groupItems.RemoveAt(i);
            }
        }

        foreach (var g in hubGroups)
        {
            var existing = _groupItems.FirstOrDefault(x => x.Id == g.Id);
            if (existing == null)
            {
                _groupItems.Add(new GroupItemViewModel { Id = g.Id, Name = g.Name });
            }
            else if (existing.Name != g.Name)
            {
                existing.Name = g.Name;
            }
        }

        _suppressGroupListUnselect = true;
        GroupsListBox.UnselectAll();
        _suppressGroupListUnselect = false;
        RefreshStudentGroupNames();
        UpdateGroupsEnabled();
    }

    private void GroupsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGroupListUnselect || GroupsListBox.SelectedItem == null)
        {
            return;
        }

        _suppressGroupListUnselect = true;
        GroupsListBox.UnselectAll();
        _suppressGroupListUnselect = false;
    }

    private List<StudentItemViewModel> GetSelectedStudentsFromList()
    {
        return StudentsListBox.SelectedItems.OfType<StudentItemViewModel>().ToList();
    }

    private async Task<GroupItemViewModel?> PickExistingGroupAsync(string dialogTitle, string pickTitle, string confirmText)
    {
        if (_groupItems.Count == 0)
        {
            ShowInfoNotice(dialogTitle, "Нет ни одной группы. Сначала создайте группу.");
            return null;
        }

        return await PickGroupAsync(pickTitle, confirmText);
    }

    private string DescribeUnavailableGroup(GroupItemViewModel group)
    {
        if (!_hub.Groups.Any(g => g.Id == group.Id))
        {
            return $"Группы «{group.Name}» нет";
        }

        var anyMember = _students.Any(s => _hub.GetStudentGroupIds(s.Id).Contains(group.Id));
        return anyMember
            ? $"В группе «{group.Name}» нет учеников онлайн"
            : $"В группе «{group.Name}» нет учеников";
    }

    private async void NewGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Группы"))
        {
            return;
        }

        var name = await ShowGroupPromptAsync("Новая группа", "Создать");
        if (name == null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var id = _hub.CreateGroup(name);
        RefreshGroupsFromHub();
        GroupsListBox.UnselectAll();
    }

    private async void RenameGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Группы"))
        {
            return;
        }

        var group = await PickExistingGroupAsync("Группы", "Переименовать группу", "Выбрать");
        if (group == null)
        {
            return;
        }

        var name = await ShowGroupPromptAsync("Переименовать", "Сохранить", group.Name);
        if (name == null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _hub.RenameGroup(group.Id, name);
        RefreshGroupsFromHub();
        GroupsListBox.UnselectAll();
    }

    private async void DisbandGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Группы"))
        {
            return;
        }

        var group = await PickExistingGroupAsync("Группы", "Распустить группу", "Распустить");
        if (group == null)
        {
            return;
        }

        if (!await ShowConfirmNoticeAsync(
                "Распустить группу?",
                $"«{group.Name}»",
                "Ученики останутся в других группах, если они есть.",
                "Да, распустить"))
        {
            return;
        }

        _hub.DisbandGroup(group.Id);
        RefreshGroupsFromHub();
        GroupsListBox.UnselectAll();
    }

    private async void AssignGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Группы"))
        {
            return;
        }

        var selectedStudents = StudentsListBox.SelectedItems.OfType<StudentItemViewModel>().ToList();
        if (selectedStudents.Count == 0)
        {
            ShowInfoNotice("Группы", "Выберите ученика из списка слева");
            return;
        }

        var group = await PickExistingGroupAsync("Группы", "Назначить в группу", "Назначить");
        if (group == null)
        {
            return;
        }

        foreach (var student in selectedStudents)
        {
            _hub.AddStudentToGroup(student.Id, group.Id);
        }

        RefreshStudentGroupNames();
        GroupsListBox.UnselectAll();
    }

    private async void RemoveFromGroupButton_Click(object sender, RoutedEventArgs e)
    {
        await RemoveSelectedStudentsFromGroupAsync();
    }

    private async Task RemoveSelectedStudentsFromGroupAsync()
    {
        if (!EnsureClassRunning("Группы"))
        {
            return;
        }

        var selectedStudents = StudentsListBox.SelectedItems.OfType<StudentItemViewModel>().ToList();
        if (selectedStudents.Count == 0)
        {
            ShowInfoNotice("Группы", "Выберите ученика из списка слева");
            return;
        }

        var group = await PickExistingGroupAsync("Группы", "Убрать из группы", "Убрать");
        if (group == null)
        {
            return;
        }

        foreach (var student in selectedStudents)
        {
            _hub.RemoveStudentFromGroup(student.Id, group.Id);
        }

        RefreshStudentGroupNames();
        GroupsListBox.UnselectAll();
    }

    private void StudentsListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not StudentItemViewModel vm)
        {
            return;
        }

        if (!StudentsListBox.SelectedItems.Contains(vm))
        {
            StudentsListBox.SelectedItem = vm;
        }
    }

    private async void StudentMenuShowScreen_Click(object sender, RoutedEventArgs e)
    {
        if (StudentsListBox.SelectedItem is not StudentItemViewModel vm)
        {
            return;
        }

        if (_screenGridMode)
        {
            await ApplyScreenGridModeAsync(false);
        }

        if (!ReferenceEquals(StudentsListBox.SelectedItem, vm) || StudentsListBox.SelectedItems.Count != 1)
        {
            _suppressStudentListSelection = true;
            StudentsListBox.UnselectAll();
            _suppressStudentListSelection = false;
            StudentsListBox.SelectedItem = vm;
        }
    }

    private void StudentMenuAssignGroup_Click(object sender, RoutedEventArgs e)
    {
        AssignGroupButton_Click(sender, e);
    }

    private async void StudentMenuRemoveFromGroup_Click(object sender, RoutedEventArgs e)
    {
        await RemoveSelectedStudentsFromGroupAsync();
    }

    private void StudentMenuClearGroups_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Группы"))
        {
            return;
        }

        var selectedStudents = StudentsListBox.SelectedItems.OfType<StudentItemViewModel>().ToList();
        if (selectedStudents.Count == 0)
        {
            ShowInfoNotice("Группы", "Выберите ученика из списка слева");
            return;
        }

        foreach (var student in selectedStudents)
        {
            _hub.SetStudentGroup(student.Id, null);
        }

        RefreshStudentGroupNames();
    }

    private async void StudentMenuKick_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Урок"))
        {
            return;
        }

        var selectedStudents = StudentsListBox.SelectedItems.OfType<StudentItemViewModel>().ToList();
        if (selectedStudents.Count == 0)
        {
            ShowInfoNotice("Урок", "Выберите ученика из списка слева");
            return;
        }

        var names = string.Join(", ", selectedStudents.Select(s => s.DisplayName));
        if (!await ShowConfirmNoticeAsync(
                selectedStudents.Count == 1 ? "Исключить с урока?" : "Исключить учеников?",
                "Они выйдут сразу",
                selectedStudents.Count == 1
                    ? $"«{names}» потеряет связь с классом. Ограничения на этом ПК снимутся."
                    : $"Исключить: {names}. Ограничения на их ПК снимутся.",
                "Исключить"))
        {
            return;
        }

        foreach (var student in selectedStudents)
        {
            await _hub.KickStudentAsync(student.Id);
        }
    }

    private async void CopyPinButton_Click(object sender, RoutedEventArgs e)
    {
        await CopyTextWithBriefTooltipAsync(
            PinTextBlock.Text,
            CopyPinButton,
            "PIN скопирован",
            "Копировать PIN в буфер обмена");
    }

    private async void CopyIpButton_Click(object sender, RoutedEventArgs e)
    {
        var ip = LanIpTextBlock.Text;
        if (ip == "—" || ip == "нет LAN")
        {
            return;
        }

        await CopyTextWithBriefTooltipAsync(ip, CopyIpButton, "IP скопирован", "Скопировать IP");
    }

    private async Task CopyTextWithBriefTooltipAsync(string text, Button button, string copiedMessage, string restoreTooltip)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            return;
        }

        var tip = new ToolTip
        {
            Content = copiedMessage,
            PlacementTarget = button,
            Placement = PlacementMode.Bottom,
            StaysOpen = false
        };
        button.ToolTip = tip;
        tip.IsOpen = true;
        await Task.Delay(1200);
        tip.IsOpen = false;
        button.ToolTip = restoreTooltip;
    }

    private void RefreshLanIpDisplay()
    {
        var ips = LanEndpoints.GetLocalUnicastIpv4();
        if (ips.Count == 0)
        {
            LanIpTextBlock.Text = "нет LAN";
            CopyIpButton.IsEnabled = false;
            LanIpBadge.ToolTip = "Локальный адрес не найден. Проверьте Wi‑Fi или Ethernet.";
            return;
        }

        LanIpTextBlock.Text = ips[0].ToString();
        CopyIpButton.IsEnabled = true;
        LanIpBadge.ToolTip = ips.Count == 1
            ? $"Ученики вводят этот IP, если автопоиск не сработал: {ips[0]}"
            : "Адреса этого ПК: " + string.Join(", ", ips);
    }

    private void StreamFullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleStreamFullscreen();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && GroupPickOverlay.Visibility == Visibility.Visible)
        {
            DismissGroupPick(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && GroupPromptOverlay.Visibility == Visibility.Visible)
        {
            DismissGroupPrompt(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && AppNoticeOverlay.Visibility == Visibility.Visible)
        {
            DismissAppNotice(false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _streamFullscreen)
        {
            ToggleStreamFullscreen();
            e.Handled = true;
        }
    }

    private void ToggleStreamFullscreen()
    {
        _streamFullscreen = !_streamFullscreen;
        StreamFullscreenOverlay.Visibility = _streamFullscreen ? Visibility.Visible : Visibility.Collapsed;
        if (_streamFullscreen)
        {
            _restoreWindowState = WindowState;
            WindowState = WindowState.Maximized;
            FullscreenImage.Source = ScreenImage.Source;
            FullscreenMetaTextBlock.Text = StreamMetaTextBlock.Text;
        }
        else
        {
            WindowState = _restoreWindowState;
            FullscreenImage.Source = null;
        }
    }

    private void ExitStreamFullscreenIfNeeded()
    {
        if (_streamFullscreen)
            ToggleStreamFullscreen();
    }

    private void ResetStreamMeta()
    {
        _framesInWindow = 0;
        _fpsWindowStartMs = 0;
        _currentFps = 0;
        StreamMetaTextBlock.Text = "— · — FPS";
        FullscreenMetaTextBlock.Text = "— · — FPS";
    }

    private void UpdateStreamMeta(uint width, uint height, ulong timestampMs)
    {
        if (_fpsWindowStartMs == 0)
            _fpsWindowStartMs = timestampMs;

        _framesInWindow++;
        var elapsed = timestampMs >= _fpsWindowStartMs ? timestampMs - _fpsWindowStartMs : 0UL;
        if (elapsed >= 1000)
        {
            _currentFps = (int)Math.Round(_framesInWindow * 1000.0 / Math.Max(1UL, elapsed));
            _framesInWindow = 0;
            _fpsWindowStartMs = timestampMs;
        }

        var text = $"{width}x{height} · {_currentFps} FPS";
        StreamMetaTextBlock.Text = text;
        if (_streamFullscreen)
            FullscreenMetaTextBlock.Text = text;
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
        PinTextBlock.Text = PinCode.Generate();
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

        var pin = PinCode.Normalize(PinTextBlock.Text);
        if (!PinCode.IsWellFormed(pin))
        {
            GenerateNewPin();
            pin = PinTextBlock.Text;
        }

        try
        {
            await _hub.StartClassAsync(className, pin);
            RefreshLanIpDisplay();
            UpdateUiState(isRunning: true);
            if (_screenGridMode)
            {
                await ApplyScreenGridModeAsync(true);
            }
        }
        catch (Exception ex)
        {
            ShowInfoNotice("Ошибка запуска класса", ex.Message, warning: true);
            UpdateUiState(isRunning: false);
        }
    }

    private async void StopClassButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_hub.IsRunning || _appNoticeIsEndClass)
        {
            return;
        }

        if (!await ShowEndClassConfirmAsync())
        {
            return;
        }

        StopClassButton.IsEnabled = false;
        try
        {
            await _hub.StopClassAsync();
        }
        finally
        {
            Dispatcher.Invoke(() =>
            {
                ExitStreamFullscreenIfNeeded();
                _screenGridMode = false;
                StudentsListBox.SelectedItem = null;
                _previewBroadcastOn = false;
                ClearAllPreviewBitmaps();
                ScreenPreviewGridHost.Visibility = Visibility.Collapsed;
                PlaceholderBorder.Visibility = Visibility.Visible;
                StudentDetailGrid.Visibility = Visibility.Collapsed;
                ScreenImage.Source = null;
                FullscreenImage.Source = null;
                _processes.Clear();
                _selectedStudentHints.Clear();
                ResetStreamMeta();
                DismissAppNotice(false);
                DismissGroupPick(null);
                DismissGroupPrompt(null);
                ClearTeacherSentMessages();
                RefreshGroupsFromHub();
                RefreshScreenModeToggleAppearance();
            });
            UpdateUiState(isRunning: false);
        }
    }

    private Task<bool> ShowEndClassConfirmAsync()
    {
        return ShowAppNoticeAsync(
            "Завершить урок?",
            "Действие нельзя отменить",
            "Класс остановится, ученики выйдут сразу. Ограничения на их компьютерах снимутся.",
            "Да, завершить",
            "Нет",
            destructive: true,
            isEndClass: true);
    }

    private void ShowInfoNotice(string title, string message, bool warning = false)
    {
        _ = ShowAppNoticeAsync(
            title,
            warning ? "Не удалось выполнить" : "Нужно действие",
            message,
            "Понятно",
            cancelText: null,
            destructive: warning,
            isEndClass: false);
    }

    private Task<bool> ShowConfirmNoticeAsync(string title, string caption, string body, string confirmText, bool destructive = true)
    {
        return ShowAppNoticeAsync(title, caption, body, confirmText, "Отмена", destructive, isEndClass: false);
    }

    private Task<bool> ShowAppNoticeAsync(
        string title,
        string? caption,
        string body,
        string confirmText,
        string? cancelText,
        bool destructive,
        bool isEndClass)
    {
        _appNoticeTimer?.Stop();
        _appNoticeTcs?.TrySetResult(false);
        _appNoticeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _appNoticeIsEndClass = isEndClass;

        AppNoticeTitle.Text = title;
        AppNoticeCaption.Text = caption ?? string.Empty;
        AppNoticeCaption.Visibility = string.IsNullOrWhiteSpace(caption) ? Visibility.Collapsed : Visibility.Visible;
        AppNoticeBody.Text = body;
        AppNoticeConfirmButton.Content = confirmText;

        var rose = destructive;
        AppNoticeIconChrome.Background = (Brush)FindResource(rose ? "Brush.RoseSoft" : "Brush.BlueSoft");
        AppNoticeIcon.Fill = (Brush)FindResource(rose ? "Brush.Rose" : "Brush.Blue");
        AppNoticeIcon.Data = (Geometry)FindResource(cancelText == null ? "Icon.Message" : "Icon.Alert");
        AppNoticeConfirmButton.Style = (Style)FindResource(
            rose && cancelText != null ? "Style.DangerButton" : "Style.AccentButton");

        if (cancelText == null)
        {
            AppNoticeCancelButton.Visibility = Visibility.Collapsed;
            Grid.SetColumn(AppNoticeConfirmButton, 0);
            Grid.SetColumnSpan(AppNoticeConfirmButton, 3);
            _appNoticeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
            _appNoticeTimer.Tick -= AppNoticeTimer_Tick;
            _appNoticeTimer.Tick += AppNoticeTimer_Tick;
            _appNoticeTimer.Start();
        }
        else
        {
            AppNoticeCancelButton.Visibility = Visibility.Visible;
            AppNoticeCancelButton.Content = cancelText;
            Grid.SetColumn(AppNoticeConfirmButton, 2);
            Grid.SetColumnSpan(AppNoticeConfirmButton, 1);
        }

        AppNoticeOverlay.Visibility = Visibility.Visible;
        if (cancelText == null)
        {
            AppNoticeConfirmButton.Focus();
        }
        else
        {
            AppNoticeCancelButton.Focus();
        }

        return _appNoticeTcs.Task;
    }

    private void AppNoticeTimer_Tick(object? sender, EventArgs e)
    {
        DismissAppNotice(false);
    }

    private void DismissAppNotice(bool confirmed)
    {
        if (AppNoticeOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        _appNoticeTimer?.Stop();
        AppNoticeOverlay.Visibility = Visibility.Collapsed;
        _appNoticeIsEndClass = false;
        _appNoticeTcs?.TrySetResult(confirmed);
    }

    private void AppNoticeConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DismissAppNotice(true);
    }

    private void AppNoticeCancelButton_Click(object sender, RoutedEventArgs e)
    {
        DismissAppNotice(false);
    }

    private void AppNoticeScrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DismissAppNotice(false);
        e.Handled = true;
    }

    private void AppNoticeCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private async void StudentsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStudentListSelection)
        {
            return;
        }

        if (_screenGridMode)
        {
            return;
        }

        var selected = StudentsListBox.SelectedItem as StudentItemViewModel;
        var showDetailPanels = !_screenGridMode;
        if (selected == null || !_hub.IsRunning)
        {
            if (showDetailPanels)
            {
                PlaceholderBorder.Visibility = Visibility.Visible;
                StudentDetailGrid.Visibility = Visibility.Collapsed;
            }

            ScreenImage.Source = null;
            FullscreenImage.Source = null;
            _processes.Clear();
            _selectedStudentHints.Clear();
            ActiveAppTextBlock.Text = "—";
            ResetStreamMeta();
            await _hub.SelectStudentAsync(null);
            RefreshAppSuggestions();
            return;
        }

        if (showDetailPanels)
        {
            PlaceholderBorder.Visibility = Visibility.Collapsed;
            StudentDetailGrid.Visibility = Visibility.Visible;
        }

        SelectedStudentNameTextBlock.Text = selected.DisplayName;
        ScreenImage.Source = null;
        FullscreenImage.Source = null;
        WaitingForFrameTextBlock.Visibility = Visibility.Visible;
        _processes.Clear();
        ActiveAppTextBlock.Text = "—";
        ResetStreamMeta();

        _selectedStudentHints.Clear();
        if (_hintsByStudent.TryGetValue(selected.Id, out var hints))
        {
            foreach (var h in hints)
            {
                _selectedStudentHints.Add(h);
            }
        }

        await _hub.SelectStudentAsync(selected.Id);
        RefreshAppSuggestions();
    }

    private void OnScreenFrameReceived(string studentId, JpegFrame frame)
    {
        if (studentId != _hub.SelectedStudentId || frame.Data == null || frame.Data.Length == 0)
            return;

        lock (_frameDecodeGate)
        {
            _pendingFrameStudentId = studentId;
            _pendingFrame = frame;
        }

        if (Interlocked.CompareExchange(ref _frameDecodeBusy, 1, 0) == 0)
        {
            _ = Task.Run(DecodePendingFrames);
        }
    }

    private void DecodePendingFrames()
    {
        while (true)
        {
            string? studentId;
            JpegFrame frame;
            lock (_frameDecodeGate)
            {
                studentId = _pendingFrameStudentId;
                frame = _pendingFrame;
                _pendingFrameStudentId = null;
                _pendingFrame = default;
            }

            if (studentId == null || frame.Data == null || frame.Data.Length == 0)
            {
                Interlocked.Exchange(ref _frameDecodeBusy, 0);
                var hasPending = false;
                lock (_frameDecodeGate)
                {
                    hasPending = _pendingFrameStudentId != null;
                }

                if (hasPending && Interlocked.CompareExchange(ref _frameDecodeBusy, 1, 0) == 0)
                {
                    continue;
                }

                return;
            }

            if (studentId != _hub.SelectedStudentId)
            {
                continue;
            }

            try
            {
                using var ms = new MemoryStream(frame.Data);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                Dispatcher.InvokeAsync(() =>
                {
                    if (studentId != _hub.SelectedStudentId)
                    {
                        return;
                    }

                    ScreenImage.Source = bitmap;
                    if (_streamFullscreen)
                        FullscreenImage.Source = bitmap;
                    WaitingForFrameTextBlock.Visibility = Visibility.Collapsed;
                    UpdateStreamMeta(frame.Width, frame.Height, frame.TimestampMs);
                });
            }
            catch
            {
                // Ignore rendering errors on broken frames
            }
        }
    }

    private void OnPreviewFrameReceived(string studentId, JpegFrame frame)
    {
        if (!_screenGridMode || !_hub.IsRunning || frame.Data == null || frame.Data.Length == 0)
        {
            return;
        }

        lock (_previewDecodeGate)
        {
            _pendingPreviewFrames[studentId] = frame;
        }

        if (Interlocked.CompareExchange(ref _previewDecodeBusy, 1, 0) == 0)
        {
            _ = Task.Run(DecodePendingPreviews);
        }
    }

    private void DecodePendingPreviews()
    {
        while (true)
        {
            List<KeyValuePair<string, JpegFrame>> batch;
            lock (_previewDecodeGate)
            {
                if (_pendingPreviewFrames.Count == 0)
                {
                    Interlocked.Exchange(ref _previewDecodeBusy, 0);
                    if (_pendingPreviewFrames.Count > 0
                        && Interlocked.CompareExchange(ref _previewDecodeBusy, 1, 0) == 0)
                    {
                        continue;
                    }

                    return;
                }

                batch = [.. _pendingPreviewFrames];
                _pendingPreviewFrames.Clear();
            }

            foreach (var pair in batch)
            {
                var studentId = pair.Key;
                var frame = pair.Value;
                if (frame.Data == null || frame.Data.Length == 0)
                {
                    continue;
                }

                try
                {
                    using var ms = new MemoryStream(frame.Data, writable: false);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    Dispatcher.InvokeAsync(() =>
                    {
                        if (!_screenGridMode || !_hub.IsRunning)
                        {
                            return;
                        }

                        var vm = _students.FirstOrDefault(s => s.Id == studentId);
                        if (vm == null || vm.Status != StudentHubStatus.Online)
                        {
                            return;
                        }

                        vm.PreviewImage = bitmap;
                    });
                }
                catch
                {
                }
            }
        }
    }

    private void OnProcessListReceived(string studentId, WireMessage msg)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (studentId != _hub.SelectedStudentId)
                return;

            var selectedPid = GetSelectedProcess()?.Pid;
            var selectedExe = GetSelectedProcess()?.Exe;
            _processes.Clear();
            if (msg.Items != null)
            {
                foreach (var item in msg.Items)
                {
                    var isActive = !string.IsNullOrWhiteSpace(msg.ActiveExe)
                        && string.Equals(item.Exe, msg.ActiveExe, StringComparison.OrdinalIgnoreCase);
                    _processes.Add(new ProcessRowViewModel(item, isActive));
                }
            }

            if (selectedPid is int pid)
            {
                var match = _processes.FirstOrDefault(p => p.Pid == pid)
                    ?? _processes.FirstOrDefault(p => string.Equals(p.Exe, selectedExe, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    ProcessesListView.SelectedItem = match;
                    ProcessesListView.ScrollIntoView(match);
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
                RefreshAppSuggestions();
            }
        });
    }

    private void UpdateUiState(bool isRunning)
    {
        ClassNameTextBox.IsEnabled = !isRunning;
        LessonEditPanel.Visibility = isRunning ? Visibility.Collapsed : Visibility.Visible;
        LessonActiveBadge.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        NewPinButton.IsEnabled = !isRunning;
        StartClassButton.IsEnabled = !isRunning;
        StopClassButton.IsEnabled = isRunning;

        if (isRunning)
        {
            var lessonName = ClassNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(lessonName))
            {
                lessonName = "Класс";
            }

            LessonActiveTextBlock.Text = $"Урок: {lessonName} (активен)";
            StatusTextBlock.Text = FormatClassRunningStatus();
            ClassStateBadgeText.Text = "Активен";
            ClassStateDot.Fill = (Brush)FindResource("Brush.Emerald");
            ClassStateBadge.Background = (Brush)FindResource("Brush.EmeraldSoft");
        }
        else
        {
            StatusTextBlock.Text = "класс остановлен";
            ClassStateBadgeText.Text = "Остановлен";
            ClassStateDot.Fill = (Brush)FindResource("Brush.TextMuted");
            ClassStateBadge.Background = (Brush)FindResource("Brush.SurfaceMuted");
        }

        RefreshStudentCount();
        RefreshGroupsFromHub();
    }

    private string FormatClassRunningStatus()
    {
        var online = _students.Count(s => s.Status == StudentHubStatus.Online);
        return $"класс запущен · порт {_hub.TcpPort} · учеников: {online}";
    }

    private void OnStudentJoined(ConnectedStudent student)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            UpsertStudentRow(student);
            await PushBlockListToStudentAsync(student.Id);
            await SyncPreviewEnableIfGridAsync();
        });
    }

    private void OnStudentStatusChanged(ConnectedStudent student)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            UpsertStudentRow(student);
            if (student.Status == StudentHubStatus.Online)
            {
                await PushBlockListToStudentAsync(student.Id);
                await SyncPreviewEnableIfGridAsync();
            }
        });
    }

    private void UpsertStudentRow(ConnectedStudent student)
    {
        var existing = _students.FirstOrDefault(s => s.Id == student.Id);
        if (existing == null)
        {
            _students.Add(new StudentItemViewModel
            {
                Id = student.Id,
                DisplayName = student.DisplayName,
                Hostname = student.Hostname,
                Status = student.Status,
                GroupName = ResolveGroupName(student.Id)
            });
        }
        else
        {
            existing.DisplayName = student.DisplayName;
            existing.Hostname = student.Hostname;
            existing.Status = student.Status;
            existing.GroupName = ResolveGroupName(student.Id);
        }

        if (_hub.IsRunning)
        {
            StatusTextBlock.Text = FormatClassRunningStatus();
        }

        RefreshStudentCount();
    }

    private async Task PushBlockListToStudentAsync(string studentId)
    {
        if (!_hub.IsRunning || string.IsNullOrWhiteSpace(studentId))
        {
            return;
        }

        await _hub.SendBlockListAsync(studentId, _blockedApps.ToList());
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
                StatusTextBlock.Text = FormatClassRunningStatus();
            }

            RefreshStudentCount();
        });
    }

    private void RefreshAppSuggestions()
    {
        IEnumerable<InstalledAppInfo> preferred = _selectedStudentHints;
        if (_selectedStudentHints.Count == 0)
        {
            preferred = _hintsByStudent.Values.SelectMany(list => list);
        }

        var catalog = AppSuggestionFilter.Merge(
            preferred,
            _quickApps,
            AppSuggestionCatalog.CommonApps);

        LaunchAppSuggestBox.SetCatalog(catalog);
        BlockAppSuggestBox.SetCatalog(catalog);
    }

    private void AddQuickApp_Click(object sender, RoutedEventArgs e)
    {
        var selected = LaunchAppSuggestBox.SelectedApp;
        string name;
        string exe;
        var path = NewAppPathTextBox.Text.Trim();

        if (selected != null)
        {
            name = selected.Name;
            exe = selected.Exe;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = selected.LaunchTarget ?? string.Empty;
            }
        }
        else
        {
            var typed = LaunchAppSuggestBox.QueryText;
            if (string.IsNullOrWhiteSpace(typed))
            {
                ShowInfoNotice("Добавление программы", "Выберите программу из списка или введите имя exe");
                return;
            }

            exe = ProcessNameHelper.Normalize(typed);
            name = Path.GetFileNameWithoutExtension(exe);
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(exe))
        {
            ShowInfoNotice("Добавление программы", "Укажите название и имя exe-файла программы");
            return;
        }

        EnsureQuickAppListed(name, exe, string.IsNullOrWhiteSpace(path) ? null : path);
    }

    private void EnsureQuickAppListed(string? name, string rawExe, string? launchTarget)
    {
        var exe = ProcessNameHelper.Normalize(rawExe);
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        var displayName = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(exe) : name.Trim();
        var existing = _quickApps.FirstOrDefault(a => string.Equals(a.Exe, exe, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new InstalledAppInfo
            {
                Name = displayName,
                Exe = exe,
                LaunchTarget = string.IsNullOrWhiteSpace(launchTarget) ? null : launchTarget
            };
            _quickApps.Add(existing);
            SavePreset();
            RefreshAppSuggestions();
        }
        else if (!string.IsNullOrWhiteSpace(launchTarget) && string.IsNullOrWhiteSpace(existing.LaunchTarget))
        {
            existing.LaunchTarget = launchTarget;
            SavePreset();
        }

        QuickAppsListBox.SelectedItem = existing;
        QuickAppsListBox.ScrollIntoView(existing);
        LaunchAppSuggestBox.Clear();
        NewAppPathTextBox.Clear();
    }

    private void RemoveQuickApp_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAppsListBox.SelectedItem is InstalledAppInfo app)
        {
            _quickApps.Remove(app);
            SavePreset();
        }
    }

    private Task<bool> ShowConflictDialogAsync(string title, string message, string confirmButtonText, bool isDestructive = false)
    {
        return ShowAppNoticeAsync(
            title,
            "Конфликт действия",
            message,
            confirmButtonText,
            "Отмена",
            isDestructive,
            isEndClass: false);
    }

    private Task<string?> ShowGroupPromptAsync(string title, string confirmButtonText, string? initial = null)
    {
        _groupPromptTcs?.TrySetResult(null);
        _groupPromptTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        GroupPromptTitle.Text = title;
        GroupPromptConfirmButton.Content = confirmButtonText;
        GroupPromptTextBox.Text = initial ?? string.Empty;
        GroupPromptOverlay.Visibility = Visibility.Visible;
        GroupPromptTextBox.Focus();
        GroupPromptTextBox.SelectAll();

        return _groupPromptTcs.Task;
    }

    private void DismissGroupPrompt(string? result)
    {
        if (GroupPromptOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        GroupPromptOverlay.Visibility = Visibility.Collapsed;
        _groupPromptTcs?.TrySetResult(result);
    }

    private void GroupPromptConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var name = GroupPromptTextBox.Text;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        DismissGroupPrompt(name.Trim());
    }

    private void GroupPromptCancelButton_Click(object sender, RoutedEventArgs e)
    {
        DismissGroupPrompt(null);
    }

    private void GroupPromptTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            GroupPromptConfirmButton_Click(sender, e);
        }
    }

    private Task<GroupItemViewModel?> PickGroupAsync(string title, string confirmButtonText)
    {
        _groupPickTcs?.TrySetResult(null);
        _groupPickTcs = new TaskCompletionSource<GroupItemViewModel?>(TaskCreationOptions.RunContinuationsAsynchronously);
        GroupPickTitle.Text = title;
        GroupPickConfirmButton.Content = confirmButtonText;
        GroupPickListBox.ItemsSource = _groupItems;
        GroupPickListBox.SelectedItem = null;
        GroupPickOverlay.Visibility = Visibility.Visible;
        GroupPickListBox.Focus();
        return _groupPickTcs.Task;
    }

    private void DismissGroupPick(GroupItemViewModel? result)
    {
        if (GroupPickOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        GroupPickOverlay.Visibility = Visibility.Collapsed;
        _groupPickTcs?.TrySetResult(result);
    }

    private void GroupPickConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (GroupPickListBox.SelectedItem is GroupItemViewModel group)
        {
            DismissGroupPick(group);
        }
    }

    private void GroupPickCancelButton_Click(object sender, RoutedEventArgs e)
    {
        DismissGroupPick(null);
    }

    private void GroupPickListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GroupPickListBox.SelectedItem is GroupItemViewModel group)
        {
            DismissGroupPick(group);
        }
    }

    private void GroupPickListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not GroupItemViewModel group)
        {
            return;
        }

        e.Handled = true;
        Dispatcher.BeginInvoke(() => DismissGroupPick(group));
    }

    private void GroupPickListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            GroupPickConfirmButton_Click(sender, e);
        }
    }

    private async Task LaunchSingleAppCoreAsync(string rawExe, string? launchTarget, string? name = null)
    {
        if (!EnsureClassRunning("Быстрый запуск"))
        {
            return;
        }

        var selected = GetSelectedStudentsFromList();
        if (selected.Count == 0)
        {
            ShowInfoNotice("Быстрый запуск", "Выберите ученика из списка слева");
            return;
        }

        var target = selected.FirstOrDefault(s => s.Status == StudentHubStatus.Online);
        if (target == null)
        {
            StatusTextBlock.Text = selected.Count == 1
                ? $"Ученик «{selected[0].DisplayName}» не онлайн"
                : "Выбранные ученики не онлайн";
            return;
        }

        var exe = ProcessNameHelper.Normalize(rawExe);
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        var existingBlocked = _blockedApps.FirstOrDefault(b => string.Equals(b, exe, StringComparison.OrdinalIgnoreCase));
        if (existingBlocked != null)
        {
            var confirm = await ShowConflictDialogAsync("Программа заблокирована", $"«{exe}» в блоклисте.", "Всё равно открыть", isDestructive: false);
            if (!confirm)
            {
                return;
            }

            _blockedApps.Remove(existingBlocked);
            SavePreset();
            await BroadcastCurrentBlockListAsync();
        }

        var sent = await _hub.SendLaunchAppAfterBlockListAsync(target.Id, _blockedApps.ToList(), exe, launchTarget);
        StatusTextBlock.Text = sent
            ? $"Команда запуска «{name ?? exe}» отправлена выбранному ученику"
            : "Не удалось отправить команду запуска";
    }

    private async void LaunchSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAppsListBox.SelectedItem is not InstalledAppInfo app)
        {
            ShowInfoNotice("Быстрый запуск", "Выберите программу из списка быстрого запуска");
            return;
        }

        await LaunchSingleAppCoreAsync(app.Exe, app.LaunchTarget, app.Name);
    }

    private async void LaunchAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Быстрый запуск"))
        {
            return;
        }

        if (QuickAppsListBox.SelectedItem is not InstalledAppInfo app)
        {
            ShowInfoNotice("Быстрый запуск", "Выберите программу из списка быстрого запуска");
            return;
        }

        var exe = ProcessNameHelper.Normalize(app.Exe);
        var existingBlocked = _blockedApps.FirstOrDefault(b => string.Equals(b, exe, StringComparison.OrdinalIgnoreCase));
        if (existingBlocked != null)
        {
            var confirm = await ShowConflictDialogAsync("Программа заблокирована", $"«{exe}» в блоклисте.", "Всё равно открыть", isDestructive: false);
            if (!confirm)
            {
                return;
            }

            _blockedApps.Remove(existingBlocked);
            SavePreset();
        }

        var count = await _hub.BroadcastLaunchAppAfterBlockListAsync(_blockedApps.ToList(), exe, app.LaunchTarget);
        StatusTextBlock.Text = count > 0
            ? $"Команда запуска «{app.Name}» отправлена {count} ученикам"
            : "Не удалось отправить команду запуска";
    }

    private async void LaunchGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Быстрый запуск"))
        {
            return;
        }

        if (QuickAppsListBox.SelectedItem is not InstalledAppInfo app)
        {
            ShowInfoNotice("Быстрый запуск", "Выберите программу из списка быстрого запуска");
            return;
        }

        var group = await PickExistingGroupAsync("Быстрый запуск", "Запустить у группы", "Запустить");
        if (group == null)
        {
            return;
        }

        if (_hub.GetOnlineStudentIdsInGroup(group.Id).Count == 0)
        {
            StatusTextBlock.Text = DescribeUnavailableGroup(group);
            return;
        }

        var exe = ProcessNameHelper.Normalize(app.Exe);
        var existingBlocked = _blockedApps.FirstOrDefault(b => string.Equals(b, exe, StringComparison.OrdinalIgnoreCase));
        if (existingBlocked != null)
        {
            var confirm = await ShowConflictDialogAsync("Программа заблокирована", $"«{exe}» в блоклисте.", "Всё равно открыть", isDestructive: false);
            if (!confirm)
            {
                return;
            }

            _blockedApps.Remove(existingBlocked);
            SavePreset();
        }

        var count = await _hub.SendLaunchAppAfterBlockListToGroupAsync(group.Id, _blockedApps.ToList(), exe, app.LaunchTarget);
        StatusTextBlock.Text = count > 0
            ? $"Команда запуска «{app.Name}» отправлена группе «{group.Name}» ({count})"
            : "Не удалось отправить команду запуска";
    }

    private string? GetTeacherMessageTextOrNull()
    {
        var text = TeacherMessageTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowTeacherMessageFeedback("Введите текст уведомления", ok: false);
            return null;
        }

        return text;
    }

    private void ShowTeacherMessageFeedback(string text, bool ok)
    {
        StatusTextBlock.Text = text;
        TeacherMessageFeedbackTextBlock.Text = text;
        TeacherMessageFeedbackTextBlock.Foreground = ok
            ? (Brush)FindResource("Brush.Emerald")
            : (Brush)FindResource("Brush.Rose");
        var token = ++_messageFeedbackSeq;
        if (!ok)
        {
            return;
        }

        try
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch
        {
        }

        _ = HideTeacherMessageFeedbackLaterAsync(token);
    }

    private async Task HideTeacherMessageFeedbackLaterAsync(int token)
    {
        try
        {
            await Task.Delay(4000);
        }
        catch
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            if (token != _messageFeedbackSeq)
            {
                return;
            }

            TeacherMessageFeedbackTextBlock.Text = string.Empty;
        });
    }

    private void RecordTeacherSentMessage(string body, string audience, int delivered, int attempted)
    {
        var ok = delivered > 0;
        var status = !ok
            ? "Не доставлено"
            : delivered == attempted
                ? (delivered == 1 ? "Доставлено" : $"Доставлено · {delivered}")
                : $"Доставлено · {delivered} из {attempted}";

        _sentMessages.Insert(0, new TeacherSentMessageViewModel
        {
            TimeText = DateTime.Now.ToString("HH:mm"),
            Audience = audience,
            Body = body.Trim(),
            StatusText = status,
            Ok = ok
        });

        while (_sentMessages.Count > 80)
        {
            _sentMessages.RemoveAt(_sentMessages.Count - 1);
        }

        TeacherMessageHistoryEmpty.Visibility = Visibility.Collapsed;
        TeacherMessageHistoryListBox.ScrollIntoView(_sentMessages[0]);
    }

    private void ClearTeacherSentMessages()
    {
        _sentMessages.Clear();
        TeacherMessageHistoryEmpty.Visibility = Visibility.Visible;
    }

    private static string FormatSelectedAudience(IReadOnlyList<StudentItemViewModel> students)
    {
        if (students.Count == 1)
        {
            return students[0].DisplayName;
        }

        if (students.Count <= 3)
        {
            return string.Join(", ", students.Select(s => s.DisplayName));
        }

        return $"{students.Count} учеников";
    }

    private async void SendSelectedMessageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Сообщение классу"))
        {
            return;
        }

        var text = GetTeacherMessageTextOrNull();
        if (text == null)
        {
            return;
        }

        var selected = GetSelectedStudentsFromList();
        if (selected.Count == 0)
        {
            ShowTeacherMessageFeedback("Выберите ученика из списка слева", ok: false);
            return;
        }

        var online = selected.Where(s => s.Status == StudentHubStatus.Online).ToList();
        if (online.Count == 0)
        {
            ShowTeacherMessageFeedback(
                selected.Count == 1
                    ? $"Ученик «{selected[0].DisplayName}» не онлайн"
                    : "Выбранные ученики не онлайн",
                ok: false);
            return;
        }

        var sent = 0;
        foreach (var student in online)
        {
            if (await _hub.SendTeacherMessageAsync(student.Id, text))
            {
                sent++;
            }
        }

        ShowTeacherMessageFeedback(
            sent == 0
                ? "Не удалось отправить сообщение"
                : sent == 1
                    ? "Сообщение отправлено выбранному ученику"
                    : $"Сообщение отправлено {sent} выбранным ученикам",
            ok: sent > 0);
        RecordTeacherSentMessage(text, FormatSelectedAudience(online), sent, online.Count);
    }

    private async void SendGroupMessageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Сообщение классу"))
        {
            return;
        }

        var text = GetTeacherMessageTextOrNull();
        if (text == null)
        {
            return;
        }

        var group = await PickExistingGroupAsync("Сообщение классу", "Отправить группе", "Отправить");
        if (group == null)
        {
            return;
        }

        if (_hub.GetOnlineStudentIdsInGroup(group.Id).Count == 0)
        {
            ShowTeacherMessageFeedback(DescribeUnavailableGroup(group), ok: false);
            return;
        }

        var attempted = _hub.GetOnlineStudentIdsInGroup(group.Id).Count;
        var count = await _hub.SendTeacherMessageToGroupAsync(group.Id, text);
        ShowTeacherMessageFeedback(
            count > 0
                ? $"Сообщение отправлено группе «{group.Name}» ({count})"
                : "Не удалось отправить сообщение",
            ok: count > 0);
        RecordTeacherSentMessage(text, $"Группа «{group.Name}»", count, attempted);
    }

    private async void SendAllMessageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Сообщение классу"))
        {
            return;
        }

        var text = GetTeacherMessageTextOrNull();
        if (text == null)
        {
            return;
        }

        var attempted = _students.Count(s => s.Status == StudentHubStatus.Online);
        var count = await _hub.BroadcastTeacherMessageAsync(text);
        ShowTeacherMessageFeedback(
            count > 0
                ? $"Сообщение отправлено {count} ученикам"
                : attempted == 0
                    ? "Нет учеников онлайн"
                    : "Не удалось отправить сообщение",
            ok: count > 0);
        RecordTeacherSentMessage(text, "Весь класс", count, Math.Max(attempted, count));
    }

    private async Task<int> BroadcastCurrentBlockListAsync()
    {
        if (!_hub.IsRunning)
        {
            return 0;
        }

        return await _hub.BroadcastBlockListAsync(_blockedApps.ToList());
    }

    private void AddBlockedApp_Click(object sender, RoutedEventArgs e)
    {
        _ = AddBlockedAppCoreAsync();
    }

    private async Task AddBlockedAppCoreAsync(InstalledAppInfo? submitted = null)
    {
        var selected = submitted ?? BlockAppSuggestBox.SelectedApp;
        var exe = selected != null
            ? ProcessNameHelper.Normalize(selected.Exe)
            : ProcessNameHelper.Normalize(BlockAppSuggestBox.QueryText);
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        if (_processes.Any(p => string.Equals(p.Exe, exe, StringComparison.OrdinalIgnoreCase)))
        {
            var confirm = await ShowConflictDialogAsync("Программа открыта", $"«{exe}» сейчас открыто.", "Всё равно запретить", isDestructive: true);
            if (!confirm)
            {
                return;
            }
        }

        if (!_blockedApps.Any(a => string.Equals(a, exe, StringComparison.OrdinalIgnoreCase)))
        {
            _blockedApps.Add(exe);
            SavePreset();
        }

        var listed = _blockedApps.First(a => string.Equals(a, exe, StringComparison.OrdinalIgnoreCase));
        BlockedAppsListBox.SelectedItem = listed;
        BlockedAppsListBox.ScrollIntoView(listed);
        BlockAppSuggestBox.Clear();
        if (_hub.IsRunning)
        {
            var count = await BroadcastCurrentBlockListAsync();
            StatusTextBlock.Text = $"«{exe}» в блоклисте, список отправлен {count} ученикам";
        }
        else
        {
            StatusTextBlock.Text = $"«{exe}» добавлена в блоклист пресета";
        }
    }

    private async void RemoveBlockedApp_Click(object sender, RoutedEventArgs e)
    {
        if (BlockedAppsListBox.SelectedItem is not string exe)
        {
            return;
        }

        _blockedApps.Remove(exe);
        SavePreset();

        if (_hub.IsRunning)
        {
            var count = await BroadcastCurrentBlockListAsync();
            StatusTextBlock.Text = $"«{exe}» снята с блоклиста, список отправлен {count} ученикам";
        }
    }

    private async void ApplyBlockListAll_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Блокировка приложений"))
        {
            return;
        }

        var count = await BroadcastCurrentBlockListAsync();
        StatusTextBlock.Text = $"Блоклист ({_blockedApps.Count} программ) отправлен {count} ученикам";
    }

    private async void ApplyBlockListSelected_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Блокировка приложений"))
        {
            return;
        }

        var selected = GetSelectedStudentsFromList();
        if (selected.Count == 0)
        {
            ShowInfoNotice("Блокировка приложений", "Выберите ученика из списка слева");
            return;
        }

        var target = selected.FirstOrDefault(s => s.Status == StudentHubStatus.Online);
        if (target == null)
        {
            StatusTextBlock.Text = selected.Count == 1
                ? $"Ученик «{selected[0].DisplayName}» не онлайн"
                : "Выбранные ученики не онлайн";
            return;
        }

        var sent = await _hub.SendBlockListAsync(target.Id, _blockedApps.ToList());
        StatusTextBlock.Text = sent
            ? $"Блоклист ({_blockedApps.Count} программ) отправлен выбранному ученику"
            : "Не удалось отправить блоклист";
    }

    private async void ApplyBlockListGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureClassRunning("Блокировка приложений"))
        {
            return;
        }

        var group = await PickExistingGroupAsync("Блокировка приложений", "Отправить группе", "Отправить");
        if (group == null)
        {
            return;
        }

        if (_hub.GetOnlineStudentIdsInGroup(group.Id).Count == 0)
        {
            StatusTextBlock.Text = DescribeUnavailableGroup(group);
            return;
        }

        var count = await _hub.SendBlockListToGroupAsync(group.Id, _blockedApps.ToList());
        StatusTextBlock.Text = count > 0
            ? $"Блоклист ({_blockedApps.Count} программ) отправлен группе «{group.Name}» ({count})"
            : "Не удалось отправить блоклист";
    }

    private async void ClearBlockListAll_Click(object sender, RoutedEventArgs e)
    {
        _blockedApps.Clear();
        SavePreset();

        if (_hub.IsRunning)
        {
            var count = await BroadcastCurrentBlockListAsync();
            StatusTextBlock.Text = $"Запреты сняты у {count} учеников";
        }
        else
        {
            StatusTextBlock.Text = "Все запреты сняты";
        }
    }

    private async void BlockProcessMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await BlockSelectedProcessCoreAsync();
    }

    private async void BlockSelectedProcessButton_Click(object sender, RoutedEventArgs e)
    {
        await BlockSelectedProcessCoreAsync();
    }

    private async void ProcessesListView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            FocusSelectedProcessRow();
            return;
        }

        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            await BlockSelectedProcessCoreAsync();
        }
    }

    private void ProcessesListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindVisualAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        if (item == null)
        {
            return;
        }

        item.IsSelected = true;
        item.Focus();
    }

    private void ProcessesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        FocusSelectedProcessRow();
    }

    private void FocusSelectedProcessRow()
    {
        var proc = GetSelectedProcess();
        if (proc == null)
        {
            return;
        }

        if (ProcessesListView.SelectedItem != null)
        {
            ProcessesListView.ScrollIntoView(ProcessesListView.SelectedItem);
        }

        var title = string.IsNullOrWhiteSpace(proc.Title) ? "без заголовка" : proc.Title;
        StatusTextBlock.Text = $"{proc.Exe} · PID {proc.Pid} · {title}";
    }

    private bool EnsureClassRunning(string caption)
    {
        if (_hub.IsRunning)
        {
            return true;
        }

        ShowInfoNotice(caption, "Сначала начните класс");
        return false;
    }

    private static void ApplyCachedUniformGridColumns(
        ref UniformGrid? cached,
        ref int lastColumns,
        DependencyObject host,
        int columns)
    {
        if (columns == lastColumns && cached != null)
        {
            return;
        }

        cached ??= FindVisualChild<UniformGrid>(host);
        if (cached == null)
        {
            return;
        }

        if (cached.Columns != columns)
        {
            cached.Columns = columns;
        }

        lastColumns = columns;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private async void QuickAppsListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && QuickAppsListBox.SelectedItem is InstalledAppInfo app)
        {
            e.Handled = true;
            await LaunchSingleAppCoreAsync(app.Exe, app.LaunchTarget, app.Name);
        }
    }

    private async void BlockedAppsListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (_hub.IsRunning)
            {
                var count = await BroadcastCurrentBlockListAsync();
                StatusTextBlock.Text = $"Блоклист ({_blockedApps.Count} программ) отправлен {count} ученикам";
            }
        }
    }

    private void HintsListView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && HintsListView.SelectedItem is InstalledAppInfo)
        {
            e.Handled = true;
            AddHintToQuickApps_Click(sender, e);
        }
    }

    private ProcessItemInfo? GetSelectedProcess()
    {
        return (ProcessesListView.SelectedItem as ProcessRowViewModel)?.Item;
    }

    private async Task BlockSelectedProcessCoreAsync()
    {
        var proc = GetSelectedProcess();
        if (proc == null || string.IsNullOrWhiteSpace(proc.Exe))
        {
            ShowInfoNotice("Блокировка процесса", "Выберите процесс из таблицы");
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
            var count = await BroadcastCurrentBlockListAsync();
            StatusTextBlock.Text = $"Процесс «{exe}» в блоклисте, список отправлен {count} ученикам";
        }
        else
        {
            StatusTextBlock.Text = $"Процесс «{exe}» добавлен в список блокировок пресета";
        }
    }

    private void AddHintToQuickApps_Click(object sender, RoutedEventArgs e)
    {
        if (HintsListView.SelectedItem is not InstalledAppInfo hint)
        {
            ShowInfoNotice("Быстрый запуск", "Выберите программу из списка подсказок");
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
            RefreshAppSuggestions();
        }
    }

    private async void AddHintToBlockList_Click(object sender, RoutedEventArgs e)
    {
        if (HintsListView.SelectedItem is not InstalledAppInfo hint)
        {
            ShowInfoNotice("Блокировка приложений", "Выберите программу из списка подсказок");
            return;
        }

        var exe = hint.Exe.Trim();
        if (_processes.Any(p => string.Equals(p.Exe, exe, StringComparison.OrdinalIgnoreCase)))
        {
            var confirm = await ShowConflictDialogAsync("Программа открыта", $"«{exe}» сейчас открыто.", "Всё равно запретить", isDestructive: true);
            if (!confirm)
            {
                return;
            }
        }

        if (!_blockedApps.Any(a => string.Equals(a, exe, StringComparison.OrdinalIgnoreCase)))
        {
            _blockedApps.Add(exe);
            SavePreset();

            if (_hub.IsRunning)
            {
                var count = await BroadcastCurrentBlockListAsync();
                StatusTextBlock.Text = $"«{exe}» в блоклисте, список отправлен {count} ученикам";
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
        SaveUiSettings();

        if (_isClosingInProgress)
        {
            base.OnClosing(e);
            return;
        }

        if (_hub.IsRunning)
        {
            e.Cancel = true;
            if (_appNoticeIsEndClass)
            {
                return;
            }

            if (!await ShowEndClassConfirmAsync())
            {
                return;
            }

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
