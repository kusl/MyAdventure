using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace MyAdventure.Android;

/// <summary>
/// Android entry-point activity. In Avalonia 12 this is intentionally empty:
/// app initialization and AppBuilder customization moved to AndroidApp.
/// </summary>
[Activity(
    Label = "MyAdventure",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
