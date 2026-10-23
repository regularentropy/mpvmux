using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace mpvmux.Services;

/// <summary>
/// Writes the open bundle to its selected path a moment after the last edit, when the user has
/// turned autosave on. The bundle is written with <see cref="BundleBackupMode.Keep"/>, so the
/// previous version stays the last deliberate save. Failures are reported through
/// <see cref="Failed"/> instead of a dialog: one per keystroke would be unusable.
/// </summary>
internal interface IBundleAutoSaveService
{
    /// <summary>Raised after an automatic write succeeded.</summary>
    event EventHandler? Saved;

    /// <summary>Raised with a user-facing message when an automatic write failed.</summary>
    event EventHandler<string>? Failed;

    /// <summary>True when autosave is switched on in the config.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Writes pending edits immediately, cancelling the debounce. Completes with true when the
    /// bundle is clean afterwards; false means the caller has to prompt the user.
    /// </summary>
    Task<bool> FlushAsync();

    /// <summary>Drops a pending automatic write without saving.</summary>
    void Cancel();
}

internal sealed class BundleAutoSaveService : IBundleAutoSaveService
{
    private readonly IBundleFileService _bundleFileService;
    private readonly IBundleRepository _bundleRepository;
    private readonly BundleSession _bundleSession;
    private readonly IBundleEditTracker _editTracker;
    private readonly IConfigService _configService;
    private readonly TimeSpan _delay;

    private CancellationTokenSource? _debounce;
    private int _generation;

    public BundleAutoSaveService(
        IBundleFileService bundleFileService,
        IBundleRepository bundleRepository,
        BundleSession bundleSession,
        IBundleEditTracker editTracker,
        IConfigService configService,
        TimeSpan? delay = null)
    {
        _bundleFileService = bundleFileService;
        _bundleRepository = bundleRepository;
        _bundleSession = bundleSession;
        _editTracker = editTracker;
        _configService = configService;
        _delay = delay ?? TimeSpan.FromSeconds(2);

        _editTracker.Edited += OnEdited;
        _bundleSession.PropertyChanged += OnSessionPropertyChanged;
        _configService.Config.PropertyChanged += OnConfigPropertyChanged;
    }

    public event EventHandler? Saved;

    public event EventHandler<string>? Failed;

    public bool IsEnabled => _configService.Config.AutoSaveEnabled;

    public void Cancel()
    {
        _debounce?.Cancel();
        _debounce = null;
    }

    public async Task<bool> FlushAsync()
    {
        Cancel();

        if (!IsEnabled || !_editTracker.IsDirty)
            return !_editTracker.IsDirty;

        if (string.IsNullOrEmpty(_bundleRepository.SelectedFilePath))
            return false;

        return await WriteAsync(_generation);
    }

    private void OnEdited(object? sender, EventArgs e)
    {
        if (!IsEnabled)
            return;

        var generation = Interlocked.Increment(ref _generation);

        Cancel();
        var source = new CancellationTokenSource();
        _debounce = source;

        _ = WriteAfterDelayAsync(generation, source.Token);
    }

    private async Task WriteAfterDelayAsync(int generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(_delay, token);
        }
        catch (OperationCanceledException)
        {
            // a newer edit restarted the debounce, or the pending write was cancelled
            return;
        }

        await WriteAsync(generation);
    }

    /// <summary>
    /// Writes the bundle when it is still the one the edit belonged to. The identity and path
    /// checks matter: a pending write must never land the newly opened bundle in the file of the
    /// bundle that was open when the edit was made.
    /// </summary>
    private async Task<bool> WriteAsync(int generation)
    {
        if (!IsEnabled || !_editTracker.IsDirty)
            return !_editTracker.IsDirty;

        if (generation != Volatile.Read(ref _generation))
            return false;

        var path = _bundleRepository.SelectedFilePath;
        if (string.IsNullOrEmpty(path))
            return false;

        try
        {
            await _bundleFileService.SaveAsync(BundleBackupMode.Keep);
        }
        catch (Exception ex) when (ex is BundleSaveException or IOException or UnauthorizedAccessException)
        {
            Failed?.Invoke(this, $"Could not save automatically: {ex.Message}");
            return false;
        }

        Saved?.Invoke(this, EventArgs.Empty);
        return !_editTracker.IsDirty;
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // the pending write belonged to the bundle that is being replaced
        if (e.PropertyName == nameof(BundleSession.Bundle))
            Cancel();
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Models.ConfigModel.AutoSaveEnabled))
            return;

        if (!IsEnabled)
        {
            Cancel();
            return;
        }

        // turning autosave on with unsaved edits should save them without waiting for a new edit
        if (_editTracker.IsDirty)
            OnEdited(this, EventArgs.Empty);
    }
}
