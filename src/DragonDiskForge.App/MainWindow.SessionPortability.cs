using DragonDiskForge.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DragonDiskForge.App;

public sealed partial class MainWindow
{
    private readonly ApplicationPortabilityService _applicationPortability =
        new(GetApplicationStatePath());
    private bool _sessionPortabilityEnabled;
    private long _imagePathCallbackToken;
    private string? _startupImagePath;

    internal void EnableSessionPortability(string? startupImagePath = null)
    {
        if (_sessionPortabilityEnabled)
            return;

        _sessionPortabilityEnabled = true;
        _startupImagePath = startupImagePath;
        RootLayout.Loaded += RestoreSavedSessionAsync;
        _imagePathCallbackToken = ImagePathText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => PersistCurrentSessionPath());
        Closed += (_, _) =>
        {
            if (_imagePathCallbackToken != 0)
            {
                ImagePathText.UnregisterPropertyChangedCallback(
                    TextBlock.TextProperty,
                    _imagePathCallbackToken);
                _imagePathCallbackToken = 0;
            }
        };
    }

    private async void RestoreSavedSessionAsync(object sender, RoutedEventArgs e)
    {
        RootLayout.Loaded -= RestoreSavedSessionAsync;

        try
        {
            if (!string.IsNullOrWhiteSpace(_startupImagePath))
            {
                var startupPath = _startupImagePath;
                _startupImagePath = null;
                if (File.Exists(startupPath))
                {
                    await LoadImageAsync(startupPath);
                    return;
                }
            }

            var state = await _applicationPortability.LoadAsync();
            if (!state.Settings.RestoreLastImage || string.IsNullOrWhiteSpace(state.Session.LastImagePath))
                return;

            var path = state.Session.LastImagePath;
            if (!File.Exists(path))
            {
                await _applicationPortability.RecordLastImageAsync(null);
                return;
            }

            await LoadImageAsync(path);
        }
        catch
        {
            // Corrupt/unavailable session state or launch input must never prevent the main window from starting.
        }
    }

    private async void PersistCurrentSessionPath()
    {
        var path = _current?.Path;
        if (string.IsNullOrWhiteSpace(path)
            || !string.Equals(ImagePathText.Text, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await _applicationPortability.RecordLastImageAsync(path);
        }
        catch
        {
            // Session persistence is best-effort and must never block image inspection.
        }
    }

    internal static string GetApplicationStatePath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DragonDiskForge");
        return Path.Combine(root, "app-state.json");
    }
}
