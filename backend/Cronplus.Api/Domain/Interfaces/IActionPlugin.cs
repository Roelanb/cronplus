using Cronplus.Api.Domain.Models.PipelineSteps;
using FluentValidation.Results;

namespace Cronplus.Api.Domain.Interfaces;

/// <summary>
/// Interface for custom action plugins that extend the pipeline capabilities
/// </summary>
public interface IActionPlugin
{
    /// <summary>
    /// Unique identifier for the plugin
    /// </summary>
    string PluginId { get; }
    
    /// <summary>
    /// Display name of the plugin
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Description of what the plugin does
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Version of the plugin
    /// </summary>
    Version Version { get; }
    
    /// <summary>
    /// Author of the plugin
    /// </summary>
    string Author { get; }
    
    /// <summary>
    /// List of action types this plugin provides
    /// </summary>
    IEnumerable<string> ProvidedActionTypes { get; }
    
    /// <summary>
    /// Create an instance of a pipeline step for the given type
    /// </summary>
    /// <param name="actionType">The type of action to create</param>
    /// <returns>A new instance of the pipeline step, or null if type not supported</returns>
    PipelineStepBase? CreateStep(string actionType);
    
    /// <summary>
    /// Validate plugin configuration
    /// </summary>
    ValidationResult ValidateConfiguration(Dictionary<string, object> configuration);
    
    /// <summary>
    /// Initialize the plugin with configuration
    /// </summary>
    Task InitializeAsync(Dictionary<string, object> configuration, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Cleanup resources when plugin is unloaded
    /// </summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get the JSON schema for configuration of a specific action type
    /// </summary>
    string? GetConfigurationSchema(string actionType);
}

/// <summary>
/// Metadata about a plugin action
/// </summary>
public class ActionMetadata
{
    public string ActionType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public Dictionary<string, PropertyMetadata> Properties { get; set; } = new();
}

/// <summary>
/// Metadata about an action property
/// </summary>
public class PropertyMetadata
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // string, number, boolean, object, array
    public bool Required { get; set; }
    public object? DefaultValue { get; set; }
    public string? ValidationPattern { get; set; }
    public object[]? AllowedValues { get; set; }
}