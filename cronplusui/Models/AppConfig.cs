using System;
using System.IO;
using Newtonsoft.Json;

namespace CronPlusUI.Models;

/// <summary>
/// Application configuration model that stores UI application settings
/// </summary>
public class AppConfig
{
    /// <summary>
    /// Default path to the service configuration file
    /// </summary>
    public string DefaultServiceConfigPath { get; set; } = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Config.json");
    
    /// <summary>
    /// Default theme for the application (Light/Dark)
    /// </summary>
    public string Theme { get; set; } = "Light";
    
    /// <summary>
    /// Whether to automatically start the service on application startup
    /// </summary>
    public bool AutoStartService { get; set; } = false;
    
    /// <summary>
    /// Whether to minimize to system tray instead of closing
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;
}
