using CarbonStudio.Core.Commands;
using CarbonStudio.Core.Documents;
using CarbonStudio.Core.FileSystem;
using CarbonStudio.Core.Logging;
using CarbonStudio.Core.Output;
using CarbonStudio.Core.Paths;
using CarbonStudio.Core.Settings;
using CarbonStudio.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CarbonStudio.Core.DependencyInjection;

public static class CoreServiceCollectionExtensions
{
    /// <summary>Registers all Core services, rolling-file logging and the Output panel log mirror.</summary>
    public static IServiceCollection AddCarbonCore(this IServiceCollection services, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        paths.EnsureCreated();

        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOutputService, OutputService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IRecentProjectsStore, RecentProjectsStore>();
        services.AddSingleton<IProjectDetector, ProjectDetector>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<DocumentManager>();
        services.AddSingleton<ICommandRegistry, CommandRegistry>();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new RollingFileLoggerProvider(new RollingFileLoggerOptions { Directory = paths.LogsDirectory }));
        });

        // The Output mirror needs the OutputService instance, so it is resolved lazily from the container.
        services.AddSingleton<ILoggerProvider>(sp => new OutputLoggerProvider(sp.GetRequiredService<IOutputService>()));
        return services;
    }
}
