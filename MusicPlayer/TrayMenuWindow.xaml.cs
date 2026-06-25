using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace MusicPlayer;

public partial class TrayMenuWindow : Window
{
    public event EventHandler? ShowWindowRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? PreviousRequested;

    public event EventHandler? PlayPauseRequested;

    public event EventHandler? NextRequested;

    private bool isClosingByCommand;

    public TrayMenuWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) =>
        {
            if (!isClosingByCommand)
            {
                Close();
            }
        };
    }

    public void SetPlaybackState(bool isPlaying)
    {
        PlayPauseTextBlock.Text = isPlaying ? "\u6682\u505C" : "\u64AD\u653E";
        PlayPauseIconPath.Data = Geometry.Parse(isPlaying
            ? "M4,2 L7,2 L7,14 L4,14 Z M10,2 L13,2 L13,14 L10,14 Z"
            : "M4,2 L13,8 L4,14 Z");
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAndRequest(() => PreviousRequested?.Invoke(this, EventArgs.Empty));
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAndRequest(() => PlayPauseRequested?.Invoke(this, EventArgs.Empty));
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAndRequest(() => NextRequested?.Invoke(this, EventArgs.Empty));
    }

    private void ShowWindowButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAndRequest(() => ShowWindowRequested?.Invoke(this, EventArgs.Empty));
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAndRequest(() => Dispatcher.BeginInvoke(
            () => ExitRequested?.Invoke(this, EventArgs.Empty),
            DispatcherPriority.Background));
    }

    private void CloseAndRequest(Action request)
    {
        isClosingByCommand = true;
        Close();
        request();
    }
}
