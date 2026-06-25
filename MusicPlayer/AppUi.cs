using System.Windows;
using System.Windows.Controls;
using WpfApplication = System.Windows.Application;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace MusicPlayer;

public static class AppUi
{
    public static WpfContextMenu CreateContextMenu(FrameworkElement placementTarget)
    {
        return new WpfContextMenu
        {
            Style = (Style)WpfApplication.Current.FindResource("AppContextMenuStyle"),
            PlacementTarget = placementTarget
        };
    }

    public static WpfMenuItem CreateMenuItem(string header, RoutedEventHandler clickHandler)
    {
        var item = new WpfMenuItem
        {
            Header = header,
            Style = (Style)WpfApplication.Current.FindResource("AppMenuItemStyle")
        };
        item.Click += clickHandler;
        return item;
    }

    public static bool Confirm(Window owner, string title, string message, string confirmText = "\u786E\u8BA4", string cancelText = "\u53D6\u6D88")
    {
        var dialog = new ConfirmDialog(title, message, confirmText, cancelText)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true;
    }
}
