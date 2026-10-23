using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;

namespace SpacePilot.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IS3Service _s3Service;

    [ObservableProperty]
    private StorageProfile? _selectedProfile;

    [ObservableProperty]
    private ApplicationSettings _settings = new();

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string _testResult = string.Empty;

    public ObservableCollection<StorageProfile> Profiles { get; } = new();

    public SettingsViewModel(
        ISettingsService settingsService,
        IS3Service s3Service)
    {
        _settingsService = settingsService;
        _s3Service = s3Service;
    }

    public async Task InitializeAsync()
    {
        Settings = await _settingsService.LoadSettingsAsync();

        var profiles = await _settingsService.GetProfilesAsync();
        Profiles.Clear();
        foreach (var p in profiles) Profiles.Add(p);

        SelectedProfile = Profiles.FirstOrDefault();
    }

    [RelayCommand]
    public void AddProfile()
    {
        var newProfile = new StorageProfile
        {
            Name = "New S3 Profile",
            ProviderType = StorageProviderType.DigitalOceanSpaces,
            ServiceEndpoint = "https://blr1.digitaloceanspaces.com",
            Region = "blr1",
            BucketName = "docs456"
        };
        Profiles.Add(newProfile);
        SelectedProfile = newProfile;
    }

    [RelayCommand]
    public async Task SaveProfileAsync()
    {
        if (SelectedProfile == null) return;
        await _settingsService.SaveProfileAsync(SelectedProfile);
        await _settingsService.SaveSettingsAsync(Settings);
        MessageBox.Show("Profile and settings saved successfully.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    public async Task DeleteProfileAsync()
    {
        if (SelectedProfile == null) return;
        if (Profiles.Count <= 1)
        {
            MessageBox.Show("At least one profile must be retained.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var res = MessageBox.Show($"Delete profile '{SelectedProfile.Name}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            await _settingsService.DeleteProfileAsync(SelectedProfile.Id);
            Profiles.Remove(SelectedProfile);
            SelectedProfile = Profiles.FirstOrDefault();
        }
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        if (SelectedProfile == null) return;

        IsTesting = true;
        TestResult = "Connecting to endpoint and testing S3 access...";

        try
        {
            bool success = await _s3Service.TestConnectionAsync(SelectedProfile);
            if (success)
            {
                SelectedProfile.LastConnectedAt = DateTime.UtcNow;
                await _settingsService.SaveProfileAsync(SelectedProfile);
                TestResult = "Connection test SUCCEEDED! Verified S3 bucket access.";
            }
            else
            {
                TestResult = "Connection test FAILED. Please check endpoint, credentials, and bucket name.";
            }
        }
        catch (Exception ex)
        {
            TestResult = $"Connection error: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    public void SetDigitalOceanPreset(string region)
    {
        if (SelectedProfile == null) return;
        SelectedProfile.ProviderType = StorageProviderType.DigitalOceanSpaces;
        SelectedProfile.Region = region.ToLowerInvariant();
        SelectedProfile.ServiceEndpoint = $"https://{region.ToLowerInvariant()}.digitaloceanspaces.com";
        SelectedProfile.ForcePathStyle = false;
        SelectedProfile.UseSsl = true;
    }
}
