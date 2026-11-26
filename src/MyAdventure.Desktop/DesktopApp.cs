using Microsoft.Extensions.DependencyInjection;
using MyAdventure.Shared;
using MyAdventure.Shared.Feedback;

namespace MyAdventure.Desktop;

public sealed class DesktopApp : App
{
    protected override void ConfigurePlatformServices(IServiceCollection services) =>
        services.AddSingleton<ISoundPlayer, DesktopSoundPlayer>();
}
