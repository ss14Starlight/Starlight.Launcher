using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaWebView;

namespace Starlight.Launcher;

public partial class MainWindow : Window
{
    private readonly NativeWebView _web;

    public MainWindow() : this(null) { }

    public MainWindow(Uri? blazorUrl)
    {
        InitializeComponent();

        _web = this.FindControl<NativeWebView>("Web")
            ?? throw new InvalidOperationException("WebView not found.");

        if (blazorUrl is not null)
            _web.Source = blazorUrl;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
