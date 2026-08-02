using System.Windows;
using System.Windows.Media;
using VeilBrowser.Core.Models;
using VeilBrowser.Infrastructure;

namespace VeilBrowser.Ui.SmokeTests;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        foreach (var key in new[]
        {
            "WindowBrush",
            "ChromeBrush",
            "SurfaceBrush",
            "SurfaceAltBrush",
            "SurfaceHoverBrush",
            "BorderBrush",
            "AccentBrush",
            "AccentHoverBrush",
            "ProtectBrush",
            "TextBrush",
            "MutedTextBrush",
            "DangerBrush",
            "PrimaryTextBrush",
            "ProtectionSurfaceBrush",
            "ProtectionBorderBrush",
            "SelectedSurfaceBrush",
            "TooltipBrush",
            "OverlayBrush"
        })
        {
            application.Resources[key] = new SolidColorBrush();
        }

        var expectedWindows = new Dictionary<BrowserTheme, Color>
        {
            [BrowserTheme.MidnightEmerald] =
                (Color)ColorConverter.ConvertFromString("#0B1116"),
            [BrowserTheme.PorcelainDaylight] =
                (Color)ColorConverter.ConvertFromString("#F6F7F4"),
            [BrowserTheme.GraphiteFocus] =
                (Color)ColorConverter.ConvertFromString("#15181D")
        };

        foreach (var pair in expectedWindows)
        {
            ThemeManager.Apply(pair.Key);
            Require(
                ThemeManager.Current == pair.Key,
                $"ThemeManager did not select {pair.Key}.");
            var brush = (SolidColorBrush)application.Resources["WindowBrush"];
            Require(
                brush.Color == pair.Value,
                $"{pair.Key} window color was not applied.");
        }

        Console.WriteLine("All VeilBrowser UI theme smoke tests passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
