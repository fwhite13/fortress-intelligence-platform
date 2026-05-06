using MudBlazor;

namespace FortressAI.V2.Web.Theme;

/// <summary>
/// FAIT v2 — Fortress brand theme with dark/light toggle support.
/// Brand colors: Blue #0066CC, Dark #1A1A2E, Gold #d4af37
/// </summary>
public static class FipTheme
{
    public static MudTheme Create() => new MudTheme
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#0066CC",
            PrimaryContrastText = "#ffffff",
            Secondary = "#d4af37",
            SecondaryContrastText = "#1A1A2E",
            Background = "#f8f9fa",
            Surface = "#ffffff",
            AppbarBackground = "#1A1A2E",
            AppbarText = "#ffffff",
            DrawerBackground = "#1A1A2E",
            DrawerText = "#f0f0f0",
            DrawerIcon = "#d4af37",
            TextPrimary = "#1A1A2E",
            TextSecondary = "#6b7280",
            TextDisabled = "rgba(0,0,0,0.38)",
            ActionDefault = "#6b7280",
            Success = "#059669",
            Warning = "#d97706",
            Error = "#dc2626",
            Info = "#0066CC",
            TableLines = "#e5e7eb",
            TableHover = "#f3f4f6",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#0066CC",
            PrimaryContrastText = "#ffffff",
            Secondary = "#d4af37",
            SecondaryContrastText = "#1A1A2E",
            Background = "#0d0d1a",
            Surface = "#1A1A2E",
            AppbarBackground = "#0d0d1a",
            AppbarText = "#f0f0f0",
            DrawerBackground = "#0d0d1a",
            DrawerText = "#f0f0f0",
            DrawerIcon = "#d4af37",
            TextPrimary = "#f0f0f0",
            TextSecondary = "#9ca3af",
            TextDisabled = "rgba(255,255,255,0.38)",
            ActionDefault = "#9ca3af",
            Success = "#34d399",
            Warning = "#fbbf24",
            Error = "#f87171",
            Info = "#60a5fa",
            TableLines = "#374151",
            TableHover = "#1f2937",
        },
        Typography = new Typography
        {
            Default = new Default
            {
                FontFamily = new[] { "Inter", "system-ui", "-apple-system", "sans-serif" },
                FontSize = "0.9375rem",
                LineHeight = 1.6,
            },
            H4 = new H4 { FontWeight = 700 },
            H5 = new H5 { FontWeight = 600 },
            H6 = new H6 { FontWeight = 600 },
            Button = new MudBlazor.Button
            {
                FontFamily = new[] { "Inter", "sans-serif" },
                FontWeight = 500,
                TextTransform = "none",
                FontSize = "0.9rem",
            },
            Caption = new Caption { FontSize = "0.75rem" }
        },
        LayoutProperties = new LayoutProperties
        {
            AppbarHeight = "56px",
            DrawerWidthLeft = "260px",
        }
    };
}
