using System.Windows.Controls;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace MusicPlayer.Views;

public partial class HomeView : WpfUserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    public void SetFavoritePlaylist(string? name, int playCount)
    {
        if (string.IsNullOrWhiteSpace(name) || playCount <= 0)
        {
            FavoritePlaylistNameTextBlock.Text = "\u6682\u65E0\u64AD\u653E\u8BB0\u5F55";
            FavoritePlaylistCountTextBlock.Text = "";
            return;
        }

        FavoritePlaylistNameTextBlock.Text = name;
        FavoritePlaylistCountTextBlock.Text = $"{playCount} \u6B21\u64AD\u653E";
    }

    public void SetFavoriteTrack(string? title, string? artist, string? album, int playCount)
    {
        if (string.IsNullOrWhiteSpace(title) || playCount <= 0)
        {
            FavoriteTrackTitleTextBlock.Text = "\u6682\u65E0\u64AD\u653E\u8BB0\u5F55";
            FavoriteTrackMetaTextBlock.Text = "";
            FavoriteTrackCountTextBlock.Text = "";
            return;
        }

        FavoriteTrackTitleTextBlock.Text = title;
        FavoriteTrackMetaTextBlock.Text = $"{artist} \u00B7 {album}";
        FavoriteTrackCountTextBlock.Text = $"{playCount} \u6B21\u64AD\u653E";
    }
}
