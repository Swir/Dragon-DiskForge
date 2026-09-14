using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace DragonDiskForge.App.Views;

public sealed partial class ExplorerView
{
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

            if (item.IsReparsePoint)
            {
                args.Cancel = true;
                ShowStatus("Drag-out is blocked for reparse points and junctions.", InfoBarSeverity.Warning);
                return;
            }

            var sourcePath = Path.GetFullPath(item.FullPath);
            if (!IsInsideRoot(_rootPath, sourcePath))
            {
                args.Cancel = true;
                ShowStatus("Drag-out was blocked because the item is outside the mounted root.", InfoBarSeverity.Warning);
                return;
            }

            var exists = item.IsDirectory ? Directory.Exists(sourcePath) : File.Exists(sourcePath);
            if (!exists)
            {
                args.Cancel = true;
                ShowStatus("The dragged item is no longer available. Refresh Dragon Explorer.", InfoBarSeverity.Warning);
                return;
            }

            try
            {
                if ((File.GetAttributes(sourcePath) & System.IO.FileAttributes.ReparsePoint) != 0)
                {
                    args.Cancel = true;
                    ShowStatus("Drag-out was blocked because the item became a reparse point.", InfoBarSeverity.Warning);
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                args.Cancel = true;
                ShowStatus($"Could not validate drag-out safety: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
                return;
            }

            IStorageItem storageItem = item.IsDirectory
                ? await StorageFolder.GetFolderFromPathAsync(sourcePath)
                : await StorageFile.GetFileFromPathAsync(sourcePath);

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
