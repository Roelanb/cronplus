using System.Text.Json;
using Cronplus.Api.Domain.Entities;
using Cronplus.Api.Domain.Models.PipelineSteps;

namespace Cronplus.Api.Infrastructure.Services;

/// <summary>
/// Adapter to convert between Entity and Domain models for pipeline steps
/// </summary>
public static class PipelineStepAdapter
{
    /// <summary>
    /// Convert a database entity to a domain model
    /// </summary>
    public static PipelineStepBase? ConvertToDomainModel(PipelineStep entity)
    {
        if (entity == null)
            return null;

        // Parse the configuration JSON
        var config = entity.Configuration?.RootElement;
        
        PipelineStepBase? step = entity.Type?.ToLowerInvariant() switch
        {
            "copy" => new CopyStep(),
            "delete" => new DeleteStep(),
            "archive" => new ArchiveStep(),
            "print" => new PrintStep(),
            "restapi" => new RestApiStep(),
            "decision" => new DecisionStep(),
            _ => null
        };

        if (step == null)
            return null;

        // Set common properties
        step.Id = entity.Id;
        step.TaskId = entity.TaskId;
        step.StepOrder = entity.StepOrder;
        step.Name = config?.TryGetProperty("name", out var name) == true 
            ? name.GetString() ?? entity.Type 
            : entity.Type;
        step.Description = config?.TryGetProperty("description", out var desc) == true 
            ? desc.GetString() 
            : null;
        step.Enabled = config?.TryGetProperty("enabled", out var enabled) == true 
            ? enabled.GetBoolean() 
            : true;

        // Set retry policy
        if (entity.RetryMax.HasValue && entity.RetryMax.Value > 0)
        {
            step.RetryPolicy = new RetryPolicy
            {
                MaxAttempts = entity.RetryMax.Value,
                BackoffMilliseconds = entity.RetryBackoffMs ?? 1000,
                BackoffType = RetryBackoffType.Exponential
            };
        }

        // Set timeout
        if (config?.TryGetProperty("timeoutSeconds", out var timeout) == true)
        {
            step.TimeoutSeconds = timeout.GetInt32();
        }

        // Set step-specific properties based on type
        switch (step)
        {
            case CopyStep copyStep:
                if (config?.TryGetProperty("destination", out var dest) == true)
                    copyStep.DestinationPath = dest.GetString() ?? "";
                if (config?.TryGetProperty("overwrite", out var overwrite) == true)
                    copyStep.Overwrite = overwrite.GetBoolean();
                if (config?.TryGetProperty("createDirectories", out var createDir) == true)
                    copyStep.CreateDirectories = createDir.GetBoolean();
                if (config?.TryGetProperty("renamePattern", out var rename) == true)
                    copyStep.RenamePattern = rename.GetString();
                break;

            case DeleteStep deleteStep:
                if (config?.TryGetProperty("deleteEmptyDirectories", out var delEmpty) == true)
                    deleteStep.DeleteEmptyDirectories = delEmpty.GetBoolean();
                if (config?.TryGetProperty("requireConfirmation", out var confirm) == true)
                    deleteStep.RequireConfirmation = confirm.GetBoolean();
                if (config?.TryGetProperty("minFileAgeMinutes", out var minAge) == true)
                    deleteStep.MinFileAgeMinutes = minAge.GetInt64();
                if (config?.TryGetProperty("filePattern", out var pattern) == true)
                    deleteStep.FilePattern = pattern.GetString();
                break;

            case ArchiveStep archiveStep:
                if (config?.TryGetProperty("archivePath", out var archPath) == true)
                    archiveStep.ArchivePath = archPath.GetString() ?? "";
                if (config?.TryGetProperty("format", out var format) == true)
                    archiveStep.Format = Enum.TryParse<ArchiveFormat>(format.GetString(), true, out var fmt) 
                        ? fmt 
                        : ArchiveFormat.Zip;
                if (config?.TryGetProperty("compressionLevel", out var compLevel) == true)
                {
                    var level = compLevel.GetInt32();
                    archiveStep.CompressionLevel = (System.IO.Compression.CompressionLevel)level;
                }
                break;

            case PrintStep printStep:
                // PrintStep doesn't have many configurable properties in the current implementation
                // The actual printing logic is in the ExecuteAsync method
                break;

            case RestApiStep apiStep:
                if (config?.TryGetProperty("url", out var url) == true)
                    apiStep.Url = url.GetString() ?? "";
                if (config?.TryGetProperty("method", out var method) == true)
                {
                    var methodStr = method.GetString() ?? "GET";
                    apiStep.Method = new System.Net.Http.HttpMethod(methodStr);
                }
                if (config?.TryGetProperty("headers", out var headers) == true && headers.ValueKind == JsonValueKind.Object)
                {
                    apiStep.Headers = new Dictionary<string, string>();
                    foreach (var header in headers.EnumerateObject())
                    {
                        apiStep.Headers[header.Name] = header.Value.GetString() ?? "";
                    }
                }
                if (config?.TryGetProperty("body", out var body) == true)
                    apiStep.Body = body.GetRawText();
                if (config?.TryGetProperty("bearerToken", out var bearer) == true)
                    apiStep.BearerToken = bearer.GetString();
                if (config?.TryGetProperty("basicAuthUsername", out var basicUser) == true)
                    apiStep.BasicAuthUsername = basicUser.GetString();
                if (config?.TryGetProperty("basicAuthPassword", out var basicPass) == true)
                    apiStep.BasicAuthPassword = basicPass.GetString();
                if (config?.TryGetProperty("apiKey", out var apiKey) == true)
                    apiStep.ApiKey = apiKey.GetString();
                if (config?.TryGetProperty("apiKeyHeaderName", out var apiKeyHeader) == true)
                    apiStep.ApiKeyHeaderName = apiKeyHeader.GetString();
                if (config?.TryGetProperty("queryParameters", out var queryParams) == true && queryParams.ValueKind == JsonValueKind.Object)
                {
                    apiStep.QueryParameters = new Dictionary<string, string>();
                    foreach (var param in queryParams.EnumerateObject())
                    {
                        apiStep.QueryParameters[param.Name] = param.Value.GetString() ?? "";
                    }
                }
                if (config?.TryGetProperty("maxRetries", out var maxRetries) == true)
                    apiStep.MaxRetries = maxRetries.GetInt32();
                if (config?.TryGetProperty("retryDelayMilliseconds", out var retryDelay) == true)
                    apiStep.RetryDelayMilliseconds = retryDelay.GetInt32();
                if (config?.TryGetProperty("useExponentialBackoff", out var expBackoff) == true)
                    apiStep.UseExponentialBackoff = expBackoff.GetBoolean();
                if (config?.TryGetProperty("followRedirects", out var followRedirects) == true)
                    apiStep.FollowRedirects = followRedirects.GetBoolean();
                if (config?.TryGetProperty("validateSslCertificate", out var validateSsl) == true)
                    apiStep.ValidateSslCertificate = validateSsl.GetBoolean();
                if (config?.TryGetProperty("responseVariable", out var respVar) == true)
                    apiStep.ResponseVariable = respVar.GetString();
                if (config?.TryGetProperty("statusCodeVariable", out var statusVar) == true)
                    apiStep.StatusCodeVariable = statusVar.GetString();
                if (config?.TryGetProperty("failOnNonSuccessCode", out var failOnError) == true)
                    apiStep.FailOnNonSuccessCode = failOnError.GetBoolean();
                if (config?.TryGetProperty("sendFileContent", out var sendFile) == true)
                    apiStep.SendFileContent = sendFile.GetBoolean();
                if (config?.TryGetProperty("sendFileAsFormData", out var sendForm) == true)
                    apiStep.SendFileAsFormData = sendForm.GetBoolean();
                if (config?.TryGetProperty("formFieldName", out var formField) == true)
                    apiStep.FormFieldName = formField.GetString();
                break;

            case DecisionStep decisionStep:
                if (config?.TryGetProperty("defaultAction", out var defAction) == true)
                    decisionStep.DefaultAction = Enum.TryParse<DecisionAction>(defAction.GetString(), true, out var action)
                        ? action
                        : DecisionAction.Continue;
                
                if (config?.TryGetProperty("jumpToStepName", out var jumpTo) == true)
                    decisionStep.JumpToStepName = jumpTo.GetString();
                
                if (config?.TryGetProperty("setVariable", out var setVar) == true)
                    decisionStep.SetVariable = setVar.GetString();
                
                if (config?.TryGetProperty("setVariableValue", out var setVarValue) == true)
                    decisionStep.SetVariableValue = setVarValue.GetRawText();
                
                if (config?.TryGetProperty("rules", out var rules) == true && rules.ValueKind == JsonValueKind.Array)
                {
                    decisionStep.Rules = new List<DecisionRule>();
                    
                    foreach (var ruleElement in rules.EnumerateArray())
                    {
                        var rule = new DecisionRule();
                        
                        if (ruleElement.TryGetProperty("name", out var ruleName))
                            rule.Name = ruleName.GetString();
                        
                        if (ruleElement.TryGetProperty("enabled", out var ruleEnabled))
                            rule.Enabled = ruleEnabled.GetBoolean();
                        
                        if (ruleElement.TryGetProperty("action", out var ruleAction))
                            rule.Action = Enum.TryParse<DecisionAction>(ruleAction.GetString(), true, out var act)
                                ? act
                                : DecisionAction.Continue;
                        
                        if (ruleElement.TryGetProperty("logic", out var ruleLogic))
                            rule.Logic = Enum.TryParse<ConditionLogic>(ruleLogic.GetString(), true, out var logic)
                                ? logic
                                : ConditionLogic.And;
                        
                        if (ruleElement.TryGetProperty("jumpToStepName", out var ruleJumpTo))
                            rule.JumpToStepName = ruleJumpTo.GetString();
                        
                        if (ruleElement.TryGetProperty("setVariable", out var ruleSetVar))
                            rule.SetVariable = ruleSetVar.GetString();
                        
                        if (ruleElement.TryGetProperty("setVariableValue", out var ruleSetVarValue))
                            rule.SetVariableValue = ruleSetVarValue.GetRawText();
                        
                        if (ruleElement.TryGetProperty("logMessage", out var logMsg))
                            rule.LogMessage = logMsg.GetString();
                        
                        if (ruleElement.TryGetProperty("conditions", out var conditions) && conditions.ValueKind == JsonValueKind.Array)
                        {
                            rule.Conditions = new List<DecisionCondition>();
                            
                            foreach (var condElement in conditions.EnumerateArray())
                            {
                                var condition = new DecisionCondition();
                                
                                if (condElement.TryGetProperty("field", out var field))
                                    condition.Field = field.GetString() ?? "";
                                
                                if (condElement.TryGetProperty("operator", out var op))
                                    condition.Operator = Enum.TryParse<ComparisonOperator>(op.GetString(), true, out var oper)
                                        ? oper
                                        : ComparisonOperator.Equals;
                                
                                if (condElement.TryGetProperty("value", out var val))
                                {
                                    // Handle different value types
                                    condition.Value = val.ValueKind switch
                                    {
                                        JsonValueKind.String => val.GetString(),
                                        JsonValueKind.Number => val.GetDouble(),
                                        JsonValueKind.True => true,
                                        JsonValueKind.False => false,
                                        JsonValueKind.Null => null,
                                        _ => val.GetRawText()
                                    };
                                }
                                
                                rule.Conditions.Add(condition);
                            }
                        }
                        
                        decisionStep.Rules.Add(rule);
                    }
                }
                break;
        }

        return step;
    }

    /// <summary>
    /// Convert multiple entities to domain models
    /// </summary>
    public static IEnumerable<PipelineStepBase> ConvertToDomainModels(IEnumerable<PipelineStep> entities)
    {
        var models = new List<PipelineStepBase>();
        
        foreach (var entity in entities)
        {
            var model = ConvertToDomainModel(entity);
            if (model != null)
            {
                models.Add(model);
            }
        }
        
        return models;
    }
}