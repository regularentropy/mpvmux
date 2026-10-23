using System;
using System.IO;
using System.Threading.Tasks;

namespace mpvmux.Services;

/// <summary>
/// Decides what happens to unsaved bundle edits when the document is replaced or the app closes.
/// Autosave is tried first, so a prompt only appears when the edits cannot be written by
/// themselves: autosave is off, the bundle has no destination yet, or the write failed.
/// </summary>
internal interface IUnsavedChangesGuard
{
    /// <summary>
    /// Completes with true when the caller may go ahead. False means the user cancelled (or a
    /// save failed), so the current bundle must be kept open.
    /// </summary>
    /// <param name="action">Continues the question, e.g. "closing mpvmux".</param>
    Task<bool> ConfirmProceedAsync(string action);
}

internal sealed class UnsavedChangesGuard(
    IBundleEditTracker editTracker,
    IBundleAutoSaveService autoSave,
    IBundleFileService bundleFileService,
    IBundleRepository bundleRepository,
    IFilePickerService filePickerService,
    IWindowService windowService,
    IDialogHelper dialogHelper) : IUnsavedChangesGuard
{
    public async Task<bool> ConfirmProceedAsync(string action)
    {
        if (!editTracker.IsDirty)
            return true;

        // autosave would have written these edits, so give it the chance before asking
        if (await autoSave.FlushAsync())
            return true;

        if (!editTracker.IsDirty)
            return true;

        var choice = await windowService.ShowUnsavedChangesAsync(
            "Unsaved changes",
            $"Save changes to {DescribeBundle()} before {action}?");

        return choice switch
        {
            UnsavedChangesChoice.Cancel => false,
            UnsavedChangesChoice.Discard => true,
            _ => await SaveAsync(),
        };
    }

    private async Task<bool> SaveAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(bundleRepository.SelectedFilePath))
            {
                // same rule as File > Save: a bundle without a destination asks for one
                var path = await filePickerService.SaveFilePickerAsync();
                if (path is null)
                    return false;

                await bundleFileService.SaveAsAsync(path);
                return true;
            }

            await bundleFileService.SaveAsync();
            return true;
        }
        catch (Exception ex) when (ex is BundleSaveException or IOException or UnauthorizedAccessException)
        {
            dialogHelper.ShowError($"Could not save the bundle: {ex.Message}");
            return false;
        }
    }

    private string DescribeBundle()
    {
        var path = bundleRepository.SelectedFilePath;

        return string.IsNullOrEmpty(path)
            ? "this bundle"
            : $"'{Path.GetFileNameWithoutExtension(path)}'";
    }
}
