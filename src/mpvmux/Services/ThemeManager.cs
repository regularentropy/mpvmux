using Avalonia;
using Avalonia.Styling;

namespace mpvmux.Services;

internal static class ThemeManager
{
    public static void Apply(bool useDarkTheme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = useDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
