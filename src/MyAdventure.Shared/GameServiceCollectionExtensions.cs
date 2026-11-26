using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Feedback;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;

namespace MyAdventure.Shared;

public static class GameServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddGame()
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ISoundPlayer, SilentSoundPlayer>();
            services.TryAddSingleton<IHapticPlayer, NoHaptics>();
            services.AddSingleton<IFeedbackService, FeedbackService>();
            services.AddSingleton<ToastService>();
            services.AddSingleton<GameEngine>();
            services.AddSingleton<GameViewModel>();
            return services;
        }
    }
}
