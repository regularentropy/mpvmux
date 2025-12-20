using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using mpvmux.Constants;
using mpvmux.Models;
using mpvmux.Services;
using mpvmux.ViewModels;
using mpvmux.Views;
using System.IO;

namespace mpvmux;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Directory.CreateDirectory(AppConstants.BaseFolderPath);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Line below is needed to remove Avalonia data validation.
            // Without this line you will get duplicate validations from both Avalonia and CT
            BindingPlugins.DataValidators.RemoveAt(0);

            var collection = new ServiceCollection();
            collection.AddCommonServices();

            var mainWindow = new MainWindow();

            collection.AddSingleton<IWindowService>(
                x => new WindowService(mainWindow, x)
            );

            collection.AddSingleton<IFilePickerService>(
                _ => new FilePickerService(mainWindow)
            );

            var services = collection.BuildServiceProvider();

            var vm = services.GetRequiredService<MainWindowViewModel>();


            mainWindow.DataContext = vm;

            desktop.MainWindow = mainWindow;

            mainWindow.Loaded += async (_, _) => await vm.InitializeAsync();

        }

        base.OnFrameworkInitializationCompleted();
    }
}

public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton<IVideoRepositoryService, VideoRepositoryService>();
        collection.AddSingleton<IConfigService, ConfigService>();
        collection.AddSingleton<IHistoryService, HistoryService>();
        collection.AddSingleton<IUpdateService, UpdateService>();
        collection.AddSingleton<IBundleFileService, BundleFileService>();
        collection.AddSingleton<IPlayerService, PlayerService>();

        collection.AddSingleton<HistoryModel>();
        collection.AddSingleton<MediaContext>();
        collection.AddSingleton<MetadataRepository>();

        collection.AddSingleton<TopMenuViewModel>();
        collection.AddSingleton<BottomPanelViewModel>();
        collection.AddSingleton<AboutWindowViewModel>();

        collection.AddTransient<UpdaterViewModel>();

        collection.AddSingleton<MainWindowViewModel>();
    }
}