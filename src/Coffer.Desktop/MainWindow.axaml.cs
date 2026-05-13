using Avalonia.Controls;
using Microsoft.Extensions.Logging;

namespace Coffer.Desktop;

public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow>? _logger;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(ILogger<MainWindow> logger) : this()
    {
        _logger = logger;
        _logger.LogInformation("MainWindow created");
    }
}
