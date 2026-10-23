using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SpacePilot.App.ViewModels;

namespace SpacePilot.App.Views;

public partial class CompressorView : UserControl
{
    public CompressorView()
    {
        InitializeComponent();
    }

    private void OnAddFilesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Select Images or PDFs to Compress",
            Filter = "Supported Media (*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tiff;*.pdf)|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tiff;*.pdf|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true && DataContext is CompressorViewModel vm)
        {
            vm.AddFiles(dialog.FileNames);
        }
    }

    private void OnDropFiles(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && DataContext is CompressorViewModel vm)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var collected = new List<string>();

            foreach (var file in files)
            {
                if (File.Exists(file))
                {
                    collected.Add(file);
                }
                else if (Directory.Exists(file))
                {
                    collected.AddRange(Directory.EnumerateFiles(file, "*.*", SearchOption.AllDirectories));
                }
            }

            vm.AddFiles(collected);
        }
    }

    private void OnDragOverFiles(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }
}
