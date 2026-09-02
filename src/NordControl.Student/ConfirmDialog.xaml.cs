using System.Windows;
using System.Windows.Input;

namespace NordControl.Student;

public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmDialog(Window owner, string title, string caption, string body, string confirmText)
    {
        InitializeComponent();
        Owner = owner;
        TitleTextBlock.Text = title;
        CaptionTextBlock.Text = caption;
        CaptionTextBlock.Visibility = string.IsNullOrWhiteSpace(caption)
            ? Visibility.Collapsed
            : Visibility.Visible;
        BodyTextBlock.Text = body;
        ConfirmButton.Content = confirmText;
        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + Math.Max(0, (workArea.Width - ActualWidth) / 2);
            Top = workArea.Top + 96;
        };
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase)
            return;
        DragMove();
    }
}
