namespace Cronplus.Api.Infrastructure.Configuration;

/// <summary>
/// Configuration for plugins
/// </summary>
public class PluginConfiguration
{
    /// <summary>
    /// Whether plugins are enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Directory where plugins are stored
    /// </summary>
    public string PluginDirectory { get; set; } = "plugins";
    
    /// <summary>
    /// Whether to auto-load plugins on startup
    /// </summary>
    public bool AutoLoad { get; set; } = true;
    
    /// <summary>
    /// List of plugin IDs to load (if empty, all plugins are loaded)
    /// </summary>
    public List<string> AllowedPlugins { get; set; } = new();
    
    /// <summary>
    /// List of plugin IDs to block from loading
    /// </summary>
    public List<string> BlockedPlugins { get; set; } = new();
    
    /// <summary>
    /// Plugin-specific configurations
    /// </summary>
    public Dictionary<string, PluginSettings> PluginSettings { get; set; } = new();
}

/// <summary>
/// Settings for a specific plugin
/// </summary>
public class PluginSettings
{
    /// <summary>
    /// Whether this specific plugin is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Plugin-specific configuration
    /// </summary>
    public Dictionary<string, object> Configuration { get; set; } = new();
    
    /// <summary>
    /// Auto-start the plugin on application startup
    /// </summary>
    public bool AutoStart { get; set; } = true;
    
    /// <summary>
    /// Priority for loading (higher priority loads first)
    /// </summary>
    public int Priority { get; set; } = 0;
}