using Microsoft.Extensions.DependencyInjection;
using MyAdventure.Shared;
using MyAdventure.Shared.Feedback;

namespace MyAdventure.Android;

public sealed class AndroidGameApp : App
{
    protected override void ConfigurePlatformServices(IServiceCollection services)
    {
        var context = global::Android.App.Application.Context;
        services.AddSingleton<ISoundPlayer>(_ => new AndroidSoundPlayer(context));
        services.AddSingleton<IHapticPlayer>(_ => new AndroidHapticPlayer(context));
    }
}
