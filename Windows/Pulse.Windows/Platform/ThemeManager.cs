namespace Pulse.Windows.Platform;

using System;
using System.Windows;
using System.Windows.Media;
using Pulse.Windows.SystemIntegration;

public static class ThemeManager
{
    public static string CurrentTheme { get; private set; } = "dark";

    public static void ApplyTheme(string themeName, bool usesGlass = false, Window? targetWindow = null)
    {
        CurrentTheme = themeName.ToLowerInvariant();

        var app = Application.Current;
        if (app == null) return;

        Color winBg, sidebarBg, cardBg, border, railBg, railBorder, textPrim, textSec, textMuted, accent, ringTrack;
        bool isLight = false;

        switch (CurrentTheme)
        {
            case "light":
                isLight = true;
                winBg = Color.FromRgb(0xF5, 0xF5, 0xF7);
                sidebarBg = Color.FromRgb(0xEA, 0xEA, 0xEF);
                cardBg = Color.FromRgb(0xFF, 0xFF, 0xFF);
                border = Color.FromRgb(0xD2, 0xD2, 0xD7);
                railBg = usesGlass ? Color.FromArgb(0xD0, 0xF5, 0xF5, 0xF7) : Color.FromRgb(0xFF, 0xFF, 0xFF);
                railBorder = Color.FromRgb(0xCC, 0xCC, 0xD2);
                textPrim = Color.FromRgb(0x1D, 0x1D, 0x1F);
                textSec = Color.FromRgb(0x48, 0x48, 0x4A);
                textMuted = Color.FromRgb(0x86, 0x86, 0x8B);
                accent = Color.FromRgb(0x00, 0x71, 0xE3);
                ringTrack = Color.FromRgb(0xE5, 0xE5, 0xEA);
                break;

            case "purple":
                winBg = Color.FromRgb(0x0B, 0x08, 0x14);
                sidebarBg = Color.FromRgb(0x13, 0x0E, 0x24);
                cardBg = Color.FromRgb(0x1A, 0x13, 0x33);
                border = Color.FromRgb(0x32, 0x25, 0x5C);
                railBg = usesGlass ? Color.FromArgb(0xD0, 0x12, 0x0C, 0x24) : Color.FromRgb(0x10, 0x0B, 0x22);
                railBorder = Color.FromRgb(0x3D, 0x2D, 0x70);
                textPrim = Color.FromRgb(0xF5, 0xF3, 0xFF);
                textSec = Color.FromRgb(0xDD, 0xD6, 0xFE);
                textMuted = Color.FromRgb(0xA7, 0x8B, 0xFA);
                accent = Color.FromRgb(0xA8, 0x55, 0xF7);
                ringTrack = Color.FromRgb(0x25, 0x1B, 0x47);
                break;

            case "glass":
                winBg = Color.FromArgb(0xF0, 0x1A, 0x1A, 0x20);
                sidebarBg = Color.FromArgb(0xEA, 0x16, 0x16, 0x1A);
                cardBg = Color.FromArgb(0xD8, 0x24, 0x24, 0x2C);
                border = Color.FromArgb(0x50, 0x48, 0x48, 0x58);
                railBg = Color.FromArgb(0xCC, 0x18, 0x18, 0x22);
                railBorder = Color.FromArgb(0x70, 0x4C, 0x4C, 0x5C);
                textPrim = Color.FromRgb(0xFA, 0xFA, 0xFA);
                textSec = Color.FromRgb(0xE4, 0xE4, 0xE7);
                textMuted = Color.FromRgb(0xA1, 0xA1, 0xAA);
                accent = Color.FromRgb(0x0A, 0x84, 0xFF);
                ringTrack = Color.FromArgb(0x80, 0x2E, 0x2E, 0x3A);
                break;

            default: // dark
                winBg = Color.FromRgb(0x18, 0x18, 0x1C);
                sidebarBg = Color.FromRgb(0x13, 0x13, 0x16);
                cardBg = Color.FromRgb(0x22, 0x22, 0x28);
                border = Color.FromRgb(0x32, 0x32, 0x3C);
                railBg = usesGlass ? Color.FromArgb(0xCC, 0x18, 0x18, 0x22) : Color.FromRgb(0x18, 0x18, 0x1C);
                railBorder = usesGlass ? Color.FromArgb(0x70, 0x4C, 0x4C, 0x5C) : Color.FromRgb(0x32, 0x32, 0x3C);
                textPrim = Color.FromRgb(0xFA, 0xFA, 0xFA);
                textSec = Color.FromRgb(0xD4, 0xD4, 0xD8);
                textMuted = Color.FromRgb(0x8E, 0x8E, 0x93);
                accent = Color.FromRgb(0x00, 0x7A, 0xFF);
                ringTrack = Color.FromRgb(0x2A, 0x2A, 0x32);
                break;
        }

        SetBrush(app, "ThemeWindowBackground", winBg);
        SetBrush(app, "ThemeSidebarBackground", sidebarBg);
        SetBrush(app, "ThemeCardBackground", cardBg);
        SetBrush(app, "ThemeBorderBrush", border);
        SetBrush(app, "ThemeRailBackground", railBg);
        SetBrush(app, "ThemeRailBorderBrush", railBorder);
        SetBrush(app, "ThemeTextPrimary", textPrim);
        SetBrush(app, "ThemeTextSecondary", textSec);
        SetBrush(app, "ThemeTextMuted", textMuted);
        SetBrush(app, "ThemeAccent", accent);
        SetBrush(app, "ThemeRingTrack", ringTrack);

        foreach (Window window in app.Windows)
        {
            if (window.AllowsTransparency) continue; // Skip transparent floating overlays!

            if (isLight)
            {
                // In light mode, turn off dark mode window title bar
                Win32WindowHelper.DisableImmersiveDarkMode(window);
            }
            else
            {
                Win32WindowHelper.EnableImmersiveDarkMode(window);
            }
        }
    }

    private static void SetBrush(Application app, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        app.Resources[key] = brush;
    }
}
