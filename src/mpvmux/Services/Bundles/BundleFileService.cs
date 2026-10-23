﻿using mpvmux.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IBundleFileService
{
    Task<BundleLoadResult> LoadAsync(string path);
    Task<BundleLoadResult> RestoreBackupAsync(string path);
    void RememberBundles(IReadOnlyList<string> paths);
    void NewFile();
    Task<BundleSaveResult> SaveAsAsync(string path);
    Task<BundleSaveResult> SaveAsync();
}

internal class BundleFileService : IBundleFileService
{
    private readonly IBundleRepository _bundleRepository;
    private readonly BundleSession _bundleSession;
    private readonly IHistoryService _historyService;

    public BundleFileService(
        IBundleRepository bundleRepository,
        BundleSession bundleSession,
        IHistoryService historyService)
    {
        _bundleRepository = bundleRepository;
        _bundleSession = bundleSession;
        _historyService = historyService;
    }

    public Task<BundleSaveResult> SaveAsync() => _bundleRepository.SaveAsync(_bundleSession.Bundle);

    public async Task<BundleSaveResult> SaveAsAsync(string path)
    {
        var result = await _bundleRepository.SaveAsAsync(_bundleSession.Bundle, path);
        Remember(path);
        _historyService.SetLastDatabasePath(path);
        return result;
    }

    public async Task<BundleLoadResult> LoadAsync(string path)
    {
        var result = await _bundleRepository.LoadFromFileAsync(path);

        // only a validated bundle may replace the current session; a failed load leaves the
        // user's open work exactly as it was
        if (!result.IsSuccess)
            return result;

        var bundle = result.Bundle!;

        // Fingerprints are captured on open as well as on save, so a rename that happens before
        // the next save can still be recognised. The bundle is not attached to the session yet,
        // so the (bounded) read can run off the UI thread.
        await Task.Run(() => MediaFileFingerprint.Capture(bundle));

        _bundleSession.Bundle = bundle;

        // opening a .mpvm the user has not opened before adds it to history; repeats are a no-op
        Remember(path);
        _historyService.SetLastDatabasePath(path);
        _historyService.ClearCorrupt(path);

        return result;
    }

    public async Task<BundleLoadResult> RestoreBackupAsync(string path)
    {
        var result = await _bundleRepository.RestoreBackupAsync(path);
        if (!result.IsSuccess)
            return result;

        var bundle = result.Bundle!;
        await Task.Run(() => MediaFileFingerprint.Capture(bundle));

        _bundleSession.Bundle = bundle;
        Remember(path);
        _historyService.ClearCorrupt(path);

        // recovered content opens as unsaved, so Save asks for a destination instead of
        // silently overwriting the file that was just repaired
        _bundleRepository.ClearSelectedFile();

        return result;
    }

    public void RememberBundles(IReadOnlyList<string> paths)
    {
        // AddEntry puts each bundle at the top of the recent group, so register in reverse to
        // keep the drop order; opening the first bundle afterwards leaves it at the top.
        for (var i = paths.Count - 1; i >= 0; i--)
            Remember(paths[i]);
    }

    public void NewFile()
    {
        _bundleSession.Bundle = new MediaBundleModel();
        _bundleRepository.ClearSelectedFile();
    }

    private void Remember(string path) =>
        _historyService.AddEntry(new MediaRecord(Path.GetFileNameWithoutExtension(path), path));
}
