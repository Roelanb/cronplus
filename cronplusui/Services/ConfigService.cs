using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CronPlus;
using CronPlusUI.Models;
using Newtonsoft.Json;

namespace CronPlusUI.Services;

public class ConfigService
{
    private readonly AppConfigService _appConfigService;
    private string _defaultConfigPath;
    
    public ConfigService(AppConfigService appConfigService)
    {
        _appConfigService = appConfigService;
        _defaultConfigPath = _appConfigService.GetConfig().DefaultServiceConfigPath;
    }
    
    /// <summary>
    /// Load tasks from a JSON configuration file
    /// </summary>
    /// <param name="filePath">Path to the configuration file, or null to use default</param>
    /// <returns>List of task models</returns>
    public async Task<List<TaskModel>> LoadConfigAsync(string? filePath = null)
    {
        string path = filePath ?? _defaultConfigPath;
        
        if (!File.Exists(path))
        {
            return new List<TaskModel>();
        }
        
        try
        {
            string json = await File.ReadAllTextAsync(path);
            var configs = JsonConvert.DeserializeObject<List<TaskConfig>>(json);
            
            var models = new List<TaskModel>();
            if (configs != null)
            {
                foreach (var config in configs)
                {
                    models.Add(TaskModel.FromTaskConfig(config));
                }
            }
            
            return models;
        }
        catch (Exception ex)
        {
            // In a real app, you might want to log this error
            Console.WriteLine($"Error loading configuration: {ex.Message}");
            return new List<TaskModel>();
        }
    }
    
    /// <summary>
    /// Save tasks to a JSON configuration file
    /// </summary>
    /// <param name="tasks">List of task models to save</param>
    /// <param name="filePath">Path to the configuration file, or null to use default</param>
    public async Task SaveConfigAsync(List<TaskModel> tasks, string? filePath = null)
    {
        string path = filePath ?? _defaultConfigPath;
        
        try
        {
            var configs = new List<TaskConfig>();
            foreach (var task in tasks)
            {
                configs.Add(task.ToTaskConfig());
            }
            
            string json = JsonConvert.SerializeObject(configs, Formatting.Indented);
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            // In a real app, you might want to log this error
            Console.WriteLine($"Error saving configuration: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Set the default configuration path
    /// </summary>
    /// <param name="path">New path to use as default</param>
    public async Task SetDefaultConfigPathAsync(string path)
    {
        _defaultConfigPath = path;
        
        // Update the app config with the new path
        var appConfig = _appConfigService.GetConfig();
        appConfig.DefaultServiceConfigPath = path;
        await _appConfigService.SaveConfigAsync(appConfig);
    }
    
    /// <summary>
    /// Get the current default configuration path
    /// </summary>
    public string GetDefaultConfigPath()
    {
        return _defaultConfigPath;
    }
}
