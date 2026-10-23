using System.Windows;
using SpacePilot.App.ViewModels;

namespace SpacePilot.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (s, e) => await viewModel.InitializeAsync();
    }
}