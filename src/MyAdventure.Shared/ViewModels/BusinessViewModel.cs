using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Services;

namespace MyAdventure.Shared.ViewModels;

/// <summary>ViewModel wrapping a single Business for data binding.</summary>
public partial class BusinessViewModel(Business model, GameEngine engine) : ViewModelBase
{
    public Business Model => model;
    public string Id => model.Id;
    public string Name => model.Name;
    public string Icon => model.Icon;
    public string Color => model.Color;

    [ObservableProperty] private int _owned;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasManager;
    [ObservableProperty] private string _costText = "";
    [ObservableProperty] private string _revenueText = "";
    [ObservableProperty] private string _managerCostText = "";
    [ObservableProperty] private bool _canAfford;
    [ObservableProperty] private bool _canAffordManager;

    [RelayCommand]
    private void ClickBusiness()
    {
        if (model.Owned <= 0)
            engine.BuyBusiness(model.Id);
        else
            engine.StartBusiness(model.Id);
    }

    [RelayCommand]
    private void BuyBusiness() => engine.BuyBusiness(model.Id);

    [RelayCommand]
    private void BuyManager() => engine.BuyManager(model.Id);

    /// <summary>Refresh all bindable properties from the model.</summary>
    public void Refresh(double cash)
    {
        Owned = model.Owned;
        ProgressPercent = model.ProgressPercent;
        IsRunning = model.IsRunning;
        HasManager = model.HasManager;
        CostText = NumberFormatter.Format(model.NextCost);
        RevenueText = model.Owned > 0 ? NumberFormatter.Format(model.Revenue) : "—";
        ManagerCostText = NumberFormatter.Format(model.BaseCost * 1000);
        CanAfford = cash >= model.NextCost;
        CanAffordManager = !model.HasManager && cash >= model.BaseCost * 1000;
    }
}
