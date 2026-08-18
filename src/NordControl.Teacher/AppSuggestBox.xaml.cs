using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NordControl.Core.Policies;
using NordControl.Protocol;

namespace NordControl.Teacher;

public partial class AppSuggestBox : UserControl
{
    private IReadOnlyList<InstalledAppInfo> _catalog = Array.Empty<InstalledAppInfo>();
    private bool _suppressFilter;

    private Window? _hostWindow;

    public AppSuggestBox()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow != null)
        {
            _hostWindow.PreviewMouseDown += Host_PreviewMouseDown;
            _hostWindow.Deactivated += Host_Deactivated;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hostWindow != null)
        {
            _hostWindow.PreviewMouseDown -= Host_PreviewMouseDown;
            _hostWindow.Deactivated -= Host_Deactivated;
            _hostWindow = null;
        }
    }

    private void Host_Deactivated(object? sender, EventArgs e)
    {
        SuggestPopup.IsOpen = false;
    }

    private void Host_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!SuggestPopup.IsOpen)
        {
            return;
        }

        if (IsMouseOver)
        {
            return;
        }

        if (SuggestPopup.Child is FrameworkElement popupRoot && popupRoot.IsMouseOver)
        {
            return;
        }

        SuggestPopup.IsOpen = false;
    }

    public InstalledAppInfo? SelectedApp { get; private set; }

    public string QueryText => QueryTextBox.Text.Trim();

    public string Placeholder
    {
        get => QueryTextBox.Tag as string ?? string.Empty;
        set => QueryTextBox.Tag = value;
    }

    public event Action<InstalledAppInfo>? SuggestionChosen;

    public void SetCatalog(IReadOnlyList<InstalledAppInfo> catalog)
    {
        _catalog = catalog ?? Array.Empty<InstalledAppInfo>();
        if (IsKeyboardFocusWithin)
        {
            RefreshSuggestions(open: SuggestPopup.IsOpen);
        }
    }

    public void Clear()
    {
        _suppressFilter = true;
        SelectedApp = null;
        QueryTextBox.Clear();
        SuggestPopup.IsOpen = false;
        SuggestListBox.ItemsSource = null;
        _suppressFilter = false;
    }

    private void QueryTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        RefreshSuggestions(open: true);
    }

    private void QueryTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFilter)
        {
            return;
        }

        SelectedApp = null;
        RefreshSuggestions(open: true);
    }

    private void QueryTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            RefreshSuggestions(open: true);
            if (SuggestListBox.Items.Count > 0)
            {
                SuggestListBox.Focus();
                SuggestListBox.SelectedIndex = 0;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            SuggestPopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && SuggestPopup.IsOpen && SuggestListBox.SelectedItem is InstalledAppInfo selected)
        {
            ApplySuggestion(selected);
            e.Handled = true;
        }
    }

    private void SuggestListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SuggestListBox.SelectedItem is InstalledAppInfo selected)
        {
            ApplySuggestion(selected);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SuggestPopup.IsOpen = false;
            QueryTextBox.Focus();
            e.Handled = true;
        }
    }

    private void SuggestListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(SuggestListBox, e.OriginalSource as DependencyObject) is ListBoxItem item
            && item.DataContext is InstalledAppInfo app)
        {
            SuggestListBox.SelectedItem = app;
            ApplySuggestion(app);
            e.Handled = true;
        }
    }

    private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (IsKeyboardFocusWithin || SuggestListBox.IsKeyboardFocusWithin || SuggestListBox.IsMouseOver)
            {
                return;
            }

            SuggestPopup.IsOpen = false;
        });
    }

    private void RefreshSuggestions(bool open)
    {
        var items = AppSuggestionFilter.Filter(_catalog, QueryTextBox.Text);
        SuggestListBox.ItemsSource = items;
        SuggestPopup.IsOpen = open && items.Count > 0 && IsKeyboardFocusWithin;
        if (SuggestPopup.IsOpen && SuggestListBox.SelectedIndex < 0 && items.Count > 0)
        {
            SuggestListBox.SelectedIndex = 0;
        }
    }

    private void ApplySuggestion(InstalledAppInfo app)
    {
        _suppressFilter = true;
        SelectedApp = app;
        QueryTextBox.Text = app.Name;
        QueryTextBox.CaretIndex = QueryTextBox.Text.Length;
        SuggestPopup.IsOpen = false;
        _suppressFilter = false;
        SuggestionChosen?.Invoke(app);
        QueryTextBox.Focus();
    }
}
