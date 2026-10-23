using Avalonia;
using mpvmux.Services;
using System;
using System.Collections.Generic;

namespace mpvmux
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            var bundlePaths = LaunchArguments.BundlePaths(args);

            // Taken before Avalonia starts, so a second instance never flashes a window. The
            // handle is held until the app exits, which is what keeps the lock alive.
            if (!SingleInstanceGuard.TryAcquire(out var instance))
            {
                // Another instance owns the data folder. Hand it the bundles to open, or tell
                // the user why this launch does nothing.
                using var channel = InstanceChannel.ForCurrentUser();

                if (SecondLaunch.ShouldShowAlreadyRunning(
                        bundlePaths,
                        path => channel.SendBundlePathAsync(path).GetAwaiter().GetResult()))
                {
                    App.ShowAlreadyRunningMessage = true;
                }
                else
                {
                    return;
                }
            }

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                instance?.Dispose();
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
