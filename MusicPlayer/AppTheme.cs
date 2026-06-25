using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfControl = System.Windows.Controls.Control;
using WpfPanel = System.Windows.Controls.Panel;

namespace MusicPlayer;

public enum ThemePreset
{
    System,
    Dark,
    Light
}

public enum AppThemeRole
{
    None,
    Window,
    Sidebar,
    Panel,
    PlayerBar,
    BorderLine,
    CoverSurface,
    TextPrimary,
    TextMuted,
    Accent
}

public static class AppTheme
{
    public const string DefaultAccentColor = "#5AA9FF";
    public static readonly DependencyProperty RoleProperty = DependencyProperty.RegisterAttached(
        "Role",
        typeof(AppThemeRole),
        typeof(AppTheme),
        new PropertyMetadata(AppThemeRole.None, OnRoleChanged));

    private static ThemePalette currentPalette = CreatePalette(ThemePreset.Dark);

    public static void Apply(ThemePreset preset, FrameworkElement? localResourceOwner = null)
    {
        var resolvedPreset = ResolvePreset(preset);
        using var measure = PerfLog.Measure($"Theme.Apply preset={preset} resolved={resolvedPreset} owner={localResourceOwner?.GetType().Name ?? "none"}");

        try
        {
            var palette = CreatePalette(resolvedPreset);
            currentPalette = palette;

            using (PerfLog.Measure("Theme.Apply global resources"))
            {
                ApplyToResources(WpfApplication.Current.Resources, palette);
            }

            if (localResourceOwner is not null)
            {
                using (PerfLog.Measure("Theme.Apply local resources"))
                {
                    ApplyToResources(localResourceOwner.Resources, palette);
                }

                ApplyRegisteredElements(localResourceOwner);
            }
        }
        catch (Exception ex)
        {
            PerfLog.Exception("Theme.Apply", ex);
            throw;
        }
    }

    public static void SetRole(DependencyObject element, AppThemeRole value)
    {
        element.SetValue(RoleProperty, value);
    }

    public static AppThemeRole GetRole(DependencyObject element)
    {
        return (AppThemeRole)element.GetValue(RoleProperty);
    }

    public static void ApplyRegisteredElements(DependencyObject root)
    {
        using var measure = PerfLog.Measure($"Theme.ApplyRegisteredElements root={root.GetType().Name}");
        var stopwatch = Stopwatch.StartNew();
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(DependencyObject Element, int Depth)>();
        var stats = new ThemeApplyStats();

        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (element, depth) = stack.Pop();
            stats.MaxDepth = Math.Max(stats.MaxDepth, depth);

            if (!visited.Add(element))
            {
                stats.DuplicateCount++;
                continue;
            }

            stats.VisitedCount++;
            if (stats.VisitedCount % 1000 == 0)
            {
                PerfLog.Mark($"Theme.ApplyRegisteredElements progress visited={stats.VisitedCount} roles={stats.RoleCount} duplicates={stats.DuplicateCount} depth={stats.MaxDepth} elapsed={stopwatch.ElapsedMilliseconds}ms");
            }

            var role = GetRole(element);
            if (role != AppThemeRole.None)
            {
                stats.RoleCount++;
                ApplyRole(element, role, currentPalette);
            }

            foreach (var child in EnumerateChildren(element))
            {
                stats.ChildEdgeCount++;
                stack.Push((child, depth + 1));
            }
        }

        PerfLog.Mark($"Theme.ApplyRegisteredElements summary root={root.GetType().Name} visited={stats.VisitedCount} roles={stats.RoleCount} edges={stats.ChildEdgeCount} duplicates={stats.DuplicateCount} maxDepth={stats.MaxDepth} elapsed={stopwatch.ElapsedMilliseconds}ms");
    }

    public static void ApplyElement(DependencyObject element)
    {
        var role = GetRole(element);
        if (role == AppThemeRole.None)
        {
            return;
        }

        ApplyRole(element, role, currentPalette);
    }

    public static ThemePreset ParsePreset(string? value)
    {
        return Enum.TryParse<ThemePreset>(value, ignoreCase: true, out var preset)
            ? preset
            : ThemePreset.Dark;
    }

    public static ThemePreset ResolvePreset(ThemePreset preset)
    {
        if (preset != ThemePreset.System)
        {
            return preset;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value > 0
                ? ThemePreset.Light
                : ThemePreset.Dark;
        }
        catch (Exception ex)
        {
            PerfLog.Exception("Theme.ResolvePreset", ex);
            return ThemePreset.Dark;
        }
    }

    private static ThemePalette CreatePalette(ThemePreset preset)
    {
        return preset switch
        {
            ThemePreset.Light => new ThemePalette(
                WpfColor.FromRgb(0xF5, 0xF7, 0xFB),
                WpfColor.FromRgb(0xEA, 0xEE, 0xF5),
                WpfColor.FromRgb(0xFF, 0xFF, 0xFF),
                WpfColor.FromRgb(0xD6, 0xE0, 0xEF),
                WpfColor.FromRgb(0xD8, 0xE2, 0xF0),
                WpfColor.FromRgb(0xFF, 0xFF, 0xFF),
                WpfColor.FromRgb(0xF6, 0xF8, 0xFC),
                WpfColor.FromRgb(0x16, 0x1A, 0x22),
                WpfColor.FromRgb(0x5F, 0x6B, 0x7C),
                WpfColor.FromRgb(0x2F, 0x80, 0xED),
                WpfColor.FromRgb(0x2F, 0x80, 0xED),
                WpfColor.FromRgb(0xC8, 0xD1, 0xDF),
                WpfColor.FromRgb(0xD7, 0xE3, 0xF3),
                WpfColor.FromRgb(0x86, 0xA1, 0xC8),
                WpfColor.FromRgb(0xD9, 0x2D, 0x20),
                WpfColor.FromRgb(0xC0, 0x25, 0x1C),
                WpfColor.FromRgb(0xFE, 0xE4, 0xE2),
                WpfColor.FromRgb(0xB4, 0x23, 0x18)),
            _ => new ThemePalette(
                WpfColor.FromRgb(0x18, 0x18, 0x18),
                WpfColor.FromRgb(0x1E, 0x1E, 0x1E),
                WpfColor.FromRgb(0x24, 0x24, 0x24),
                WpfColor.FromRgb(0x2F, 0x2F, 0x2F),
                WpfColor.FromRgb(0x3A, 0x3A, 0x3A),
                WpfColor.FromRgb(0x20, 0x20, 0x20),
                WpfColor.FromRgb(0x18, 0x18, 0x18),
                WpfColor.FromRgb(0xF2, 0xF2, 0xF2),
                WpfColor.FromRgb(0xA7, 0xA7, 0xA7),
                WpfColor.FromRgb(0xFF, 0x8A, 0x3D),
                WpfColor.FromRgb(0xFF, 0x8A, 0x3D),
                WpfColor.FromRgb(0x7A, 0x7A, 0x7A),
                WpfColor.FromRgb(0x4A, 0x4A, 0x4A),
                WpfColor.FromRgb(0xFF, 0xB0, 0x71),
                WpfColor.FromRgb(0xC2, 0x41, 0x32),
                WpfColor.FromRgb(0xD1, 0x4B, 0x3B),
                WpfColor.FromRgb(0x3A, 0x2A, 0x28),
                WpfColor.FromRgb(0xFF, 0xB4, 0xA8))
        };
    }

    private static void ApplyToResources(ResourceDictionary resources, ThemePalette palette)
    {
        PerfLog.Mark($"Theme.ApplyToResources start count={resources.Count}");
        SetBrush(resources, "WindowBrush", palette.Window);
        SetBrush(resources, "SidebarBrush", palette.Sidebar);
        SetBrush(resources, "PanelBrush", palette.Panel);
        SetBrush(resources, "PanelHoverBrush", palette.PanelHover);
        SetBrush(resources, "PanelBorderBrush", palette.Border);
        SetBrush(resources, "PlayerBarBrush", palette.PlayerBar);
        SetBrush(resources, "CoverSurfaceBrush", palette.CoverSurface);
        SetBrush(resources, "TextPrimaryBrush", palette.TextPrimary);
        SetBrush(resources, "MutedBrush", palette.TextMuted);
        SetBrush(resources, "AccentBrush", palette.Accent);
        SetBrush(resources, "ButtonBrush", palette.PanelHover);
        SetBrush(resources, "ButtonHoverBrush", palette.Border);
        SetBrush(resources, "ButtonBorderBrush", palette.Border);
        SetBrush(resources, "SliderActiveBrush", palette.SliderActive);
        SetBrush(resources, "SliderTrackBrush", palette.SliderTrack);
        SetBrush(resources, "SliderThumbOuterBrush", palette.SliderThumbOuter);
        SetBrush(resources, "SliderThumbStrokeBrush", palette.SliderThumbStroke);
        SetBrush(resources, "DangerBrush", palette.Danger);
        SetBrush(resources, "DangerHoverBrush", palette.DangerHover);
        SetBrush(resources, "DangerSubtleBrush", palette.DangerSubtle);
        SetBrush(resources, "DangerTextBrush", palette.DangerText);
        PerfLog.Mark($"Theme.ApplyToResources end count={resources.Count}");
    }

    private static void SetBrush(ResourceDictionary resources, string key, WpfColor color)
    {
        if (resources[key] is SolidColorBrush existingBrush && !existingBrush.IsFrozen)
        {
            existingBrush.Color = color;
        }
        else
        {
            resources[key] = new SolidColorBrush(color);
        }
    }

    private static void OnRoleChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        ApplyElement(element);
    }

    private static void ApplyRole(DependencyObject element, AppThemeRole role, ThemePalette palette)
    {
        var brush = role switch
        {
            AppThemeRole.Window => CreateBrush(palette.Window),
            AppThemeRole.Sidebar => CreateBrush(palette.Sidebar),
            AppThemeRole.Panel => CreateBrush(palette.Panel),
            AppThemeRole.PlayerBar => CreateBrush(palette.PlayerBar),
            AppThemeRole.BorderLine => CreateBrush(palette.Border),
            AppThemeRole.CoverSurface => CreateBrush(palette.CoverSurface),
            AppThemeRole.TextPrimary => CreateBrush(palette.TextPrimary),
            AppThemeRole.TextMuted => CreateBrush(palette.TextMuted),
            AppThemeRole.Accent => CreateBrush(palette.Accent),
            _ => null
        };

        if (brush is null)
        {
            return;
        }

        switch (element)
        {
            case Border border when role is AppThemeRole.Window or AppThemeRole.Sidebar or AppThemeRole.Panel or AppThemeRole.PlayerBar or AppThemeRole.CoverSurface:
                border.Background = brush;
                if (role is AppThemeRole.Panel or AppThemeRole.CoverSurface)
                {
                    border.BorderBrush = CreateBrush(palette.Border);
                }
                break;
            case Border border when role == AppThemeRole.BorderLine:
                border.Background = brush;
                border.BorderBrush = brush;
                break;
            case WpfPanel panel when role is AppThemeRole.Window or AppThemeRole.Sidebar or AppThemeRole.Panel or AppThemeRole.PlayerBar:
                panel.Background = brush;
                break;
            case WpfControl control when role is AppThemeRole.TextPrimary or AppThemeRole.TextMuted or AppThemeRole.Accent:
                control.Foreground = brush;
                break;
            case TextBlock textBlock when role is AppThemeRole.TextPrimary or AppThemeRole.TextMuted or AppThemeRole.Accent:
                textBlock.Foreground = brush;
                break;
            case Shape shape when role == AppThemeRole.Accent:
                shape.Fill = brush;
                shape.Stroke = brush;
                break;
        }
    }

    private static IEnumerable<DependencyObject> EnumerateChildren(DependencyObject root)
    {
        var visualChildrenCount = 0;
        try
        {
            visualChildrenCount = VisualTreeHelper.GetChildrenCount(root);
        }
        catch (InvalidOperationException)
        {
            visualChildrenCount = 0;
        }

        for (var i = 0; i < visualChildrenCount; i++)
        {
            yield return VisualTreeHelper.GetChild(root, i);
        }

        foreach (var logicalChild in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return logicalChild;
        }
    }

    private static SolidColorBrush CreateBrush(WpfColor color)
    {
        return new SolidColorBrush(color);
    }

    private sealed record ThemePalette(
        WpfColor Window,
        WpfColor Sidebar,
        WpfColor Panel,
        WpfColor PanelHover,
        WpfColor Border,
        WpfColor PlayerBar,
        WpfColor CoverSurface,
        WpfColor TextPrimary,
        WpfColor TextMuted,
        WpfColor Accent,
        WpfColor SliderActive,
        WpfColor SliderTrack,
        WpfColor SliderThumbOuter,
        WpfColor SliderThumbStroke,
        WpfColor Danger,
        WpfColor DangerHover,
        WpfColor DangerSubtle,
        WpfColor DangerText);

    private sealed class ThemeApplyStats
    {
        public int VisitedCount { get; set; }
        public int RoleCount { get; set; }
        public int ChildEdgeCount { get; set; }
        public int DuplicateCount { get; set; }
        public int MaxDepth { get; set; }
    }
}
