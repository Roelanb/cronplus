using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using CronPlusUI.Commands;
using CronPlusUI.Models;
using CronPlusUI.Services;

namespace CronPlusUI.ViewModels;

public class TaskListViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private ObservableCollection<TaskModel> _tasks;
    private TaskModel? _selectedTask;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private string _configPath;

    public TaskListViewModel()
    {
        _configService = new ConfigService();
        _tasks = new ObservableCollection<TaskModel>();
        _configPath = _configService.GetDefaultConfigPath();
        
        // Initialize commands
        AddTaskCommand = new DelegateCommand(AddTask);
        EditTaskCommand = new DelegateCommand<TaskModel>(EditTask);
        DeleteTaskCommand = new DelegateCommand<TaskModel>(DeleteTask);
        LoadConfigCommand = new DelegateCommand(async () => await LoadConfigAsync());
        SaveConfigCommand = new DelegateCommand(async () => await SaveConfigAsync());
        OpenConfigCommand = new DelegateCommand<Window>(async (window) => await OpenConfigFileAsync(window));
        
        // Initial load
        _ = LoadConfigAsync();
    }
    
    // Properties
    public ObservableCollection<TaskModel> Tasks
    {
        get => _tasks;
        set => SetProperty(ref _tasks, value);
    }
    
    public TaskModel? SelectedTask
    {
        get => _selectedTask;
        set => SetProperty(ref _selectedTask, value);
    }
    
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }
    
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
    
    public string ConfigPath
    {
        get => _configPath;
        set => SetProperty(ref _configPath, value);
    }
    
    // Commands
    public ICommand AddTaskCommand { get; }
    public ICommand EditTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand LoadConfigCommand { get; }
    public ICommand SaveConfigCommand { get; }
    public ICommand OpenConfigCommand { get; }
    
    // Command methods
    private void AddTask()
    {
        var newTask = new TaskModel();
        Tasks.Add(newTask);
        SelectedTask = newTask;
        
        // Task added, notify user
        StatusMessage = "New task added. Don't forget to save your changes!";
    }
    
    private void EditTask(TaskModel task)
    {
        SelectedTask = task;
    }
    
    private void DeleteTask(TaskModel task)
    {
        Tasks.Remove(task);
        
        // If we deleted the selected task, clear selection
        if (SelectedTask == task)
        {
            SelectedTask = null;
        }
        
        StatusMessage = "Task deleted. Don't forget to save your changes!";
    }
    
    public async Task LoadConfigAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading configuration...";
            
            var loadedTasks = await _configService.LoadConfigAsync(ConfigPath);
            Tasks.Clear();
            
            foreach (var task in loadedTasks)
            {
                Tasks.Add(task);
            }
            
            StatusMessage = $"Loaded {Tasks.Count} tasks from {ConfigPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading configuration: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    public async Task SaveConfigAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Saving configuration...";
            
            await _configService.SaveConfigAsync(Tasks.ToList(), ConfigPath);
            
            StatusMessage = $"Saved {Tasks.Count} tasks to {ConfigPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving configuration: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private async Task OpenConfigFileAsync(Window window)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Config File",
            Filters = new()
            {
                new FileDialogFilter { Name = "JSON Files", Extensions = { "json" } },
                new FileDialogFilter { Name = "All Files", Extensions = { "*" } }
            }
        };
        
        var result = await dialog.ShowAsync(window);
        if (result != null && result.Length > 0)
        {
            ConfigPath = result[0];
            _configService.SetDefaultConfigPath(ConfigPath);
            await LoadConfigAsync();
        }
    }
}
