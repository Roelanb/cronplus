using System;
using System.IO;
using System.Windows.Input;
using Avalonia.Controls;
using CronPlusUI.Commands;
using CronPlusUI.Services;
using System.Threading.Tasks;

namespace CronPlusUI.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly ServiceController _serviceController;
    private TaskListViewModel _taskListViewModel;
    private string _serviceStatus = "Not Running";
    private string _serviceOutput = string.Empty;
    private bool _isServiceRunning = false;
    
    public MainWindowViewModel()
    {
        // Set up service controller with paths to the cronplus service
        string serviceDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".." ,".." ,".." ,".." ,".." ,"cronplusservice");
        string servicePath = Path.Combine(serviceDirectory, "bin", "Debug", "net8.0", "cronplus.dll");
        string configPath = Path.Combine(serviceDirectory, "Config.json");
        
        _serviceController = new ServiceController(servicePath, configPath);
        _taskListViewModel = new TaskListViewModel();
        
        // Update the config path in the task list to point to our service config
        _taskListViewModel.ConfigPath = configPath;
        
        // Subscribe to service events
        _serviceController.ServiceOutput += (sender, output) => 
        {
            ServiceOutput += output + "\n";
        };
        
        _serviceController.ServiceError += (sender, error) => 
        {
            ServiceOutput += "ERROR: " + error + "\n";
        };
        
        _serviceController.ServiceStopped += (sender, args) => 
        {
            IsServiceRunning = false;
            ServiceStatus = "Stopped";
        };
        
        // Initialize commands
        StartServiceCommand = new DelegateCommand(async () => await StartService());
        StopServiceCommand = new DelegateCommand(async () => await StopService());
        RestartServiceCommand = new DelegateCommand(async () => await RestartService());
        ClearOutputCommand = new DelegateCommand(ClearOutput);
    }
    
    // Properties
    public TaskListViewModel TaskListViewModel
    {
        get => _taskListViewModel;
        set => SetProperty(ref _taskListViewModel, value);
    }
    
    public string ServiceStatus
    {
        get => _serviceStatus;
        set => SetProperty(ref _serviceStatus, value);
    }
    
    public string ServiceOutput
    {
        get => _serviceOutput;
        set => SetProperty(ref _serviceOutput, value);
    }
    
    public bool IsServiceRunning
    {
        get => _isServiceRunning;
        set => SetProperty(ref _isServiceRunning, value);
    }
    
    // Commands
    public ICommand StartServiceCommand { get; }
    public ICommand StopServiceCommand { get; }
    public ICommand RestartServiceCommand { get; }
    public ICommand ClearOutputCommand { get; }
    
    // Command methods
    private async Task StartService()
    {
        // Save config before starting to ensure latest changes are used
        await TaskListViewModel.SaveConfigAsync();
        
        // Update service config path in case it changed
        _serviceController.UpdateConfigPath(TaskListViewModel.ConfigPath);
        
        ServiceOutput += "Starting CronPlus service...\n";
        bool success = await _serviceController.StartServiceAsync();
        
        if (success)
        {
            IsServiceRunning = true;
            ServiceStatus = "Running";
        }
        else
        {
            ServiceStatus = "Failed to start";
        }
    }
    
    private async Task StopService()
    {
        ServiceOutput += "Stopping CronPlus service...\n";
        bool success = await _serviceController.StopServiceAsync();
        
        if (success)
        {
            IsServiceRunning = false;
            ServiceStatus = "Stopped";
        }
    }
    
    private async Task RestartService()
    {
        // Save config before restarting
        await TaskListViewModel.SaveConfigAsync();
        
        ServiceOutput += "Restarting CronPlus service...\n";
        bool success = await _serviceController.RestartServiceAsync();
        
        if (success)
        {
            IsServiceRunning = true;
            ServiceStatus = "Running";
        }
        else
        {
            ServiceStatus = "Failed to restart";
        }
    }
    
    private void ClearOutput()
    {
        ServiceOutput = string.Empty;
    }
}
