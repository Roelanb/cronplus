using System.Text.Json;
using FluentValidation;

namespace Cronplus.Api.Domain.Models;

/// <summary>
/// Variable model with strong typing support
/// </summary>
public class VariableModel
{
    public int Id { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public VariableType Type { get; set; } = VariableType.String;
    public object? Value { get; set; }
    public string? Description { get; set; }
    public bool IsConstant { get; set; } = false;
    public bool IsRequired { get; set; } = false;
    public object? DefaultValue { get; set; }
    public VariableScope Scope { get; set; } = VariableScope.Task;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Get the typed value
    /// </summary>
    public T? GetValue<T>()
    {
        if (Value == null)
            return default;
        
        try
        {
            if (Value is T typedValue)
                return typedValue;
            
            if (Value is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }
            
            // Type conversion based on variable type
            return Type switch
            {
                VariableType.String => (T)(object)Value.ToString()!,
                VariableType.Integer => (T)Convert.ChangeType(Value, typeof(T)),
                VariableType.Decimal => (T)Convert.ChangeType(Value, typeof(T)),
                VariableType.Boolean => (T)Convert.ChangeType(Value, typeof(T)),
                VariableType.DateTime => (T)Convert.ChangeType(Value, typeof(T)),
                VariableType.Json => JsonSerializer.Deserialize<T>(Value.ToString()!),
                VariableType.List => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(Value)),
                VariableType.Dictionary => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(Value)),
                _ => (T)Convert.ChangeType(Value, typeof(T))
            };
        }
        catch
        {
            return default;
        }
    }
    
    /// <summary>
    /// Set the value with type validation
    /// </summary>
    public void SetValue(object? value)
    {
        if (value == null)
        {
            Value = null;
            return;
        }
        
        // Validate and convert based on type
        Value = Type switch
        {
            VariableType.String => value.ToString(),
            VariableType.Integer => Convert.ToInt64(value),
            VariableType.Decimal => Convert.ToDecimal(value),
            VariableType.Boolean => Convert.ToBoolean(value),
            VariableType.DateTime => value is DateTime dt ? dt : DateTime.Parse(value.ToString()!),
            VariableType.Json => value is string jsonStr ? JsonDocument.Parse(jsonStr) : JsonSerializer.SerializeToDocument(value),
            VariableType.List => value,
            VariableType.Dictionary => value,
            _ => value
        };
        
        UpdatedAt = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Validate the value against the type
    /// </summary>
    public bool IsValidValue(object? value)
    {
        if (value == null)
            return !IsRequired;
        
        try
        {
            return Type switch
            {
                VariableType.String => true,
                VariableType.Integer => long.TryParse(value.ToString(), out _),
                VariableType.Decimal => decimal.TryParse(value.ToString(), out _),
                VariableType.Boolean => bool.TryParse(value.ToString(), out _),
                VariableType.DateTime => DateTime.TryParse(value.ToString(), out _),
                VariableType.Json => TryParseJson(value),
                VariableType.List => value is IEnumerable<object>,
                VariableType.Dictionary => value is IDictionary<string, object>,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }
    
    private bool TryParseJson(object value)
    {
        try
        {
            if (value is string jsonStr)
            {
                JsonDocument.Parse(jsonStr);
                return true;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Variable type enumeration
/// </summary>
public enum VariableType
{
    String,
    Integer,
    Decimal,
    Boolean,
    DateTime,
    Json,
    List,
    Dictionary
}

/// <summary>
/// Variable scope enumeration
/// </summary>
public enum VariableScope
{
    Task,       // Variable is specific to a task
    Pipeline,   // Variable is available throughout the pipeline
    Global,     // Variable is available globally
    Step        // Variable is specific to a step
}

/// <summary>
/// Variable collection for execution context
/// </summary>
public class VariableCollection
{
    private readonly Dictionary<string, VariableModel> _variables = new();
    
    public void Add(VariableModel variable)
    {
        _variables[variable.Name] = variable;
    }
    
    public void Set(string name, object? value, VariableType? type = null)
    {
        if (_variables.TryGetValue(name, out var variable))
        {
            if (variable.IsConstant)
            {
                throw new InvalidOperationException($"Cannot modify constant variable: {name}");
            }
            
            variable.SetValue(value);
        }
        else
        {
            _variables[name] = new VariableModel
            {
                Name = name,
                Type = type ?? DetectType(value),
                Value = value
            };
        }
    }
    
    public T? Get<T>(string name, T? defaultValue = default)
    {
        if (_variables.TryGetValue(name, out var variable))
        {
            return variable.GetValue<T>();
        }
        
        return defaultValue;
    }
    
    public bool TryGet(string name, out VariableModel? variable)
    {
        return _variables.TryGetValue(name, out variable);
    }
    
    public bool Contains(string name)
    {
        return _variables.ContainsKey(name);
    }
    
    public void Remove(string name)
    {
        if (_variables.TryGetValue(name, out var variable) && variable.IsConstant)
        {
            throw new InvalidOperationException($"Cannot remove constant variable: {name}");
        }
        
        _variables.Remove(name);
    }
    
    public Dictionary<string, object?> ToDictionary()
    {
        return _variables.ToDictionary(kv => kv.Key, kv => kv.Value.Value);
    }
    
    public IEnumerable<VariableModel> GetAll()
    {
        return _variables.Values;
    }
    
    public IEnumerable<VariableModel> GetByScope(VariableScope scope)
    {
        return _variables.Values.Where(v => v.Scope == scope);
    }
    
    private VariableType DetectType(object? value)
    {
        if (value == null)
            return VariableType.String;
        
        return value switch
        {
            string => VariableType.String,
            int or long => VariableType.Integer,
            decimal or double or float => VariableType.Decimal,
            bool => VariableType.Boolean,
            DateTime => VariableType.DateTime,
            JsonDocument or JsonElement => VariableType.Json,
            IEnumerable<object> => VariableType.List,
            IDictionary<string, object> => VariableType.Dictionary,
            _ => VariableType.String
        };
    }
}

/// <summary>
/// Validator for VariableModel
/// </summary>
public class VariableModelValidator : AbstractValidator<VariableModel>
{
    public VariableModelValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Variable name is required")
            .Matches(@"^[a-zA-Z_][a-zA-Z0-9_]*$").WithMessage("Variable name must be a valid identifier")
            .Length(1, 100).WithMessage("Variable name must be between 1 and 100 characters");
        
        RuleFor(x => x.TaskId)
            .NotEmpty().WithMessage("Task ID is required");
        
        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Invalid variable type");
        
        RuleFor(x => x.Scope)
            .IsInEnum().WithMessage("Invalid variable scope");
        
        RuleFor(x => x.Value)
            .Must((model, value) => model.IsValidValue(value))
            .WithMessage((model, value) => $"Invalid value for type {model.Type}");
        
        RuleFor(x => x.DefaultValue)
            .Must((model, value) => model.IsValidValue(value))
            .When(x => x.DefaultValue != null)
            .WithMessage((model, value) => $"Invalid default value for type {model.Type}");
        
        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters");
    }
}