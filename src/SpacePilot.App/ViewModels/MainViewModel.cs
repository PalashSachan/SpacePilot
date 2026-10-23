using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpacePilot.Core.Interfaces;

namespace SpacePilot.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDiagnosticService _diagnosticService;

    [ObservableProperty]
    private ObservableObject _currentView;

    [ObservableProperty]
    private string _currentViewName = "Dashboard";

    [ObservableProperty]
    private string _activeConnectionPill = "Disconnected";

    public DashboardViewModel DashboardVM { get; }
    public ExplorerViewModel ExplorerVM { get; }
    public CompressorViewModel CompressorVM { get; }
    public LocalFilesViewModel LocalFilesVM { get; }
    public CleanupViewModel CleanupVM { get; }
    public JobsViewModel JobsVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public AboutViewModel AboutVM { get; }

    public MainViewModel(
        DashboardViewModel dashboardVM,
        ExplorerViewModel explorerVM,
        CompressorViewModel compressorVM,
        LocalFilesViewModel localFilesVM,
        CleanupViewModel cleanupVM,
        JobsViewModel jobsVM,
        SettingsViewModel settingsVM,
        AboutViewModel aboutVM,
        ISettingsService settingsService,
        IDiagnosticService diagnosticService)
    {
        DashboardVM = dashboardVM;
        ExplorerVM = explorerVM;
        CompressorVM = compressorVM;
        LocalFilesVM = localFilesVM;
        CleanupVM = cleanupVM;
        JobsVM = jobsVM;
        SettingsVM = settingsVM;
        AboutVM = aboutVM;
        _settingsService = settingsService;
        _diagnosticService = diagnosticService;

        DashboardVM.NavigateAction = Navigate;
        _currentView = DashboardVM;
    }

    public async Task InitializeAsync()
    {
        await SettingsVM.InitializeAsync();
        await ExplorerVM.InitializeAsync();
        await DashboardVM.RefreshDashboardAsync();

        var profiles = await _settingsService.GetProfilesAsync();
        var active = profiles.FirstOrDefault();
        if (active != null)
        {
            ActiveConnectionPill = $"{active.Name} ({active.BucketName})";
        }
    }

    [RelayCommand]
    public void Navigate(string destination)
    {
        CurrentViewName = destination;
        CurrentView = destination switch
        {
            "Dashboard" => DashboardVM,
            "Explorer" => ExplorerVM,
            "Compressor" => CompressorVM,
            "LocalFiles" => LocalFilesVM,
            "Cleanup" => CleanupVM,
            "Jobs" => JobsVM,
            "Settings" => SettingsVM,
            "About" => AboutVM,
            _ => DashboardVM
        };

        if (destination == "Dashboard") _ = DashboardVM.RefreshDashboardAsync();
        if (destination == "Jobs") _ = JobsVM.RefreshJobsAsync();
    }

    // Working Menu Commands
    [RelayCommand]
    public void MenuConnectStorage() => Navigate("Settings");

    [RelayCommand]
    public void MenuSelectLocalFolder() => Navigate("LocalFiles");

    [RelayCommand]
    public void MenuOpenLogsFolder() => JobsVM.OpenLogsFolder();

    [RelayCommand]
    public async Task MenuExportReportAsync() => await JobsVM.ExportDiagnosticReportAsync();

    [RelayCommand]
    public void MenuExit() => System.Windows.Application.Current.Shutdown();

    [RelayCommand]
    public void MenuScanBucket()
    {
        Navigate("Explorer");
        _ = ExplorerVM.StartScanAsync();
    }

    [RelayCommand]
    public void MenuRefreshListing()
    {
        Navigate("Explorer");
        _ = ExplorerVM.NavigateToPrefixAsync(ExplorerVM.CurrentPrefix);
    }

    [RelayCommand]
    public void MenuFindLargestFiles()
    {
        Navigate("Cleanup");
        _ = CleanupVM.RunAnalysisAsync();
    }

    [RelayCommand]
    public void MenuBulkCompress() => Navigate("Compressor");

    [RelayCommand]
    public void MenuStorageAnalyzer()
    {
        Navigate("Cleanup");
        _ = CleanupVM.RunAnalysisAsync();
    }

    [RelayCommand]
    public void MenuTestConnection()
    {
        Navigate("Settings");
        _ = SettingsVM.TestConnectionAsync();
    }

    [RelayCommand]
    public void MenuHelpAbout() => Navigate("About");
}
