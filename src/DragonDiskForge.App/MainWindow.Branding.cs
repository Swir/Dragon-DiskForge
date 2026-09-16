using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DragonDiskForge.App;

public sealed partial class MainWindow
{
    public void ConfigureBrandingFooter()
    {
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(8, 4, 8, 8)
        };
        var credit = new TextBlock
        {
            Text = "by Swir",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DragonMutedBrush"]
        };
        var link = new HyperlinkButton
        {
            Content = "GitHub",
            Padding = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(link, "Open Swir GitHub profile");
        link.Click += async (_, _) =>
            await global::Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/Swir"));
        footer.Children.Add(credit);
        footer.Children.Add(link);
        ShellNav.PaneFooter = footer;
    }
}
