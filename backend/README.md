# Cronplus Backend API

A high-performance file automation backend built with .NET 9 and FastEndpoints, featuring DuckDB for storage and a vertical slice architecture.

## Technology Stack

- **.NET 9**: Latest framework for high-performance APIs
- **FastEndpoints**: Minimal API framework with superior performance
- **DuckDB**: Embedded analytical database
- **Serilog**: Structured logging
- **FluentValidation**: Request validation
- **Swagger/OpenAPI**: API documentation

## Architecture

This project follows **Vertical Slice Architecture** where each feature is self-contained:

```
backend/
├── Features/           # Feature-based organization
│   ├── Tasks/         # Task management features
│   │   ├── Create/    # Each operation is self-contained
│   │   ├── Get/
│   │   ├── Update/
│   │   ├── Delete/
│   │   └── List/
│   ├── Pipeline/      # Pipeline management
│   ├── Variables/     # Variable management
│   └── Monitoring/    # Health checks and metrics
├── Domain/            # Domain entities and value objects
├── Infrastructure/    # External concerns (DB, FileSystem)
└── Common/           # Shared utilities
```

## Getting Started

### Prerequisites

- .NET 9 SDK
- Visual Studio 2022 / VS Code / Rider

### Running the API

```bash
# Restore packages
dotnet restore

# Build the project
dotnet build

# Run in development mode
dotnet run

# Or with watch mode for hot reload
dotnet watch run
```

The API will start on `http://localhost:5000` (or the port specified in launchSettings.json).

### Accessing the API

- **Health Check**: `http://localhost:5000/api/v1/health/status`
- **Swagger UI**: `http://localhost:5000/swagger`
- **Tasks List**: `http://localhost:5000/api/v1/tasks`

## Key Features

### FastEndpoints Benefits
- **Performance**: 2-3x faster than traditional MVC controllers
- **Minimal Boilerplate**: Clean, focused endpoint classes
- **Built-in Validation**: FluentValidation integration
- **Type Safety**: Strongly typed request/response contracts
- **Testability**: Excellent testing support

### Implemented Endpoints

1. **Health Check** (`/api/v1/health/status`)
   - Basic health status
   - Detailed system information (with `?detailed=true`)

2. **List Tasks** (`/api/v1/tasks`)
   - Paginated task listing
   - Search and filtering support
   - Mock data (pending database implementation)

## Configuration

### appsettings.json
- Serilog logging configuration
- DuckDB connection string
- CORS settings
- Cronplus-specific settings

### Development Configuration
- Debug-level logging
- Swagger UI enabled
- Detailed error messages

## Logging

Logs are written to:
- Console (structured output)
- File (`logs/cronplus-{date}.log`)
- Rolling daily, 7-day retention

## Next Steps

- [ ] Implement DuckDB database layer (Phase 1.2)
- [ ] Add authentication/authorization
- [ ] Implement remaining CRUD endpoints
- [ ] Add file watching service
- [ ] Implement pipeline execution engine

## Development Guidelines

1. **One endpoint per file**: Keep endpoints focused and maintainable
2. **Validators separate**: Create dedicated validator classes
3. **Use dependency injection**: Register services in Program.cs
4. **Structured logging**: Use Serilog with contextual properties
5. **API versioning**: All endpoints versioned under `/api/v{version}/`

## Testing

```bash
# Run unit tests
dotnet test

# With coverage
dotnet test /p:CollectCoverage=true
```

## Troubleshooting

- **Port already in use**: Check launchSettings.json and update the port
- **Swagger not loading**: Ensure you're running in Development environment
- **Database errors**: Check DuckDB connection string in appsettings.json


Great, now I want you to carefully read over all of the new code you just wrote and other existing code you just modified with "fresh eyes," looking super carefully for any obvious bugs, errors, problems, issues, confusion, etc.