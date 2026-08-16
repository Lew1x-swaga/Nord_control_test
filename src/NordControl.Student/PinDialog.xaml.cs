using System.Windows;

namespace NordControl.Student;

public partial class PinDialog : Window
{
    private readonly string _expectedPin;

    public bool IsPinCorrect { get; private set; }

    public PinDialog(string expectedPin, Window owner)
    {
        InitializeComponent();
        _expectedPin = expectedPin;
        Owner = owner;

        Loaded += (_, _) =>
        {
            PinInputTextBox.Focus();
        };
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var entered = PinInputTextBox.Text.Trim();
        if (entered == _expectedPin)
        {
            IsPinCorrect = true;
            DialogResult = true;
            Close();
        }
        else
        {
            ErrorTextBlock.Text = "Неверный PIN";
            ErrorTextBlock.Visibility = Visibility.Visible;
            PinInputTextBox.SelectAll();
            PinInputTextBox.Focus();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        IsPinCorrect = false;
        DialogResult = false;
        Close();
    }
}
