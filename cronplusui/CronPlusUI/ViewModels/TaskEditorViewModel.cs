using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia.Controls;
using CronPlusUI.Commands;
using CronPlusUI.Models;
using CronPlusUI.Services;
using System.Threading.Tasks;

namespace CronPlusUI.ViewModels;

public class TaskEditorViewModel : ViewModelBase
{
    private readonly PrinterService _printerService;
    private TaskModel _task;
    private List<string> _availablePrinters = new();
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    
    public TaskEditorViewModel(TaskModel task)
    {
        _printerService = new PrinterService();
        _task = task;
        
        // Commands
        BrowseDirectoryCommand = new DelegateCommand<Window>(async (window) => await BrowseDirectoryAsync(window));
        BrowseSourceFileCommand = new DelegateCommand<Window>(async (window) => await BrowseSourceFileAsync(window));
        BrowseDestinationFileCommand = new DelegateCommand<Window>(async (window) => await BrowseDestinationFileAsync(window));
        BrowseArchiveDirectoryCommand = new DelegateCommand<Window>(async (window) => await BrowseArchiveDirectoryAsync(window));
        RefreshPrintersCommand = new DelegateCommand(async () => await LoadPrintersAsync());
        
        // Initial load of printers
        _ = LoadPrintersAsync();
    }
    
    // Properties
    public TaskModel Task
    {
        get => _task;
        set => SetProperty(ref _task, value);
    }
    
    public List<string> AvailablePrinters
    {
        get => _availablePrinters;
        set => SetProperty(ref _availablePrinters, value);
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
    
    // Available options from TaskModel for the UI
    public List<string> AvailableTriggerTypes => TaskModel.AvailableTriggerTypes;
    public List<string> AvailableTaskTypes => TaskModel.AvailableTaskTypes;
    
    // Commands
    public ICommand BrowseDirectoryCommand { get; }
    public ICommand BrowseSourceFileCommand { get; }
    public ICommand BrowseDestinationFileCommand { get; }
    public ICommand BrowseArchiveDirectoryCommand { get; }
    public ICommand RefreshPrintersCommand { get; }
    
    // Command methods
    private async Task BrowseDirectoryAsync(Window window)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Directory to Watch"
        };
        
        var result = await dialog.ShowAsync(window);
        if (!string.IsNullOrEmpty(result))
        {
            Task.Directory = result;
            OnPropertyChanged(nameof(Task));
        }
    }
    
    private async Task BrowseSourceFileAsync(Window window)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Source File",
            AllowMultiple = false
        };
        
        var result = await dialog.ShowAsync(window);
        if (result != null && result.Length > 0)
        {
            Task.SourceFile = result[0];
            OnPropertyChanged(nameof(Task));
        }
    }
    
    private async Task BrowseDestinationFileAsync(Window window)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Select Destination File"
        };
        
        var result = await dialog.ShowAsync(window);
        if (!string.IsNullOrEmpty(result))
        {
            Task.DestinationFile = result;
            OnPropertyChanged(nameof(Task));
        }
    }
    
    private async Task BrowseArchiveDirectoryAsync(Window window)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Archive Directory"
        };
        
        var result = await dialog.ShowAsync(window);
        if (!string.IsNullOrEmpty(result))
        {
            Task.ArchiveDirectory = result;
            OnPropertyChanged(nameof(Task));
        }
    }
    
    private async Task LoadPrintersAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading printers...";
            
            AvailablePrinters = await _printerService.GetAvailablePrintersAsync();
            
            // If we have no printer selected but have available printers, select the first one
            if (string.IsNullOrEmpty(Task.PrinterName) && AvailablePrinters.Count > 0)
            {
                // Try to get the default printer first
                var defaultPrinter = await _printerService.GetDefaultPrinterAsync();
                
                if (!string.IsNullOrEmpty(defaultPrinter) && AvailablePrinters.Contains(defaultPrinter))
                {
                    Task.PrinterName = defaultPrinter;
                }
                else
                {
                    Task.PrinterName = AvailablePrinters[0];
                }
                
                OnPropertyChanged(nameof(Task));
            }
            
            StatusMessage = $"Loaded {AvailablePrinters.Count} printers";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading printers: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
