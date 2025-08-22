using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cronplus.Api.Domain.Models.PipelineSteps;
using ExecutionContext = Cronplus.Api.Domain.Models.PipelineSteps.ExecutionContext;

namespace Cronplus.Api.Infrastructure.Services;

/// <summary>
/// Service responsible for interpolating variables in strings and objects
/// </summary>
public interface IVariableInterpolator
{
    Task<string> InterpolateAsync(string input, ExecutionContext context);
    Task InterpolateStepAsync(PipelineStepBase step, ExecutionContext context);
    Task<T> InterpolateObjectAsync<T>(T obj, ExecutionContext context) where T : class;
}

public class VariableInterpolator : IVariableInterpolator
{
    private readonly ILogger<VariableInterpolator> _logger;
    private static readonly Regex VariablePattern = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);
    private static readonly Regex NestedPropertyPattern = new(@"^([^.]+)\.(.+)$", RegexOptions.Compiled);

    public VariableInterpolator(ILogger<VariableInterpolator> logger)
    {
        _logger = logger;
    }

    public Task<string> InterpolateAsync(string input, ExecutionContext context)
    {
        if (string.IsNullOrEmpty(input))
            return Task.FromResult(input);

        var result = VariablePattern.Replace(input, match =>
        {
            var variableName = match.Groups[1].Value.Trim();
            var value = ResolveVariable(variableName, context);
            
            if (value == null)
            {
                _logger.LogWarning("Variable {VariableName} not found, keeping original placeholder", variableName);
                return match.Value;
            }

            return ConvertToString(value);
        });

        _logger.LogDebug("Interpolated string: {Original} => {Result}", input, result);
        return Task.FromResult(result);
    }

    public async Task InterpolateStepAsync(PipelineStepBase step, ExecutionContext context)
    {
        if (step == null)
            return;

        var type = step.GetType();
        await InterpolateObjectPropertiesAsync(step, type, context);
    }

    public async Task<T> InterpolateObjectAsync<T>(T obj, ExecutionContext context) where T : class
    {
        if (obj == null)
            return obj;

        var type = typeof(T);
        await InterpolateObjectPropertiesAsync(obj, type, context);
        return obj;
    }

    private async Task InterpolateObjectPropertiesAsync(object obj, Type type, ExecutionContext context)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite);

        foreach (var property in properties)
        {
            var value = property.GetValue(obj);
            if (value == null)
                continue;

            if (property.PropertyType == typeof(string))
            {
                var interpolated = await InterpolateAsync((string)value, context);
                property.SetValue(obj, interpolated);
            }
            else if (property.PropertyType == typeof(Dictionary<string, string>))
            {
                var dict = (Dictionary<string, string>)value;
                var interpolatedDict = new Dictionary<string, string>();
                
                foreach (var kvp in dict)
                {
                    var interpolatedKey = await InterpolateAsync(kvp.Key, context);
                    var interpolatedValue = await InterpolateAsync(kvp.Value, context);
                    interpolatedDict[interpolatedKey] = interpolatedValue;
                }
                
                property.SetValue(obj, interpolatedDict);
            }
            else if (property.PropertyType == typeof(List<string>))
            {
                var list = (List<string>)value;
                var interpolatedList = new List<string>();
                
                foreach (var item in list)
                {
                    var interpolated = await InterpolateAsync(item, context);
                    interpolatedList.Add(interpolated);
                }
                
                property.SetValue(obj, interpolatedList);
            }
            else if (property.PropertyType.IsClass && 
                     !property.PropertyType.IsArray && 
                     property.PropertyType != typeof(string) &&
                     !property.PropertyType.IsGenericType &&
                     !IsSystemType(property.PropertyType))
            {
                // Recursively interpolate nested objects
                await InterpolateObjectPropertiesAsync(value, property.PropertyType, context);
            }
        }
    }

    private object? ResolveVariable(string variableName, ExecutionContext context)
    {
        // Check for nested property access (e.g., "user.name")
        var nestedMatch = NestedPropertyPattern.Match(variableName);
        if (nestedMatch.Success)
        {
            var rootVariable = nestedMatch.Groups[1].Value;
            var propertyPath = nestedMatch.Groups[2].Value;
            
            if (context.Variables.TryGetValue(rootVariable, out var rootValue))
            {
                return ResolveNestedProperty(rootValue, propertyPath);
            }
        }
        
        // Check for array/list indexing (e.g., "items[0]")
        if (variableName.Contains('['))
        {
            return ResolveIndexedVariable(variableName, context);
        }
        
        // Check for built-in functions
        if (variableName.StartsWith("fn:"))
        {
            return EvaluateFunction(variableName.Substring(3), context);
        }
        
        // Direct variable lookup
        if (context.Variables.TryGetValue(variableName, out var value))
        {
            return value;
        }
        
        // Check for environment variables
        if (variableName.StartsWith("env:"))
        {
            var envVarName = variableName.Substring(4);
            return Environment.GetEnvironmentVariable(envVarName);
        }
        
        return null;
    }

    private object? ResolveNestedProperty(object obj, string propertyPath)
    {
        if (obj == null)
            return null;
        
        var parts = propertyPath.Split('.');
        var current = obj;
        
        foreach (var part in parts)
        {
            if (current == null)
                return null;
            
            if (current is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty(part, out var prop))
                {
                    current = prop;
                }
                else
                {
                    return null;
                }
            }
            else if (current is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue(part, out var value))
                {
                    current = value;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                var type = current.GetType();
                var property = type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                
                if (property != null)
                {
                    current = property.GetValue(current);
                }
                else
                {
                    return null;
                }
            }
        }
        
        return current;
    }

    private object? ResolveIndexedVariable(string variableName, ExecutionContext context)
    {
        var match = Regex.Match(variableName, @"^([^\[]+)\[(\d+)\](.*)$");
        if (!match.Success)
            return null;
        
        var arrayName = match.Groups[1].Value;
        var index = int.Parse(match.Groups[2].Value);
        var remainder = match.Groups[3].Value;
        
        if (!context.Variables.TryGetValue(arrayName, out var value))
            return null;
        
        object? element = null;
        
        if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
        {
            var array = jsonElement.EnumerateArray().ToList();
            if (index >= 0 && index < array.Count)
            {
                element = array[index];
            }
        }
        else if (value is IList<object> list)
        {
            if (index >= 0 && index < list.Count)
            {
                element = list[index];
            }
        }
        else if (value is Array array)
        {
            if (index >= 0 && index < array.Length)
            {
                element = array.GetValue(index);
            }
        }
        
        if (element != null && !string.IsNullOrEmpty(remainder))
        {
            // Handle nested property access after array indexing
            if (remainder.StartsWith("."))
            {
                return ResolveNestedProperty(element, remainder.Substring(1));
            }
        }
        
        return element;
    }

    private object? EvaluateFunction(string function, ExecutionContext context)
    {
        var parts = function.Split(':');
        if (parts.Length < 1)
            return null;
        
        var functionName = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1].Split(',').Select(a => a.Trim()).ToArray() : Array.Empty<string>();
        
        return functionName switch
        {
            "now" => DateTime.UtcNow.ToString(args.Length > 0 ? args[0] : "yyyy-MM-dd HH:mm:ss"),
            "date" => DateTime.UtcNow.ToString(args.Length > 0 ? args[0] : "yyyy-MM-dd"),
            "time" => DateTime.UtcNow.ToString(args.Length > 0 ? args[0] : "HH:mm:ss"),
            "guid" => Guid.NewGuid().ToString(args.Length > 0 ? args[0] : "D"),
            "random" => GenerateRandom(args),
            "upper" => args.Length > 0 ? ResolveVariable(args[0], context)?.ToString()?.ToUpper() : null,
            "lower" => args.Length > 0 ? ResolveVariable(args[0], context)?.ToString()?.ToLower() : null,
            "trim" => args.Length > 0 ? ResolveVariable(args[0], context)?.ToString()?.Trim() : null,
            "length" => args.Length > 0 ? ResolveVariable(args[0], context)?.ToString()?.Length : null,
            "substring" => EvaluateSubstring(args, context),
            "replace" => EvaluateReplace(args, context),
            "join" => EvaluateJoin(args, context),
            "split" => EvaluateSplit(args, context),
            _ => null
        };
    }

    private object? GenerateRandom(string[] args)
    {
        var random = new Random();
        
        if (args.Length == 0)
            return random.Next().ToString();
        
        if (args.Length == 1 && int.TryParse(args[0], out var max))
            return random.Next(max).ToString();
        
        if (args.Length == 2 && int.TryParse(args[0], out var min) && int.TryParse(args[1], out max))
            return random.Next(min, max).ToString();
        
        return null;
    }

    private object? EvaluateSubstring(string[] args, ExecutionContext context)
    {
        if (args.Length < 2)
            return null;
        
        var str = ResolveVariable(args[0], context)?.ToString();
        if (str == null)
            return null;
        
        if (!int.TryParse(args[1], out var start))
            return null;
        
        if (args.Length == 2)
            return str.Substring(start);
        
        if (int.TryParse(args[2], out var length))
            return str.Substring(start, Math.Min(length, str.Length - start));
        
        return null;
    }

    private object? EvaluateReplace(string[] args, ExecutionContext context)
    {
        if (args.Length != 3)
            return null;
        
        var str = ResolveVariable(args[0], context)?.ToString();
        if (str == null)
            return null;
        
        return str.Replace(args[1], args[2]);
    }

    private object? EvaluateJoin(string[] args, ExecutionContext context)
    {
        if (args.Length < 2)
            return null;
        
        var separator = args[0];
        var values = new List<string>();
        
        for (int i = 1; i < args.Length; i++)
        {
            var value = ResolveVariable(args[i], context);
            if (value != null)
            {
                values.Add(ConvertToString(value));
            }
        }
        
        return string.Join(separator, values);
    }

    private object? EvaluateSplit(string[] args, ExecutionContext context)
    {
        if (args.Length != 2)
            return null;
        
        var str = ResolveVariable(args[0], context)?.ToString();
        if (str == null)
            return null;
        
        return str.Split(args[1]);
    }

    private string ConvertToString(object value)
    {
        if (value == null)
            return string.Empty;
        
        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                JsonValueKind.String => jsonElement.GetString() ?? string.Empty,
                JsonValueKind.Number => jsonElement.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => jsonElement.GetRawText()
            };
        }
        
        return value.ToString() ?? string.Empty;
    }

    private bool IsSystemType(Type type)
    {
        return type.Namespace != null && 
               (type.Namespace.StartsWith("System") || 
                type.Namespace.StartsWith("Microsoft"));
    }
}