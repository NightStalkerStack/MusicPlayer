using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace MusicPlayer.Views;

public partial class PlaylistContentView : WpfUserControl
{
    private readonly ObservableCollection<TrackListItem> trackItems = [];
    private WpfPoint dragStartPoint;
    private WpfPoint dragOffsetFromRow;
    private int dragStartIndex = -1;
    private TrackListItem? draggingTrackItem;
    private DataGridRow? draggingTrackRow;
    private AdornerLayer? dragAdornerLayer;
    private DragPreviewAdorner? dragAdorner;
    private InsertionLineAdorner? insertionAdorner;
    private readonly DispatcherTimer autoScrollTimer;
    private WpfPoint lastDragPoint;
    private bool isDraggingTrack;
    private bool suppressNextTrackClick;

    public event RoutedEventHandler? AddPlaylistRequested;
    public event RoutedEventHandler? PlayMusicRequested;
    public event RoutedEventHandler? AddMusicRequested;
    public event RoutedEventHandler? ClearPlaylistRequested;
    public event RoutedEventHandler? CoverChangeRequested;
    public event EventHandler<int>? TrackPlayRequested;
    public event EventHandler<int>? TrackOpenLocationRequested;
    public event EventHandler<int>? TrackRemoveRequested;
    public event EventHandler<TrackMoveRequestedEventArgs>? TrackMoveRequested;

    public PlaylistContentView()
    {
        InitializeComponent();
        autoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(35)
        };
        autoScrollTimer.Tick += (_, _) =>
        {
            AutoScrollTrackList(lastDragPoint);
            MoveDraggedTrackToPointer(lastDragPoint);
        };

        TrackDataGrid.ItemsSource = trackItems;
        TrackDataGrid.PreviewMouseMove += TrackDataGrid_PreviewMouseMove;
        TrackDataGrid.PreviewMouseLeftButtonUp += TrackDataGrid_PreviewMouseLeftButtonUp;
    }

    public void ShowEmptyState()
    {
        PlaylistTitleTextBlock.Text = "\u6682\u65E0\u6B4C\u5355";
        TrackCountTextBlock.Text = "0 \u9996\u6B4C";
        trackItems.Clear();
        CoverImage.Visibility = Visibility.Collapsed;
        CoverPlaceholder.Visibility = Visibility.Visible;
        EmptyTracksTextBlock.Text = "\u70B9\u51FB\u5DE6\u4FA7\u7684\u201C\u589E\u52A0\u6B4C\u5355\u201D\u5F00\u59CB\u521B\u5EFA";
        EmptyTracksTextBlock.Visibility = Visibility.Visible;
        TrackDataGrid.Visibility = Visibility.Collapsed;
    }

    public void ShowPlaylist(string playlistName, string coverPath, IReadOnlyList<PlaylistTrackViewModel> tracks)
    {
        PlaylistTitleTextBlock.Text = playlistName;
        TrackCountTextBlock.Text = $"{tracks.Count} \u9996\u6B4C";
        SetCover(coverPath);

        trackItems.Clear();
        for (var i = 0; i < tracks.Count; i++)
        {
            trackItems.Add(new TrackListItem(i + 1, tracks[i].Title, tracks[i].Album, tracks[i].DurationText, tracks[i].Artist));
        }

        EmptyTracksTextBlock.Text = "\u6682\u65E0\u97F3\u4E50\uFF0C\u70B9\u51FB\u201C\u6DFB\u52A0\u97F3\u4E50\u201D\u5F00\u59CB\u6784\u5EFA\u6B4C\u5355";
        EmptyTracksTextBlock.Visibility = tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TrackDataGrid.Visibility = tracks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SelectTrack(int trackIndex)
    {
        if (trackIndex < 0 || trackIndex >= trackItems.Count)
        {
            TrackDataGrid.SelectedItem = null;
            return;
        }

        var item = trackItems[trackIndex];
        TrackDataGrid.SelectedItem = item;
        TrackDataGrid.ScrollIntoView(item);
    }

    public double GetTrackScrollOffset()
    {
        return FindVisualChild<ScrollViewer>(TrackDataGrid)?.VerticalOffset ?? 0;
    }

    public void RestoreTrackScrollOffset(double offset)
    {
        Dispatcher.BeginInvoke(() =>
        {
            TrackDataGrid.UpdateLayout();
            FindVisualChild<ScrollViewer>(TrackDataGrid)?.ScrollToVerticalOffset(offset);
        }, DispatcherPriority.Loaded);
    }

    private void SetCover(string coverPath)
    {
        if (string.IsNullOrWhiteSpace(coverPath))
        {
            CoverImage.Visibility = Visibility.Collapsed;
            CoverPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(coverPath, UriKind.RelativeOrAbsolute);
            image.EndInit();
            image.Freeze();

            CoverImage.Source = image;
            CoverImage.Visibility = Visibility.Visible;
            CoverPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            CoverImage.Visibility = Visibility.Collapsed;
            CoverPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void AddMusicButton_Click(object sender, RoutedEventArgs e)
    {
        AddMusicRequested?.Invoke(this, e);
    }

    private void PlayMusicButton_Click(object sender, RoutedEventArgs e)
    {
        PlayMusicRequested?.Invoke(this, e);
    }

    private void ClearPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        ClearPlaylistRequested?.Invoke(this, e);
    }

    private void CoverButton_Click(object sender, RoutedEventArgs e)
    {
        CoverChangeRequested?.Invoke(this, e);
    }

    private void AddPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        AddPlaylistRequested?.Invoke(this, e);
    }

    private void TrackItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (suppressNextTrackClick)
        {
            suppressNextTrackClick = false;
            e.Handled = true;
            return;
        }

        if (sender is FrameworkElement { DataContext: TrackListItem item })
        {
            TrackPlayRequested?.Invoke(this, item.Index - 1);
        }
    }

    private void TrackItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow { DataContext: TrackListItem item } row)
        {
            return;
        }

        TrackDataGrid.SelectedItem = item;

        var menu = AppUi.CreateContextMenu(row);
        menu.Items.Add(AppUi.CreateMenuItem("\u6253\u5F00\u6587\u4EF6\u6240\u5728\u4F4D\u7F6E", (_, _) => TrackOpenLocationRequested?.Invoke(this, item.Index - 1)));
        menu.Items.Add(AppUi.CreateMenuItem("\u4ECE\u6B4C\u5355\u4E2D\u5220\u9664", (_, _) => TrackRemoveRequested?.Invoke(this, item.Index - 1)));

        row.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void TrackItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow row || row.DataContext is not TrackListItem item)
        {
            dragStartIndex = -1;
            draggingTrackItem = null;
            draggingTrackRow = null;
            return;
        }

        dragStartPoint = e.GetPosition(TrackDataGrid);
        dragOffsetFromRow = e.GetPosition(row);
        dragStartIndex = item.Index - 1;
        draggingTrackItem = item;
        draggingTrackRow = row;
        isDraggingTrack = false;
    }

    private void TrackDataGrid_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || dragStartIndex < 0
            || draggingTrackItem is null)
        {
            return;
        }

        var currentPoint = e.GetPosition(TrackDataGrid);
        lastDragPoint = currentPoint;
        if (!isDraggingTrack)
        {
            if (Math.Abs(currentPoint.X - dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(currentPoint.Y - dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            BeginTrackDrag();
        }

        dragAdorner?.UpdatePosition(currentPoint.X - dragOffsetFromRow.X, currentPoint.Y - dragOffsetFromRow.Y);
        AutoScrollTrackList(currentPoint);
        MoveDraggedTrackToPointer(currentPoint);
        e.Handled = true;
    }

    private void TrackDataGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isDraggingTrack)
        {
            ResetTrackDragState();
            return;
        }

        CompleteTrackDrag();
        e.Handled = true;
    }

    private void BeginTrackDrag()
    {
        isDraggingTrack = true;
        suppressNextTrackClick = true;
        draggingTrackRow ??= GetRowForTrackItem(draggingTrackItem);
        if (draggingTrackRow is not null)
        {
            dragAdornerLayer = AdornerLayer.GetAdornerLayer(TrackDataGrid);
            if (dragAdornerLayer is not null)
            {
                dragAdorner = new DragPreviewAdorner(TrackDataGrid, draggingTrackRow, draggingTrackRow.ActualWidth, draggingTrackRow.ActualHeight)
                {
                    IsHitTestVisible = false
                };
                dragAdornerLayer.Add(dragAdorner);
            }

            draggingTrackRow.Opacity = 0.35;
        }

        TrackDataGrid.CaptureMouse();
        autoScrollTimer.Start();
    }

    private void MoveDraggedTrackToPointer(WpfPoint point)
    {
        if (draggingTrackItem is null)
        {
            return;
        }

        var row = FindRowFromPoint(point);
        if (row?.DataContext is not TrackListItem targetItem)
        {
            HideInsertionLine();
            return;
        }

        if (ReferenceEquals(targetItem, draggingTrackItem))
        {
            UpdateInsertionLine(row, placeAfter: true);
            return;
        }

        var sourceIndex = trackItems.IndexOf(draggingTrackItem);
        var targetIndex = trackItems.IndexOf(targetItem);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        UpdateInsertionLine(row, targetIndex > sourceIndex);
        trackItems.Move(sourceIndex, targetIndex);
        ResetTrackItemIndexes();
        TrackDataGrid.SelectedItem = draggingTrackItem;
    }

    private void CompleteTrackDrag()
    {
        var targetIndex = draggingTrackItem is null ? -1 : trackItems.IndexOf(draggingTrackItem);
        ClearTrackDragVisuals();

        if (dragStartIndex >= 0 && targetIndex >= 0 && dragStartIndex != targetIndex)
        {
            TrackMoveRequested?.Invoke(this, new TrackMoveRequestedEventArgs(dragStartIndex, targetIndex));
        }

        ResetTrackDragState();
    }

    private void ClearTrackDragVisuals()
    {
        autoScrollTimer.Stop();
        TrackDataGrid.ReleaseMouseCapture();
        if (draggingTrackRow is not null)
        {
            draggingTrackRow.Opacity = 1;
        }

        HideInsertionLine();

        if (dragAdornerLayer is not null && dragAdorner is not null)
        {
            dragAdornerLayer.Remove(dragAdorner);
        }

        dragAdorner = null;
        dragAdornerLayer = null;
    }

    private void ResetTrackDragState()
    {
        if (draggingTrackRow is not null)
        {
            draggingTrackRow.Opacity = 1;
        }

        isDraggingTrack = false;
        dragStartIndex = -1;
        draggingTrackItem = null;
        draggingTrackRow = null;
    }

    private void AutoScrollTrackList(WpfPoint point)
    {
        if (!isDraggingTrack)
        {
            return;
        }

        var scrollViewer = FindVisualChild<ScrollViewer>(TrackDataGrid);
        if (scrollViewer is null)
        {
            return;
        }

        const double edgeSize = 42;
        const double scrollStep = 18;
        if (point.Y < edgeSize)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollStep);
        }
        else if (point.Y > TrackDataGrid.ActualHeight - edgeSize)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollStep);
        }
    }

    private void UpdateInsertionLine(DataGridRow row, bool placeAfter)
    {
        dragAdornerLayer ??= AdornerLayer.GetAdornerLayer(TrackDataGrid);
        if (dragAdornerLayer is null)
        {
            return;
        }

        insertionAdorner ??= new InsertionLineAdorner(TrackDataGrid)
        {
            IsHitTestVisible = false
        };

        var adorners = dragAdornerLayer.GetAdorners(TrackDataGrid);
        if (adorners is null || !adorners.Contains(insertionAdorner))
        {
            dragAdornerLayer.Add(insertionAdorner);
        }

        var position = row.TransformToAncestor(TrackDataGrid).Transform(new WpfPoint(0, 0));
        var y = placeAfter ? position.Y + row.ActualHeight - 3 : position.Y + 3;
        insertionAdorner.UpdateLine(0, y, Math.Max(0, TrackDataGrid.ActualWidth - 14));
    }

    private void HideInsertionLine()
    {
        if (dragAdornerLayer is not null && insertionAdorner is not null)
        {
            dragAdornerLayer.Remove(insertionAdorner);
        }

        insertionAdorner = null;
    }

    private DataGridRow? FindRowFromPoint(WpfPoint point)
    {
        var result = VisualTreeHelper.HitTest(TrackDataGrid, point);
        var source = result?.VisualHit;
        while (source is not null)
        {
            if (source is DataGridRow row)
            {
                return row;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private DataGridRow? GetRowForTrackItem(TrackListItem? item)
    {
        return item is null
            ? null
            : TrackDataGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
    }

    private void ResetTrackItemIndexes()
    {
        for (var i = 0; i < trackItems.Count; i++)
        {
            trackItems[i].Index = i + 1;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var result = FindVisualChild<T>(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private sealed class TrackListItem(int index, string title, string album, string durationText, string artist) : INotifyPropertyChanged
    {
        private int index = index;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index
        {
            get => index;
            set
            {
                if (index == value)
                {
                    return;
                }

                index = value;
                OnPropertyChanged();
            }
        }

        public string Title { get; } = title;

        public string Album { get; } = album;

        public string DurationText { get; } = durationText;

        public string Artist { get; } = artist;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
            drawingContext.DrawRoundedRectangle(brush, null, new Rect(left, top, width, height), 6, 6);
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
            drawingContext.DrawLine(linePen, new WpfPoint(x + 8, y), new WpfPoint(x + width, y));
            drawingContext.DrawEllipse(linePen.Brush, null, new WpfPoint(x + 8, y), 4, 4);
        }
    }
}

public sealed record PlaylistTrackViewModel(string Title, string DurationText, string Artist, string Album);

public sealed record TrackMoveRequestedEventArgs(int SourceIndex, int TargetIndex);
