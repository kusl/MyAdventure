using MyAdventure.Core.Entities;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Feedback;
using MyAdventure.Shared.ViewModels;
using NSubstitute;

namespace MyAdventure.UI.Tests;

public class BusinessViewModelFeedbackTests
{
    private readonly IFeedbackService _feedback = Substitute.For<IFeedbackService>();
    private readonly GameEngine _engine = Ui.CreateEngine();

    private BusinessViewModel Lemonade() =>
        Ui.CreateBusiness(_engine.Businesses.First(b => b.Id == "lemonade"), _engine, _feedback);

    [Fact]
    public void Buy_Affordable_PlaysPurchase()
    {
        _engine.Cash = new BigDouble(100);

        Lemonade().BuyBusinessCommand.Execute(null);

        _feedback.Received(1).Play(FeedbackCue.Purchase);
    }

    [Fact]
    public void Buy_Unaffordable_PlaysDenied()
    {
        _engine.Cash = BigDouble.Zero;

        Lemonade().BuyBusinessCommand.Execute(null);

        _feedback.Received(1).Play(FeedbackCue.Denied);
    }

    [Fact]
    public void Buy_CrossingMilestone_PlaysMilestone()
    {
        _engine.Cash = new BigDouble(1.0, 9);
        _engine.Businesses.First(b => b.Id == "lemonade").Owned = 24;

        Lemonade().BuyBusinessCommand.Execute(null);

        _feedback.Received(1).Play(FeedbackCue.Milestone);
    }

    [Fact]
    public void BulkBuy_ReachingMilestone_PlaysMilestone()
    {
        _engine.Cash = new BigDouble(1.0, 9);

        Lemonade().BulkBuyCommand.Execute(null);

        _feedback.Received(1).Play(FeedbackCue.Milestone);
    }

    [Fact]
    public void Run_OwnedIdleBusiness_PlaysTap()
    {
        _engine.Cash = new BigDouble(100);
        _engine.BuyBusiness("lemonade");

        Lemonade().ClickBusinessCommand.Execute(null);

        _feedback.Received(1).Play(FeedbackCue.Tap);
    }

    [Fact]
    public void Manager_Affordable_PlaysManager()
    {
        _engine.Cash = new BigDouble(1.0, 6);
        _engine.BuyBusiness("lemonade");

        Lemonade().BuyManagerCommand.Execute(null);

        _feedback.Received(1).Play(FeedbackCue.Manager);
    }
}
