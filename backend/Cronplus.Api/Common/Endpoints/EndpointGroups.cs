using FastEndpoints;

namespace Cronplus.Api.Common.Endpoints;

/// <summary>
/// Endpoint group for Task-related endpoints
/// </summary>
public class TasksGroup : Group
{
    public TasksGroup()
    {
        Configure("tasks", ep =>
        {
            ep.Description(x => x
                .WithTags("Tasks")
                .ProducesProblemDetails(401)
                .ProducesProblemDetails(403));
        });
    }
}

/// <summary>
/// Endpoint group for Pipeline-related endpoints
/// </summary>
public class PipelineGroup : Group
{
    public PipelineGroup()
    {
        Configure("pipeline", ep =>
        {
            ep.Description(x => x
                .WithTags("Pipeline")
                .ProducesProblemDetails(401)
                .ProducesProblemDetails(403));
        });
    }
}

/// <summary>
/// Endpoint group for Variables-related endpoints
/// </summary>
public class VariablesGroup : Group
{
    public VariablesGroup()
    {
        Configure("variables", ep =>
        {
            ep.Description(x => x
                .WithTags("Variables")
                .ProducesProblemDetails(401)
                .ProducesProblemDetails(403));
        });
    }
}

/// <summary>
/// Endpoint group for Monitoring endpoints
/// </summary>
public class MonitoringGroup : Group
{
    public MonitoringGroup()
    {
        Configure("monitoring", ep =>
        {
            ep.Description(x => x
                .WithTags("Monitoring")
                .AllowAnonymous());
        });
    }
}

/// <summary>
/// Endpoint group for Execution/Logs endpoints
/// </summary>
public class ExecutionGroup : Group
{
    public ExecutionGroup()
    {
        Configure("execution", ep =>
        {
            ep.Description(x => x
                .WithTags("Execution")
                .ProducesProblemDetails(401)
                .ProducesProblemDetails(403));
        });
    }
}