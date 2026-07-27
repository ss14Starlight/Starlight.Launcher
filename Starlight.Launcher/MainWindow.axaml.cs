using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AvaloniaWebView;

namespace Starlight.Launcher;

public partial class MainWindow : Window
{
    private readonly NativeWebView _web;

    private readonly Uri? _blazorUrl;

    public MainWindow() : this(null) { }

    public MainWindow(Uri? blazorUrl)
    {
        InitializeComponent();

        _blazorUrl = blazorUrl;

        _web = this.FindControl<NativeWebView>("Web")
            ?? throw new InvalidOperationException("WebView not found.");

        if (blazorUrl is not null)
            _web.Source = blazorUrl;

#if DEBUG
        Opened += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = _blazorUrl!.ToString(),
            UseShellExecute = true
        });
#endif
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
