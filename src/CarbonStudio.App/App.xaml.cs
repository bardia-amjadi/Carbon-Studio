using CarbonStudio.Core.DependencyInjection;
using CarbonStudio.Core.Paths;
using CarbonStudio.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace CarbonStudio.App;

public partial class CarbonApplication : Application
{
    private readonly ServiceProvider _services;
    private readonly ILogger<CarbonApplication> _logger;
    private Window? _window;

    public CarbonApplication()
    {
        InitializeComponent();

        var services = new ServiceCollection();
        services.AddCarbonCore(AppPaths.CreateDefault());
        _services = services.BuildServiceProvider();
        _logger = _services.GetRequiredService<ILogger<CarbonApplication>>();

        UnhandledException += (_, e) => _logger.LogCritical(e.Exception, "Unhandled UI exception: {Message}", e.Message);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var settings = _services.GetRequiredService<ISettingsService>();
            await settings.LoadAsync();
            foreach (var warning in settings.LoadWarnings)
            {
                _logger.LogWarning("{Warning}", warning);
            }

            _logger.LogInformation("Carbon Studio starting");
            _window = new MainWindow(_services);
            _window.Closed += (_, _) =>
            {
                _logger.LogInformation("Carbon Studio closed");
                _services.Dispose();
            };
            _window.Activate();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Startup failed");
            throw;
        }
    }
}
