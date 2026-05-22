using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace MyAdventure.Android;

/// <summary>
/// Android Application class. Required by Avalonia 12 — AppBuilder
/// customization (such as <c>WithInterFont()</c>) was previously hooked
/// onto <c>AvaloniaMainActivity{TApp}</c>'s <c>CustomizeAppBuilder</c>,
/// but in v12 that generic activity type no longer exists and those virtual
/// methods are no longer called by the framework. All AppBuilder configuration
/// now lives here, on a class deriving from
/// <see cref="AvaloniaAndroidApplication{TApp}"/> and decorated with
/// <see cref="ApplicationAttribute"/>. <c>MainActivity</c> is now empty
/// and inherits from the non-generic <see cref="AvaloniaMainActivity"/>.
///
/// IMPORTANT: do NOT set <c>[Application(Name = "...")]</c> to the package
/// name (e.g. "com.myadventure.app"). That attribute value is the
/// fully-qualified Java class name for the generated Application subclass.
/// Setting it equal to the package name causes a javac collision:
///     class 'app' clashes with package of same name
///     package 'com.myadventure.app' clashes with class of same name
/// because R.java lives in com.myadventure.app, and a class also named
/// com.myadventure.app cannot coexist. Leave Name unset (the build
/// generates a synthetic, collision-free Java name) or use a distinct
/// class name like "com.myadventure.app.MyAdventureApp".
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
