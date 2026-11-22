using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TeleClean.App.Services;
using TeleClean.App.ViewModels;
using TeleClean.Core.Interfaces;
using TeleClean.Core.Services;

namespace TeleClean.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow MainWindow { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var services = new ServiceCollection();

        // Dispatcher service initialized with main UI thread queue
        services.AddSingleton<IDispatcherService>(new WinUIDispatcherService(DispatcherQueue.GetForCurrentThread()));

        // Core singletons
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<ICacheService, CacheService>();
        services.AddSingleton<ITelegramService, TelegramService>();
        services.AddSingleton<IBulkExecutionEngine, BulkExecutionEngine>();

        // ViewModels
        services.AddSingleton<AuthViewModel>();
        services.AddSingleton<DialogListViewModel>();
        services.AddSingleton<BulkExecutionViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        Services = services.BuildServiceProvider();

        // Eagerly resolve BulkExecutionViewModel so WeakReferenceMessenger and engine event subscriptions
        // are active immediately from application startup before any user interaction
        Services.GetRequiredService<BulkExecutionViewModel>();

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
