using DragonDiskForge.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace DragonDiskForge.App.Views;

public sealed partial class ExplorerView
{
    private readonly ExplorerDragOutValidator _dragOutValidator = new();

    private async void ExplorerItem_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (sender is not FrameworkElement element
                || element.DataContext is not ExplorerEntryViewModel item
                || string.IsNullOrWhiteSpace(_rootPath))
            {
                args.Cancel = true;
                return;
            }

            string sourcePath;
            try
            {
                sourcePath = _dragOutValidator.Validate(
                    _rootPath,
                    item.FullPath,
                    item.IsDirectory,
                    item.IsReparsePoint);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                       or FileNotFoundException
                                       or IOException
                                       or UnauthorizedAccessException)
            {
                args.Cancel = true;
                ShowStatus($"Drag-out blocked: {ShortMessage(ex.Message)}", InfoBarSeverity.Warning);
                return;
            }

            IStorageItem storageItem = item.IsDirectory
                ? await StorageFolder.GetFolderFromPathAsync(sourcePath)
                : await StorageFile.GetFileFromPathAsync(sourcePath);

            try
            {
                sourcePath = _dragOutValidator.ValidateResolvedStorageItem(
                    _rootPath,
                    sourcePath,
                    storageItem.Path,
                    item.IsDirectory,
                    storageItem is StorageFolder,
                    item.IsReparsePoint);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                       or FileNotFoundException
                                       or DirectoryNotFoundException
                                       or IOException
                                       or UnauthorizedAccessException)
            {
                args.Cancel = true;
                ShowStatus($"Drag-out blocked after storage resolution: {ShortMessage(ex.Message)}", InfoBarSeverity.Warning);
                return;
            }

            args.Data.Properties.Title = item.Name;
            args.Data.Properties.Description = "Copy from Dragon DiskForge mounted image";
            args.Data.SetStorageItems(new[] { storageItem }, readOnly: true);
            args.Data.RequestedOperation = DataPackageOperation.Copy;
            args.AllowedOperations = DataPackageOperation.Copy;
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            ShowStatus($"Could not start drag-out: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
