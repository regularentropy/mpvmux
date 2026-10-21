using Avalonia.Controls;
using Avalonia.Platform.Storage;
using mpvmux.Constants;
using mpvmux.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IFilePickerService
{
    Task<List<MediaRecord>?> GetFilesFromFolder(string[] extensions);
    Task<string?> PickMediaFileAsync(string[] extensions);
    Task<string?> OpenFilePickerAsync();
    Task<string?> SaveFilePickerAsync();
    Task<string?> PickExecutableAsync();
}

internal class FilePickerService : IFilePickerService
{
    private static Window Owner => Desktop.OwnerWindow;

    public async Task<string?> OpenFilePickerAsync()
    {
        var file = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
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
        var file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
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

    public async Task<string?> PickExecutableAsync()
    {
        var file = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            AllowMultiple = false,
            // the app ships for Windows; elsewhere mpv is a bare binary with no extension
            FileTypeFilter =
            [
                new FilePickerFileType("Executable")
                {
                    Patterns = OperatingSystem.IsWindows() ? ["*.exe"] : ["*"]
                }
            ]
        });
        if (file.Count == 0) return null;
        return file[0].Path.LocalPath;
    }

    public async Task<string?> PickMediaFileAsync(string[] extensions)
    {
        var file = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Supported files")
                {
                    Patterns = extensions.Select(extension => $"*{extension}").ToArray()
                }
            ]
        });
        if (file.Count == 0) return null;
        return file[0].Path.LocalPath;
    }

    public async Task<List<MediaRecord>?> GetFilesFromFolder(string[] extensions)
    {
        var folder = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions() { AllowMultiple = false });
        if (folder.Count == 0) return null;

        var path = folder[0].Path.LocalPath;
        var allowed = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        
        return await Task.Run(() => FindMediaFiles(path, allowed));
    }

    internal static List<MediaRecord> FindMediaFiles(string folder, HashSet<string> allowedExtensions)
    {
        return Directory
            .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(f => allowedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => new MediaRecord(Path.GetFileNameWithoutExtension(f), f))
            .ToList();
    }
}
