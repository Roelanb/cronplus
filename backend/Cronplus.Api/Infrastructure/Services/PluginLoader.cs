using System.Reflection;
using System.Runtime.Loader;
using Cronplus.Api.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cronplus.Api.Infrastructure.Services;

/// <summary>
/// Service for loading and managing action plugins
/// </summary>
public interface IPluginLoader
{
    /// <summary>
    /// Load a plugin from an assembly file
    /// </summary>
    Task<IActionPlugin?> LoadPluginAsync(string assemblyPath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Load all plugins from a directory
    /// </summary>
    Task<IEnumerable<IActionPlugin>> LoadPluginsFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Unload a plugin
    /// </summary>
    Task UnloadPluginAsync(string pluginId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all loaded plugins
    /// </summary>
    IEnumerable<IActionPlugin> GetLoadedPlugins();
}

/// <summary>
/// Implementation of the plugin loader
/// </summary>
public class PluginLoader : IPluginLoader
{
    private readonly ILogger<PluginLoader> _logger;
    private readonly IActionFactory _actionFactory;
    private readonly Dictionary<string, PluginContext> _loadedPlugins;
    
    public PluginLoader(ILogger<PluginLoader> logger, IActionFactory actionFactory)
    {
        _logger = logger;
        _actionFactory = actionFactory;
        _loadedPlugins = new Dictionary<string, PluginContext>();
    }
    
    public async Task<IActionPlugin?> LoadPluginAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(assemblyPath))
        {
            _logger.LogError("Plugin assembly not found: {AssemblyPath}", assemblyPath);
            return null;
        }
        
        try
        {
            _logger.LogInformation("Loading plugin from: {AssemblyPath}", assemblyPath);
            
            // Create a new AssemblyLoadContext for plugin isolation
            var pluginContext = new PluginAssemblyLoadContext(assemblyPath);
            
            // Load the assembly
            var assembly = pluginContext.LoadFromAssemblyPath(assemblyPath);
            
            // Find types that implement IActionPlugin
            var pluginTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IActionPlugin).IsAssignableFrom(t))
                .ToList();
            
            if (!pluginTypes.Any())
            {
                _logger.LogWarning("No plugin types found in assembly: {AssemblyPath}", assemblyPath);
                return null;
            }
            
            if (pluginTypes.Count > 1)
            {
                _logger.LogWarning("Multiple plugin types found in assembly: {AssemblyPath}. Using first one.", assemblyPath);
            }
            
            var pluginType = pluginTypes.First();
            
            // Create an instance of the plugin
            var plugin = Activator.CreateInstance(pluginType) as IActionPlugin;
            if (plugin == null)
            {
                _logger.LogError("Failed to create plugin instance from type: {PluginType}", pluginType.FullName);
                return null;
            }
            
            // Register with the action factory
            await _actionFactory.RegisterPluginAsync(plugin, cancellationToken);
            
            // Store the context for later unloading
            _loadedPlugins[plugin.PluginId] = new PluginContext
            {
                Plugin = plugin,
                LoadContext = pluginContext,
                AssemblyPath = assemblyPath
            };
            
            _logger.LogInformation("Successfully loaded plugin: {PluginId} - {PluginName} v{Version}", 
                plugin.PluginId, plugin.Name, plugin.Version);
            
            return plugin;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin from: {AssemblyPath}", assemblyPath);
            return null;
        }
    }
    
    public async Task<IEnumerable<IActionPlugin>> LoadPluginsFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            _logger.LogWarning("Plugin directory not found: {DirectoryPath}", directoryPath);
            return Enumerable.Empty<IActionPlugin>();
        }
        
        _logger.LogInformation("Loading plugins from directory: {DirectoryPath}", directoryPath);
        
        var plugins = new List<IActionPlugin>();
        
        // Look for DLL files in the directory
        var assemblyFiles = Directory.GetFiles(directoryPath, "*.dll", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith("System.") && 
                       !Path.GetFileName(f).StartsWith("Microsoft."));
        
        foreach (var assemblyFile in assemblyFiles)
        {
            try
            {
                var plugin = await LoadPluginAsync(assemblyFile, cancellationToken);
                if (plugin != null)
                {
                    plugins.Add(plugin);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin from file: {AssemblyFile}", assemblyFile);
            }
        }
        
        _logger.LogInformation("Loaded {Count} plugins from directory: {DirectoryPath}", 
            plugins.Count, directoryPath);
        
        return plugins;
    }
    
    public async Task UnloadPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        if (!_loadedPlugins.TryGetValue(pluginId, out var context))
        {
            _logger.LogWarning("Plugin not found for unloading: {PluginId}", pluginId);
            return;
        }
        
        _logger.LogInformation("Unloading plugin: {PluginId}", pluginId);
        
        try
        {
            // Unregister from action factory
            await _actionFactory.UnregisterPluginAsync(pluginId, cancellationToken);
            
            // Shutdown the plugin
            await context.Plugin.ShutdownAsync(cancellationToken);
            
            // Remove from loaded plugins
            _loadedPlugins.Remove(pluginId);
            
            // Unload the assembly context
            context.LoadContext.Unload();
            
            _logger.LogInformation("Successfully unloaded plugin: {PluginId}", pluginId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unloading plugin: {PluginId}", pluginId);
            throw;
        }
    }
    
    public IEnumerable<IActionPlugin> GetLoadedPlugins()
    {
        return _loadedPlugins.Values.Select(c => c.Plugin);
    }
    
    private class PluginContext
    {
        public required IActionPlugin Plugin { get; set; }
        public required PluginAssemblyLoadContext LoadContext { get; set; }
        public required string AssemblyPath { get; set; }
    }
}

/// <summary>
/// Custom AssemblyLoadContext for plugin isolation
/// </summary>
public class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    
    public PluginAssemblyLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }
    
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }
        
        return null;
    }
    
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }
        
        return IntPtr.Zero;
    }
}