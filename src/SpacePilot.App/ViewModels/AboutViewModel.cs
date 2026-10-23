using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SpacePilot.App.ViewModels;

public record ThirdPartyLicense(string LibraryName, string License, string Copyright, string Url);

public partial class AboutViewModel : ObservableObject
{
    public string AppName => "SpacePilot";
    public string Tagline => "Explore. Optimize. Reclaim space.";
    public string Version => "1.0.0 (Release Enterprise)";
    public string Runtime => RuntimeInformation.FrameworkDescription;
    public string Architecture => RuntimeInformation.ProcessArchitecture.ToString();
    public string OperatingSystem => RuntimeInformation.OSDescription;

    public List<ThirdPartyLicense> Licenses { get; } = new()
    {
        new("WPF-UI", "MIT License", "Copyright (c) Lepoco", "https://github.com/lepoco/wpfui"),
        new("AWSSDK.S3", "Apache-2.0", "Copyright Amazon.com, Inc.", "https://aws.amazon.com/sdk-for-net/"),
        new("Magick.NET", "Apache-2.0", "Copyright Dirk Lemstra", "https://github.com/dlemstra/Magick.NET"),
        new("PDFsharp", "MIT License", "Copyright (c) empira Software GmbH", "https://www.pdfsharp.net/"),
        new("Microsoft.Data.Sqlite", "MIT License", "Copyright (c) Microsoft Corporation", "https://github.com/dotnet/efcore"),
        new("CommunityToolkit.Mvvm", "MIT License", "Copyright (c) .NET Foundation", "https://github.com/CommunityToolkit/dotnet"),
        new("Serilog", "Apache-2.0", "Copyright Serilog Contributors", "https://serilog.net/")
    };
}
