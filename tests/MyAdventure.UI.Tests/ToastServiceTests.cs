using MyAdventure.Shared.Services;
using Shouldly;

namespace MyAdventure.UI.Tests;

public class ToastServiceTests
{
    private readonly ManualTimeProvider _clock = new();

    [Fact]
    public void Show_ShouldAddToast()
    {
        var service = new ToastService(_clock);
        service.Show("Test message");
        service.ActiveToasts.Count.ShouldBe(1);
        service.ActiveToasts[0].Message.ShouldBe("Test message");
    }

    [Fact]
    public void Show_MultipleTimes_ShouldAddMultiple()
    {
        var service = new ToastService(_clock);
        service.Show("One");
        service.Show("Two");
        service.Show("Three");
        service.ActiveToasts.Count.ShouldBe(3);
    }

    [Fact]
    public void CleanupExpired_ShouldRemoveExpiredToasts()
    {
        var service = new ToastService(_clock);
        service.Show("Will expire", TimeSpan.Zero);

        service.CleanupExpired();

        service.ActiveToasts.ShouldBeEmpty();
    }

    [Fact]
    public void CleanupExpired_DefaultLifetime_ExpiresAfterThreeSeconds()
    {
        var service = new ToastService(_clock);
        service.Show("Default");

        _clock.Advance(ToastService.DefaultLifetime - TimeSpan.FromMilliseconds(1));
        service.CleanupExpired();
        service.ActiveToasts.Count.ShouldBe(1);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        service.CleanupExpired();
        service.ActiveToasts.ShouldBeEmpty();
    }

    [Fact]
    public void CleanupExpired_MixedToasts_ShouldOnlyRemoveExpired()
    {
        var service = new ToastService(_clock);
        service.Show("Expired", TimeSpan.Zero);
        service.Show("Active", TimeSpan.FromMinutes(5));

        _clock.Advance(TimeSpan.FromSeconds(1));
        service.CleanupExpired();

        service.ActiveToasts.Count.ShouldBe(1);
        service.ActiveToasts[0].Message.ShouldBe("Active");
    }
}
