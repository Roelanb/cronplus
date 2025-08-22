# Cronplus Development Plan

## 📊 Progress Summary
- **Phase 1.1**: ✅ COMPLETED (2025-08-20) - Backend Infrastructure Setup with FastEndpoints
- **Phase 1.2**: ✅ COMPLETED (2025-08-20) - Database Layer with DuckDB
- **Phase 1.3**: ✅ COMPLETED (2025-08-20) - FastEndpoints Integration Patterns
- **Phase 1.4**: ✅ COMPLETED (2025-08-20) - Core Domain Models
- **Phase 2.1**: ✅ COMPLETED (2025-08-21) - File System Watcher Service
- **Phase 2.2**: ✅ COMPLETED (2025-08-21) - Task Supervisor System
- **Phase 2.3**: ✅ COMPLETED (2025-08-21) - Event Processing Pipeline
- **Phase 3.1**: ✅ COMPLETED (2025-08-21) - Core Actions Enhancement
- **Phase 3.2**: ✅ COMPLETED (2025-08-21) - New Actions (REST API & Decision)
- **Phase 3.3**: ✅ COMPLETED (2025-08-21) - Action Extensibility
- **Phase 4**: 🚧 IN PROGRESS - API Layer with FastEndpoints
- **Phase 5**: ⏳ PENDING - Frontend Development
- **Phase 6**: ⏳ PENDING - Integration and Testing
- **Phase 7**: ⏳ PENDING - DevOps and Deployment
- **Phase 8**: ⏳ PENDING - Migration and Rollout

### Latest Updates
- **2025-08-22**:
  - Completed Phase 4.2 - API Endpoints Implementation:
    - ✅ All Task management endpoints (CRUD operations)
    - ✅ Pipeline configuration endpoints
    - ✅ Variables management endpoints  
    - ✅ Task execution control endpoints (Start/Stop/Status)
    - ✅ Monitoring endpoints (Logs, Health, Metrics)
    - ✅ Fixed all compilation errors and type mismatches
    - ✅ Added comprehensive request/response DTOs and validators
  - Phase 4 Progress - Backend API Infrastructure:
    - Created SignalR hubs for real-time task execution and system monitoring updates
    - Implemented JWT-based authentication endpoints with refresh token support
    - Added comprehensive security policies and authorization handlers
    - Created TaskExecutionHub for real-time task status broadcasting
    - Created MonitoringHub for system metrics and health monitoring
    - Integrated authentication middleware and SignalR into Program.cs
    - Added background service for periodic metrics broadcasting
    - Configured JWT settings in appsettings.json
- **2025-08-21**:
  - Completed Phase 2.3 - Implemented pipeline executor service with variable interpolation, execution context propagation, retry logic, and dead letter queue
  - Completed Phase 3.1 - Enhanced all core actions with advanced features including atomic moves, checksum verification, secure delete, and archive conflict strategies
  - Completed Phase 3.2 - Implemented REST API action with comprehensive authentication (Basic, Bearer, API Key), retry logic with Polly, and enhanced Decision/Condition step with advanced expression evaluation and file property support
  - Completed Phase 3.3 - Created plugin architecture with IActionPlugin interface, ActionFactory pattern, plugin loader with AssemblyLoadContext isolation, comprehensive validation framework, and example email plugin
- **2025-08-20**: 
  - Completed Phase 1.1 - Successfully set up .NET 9 backend with FastEndpoints, Vertical Slice Architecture, Serilog logging, and initial test endpoints
  - Completed Phase 1.2 - Implemented DuckDB database layer with repository pattern, unit of work, migrations, and seed data
  - Completed Phase 1.3 - Created base endpoint classes, security policies, and integration patterns
  - Completed Phase 1.4 - Implemented core domain models including Task, PipelineStep types, and Variables
  - Completed Phase 2.1 - Built file system watcher with debouncing and event processing
  - Completed Phase 2.2 - Created task supervisor system with lifecycle management

## Project Overview
Cronplus is a cron job executor that monitors directories for file changes and executes configurable action pipelines. The current implementation is in Go but needs to be migrated to C#/.NET 9 for the backend and React/Vite for the frontend, with DuckDB for data storage.

## Technology Stack Highlights
### FastEndpoints Benefits
- **Performance**: Faster than MVC/Minimal APIs with less overhead
- **Vertical Slice Architecture**: Each endpoint is self-contained with its request/response DTOs
- **Built-in Validation**: FluentValidation integration out of the box
- **Type Safety**: Strongly typed request/response contracts
- **Testing**: Excellent testing support with built-in test fixtures
- **Documentation**: Automatic Swagger/OpenAPI generation
- **Security**: JWT authentication and authorization policies built-in
- **Event-Driven**: Native support for event publishing and handling

## Current State Analysis
- **Existing**: Go-based backend with file watching, action pipelines, HTTP API, and basic HTML UI
- **Storage**: Currently using BoltDB, needs migration to DuckDB
- **Actions**: Copy, Delete, Archive, Print actions implemented
- **Features**: Variable interpolation, retry policies, debouncing, state tracking

## Migration Requirements
1. Backend in C#/.NET 9 with FastEndpoints and DuckDB storage
2. Frontend in React/Vite with TypeScript and Ant Design
3. Maintain all existing functionality
4. Add decision/condition steps in pipelines
5. Improve UI/UX for task management
6. REST endpoint action support

---

## Phase 1: Backend Infrastructure Setup
### 1.1 Project Setup and Structure ✅ COMPLETED (2025-08-20)
- [x] Create new `backend` folder structure
- [x] Initialize .NET 9 project with Web API template
- [x] Install FastEndpoints NuGet packages
  - [x] FastEndpoints
  - [x] FastEndpoints.Swagger
  - [x] FastEndpoints.Security
  - [ ] FastEndpoints.Testing (for test project - pending)
- [x] Set up project structure following Vertical Slice Architecture
  ```
  backend/
  ├── Features/
  │   ├── Tasks/
  │   │   ├── Create/
  │   │   │   ├── CreateTaskEndpoint.cs
  │   │   │   ├── CreateTaskRequest.cs
  │   │   │   ├── CreateTaskResponse.cs
  │   │   │   └── CreateTaskValidator.cs
  │   │   ├── Get/
  │   │   ├── Update/
  │   │   └── Delete/
  │   ├── Pipeline/
  │   ├── Variables/
  │   └── Monitoring/
  ├── Domain/
  │   ├── Entities/
  │   └── ValueObjects/
  ├── Infrastructure/
  │   ├── Database/
  │   ├── FileSystem/
  │   └── Services/
  └── Common/
      ├── Extensions/
      ├── Mappers/
      └── Processors/
  ```
- [x] Configure FastEndpoints in Program.cs
- [x] Set up global error handling with FastEndpoints
- [x] Set up logging with Serilog (console and file sinks)
- [x] Configure development and production appsettings
- [x] Set up FluentValidation for request validation
- [x] Create initial test endpoints (HealthCheck and ListTasks)
- [x] Add DuckDB.NET.Data package
- [x] Configure CORS for frontend integration
- [x] Add .gitignore for .NET projects
- [x] Create backend README documentation

### 1.2 Database Layer with DuckDB ✅ COMPLETED (2025-08-20)
- [x] Install DuckDB.NET.Data package (completed in 1.1)
- [x] Design database schema
  - [x] Tasks table (id, enabled, watch_config, created_at, updated_at)
  - [x] Pipeline_Steps table (id, task_id, step_order, type, config)
  - [x] Variables table (id, task_id, name, type, value)
  - [x] Execution_Log table (id, task_id, file_path, status, timestamp, details)
  - [x] Conditions table (id, step_id, expression, true_action, false_action)
- [x] Implement repository pattern for data access
- [x] Create database migration system with version tracking
- [x] Implement unit of work pattern
- [x] Add database initialization and seeding
- [x] Create repository implementations for all entities
- [x] Add database context with connection management
- [x] Configure DuckDB settings in appsettings
- [x] Integrate database with ListTasks endpoint

### 1.3 FastEndpoints Integration Patterns
- [x] Create base endpoint classes for common functionality
- [x] Implement summary metadata for all endpoints
- [x] Set up endpoint groups for feature organization
- [x] Configure endpoint security policies
- [x] Create custom endpoint processors
- [x] Implement response caching strategies
- [x] Set up endpoint testing helpers
- [x] Create common request/response DTOs
- [x] Implement global error handling patterns

### 1.4 Core Domain Models
- [x] Create Task entity with validation
- [x] Create PipelineStep base class and derived types
  - [x] CopyStep
  - [x] DeleteStep
  - [x] ArchiveStep
  - [x] PrintStep
  - [x] RestApiStep (new)
  - [x] DecisionStep (new)
- [x] Create Variable entity with type system
- [x] Create WatchConfiguration value object
- [x] Create ExecutionContext for pipeline processing
- [x] Implement retry policy models

---

## Phase 2: File Watching and Event Processing
### 2.1 File System Watcher Service
- [x] Implement FileSystemWatcher wrapper service
- [x] Add glob pattern matching support
- [x] Implement debouncing mechanism
- [x] Implement file stabilization detection
- [x] Create event queue for processing
- [x] Add file locking and concurrency handling

### 2.2 Task Supervisor System
- [x] Create TaskSupervisor base class
- [x] Implement task lifecycle management
- [x] Add concurrent execution limits per task
- [x] Implement graceful shutdown handling
- [x] Create task state machine
- [x] Add health check monitoring

### 2.3 Event Processing Pipeline ✅ COMPLETED (2025-08-21)
- [x] Create pipeline executor service
- [x] Implement variable interpolation engine
- [x] Add execution context propagation
- [x] Implement step-by-step execution with logging
- [x] Add error handling and retry logic
- [x] Create dead letter queue for failed items

---

## Phase 3: Action Implementation
### 3.1 Core Actions ✅ COMPLETED (2025-08-21)
- [x] Implement Copy action with atomic move support
- [x] Add checksum verification for Copy
- [x] Implement Delete action with secure delete option
- [x] Implement Archive action with conflict strategies
- [x] Implement Print action with printer management
- [x] Add timeout support for all actions

### 3.2 New Actions ✅ COMPLETED (2025-08-21)
- [x] Implement REST API action
  - [x] Support GET, POST, PUT, DELETE methods
  - [x] Add authentication options (Basic, Bearer, API Key)
  - [x] Implement request/response mapping
  - [x] Add retry and timeout configuration with Polly
- [x] Implement Decision/Condition step
  - [x] Create expression evaluator
  - [x] Support file properties (size, name, extension, date)
  - [x] Add variable comparison support
  - [x] Implement branching logic

### 3.3 Action Extensibility ✅ COMPLETED (2025-08-21)
- [x] Create action plugin interface (IActionPlugin)
- [x] Implement action factory pattern for dynamic step creation
- [x] Add custom action registration and plugin loading system
- [x] Create action validation framework with pipeline validation
- [x] Implement plugin isolation with AssemblyLoadContext
- [x] Create example email plugin demonstrating the system
- [x] Add plugin configuration support
- [x] Update PipelineExecutor to use factory pattern

---

## Phase 4: API Layer with FastEndpoints
### 4.1 FastEndpoints Setup and Configuration
- [x] Configure FastEndpoints in Program.cs
- [x] Set up Swagger/OpenAPI generation with FastEndpoints
- [x] Implement JWT authentication with FastEndpoints security
- [x] Configure API versioning with FastEndpoints
- [x] Set up request/response DTOs with FluentValidation
- [x] Configure global pre/post processors
- [ ] Implement rate limiting with FastEndpoints
- [x] Set up CORS policies

### 4.2 API Endpoints Implementation with FastEndpoints ✅ COMPLETED (2025-08-22)
- [x] Tasks Feature Endpoints
  - [x] Create GetTasksEndpoint (GET /api/tasks)
  - [x] Create GetTaskByIdEndpoint (GET /api/tasks/{id})
  - [x] Create CreateTaskEndpoint (POST /api/tasks)
  - [x] Create UpdateTaskEndpoint (PUT /api/tasks/{id})
  - [x] Create DeleteTaskEndpoint (DELETE /api/tasks/{id})
  - [x] Implement request/response mappers for each endpoint
  - [x] Add validators for each request DTO
- [x] Pipeline Feature Endpoints
  - [x] Create GetPipelineEndpoint (GET /api/tasks/{id}/pipeline)
  - [x] Create UpdatePipelineEndpoint (PUT /api/tasks/{id}/pipeline)
  - [x] Implement pipeline step validators
  - [x] Add pipeline configuration mappers
- [x] Variables Feature Endpoints
  - [x] Create GetVariablesEndpoint (GET /api/tasks/{id}/variables)
  - [x] Create AddVariableEndpoint (POST /api/tasks/{id}/variables)
  - [x] Create UpdateVariableEndpoint (PUT /api/variables/{id})
  - [x] Create DeleteVariableEndpoint (DELETE /api/variables/{id})
  - [x] Implement variable type validation
- [x] Execution Feature Endpoints
  - [x] Create StartTaskEndpoint (POST /api/tasks/{id}/start)
  - [x] Create StopTaskEndpoint (POST /api/tasks/{id}/stop)
  - [x] Create GetTaskStatusEndpoint (GET /api/tasks/{id}/status)
  - [x] Implement async command handlers
- [x] Monitoring Feature Endpoints
  - [x] Create GetLogsEndpoint (GET /api/logs) with pagination
  - [x] Create HealthCheckEndpoint (GET /api/health)
  - [x] Create MetricsEndpoint (GET /api/metrics)
  - [x] Implement response caching where appropriate

### 4.3 Real-time Updates
- [x] Implement SignalR hub for real-time updates
- [x] Integrate SignalR with FastEndpoints event publishing
- [x] Create notification service with FastEndpoints event handlers
- [ ] Add WebSocket support for log streaming
- [ ] Implement server-sent events endpoint as fallback
- [ ] Create event stream endpoints using FastEndpoints

---

## Phase 5: Frontend Development
### 5.1 Project Setup
- [ ] Create `frontend` folder structure
- [ ] Initialize Vite project with React and TypeScript
- [ ] Configure ESLint and Prettier
- [ ] Set up Ant Design (antd) theme
- [ ] Configure routing with React Router
- [ ] Set up state management (Redux Toolkit or Zustand)
- [ ] Configure API client with Axios

### 5.2 Core Layout and Navigation
- [ ] Create main application layout
- [ ] Implement responsive navigation menu
- [ ] Add breadcrumb navigation
- [ ] Create loading states and skeletons
- [ ] Implement error boundary
- [ ] Add notification system

### 5.3 Task Management UI
- [ ] Task list page
  - [ ] Sortable/filterable table view
  - [ ] Quick actions (enable/disable, delete)
  - [ ] Status indicators
  - [ ] Pagination support
- [ ] Task creation wizard
  - [ ] Step 1: Basic info (ID, enabled state)
  - [ ] Step 2: Watch configuration with folder picker
  - [ ] Step 3: Variables definition
  - [ ] Step 4: Pipeline builder (drag-and-drop)
  - [ ] Step 5: Review and create
- [ ] Task edit page (redesigned)
  - [ ] Header section with ID and enabled toggle
  - [ ] Watch configuration card
  - [ ] Variables management section
  - [ ] Visual pipeline editor
  - [ ] Save/Cancel/Delete actions in header

### 5.4 Pipeline Builder
- [ ] Drag-and-drop step management
- [ ] Step configuration modals
- [ ] Visual flow representation
- [ ] Condition/decision tree visualization
- [ ] Variable interpolation preview
- [ ] Step validation indicators

### 5.5 Monitoring Dashboard
- [ ] Real-time task status display
- [ ] Execution log viewer with filters
- [ ] Performance metrics charts
- [ ] System health indicators
- [ ] File processing statistics
- [ ] Error rate monitoring

### 5.6 Advanced Features
- [ ] File/folder picker dialog component
- [ ] Variable editor with type validation
- [ ] Expression builder for conditions
- [ ] Printer selection dialog
- [ ] API endpoint tester
- [ ] Configuration import/export

---

## Phase 6: Integration and Testing
### 6.1 Backend Testing
- [ ] Unit tests for all services
- [ ] Integration tests for data access layer
- [ ] FastEndpoints integration tests using TestBase
- [ ] API endpoint tests with FastEndpoints test fixtures
- [ ] File system operation tests
- [ ] Pipeline execution tests
- [ ] Performance benchmarks
- [ ] Validator tests for all request DTOs
- [ ] Mapper tests for request/response transformations

### 6.2 Frontend Testing
- [ ] Component unit tests with React Testing Library
- [ ] Integration tests for API calls
- [ ] E2E tests with Playwright/Cypress
- [ ] Accessibility testing
- [ ] Cross-browser testing
- [ ] Performance testing

### 6.3 System Integration
- [ ] Backend-Frontend integration tests
- [ ] WebSocket/SignalR connection tests
- [ ] File watching stress tests
- [ ] Concurrent execution tests
- [ ] Database performance optimization
- [ ] Memory leak detection

---

## Phase 7: DevOps and Deployment
### 7.1 Build and CI/CD
- [ ] Create multi-stage Dockerfile for backend
- [ ] Create optimized build for frontend
- [ ] Set up GitHub Actions workflows
- [ ] Configure automated testing pipeline
- [ ] Add code quality checks (SonarQube)
- [ ] Implement semantic versioning

### 7.2 Deployment Configuration
- [ ] Create Docker Compose setup
- [ ] Add Kubernetes manifests (optional)
- [ ] Configure environment variables
- [ ] Set up secrets management
- [ ] Create deployment scripts
- [ ] Add health check endpoints

### 7.3 Documentation
- [ ] API documentation with FastEndpoints Swagger generation
- [ ] Endpoint XML documentation comments
- [ ] Request/Response DTO documentation
- [ ] User manual for UI
- [ ] Administrator guide
- [ ] Developer documentation for FastEndpoints patterns
- [ ] Configuration examples
- [ ] Troubleshooting guide
- [ ] FastEndpoints best practices guide

---

## Phase 8: Migration and Rollout
### 8.1 Data Migration
- [ ] Create migration tool from BoltDB to DuckDB
- [ ] Migrate existing configurations
- [ ] Preserve execution history
- [ ] Validate migrated data
- [ ] Create rollback procedures

### 8.2 Feature Parity Validation
- [ ] Verify all Go features are implemented
- [ ] Performance comparison testing
- [ ] Load testing and benchmarking
- [ ] User acceptance testing
- [ ] Bug fixing and optimization

### 8.3 Production Readiness
- [ ] Security audit
- [ ] Performance tuning
- [ ] Monitoring setup (Prometheus/Grafana)
- [ ] Log aggregation setup
- [ ] Backup and recovery procedures
- [ ] Disaster recovery plan

---

## Timeline Estimate
- **Phase 1**: 1 week - Backend infrastructure setup with FastEndpoints
- **Phase 2**: 1 week - File watching and event processing
- **Phase 3**: 1 week - Action implementation
- **Phase 4**: 1 week - FastEndpoints API layer
- **Phase 5**: 2 weeks - Frontend development
- **Phase 6**: 1 week - Integration and testing
- **Phase 7**: 3 days - DevOps and deployment
- **Phase 8**: 3 days - Migration and rollout

**Total Estimated Time**: 8-9 weeks

---

## FastEndpoints Development Practices
1. **Endpoint Organization**: One endpoint per file, grouped by feature
2. **Request/Response DTOs**: Separate files for each, with validators
3. **Mappers**: Use FastEndpoints mappers for entity-DTO transformations
4. **Events**: Leverage FastEndpoints event bus for decoupled communication
5. **Testing**: Use FastEndpoints.Testing for integration tests
6. **Documentation**: Use XML comments and Summary() for auto-documentation
7. **Security**: Apply policies at endpoint or group level
8. **Performance**: Use response caching and pagination where appropriate

## Risk Mitigation
1. **DuckDB Performance**: Benchmark early, have PostgreSQL as backup option
2. **File Watching at Scale**: Implement queue-based processing, add backpressure
3. **Complex UI Requirements**: Use component library, iterate with user feedback
4. **Migration Complexity**: Build migration tools early, test with production data
5. **Cross-platform Compatibility**: Test on Windows, Linux, macOS throughout development
6. **FastEndpoints Learning Curve**: Team training on vertical slice architecture

---

## Success Criteria
- [ ] All existing Go functionality replicated in C# with FastEndpoints
- [ ] New features (REST API action, decision steps) working
- [ ] UI is intuitive and responsive
- [ ] FastEndpoints API performing at high throughput
- [ ] Clean endpoint organization with vertical slice architecture
- [ ] Zero data loss during migration
- [ ] Comprehensive test coverage (>80%)
- [ ] Complete API documentation via FastEndpoints Swagger
- [ ] Successful production deployment