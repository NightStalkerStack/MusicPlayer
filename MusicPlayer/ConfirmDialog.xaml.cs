using System.Windows;

namespace MusicPlayer;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmText, string cancelText)
    {
        InitializeComponent();
        AppTheme.ApplyRegisteredElements(this);

        Title = title;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
