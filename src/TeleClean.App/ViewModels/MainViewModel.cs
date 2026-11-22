using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using TeleClean.App.Messages;
using TeleClean.App.Services;

namespace TeleClean.App.ViewModels;

public sealed partial class MainViewModel : ViewModelBase,
    IRecipient<AuthenticationStateChangedMessage>,
    IRecipient<StartBulkExecutionMessage>,
    IRecipient<NavigationMessage>
{
    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private string _currentView = "Auth";

    public MainViewModel(IDispatcherService dispatcher) : base(dispatcher)
    {
        WeakReferenceMessenger.Default.Register<AuthenticationStateChangedMessage>(this);
        WeakReferenceMessenger.Default.Register<StartBulkExecutionMessage>(this);
        WeakReferenceMessenger.Default.Register<NavigationMessage>(this);
    }

    public void Receive(AuthenticationStateChangedMessage message)
    {
        Dispatcher.Enqueue(() =>
        {
            IsAuthenticated = message.Value;
            CurrentView = message.Value ? "Dialogs" : "Auth";
        });
    }

    public void Receive(StartBulkExecutionMessage message)
    {
        Dispatcher.Enqueue(() =>
        {
            CurrentView = "Progress";
        });
    }

    public void Receive(NavigationMessage message)
    {
        Dispatcher.Enqueue(() =>
        {
            CurrentView = message.Value;
        });
    }

    [RelayCommand]
    public void Navigate(string viewName)
    {
        CurrentView = viewName;
    }

    public override void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        base.Dispose();
    }
}
