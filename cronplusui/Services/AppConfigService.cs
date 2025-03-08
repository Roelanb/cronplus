using System;
using System.IO;
using System.Threading.Tasks;
using CronPlusUI.Models;
using Newtonsoft.Json;

namespace CronPlusUI.Services;

/// <summary>
/// Service for managing application configuration settings
/// </summary>
public class AppConfigService
{
    private readonly string _appConfigPath;
    private AppConfig _currentConfig;
    
    public AppConfigService()
    {
        // Store the app config in the same directory as the application
        _appConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "AppConfig.json");
        
        // Initialize with default settings
        _currentConfig = new AppConfig();
    }
    
    /// <summary>
    /// Load application configuration from the config file
    /// </summary>
    /// <returns>The loaded AppConfig</returns>
    public async Task<AppConfig> LoadConfigAsync()
    {
        try
        {
            if (File.Exists(_appConfigPath))
            {
                string json = await File.ReadAllTextAsync(_appConfigPath);
                var config = JsonConvert.DeserializeObject<AppConfig>(json);
                
                if (config != null)
                {
                    _currentConfig = config;
                }
            }
            else
            {
                // If config doesn't exist, create it with defaults
                await SaveConfigAsync(_currentConfig);
            }
            
            return _currentConfig;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading application configuration: {ex.Message}");
            return _currentConfig; // Return default config on error
        }
    }
    
    /// <summary>
    /// Save the current application configuration to the config file
    /// </summary>
    /// <param name="config">The configuration to save</param>
    public async Task SaveConfigAsync(AppConfig config)
    {
        try
        {
            _currentConfig = config;
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            await File.WriteAllTextAsync(_appConfigPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving application configuration: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Get the current application configuration
    /// </summary>
    /// <returns>The current AppConfig</returns>
    public AppConfig GetConfig()
    {
        return _currentConfig;
    }
    
    /// <summary>
    /// Update the application configuration
    /// </summary>
    /// <param name="config">The new configuration</param>
    public void UpdateConfig(AppConfig config)
    {
        _currentConfig = config;
    }
}
