using System.Collections.Concurrent;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Domain.Models.PipelineSteps;
using Microsoft.Extensions.Logging;

namespace Cronplus.Api.Infrastructure.Services;

/// <summary>
/// Factory for creating pipeline step instances based on their type
/// </summary>
public interface IActionFactory
{
    /// <summary>
    /// Create a pipeline step instance by type
    /// </summary>
    PipelineStepBase? CreateStep(string stepType);
    
    /// <summary>
    /// Register a custom action plugin
    /// </summary>
    Task RegisterPluginAsync(IActionPlugin plugin, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Unregister a plugin
    /// </summary>
    Task UnregisterPluginAsync(string pluginId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all registered action types
    /// </summary>
    IEnumerable<string> GetRegisteredActionTypes();
    
    /// <summary>
    /// Get metadata for an action type
    /// </summary>
    ActionMetadata? GetActionMetadata(string actionType);
    
    /// <summary>
    /// Check if an action type is registered
    /// </summary>
    bool IsActionTypeRegistered(string actionType);
}

/// <summary>
/// Implementation of the action factory
/// </summary>
public class ActionFactory : IActionFactory
{
    private readonly ILogger<ActionFactory> _logger;
    private readonly ConcurrentDictionary<string, Func<PipelineStepBase>> _builtInActions;
    private readonly ConcurrentDictionary<string, IActionPlugin> _plugins;
    private readonly ConcurrentDictionary<string, ActionMetadata> _actionMetadata;
    
    public ActionFactory(ILogger<ActionFactory> logger)
    {
        _logger = logger;
        _plugins = new ConcurrentDictionary<string, IActionPlugin>();
        _actionMetadata = new ConcurrentDictionary<string, ActionMetadata>();
        _builtInActions = new ConcurrentDictionary<string, Func<PipelineStepBase>>();
        
        RegisterBuiltInActions();
    }
    
    private void RegisterBuiltInActions()
    {
        // Register built-in action types
        _builtInActions["copy"] = () => new CopyStep();
        _builtInActions["delete"] = () => new DeleteStep();
        _builtInActions["archive"] = () => new ArchiveStep();
        _builtInActions["print"] = () => new PrintStep();
        _builtInActions["restapi"] = () => new RestApiStep();
        _builtInActions["decision"] = () => new DecisionStep();
        
        // Register metadata for built-in actions
        RegisterBuiltInMetadata();
    }
    
    private void RegisterBuiltInMetadata()
    {
        _actionMetadata["copy"] = new ActionMetadata
        {
            ActionType = "copy",
            DisplayName = "Copy File",
            Description = "Copy or move files to another location",
            Category = "File Operations",
            Icon = "copy",
            Properties = new Dictionary<string, PropertyMetadata>
            {
                ["destinationPath"] = new PropertyMetadata
                {
                    Name = "destinationPath",
                    DisplayName = "Destination Path",
                    Description = "Path where the file will be copied",
                    Type = "string",
                    Required = true
                },
                ["overwrite"] = new PropertyMetadata
                {
                    Name = "overwrite",
                    DisplayName = "Overwrite Existing",
                    Description = "Overwrite if destination file exists",
                    Type = "boolean",
                    Required = false,
                    DefaultValue = false
                },
                ["verifyChecksum"] = new PropertyMetadata
                {
                    Name = "verifyChecksum",
                    DisplayName = "Verify Checksum",
                    Description = "Verify file integrity after copy",
                    Type = "boolean",
                    Required = false,
                    DefaultValue = true
                }
            }
        };
        
        _actionMetadata["delete"] = new ActionMetadata
        {
            ActionType = "delete",
            DisplayName = "Delete File",
            Description = "Delete files with optional secure deletion",
            Category = "File Operations",
            Icon = "delete",
            Properties = new Dictionary<string, PropertyMetadata>
            {
                ["secureDelete"] = new PropertyMetadata
                {
                    Name = "secureDelete",
                    DisplayName = "Secure Delete",
                    Description = "Overwrite file data before deletion",
                    Type = "boolean",
                    Required = false,
                    DefaultValue = false
                },
                ["moveToRecycleBin"] = new PropertyMetadata
                {
                    Name = "moveToRecycleBin",
                    DisplayName = "Move to Recycle Bin",
                    Description = "Move to recycle bin instead of permanent deletion",
                    Type = "boolean",
                    Required = false,
                    DefaultValue = false
                }
            }
        };
        
        _actionMetadata["archive"] = new ActionMetadata
        {
            ActionType = "archive",
            DisplayName = "Archive File",
            Description = "Compress files into archives",
            Category = "File Operations",
            Icon = "archive",
            Properties = new Dictionary<string, PropertyMetadata>
            {
                ["archivePath"] = new PropertyMetadata
                {
                    Name = "archivePath",
                    DisplayName = "Archive Path",
                    Description = "Path for the archive file",
                    Type = "string",
                    Required = true
                },
                ["format"] = new PropertyMetadata
                {
                    Name = "format",
                    DisplayName = "Archive Format",
                    Description = "Format of the archive",
                    Type = "string",
                    Required = false,
                    DefaultValue = "Zip",
                    AllowedValues = new object[] { "Zip", "GZip", "Tar" }
                }
            }
        };
        
        _actionMetadata["restapi"] = new ActionMetadata
        {
            ActionType = "restapi",
            DisplayName = "REST API Call",
            Description = "Make HTTP/REST API calls",
            Category = "Integration",
            Icon = "api",
            Properties = new Dictionary<string, PropertyMetadata>
            {
                ["url"] = new PropertyMetadata
                {
                    Name = "url",
                    DisplayName = "URL",
                    Description = "API endpoint URL",
                    Type = "string",
                    Required = true,
                    ValidationPattern = @"^https?://.+"
                },
                ["method"] = new PropertyMetadata
                {
                    Name = "method",
                    DisplayName = "HTTP Method",
                    Description = "HTTP method to use",
                    Type = "string",
                    Required = false,
                    DefaultValue = "GET",
                    AllowedValues = new object[] { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" }
                }
            }
        };
        
        _actionMetadata["decision"] = new ActionMetadata
        {
            ActionType = "decision",
            DisplayName = "Decision/Condition",
            Description = "Conditional branching based on rules",
            Category = "Control Flow",
            Icon = "decision",
            Properties = new Dictionary<string, PropertyMetadata>
            {
                ["rules"] = new PropertyMetadata
                {
                    Name = "rules",
                    DisplayName = "Decision Rules",
                    Description = "List of rules to evaluate",
                    Type = "array",
                    Required = true
                },
                ["defaultAction"] = new PropertyMetadata
                {
                    Name = "defaultAction",
                    DisplayName = "Default Action",
                    Description = "Action to take if no rules match",
                    Type = "string",
                    Required = false,
                    DefaultValue = "Continue",
                    AllowedValues = new object[] { "Continue", "Skip", "Stop", "Fail", "Jump" }
                }
            }
        };
    }
    
    public PipelineStepBase? CreateStep(string stepType)
    {
        if (string.IsNullOrWhiteSpace(stepType))
            return null;
        
        var normalizedType = stepType.ToLowerInvariant();
        
        // Check built-in actions first
        if (_builtInActions.TryGetValue(normalizedType, out var builtInFactory))
        {
            _logger.LogDebug("Creating built-in action: {ActionType}", normalizedType);
            return builtInFactory();
        }
        
        // Check plugins
        foreach (var plugin in _plugins.Values)
        {
            if (plugin.ProvidedActionTypes.Contains(normalizedType))
            {
                _logger.LogDebug("Creating plugin action: {ActionType} from plugin: {PluginId}", 
                    normalizedType, plugin.PluginId);
                
                try
                {
                    return plugin.CreateStep(normalizedType);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create action {ActionType} from plugin {PluginId}", 
                        normalizedType, plugin.PluginId);
                    return null;
                }
            }
        }
        
        _logger.LogWarning("Unknown action type: {ActionType}", normalizedType);
        return null;
    }
    
    public async Task RegisterPluginAsync(IActionPlugin plugin, CancellationToken cancellationToken = default)
    {
        if (plugin == null)
            throw new ArgumentNullException(nameof(plugin));
        
        _logger.LogInformation("Registering plugin: {PluginId} - {PluginName} v{Version}", 
            plugin.PluginId, plugin.Name, plugin.Version);
        
        // Check for conflicts
        foreach (var actionType in plugin.ProvidedActionTypes)
        {
            if (_builtInActions.ContainsKey(actionType.ToLowerInvariant()))
            {
                throw new InvalidOperationException(
                    $"Plugin {plugin.PluginId} cannot override built-in action type: {actionType}");
            }
            
            // Check if another plugin already provides this action
            var existingPlugin = _plugins.Values
                .FirstOrDefault(p => p.ProvidedActionTypes.Contains(actionType));
            
            if (existingPlugin != null && existingPlugin.PluginId != plugin.PluginId)
            {
                throw new InvalidOperationException(
                    $"Action type {actionType} is already provided by plugin {existingPlugin.PluginId}");
            }
        }
        
        // Initialize the plugin
        await plugin.InitializeAsync(new Dictionary<string, object>(), cancellationToken);
        
        // Register the plugin
        _plugins[plugin.PluginId] = plugin;
        
        _logger.LogInformation("Successfully registered plugin: {PluginId} providing actions: {Actions}", 
            plugin.PluginId, string.Join(", ", plugin.ProvidedActionTypes));
    }
    
    public async Task UnregisterPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        if (_plugins.TryRemove(pluginId, out var plugin))
        {
            _logger.LogInformation("Unregistering plugin: {PluginId}", pluginId);
            
            try
            {
                await plugin.ShutdownAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during plugin shutdown: {PluginId}", pluginId);
            }
            
            // Remove metadata for plugin actions
            foreach (var actionType in plugin.ProvidedActionTypes)
            {
                _actionMetadata.TryRemove(actionType, out _);
            }
            
            _logger.LogInformation("Successfully unregistered plugin: {PluginId}", pluginId);
        }
    }
    
    public IEnumerable<string> GetRegisteredActionTypes()
    {
        var actionTypes = new HashSet<string>(_builtInActions.Keys);
        
        foreach (var plugin in _plugins.Values)
        {
            foreach (var actionType in plugin.ProvidedActionTypes)
            {
                actionTypes.Add(actionType.ToLowerInvariant());
            }
        }
        
        return actionTypes.OrderBy(t => t);
    }
    
    public ActionMetadata? GetActionMetadata(string actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType))
            return null;
        
        var normalizedType = actionType.ToLowerInvariant();
        
        if (_actionMetadata.TryGetValue(normalizedType, out var metadata))
            return metadata;
        
        // Try to get metadata from plugins
        foreach (var plugin in _plugins.Values)
        {
            if (plugin.ProvidedActionTypes.Contains(normalizedType))
            {
                // Create basic metadata if plugin doesn't provide it
                return new ActionMetadata
                {
                    ActionType = normalizedType,
                    DisplayName = actionType,
                    Description = $"Custom action provided by {plugin.Name}",
                    Category = "Plugin",
                    Icon = "plugin"
                };
            }
        }
        
        return null;
    }
    
    public bool IsActionTypeRegistered(string actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType))
            return false;
        
        var normalizedType = actionType.ToLowerInvariant();
        
        if (_builtInActions.ContainsKey(normalizedType))
            return true;
        
        return _plugins.Values.Any(p => p.ProvidedActionTypes.Contains(normalizedType));
    }
}