using System.Windows;
using System.Windows.Media;

namespace NovaBrowser.App.Services;

public static class NovaThemeService
{
    public static readonly string[] KnownThemes = { "NovaNeon", "Aurora", "Fokus", "Glas" };

    public static void Apply(string theme, ResourceDictionary target)
    {
        var palette = GetPalette(theme);

        target["NovaButton"] = LinearBrush(
            (palette.AccentA, 0),
            (palette.AccentB, 1));

        target["NovaFireButton"] = LinearBrush(
            (palette.AccentA, 0),
            (palette.AccentB, 0.55),
            (palette.AccentC, 1));

        target["NovaGlassPanel"] = LinearBrush(
            (palette.PanelTint, 0),
            (palette.PanelMid, 0.58),
            (palette.PanelDark, 1));

        target["NovaFlameWash"] = LinearBrush(
            (palette.WashA, 0),
            (palette.WashB, 0.48),
            (palette.PanelDark, 1));

        target["NovaAuroraBackground"] = RadialBrush(
            (palette.WashA, 0),
            (palette.PanelMid, 0.42),
            (palette.PanelDark, 1));
    }

    private static LinearGradientBrush LinearBrush(params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        foreach (var (color, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }
        return brush;
    }

    private static RadialGradientBrush RadialBrush(params (Color Color, double Offset)[] stops)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.62, 0.18),
            GradientOrigin = new Point(0.62, 0.18),
            RadiusX = 0.9,
            RadiusY = 0.9
        };
        foreach (var (color, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }
        return brush;
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    private static ThemePalette GetPalette(string theme) => theme switch
    {
        "Aurora" => new ThemePalette(
            AccentA: C("#2e8fff"), AccentB: C("#34c9ff"), AccentC: C("#33ffd6"),
            PanelTint: C("#13283f"), PanelMid: C("#10181f"), PanelDark: C("#07090d"),
            WashA: C("#0a2a5a"), WashB: C("#15455f")),

        "Fokus" => new ThemePalette(
            AccentA: C("#4a5f8f"), AccentB: C("#5a6fa0"), AccentC: C("#6fa0c0"),
            PanelTint: C("#1c2230"), PanelMid: C("#161a22"), PanelDark: C("#090a0d"),
            WashA: C("#1f2a4a"), WashB: C("#28304f")),

        "Glas" => new ThemePalette(
            AccentA: C("#d782ff"), AccentB: C("#ff9fd6"), AccentC: C("#ffd0ec"),
            PanelTint: C("#3f1f3a"), PanelMid: C("#20141f"), PanelDark: C("#0d090c"),
            WashA: C("#4a205a"), WashB: C("#5f2f55")),

        _ => new ThemePalette(
            AccentA: C("#8f42ff"), AccentB: C("#a739ff"), AccentC: C("#33d6ff"),
            PanelTint: C("#24133f"), PanelMid: C("#15101f"), PanelDark: C("#09070d"),
            WashA: C("#28105a"), WashB: C("#33165f")),
    };

    private sealed record ThemePalette(
        Color AccentA,
        Color AccentB,
        Color AccentC,
        Color PanelTint,
        Color PanelMid,
        Color PanelDark,
        Color WashA,
        Color WashB);
}
