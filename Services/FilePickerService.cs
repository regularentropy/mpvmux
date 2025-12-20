using Avalonia.Controls;
using Avalonia.Platform.Storage;
using mpvmux.Models;
using mpvmux.Constants;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IFilePickerService
{
    Task<List<MediaRecord>?> GetFilesFromFolder();
    Task<string?> OpenFilePickerAsync();
    Task<string?> SaveFilePickerAsync();
}

internal class FilePickerService(Window window) : IFilePickerService
{
    public async Task<string?> OpenFilePickerAsync()
    {
        var file = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("MPVM Files")
                {
                    Patterns = [$"*{AppConstants.FileExtension}"]
                }
            ]
        });
        if (file.Count == 0) return null;
        return file[0].Path.LocalPath;
    }

    public async Task<string?> SaveFilePickerAsync()
    {
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            DefaultExtension = AppConstants.FileExtension,
            FileTypeChoices =
            [
                new FilePickerFileType("MPVM Files")
                {
                    Patterns = [$"*{AppConstants.FileExtension}"]
                }
            ]
        });
        return file?.Path.LocalPath;
    }

    public async Task<List<MediaRecord>?> GetFilesFromFolder()
    {
        var folder = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions() { AllowMultiple = false });
        if (folder.Count == 0) return null;
        var files = Directory.GetFiles(folder[0].Path.LocalPath);
        return files.Select(f => new MediaRecord(Path.GetFileNameWithoutExtension(f), f)).ToList();
    }
}
