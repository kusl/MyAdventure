using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace MyAdventure.Android;

/// <summary>
/// Android entry-point activity. In Avalonia 12 this is now intentionally
/// empty: app initialization and AppBuilder customization moved to
/// <see cref="AndroidApp"/>. The activity itself just declares its
/// Android metadata via the <see cref="ActivityAttribute"/>.
///
/// Note: this inherits from the non-generic <see cref="AvaloniaMainActivity"/>;
/// the old generic <c>AvaloniaMainActivity&lt;App&gt;</c> form is no longer the
/// recommended pattern in v12, because the framework no longer invokes the
/// activity's <c>CreateAppBuilder</c>/<c>CustomizeAppBuilder</c> hooks.
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
