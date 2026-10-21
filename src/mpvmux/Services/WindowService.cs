using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using mpvmux.ViewModels;
using mpvmux.Views;
using System;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IWindowService
{
    void ShowMessage(string title, string text);
    void ShowPreferences();
    void ShowAbout();
    void ShowUpdater(UpdateResponse result);

    /// <summary>
    /// Asks for a new name for a recent bundle. Completes with the trimmed name, or null when
    /// the user cancels.
    /// </summary>
    Task<string?> ShowRenameAsync(string currentName);

    /// <summary>Shows the mpv setup window and completes when the user closes it.</summary>
    Task ShowSetupAsync();
}

internal class WindowService(IServiceProvider serviceProvider) : IWindowService
{
    public void ShowMessage(string title, string text)
    {
        ShowDialog(new MessageBoxWindow(title, text), null);
    }

    public void ShowPreferences()
    {
        ShowDialog(new PreferencesWindow(), serviceProvider.GetRequiredService<PreferencesWindowViewModel>());
    }

    public void ShowAbout()
    {
        ShowDialog(new AboutWindow(), serviceProvider.GetRequiredService<AboutWindowViewModel>());
    }

    public void ShowUpdater(UpdateResponse result)
    {
        var viewModel = new UpdaterViewModel(result, serviceProvider.GetRequiredService<AppMetadata>());
        ShowDialog(new UpdaterWindow(), viewModel);
    }

    public Task<string?> ShowRenameAsync(string currentName)
    {
        var window = new RenameWindow(currentName);
        return window.ShowDialog<string?>(Desktop.OwnerWindow);
    }

    public async Task ShowSetupAsync()
    {
        var viewModel = serviceProvider.GetRequiredService<SetupWindowViewModel>();
        var window = new SetupWindow { DataContext = viewModel };
        await window.ShowDialog(Desktop.OwnerWindow);
    }

    private static void ShowDialog(Window window, ViewModelBase? viewModel)
    {
        if (viewModel is not null) window.DataContext = viewModel;
        _ = window.ShowDialog(Desktop.OwnerWindow);
    }
}

internal static class Desktop
{
    public static Window OwnerWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
        ?? throw new InvalidOperationException("No main window; window services are only usable after startup.");
}
