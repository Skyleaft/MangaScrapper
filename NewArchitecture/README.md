
# 🚀 NovaStack

**Enterprise-grade .NET 10 microservice boilerplate**  
*Vertical Slice Architecture · CQRS · Minimal API · Multi-DB (PostgreSQL · SQL Server · MongoDB) · Multi-Broker · Docker-ready*

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-13.0-239120?style=flat-square&logo=csharp)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow?style=flat-square)](LICENSE)
[![Build](https://img.shields.io/badge/build-passing-brightgreen?style=flat-square)](#)


---

## ✨ Features

| Feature | Technology |
|---------|-----------|
| **Architecture** | Vertical Slice Architecture |
| **CQRS + Mediator** | MediatR 14 with pipeline behaviors |
| **API style** | ASP.NET Core Minimal API |
| **Database (multi)** | EF Core 10 — PostgreSQL or SQL Server (config-driven) |
| **NoSQL (multi)** | MongoDB 8 via native `MongoDB.Driver` 3.x (config-driven) |
| **Complex Queries** | Dapper — high performance read-only queries (relational only) |
| **Messaging (multi)** | Native Clients (RabbitMQ.Client, Confluent.Kafka) (config-driven) |
| **Outbox pattern** | Domain events → outbox table via EF Core interceptor (relational only) |
| **Validation** | FluentValidation in MediatR pipeline |
| **Object Mapping** | Mapster — high-performance object mapping |
| **Error handling** | Railway-oriented `Result<T>` — no exceptions for domain flow |
| **Caching** | In-memory or Redis (config-driven) |
| **Identity** | Standard OpenID Connect · JWT Bearer · AccessToken + RefreshToken rotation |
| **User Management** | Register, login, profile, deactivate — full CRUD |
| **Authorization** | RBAC — role-based policies, `Admin` policy out-of-the-box |
| **Auth** | JWT Bearer |
| **Logging** | Serilog (console + rolling file, enriched) |
| **Observability** | OpenTelemetry traces + metrics + runtime instrumentation |
| **Testing** | xUnit · Moq · FluentAssertions · Testcontainers · NetArchTest |
| **Docker** | Multi-stage Dockerfiles, full docker-compose stack |

---

## 📁 Solution Structure

```
NovaStack/
├── src/
│   ├── BuildingBlocks/
│   │   ├── NovaStack.SharedKernel/        # Result<T>, Entity, Guard, Exceptions, ValueObjects
│   │   ├── NovaStack.Infrastructure/      # Auth, Cache, Logging, OTel, Persistence base, Messaging
│   │   └── NovaStack.Contracts/           # Integration events, API response shapes
│   │
│   └── Services/
│       ├── Product.Domain/                # Aggregate, ValueObjects, Domain Events, Repository interface
│       ├── Product.Application/           # CQRS vertical slices, pipeline behaviors, endpoint definitions
│       ├── Product.Infrastructure/        # EF Core DbContext, Repository impl, Native Messaging wiring
│       ├── Product.Api/                   # Minimal API host, composition root, Dockerfile
│       ├── Identity.Domain/               # User/Role/RefreshToken aggregates, RBAC Permission VO
│       ├── Identity.Application/          # 16 VSA slices: Auth, OIDC, Users, Roles
│       ├── Identity.Infrastructure/       # EF Core IdentityDbContext (identity schema), repositories
│       └── Identity.Api/                  # OIDC + Auth host (port 5010), Dockerfile
│
├── src/Workers/
│   ├── Product.Consumer/                  # MassTransit worker (RabbitMQ or Kafka)
│   └── Notification.Consumer/            # Notification dispatch worker
│
├── tests/
│   ├── UnitTests/                         # Handler unit tests (Moq + FluentAssertions)
│   ├── IntegrationTests/                  # API tests with real PostgreSQL (Testcontainers)
│   └── ArchitectureTests/                 # Dependency boundary tests (NetArchTest)
│
├── docker-compose.yml                     # Full local infrastructure stack
├── docker-compose.override.yml            # Dev environment overrides
├── Directory.Build.props                  # Shared MSBuild settings
└── NovaStack.sln
```

---

## 🏁 Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

### 1. Start infrastructure

```bash
docker-compose up postgres rabbitmq redis -d
```

### 2. Run the API

```bash
# Product API (port 5191 / 5000 in Docker)
cd src/Services/Product.Api
dotnet run

# Identity API (port 5010)
cd src/Services/Identity.Api
dotnet run
```

Product API: `http://localhost:5191` (Scalar: `http://localhost:5191/scalar/v1`)
Identity API: `http://localhost:5010` (Scalar: `http://localhost:5010/scalar/v1`)

OIDC Discovery: `http://localhost:5010/.well-known/openid-configuration`

### 3. Run everything via Docker

```bash
docker-compose up --build
```

---

## ⚙️ Configuration

All behavior is driven by `appsettings.json`. No code changes required to switch providers.

### Database

```json
"Database": {
  "Provider": "PostgreSQL",              // or "SqlServer"
  "ConnectionString": "Host=localhost;...",
  "AutoMigrate": true
}
```

### Messaging

```json
"Messaging": {
  "Provider": "RabbitMQ",               // or "Kafka"
  "RabbitMQ": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest"
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "GroupId": "product-api-group"
  }
}
```

### Caching

```json
"Cache": {
  "Provider": "InMemory",               // or "Redis"
  "RedisConnectionString": "localhost:6379"
}
```

### MongoDB

```json
"Database": {
  "Provider": "MongoDB",
  "ConnectionString": "mongodb://myuser:mypassword@localhost:27017",
  "DatabaseName": "novastack_products"
}
```

> **No migrations needed** — MongoDB is schemaless. Collections are created on first write.  
> Start the container: `docker-compose up mongo -d`

### JWT + OpenID Connect (Identity Service)

```json
"Jwt": {
  "Issuer": "NovaStack",
  "Audience": "NovaStack.Clients",
  "SecretKey": "CHANGE_ME_super_secret_key_at_least_32_chars!",
  "ExpiryMinutes": 60,
  "RefreshTokenExpiryDays": 7,
  "ValidateIssuer": true,
  "ValidateAudience": true,
  "ValidateLifetime": true,
  "OpenId": {
    "Authority": "http://localhost:5010",
    "SupportedScopes": "openid profile email",
    "SupportedResponseTypes": "code token id_token",
    "SupportedGrantTypes": "authorization_code password refresh_token"
  }
}
```

> The `OpenId.Authority` is used to build the discovery document URLs. Leave empty to auto-detect from the current request host.

### Standalone Service JWT Validation (without Identity Service)

To run a microservice (e.g., `Product.Api`) in JWT-only validation mode without calling the external OIDC/Identity service, configure the `Authentication` section in its `appsettings.json` to use local validation with a symmetric or asymmetric key:

```json
"Authentication": {
  "Issuer": "NovaStack",
  "Audience": "product-api",
  "RequireHttps": false,
  "UseLocalValidation": true,
  "Signing": {
    "Algorithm": "HS256",
    "SymmetricKey": "CHANGE_ME_super_secret_key_at_least_32_chars!"
  }
}
```

---

## 🔌 Product API Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/api/v1/products` | Create a new product |
| `GET` | `/api/v1/products` | Paginated list (`?page=1&pageSize=10&search=`) |
| `GET` | `/api/v1/products/{id}` | Get product by ID |
| `PUT` | `/api/v1/products/{id}` | Update product |
| `DELETE` | `/api/v1/products/{id}` | Soft-delete (deactivate) |
| `GET` | `/api/v1/products/stock-report` | Stock statistics/low-stock report (Dapper example) |
| `GET` | `/health` | Health check |
| `GET` | `/openapi/v1.json` | OpenAPI spec (Development only) |
| `GET` | `/scalar/v1` | Scalar API Interactive UI (Development only) |

---

## 🔐 Identity Service API (`http://localhost:5010`)

### Authentication

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/api/v1/auth/register` | — | Register a new user account |
| `POST` | `/api/v1/auth/login` | — | Login — returns `AccessToken` + `RefreshToken` |
| `POST` | `/api/v1/auth/refresh` | — | Rotate tokens using a valid refresh token |
| `POST` | `/api/v1/auth/revoke` | 🔒 | Revoke a specific refresh token |
| `POST` | `/api/v1/auth/logout` | 🔒 | Revoke ALL refresh tokens (logout all devices) |
| `GET`  | `/api/v1/auth/me` | 🔒 | Get current user profile + roles |

### OpenID Connect (Standard)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `GET` | `/.well-known/openid-configuration` | — | OIDC Discovery document |
| `GET` | `/connect/userinfo` | 🔒 | Standard UserInfo claims (OIDC Core spec) |

### User Management (Admin)

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/api/v1/users` | Paginated user list with search |
| `GET` | `/api/v1/users/{id}` | User detail with roles |
| `PUT` | `/api/v1/users/{id}` | Update user profile |
| `DELETE` | `/api/v1/users/{id}` | Soft-deactivate user + revoke tokens |

### Role Management (Admin)

| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/api/v1/roles` | Create a new RBAC role |
| `GET` | `/api/v1/roles` | List all roles |
| `POST` | `/api/v1/users/{id}/roles` | Assign role to user |
| `DELETE` | `/api/v1/users/{id}/roles/{roleId}` | Revoke role from user |

---

## 🧱 Architecture Deep Dive

### Vertical Slice Architecture

Each feature is self-contained in a folder. No cross-cutting shared layers — only shared *infrastructure*.

```text
Features/
└── Products/
    ├── CreateProduct/
    │   ├── CreateProductCommand.cs       # Record: ICommand<Guid>
    │   ├── CreateProductCommandHandler.cs
    │   ├── CreateProductCommandValidator.cs
    │   └── CreateProductEndpoint.cs     # IEndpointDefinition
    ├── GetProductById/
    ├── GetProducts/
    ├── UpdateProduct/
    └── DeleteProduct/
```

### CQRS with Result Pattern

```csharp
// Command returns Result<Guid> — no exceptions for domain flow
public record CreateProductCommand(...) : ICommand<Guid>;

// Handler
public class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateProductCommand cmd, CancellationToken ct)
    {
        if (await _repo.ExistsByNameAsync(cmd.Name, ct))
            return Error.Conflict("Product.NameConflict", "Name already exists.");
        // ...
        return product.Id.Value;
    }
}
```

### CQRS Read Optimization with Dapper

For read-only operations requiring high performance or complex logic (e.g. dashboards, statistics, or reports), the application injects `ISqlConnectionFactory` and uses **Dapper**. This bypasses EF Core's change tracker and model mapping overhead:

```csharp
public class GetProductStockReportQueryHandler(ISqlConnectionFactory sqlConnectionFactory)
    : IQueryHandler<GetProductStockReportQuery, ProductStockReportResponse>
{
    public async Task<Result<ProductStockReportResponse>> Handle(...)
    {
        using var connection = sqlConnectionFactory.CreateConnection();
        // Execute optimized raw SQL queries...
    }
}
```

> **Note**: `ISqlConnectionFactory` is only registered for relational providers (PostgreSQL / SQL Server). It is not available when `Provider` is `MongoDB`.

### Object Mapping with Mapster

For cleaner mapping logic between Domain Entities and API Contracts/DTOs, the solution utilizes **Mapster**. This decouples presentation details from domain entities and reduces manual mapping boilerplate:

```csharp
// Define mapping rules (IRegister)
public class ProductMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Product, ProductResponse>()
            .Map(dest => dest.Id, src => src.Id.Value)
            .Map(dest => dest.Price, src => src.Price.Amount)
            .Map(dest => dest.Currency, src => src.Price.Currency);
    }
}

// Inject IMapper and map
public class GetProductByIdQueryHandler(IProductRepository repo, IMapper mapper)
{
    public async Task<Result<ProductResponse>> Handle(...)
    {
        var product = await repo.GetByIdAsync(...);
        return mapper.Map<ProductResponse>(product);
    }
}
```

### Edit (UpdateProduct) Vertical Slice Example

Demonstrates a `PUT` command that returns `204 No Content` on success, matching the pattern in `Product.Application/Features/Products/UpdateProduct/UpdateProduct.cs`:

```csharp
// ICommand (no return type) → handler returns Result (not Result<T>)
public sealed record UpdateProductCommand(Guid Id, string Name, string Description,
    decimal Price, string Currency) : ICommand;

// Validator — runs in MediatR pipeline before handler
public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3).Matches("^[A-Z]{3}$");
    }
}

// Handler — returns 404 via Error.NotFound, never throws
internal sealed class UpdateProductCommandHandler(
    IProductRepository productRepository, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateProductCommand>
{
    public async Task<Result> Handle(UpdateProductCommand command, CancellationToken ct)
    {
        var product = await productRepository.GetByIdAsync(ProductId.From(command.Id), ct);
        if (product is null)
            return Error.NotFound("Product.NotFound", $"Product '{command.Id}' not found.");

        product.Update(command.Name, command.Description,
            Money.Create(command.Price, command.Currency));
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// Endpoint — auto-registered via IEndpointDefinition scanner
public sealed class UpdateProductEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/v1/products/{id:guid}", HandleAsync)
            .WithName("UpdateProduct").WithTags("Products")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> HandleAsync(
        Guid id, UpdateProductRequest req, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(
            new UpdateProductCommand(id, req.Name, req.Description, req.Price, req.Currency), ct);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToHttpResult();
    }
}
```


`DbContextBase.SaveChangesAsync` automatically captures domain events from any `IHasDomainEvents` entity and writes them to an `outbox_messages` table atomically in the same transaction.

### MediatR Pipeline

```text
Request → LoggingBehavior → ValidationBehavior → Handler → Response
```

---

## 🧪 Running Tests

```bash
# Unit tests (no Docker required)
dotnet test tests/UnitTests

# Architecture boundary tests
dotnet test tests/ArchitectureTests

# Integration tests (requires Docker)
dotnet test tests/IntegrationTests
```

---

## 🗄️ Database Migrations

```bash
# Add a migration
dotnet ef migrations add InitialCreate \
  --project src/Services/Product.Infrastructure \
  --startup-project src/Services/Product.Api \
  --output-dir Persistence/Migrations

# Apply manually
dotnet ef database update \
  --project src/Services/Product.Infrastructure \
  --startup-project src/Services/Product.Api
```

> **Auto-migrate on startup** is enabled by default (`"AutoMigrate": true`). Disable in production if you prefer manual migration runs.

---

## 🐳 Docker Infrastructure

The `docker-compose.yml` brings up the full stack:

| Service | Port | Description |
|---------|------|-------------|
| PostgreSQL | `5432` | Primary relational database |
| SQL Server | `1433` | Alternative relational database |
| MongoDB | `27017` | Document database (use when `Provider: MongoDB`) |
| RabbitMQ | `5672` / `15672` | Message broker + management UI |
| Kafka | `9092` / `29092` | Event streaming |
| Redis | `6379` | Distributed cache |
| Product API | `5000` | Product REST API |
| **Identity API** | **`5010`** | **Auth / OIDC / User Management API** |
| Product Consumer | — | Background message consumer |
| Notification Consumer | — | Notification dispatch worker |

---

## 🔭 Observability

| Signal | Implementation |
|--------|---------------|
| **Structured logs** | Serilog → console + rolling file (`logs/novastack-YYYY-MM-DD.log`) |
| **Traces** | OpenTelemetry → ASP.NET Core + HTTP client instrumentation |
| **Metrics** | OpenTelemetry → ASP.NET Core + runtime metrics |
| **OTLP export** | Add `OpenTelemetry.Exporter.OpenTelemetryProtocol` and set `Observability:OtlpEndpoint` |

---

## 🗺️ Extending NovaStack

### Add a new service (e.g., Order service)

1. Create `Order.Domain`, `Order.Application`, `Order.Infrastructure`, `Order.Api` projects
2. Add project references following the same pattern
3. Register `IEndpointDefinition` implementations in the new API's `Program.cs`
4. Add to `docker-compose.yml`

### Scalar API UI Integration

Scalar API interactive documentation is integrated into the API host projects out-of-the-box:

- **Interactive UI**: `http://localhost:5191/scalar/v1` (Local) or `http://localhost:5000/scalar/v1` (Docker)
- Powered by `Scalar.AspNetCore` mapped inside `Program.cs`.

### Add OTLP export (Jaeger / Grafana Tempo)

```bash
dotnet add src/BuildingBlocks/NovaStack.Infrastructure package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

Set `Observability__OtlpEndpoint=http://jaeger:4317` in your environment.

---

## 📦 Key NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `MediatR` | 14.x | CQRS mediator |
| `FluentValidation` | 12.x | Command/Query validation |
| `Microsoft.EntityFrameworkCore` | 10.x | ORM (relational providers) |
| `Dapper` | 2.x | High-performance Micro-ORM for read queries |
| `Mapster` | 10.x | High-performance object mapping |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.x | PostgreSQL provider |
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.x | SQL Server provider |
| `MongoDB.Driver` | 3.x | Native MongoDB client |
| `RabbitMQ.Client` | 7.x | Native RabbitMQ client |
| `Confluent.Kafka` | 2.x | Native Kafka client |
| `Serilog.AspNetCore` | 10.x | Structured logging |
| `OpenTelemetry.Extensions.Hosting` | 1.x | OTel SDK |
| `Testcontainers.PostgreSql` | 4.x | Integration test DB |
| `NetArchTest.Rules` | 1.x | Architecture enforcement |

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).

---

<div align="center">

Built with ❤️ using **.NET 10** and **Vertical Slice Architecture**

</div>

