using System;

using Avalonia.Controls;

using Prism.Regions;

namespace ImageCare.UI.Avalonia.Views;

public partial class MainWindow:Window
{
    public MainWindow(IRegionManager regionManager)
    {
        InitializeComponent();
    }
}