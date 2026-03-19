using MudBlazor;

namespace FamOs.Web.Theme;

/// <summary>
/// FAM OS Theme — Sprint 3 restyling.
/// Matches Lauren's IAAPA Portal v2 mockup visual language.
/// Light mode only. MudBlazor v7.
/// </summary>
public static class FipTheme
{
    public static MudTheme Create() => new MudTheme
    {
        PaletteLight = new PaletteLight
        {
            // --- Core Brand ---
            Primary           = "#002050",      // navy — sidebar, appbar, primary buttons
            PrimaryContrastText = "#ffffff",
            Secondary         = "#0090d0",      // sky-blue — active states, highlights
            SecondaryContrastText = "#ffffff",
            Tertiary          = "#f0a010",      // amber — warnings

            // --- Surfaces ---
            Background        = "#f2f4f7",      // --cream page background
            Surface           = "#ffffff",      // card/panel surface
            AppbarBackground  = "#002050",      // matches sidebar navy
            AppbarText        = "#ffffff",

            // --- Drawer/Sidebar ---
            DrawerBackground  = "#002050",
            DrawerText        = "rgba(255,255,255,0.85)",
            DrawerIcon        = "#0090d0",      // sky accent for nav icons

            // --- Text ---
            TextPrimary       = "#3a4250",      // --text body color
            TextSecondary     = "#6b7585",      // --muted
            TextDisabled      = "rgba(58,66,80,0.38)",
            ActionDefault     = "#6b7585",

            // --- Semantic ---
            Success           = "#059669",
            Warning           = "#f0a010",
            Error             = "#DC2626",
            Info              = "#2563EB",

            // --- Table / Structure ---
            TableLines        = "#e2e6ed",      // --border
            TableHover        = "#f8f9fb",
        },

        Typography = new Typography
        {
            Default = new Default
            {
                FontFamily  = new[] { "Plus Jakarta Sans", "system-ui", "-apple-system", "sans-serif" },
                FontSize    = "0.875rem",   // 14px
                FontWeight = 400,
                LineHeight  = 1.5,
                LetterSpacing = "0em",
            },
            H1 = new H1 { FontFamily = new[] { "Fraunces", "Georgia", "serif" }, FontSize = "2rem",    FontWeight = 400 },
            H2 = new H2 { FontFamily = new[] { "Fraunces", "Georgia", "serif" }, FontSize = "1.4375rem", FontWeight = 400, LetterSpacing = "-0.3px" },
            H3 = new H3 { FontFamily = new[] { "Fraunces", "Georgia", "serif" }, FontSize = "1.25rem",  FontWeight = 400 },
            H4 = new H4 { FontFamily = new[] { "Fraunces", "Georgia", "serif" }, FontSize = "1.875rem", FontWeight = 400, LineHeight = 1.1 }, // KPI value
            H5 = new H5 { FontFamily = new[] { "Plus Jakarta Sans", "sans-serif" }, FontWeight = 700, FontSize = "0.78125rem" }, // card titles
            H6 = new H6 { FontFamily = new[] { "Plus Jakarta Sans", "sans-serif" }, FontWeight = 700, FontSize = "0.78125rem" },
            Subtitle1 = new Subtitle1 { FontSize = "0.78125rem", FontWeight = 700, LineHeight = 1.3 },
            Subtitle2 = new Subtitle2 { FontSize = "0.71875rem", FontWeight = 600 },
            Body1 = new Body1 { FontSize = "0.875rem",  FontWeight = 400, LineHeight = 1.5 },
            Body2 = new Body2 { FontSize = "0.71875rem", FontWeight = 400, LineHeight = 1.45, LetterSpacing = "0em" },
            Button = new MudBlazor.Button
            {
                FontFamily    = new[] { "Plus Jakarta Sans", "sans-serif" },
                FontSize      = "0.78125rem",  // 12.5px
                FontWeight = 600,
                TextTransform = "none",
                LetterSpacing = "0em",
            },
            Caption = new Caption { FontSize = "0.6875rem", FontWeight = 400 },
            Overline = new Overline
            {
                FontSize    = "0.59375rem",  // 9.5px
                FontWeight = 700,
                TextTransform = "uppercase",
                LetterSpacing = "1.2px",
            },
        },

        LayoutProperties = new LayoutProperties
        {
            AppbarHeight    = "54px",       // matches mockup topbar height
            DrawerWidthLeft = "262px",      // matches mockup --sidebar-w
        },

        Shadows = new Shadow
        {
            // Minimal shadow system — cards rely on borders, not heavy shadows
            Elevation = new[]
            {
                "none",
                "0 1px 2px rgba(0,0,0,0.05)",
                "0 1px 3px rgba(0,0,0,0.08), 0 1px 2px rgba(0,0,0,0.04)",
                "0 2px 4px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)",
                "0 4px 6px rgba(0,0,0,0.06), 0 2px 4px rgba(0,0,0,0.04)",
                "0 4px 12px rgba(0,144,208,0.10)",   // elevation[5] — card hover (sky tint)
                "0 8px 16px rgba(0,0,0,0.08)",
                "0 12px 24px rgba(0,0,0,0.08)",
                "0 16px 32px rgba(0,0,0,0.08)",
                "0 20px 40px rgba(0,0,0,0.10)",
                "0 24px 48px rgba(0,0,0,0.12)",
                "0 32px 56px rgba(0,0,0,0.12)",
                "0 40px 64px rgba(0,0,0,0.12)",
                "0 48px 72px rgba(0,0,0,0.12)",
                "0 56px 80px rgba(0,0,0,0.12)",
                "0 64px 88px rgba(0,0,0,0.12)",
                "0 72px 96px rgba(0,0,0,0.12)",
                "0 80px 104px rgba(0,0,0,0.12)",
                "0 88px 112px rgba(0,0,0,0.12)",
                "0 96px 120px rgba(0,0,0,0.12)",
                "0 104px 128px rgba(0,0,0,0.12)",
                "0 112px 136px rgba(0,0,0,0.12)",
                "0 120px 144px rgba(0,0,0,0.12)",
                "0 128px 152px rgba(0,0,0,0.12)",
                "0 136px 160px rgba(0,0,0,0.12)",
            }
        },
    };
}
