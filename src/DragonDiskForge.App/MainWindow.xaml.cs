using System.Security.Cryptography;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DragonDiskForge.App;

public sealed partial class MainWindow : Window
{
    private readonly ImageDetectionService _detector = new();
    private DiskImageInfo? _current;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
    }

    private async void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var extension in SupportedFormats.All.SelectMany(x => x.Extensions).Distinct(StringComparer.OrdinalIgnoreCase))
            picker.FileTypeFilter.Add(extension);
        picker.FileTypeFilter.Add("*");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            await LoadImageAsync(file.Path);
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Open in Dragon DiskForge";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.OfType<StorageFile>().FirstOrDefault();
        if (file is not null)
            await LoadImageAsync(file.Path);
    }

    private async Task LoadImageAsync(string path)
    {
        try
        {
            _current = await _detector.InspectAsync(path);
            ImageNameText.Text = _current.FileName;
            ImageMetaText.Text = $"{_current.Format}  •  {_current.SizeDisplay}  •  {_current.DetectionMethod}";
            ImagePathText.Text = _current.Path;
            MountButton.IsEnabled = false; // Enabled when the milestone-2 mount service lands.
            ResultCard.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("Could not open image", ex.Message);
        }
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null)
            return;

        try
        {
            using var sha256 = SHA256.Create();
            await using var stream = File.OpenRead(_current.Path);
            var hash = await sha256.ComputeHashAsync(stream);
            var value = Convert.ToHexString(hash);
            await ShowDialogAsync("SHA-256", value);
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("Verification failed", ex.Message);
        }
    }

    private async Task ShowDialogAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Close",
            XamlRoot = ShellNav.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
