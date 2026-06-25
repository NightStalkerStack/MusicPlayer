using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Interop;
using System.Windows.Threading;
using MusicPlayer.Views;
using Microsoft.Win32;
using NAudio.Wave;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using WpfButton = System.Windows.Controls.Button;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfImage = System.Windows.Controls.Image;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace MusicPlayer;

public partial class MainWindow : Window
{
    private const double ExpandedSidebarWidth = 220;
    private const double CollapsedSidebarWidth = 64;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmWindowCornerRound = 2;
    private const int WmGetMinMaxInfo = 0x0024;
    private readonly DispatcherTimer progressTimer;
    private readonly DispatcherTimer saveTimer;
    private readonly List<PlaylistEntry> playlists = [];
    private Forms.NotifyIcon? notifyIcon;
    private TrayMenuWindow? trayMenuWindow;
    private HomeView? homeView;
    private NowPlayingView? nowPlayingView;
    private PlaylistContentView? playlistContentView;
    private LibraryView? libraryView;
    private SettingsView? settingsView;
    private AudioFileReader? audioFile;
    private WaveOutEvent? outputDevice;
    private readonly Random random = new();
    private string currentSongName = "\u8BF7\u9009\u62E9\u4E00\u4E2A\u97F3\u9891\u6587\u4EF6";
    private string currentSongPath = "";
    private string currentArtistName = "\u672A\u77E5\u6B4C\u624B";
    private string currentAlbumName = "\u672A\u77E5\u5531\u7247";
    private bool isDraggingProgress;
    private bool isSidebarCollapsed;
    private bool isChangingTrack;
    private bool isManualStop;
    private bool isMuted;
    private bool isUpdatingVolumeSlider;
    private bool isExitRequested;
    private bool isRestoringState;
    private bool hasPendingSave;
    private double volumeBeforeMute = 0.7;
    private int nextPlaylistId = 1;
    private int? selectedPlaylistId;
    private int? playingPlaylistId;
    private int currentTrackIndex = -1;
    private PlaylistPlayMode playMode = PlaylistPlayMode.SequentialLoop;
    private ThemePreset themePreset = ThemePreset.Dark;
    private PlaylistEntry? editingPlaylist;
    private PlaylistEntry? draggingPlaylist;
    private PlaylistEntry? draggingPlaylistTarget;
    private WpfPoint playlistDragStartPoint;
    private WpfPoint playlistDragOffsetFromRow;
    private int playlistDragStartIndex = -1;
    private DragPreviewAdorner? playlistDragAdorner;
    private InsertionLineAdorner? playlistInsertionAdorner;
    private AdornerLayer? playlistDragAdornerLayer;
    private readonly DispatcherTimer playlistAutoScrollTimer;
    private WpfPoint lastPlaylistDragPoint;
    private bool isDraggingPlaylist;
    private bool suppressNextPlaylistClick;

    public MainWindow()
    {
        InitializeComponent();
        PlaylistItemsPanel.PreviewMouseMove += PlaylistItemsPanel_PreviewMouseMove;
        PlaylistItemsPanel.PreviewMouseLeftButtonUp += PlaylistItemsPanel_PreviewMouseLeftButtonUp;

        progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        progressTimer.Tick += ProgressTimer_Tick;

        playlistAutoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(35)
        };
        playlistAutoScrollTimer.Tick += (_, _) =>
        {
            AutoScrollPlaylistList(lastPlaylistDragPoint);
            MoveDraggedPlaylistToPointer(lastPlaylistDragPoint);
        };

        saveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        saveTimer.Tick += SaveTimer_Tick;
        saveTimer.Start();

        PreviewMouseDown += MainWindow_PreviewMouseDown;
        SourceInitialized += (_, _) =>
        {
            AddWindowMessageHook();
            ApplyWindowCornerPreference();
        };
        StateChanged += (_, _) =>
        {
            UpdateWindowCornerRadius();
            QueueSaveState();
        };
        LocationChanged += (_, _) => QueueSaveState();
        SizeChanged += (_, _) => QueueSaveState();
        SetApplicationWindowIcon();
        InitializeNotifyIcon();
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        RestoreState();
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo monitorInfo);

    private static void AddWindowMessageHook()
    {
        if (PresentationSource.FromVisual(System.Windows.Application.Current.MainWindow) is HwndSource source)
        {
            source.AddHook(WindowMessageHook);
        }
    }

    private static IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            ApplyWorkAreaMaximizedSize(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void ApplyWorkAreaMaximizedSize(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, 0x00000002);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo();
        monitorInfo.Size = Marshal.SizeOf<MonitorInfo>();
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var workArea = monitorInfo.WorkArea;
        var monitorArea = monitorInfo.MonitorArea;
        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);

        minMaxInfo.MaxPosition.X = Math.Abs(workArea.Left - monitorArea.Left);
        minMaxInfo.MaxPosition.Y = Math.Abs(workArea.Top - monitorArea.Top);
        minMaxInfo.MaxSize.X = Math.Abs(workArea.Right - workArea.Left);
        minMaxInfo.MaxSize.Y = Math.Abs(workArea.Bottom - workArea.Top);

        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    private void ApplyWindowCornerPreference()
    {
        if (Environment.OSVersion.Version.Build < 22000)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        var preference = DwmWindowCornerRound;
        DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref preference, sizeof(int));
        UpdateWindowCornerRadius();
    }

    private void UpdateWindowCornerRadius()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        var radius = isMaximized ? new CornerRadius(0) : new CornerRadius(8);
        RootBorder.CornerRadius = radius;
        RootBorder.Margin = new Thickness(0);

        if (WindowChrome.GetWindowChrome(this) is { } chrome)
        {
            chrome.CornerRadius = radius;
        }
    }

    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (editingPlaylist is null || IsDescendantOf(e.OriginalSource as DependencyObject, editingPlaylist.NameTextBox))
        {
            return;
        }

        CommitPlaylistRename(editingPlaylist);
        Keyboard.ClearFocus();
    }

    private void RestoreState()
    {
        isRestoringState = true;

        var state = AppStorage.Load();
        isSidebarCollapsed = state.Settings.IsSidebarCollapsed;
        playMode = Enum.TryParse<PlaylistPlayMode>(state.Settings.PlayMode, out var savedMode)
            ? savedMode
            : PlaylistPlayMode.SequentialLoop;
        themePreset = AppTheme.ParsePreset(state.Settings.ThemePreset);
        ApplyTheme();
        isMuted = state.Settings.IsMuted;
        volumeBeforeMute = state.Settings.VolumeBeforeMute <= 0 ? 0.7 : state.Settings.VolumeBeforeMute;
        SetVolumeSliderValue(Math.Clamp(state.Settings.Volume, 0, 1));
        RestoreWindowState(state.Window);

        playlists.Clear();
        PlaylistItemsPanel.Children.Clear();

        foreach (var savedPlaylist in state.Playlists.Where(item => item.Id > 0))
        {
            var playlistData = new PlaylistData(
                savedPlaylist.Id,
                string.IsNullOrWhiteSpace(savedPlaylist.Name) ? "\u672A\u547D\u540D\u6B4C\u5355" : savedPlaylist.Name,
                string.IsNullOrWhiteSpace(savedPlaylist.CoverPath) ? "Assets/playlist-default-cover.png" : savedPlaylist.CoverPath);
            playlistData.PlayCount = Math.Max(0, savedPlaylist.PlayCount);

            foreach (var track in savedPlaylist.Tracks.Where(item => !string.IsNullOrWhiteSpace(item.FilePath)))
            {
                playlistData.Tracks.Add(new PlaylistTrack(
                    track.FilePath,
                    string.IsNullOrWhiteSpace(track.Title) ? Path.GetFileNameWithoutExtension(track.FilePath) : track.Title,
                    string.IsNullOrWhiteSpace(track.DurationText) ? "--:--" : track.DurationText,
                    string.IsNullOrWhiteSpace(track.Artist) ? "\u672A\u77E5\u6B4C\u624B" : track.Artist,
                    string.IsNullOrWhiteSpace(track.Album) ? "\u672A\u77E5\u5531\u7247" : track.Album,
                    Math.Max(0, track.PlayCount)));
            }

            var playlist = CreatePlaylistEntry(playlistData);
            playlists.Add(playlist);
            PlaylistItemsPanel.Children.Add(playlist.Row);
        }

        nextPlaylistId = Math.Max(state.NextPlaylistId, playlists.Select(item => item.Data.Id).DefaultIfEmpty(0).Max() + 1);
        UpdateSidebarLayout();
        UpdatePlayModeButton();
        UpdateMuteButton();

        var selectedPlaylist = state.SelectedPlaylistId is null
            ? null
            : playlists.FirstOrDefault(item => item.Data.Id == state.SelectedPlaylistId.Value);

        if (selectedPlaylist is not null)
        {
            SelectPlaylist(selectedPlaylist);
        }
        else
        {
            SelectView("Home");
        }

        RestorePlayback(state.Playback);
        isRestoringState = false;
        hasPendingSave = false;
    }

    private void RestoreWindowState(WindowStateData windowState)
    {
        if (windowState.Width >= MinWidth)
        {
            Width = windowState.Width;
        }

        if (windowState.Height >= MinHeight)
        {
            Height = windowState.Height;
        }

        var workArea = SystemParameters.WorkArea;
        if (!double.IsNaN(windowState.Left) && !double.IsNaN(windowState.Top))
        {
            Left = Math.Clamp(windowState.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
            Top = Math.Clamp(windowState.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
        }

        if (windowState.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void RestorePlayback(PlaybackStateData playback)
    {
        var restored = false;

        if (playback.PlayingPlaylistId is not null)
        {
            var playlist = playlists.FirstOrDefault(item => item.Data.Id == playback.PlayingPlaylistId.Value);
            if (playlist is not null
                && playback.CurrentTrackIndex >= 0
                && playback.CurrentTrackIndex < playlist.Data.Tracks.Count)
            {
                var track = playlist.Data.Tracks[playback.CurrentTrackIndex];
                if (File.Exists(track.FilePath))
                {
                    playingPlaylistId = playlist.Data.Id;
                    currentTrackIndex = playback.CurrentTrackIndex;
                    LoadAudioFile(track.FilePath);
                    SeekToSavedPosition(playback.PositionSeconds);
                    SetPlayerTrackInfo(track.Title, track.Artist, track.Album);
                    UpdateTransportButtonState();
                    SyncPlayingTrackSelection(playlist, currentTrackIndex);
                    restored = true;
                }
            }
        }

        if (!restored && !string.IsNullOrWhiteSpace(playback.CurrentSongPath) && File.Exists(playback.CurrentSongPath))
        {
            playingPlaylistId = null;
            currentTrackIndex = -1;
            LoadAudioFile(playback.CurrentSongPath);
            SeekToSavedPosition(playback.PositionSeconds);
            UpdateTransportButtonState();
            restored = true;
        }

        if (!restored)
        {
            playingPlaylistId = null;
            currentTrackIndex = -1;
            UpdateTransportButtonState();
        }

        SetPlayPauseIcon(false);
    }

    private void SeekToSavedPosition(double positionSeconds)
    {
        if (audioFile is null || positionSeconds <= 0)
        {
            return;
        }

        var seconds = Math.Clamp(positionSeconds, 0, Math.Max(audioFile.TotalTime.TotalSeconds - 1, 0));
        audioFile.CurrentTime = TimeSpan.FromSeconds(seconds);
        ProgressSlider.Value = seconds;
        UpdateTimeText();
    }

    private void SaveTimer_Tick(object? sender, EventArgs e)
    {
        if (!hasPendingSave || isRestoringState)
        {
            return;
        }

        SaveState();
    }

    private void QueueSaveState()
    {
        if (isRestoringState)
        {
            return;
        }

        hasPendingSave = true;
    }

    private void SaveState()
    {
        if (isRestoringState)
        {
            return;
        }

        using var _ = PerfLog.Measure("SaveState total");
        AppState snapshot;
        using (PerfLog.Measure("SaveState create snapshot"))
        {
            snapshot = CreateStateSnapshot();
        }

        using (PerfLog.Measure($"SaveState write json playlists={snapshot.Playlists.Count} tracks={snapshot.Playlists.Sum(item => item.Tracks.Count)}"))
        {
            AppStorage.Save(snapshot);
        }

        hasPendingSave = false;
    }

    private void SaveStateIfNeeded()
    {
        PerfLog.Mark($"SaveStateIfNeeded hasPendingSave={hasPendingSave}");
        if (hasPendingSave)
        {
            SaveState();
        }
    }

    private AppState CreateStateSnapshot()
    {
        return new AppState
        {
            NextPlaylistId = nextPlaylistId,
            SelectedPlaylistId = selectedPlaylistId,
            Settings = new AppSettingsData
            {
                IsSidebarCollapsed = isSidebarCollapsed,
                Volume = VolumeSlider.Value,
                IsMuted = isMuted,
                VolumeBeforeMute = volumeBeforeMute,
                PlayMode = playMode.ToString(),
                ThemePreset = themePreset.ToString()
            },
            Window = new WindowStateData
            {
                Left = RestoreBounds.Left,
                Top = RestoreBounds.Top,
                Width = RestoreBounds.Width,
                Height = RestoreBounds.Height,
                IsMaximized = WindowState == WindowState.Maximized
            },
            Playback = new PlaybackStateData
            {
                PlayingPlaylistId = playingPlaylistId,
                CurrentTrackIndex = currentTrackIndex,
                CurrentSongPath = currentSongPath,
                PositionSeconds = audioFile?.CurrentTime.TotalSeconds ?? 0
            },
            Playlists = playlists.Select(playlist => new PlaylistStateData
            {
                Id = playlist.Data.Id,
                Name = playlist.Data.Name,
                CoverPath = playlist.Data.CoverPath,
                PlayCount = playlist.Data.PlayCount,
                Tracks = playlist.Data.Tracks.Select(track => new PlaylistTrackStateData
                {
                    FilePath = track.FilePath,
                    Title = track.Title,
                    DurationText = track.DurationText,
                    Artist = track.Artist,
                    Album = track.Album,
                    PlayCount = track.PlayCount
                }).ToList()
            }).ToList()
        };
    }

    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "\u9009\u62E9\u97F3\u9891\u6587\u4EF6",
            Filter = "\u97F3\u9891\u6587\u4EF6|*.mp3;*.wav;*.flac;*.aac;*.wma;*.m4a|\u6240\u6709\u6587\u4EF6|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        playingPlaylistId = null;
        currentTrackIndex = -1;
        LoadAudioFile(dialog.FileName);
        Play();
        QueueSaveState();
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayback();
    }

    private void TogglePlayback()
    {
        if (outputDevice is null)
        {
            return;
        }

        if (outputDevice.PlaybackState == PlaybackState.Playing)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (audioFile is null || outputDevice is null)
        {
            return;
        }

        isManualStop = true;
        outputDevice.Stop();
        isManualStop = false;
        audioFile.Position = 0;
        ProgressSlider.Value = 0;
        UpdateTimeText();
        SetPlayPauseIcon(false);
        QueueSaveState();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        QueueSaveState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void InitializeNotifyIcon()
    {
        notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "\u97F3\u4E50\u64AD\u653E\u5668",
            Visible = true
        };

        notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                var cursorPosition = Forms.Cursor.Position;
                Dispatcher.Invoke(() => ShowTrayMenu(cursorPosition.X, cursorPosition.Y));
            }
        };
    }

    private void ShowTrayMenu(int screenX, int screenY)
    {
        trayMenuWindow?.Close();

        trayMenuWindow = new TrayMenuWindow();
        trayMenuWindow.SetPlaybackState(outputDevice?.PlaybackState == PlaybackState.Playing);
        trayMenuWindow.PreviousRequested += (_, _) => PlayPreviousPlaylistTrack();
        trayMenuWindow.PlayPauseRequested += (_, _) => TogglePlayback();
        trayMenuWindow.NextRequested += (_, _) => PlayNextPlaylistTrack();
        trayMenuWindow.ShowWindowRequested += (_, _) => ShowFromTray();
        trayMenuWindow.ExitRequested += (_, _) => ExitApplication();
        trayMenuWindow.Closed += (_, _) => trayMenuWindow = null;

        var dpi = VisualTreeHelper.GetDpi(this);
        var x = screenX / dpi.DpiScaleX;
        var y = screenY / dpi.DpiScaleY;
        var workArea = SystemParameters.WorkArea;
        const double menuWidth = 190;
        const double menuHeight = 212;

        const double trayMenuGap = 2;
        const double trayMenuOffset = 28;
        var preferredLeft = x + trayMenuGap;
        if (preferredLeft + menuWidth > workArea.Right)
        {
            preferredLeft = x - menuWidth - trayMenuGap;
        }

        trayMenuWindow.Left = Math.Clamp(preferredLeft, workArea.Left, workArea.Right - menuWidth + trayMenuOffset);
        trayMenuWindow.Top = Math.Clamp(y - menuHeight + trayMenuOffset, workArea.Top, workArea.Bottom - menuHeight + trayMenuOffset);
        trayMenuWindow.Show();
        trayMenuWindow.Activate();
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "app.ico");
        return File.Exists(iconPath)
            ? new Drawing.Icon(iconPath)
            : Drawing.SystemIcons.Application;
    }

    private void SetApplicationWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "app.ico");
        if (!File.Exists(iconPath))
        {
            return;
        }

        Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
    }

    private void HideToTray()
    {
        using var _ = PerfLog.Measure("HideToTray total");
        SaveStateIfNeeded();
        Hide();
        notifyIcon?.ShowBalloonTip(
            1200,
            "\u97F3\u4E50\u64AD\u653E\u5668",
            "\u7A0B\u5E8F\u5DF2\u5728\u540E\u53F0\u8FD0\u884C\uFF0C\u53EF\u4EE5\u4ECE\u6258\u76D8\u56FE\u6807\u6062\u590D\u3002",
            Forms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    public void ShowExistingInstance()
    {
        ShowFromTray();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitApplication()
    {
        using var _ = PerfLog.Measure("ExitApplication total");
        PerfLog.Mark("ExitApplication requested from tray menu");

        try
        {
            using (PerfLog.Measure("ExitApplication stop timers"))
            {
                saveTimer.Stop();
                progressTimer.Stop();
            }

            SaveStateIfNeeded();

            using (PerfLog.Measure("ExitApplication close tray menu"))
            {
                isExitRequested = true;
                trayMenuWindow?.Close();
            }

            using (PerfLog.Measure("ExitApplication Close()"))
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            PerfLog.Exception("ExitApplication", ex);
            throw;
        }
    }

    private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        isSidebarCollapsed = !isSidebarCollapsed;
        UpdateSidebarLayout();
        QueueSaveState();
    }

    private void UpdateSidebarLayout()
    {
        SidebarColumn.Width = new GridLength(isSidebarCollapsed ? CollapsedSidebarWidth : ExpandedSidebarWidth);

        var labelVisibility = isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        var navWidth = isSidebarCollapsed ? 44 : double.NaN;
        var navMargin = isSidebarCollapsed ? new Thickness(10, 6, 10, 6) : new Thickness(14, 5, 14, 5);

        SidebarTitlePanel.Visibility = labelVisibility;
        PlaylistGroupHeader.Visibility = labelVisibility;
        CollapsedPlaylistDivider.Visibility = isSidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;
        AddPlaylistButton.Visibility = labelVisibility;
        HomeNavLabel.Visibility = labelVisibility;
        AddPlaylistNavLabel.Visibility = labelVisibility;
        SettingsNavLabel.Visibility = labelVisibility;

        SidebarToggleButton.Content = isSidebarCollapsed ? "\u2630" : "\u2039";
        SidebarToggleButton.ToolTip = isSidebarCollapsed ? "\u5C55\u5F00\u4FA7\u680F" : "\u6536\u8D77\u4FA7\u680F";

        HomeNavButton.Padding = isSidebarCollapsed ? new Thickness(0) : new Thickness(16, 0, 16, 0);
        HomeNavButton.Width = navWidth;
        HomeNavButton.Margin = navMargin;
        HomeNavButton.HorizontalContentAlignment = isSidebarCollapsed ? WpfHorizontalAlignment.Center : WpfHorizontalAlignment.Stretch;

        SettingsNavButton.Padding = isSidebarCollapsed ? new Thickness(0) : new Thickness(16, 0, 16, 0);
        SettingsNavButton.Width = navWidth;
        SettingsNavButton.Margin = navMargin;
        SettingsNavButton.HorizontalContentAlignment = isSidebarCollapsed ? WpfHorizontalAlignment.Center : WpfHorizontalAlignment.Stretch;

        foreach (var playlist in playlists)
        {
            playlist.Row.Width = navWidth;
            playlist.Row.Margin = navMargin;
            playlist.Row.Padding = isSidebarCollapsed ? new Thickness(0) : new Thickness(16, 0, 16, 0);
            playlist.IconColumn.Width = new GridLength(isSidebarCollapsed ? 44 : 34);
            playlist.TextColumn.Width = new GridLength(isSidebarCollapsed ? 0 : 1, isSidebarCollapsed ? GridUnitType.Pixel : GridUnitType.Star);
            playlist.MenuColumn.Width = new GridLength(isSidebarCollapsed ? 0 : 28);
            playlist.NameTextBox.Visibility = labelVisibility;
            playlist.MenuButton.Visibility = labelVisibility;
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string viewName })
        {
            SelectView(viewName);
        }
    }

    private void SelectView(string viewName)
    {
        if (viewName != "Playlists")
        {
            selectedPlaylistId = null;
            UpdatePlaylistSelectionStyles();
        }

        MainContentHost.Content = viewName switch
        {
            "Home" => GetHomeView(),
            "NowPlaying" => GetNowPlayingView(),
            "Library" => GetLibraryView(),
            "Settings" => GetSettingsView(),
            _ => GetPlaylistContentView()
        };

        if (MainContentHost.Content is DependencyObject content)
        {
            using (PerfLog.Measure($"SelectView apply theme roles view={viewName} content={content.GetType().Name}"))
            {
                AppTheme.ApplyRegisteredElements(content);
            }
        }

        HomeNavButton.Background = viewName == "Home" ? FindResource("PanelHoverBrush") as WpfBrush : WpfBrushes.Transparent;
        SettingsNavButton.Background = viewName == "Settings" ? FindResource("PanelHoverBrush") as WpfBrush : WpfBrushes.Transparent;
        QueueSaveState();
    }

    private HomeView GetHomeView()
    {
        homeView ??= new HomeView();
        UpdateHomeStats();
        return homeView;
    }

    private void UpdateHomeStats()
    {
        if (homeView is null)
        {
            return;
        }

        var favoritePlaylist = playlists
            .Where(item => item.Data.PlayCount > 0)
            .OrderByDescending(item => item.Data.PlayCount)
            .ThenBy(item => item.Data.Name)
            .FirstOrDefault();

        homeView.SetFavoritePlaylist(favoritePlaylist?.Data.Name, favoritePlaylist?.Data.PlayCount ?? 0);

        var favoriteTrack = playlists
            .SelectMany(playlist => playlist.Data.Tracks)
            .Where(track => track.PlayCount > 0)
            .GroupBy(track => track.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Track = group.First(),
                PlayCount = group.Sum(track => track.PlayCount)
            })
            .OrderByDescending(item => item.PlayCount)
            .ThenBy(item => item.Track.Title)
            .FirstOrDefault();

        homeView.SetFavoriteTrack(
            favoriteTrack?.Track.Title,
            favoriteTrack?.Track.Artist,
            favoriteTrack?.Track.Album,
            favoriteTrack?.PlayCount ?? 0);
    }

    private NowPlayingView GetNowPlayingView()
    {
        if (nowPlayingView is not null)
        {
            return nowPlayingView;
        }

        nowPlayingView = new NowPlayingView();
        nowPlayingView.OpenFileRequested += OpenFileButton_Click;
        nowPlayingView.SetSongInfo(currentSongName, currentSongPath);
        return nowPlayingView;
    }

    private PlaylistContentView GetPlaylistContentView()
    {
        if (playlistContentView is not null)
        {
            return playlistContentView;
        }

        playlistContentView = new PlaylistContentView();
        playlistContentView.AddPlaylistRequested += AddPlaylistButton_Click;
        playlistContentView.PlayMusicRequested += PlayMusicButton_Click;
        playlistContentView.AddMusicRequested += AddMusicButton_Click;
        playlistContentView.ClearPlaylistRequested += ClearPlaylistButton_Click;
        playlistContentView.CoverChangeRequested += CoverChangeButton_Click;
        playlistContentView.TrackPlayRequested += PlaylistTrackPlayRequested;
        playlistContentView.TrackOpenLocationRequested += PlaylistTrackOpenLocationRequested;
        playlistContentView.TrackRemoveRequested += PlaylistTrackRemoveRequested;
        playlistContentView.TrackMoveRequested += PlaylistTrackMoveRequested;
        return playlistContentView;
    }

    private LibraryView GetLibraryView()
    {
        return libraryView ??= new LibraryView();
    }

    private SettingsView GetSettingsView()
    {
        if (settingsView is not null)
        {
            return settingsView;
        }

        settingsView = new SettingsView();
        settingsView.ThemeChanged += SettingsThemeChanged;
        settingsView.SetTheme(themePreset);
        return settingsView;
    }

    private void SettingsThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        using var measure = PerfLog.Measure($"SettingsThemeChanged preset={e.Preset}");

        try
        {
            themePreset = e.Preset;

            PerfLog.Mark("SettingsThemeChanged apply theme");
            ApplyTheme();
            PerfLog.Mark("SettingsThemeChanged sync settings view");
            settingsView?.SetTheme(themePreset);
            PerfLog.Mark("SettingsThemeChanged queue save state");
            QueueSaveState();
        }
        catch (Exception ex)
        {
            PerfLog.Exception("SettingsThemeChanged", ex);
            throw;
        }
    }

    private void ApplyTheme()
    {
        using var measure = PerfLog.Measure($"MainWindow.ApplyTheme preset={themePreset} resolved={AppTheme.ResolvePreset(themePreset)}");

        try
        {
            PerfLog.Mark("MainWindow.ApplyTheme AppTheme.Apply");
            AppTheme.Apply(themePreset, this);

            PerfLog.Mark("MainWindow.ApplyTheme set window resource refs");
            SetResourceReference(BackgroundProperty, "WindowBrush");
            SetResourceReference(ForegroundProperty, "TextPrimaryBrush");

            PerfLog.Mark("MainWindow.ApplyTheme update nav backgrounds");
            HomeNavButton.Background = selectedPlaylistId is null && MainContentHost.Content == homeView
                ? FindResource("PanelHoverBrush") as WpfBrush
                : WpfBrushes.Transparent;
            SettingsNavButton.Background = MainContentHost.Content == settingsView
                ? FindResource("PanelHoverBrush") as WpfBrush
                : WpfBrushes.Transparent;

            PerfLog.Mark("MainWindow.ApplyTheme update playlist selection styles");
            UpdatePlaylistSelectionStyles();
            RefreshPlaylistRuntimeTheme();

            PerfLog.Mark("MainWindow.ApplyTheme refresh themed icons");
            RefreshThemedIcons();
        }
        catch (Exception ex)
        {
            PerfLog.Exception("MainWindow.ApplyTheme", ex);
            throw;
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (themePreset != ThemePreset.System)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            using var measure = PerfLog.Measure($"System theme changed category={e.Category}");
            ApplyTheme();
            settingsView?.SetTheme(themePreset);
        });
    }

    private void AddPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        var playlist = CreatePlaylistEntry(new PlaylistData(nextPlaylistId++, $"\u65B0\u5EFA\u6B4C\u5355 {playlists.Count + 1}", "Assets/playlist-default-cover.png"));
        playlists.Add(playlist);
        PlaylistItemsPanel.Children.Add(playlist.Row);

        SelectPlaylist(playlist);
        UpdateSidebarLayout();
        BeginRenamePlaylist(playlist);
        QueueSaveState();
    }

    private void RefreshPlaylistRuntimeTheme()
    {
        var mutedBrush = (WpfBrush)FindResource("MutedBrush");
        var textBrush = (WpfBrush)FindResource("TextPrimaryBrush");

        foreach (var playlist in playlists)
        {
            playlist.MenuButton.Foreground = mutedBrush;
            playlist.NameTextBox.Foreground = textBrush;
            playlist.NameTextBox.CaretBrush = textBrush;
        }
    }

    private PlaylistEntry CreatePlaylistEntry(PlaylistData data)
    {
        var row = new Border
        {
            Height = 44,
            Margin = isSidebarCollapsed ? new Thickness(10, 6, 10, 6) : new Thickness(14, 5, 14, 5),
            Padding = isSidebarCollapsed ? new Thickness(0) : new Thickness(16, 0, 16, 0),
            Background = WpfBrushes.Transparent,
            CornerRadius = new CornerRadius(7),
            Cursor = WpfCursors.Hand,
            Tag = data.Id,
            AllowDrop = true
        };

        var grid = new Grid();
        var iconColumn = new ColumnDefinition { Width = new GridLength(isSidebarCollapsed ? 44 : 34) };
        var textColumn = new ColumnDefinition { Width = isSidebarCollapsed ? new GridLength(0) : new GridLength(1, GridUnitType.Star) };
        var menuColumn = new ColumnDefinition { Width = isSidebarCollapsed ? new GridLength(0) : new GridLength(28) };
        grid.ColumnDefinitions.Add(iconColumn);
        grid.ColumnDefinitions.Add(textColumn);
        grid.ColumnDefinitions.Add(menuColumn);

        var icon = new TextBlock
        {
            Text = "\u266B",
            Width = 34,
            FontSize = 20,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        var nameTextBox = new WpfTextBox
        {
            Text = data.Name,
            IsReadOnly = true,
            Visibility = isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible,
            Margin = new Thickness(10, 0, 0, 0),
            Style = (Style)FindResource("PlaylistNameTextBoxStyle")
        };

        var menuButton = new WpfButton
        {
            Content = "\u22EF",
            Width = 26,
            Height = 28,
            MinWidth = 0,
            Padding = new Thickness(0),
            Background = WpfBrushes.Transparent,
            BorderBrush = WpfBrushes.Transparent,
            Foreground = (WpfBrush)FindResource("MutedBrush"),
            Visibility = isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible,
            FocusVisualStyle = null,
            Cursor = WpfCursors.Hand
        };

        var entry = new PlaylistEntry(data, row, iconColumn, textColumn, menuColumn, nameTextBox, menuButton);

        var contextMenu = AppUi.CreateContextMenu(menuButton);
        var renameItem = AppUi.CreateMenuItem("\u91CD\u547D\u540D", (_, _) => BeginRenamePlaylist(entry));
        var deleteItem = AppUi.CreateMenuItem("\u5220\u9664", (_, _) => DeletePlaylist(entry));
        contextMenu.Items.Add(renameItem);
        contextMenu.Items.Add(deleteItem);
        menuButton.ContextMenu = contextMenu;
        menuButton.Click += (sender, args) =>
        {
            args.Handled = true;
            menuButton.ContextMenu.IsOpen = true;
        };

        nameTextBox.PreviewMouseLeftButtonDown += (sender, args) =>
        {
            if (nameTextBox.IsReadOnly)
            {
                args.Handled = true;
                SelectPlaylist(entry);
            }
        };
        nameTextBox.LostKeyboardFocus += (_, _) => CommitPlaylistRename(entry);
        nameTextBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                CommitPlaylistRename(entry);
                Keyboard.ClearFocus();
                args.Handled = true;
            }
        };

        row.MouseEnter += (_, _) =>
        {
            if (selectedPlaylistId != entry.Data.Id)
            {
                row.Background = (WpfBrush)FindResource("PanelHoverBrush");
            }
        };
        row.MouseLeave += (_, _) =>
        {
            if (selectedPlaylistId != entry.Data.Id)
            {
                row.Background = WpfBrushes.Transparent;
            }
        };
        row.PreviewMouseLeftButtonDown += (sender, args) => PlaylistRow_PreviewMouseLeftButtonDown(entry, args);
        row.MouseLeftButtonUp += (_, args) =>
        {
            if (suppressNextPlaylistClick)
            {
                suppressNextPlaylistClick = false;
                args.Handled = true;
                return;
            }

            SelectPlaylist(entry);
        };

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(nameTextBox, 1);
        Grid.SetColumn(menuButton, 2);
        grid.Children.Add(icon);
        grid.Children.Add(nameTextBox);
        grid.Children.Add(menuButton);
        row.Child = grid;

        return entry;
    }

    private void PlaylistRow_PreviewMouseLeftButtonDown(PlaylistEntry playlist, MouseButtonEventArgs e)
    {
        if (IsDescendantOf(e.OriginalSource as DependencyObject, playlist.MenuButton)
            || !playlist.NameTextBox.IsReadOnly)
        {
            ResetPlaylistDragState();
            return;
        }

        playlistDragStartPoint = e.GetPosition(PlaylistItemsPanel);
        playlistDragOffsetFromRow = e.GetPosition(playlist.Row);
        playlistDragStartIndex = playlists.IndexOf(playlist);
        draggingPlaylist = playlist;
        draggingPlaylistTarget = playlist;
        isDraggingPlaylist = false;
    }

    private void PlaylistItemsPanel_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || draggingPlaylist is null
            || playlistDragStartIndex < 0)
        {
            return;
        }

        var currentPoint = e.GetPosition(PlaylistItemsPanel);
        lastPlaylistDragPoint = currentPoint;
        if (!isDraggingPlaylist)
        {
            if (Math.Abs(currentPoint.X - playlistDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(currentPoint.Y - playlistDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            BeginPlaylistDrag();
        }

        playlistDragAdorner?.UpdatePosition(
            currentPoint.X - playlistDragOffsetFromRow.X,
            currentPoint.Y - playlistDragOffsetFromRow.Y);
        AutoScrollPlaylistList(currentPoint);
        MoveDraggedPlaylistToPointer(currentPoint);
        e.Handled = true;
    }

    private void PlaylistItemsPanel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isDraggingPlaylist)
        {
            ResetPlaylistDragState();
            return;
        }

        CompletePlaylistDrag();
        e.Handled = true;
    }

    private void BeginPlaylistDrag()
    {
        if (draggingPlaylist is null)
        {
            return;
        }

        isDraggingPlaylist = true;
        suppressNextPlaylistClick = true;
        playlistDragAdornerLayer = AdornerLayer.GetAdornerLayer(PlaylistItemsPanel);
        if (playlistDragAdornerLayer is not null)
        {
            playlistDragAdorner = new DragPreviewAdorner(
                PlaylistItemsPanel,
                draggingPlaylist.Row,
                draggingPlaylist.Row.ActualWidth,
                draggingPlaylist.Row.ActualHeight)
            {
                IsHitTestVisible = false
            };
            playlistDragAdornerLayer.Add(playlistDragAdorner);
        }

        draggingPlaylist.Row.Opacity = 0.35;
        PlaylistItemsPanel.CaptureMouse();
        playlistAutoScrollTimer.Start();
    }

    private void MoveDraggedPlaylistToPointer(WpfPoint point)
    {
        if (draggingPlaylist is null)
        {
            return;
        }

        var target = GetPlaylistFromPoint(point);
        if (target is null)
        {
            HidePlaylistInsertionLine();
            return;
        }

        if (target.Data.Id == draggingPlaylist.Data.Id)
        {
            UpdatePlaylistInsertionLine(target.Row, placeAfter: true);
            return;
        }

        var sourceIndex = playlists.IndexOf(draggingPlaylist);
        var targetIndex = playlists.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return;
        }

        UpdatePlaylistInsertionLine(target.Row, targetIndex > sourceIndex);
        MovePlaylist(draggingPlaylist.Data.Id, target.Data.Id, saveState: false);
        draggingPlaylistTarget = target;
    }

    private void CompletePlaylistDrag()
    {
        ClearPlaylistDragVisuals();
        if (playlistDragStartIndex >= 0 && draggingPlaylist is not null && playlistDragStartIndex != playlists.IndexOf(draggingPlaylist))
        {
            QueueSaveState();
        }

        ResetPlaylistDragState();
    }

    private void ClearPlaylistDragVisuals()
    {
        playlistAutoScrollTimer.Stop();
        PlaylistItemsPanel.ReleaseMouseCapture();
        if (draggingPlaylist is not null)
        {
            draggingPlaylist.Row.Opacity = 1;
        }

        HidePlaylistInsertionLine();

        if (playlistDragAdornerLayer is not null && playlistDragAdorner is not null)
        {
            playlistDragAdornerLayer.Remove(playlistDragAdorner);
        }

        playlistDragAdorner = null;
        playlistDragAdornerLayer = null;
    }

    private void ResetPlaylistDragState()
    {
        if (draggingPlaylist is not null)
        {
            draggingPlaylist.Row.Opacity = 1;
        }

        draggingPlaylist = null;
        draggingPlaylistTarget = null;
        playlistDragStartIndex = -1;
        isDraggingPlaylist = false;
    }

    private void AutoScrollPlaylistList(WpfPoint point)
    {
        if (!isDraggingPlaylist)
        {
            return;
        }

        var scrollViewer = FindVisualParent<ScrollViewer>(PlaylistItemsPanel);
        if (scrollViewer is null)
        {
            return;
        }

        var pointInScrollViewer = PlaylistItemsPanel.TransformToAncestor(scrollViewer).Transform(point);
        const double edgeSize = 36;
        const double scrollStep = 14;
        if (pointInScrollViewer.Y < edgeSize)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollStep);
        }
        else if (pointInScrollViewer.Y > scrollViewer.ActualHeight - edgeSize)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollStep);
        }
    }

    private void UpdatePlaylistInsertionLine(Border row, bool placeAfter)
    {
        playlistDragAdornerLayer ??= AdornerLayer.GetAdornerLayer(PlaylistItemsPanel);
        if (playlistDragAdornerLayer is null)
        {
            return;
        }

        playlistInsertionAdorner ??= new InsertionLineAdorner(PlaylistItemsPanel)
        {
            IsHitTestVisible = false
        };

        var adorners = playlistDragAdornerLayer.GetAdorners(PlaylistItemsPanel);
        if (adorners is null || !adorners.Contains(playlistInsertionAdorner))
        {
            playlistDragAdornerLayer.Add(playlistInsertionAdorner);
        }

        var position = row.TransformToAncestor(PlaylistItemsPanel).Transform(new WpfPoint(0, 0));
        var y = placeAfter ? position.Y + row.ActualHeight - 3 : position.Y + 3;
        playlistInsertionAdorner.UpdateLine(12, y, Math.Max(0, PlaylistItemsPanel.ActualWidth - 24));
    }

    private void HidePlaylistInsertionLine()
    {
        if (playlistDragAdornerLayer is not null && playlistInsertionAdorner is not null)
        {
            playlistDragAdornerLayer.Remove(playlistInsertionAdorner);
        }

        playlistInsertionAdorner = null;
    }

    private PlaylistEntry? GetPlaylistFromPoint(WpfPoint point)
    {
        var result = VisualTreeHelper.HitTest(PlaylistItemsPanel, point);
        var source = result?.VisualHit;
        while (source is not null)
        {
            if (source is Border { Tag: int playlistId })
            {
                return playlists.FirstOrDefault(item => item.Data.Id == playlistId);
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void MovePlaylist(int sourcePlaylistId, int targetPlaylistId, bool saveState = true)
    {
        var sourceIndex = playlists.FindIndex(item => item.Data.Id == sourcePlaylistId);
        var targetIndex = playlists.FindIndex(item => item.Data.Id == targetPlaylistId);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        var playlist = playlists[sourceIndex];
        playlists.RemoveAt(sourceIndex);
        playlists.Insert(targetIndex, playlist);

        PlaylistItemsPanel.Children.Clear();
        foreach (var item in playlists)
        {
            PlaylistItemsPanel.Children.Add(item.Row);
        }

        if (saveState)
        {
            QueueSaveState();
        }
    }

    private void SelectPlaylist(PlaylistEntry playlist)
    {
        selectedPlaylistId = playlist.Data.Id;
        SelectView("Playlists");
        SyncPlaylistName(playlist);
        ShowPlaylistContent(playlist);

        UpdatePlaylistSelectionStyles();
    }

    private void UpdatePlaylistSelectionStyles()
    {
        foreach (var item in playlists)
        {
            item.Row.Background = selectedPlaylistId == item.Data.Id
                ? (WpfBrush)FindResource("PanelHoverBrush")
                : WpfBrushes.Transparent;
        }
    }

    private void BeginRenamePlaylist(PlaylistEntry playlist)
    {
        if (isSidebarCollapsed)
        {
            isSidebarCollapsed = false;
            UpdateSidebarLayout();
        }

        SelectPlaylist(playlist);
        editingPlaylist = playlist;
        playlist.NameTextBox.IsReadOnly = false;
        playlist.NameTextBox.Background = (WpfBrush)FindResource("PanelHoverBrush");
        playlist.NameTextBox.Focus();
        playlist.NameTextBox.SelectAll();
    }

    private void CommitPlaylistRename(PlaylistEntry playlist)
    {
        if (playlist.NameTextBox.IsReadOnly)
        {
            return;
        }

        playlist.NameTextBox.Text = string.IsNullOrWhiteSpace(playlist.NameTextBox.Text)
            ? "\u672A\u547D\u540D\u6B4C\u5355"
            : playlist.NameTextBox.Text.Trim();
        SyncPlaylistName(playlist);
        playlist.NameTextBox.IsReadOnly = true;
        playlist.NameTextBox.Background = WpfBrushes.Transparent;

        if (editingPlaylist?.Data.Id == playlist.Data.Id)
        {
            editingPlaylist = null;
        }

        if (selectedPlaylistId == playlist.Data.Id)
        {
            ShowPlaylistContent(playlist);
        }

        QueueSaveState();
    }

    private static bool IsDescendantOf(DependencyObject? source, DependencyObject parent)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, parent))
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static T? FindVisualParent<T>(DependencyObject source)
        where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(source);
        while (parent is not null)
        {
            if (parent is T typedParent)
            {
                return typedParent;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private void DeletePlaylist(PlaylistEntry playlist)
    {
        var confirmed = AppUi.Confirm(
            this,
            "\u5220\u9664\u6B4C\u5355",
            "\u786E\u5B9A\u5220\u9664\u8FD9\u4E2A\u6B4C\u5355\u5417\uFF1F\u8BE5\u64CD\u4F5C\u6682\u65F6\u65E0\u6CD5\u64A4\u9500\u3002",
            "\u5220\u9664",
            "\u53D6\u6D88");
        if (!confirmed)
        {
            return;
        }

        playlists.Remove(playlist);
        PlaylistItemsPanel.Children.Remove(playlist.Row);

        if (playingPlaylistId == playlist.Data.Id)
        {
            playingPlaylistId = null;
            currentTrackIndex = -1;
            UpdateTransportButtonState();
        }

        if (selectedPlaylistId == playlist.Data.Id)
        {
            selectedPlaylistId = null;

            if (playlists.Count > 0)
            {
                SelectPlaylist(playlists[0]);
            }
            else
            {
                SelectView("Home");
            }
        }

        UpdateHomeStats();
        QueueSaveState();
    }

    private void AddMusicButton_Click(object sender, RoutedEventArgs e)
    {
        var playlist = GetSelectedPlaylist();
        if (playlist is null)
        {
            return;
        }

        var dialog = new WpfOpenFileDialog
        {
            Title = "\u9009\u62E9\u97F3\u4E50",
            Filter = "\u97F3\u9891\u6587\u4EF6|*.mp3;*.wav;*.flac;*.aac;*.wma;*.m4a|\u6240\u6709\u6587\u4EF6|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var fileName in dialog.FileNames)
        {
            var metadata = ReadTrackMetadata(fileName);
            playlist.Data.Tracks.Add(new PlaylistTrack(fileName, metadata.Title, metadata.DurationText, metadata.Artist, metadata.Album, 0));
        }

        ShowPlaylistContent(playlist);
        QueueSaveState();
    }

    private void CoverChangeButton_Click(object sender, RoutedEventArgs e)
    {
        var playlist = GetSelectedPlaylist();
        if (playlist is null)
        {
            return;
        }

        var dialog = new WpfOpenFileDialog
        {
            Title = "\u9009\u62E9\u6B4C\u5355\u5C01\u9762",
            Filter = "\u56FE\u7247\u6587\u4EF6|*.jpg;*.jpeg;*.png;*.bmp;*.webp|\u6240\u6709\u6587\u4EF6|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        playlist.Data.CoverPath = ImportPlaylistCover(playlist.Data.Id, dialog.FileName);
        ShowPlaylistContent(playlist);
        QueueSaveState();
    }

    private static string ImportPlaylistCover(int playlistId, string sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return sourcePath;
            }

            var coversDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MusicPlayer",
                "covers");
            Directory.CreateDirectory(coversDirectory);

            var extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".image";
            }

            var fileName = $"playlist-{playlistId}-{DateTime.Now:yyyyMMddHHmmssfff}{extension}";
            var destinationPath = Path.Combine(coversDirectory, fileName);
            File.Copy(sourcePath, destinationPath, overwrite: false);
            return destinationPath;
        }
        catch (Exception ex)
        {
            PerfLog.Exception("ImportPlaylistCover", ex);
            return sourcePath;
        }
    }

    private void ClearPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        var playlist = GetSelectedPlaylist();
        if (playlist is null)
        {
            return;
        }

        var confirmed = AppUi.Confirm(
            this,
            "\u6E05\u7A7A\u5217\u8868",
            "\u786E\u5B9A\u6E05\u7A7A\u5F53\u524D\u6B4C\u5355\u5417\uFF1F\u8BE5\u64CD\u4F5C\u6682\u65F6\u65E0\u6CD5\u64A4\u9500\u3002",
            "\u6E05\u7A7A",
            "\u53D6\u6D88");
        if (!confirmed)
        {
            return;
        }

        playlist.Data.Tracks.Clear();

        if (playingPlaylistId == playlist.Data.Id)
        {
            ResetPlaybackState();
        }

        ShowPlaylistContent(playlist);
        UpdateHomeStats();
        QueueSaveState();
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        PlayPreviousPlaylistTrack();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        PlayNextPlaylistTrack();
    }

    private void PlayModeButton_Click(object sender, RoutedEventArgs e)
    {
        playMode = playMode == PlaylistPlayMode.SequentialLoop
            ? PlaylistPlayMode.ShuffleLoop
            : PlaylistPlayMode.SequentialLoop;
        UpdatePlayModeButton();
        QueueSaveState();
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (isMuted)
        {
            isMuted = false;
            SetVolumeSliderValue(volumeBeforeMute <= 0 ? 0.7 : volumeBeforeMute);
        }
        else
        {
            isMuted = true;
            volumeBeforeMute = VolumeSlider.Value;
            SetVolumeSliderValue(0);
        }

        UpdateMuteButton();
        QueueSaveState();
    }

    private void PlaylistTrackPlayRequested(object? sender, int trackIndex)
    {
        var playlist = GetSelectedPlaylist();
        if (playlist is null || trackIndex < 0 || trackIndex >= playlist.Data.Tracks.Count)
        {
            return;
        }

        playingPlaylistId = playlist.Data.Id;
        currentTrackIndex = trackIndex;
        PlayTrack(playlist, currentTrackIndex);
        QueueSaveState();
    }

    private void PlaylistTrackOpenLocationRequested(object? sender, int trackIndex)
    {
        var playlist = GetSelectedPlaylist();
        if (playlist is null || trackIndex < 0 || trackIndex >= playlist.Data.Tracks.Count)
        {
            return;
        }

        var filePath = playlist.Data.Tracks[trackIndex].FilePath;
        if (!File.Exists(filePath))
        {
            WpfMessageBox.Show(this, "\u627E\u4E0D\u5230\u8FD9\u4E2A\u97F3\u4E50\u6587\u4EF6\u3002", "\u6587\u4EF6\u4E0D\u5B58\u5728", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe")
        {
            Arguments = $"/select,\"{filePath}\"",
            UseShellExecute = true
        });
    }

    private void PlaylistTrackRemoveRequested(object? sender, int trackIndex)
    {
        var playlist = GetSelectedPlaylist();
        if (playlist is null || trackIndex < 0 || trackIndex >= playlist.Data.Tracks.Count)
        {
            return;
        }

        var scrollOffset = GetPlaylistContentView().GetTrackScrollOffset();
        playlist.Data.Tracks.RemoveAt(trackIndex);

        if (playingPlaylistId == playlist.Data.Id)
        {
            if (trackIndex == currentTrackIndex)
            {
                ResetPlaybackState();
            }
            else if (trackIndex < currentTrackIndex)
            {
                currentTrackIndex--;
            }
        }

        ShowPlaylistContent(playlist);
        GetPlaylistContentView().RestoreTrackScrollOffset(scrollOffset);
        UpdateHomeStats();
        QueueSaveState();
    }

    private void PlaylistTrackMoveRequested(object? sender, TrackMoveRequestedEventArgs e)
    {
        var playlist = GetSelectedPlaylist();
        if (playlist is null
            || e.SourceIndex < 0
            || e.SourceIndex >= playlist.Data.Tracks.Count
            || e.TargetIndex < 0
            || e.TargetIndex >= playlist.Data.Tracks.Count
            || e.SourceIndex == e.TargetIndex)
        {
            return;
        }

        var scrollOffset = GetPlaylistContentView().GetTrackScrollOffset();
        var track = playlist.Data.Tracks[e.SourceIndex];
        playlist.Data.Tracks.RemoveAt(e.SourceIndex);
        playlist.Data.Tracks.Insert(e.TargetIndex, track);

        if (playingPlaylistId == playlist.Data.Id)
        {
            currentTrackIndex = GetMovedCurrentTrackIndex(currentTrackIndex, e.SourceIndex, e.TargetIndex);
        }

        ShowPlaylistContent(playlist);
        GetPlaylistContentView().RestoreTrackScrollOffset(scrollOffset);
        QueueSaveState();
    }

    private static int GetMovedCurrentTrackIndex(int currentIndex, int sourceIndex, int targetIndex)
    {
        if (currentIndex == sourceIndex)
        {
            return targetIndex;
        }

        if (sourceIndex < currentIndex && targetIndex >= currentIndex)
        {
            return currentIndex - 1;
        }

        if (sourceIndex > currentIndex && targetIndex <= currentIndex)
        {
            return currentIndex + 1;
        }

        return currentIndex;
    }

    private void PlayMusicButton_Click(object sender, RoutedEventArgs e)
    {
        var playlist = GetSelectedPlaylist();
        if (playlist is null)
        {
            return;
        }

        if (CanResumeSelectedPlaylist(playlist))
        {
            Play();
            return;
        }

        if (playlist.Data.Tracks.Count == 0)
        {
            return;
        }

        playingPlaylistId = playlist.Data.Id;
        currentTrackIndex = playMode == PlaylistPlayMode.ShuffleLoop
            ? random.Next(playlist.Data.Tracks.Count)
            : 0;
        PlayTrack(playlist, currentTrackIndex);
        QueueSaveState();
    }

    private bool CanResumeSelectedPlaylist(PlaylistEntry playlist)
    {
        return playingPlaylistId == playlist.Data.Id
            && currentTrackIndex >= 0
            && currentTrackIndex < playlist.Data.Tracks.Count
            && audioFile is not null
            && outputDevice is not null
            && !IsCurrentTrackEnded();
    }

    private void ResetPlaybackState()
    {
        DisposeAudio();
        playingPlaylistId = null;
        currentTrackIndex = -1;
        currentSongName = "\u8BF7\u9009\u62E9\u4E00\u4E2A\u97F3\u9891\u6587\u4EF6";
        currentSongPath = "";
        currentArtistName = "\u672A\u77E5\u6B4C\u624B";
        currentAlbumName = "\u672A\u77E5\u5531\u7247";
        SetPlayerTrackInfo(currentSongName, currentArtistName, currentAlbumName);
        nowPlayingView?.SetSongInfo(currentSongName, currentSongPath);
        ProgressSlider.Value = 0;
        ProgressSlider.Maximum = 1;
        CurrentTimeTextBlock.Text = "00:00";
        TotalTimeTextBlock.Text = "-00:00";
        PlayPauseButton.IsEnabled = false;
        SetPlayPauseIcon(false);
        UpdateTransportButtonState();
    }

    private void PlayNextPlaylistTrack()
    {
        if (playingPlaylistId is null)
        {
            return;
        }

        var playlist = playlists.FirstOrDefault(item => item.Data.Id == playingPlaylistId.Value);
        if (playlist is null || playlist.Data.Tracks.Count == 0)
        {
            return;
        }

        currentTrackIndex = playMode == PlaylistPlayMode.ShuffleLoop
            ? random.Next(playlist.Data.Tracks.Count)
            : (currentTrackIndex + 1) % playlist.Data.Tracks.Count;
        PlayTrack(playlist, currentTrackIndex);
        QueueSaveState();
    }

    private void PlayPreviousPlaylistTrack()
    {
        if (playingPlaylistId is null)
        {
            return;
        }

        var playlist = playlists.FirstOrDefault(item => item.Data.Id == playingPlaylistId.Value);
        if (playlist is null || playlist.Data.Tracks.Count == 0)
        {
            return;
        }

        currentTrackIndex = playMode == PlaylistPlayMode.ShuffleLoop
            ? random.Next(playlist.Data.Tracks.Count)
            : (currentTrackIndex - 1 + playlist.Data.Tracks.Count) % playlist.Data.Tracks.Count;
        PlayTrack(playlist, currentTrackIndex);
    }

    private void PlayTrack(PlaylistEntry playlist, int trackIndex)
    {
        if (trackIndex < 0 || trackIndex >= playlist.Data.Tracks.Count)
        {
            return;
        }

        var track = playlist.Data.Tracks[trackIndex];
        isChangingTrack = true;
        LoadAudioFile(track.FilePath);
        isChangingTrack = false;
        SetPlayerTrackInfo(track.Title, track.Artist, track.Album);
        playlist.Data.PlayCount++;
        track.PlayCount++;
        UpdateHomeStats();
        UpdateTransportButtonState();
        SyncPlayingTrackSelection(playlist, trackIndex);
        Play();
        QueueSaveState();
    }

    private PlaylistEntry? GetSelectedPlaylist()
    {
        return selectedPlaylistId is null
            ? null
            : playlists.FirstOrDefault(item => item.Data.Id == selectedPlaylistId.Value);
    }

    private void ShowPlaylistContent(PlaylistEntry playlist)
    {
        GetPlaylistContentView().ShowPlaylist(
            playlist.Data.Name,
            playlist.Data.CoverPath,
            playlist.Data.Tracks.Select(track => new PlaylistTrackViewModel(track.Title, track.DurationText, track.Artist, track.Album)).ToList());

        if (playingPlaylistId == playlist.Data.Id)
        {
            GetPlaylistContentView().SelectTrack(currentTrackIndex);
        }
    }

    private void SyncPlayingTrackSelection(PlaylistEntry playlist, int trackIndex)
    {
        if (selectedPlaylistId == playlist.Data.Id)
        {
            GetPlaylistContentView().SelectTrack(trackIndex);
        }
    }

    private static void SyncPlaylistName(PlaylistEntry playlist)
    {
        playlist.Data.Name = playlist.NameTextBox.Text;
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isUpdatingVolumeSlider)
        {
            return;
        }

        if (audioFile is not null)
        {
            audioFile.Volume = (float)e.NewValue;
        }

        if (e.NewValue > 0 && isMuted)
        {
            isMuted = false;
        }

        if (!isMuted && e.NewValue > 0)
        {
            volumeBeforeMute = e.NewValue;
        }

        UpdateMuteButton();
        QueueSaveState();
    }

    private void ProgressSlider_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        isDraggingProgress = true;
        ProgressSlider.CaptureMouse();
        SetSliderValueFromPoint(ProgressSlider, e.GetPosition(ProgressSlider));
        SeekToSliderValue();
        e.Handled = true;
    }

    private void ProgressSlider_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!isDraggingProgress || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        SetSliderValueFromPoint(ProgressSlider, e.GetPosition(ProgressSlider));
        SeekToSliderValue();
        e.Handled = true;
    }

    private void ProgressSlider_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetSliderValueFromPoint(ProgressSlider, e.GetPosition(ProgressSlider));
        SeekToSliderValue();
        isDraggingProgress = false;
        ProgressSlider.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isDraggingProgress)
        {
            CurrentTimeTextBlock.Text = FormatTime(TimeSpan.FromSeconds(e.NewValue));
            TotalTimeTextBlock.Text = FormatRemainingTime(TimeSpan.FromSeconds(ProgressSlider.Maximum - e.NewValue));
        }
    }

    private void ProgressTimer_Tick(object? sender, EventArgs e)
    {
        if (audioFile is null || isDraggingProgress)
        {
            return;
        }

        ProgressSlider.Value = audioFile.CurrentTime.TotalSeconds;
        UpdateTimeText();
        QueueSaveState();
    }

    private void LoadAudioFile(string filePath)
    {
        DisposeAudio();

        audioFile = new AudioFileReader(filePath)
        {
            Volume = (float)VolumeSlider.Value
        };

        outputDevice = new WaveOutEvent();
        outputDevice.Init(audioFile);
        outputDevice.PlaybackStopped += OutputDevice_PlaybackStopped;

        var metadata = ReadTrackMetadata(filePath);
        currentSongName = metadata.Title;
        currentArtistName = metadata.Artist;
        currentAlbumName = metadata.Album;
        currentSongPath = filePath;
        SetPlayerTrackInfo(currentSongName, currentArtistName, currentAlbumName);
        nowPlayingView?.SetSongInfo(currentSongName, currentSongPath);
        ProgressSlider.Maximum = Math.Max(audioFile.TotalTime.TotalSeconds, 1);
        ProgressSlider.Value = 0;
        TotalTimeTextBlock.Text = FormatRemainingTime(audioFile.TotalTime);
        CurrentTimeTextBlock.Text = "00:00";
        PlayPauseButton.IsEnabled = true;
        SetPlayPauseIcon(false);
        UpdateTransportButtonState();
    }

    private void Play()
    {
        if (outputDevice is null)
        {
            return;
        }

        outputDevice.Play();
        progressTimer.Start();
        SetPlayPauseIcon(true);
        QueueSaveState();
    }

    private void Pause()
    {
        outputDevice?.Pause();
        progressTimer.Stop();
        SetPlayPauseIcon(false);
        QueueSaveState();
    }

    private void SeekToSliderValue()
    {
        if (audioFile is null)
        {
            return;
        }

        audioFile.CurrentTime = TimeSpan.FromSeconds(ProgressSlider.Value);
        UpdateTimeText();
        QueueSaveState();
    }

    private void VolumeSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        VolumeSlider.CaptureMouse();
        SetSliderValueFromPoint(VolumeSlider, e.GetPosition(VolumeSlider));
        e.Handled = true;
    }

    private void VolumeSlider_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !VolumeSlider.IsMouseCaptured)
        {
            return;
        }

        SetSliderValueFromPoint(VolumeSlider, e.GetPosition(VolumeSlider));
        e.Handled = true;
    }

    private void VolumeSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        SetSliderValueFromPoint(VolumeSlider, e.GetPosition(VolumeSlider));
        VolumeSlider.ReleaseMouseCapture();
        e.Handled = true;
    }

    private static void SetSliderValueFromPoint(Slider slider, WpfPoint point)
    {
        if (slider.ActualWidth <= 0)
        {
            return;
        }

        var ratio = Math.Clamp(point.X / slider.ActualWidth, 0, 1);
        slider.Value = slider.Minimum + (slider.Maximum - slider.Minimum) * ratio;
    }

    private void OutputDevice_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            progressTimer.Stop();
            SetPlayPauseIcon(false);
            UpdateTimeText();

            if (e.Exception is not null)
            {
                WpfMessageBox.Show(this, e.Exception.Message, "\u64AD\u653E\u5931\u8D25", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!isManualStop && !isChangingTrack && IsCurrentTrackEnded())
            {
                PlayNextPlaylistTrack();
            }
        });
    }

    private bool IsCurrentTrackEnded()
    {
        return audioFile is not null
            && audioFile.TotalTime > TimeSpan.Zero
            && audioFile.CurrentTime >= audioFile.TotalTime - TimeSpan.FromMilliseconds(500);
    }

    private void UpdateTimeText()
    {
        if (audioFile is null)
        {
            CurrentTimeTextBlock.Text = "00:00";
            TotalTimeTextBlock.Text = "-00:00";
            return;
        }

        CurrentTimeTextBlock.Text = FormatTime(audioFile.CurrentTime);
        TotalTimeTextBlock.Text = FormatRemainingTime(audioFile.TotalTime - audioFile.CurrentTime);
    }

    private void SetPlayerTrackInfo(string title, string artist, string album)
    {
        PlayerSongTitleTextBlock.Text = string.IsNullOrWhiteSpace(title) ? "\u672A\u9009\u62E9\u6B4C\u66F2" : title;
        PlayerArtistTextBlock.Text = string.IsNullOrWhiteSpace(artist) ? "\u672A\u77E5\u6B4C\u624B" : artist;
        PlayerAlbumTextBlock.Text = string.IsNullOrWhiteSpace(album) ? "\u672A\u77E5\u5531\u7247" : album;
    }

    private void UpdateTransportButtonState()
    {
        var canNavigatePlaylist = playingPlaylistId is not null
            && playlists.Any(item => item.Data.Id == playingPlaylistId.Value && item.Data.Tracks.Count > 0);
        PreviousButton.IsEnabled = canNavigatePlaylist;
        NextButton.IsEnabled = canNavigatePlaylist;
    }

    private void UpdatePlayModeButton()
    {
        SetIcon(PlayModeIcon, playMode == PlaylistPlayMode.ShuffleLoop ? "shuffle.png" : "repeat.png");
        PlayModeButton.ToolTip = playMode == PlaylistPlayMode.ShuffleLoop
            ? "\u5F53\u524D\uFF1A\u968F\u673A\u5FAA\u73AF"
            : "\u5F53\u524D\uFF1A\u987A\u5E8F\u5FAA\u73AF";
    }

    private void UpdateMuteButton()
    {
        SetIcon(MuteIcon, isMuted || VolumeSlider.Value <= 0 ? "muted.png" : "volume.png");
    }

    private void SetPlayPauseIcon(bool isPlaying)
    {
        SetIcon(PlayPauseIcon, isPlaying ? "pause.png" : "play.png");
        PlayPauseIcon.Margin = isPlaying ? new Thickness(0) : new Thickness(2, 0, 0, 0);
    }

    private void RefreshThemedIcons()
    {
        SetIcon(PreviousIcon, "previous.png");
        SetIcon(NextIcon, "next.png");
        SetPlayPauseIcon(outputDevice?.PlaybackState == PlaybackState.Playing);
        UpdatePlayModeButton();
        UpdateMuteButton();
    }

    private void SetIcon(WpfImage image, string fileName)
    {
        try
        {
            image.Source = new BitmapImage(new Uri($"Assets/Icons/{GetThemedIconFileName(fileName)}", UriKind.Relative));
        }
        catch
        {
            image.Source = null;
        }
    }

    private string GetThemedIconFileName(string fileName)
    {
        var suffix = AppTheme.ResolvePreset(themePreset) == ThemePreset.Light ? "_light" : "_dark";
        var themedFileName = $"{Path.GetFileNameWithoutExtension(fileName)}{suffix}{Path.GetExtension(fileName)}";
        var themedPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", themedFileName);
        return File.Exists(themedPath) ? themedFileName : fileName;
    }

    private void SetVolumeSliderValue(double value)
    {
        isUpdatingVolumeSlider = true;
        VolumeSlider.Value = value;
        isUpdatingVolumeSlider = false;

        if (audioFile is not null)
        {
            audioFile.Volume = (float)value;
        }
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"mm\:ss");
    }

    private static string FormatRemainingTime(TimeSpan time)
    {
        return $"-{FormatTime(time < TimeSpan.Zero ? TimeSpan.Zero : time)}";
    }

    private static string GetAudioDurationText(string filePath)
    {
        try
        {
            using var reader = new AudioFileReader(filePath);
            return FormatTime(reader.TotalTime);
        }
        catch
        {
            return "--:--";
        }
    }

    private static TrackMetadata ReadTrackMetadata(string filePath)
    {
        var fallbackTitle = Path.GetFileNameWithoutExtension(filePath);
        var title = fallbackTitle;
        var artist = "\u672A\u77E5\u6B4C\u624B";
        var album = "\u672A\u77E5\u5531\u7247";
        var durationText = GetAudioDurationText(filePath);

        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            title = string.IsNullOrWhiteSpace(tagFile.Tag.Title)
                ? fallbackTitle
                : tagFile.Tag.Title.Trim();

            album = string.IsNullOrWhiteSpace(tagFile.Tag.Album)
                ? album
                : tagFile.Tag.Album.Trim();

            var performers = tagFile.Tag.Performers
                .Concat(tagFile.Tag.AlbumArtists)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct()
                .ToArray();

            if (performers.Length > 0)
            {
                artist = string.Join(", ", performers);
            }

            if (tagFile.Properties.Duration > TimeSpan.Zero)
            {
                durationText = FormatTime(tagFile.Properties.Duration);
            }
        }
        catch
        {
            // Keep filename and duration fallback when the file has no readable metadata.
        }

        return new TrackMetadata(title, artist, durationText, album);
    }

    private void DisposeAudio()
    {
        using var _ = PerfLog.Measure("DisposeAudio total");

        using (PerfLog.Measure("DisposeAudio stop progress timer"))
        {
            progressTimer.Stop();
        }

        if (outputDevice is not null)
        {
            using (PerfLog.Measure("DisposeAudio output device total"))
            {
                outputDevice.PlaybackStopped -= OutputDevice_PlaybackStopped;

                using (PerfLog.Measure("DisposeAudio outputDevice.Stop"))
                {
                    outputDevice.Stop();
                }

                using (PerfLog.Measure("DisposeAudio outputDevice.Dispose"))
                {
                    outputDevice.Dispose();
                }

                outputDevice = null;
            }
        }

        if (audioFile is not null)
        {
            using (PerfLog.Measure("DisposeAudio audioFile.Dispose"))
            {
                audioFile.Dispose();
                audioFile = null;
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        using var _ = PerfLog.Measure("OnClosed total");
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        SaveStateIfNeeded();

        using (PerfLog.Measure("OnClosed notifyIcon.Dispose"))
        {
            notifyIcon?.Dispose();
        }

        DisposeAudio();

        using (PerfLog.Measure("OnClosed base.OnClosed"))
        {
            base.OnClosed(e);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        using var _ = PerfLog.Measure($"OnClosing total isExitRequested={isExitRequested}");

        if (!isExitRequested)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        SaveStateIfNeeded();
        using (PerfLog.Measure("OnClosing base.OnClosing"))
        {
            base.OnClosing(e);
        }
    }

    private sealed class DragPreviewAdorner(UIElement adornedElement, Visual previewVisual, double width, double height) : Adorner(adornedElement)
    {
        private readonly VisualBrush brush = new(previewVisual)
        {
            Opacity = 0.8
        };
        private double left;
        private double top;

        public void UpdatePosition(double x, double y)
        {
            left = x;
            top = y;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.PushOpacity(0.88);
            drawingContext.DrawRoundedRectangle(brush, null, new Rect(left, top, width, height), 7, 7);
            drawingContext.Pop();
        }
    }

    private sealed class InsertionLineAdorner(UIElement adornedElement) : Adorner(adornedElement)
    {
        private readonly WpfPen linePen = new(new SolidColorBrush(WpfColor.FromRgb(255, 137, 55)), 2.5);
        private double x;
        private double y;
        private double width;

        public void UpdateLine(double lineX, double lineY, double lineWidth)
        {
            x = lineX;
            y = lineY;
            width = lineWidth;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.DrawLine(linePen, new WpfPoint(x, y), new WpfPoint(x + width, y));
            drawingContext.DrawEllipse(linePen.Brush, null, new WpfPoint(x, y), 4, 4);
        }
    }

    private sealed record PlaylistEntry(
        PlaylistData Data,
        Border Row,
        ColumnDefinition IconColumn,
        ColumnDefinition TextColumn,
        ColumnDefinition MenuColumn,
        WpfTextBox NameTextBox,
        WpfButton MenuButton);

    private sealed class PlaylistData(int id, string name, string coverPath)
    {
        public int Id { get; } = id;

        public string Name { get; set; } = name;

        public string CoverPath { get; set; } = coverPath;

        public int PlayCount { get; set; }

        public List<PlaylistTrack> Tracks { get; } = [];
    }

    private sealed class PlaylistTrack(string filePath, string title, string durationText, string artist, string album, int playCount)
    {
        public string FilePath { get; } = filePath;

        public string Title { get; } = title;

        public string DurationText { get; } = durationText;

        public string Artist { get; } = artist;

        public string Album { get; } = album;

        public int PlayCount { get; set; } = playCount;
    }

    private sealed record TrackMetadata(string Title, string Artist, string DurationText, string Album);

    private enum PlaylistPlayMode
    {
        SequentialLoop,
        ShuffleLoop
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;

        public NativePoint MaxSize;

        public NativePoint MaxPosition;

        public NativePoint MinTrackSize;

        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;

        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;

        public NativeRect MonitorArea;

        public NativeRect WorkArea;

        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }
}
