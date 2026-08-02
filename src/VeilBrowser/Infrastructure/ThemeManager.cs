using System.Windows;
using System.Windows.Media;
using VeilBrowser.Core.Models;

namespace VeilBrowser.Infrastructure;

public static class ThemeManager
{
    private static readonly Dictionary<BrowserTheme, ThemePalette> Palettes =
        new Dictionary<BrowserTheme, ThemePalette>
        {
            [BrowserTheme.MidnightEmerald] = new(
                Window: "#0B1116",
                Chrome: "#0F151B",
                Surface: "#151D22",
                SurfaceAlt: "#11181D",
                SurfaceHover: "#1D282F",
                Border: "#27343C",
                Accent: "#39D98A",
                AccentHover: "#66E6A8",
                Protect: "#4BDE91",
                Text: "#F2F7F4",
                MutedText: "#8FA19A",
                Danger: "#FF7186",
                PrimaryText: "#06140C",
                ProtectionSurface: "#11251B",
                ProtectionBorder: "#2C6042",
                Selected: "#22362C",
                Tooltip: "#17231D",
                Overlay: "#F20B1116"),
            [BrowserTheme.PorcelainDaylight] = new(
                Window: "#F6F7F4",
                Chrome: "#FFFFFF",
                Surface: "#FFFFFF",
                SurfaceAlt: "#F1F4F1",
                SurfaceHover: "#E8EEEA",
                Border: "#D6DEDA",
                Accent: "#008F77",
                AccentHover: "#00AA8D",
                Protect: "#00A67E",
                Text: "#1C252B",
                MutedText: "#68766F",
                Danger: "#C94159",
                PrimaryText: "#FFFFFF",
                ProtectionSurface: "#E8F6F1",
                ProtectionBorder: "#9ED7C6",
                Selected: "#DDF1EB",
                Tooltip: "#FFFFFF",
                Overlay: "#EAF6F7F4"),
            [BrowserTheme.GraphiteFocus] = new(
                Window: "#15181D",
                Chrome: "#191D23",
                Surface: "#20252B",
                SurfaceAlt: "#181D23",
                SurfaceHover: "#292F36",
                Border: "#353D46",
                Accent: "#6C98F0",
                AccentHover: "#91B2F5",
                Protect: "#48C78E",
                Text: "#EEF1F4",
                MutedText: "#929BA5",
                Danger: "#F06F82",
                PrimaryText: "#07130C",
                ProtectionSurface: "#183126",
                ProtectionBorder: "#35654E",
                Selected: "#23334A",
                Tooltip: "#252B32",
                Overlay: "#F215181D")
        };

    public static BrowserTheme Current { get; private set; } =
        BrowserTheme.MidnightEmerald;

    public static void Apply(BrowserTheme theme)
    {
        if (!Palettes.TryGetValue(theme, out var palette))
        {
            theme = BrowserTheme.MidnightEmerald;
            palette = Palettes[theme];
        }

        Current = theme;
        SetBrush("WindowBrush", palette.Window);
        SetBrush("ChromeBrush", palette.Chrome);
        SetBrush("SurfaceBrush", palette.Surface);
        SetBrush("SurfaceAltBrush", palette.SurfaceAlt);
        SetBrush("SurfaceHoverBrush", palette.SurfaceHover);
        SetBrush("BorderBrush", palette.Border);
        SetBrush("AccentBrush", palette.Accent);
        SetBrush("AccentHoverBrush", palette.AccentHover);
        SetBrush("ProtectBrush", palette.Protect);
        SetBrush("TextBrush", palette.Text);
        SetBrush("MutedTextBrush", palette.MutedText);
        SetBrush("DangerBrush", palette.Danger);
        SetBrush("PrimaryTextBrush", palette.PrimaryText);
        SetBrush("ProtectionSurfaceBrush", palette.ProtectionSurface);
        SetBrush("ProtectionBorderBrush", palette.ProtectionBorder);
        SetBrush("SelectedSurfaceBrush", palette.Selected);
        SetBrush("TooltipBrush", palette.Tooltip);
        SetBrush("OverlayBrush", palette.Overlay);
    }

    private static void SetBrush(string key, string color)
    {
        Application.Current.Resources[key] = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(color));
    }

    private sealed record ThemePalette(
        string Window,
        string Chrome,
        string Surface,
        string SurfaceAlt,
        string SurfaceHover,
        string Border,
        string Accent,
        string AccentHover,
        string Protect,
        string Text,
        string MutedText,
        string Danger,
        string PrimaryText,
        string ProtectionSurface,
        string ProtectionBorder,
        string Selected,
        string Tooltip,
        string Overlay);
}
