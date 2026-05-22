using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace MyAdventure.Android;

/// <summary>
/// Android Application class. Required by Avalonia 12 — AppBuilder
/// customization (like WithInterFont) now lives here, on a class deriving
/// from AvaloniaAndroidApplication&lt;TApp&gt; with the [Application] attribute.
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
