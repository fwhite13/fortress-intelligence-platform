using MudBlazor;

namespace FortressIntelligenceRM.Web.Theme;

/// <summary>
/// Fortress Intelligence Platform unified theme — MudBlazor v7 compatible.
/// Matches FORMS FipTheme exactly, namespace updated for FIRM.
/// </summary>
public static class FipTheme
{
    public static MudTheme Create() => new MudTheme
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1a2332",
            PrimaryContrastText = "#ffffff",
            Secondary = "#d4af37",
            SecondaryContrastText = "#1a2332",
            Background = "#f8f9fa",
            Surface = "#ffffff",
            AppbarBackground = "#1a2332",
            AppbarText = "#ffffff",
            DrawerBackground = "#1a2332",
            DrawerText = "#f0f0f0",
            DrawerIcon = "#d4af37",
            TextPrimary = "#1a2332",
            TextSecondary = "#6b7280",
            TextDisabled = "rgba(0,0,0,0.38)",
            ActionDefault = "#6b7280",
            Success = "#059669",
            Warning = "#d97706",
            Error = "#dc2626",
            Info = "#2563eb",
            TableLines = "#e5e7eb",
            TableHover = "#f3f4f6",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#d4af37",
            PrimaryContrastText = "#1a2332",
            Secondary = "#d4af37",
            SecondaryContrastText = "#1a2332",
            Background = "#0f1923",
            Surface = "#1a2332",
            AppbarBackground = "#1E293B",
            AppbarText = "#ffffff",
            DrawerBackground = "#1A2035",
            DrawerText = "#f0f0f0",
            DrawerIcon = "#d4af37",
            TextPrimary = "#e8edf2",
            TextSecondary = "#8899aa",
            TextDisabled = "rgba(255,255,255,0.38)",
            ActionDefault = "#8899aa",
            Success = "#059669",
            Warning = "#d97706",
            Error = "#dc2626",
            Info = "#2563eb",
            TableLines = "#2a3a4a",
            TableHover = "#1e2d3d",
            LinesDefault = "#2a3a4a",
            LinesInputs = "#3a4a5a",
            Divider = "#2a3a4a",
            DividerLight = "rgba(255,255,255,0.12)",
            OverlayLight = "rgba(255,255,255,0.05)",
            OverlayDark = "rgba(0,0,0,0.5)",
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
            AppbarHeight = "48px",
            DrawerWidthLeft = "264px",
        }
    };
}
