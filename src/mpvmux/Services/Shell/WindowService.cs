using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using mpvmux.ViewModels;
using mpvmux.Views;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace mpvmux.Services;

/// <summary>What the user chose for a bundle that has changes which are not on disk.</summary>
internal enum UnsavedChangesChoice
{
    /// <summary>Closing the dialog counts as this, so an accidental close never discards work.</summary>
    Cancel = 0,
    Save,

    /// <summary>Proceed without writing the bundle.</summary>
    Discard,
}

internal interface IWindowService
{
    void ShowMessage(string title, string text);

    /// <summary>
    /// Asks the user to confirm an action. Completes with true for OK, false for cancel.
    /// </summary>
    Task<bool> ShowConfirmAsync(string title, string text);

    /// <summary>
    /// Asks what to do with unsaved bundle changes: save them, discard them or stay put.
    /// </summary>
    Task<UnsavedChangesChoice> ShowUnsavedChangesAsync(string title, string text);
    void ShowPreferences();
    void ShowAbout();
    void ShowUpdater(UpdateResponse result);

    /// <summary>
    /// Asks for a new name for a recent bundle. Completes with the trimmed name, or null when
    /// the user cancels.
    /// </summary>
    Task<string?> ShowRenameAsync(string currentName);

    /// <summary>Shows the Manage recent bundles window.</summary>
    void ShowRecentBundles();

    /// <summary>
    /// Asks which of several dropped bundles to open. Completes with the chosen path, or null
    /// when the user cancels; the caller keeps every dropped bundle in the recent list.
    /// </summary>
    Task<string?> ShowBundleChoiceAsync(IReadOnlyList<string> paths);

    /// <summary>
    /// Shows the review window for missing media. Completes with the confirmed replacements, or
    /// null when the user cancels; an empty list means everything was rejected.
    /// </summary>
    Task<IReadOnlyList<MediaRelinkSelection>?> ShowRelinkMediaAsync(
        IReadOnlyList<MediaRelinkProposal> proposals);

    /// <summary>Shows the mpv setup window and completes when the user closes it.</summary>
    Task ShowSetupAsync();
}

internal class WindowService(IServiceProvider serviceProvider) : IWindowService
{
    public void ShowMessage(string title, string text)
    {
        ShowDialog(new MessageBoxWindow(title, text), null);
    }

    public Task<bool> ShowConfirmAsync(string title, string text)
    {
        var window = new ConfirmWindow(title, text);
        return window.ShowDialog<bool>(Desktop.OwnerWindow);
    }

    public Task<UnsavedChangesChoice> ShowUnsavedChangesAsync(string title, string text)
    {
        var window = new UnsavedChangesWindow(title, text);
        return window.ShowDialog<UnsavedChangesChoice>(Desktop.OwnerWindow);
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

    public void ShowRecentBundles()
    {
        var viewModel = serviceProvider.GetRequiredService<RecentBundlesViewModel>();
        ShowDialog(new RecentBundlesWindow(), viewModel);
    }

    public Task<string?> ShowBundleChoiceAsync(IReadOnlyList<string> paths)
    {
        var viewModel = serviceProvider.GetRequiredService<BundleChoiceViewModel>();
        viewModel.Initialize(paths);

        var window = new BundleChoiceWindow { DataContext = viewModel };
        return window.ShowDialog<string?>(Desktop.OwnerWindow);
    }

    public Task<IReadOnlyList<MediaRelinkSelection>?> ShowRelinkMediaAsync(
        IReadOnlyList<MediaRelinkProposal> proposals)
    {
        var viewModel = serviceProvider.GetRequiredService<RelinkMediaViewModel>();
        viewModel.Initialize(proposals);

        var window = new RelinkMediaWindow { DataContext = viewModel };
        return window.ShowDialog<IReadOnlyList<MediaRelinkSelection>?>(Desktop.OwnerWindow);
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
