using System.IO;
using System.Text.Json;

namespace MusicPlayer;

public static class AppStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string StateFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicPlayer",
        "app-state.json");

    public static AppState Load()
    {
        try
        {
            if (!File.Exists(StateFilePath))
            {
                return new AppState();
            }

            var json = File.ReadAllText(StateFilePath);
            return JsonSerializer.Deserialize<AppState>(json, JsonOptions) ?? new AppState();
        }
        catch
        {
            return new AppState();
        }
    }

    public static void Save(AppState state)
    {
        var directory = Path.GetDirectoryName(StateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(StateFilePath, json);
    }
}

public sealed class AppState
{
    public int NextPlaylistId { get; set; } = 1;

    public int? SelectedPlaylistId { get; set; }

    public PlaybackStateData Playback { get; set; } = new();

    public AppSettingsData Settings { get; set; } = new();

    public WindowStateData Window { get; set; } = new();

    public List<PlaylistStateData> Playlists { get; set; } = [];
}

public sealed class WindowStateData
{
    public double Left { get; set; } = double.NaN;

    public double Top { get; set; } = double.NaN;

    public double Width { get; set; } = 980;

    public double Height { get; set; } = 640;

    public bool IsMaximized { get; set; }
}

public sealed class AppSettingsData
{
    public bool IsSidebarCollapsed { get; set; }

    public double Volume { get; set; } = 0.7;

    public bool IsMuted { get; set; }

    public double VolumeBeforeMute { get; set; } = 0.7;

    public string PlayMode { get; set; } = "SequentialLoop";

    public string ThemePreset { get; set; } = "System";

    public string CustomAccentColor { get; set; } = "#5AA9FF";
}

public sealed class PlaybackStateData
{
    public int? PlayingPlaylistId { get; set; }

    public int CurrentTrackIndex { get; set; } = -1;

    public string CurrentSongPath { get; set; } = "";

    public double PositionSeconds { get; set; }
}

public sealed class PlaylistStateData
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string CoverPath { get; set; } = "";

    public int PlayCount { get; set; }

    public List<PlaylistTrackStateData> Tracks { get; set; } = [];
}

public sealed class PlaylistTrackStateData
{
    public string FilePath { get; set; } = "";

    public string Title { get; set; } = "";

    public string DurationText { get; set; } = "";

    public string Artist { get; set; } = "";

    public string Album { get; set; } = "";

    public int PlayCount { get; set; }
}
