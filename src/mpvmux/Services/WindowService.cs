using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using mpvmux.ViewModels;
using mpvmux.Views;
using System;

namespace mpvmux.Services;

internal interface IWindowService
{
    void ShowMessage(string title, string text);
    void ShowPreferences();
    void ShowAbout();
    void ShowUpdater(UpdateResponse result);
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
