using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using CronPlusUI.Models;
using CronPlusUI.Services;
using CronPlusUI.ViewModels;
using CronPlusUI.Views;

namespace CronPlusUI;

public partial class App : Application
{
    // Application services
    public static AppConfigService AppConfigService { get; private set; } = new AppConfigService();
    public static ConfigService ConfigService { get; private set; } = new ConfigService(AppConfigService);
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Initialize services synchronously to ensure they're available
        // when view models are created
        InitializeServices();
    }
    
    private void InitializeServices()
    {
        // Load application configuration synchronously
        AppConfigService.LoadConfigAsync().GetAwaiter().GetResult();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            
            // Create main window with view model
            var mainViewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}