using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Polly;
using Polly.Timeout;

namespace Cronplus.Api.Domain.Models.PipelineSteps;

/// <summary>
/// Pipeline step for making REST API calls
/// </summary>
public class RestApiStep : PipelineStepBase
{
    public override string StepType => "restapi";
    
    public string Url { get; set; } = string.Empty;
    public HttpMethod Method { get; set; } = HttpMethod.Get;
    public Dictionary<string, string>? Headers { get; set; }
    public string? Body { get; set; }
    public string? ContentType { get; set; } = "application/json";
    public bool SendFileContent { get; set; } = false; // Send the file content as body
    public bool SendFileAsFormData { get; set; } = false; // Send file as multipart/form-data
    public string? FormFieldName { get; set; } = "file"; // Field name for form data
    public Dictionary<string, string>? QueryParameters { get; set; }
    public string? BearerToken { get; set; }
    public string? BasicAuthUsername { get; set; }
    public string? BasicAuthPassword { get; set; }
    public string? ApiKey { get; set; } // API Key authentication
    public string? ApiKeyHeaderName { get; set; } = "X-API-Key"; // Header name for API key
    public new int TimeoutSeconds { get; set; } = 30;
    public string? ResponseVariable { get; set; } // Store response in a variable
    public string? StatusCodeVariable { get; set; } // Store status code in a variable
    public bool FailOnNonSuccessCode { get; set; } = true;
    public int MaxRetries { get; set; } = 3; // Number of retries for transient failures
    public int RetryDelayMilliseconds { get; set; } = 1000; // Initial delay between retries
    public bool UseExponentialBackoff { get; set; } = true; // Use exponential backoff for retries
    public bool FollowRedirects { get; set; } = true; // Follow HTTP redirects
    public int MaxRedirects { get; set; } = 5; // Maximum number of redirects to follow
    public bool ValidateSslCertificate { get; set; } = true; // Validate SSL certificates
    public Dictionary<string, object>? ExpectedResponseValues { get; set; } // Validate response values
    public string? ResponseTransform { get; set; } // JSONPath or other transform expression
    
    private static readonly HttpClient _httpClient = CreateHttpClient();
    private static readonly HttpClient _httpClientNoSslValidation = CreateHttpClient(validateCertificate: false);
    
    private static HttpClient CreateHttpClient(bool validateCertificate = true)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false, // We'll handle redirects manually
            MaxAutomaticRedirections = 5
        };
        
        if (!validateCertificate)
        {
            handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, errors) => true;
        }
        
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5) // Max timeout, actual timeout controlled per request
        };
        
        client.DefaultRequestHeaders.Add("User-Agent", "Cronplus/1.0");
        return client;
    }
    
    public override async Task<StepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            context.Logger?.LogInformation("Executing RestApiStep: {Name} - {Method} {Url}", Name, Method, Url);
            
            // Resolve variables in URL and other string properties
            var resolvedUrl = ResolveVariables(Url, context);
            
            // Add query parameters
            if (QueryParameters != null && QueryParameters.Any())
            {
                var queryString = string.Join("&", QueryParameters.Select(kv => 
                    $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(ResolveVariables(kv.Value, context))}"));
                resolvedUrl = resolvedUrl.Contains('?') 
                    ? $"{resolvedUrl}&{queryString}" 
                    : $"{resolvedUrl}?{queryString}";
            }
            
            // Create request
            var request = new HttpRequestMessage(Method, resolvedUrl);
            
            // Set timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
            
            // Add headers
            if (Headers != null)
            {
                foreach (var header in Headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, ResolveVariables(header.Value, context));
                }
            }
            
            // Add authentication
            if (!string.IsNullOrEmpty(BearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ResolveVariables(BearerToken, context));
            }
            else if (!string.IsNullOrEmpty(BasicAuthUsername) && !string.IsNullOrEmpty(BasicAuthPassword))
            {
                var resolvedUsername = ResolveVariables(BasicAuthUsername, context);
                var resolvedPassword = ResolveVariables(BasicAuthPassword, context);
                var authString = $"{resolvedUsername}:{resolvedPassword}";
                var base64Auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64Auth);
            }
            else if (!string.IsNullOrEmpty(ApiKey))
            {
                var resolvedApiKey = ResolveVariables(ApiKey, context);
                var headerName = string.IsNullOrEmpty(ApiKeyHeaderName) ? "X-API-Key" : ApiKeyHeaderName;
                request.Headers.TryAddWithoutValidation(headerName, resolvedApiKey);
            }
            
            // Set request body
            if (Method != HttpMethod.Get && Method != HttpMethod.Head)
            {
                if (SendFileAsFormData && File.Exists(context.FilePath))
                {
                    var content = new MultipartFormDataContent();
                    var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(context.FilePath, cts.Token));
                    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                    content.Add(fileContent, FormFieldName ?? "file", context.FileName);
                    request.Content = content;
                }
                else if (SendFileContent && File.Exists(context.FilePath))
                {
                    var fileContent = await File.ReadAllTextAsync(context.FilePath, cts.Token);
                    request.Content = new StringContent(fileContent, Encoding.UTF8, ContentType ?? "text/plain");
                }
                else if (!string.IsNullOrEmpty(Body))
                {
                    var resolvedBody = ResolveVariables(Body, context);
                    request.Content = new StringContent(resolvedBody, Encoding.UTF8, ContentType ?? "application/json");
                }
            }
            
            // Build retry policy
            var retryPolicy = BuildRetryPolicy(context);
            
            // Select appropriate HTTP client based on SSL validation setting
            var httpClient = ValidateSslCertificate ? _httpClient : _httpClientNoSslValidation;
            
            // Send request with retry policy
            var response = await retryPolicy.ExecuteAsync(async (ct) =>
            {
                // Clone request for retry (important!)
                var clonedRequest = await CloneHttpRequestMessageAsync(request);
                
                var resp = await httpClient.SendAsync(clonedRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                
                // Handle redirects manually if enabled
                if (FollowRedirects && IsRedirectResponse(resp))
                {
                    resp = await HandleRedirectsAsync(resp, httpClient, context, ct);
                }
                
                return resp;
            }, cts.Token);
            
            // Read response
            var responseContent = await response.Content.ReadAsStringAsync(cts.Token);
            
            // Store response in variables
            var outputs = new Dictionary<string, object>();
            
            if (!string.IsNullOrEmpty(ResponseVariable))
            {
                context.SetVariable(ResponseVariable, responseContent);
                outputs[ResponseVariable] = responseContent;
            }
            
            if (!string.IsNullOrEmpty(StatusCodeVariable))
            {
                context.SetVariable(StatusCodeVariable, (int)response.StatusCode);
                outputs[StatusCodeVariable] = (int)response.StatusCode;
            }
            
            outputs["StatusCode"] = (int)response.StatusCode;
            outputs["ResponseLength"] = responseContent.Length;
            
            // Check for success
            if (FailOnNonSuccessCode && !response.IsSuccessStatusCode)
            {
                stopwatch.Stop();
                var failResult = StepResult.FailureResult(
                    $"API call failed with status {response.StatusCode}: {response.ReasonPhrase}. Response: {responseContent}");
                failResult.ExecutionTime = stopwatch.Elapsed;
                failResult.OutputVariables = outputs;
                return failResult;
            }
            
            stopwatch.Stop();
            
            context.Logger?.LogInformation("API call successful: {StatusCode} - {Length} bytes", 
                response.StatusCode, responseContent.Length);
            
            var successResult = StepResult.SuccessResult(
                $"API call completed with status {response.StatusCode}", outputs);
            successResult.ExecutionTime = stopwatch.Elapsed;
            return successResult;
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            var timeoutResult = StepResult.FailureResult($"API call timed out after {TimeoutSeconds} seconds");
            timeoutResult.ExecutionTime = stopwatch.Elapsed;
            return timeoutResult;
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            context.Logger?.LogError(ex, "HTTP request failed in step: {Name}", Name);
            var httpResult = StepResult.FailureResult($"HTTP request failed: {ex.Message}", ex);
            httpResult.ExecutionTime = stopwatch.Elapsed;
            return httpResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            context.Logger?.LogError(ex, "Failed to execute REST API step: {Name}", Name);
            var apiResult = StepResult.FailureResult($"REST API call failed: {ex.Message}", ex);
            apiResult.ExecutionTime = stopwatch.Elapsed;
            return apiResult;
        }
    }
    
    public override ValidationResult Validate()
    {
        var validator = new RestApiStepValidator();
        return validator.Validate(this);
    }
    
    private string ResolveVariables(string text, ExecutionContext context)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        var result = text;
        
        // Replace built-in variables
        result = result.Replace("{fileName}", context.FileName);
        result = result.Replace("{fileNameWithoutExt}", context.FileNameWithoutExtension);
        result = result.Replace("{fileExt}", context.FileExtension);
        result = result.Replace("{fileDir}", context.FileDirectory);
        result = result.Replace("{filePath}", context.FilePath);
        result = result.Replace("{taskId}", context.TaskId);
        result = result.Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"));
        result = result.Replace("{time}", DateTime.Now.ToString("HH:mm:ss"));
        result = result.Replace("{datetime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        result = result.Replace("{timestamp}", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        
        // Replace custom variables
        foreach (var variable in context.Variables)
        {
            result = result.Replace($"{{{variable.Key}}}", variable.Value?.ToString() ?? string.Empty);
        }
        
        return result;
    }
    
    private IAsyncPolicy<HttpResponseMessage> BuildRetryPolicy(ExecutionContext context)
    {
        if (MaxRetries <= 0)
        {
            return Policy.NoOpAsync<HttpResponseMessage>();
        }
        
        var retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => IsTransientFailure(r))
            .Or<HttpRequestException>()
            .Or<TaskCanceledException>(ex => !(ex.InnerException is TimeoutException))
            .WaitAndRetryAsync(
                MaxRetries,
                retryAttempt => UseExponentialBackoff
                    ? TimeSpan.FromMilliseconds(RetryDelayMilliseconds * Math.Pow(2, retryAttempt - 1))
                    : TimeSpan.FromMilliseconds(RetryDelayMilliseconds),
                onRetry: (outcome, timespan, retryCount, ctx) =>
                {
                    var reason = outcome.Exception != null 
                        ? $"Exception: {outcome.Exception.Message}"
                        : $"Status: {outcome.Result?.StatusCode}";
                    
                    context.Logger?.LogWarning(
                        "REST API retry {RetryCount}/{MaxRetries} after {Delay}ms. Reason: {Reason}",
                        retryCount, MaxRetries, timespan.TotalMilliseconds, reason);
                });
        
        // Add timeout policy
        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(
            TimeSpan.FromSeconds(TimeoutSeconds),
            TimeoutStrategy.Optimistic);
        
        return Policy.WrapAsync(retryPolicy, timeoutPolicy);
    }
    
    private bool IsTransientFailure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return false;
        
        // Retry on specific status codes that indicate transient failures
        return response.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
               response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
               response.StatusCode == System.Net.HttpStatusCode.InternalServerError ||
               response.StatusCode == System.Net.HttpStatusCode.BadGateway ||
               response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
               response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout;
    }
    
    private bool IsRedirectResponse(HttpResponseMessage response)
    {
        return (int)response.StatusCode >= 300 && (int)response.StatusCode < 400 &&
               response.Headers.Location != null;
    }
    
    private async Task<HttpResponseMessage> HandleRedirectsAsync(
        HttpResponseMessage response, HttpClient httpClient, ExecutionContext context, CancellationToken cancellationToken)
    {
        var currentResponse = response;
        var redirectCount = 0;
        
        while (IsRedirectResponse(currentResponse) && redirectCount < MaxRedirects)
        {
            var location = currentResponse.Headers.Location;
            if (location == null)
                break;
            
            var newUri = location.IsAbsoluteUri 
                ? location 
                : new Uri(currentResponse.RequestMessage!.RequestUri!, location);
            
            context.Logger?.LogDebug("Following redirect to: {Uri}", newUri);
            
            var redirectRequest = new HttpRequestMessage(HttpMethod.Get, newUri);
            
            // Copy relevant headers
            foreach (var header in currentResponse.RequestMessage!.Headers)
            {
                redirectRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            
            currentResponse.Dispose();
            currentResponse = await httpClient.SendAsync(redirectRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            redirectCount++;
        }
        
        if (redirectCount >= MaxRedirects && IsRedirectResponse(currentResponse))
        {
            throw new HttpRequestException($"Too many redirects. Maximum allowed: {MaxRedirects}");
        }
        
        return currentResponse;
    }
    
    private async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        
        // Copy headers
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        
        // Copy content if present
        if (request.Content != null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(contentBytes);
            
            // Copy content headers
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        
        // Copy options (Properties is obsolete in .NET 5+)
        foreach (var option in request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }
        
        return clone;
    }
}

/// <summary>
/// Validator for RestApiStep
/// </summary>
public class RestApiStepValidator : AbstractValidator<RestApiStep>
{
    public RestApiStepValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Step name is required");
        
        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("URL is required")
            .Must(BeAValidUrl).WithMessage("URL must be a valid HTTP/HTTPS URL");
        
        RuleFor(x => x.TimeoutSeconds)
            .InclusiveBetween(1, 300).WithMessage("Timeout must be between 1 and 300 seconds");
        
        RuleFor(x => x.FormFieldName)
            .NotEmpty()
            .When(x => x.SendFileAsFormData)
            .WithMessage("Form field name is required when sending file as form data");
        
        RuleFor(x => x.ResponseVariable)
            .Matches(@"^[a-zA-Z_][a-zA-Z0-9_]*$")
            .When(x => !string.IsNullOrEmpty(x.ResponseVariable))
            .WithMessage("Response variable name must be a valid identifier");
        
        RuleFor(x => x.StatusCodeVariable)
            .Matches(@"^[a-zA-Z_][a-zA-Z0-9_]*$")
            .When(x => !string.IsNullOrEmpty(x.StatusCodeVariable))
            .WithMessage("Status code variable name must be a valid identifier");
        
        RuleFor(x => x)
            .Must(x => !(x.SendFileContent && x.SendFileAsFormData))
            .WithMessage("Cannot send file as both content and form data simultaneously");
    }
    
    private bool BeAValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        
        // Allow variables in URL
        var testUrl = url.Replace("{fileName}", "test.txt")
                        .Replace("{fileNameWithoutExt}", "test")
                        .Replace("{fileExt}", ".txt")
                        .Replace("{fileDir}", "temp")
                        .Replace("{filePath}", "temp/test.txt")
                        .Replace("{taskId}", "task123")
                        .Replace("{date}", "2024-01-01")
                        .Replace("{time}", "12:00:00")
                        .Replace("{datetime}", "2024-01-01 12:00:00")
                        .Replace("{timestamp}", "1704110400");
        
        // Basic check for variables that might remain
        if (testUrl.Contains("{") && testUrl.Contains("}"))
        {
            // Extract variable names and replace with dummy values
            var varPattern = new System.Text.RegularExpressions.Regex(@"\{([^}]+)\}");
            testUrl = varPattern.Replace(testUrl, "value");
        }
        
        return Uri.TryCreate(testUrl, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}