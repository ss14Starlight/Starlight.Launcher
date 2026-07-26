using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Starlight.Launcher;

public partial class MainWindow : Window
{
    public MainWindow() : this(null) { }

    public MainWindow(Uri? blazorUrl)
    {
        InitializeComponent();

        if (blazorUrl is not null)
            Web.Source = blazorUrl;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
