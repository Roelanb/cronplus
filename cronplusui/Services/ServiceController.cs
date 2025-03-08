using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CronPlusUI.Services;

public class ServiceController
{
    private Process? _serviceProcess;
    private string _servicePath;
    private string _configPath;
    
    public bool IsRunning => _serviceProcess != null && !_serviceProcess.HasExited;
    
    public event EventHandler<string>? ServiceOutput;
    public event EventHandler<string>? ServiceError;
    public event EventHandler? ServiceStopped;
    
    public ServiceController(string servicePath, string configPath)
    {
        _servicePath = servicePath;
        _configPath = configPath;
    }
    
    /// <summary>
    /// Start the CronPlus service with the specified configuration
    /// </summary>
    public async Task<bool> StartServiceAsync()
    {
        if (IsRunning)
        {
            return true;
        }
        
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetExecutablePath(),
                Arguments = _configPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            _serviceProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            
            _serviceProcess.OutputDataReceived += (_, e) => 
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    ServiceOutput?.Invoke(this, e.Data);
                }
            };
            
            _serviceProcess.ErrorDataReceived += (_, e) => 
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    ServiceError?.Invoke(this, e.Data);
                }
            };
            
            _serviceProcess.Exited += (_, _) => 
            {
                ServiceStopped?.Invoke(this, EventArgs.Empty);
                _serviceProcess = null;
            };
            
            if (_serviceProcess.Start())
            {
                _serviceProcess.BeginOutputReadLine();
                _serviceProcess.BeginErrorReadLine();
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            ServiceError?.Invoke(this, $"Failed to start service: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Stop the CronPlus service if it's running
    /// </summary>
    public async Task<bool> StopServiceAsync()
    {
        if (!IsRunning || _serviceProcess == null)
        {
            return true;
        }
        
        try
        {
            _serviceProcess.Kill();
            await _serviceProcess.WaitForExitAsync();
            _serviceProcess = null;
            return true;
        }
        catch (Exception ex)
        {
            ServiceError?.Invoke(this, $"Failed to stop service: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Restart the CronPlus service
    /// </summary>
    public async Task<bool> RestartServiceAsync()
    {
        await StopServiceAsync();
        return await StartServiceAsync();
    }
    
    /// <summary>
    /// Update the configuration path
    /// </summary>
    public void UpdateConfigPath(string configPath)
    {
        _configPath = configPath;
    }
    
    private string GetExecutablePath()
    {
        // On Windows, we need to add .exe extension
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.ChangeExtension(_servicePath, ".exe");
        }
        
        // For Linux/macOS, use dotnet to run the DLL
        return $"dotnet {_servicePath}";
    }
}
