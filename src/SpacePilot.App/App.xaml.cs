using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpacePilot.App.ViewModels;
using SpacePilot.Application.Compression;
using SpacePilot.Application.Jobs;
using SpacePilot.Application.Services;
using SpacePilot.Core.Interfaces;
using SpacePilot.Infrastructure.Diagnostics;
using SpacePilot.Infrastructure.Images;
using SpacePilot.Infrastructure.Logging;
using SpacePilot.Infrastructure.Pdf;
using SpacePilot.Infrastructure.Persistence;
using SpacePilot.Infrastructure.S3;
using SpacePilot.Infrastructure.Security;

namespace SpacePilot.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            // 1. Initialize rolling structured logging
            LoggingService.Initialize();

            // 2. Setup Dependency Injection
            var services = new ServiceCollection();

            // Infrastructure Logging
            services.AddLogging(builder =>
            {
                builder.AddDebug();
            });

            // Security
            services.AddSingleton<ICredentialProtectionService, DpapiCredentialProtectionService>();

            // Persistence (SQLite)
            services.AddSingleton(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<DatabaseContext>>();
                return new DatabaseContext(null, logger);
            });
            services.AddSingleton<IStorageIndexer, SqliteStorageIndexer>();
            services.AddSingleton<ISettingsService, SqliteSettingsRepository>();
            services.AddSingleton<IJobRepository, SqliteJobRepository>();
            services.AddSingleton<SqliteJobRepository>();

            // S3 Services
            services.AddSingleton<IS3Service, S3StorageService>();

            // Compression Engines
            services.AddSingleton<IImageCompressionEngine, MagickImageCompressionEngine>();
            services.AddSingleton<IPdfOptimizationEngine, PdfSharpOptimizationEngine>();
            services.AddSingleton<IUnifiedCompressionEngine, UnifiedCompressionService>();
            services.AddSingleton<ILocalFolderService, LocalFolderService>();

            // Jobs Orchestration
            services.AddSingleton<IJobManager, JobManager>();

            // Diagnostics
            services.AddSingleton<IDiagnosticService, DiagnosticService>();

            // ViewModels
            services.AddSingleton<DashboardViewModel>();
            services.AddSingleton<ExplorerViewModel>();
            services.AddSingleton<CompressorViewModel>();
            services.AddSingleton<LocalFilesViewModel>();
            services.AddSingleton<CleanupViewModel>();
            services.AddSingleton<JobsViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<AboutViewModel>();
            services.AddSingleton<MainViewModel>();

            // MainWindow
            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

            // 3. Initialize SQLite Schema
            var db = _serviceProvider.GetRequiredService<DatabaseContext>();
            await db.InitializeAsync();

            // 4. Launch Main Window
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex)
        {
            LoggingService.EmitLogEvent(Serilog.Events.LogEventLevel.Fatal, "Fatal exception on application startup", ex);
            MessageBox.Show($"SpacePilot failed to start:\n\n{ex.Message}\n\nSee logs for details.", "SpacePilot Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
