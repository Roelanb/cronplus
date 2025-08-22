using FastEndpoints;
using FastEndpoints.Swagger;
using Serilog;
using Serilog.Events;
using FluentValidation;
using Cronplus.Api.Infrastructure.Database;
using Cronplus.Api.Infrastructure.Database.Repositories;
using Cronplus.Api.Infrastructure.Services;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Common.Processors;
using Cronplus.Api.Common.Security;
using Cronplus.Api.Services.FileWatching;
using Cronplus.Api.Services.TaskSupervision;
using Microsoft.AspNetCore.Mvc;
using Cronplus.Api.Hubs;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        "logs/cronplus-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Cronplus API");
    
    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            "logs/cronplus-.log",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

    // Add services to the container
    builder.Services.AddFastEndpoints();
    
    // Configure Swagger documentation
    builder.Services.SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Cronplus API";
            s.Version = "v1";
            s.Description = "REST API for Cronplus - File-driven automation system";
        };
        o.EnableJWTBearerAuth = true;
        o.ShortSchemaNames = true;
    });

    // Add FluentValidation
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();
    
    // Add HttpContext accessor for UserContext
    builder.Services.AddHttpContextAccessor();
    
    // Add caching support
    builder.Services.AddEndpointCaching();
    
    // Add user context service
    builder.Services.AddScoped<IUserContext, UserContext>();
    
    // Add authentication and authorization
    builder.Services.AddCronplusSecurity(builder.Configuration);
    
    // Add custom processors as scoped services
    // These will be used by endpoints that explicitly reference them

    // Configure CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins(
                    "http://localhost:5173",  // Vite default
                    "http://localhost:3000")   // Alternative React port
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    // Add health checks
    builder.Services.AddHealthChecks();

    // Add SignalR for real-time updates
    builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    });

    // Configure DuckDB
    builder.Services.Configure<DatabaseSettings>(
        builder.Configuration.GetSection("DatabaseSettings"));
    
    // Register database services
    builder.Services.AddScoped<IDatabaseContext, DuckDbContext>();
    builder.Services.AddScoped<IDbConnectionFactory, DuckDbConnectionFactory>();
    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
    builder.Services.AddScoped<ITaskRepository, TaskRepository>();
    builder.Services.AddScoped<IPipelineStepRepository, PipelineStepRepository>();
    builder.Services.AddScoped<ITaskVariableRepository, TaskVariableRepository>();
    builder.Services.AddScoped<IVariableRepository, VariableRepository>();
    builder.Services.AddScoped<IExecutionLogRepository, ExecutionLogRepository>();
    builder.Services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
    
    // Register pipeline execution services
    builder.Services.AddScoped<IPipelineExecutor, PipelineExecutor>();
    builder.Services.AddScoped<IVariableInterpolator, VariableInterpolator>();
    builder.Services.AddScoped<IDeadLetterQueue, DeadLetterQueue>();
    
    // Register action plugin services
    builder.Services.AddSingleton<IActionFactory, ActionFactory>();
    builder.Services.AddSingleton<IActionValidator, ActionValidator>();
    builder.Services.AddSingleton<IPluginLoader, PluginLoader>();
    
    // Configure file watching services
    builder.Services.Configure<FileWatcherOptions>(builder.Configuration.GetSection("FileWatcher"));
    builder.Services.Configure<FileProcessorOptions>(builder.Configuration.GetSection("FileProcessor"));
    
    // Register file watching services
    builder.Services.AddSingleton<IFileWatcherService, FileWatcherService>();
    builder.Services.AddSingleton<IFileEventProcessor, FileEventProcessor>();
    builder.Services.AddSingleton<FileEventProcessor>();
    builder.Services.AddHostedService<FileEventProcessor>(provider => provider.GetRequiredService<FileEventProcessor>());
    
    // Register task supervision services
    builder.Services.AddSingleton<ITaskSupervisorManager, TaskSupervisorManager>();
    builder.Services.AddHostedService<TaskSupervisorManager>(provider => 
        provider.GetRequiredService<ITaskSupervisorManager>() as TaskSupervisorManager ?? 
        throw new InvalidOperationException("TaskSupervisorManager not found"));
    
    // Note: FileWatcherHostedService is replaced by TaskSupervisorManager
    // builder.Services.AddHostedService<FileWatcherHostedService>();
    
    // Register SignalR notification services
    builder.Services.AddSingleton<ITaskExecutionNotifier, TaskExecutionNotifier>();
    
    // Register metrics broadcast service
    builder.Services.AddHostedService<MetricsBroadcastService>();

    var app = builder.Build();
    
    // Initialize database
    using (var scope = app.Services.CreateScope())
    {
        var dbInitializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
        await dbInitializer.InitializeAsync();
    }

    // Configure the HTTP request pipeline
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetLevel = (httpContext, elapsed, ex) => LogEventLevel.Debug;
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? "unknown");
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].FirstOrDefault() ?? "unknown");
        };
    });

    // Configure middleware pipeline
    app.UseCors("AllowFrontend");
    
    // Add authentication and authorization middleware
    app.UseAuthentication();
    app.UseAuthorization();
    
    // FastEndpoints middleware
    app.UseFastEndpoints(config =>
    {
        // Don't use RoutePrefix since endpoints already include /api in their routes
        config.Serializer.Options.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        
        // Configure global pre/post processors
        config.Endpoints.Configurator = ep =>
        {
            // Note: Custom processors would be registered here per endpoint
            // Global processors are registered differently in FastEndpoints
            
            ep.Description(b => b
                .Produces(500, typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))
                .ProducesProblemFE(400)
                .ProducesProblemFE(401)
                .ProducesProblemFE(403));
        };
        
        // Configure error handling
        config.Errors.ResponseBuilder = (failures, ctx, statusCode) =>
        {
            return new ValidationProblemDetails(failures.ToDictionary(
                f => f.PropertyName,
                f => new[] { f.ErrorMessage }))
            {
                Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1",
                Title = "One or more validation errors occurred.",
                Status = statusCode,
                Instance = ctx.Request.Path,
                Extensions = { ["traceId"] = ctx.TraceIdentifier }
            };
        };
    });

    // Health check endpoint
    app.MapHealthChecks("/health");

    // Swagger documentation - FastEndpoints style
    app.UseOpenApi();
    app.UseSwaggerUi(c =>
    {
        c.DocumentTitle = "Cronplus API Documentation";
        c.Path = "/swagger";
        c.DocumentPath = "/swagger/{documentName}/swagger.json";
        c.DocExpansion = "list";
        c.PersistAuthorization = true;
        c.EnableTryItOut = true;
    });

    // Map SignalR hubs
    app.MapHub<TaskExecutionHub>("/hubs/tasks");
    app.MapHub<MonitoringHub>("/hubs/monitoring");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}