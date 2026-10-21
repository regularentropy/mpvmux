using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using System;

namespace mpvmux.Services;

internal static class ThemeManager
{
    private static readonly Uri LightLogoUri = new("avares://mpvmux/Assets/logo.png");
    private static readonly Uri DarkLogoUri = new("avares://mpvmux/Assets/logo-dark.png");

    public static void Apply(bool useDarkTheme)
    {
        if (Application.Current is not { } app)
            return;

        app.RequestedThemeVariant = useDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        
        var logoUri = useDarkTheme ? DarkLogoUri : LightLogoUri;

        using (var stream = AssetLoader.Open(logoUri))
            app.Resources["AppLogo"] = new Bitmap(stream);

        using (var stream = AssetLoader.Open(logoUri))
            app.Resources["AppWindowIcon"] = new WindowIcon(stream);
    }
}
