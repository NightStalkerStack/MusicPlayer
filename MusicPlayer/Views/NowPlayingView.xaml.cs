using System.Windows;
using System.Windows.Controls;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace MusicPlayer.Views;

public partial class NowPlayingView : WpfUserControl
{
    public event RoutedEventHandler? OpenFileRequested;

    public NowPlayingView()
    {
        InitializeComponent();
    }

    public void SetSongInfo(string songName, string filePath)
    {
        SongNameTextBlock.Text = songName;
        SongPathTextBlock.Text = filePath;
    }

    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileRequested?.Invoke(this, e);
    }
}
