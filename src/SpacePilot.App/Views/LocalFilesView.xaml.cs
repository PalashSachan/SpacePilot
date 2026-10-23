using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SpacePilot.App.ViewModels;

namespace SpacePilot.App.Views;

public partial class LocalFilesView : UserControl
{
    public LocalFilesView()
    {
        InitializeComponent();
    }

    private void OnBrowseSourceClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Directory to Scan for Images and PDFs"
        };

        if (dialog.ShowDialog() == true && DataContext is LocalFilesViewModel vm)
        {
            vm.SelectedFolderPath = dialog.FolderName;
            _ = vm.ScanFolderAsync();
        }
    }
}
