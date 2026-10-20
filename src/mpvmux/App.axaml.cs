using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using mpvmux.Constants;
using mpvmux.Services;
using mpvmux.ViewModels;
using mpvmux.Views;
using System.IO;
using System.Net.Http;

namespace mpvmux;

public partial class App : Application
{
    private IConfigService _configService;
    private IHistoryService _historyService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Directory.CreateDirectory(AppConstants.BaseFolderPath);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {

            var collection = new ServiceCollection();
            collection.AddCommonServices();

            var mainWindow = new MainWindow();

            var services = collection.BuildServiceProvider();

            _historyService = services.GetRequiredService<IHistoryService>();
            _configService = services.GetRequiredService<IConfigService>();

            var vm = services.GetRequiredService<MainWindowViewModel>();
            
            mainWindow.DataContext = vm;

            desktop.MainWindow = mainWindow;

            mainWindow.Loaded += async (_, _) => await vm.InitializeAsync();

            mainWindow.Closing += OnWindowClosing;

        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnWindowClosing(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
    {
        _historyService.SaveHistory();
        _configService.Save();
    }
}

public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton<IBundleRepository, BundleRepository>();
        collection.AddSingleton<IUpdateService, UpdateService>();
        collection.AddSingleton<IBundleFileService, BundleFileService>();
        collection.AddSingleton<IPlayerService, PlayerService>();
        collection.AddSingleton<IDialogHelper, DialogHelper>();

        // one long-lived client: sockets are reused instead of a pool per update check
        collection.AddSingleton(new HttpClient());

        collection.AddSingleton<IWindowService, WindowService>();
        collection.AddSingleton<IFilePickerService, FilePickerService>();
        collection.AddSingleton<IConfigService, ConfigService>();
        collection.AddSingleton<IHistoryService, HistoryService>();

        collection.AddSingleton<BundleSession>();
        collection.AddSingleton<AppMetadata>();

        collection.AddSingleton<HistoryViewModel>();
        collection.AddSingleton<FileMenuViewModel>();
        collection.AddSingleton<TopMenuViewModel>();
        collection.AddSingleton<BottomPanelViewModel>();
        collection.AddSingleton<AboutWindowViewModel>();

        collection.AddTransient<UpdaterViewModel>();
        collection.AddTransient<PreferencesWindowViewModel>();

        collection.AddSingleton<MainWindowViewModel>();
    }
}