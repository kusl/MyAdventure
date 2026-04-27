using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace MyAdventure.Android;

/// <summary>
/// Android Application class. Required by Avalonia 12 — AppBuilder
/// customization (such as <c>WithInterFont()</c>) was previously hooked
/// onto <c>AvaloniaMainActivity&lt;TApp&gt;</c>'s <c>CustomizeAppBuilder</c>,
/// but in v12 that generic activity type no longer exists and those virtual
/// methods are no longer called by the framework. All AppBuilder configuration
/// now lives here, on a class deriving from
/// <see cref="AvaloniaAndroidApplication{TApp}"/> and decorated with
/// <see cref="ApplicationAttribute"/>. <c>MainActivity</c> is now empty
/// and inherits from the non-generic <see cref="AvaloniaMainActivity"/>.
///
/// See: https://docs.avaloniaui.net/docs/avalonia12-breaking-changes
/// </summary>
[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder)
            .WithInterFont();
}
