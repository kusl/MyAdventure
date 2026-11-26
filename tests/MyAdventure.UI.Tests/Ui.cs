using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Feedback;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;
using NSubstitute;

namespace MyAdventure.UI.Tests;

internal static class Ui
{
    public static GameEngine CreateEngine(TimeProvider? time = null, IGameStateRepository? repository = null) =>
        new(repository ?? new InMemoryGameStateRepository(), NullLogger<GameEngine>.Instance, time);

    public static GameViewModel CreateGame(
        GameEngine engine,
        TimeProvider? time = null,
        ToastService? toasts = null,
        IFeedbackService? feedback = null,
        ISettingsRepository? settings = null)
    {
        var clock = time ?? TimeProvider.System;
        return new GameViewModel(
            engine,
            toasts ?? new ToastService(clock),
            settings ?? new InMemorySettingsRepository(),
            feedback ?? Substitute.For<IFeedbackService>(),
            NullLogger<GameViewModel>.Instance,
            clock);
    }

    public static BusinessViewModel CreateBusiness(
        Business business,
        GameEngine engine,
        IFeedbackService? feedback = null,
        ToastService? toasts = null) =>
        new(business, engine, toasts ?? new ToastService(), feedback ?? Substitute.For<IFeedbackService>());
}
