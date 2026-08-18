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
        BodyTextBlock.Text = body;
        ConfirmButton.Content = confirmText;
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
