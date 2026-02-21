using MyAdventure.Shared.Services;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class ToastServiceTests
{
    [Fact]
    public void Show_ShouldAddToast()
    {
        var service = new ToastService();
        service.Show("Test message");
        service.ActiveToasts.Count.ShouldBe(1);
        service.ActiveToasts[0].Message.ShouldBe("Test message");
    }

    [Fact]
    public void Show_MultipleTimes_ShouldAddMultiple()
    {
        var service = new ToastService();
        service.Show("One");
        service.Show("Two");
        service.Show("Three");
        service.ActiveToasts.Count.ShouldBe(3);
    }

    [Fact]
    public void CleanupExpired_ShouldRemoveExpiredToasts()
    {
        var service = new ToastService();
        service.Show("Will expire", TimeSpan.Zero);

        // Wait a tiny bit for DateTime.UtcNow to pass
        Thread.Sleep(10);

        service.CleanupExpired();
        service.ActiveToasts.Count.ShouldBe(0);
    }

    [Fact]
    public void CleanupExpired_ShouldKeepActiveToasts()
    {
        var service = new ToastService();
        service.Show("Still active", TimeSpan.FromMinutes(5));

        service.CleanupExpired();
        service.ActiveToasts.Count.ShouldBe(1);
    }

    [Fact]
    public void CleanupExpired_MixedToasts_ShouldOnlyRemoveExpired()
    {
        var service = new ToastService();
        service.Show("Expired", TimeSpan.Zero);
        service.Show("Active", TimeSpan.FromMinutes(5));

        Thread.Sleep(10);
        service.CleanupExpired();

        service.ActiveToasts.Count.ShouldBe(1);
        service.ActiveToasts[0].Message.ShouldBe("Active");
    }
}
