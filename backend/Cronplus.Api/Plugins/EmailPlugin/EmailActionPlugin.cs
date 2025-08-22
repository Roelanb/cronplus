using System.Net;
using System.Net.Mail;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Domain.Models.PipelineSteps;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using ExecutionContext = Cronplus.Api.Domain.Models.PipelineSteps.ExecutionContext;

namespace Cronplus.Api.Plugins.EmailPlugin;

/// <summary>
/// Example plugin that provides email sending capability
/// </summary>
public class EmailActionPlugin : IActionPlugin
{
    private SmtpClient? _smtpClient;
    private EmailPluginConfiguration _configuration = new();
    
    public string PluginId => "com.cronplus.plugins.email";
    public string Name => "Email Plugin";
    public string Description => "Provides email sending capabilities for pipeline actions";
    public Version Version => new Version(1, 0, 0);
    public string Author => "Cronplus Team";
    
    public IEnumerable<string> ProvidedActionTypes => new[] { "email", "email-attachment" };
    
    public PipelineStepBase? CreateStep(string actionType)
    {
        return actionType.ToLowerInvariant() switch
        {
            "email" => new EmailStep(_smtpClient ?? CreateSmtpClient()),
            "email-attachment" => new EmailAttachmentStep(_smtpClient ?? CreateSmtpClient()),
            _ => null
        };
    }
    
    public ValidationResult ValidateConfiguration(Dictionary<string, object> configuration)
    {
        var validator = new EmailPluginConfigurationValidator();
        var config = ParseConfiguration(configuration);
        return validator.Validate(config);
    }
    
    public async Task InitializeAsync(Dictionary<string, object> configuration, CancellationToken cancellationToken = default)
    {
        _configuration = ParseConfiguration(configuration);
        
        // Validate configuration
        var validationResult = ValidateConfiguration(configuration);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException($"Invalid plugin configuration: {string.Join(", ", validationResult.Errors)}");
        }
        
        // Initialize SMTP client
        _smtpClient = CreateSmtpClient();
        
        await Task.CompletedTask;
    }
    
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _smtpClient?.Dispose();
        _smtpClient = null;
        await Task.CompletedTask;
    }
    
    public string? GetConfigurationSchema(string actionType)
    {
        return actionType.ToLowerInvariant() switch
        {
            "email" => @"{
                ""type"": ""object"",
                ""properties"": {
                    ""to"": { ""type"": ""string"", ""format"": ""email"" },
                    ""subject"": { ""type"": ""string"" },
                    ""body"": { ""type"": ""string"" },
                    ""isHtml"": { ""type"": ""boolean"" }
                },
                ""required"": [""to"", ""subject"", ""body""]
            }",
            "email-attachment" => @"{
                ""type"": ""object"",
                ""properties"": {
                    ""to"": { ""type"": ""string"", ""format"": ""email"" },
                    ""subject"": { ""type"": ""string"" },
                    ""body"": { ""type"": ""string"" },
                    ""isHtml"": { ""type"": ""boolean"" },
                    ""attachFile"": { ""type"": ""boolean"" }
                },
                ""required"": [""to"", ""subject"", ""body""]
            }",
            _ => null
        };
    }
    
    private SmtpClient CreateSmtpClient()
    {
        var client = new SmtpClient(_configuration.SmtpHost, _configuration.SmtpPort)
        {
            EnableSsl = _configuration.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };
        
        if (!string.IsNullOrEmpty(_configuration.SmtpUsername) && 
            !string.IsNullOrEmpty(_configuration.SmtpPassword))
        {
            client.Credentials = new NetworkCredential(_configuration.SmtpUsername, _configuration.SmtpPassword);
        }
        
        return client;
    }
    
    private EmailPluginConfiguration ParseConfiguration(Dictionary<string, object> configuration)
    {
        var config = new EmailPluginConfiguration();
        
        if (configuration.TryGetValue("smtpHost", out var host))
            config.SmtpHost = host?.ToString() ?? "localhost";
        
        if (configuration.TryGetValue("smtpPort", out var port) && int.TryParse(port?.ToString(), out var portInt))
            config.SmtpPort = portInt;
        
        if (configuration.TryGetValue("smtpUsername", out var username))
            config.SmtpUsername = username?.ToString();
        
        if (configuration.TryGetValue("smtpPassword", out var password))
            config.SmtpPassword = password?.ToString();
        
        if (configuration.TryGetValue("enableSsl", out var ssl) && bool.TryParse(ssl?.ToString(), out var sslBool))
            config.EnableSsl = sslBool;
        
        if (configuration.TryGetValue("fromEmail", out var from))
            config.FromEmail = from?.ToString() ?? "noreply@cronplus.local";
        
        if (configuration.TryGetValue("fromName", out var fromName))
            config.FromName = fromName?.ToString() ?? "Cronplus";
        
        return config;
    }
}

/// <summary>
/// Configuration for the email plugin
/// </summary>
public class EmailPluginConfiguration
{
    public string SmtpHost { get; set; } = "localhost";
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool EnableSsl { get; set; } = true;
    public string FromEmail { get; set; } = "noreply@cronplus.local";
    public string FromName { get; set; } = "Cronplus";
}

/// <summary>
/// Validator for email plugin configuration
/// </summary>
public class EmailPluginConfigurationValidator : AbstractValidator<EmailPluginConfiguration>
{
    public EmailPluginConfigurationValidator()
    {
        RuleFor(x => x.SmtpHost)
            .NotEmpty().WithMessage("SMTP host is required");
        
        RuleFor(x => x.SmtpPort)
            .InclusiveBetween(1, 65535).WithMessage("SMTP port must be between 1 and 65535");
        
        RuleFor(x => x.FromEmail)
            .NotEmpty().WithMessage("From email is required")
            .EmailAddress().WithMessage("From email must be a valid email address");
    }
}

/// <summary>
/// Email sending step
/// </summary>
public class EmailStep : PipelineStepBase
{
    private readonly SmtpClient _smtpClient;
    
    public override string StepType => "email";
    
    public string To { get; set; } = string.Empty;
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; } = false;
    public string? ReplyTo { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    
    public EmailStep(SmtpClient smtpClient)
    {
        _smtpClient = smtpClient;
    }
    
    public override async Task<StepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            context.Logger?.LogInformation("Executing EmailStep: {Name}", Name);
            
            // Resolve variables in email properties
            var resolvedTo = ResolveVariables(To, context);
            var resolvedSubject = ResolveVariables(Subject, context);
            var resolvedBody = ResolveVariables(Body, context);
            
            // Create email message
            var message = new MailMessage
            {
                Subject = resolvedSubject,
                Body = resolvedBody,
                IsBodyHtml = IsHtml
            };
            
            // Add recipients
            foreach (var recipient in resolvedTo.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                message.To.Add(recipient.Trim());
            }
            
            if (!string.IsNullOrEmpty(Cc))
            {
                foreach (var recipient in ResolveVariables(Cc, context).Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    message.CC.Add(recipient.Trim());
                }
            }
            
            if (!string.IsNullOrEmpty(Bcc))
            {
                foreach (var recipient in ResolveVariables(Bcc, context).Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    message.Bcc.Add(recipient.Trim());
                }
            }
            
            // Add headers
            if (Headers != null)
            {
                foreach (var header in Headers)
                {
                    message.Headers.Add(header.Key, ResolveVariables(header.Value, context));
                }
            }
            
            // Send email
            await _smtpClient.SendMailAsync(message, cancellationToken);
            
            stopwatch.Stop();
            
            var outputs = new Dictionary<string, object>
            {
                ["Recipients"] = resolvedTo,
                ["Subject"] = resolvedSubject,
                ["MessageId"] = message.Headers["Message-ID"] ?? Guid.NewGuid().ToString()
            };
            
            context.Logger?.LogInformation("Email sent successfully to: {Recipients}", resolvedTo);
            
            var result = StepResult.SuccessResult($"Email sent to {resolvedTo}", outputs);
            result.ExecutionTime = stopwatch.Elapsed;
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            context.Logger?.LogError(ex, "Failed to send email in step: {Name}", Name);
            var result = StepResult.FailureResult($"Email sending failed: {ex.Message}", ex);
            result.ExecutionTime = stopwatch.Elapsed;
            return result;
        }
    }
    
    public override ValidationResult Validate()
    {
        var validator = new EmailStepValidator();
        return validator.Validate(this);
    }
    
    private string ResolveVariables(string text, ExecutionContext context)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        var result = text;
        
        // Replace built-in variables
        result = result.Replace("{fileName}", context.FileName);
        result = result.Replace("{filePath}", context.FilePath);
        result = result.Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"));
        result = result.Replace("{time}", DateTime.Now.ToString("HH:mm:ss"));
        
        // Replace custom variables
        foreach (var variable in context.Variables)
        {
            result = result.Replace($"{{{variable.Key}}}", variable.Value?.ToString() ?? string.Empty);
        }
        
        return result;
    }
}

/// <summary>
/// Email with attachment step
/// </summary>
public class EmailAttachmentStep : EmailStep
{
    public override string StepType => "email-attachment";
    
    public bool AttachFile { get; set; } = true;
    public string? AdditionalAttachments { get; set; } // Comma-separated paths
    
    public EmailAttachmentStep(SmtpClient smtpClient) : base(smtpClient)
    {
    }
    
    public override async Task<StepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        // For email with attachment, we override to add the file
        // This is a simplified implementation
        if (AttachFile && File.Exists(context.FilePath))
        {
            // Would add attachment logic here
            context.Logger?.LogDebug("Attaching file: {FilePath}", context.FilePath);
        }
        
        return await base.ExecuteAsync(context, cancellationToken);
    }
}

/// <summary>
/// Validator for email step
/// </summary>
public class EmailStepValidator : AbstractValidator<EmailStep>
{
    public EmailStepValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Step name is required");
        
        RuleFor(x => x.To)
            .NotEmpty().WithMessage("To address is required");
        
        RuleFor(x => x.Subject)
            .NotEmpty().WithMessage("Subject is required");
        
        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("Body is required");
    }
}