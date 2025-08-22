using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Infrastructure.Database.Repositories;

namespace Cronplus.Api.Infrastructure.Database;

public interface IUnitOfWork : IDisposable
{
    ITaskRepository Tasks { get; }
    IPipelineStepRepository PipelineSteps { get; }
    ITaskVariableRepository TaskVariables { get; }
    IExecutionLogRepository ExecutionLogs { get; }
    
    void BeginTransaction();
    void Commit();
    Task CommitAsync();
    void Rollback();
    Task<int> SaveChangesAsync();
}

public class UnitOfWork : IUnitOfWork
{
    private readonly IDatabaseContext _context;
    private readonly ILoggerFactory _loggerFactory;
    private bool _disposed;

    private ITaskRepository? _tasks;
    private IPipelineStepRepository? _pipelineSteps;
    private ITaskVariableRepository? _taskVariables;
    private IExecutionLogRepository? _executionLogs;

    public UnitOfWork(IDatabaseContext context, ILoggerFactory loggerFactory)
    {
        _context = context;
        _loggerFactory = loggerFactory;
    }

    public ITaskRepository Tasks => 
        _tasks ??= new TaskRepository(_context, _loggerFactory.CreateLogger<TaskRepository>());

    public IPipelineStepRepository PipelineSteps => 
        _pipelineSteps ??= new PipelineStepRepository(_context, _loggerFactory.CreateLogger<PipelineStepRepository>());

    public ITaskVariableRepository TaskVariables => 
        _taskVariables ??= new TaskVariableRepository(_context, _loggerFactory.CreateLogger<TaskVariableRepository>());

    public IExecutionLogRepository ExecutionLogs => 
        _executionLogs ??= new ExecutionLogRepository(_context, _loggerFactory.CreateLogger<ExecutionLogRepository>());

    public void BeginTransaction()
    {
        _context.BeginTransaction();
    }

    public void Commit()
    {
        _context.Commit();
    }

    public Task CommitAsync()
    {
        _context.Commit();
        return Task.CompletedTask;
    }

    public void Rollback()
    {
        _context.Rollback();
    }

    public Task<int> SaveChangesAsync()
    {
        // In DuckDB, changes are immediately persisted, so this is a no-op
        // but we keep it for consistency with the pattern
        return Task.FromResult(0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _context?.Dispose();
        _disposed = true;
    }
}