using System;
using CommunityToolkit.Mvvm.ComponentModel;
using TeleClean.App.Services;

namespace TeleClean.App.ViewModels;

/// <summary>
/// Base class for all application view models providing UI thread dispatching, busy state, and error handling.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject, IDisposable
{
    protected readonly IDispatcherService Dispatcher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    private bool _isBusy;

    public bool IsReady => !IsBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnStatusMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    protected ViewModelBase(IDispatcherService dispatcher)
    {
        Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public virtual void Dispose()
    {
    }
}
