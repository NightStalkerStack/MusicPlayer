using System.Windows.Controls;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace MusicPlayer.Views;

public partial class SettingsView : WpfUserControl
{
    private bool isUpdating;

    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    public SettingsView()
    {
        InitializeComponent();
    }

    public void SetTheme(ThemePreset preset)
    {
        isUpdating = true;

        foreach (var item in ThemePresetComboBox.Items.OfType<WpfComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, preset.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                ThemePresetComboBox.SelectedItem = item;
                break;
            }
        }

        isUpdating = false;
    }

    private void ThemePresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isUpdating || ThemePresetComboBox.SelectedItem is not WpfComboBoxItem { Tag: string tag })
        {
            return;
        }

        ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(AppTheme.ParsePreset(tag)));
    }
}

public sealed record ThemeChangedEventArgs(ThemePreset Preset);
