using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace MyAdventure.Android;

[Application]
public sealed class AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<AndroidGameApp>(javaReference, transfer)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
