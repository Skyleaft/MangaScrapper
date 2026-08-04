# 🚀 NovaStack LLM Context & Coding Specification

This document provides a token-efficient, high-density architectural and coding specification of **NovaStack**. It is designed to get LLM agents up to speed instantly with the project's structure, design patterns, coding conventions, and stack.

---

## 🛠️ Technology Stack

- **Runtime & Language**: .NET 10.0, C# 13.0
- **Architectural Style**: Vertical Slice Architecture (VSA)
- **CQRS & Mediator**: MediatR 14 (with logging and validation pipeline behaviors)
- **APIs**: ASP.NET Core Minimal APIs with custom route scanner (`IEndpointDefinition`)
- **Database (Multi-DB)**: EF Core 10 (PostgreSQL via Npgsql, SQL Server via MS SqlClient), MongoDB 8 via native `MongoDB.Driver` 3.x
- **Query Optimization**: Dapper (for high-performance read-only queries on relational providers)
- **Object Mapping**: Mapster
- **Validation**: FluentValidation 12
- **Error Handling**: Railway-oriented `Result<T>` and `Error` types (avoid domain exceptions)
- **Messaging (Multi-Broker)**: Native Clients for RabbitMQ and Confluent Kafka
- **Reliability**: Outbox Pattern (Domain Events captured automatically in `SaveChangesAsync`) — relational providers only
- **Testing**: xUnit, Moq, FluentAssertions, Testcontainers, NetArchTest (Architecture validation)

---

## 📂 Project Structure

```text
NovaStack/
├── src/
│   ├── BuildingBlocks/
│   │   ├── NovaStack.SharedKernel/        # Domain/Result bases: Result<T>, Entity<T>, Guard, exceptions
│   │   ├── NovaStack.Infrastructure/      # Shared wiring: Auth, Cache, Logging, OTel, Persistence, Messaging
│   │   └── NovaStack.Contracts/           # Inter-service schemas: Integration events, ApiResponse shapes
│   │
│   └── Services/
│       ├── Product.Domain/                # Aggregate roots, ValueObjects, Domain Events, Repositories interfaces
│       ├── Product.Application/           # CQRS vertical slices (Command/Query/Validator/Endpoint)
│       ├── Product.Infrastructure/        # EF DbContext, Repository impls, migrations, Dapper SqlConnectionFactory
│       └── Product.Api/                   # Composition root, Program.cs, config, Dockerfile
│
├── src/Workers/
│   ├── Product.Consumer/                  # Background service consumer (handles integration events)
│   └── Notification.Consumer/             # Background notification consumer
│
└── tests/
    ├── UnitTests/                         # Business logic tests (Moq + FluentAssertions)
    ├── IntegrationTests/                  # API tests with Docker PostgreSQL (Testcontainers)
    └── ArchitectureTests/                 # Structural constraint tests (NetArchTest)
```

---

## 🧱 Design Patterns & Coding Conventions

LLMs must follow these specific templates and patterns when adding new code.

### 1. Vertical Slice Architecture (VSA)
Features are placed inside self-contained folders in `Product.Application/Features/[FeatureName]/[CommandOrQuery]`. Each slice contains:
- `Command` or `Query` record
- `CommandHandler` or `QueryHandler` (`internal sealed`, primary constructors)
- `Validator` class (FluentValidation)
- `Endpoint` class implementing `IEndpointDefinition`

#### Command Vertical Slice Skeleton:
```csharp
// Features/Products/CreateProduct/CreateProductCommand.cs
using Product.Application.Common.Abstractions;

namespace Product.Application.Features.Products.CreateProduct;

public sealed record CreateProductCommand(
    string Name,
    string Description,
    decimal Price,
    string Currency,
    int StockQuantity
) : ICommand<Guid>;

// Features/Products/CreateProduct/CreateProductCommandHandler.cs
using NovaStack.SharedKernel.Abstractions;
using NovaStack.SharedKernel.Results;
using Product.Application.Common.Abstractions;
using Product.Domain.Repositories;
using Product.Domain.ValueObjects;
using DomainProduct = Product.Domain.Aggregates.Product;

namespace Product.Application.Features.Products.CreateProduct;

internal sealed class CreateProductCommandHandler(
    IProductRepository productRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateProductCommand command, CancellationToken ct)
    {
        if (await productRepository.ExistsByNameAsync(command.Name, ct))
            return Error.Conflict("Product.NameConflict", $"Product '{command.Name}' already exists.");

        var product = DomainProduct.Create(
            ProductId.New(),
            command.Name,
            command.Description,
            Money.Create(command.Price, command.Currency),
            command.StockQuantity);

        await productRepository.AddAsync(product, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return product.Id.Value;
    }
}

// Features/Products/CreateProduct/CreateProductCommandValidator.cs
using FluentValidation;

namespace Product.Application.Features.Products.CreateProduct;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}

// Features/Products/CreateProduct/CreateProductEndpoint.cs
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;
using Product.Application.Common.Abstractions;

namespace Product.Application.Features.Products.CreateProduct;

public sealed class CreateProductEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/products", HandleAsync)
            .WithName("CreateProduct")
            .WithSummary("Create a new product")
            .WithTags("Products")
            .Produces<ApiResponse<Guid>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> HandleAsync(
        CreateProductCommand command,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(command, ct);

        return result.IsSuccess
            ? Results.Created($"/api/v1/products/{result.Value}", ApiResponse.Ok(result.Value, "Product created successfully."))
            : result.Error.ToHttpResult();
    }
}
```

### 2. Domain & Aggregates
Domain entities inherit from `Entity<TId>` (which implements `IEntity<TId>`, `IAggregateRoot<TId>`, and `IHasDomainEvents`).
- **Encapsulation**: Properties must be read-only from the outside (`{ get; private set; }`).
- **Factories**: Entities are instantiated via static factory methods (`Create(...)`) to enforce invariants.
- **Invariants**: Use the `Guard` class for validation. If violated, throw a `DomainException`.
- **Domain Events**: Raise events using `RaiseDomainEvent(...)`.

```csharp
using NovaStack.SharedKernel.Common;
using NovaStack.SharedKernel.Exceptions;

namespace Product.Domain.Aggregates;

public sealed class Product : Entity<ProductId>
{
    public string Name { get; private set; } = null!;
    public Money Price { get; private set; } = null!;
    
    private Product() : base() { } // Required for EF Core

    public static Product Create(ProductId id, string name, Money price)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        Guard.NotNull(price, nameof(price));

        var product = new Product { Id = id, Name = name, Price = price };
        product.RaiseDomainEvent(new ProductCreatedDomainEvent(id.Value, name));
        return product;
    }
}
```

### 3. Read Optimization with Dapper
Avoid EF Core for complex read-only queries (e.g. search, pagination, stock reports). Inject `ISqlConnectionFactory` and use raw SQL with Dapper:

```csharp
using Dapper;
using NovaStack.SharedKernel.Results;
using NovaStack.SharedKernel.Abstractions;
using Product.Application.Common.Abstractions;

namespace Product.Application.Features.Products.GetProductStockReport;

public record GetProductStockReportQuery() : IQuery<ProductStockReportResponse>;

internal sealed class GetProductStockReportQueryHandler(ISqlConnectionFactory sqlConnectionFactory)
    : IQueryHandler<GetProductStockReportQuery, ProductStockReportResponse>
{
    public async Task<Result<ProductStockReportResponse>> Handle(GetProductStockReportQuery query, CancellationToken ct)
    {
        using var connection = sqlConnectionFactory.CreateConnection();
        const string sql = @"
            SELECT 
                COUNT(*) AS TotalProducts,
                COALESCE(SUM(stock_quantity), 0) AS TotalStock
            FROM products.products 
            WHERE is_active = true";

        var stats = await connection.QuerySingleOrDefaultAsync<ProductStockReportResponse>(sql);
        return stats ?? new ProductStockReportResponse(0, 0);
    }
}
```

### 4. Railway-Oriented Error Handling
Always return `Result<T>` or `Result` for application/domain control flow. Use `Error` types to signal business failures.
- Mapping to HTTP: Call `result.Error.ToHttpResult()` inside Endpoint handlers.
- Errors are mapped as follows in `ErrorExtensions`:
  - `ErrorType.NotFound` ➔ `404 Not Found`
  - `ErrorType.Validation` ➔ `400 Bad Request`
  - `ErrorType.Conflict` ➔ `409 Conflict`
  - `ErrorType.Unauthorized` ➔ `401 Unauthorized`
  - `ErrorType.Forbidden` ➔ `403 Forbidden`
  - `ErrorType.Failure` / default ➔ `500 Internal Server Error`

### 5. Outbox Pattern & Domain Events
- Any aggregate domain events raised during a transaction are intercepted during `SaveChangesAsync` inside `DbContextBase.cs`.
- They are serialized into the `outbox_messages` database table in the same atomic transaction.
- Background dispatchers process the outbox and forward messages downstream as integration events.

### 6. Integration Events & Messaging
Integration events reside in `NovaStack.Contracts/IntegrationEvents` and implement `IIntegrationEvent`.
- **Brokers**: Driven by RabbitMQ or Kafka depending on configuration (`appsettings.json` Messaging:Provider).
- **Publishing**: Inject and use `IEventBus`.
- **Consuming**: Create a consumer class inheriting from `IIntegrationEventHandler<TEvent>` and register it in the worker's `Program.cs`.

```csharp
// Consumer class
public sealed class ProductCreatedConsumer(ILogger<ProductCreatedConsumer> logger)
    : IIntegrationEventHandler<ProductCreatedIntegrationEvent>
{
    public async Task HandleAsync(ProductCreatedIntegrationEvent integrationEvent, CancellationToken ct)
    {
        logger.LogInformation("Processing event: {EventId}", integrationEvent.EventId);
        await Task.CompletedTask;
    }
}

// Program.cs Registration
builder.Services.AddScoped<ProductCreatedConsumer>();
if (messagingOptions.Provider == MessagingProvider.RabbitMQ)
{
    builder.Services.AddRabbitMqConsumer<ProductCreatedIntegrationEvent, ProductCreatedConsumer>("product-created-queue");
}
```

---

### 7. MongoDB Native Driver

When `Database:Provider` is `MongoDB`, EF Core is **not** used. The stack switches to the native `MongoDB.Driver`.

**Key differences from relational providers:**
- No EF migrations — MongoDB is schemaless. Collections are created on first write.
- No outbox pattern — domain events are published directly after each write (fire-and-forget). Add a Mongo outbox collection explicitly if at-least-once delivery is required.
- No `IUnitOfWork` / `ISqlConnectionFactory` registered.
- Use `Reconstitute(...)` factory on domain aggregates to hydrate from documents (no EF private-setter magic).

#### Configuration (`appsettings.json`)
```json
"Database": {
  "Provider": "MongoDB",
  "ConnectionString": "mongodb://myuser:mypassword@localhost:27017",
  "DatabaseName": "novastack_products"
}
```

Start MongoDB: `docker-compose up mongo -d`

#### Shared base types (`NovaStack.Infrastructure`)

```csharp
// NovaStack.Infrastructure/Persistence/MongoDb/IMongoDbContext.cs
public interface IMongoDbContext
{
    IMongoCollection<T> GetCollection<T>(string name);
}

// NovaStack.Infrastructure/Persistence/MongoDb/MongoDbContextBase.cs
public abstract class MongoDbContextBase : IMongoDbContext
{
    private readonly IMongoDatabase _database;
    protected MongoDbContextBase(IMongoClient client, string databaseName)
        => _database = client.GetDatabase(databaseName);

    public IMongoCollection<T> GetCollection<T>(string name)
        => _database.GetCollection<T>(name);
}
```

#### Service-specific context & document POCO

```csharp
// Product.Infrastructure/Persistence/Documents/ProductDocument.cs
public sealed class ProductDocument
{
    [BsonId, BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }
    [BsonElement("name")]   public string Name { get; set; } = string.Empty;
    [BsonElement("price_amount")] public decimal PriceAmount { get; set; }
    [BsonElement("price_currency")] public string PriceCurrency { get; set; } = string.Empty;
    // ... other fields
}

// Product.Infrastructure/Persistence/ProductMongoDbContext.cs
public sealed class ProductMongoDbContext(IMongoClient client, string dbName)
    : MongoDbContextBase(client, dbName)
{
    public IMongoCollection<ProductDocument> Products =>
        GetCollection<ProductDocument>("products");
}
```

#### Repository skeleton

```csharp
// Product.Infrastructure/Repositories/MongoProductRepository.cs
internal sealed class MongoProductRepository(ProductMongoDbContext context) : IProductRepository
{
    private readonly IMongoCollection<ProductDocument> _collection = context.Products;

    public async Task<DomainProduct?> GetByIdAsync(ProductId id, CancellationToken ct = default)
    {
        var doc = await _collection
            .Find(Builders<ProductDocument>.Filter.Eq(d => d.Id, id.Value))
            .FirstOrDefaultAsync(ct);
        return doc is null ? null : MapToDomain(doc);
    }

    public async Task AddAsync(DomainProduct entity, CancellationToken ct = default) =>
        await _collection.InsertOneAsync(MapToDocument(entity), cancellationToken: ct);

    public async Task UpdateAsync(DomainProduct entity, CancellationToken ct = default) =>
        await _collection.ReplaceOneAsync(
            Builders<ProductDocument>.Filter.Eq(d => d.Id, entity.Id.Value),
            MapToDocument(entity), cancellationToken: ct);

    // MapToDomain uses DomainProduct.Reconstitute(...) — does NOT raise domain events
    private static DomainProduct MapToDomain(ProductDocument doc) =>
        DomainProduct.Reconstitute(ProductId.From(doc.Id), doc.Name, ...);
}
```

#### DI registration (`InfrastructureExtensions.cs`)

```csharp
// MongoDB branch (no EF Core, no UoW, no SQL factory)
if (dbOptions.Provider == DatabaseProvider.MongoDB)
{
    services.AddSingleton<IMongoClient>(_ => new MongoClient(dbOptions.ConnectionString));
    services.AddScoped<ProductMongoDbContext>(sp =>
        new ProductMongoDbContext(sp.GetRequiredService<IMongoClient>(), dbOptions.DatabaseName));
    return services;
}
// IProductRepository resolves MongoProductRepository when ProductMongoDbContext is registered
services.AddScoped<IProductRepository>(sp =>
    sp.GetService<ProductMongoDbContext>() is { } ctx
        ? new MongoProductRepository(ctx)
        : new ProductRepository(sp.GetRequiredService<ProductDbContext>()));
```

---

### 8. UpdateProduct — Edit Vertical Slice Example

This is a complete, **real** example from the codebase at
`Product.Application/Features/Products/UpdateProduct/UpdateProduct.cs`.

```csharp
// ── Command ──────────────────────────────────────────────────────────────────
public sealed record UpdateProductCommand(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    string Currency
) : ICommand;   // ICommand (no return value) — returns Result

// ── Validator ─────────────────────────────────────────────────────────────────
public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3).Matches("^[A-Z]{3}$");
    }
}

// ── Handler ───────────────────────────────────────────────────────────────────
internal sealed class UpdateProductCommandHandler(
    IProductRepository productRepository,
    IUnitOfWork unitOfWork)              // IUnitOfWork is null for MongoDB — inject conditionally
    : ICommandHandler<UpdateProductCommand>
{
    public async Task<Result> Handle(UpdateProductCommand command, CancellationToken ct)
    {
        var product = await productRepository.GetByIdAsync(ProductId.From(command.Id), ct);
        if (product is null)
            return Error.NotFound("Product.NotFound", $"Product with id '{command.Id}' was not found.");

        product.Update(command.Name, command.Description, Money.Create(command.Price, command.Currency));
        await unitOfWork.SaveChangesAsync(ct);  // EF Core saves + outbox flush
        // For MongoDB: call productRepository.UpdateAsync(product, ct) directly

        return Result.Success();
    }
}

// ── Endpoint ──────────────────────────────────────────────────────────────────
public sealed class UpdateProductEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/v1/products/{id:guid}", HandleAsync)
            .WithName("UpdateProduct")
            .WithSummary("Update an existing product")
            .WithTags("Products")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        UpdateProductRequest request,
        ISender sender,
        CancellationToken ct)
    {
        var command = new UpdateProductCommand(
            id, request.Name, request.Description, request.Price, request.Currency);
        var result = await sender.Send(command, ct);

        return result.IsSuccess
            ? Results.NoContent()           // 204 — no body on successful update
            : result.Error.ToHttpResult();
    }
}

// ── Request DTO ───────────────────────────────────────────────────────────────
public sealed record UpdateProductRequest(
    string Name,
    string Description,
    decimal Price,
    string Currency);
```

**Key patterns demonstrated:**
- `ICommand` (no generic type arg) → handler returns `Result` (not `Result<T>`)
- `404 Not Found` returned as `Error.NotFound(...)` — no exception thrown
- HTTP `204 No Content` on success (edit operations return no body)
- All four VSA files in one file here for brevity; split into separate `.cs` files in the real codebase

## 🚫 LLM Guardrails & Code Standards

- **DO NOT** use horizontal layered structures (no controllers or separate services directory inside application layers).
- **DO NOT** throw domain/business exceptions in command/query handlers. Use `Result<T>` and `Error` variants.
- **DO** use primary constructors (`class CreateProductCommandHandler(IProductRepository productRepository...)`) in handlers and services.
- **DO** seal all command and query handlers (`internal sealed class`).
- **DO** use Dapper and raw SQL for high-performance read-only endpoints.
- **DO** use EF Core database interceptors/Outbox for transaction boundary synchronization.
- **DO NOT** register endpoints manually in `Program.cs`. Implementing `IEndpointDefinition` allows them to be scanned and registered automatically.
- **DO NOT** write unit tests with real databases. Use Mocking (`Moq`) for repositories, and verify execution flows.
- **DO** run `ArchitectureTests` to verify dependency directions (Domain ➔ Application ➔ Infrastructure).

---

## ⚡ Dev Operations

### Run Applications
- Start DB and services: `docker-compose up postgres rabbitmq redis -d`
- Run API: `cd src/Services/Product.Api && dotnet run`

### Run Migration Commands
```bash
# Add a migration
dotnet ef migrations add [MigrationName] --project src/Services/Product.Infrastructure --startup-project src/Services/Product.Api --output-dir Persistence/Migrations

# Update Database
dotnet ef database update --project src/Services/Product.Infrastructure --startup-project src/Services/Product.Api
```

### Run Tests
```bash
dotnet test tests/UnitTests
dotnet test tests/ArchitectureTests
dotnet test tests/IntegrationTests
```

---

## 🔐 Identity Service

The Identity Service is a **self-contained microservice** (`Identity.*` projects) that implements Standard OpenID Connect, RBAC authorization, and access/refresh token management. It follows the exact same VSA, CQRS, and Result pattern as the Product service.

### Projects

| Project | Role |
|---|---|
| `Identity.Domain` | Aggregates (`User`, `Role`, `RefreshToken`), ValueObjects (`UserId`, `RoleId`, `Permission`), Repository interfaces |
| `Identity.Application` | 16 VSA slices across Auth, OIDC, Users, Roles |
| `Identity.Infrastructure` | EF Core `IdentityDbContext` (`identity` schema), 3 Repositories, Dapper factory |
| `Identity.Api` | Composition root, Program.cs, OIDC config, Dockerfile, port `5010` |

### OIDC Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| `GET` | `/.well-known/openid-configuration` | — | OIDC Discovery document |
| `GET` | `/connect/userinfo` | 🔒 | Standard UserInfo claims (`sub`, `email`, `given_name`, `family_name`, `roles`) |

### Auth Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| `POST` | `/api/v1/auth/register` | — | Register new user account |
| `POST` | `/api/v1/auth/login` | — | Login → AccessToken + RefreshToken |
| `POST` | `/api/v1/auth/refresh` | — | Rotate expired access token via refresh token |
| `POST` | `/api/v1/auth/revoke` | 🔒 | Revoke a specific refresh token |
| `POST` | `/api/v1/auth/logout` | 🔒 | Revoke ALL refresh tokens (all devices) |
| `GET`  | `/api/v1/auth/me` | 🔒 | Get current user profile + roles |

### User Management Endpoints (Admin only)

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/v1/users` | Paginated list with search (Dapper) |
| `GET` | `/api/v1/users/{id}` | Get user detail + roles |
| `PUT` | `/api/v1/users/{id}` | Update profile (firstName, lastName) |
| `DELETE` | `/api/v1/users/{id}` | Soft-deactivate + revoke all tokens |

### Role Management Endpoints (Admin only)

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/v1/roles` | Create a new RBAC role |
| `GET` | `/api/v1/roles` | List all roles |
| `POST` | `/api/v1/users/{id}/roles` | Assign role to user |
| `DELETE` | `/api/v1/users/{id}/roles/{roleId}` | Revoke role from user |

### Token Flow

```text
POST /api/v1/auth/login
  → Validate credentials (IPasswordHasher)
  → Load roles from DB
  → Issue JWT (HS256, sub/email/roles/jti/iat/exp/iss/aud)
  → Issue opaque RefreshToken (64-byte random, stored in DB)
  → Return { accessToken, refreshToken, tokenType, expiresIn, roles }

POST /api/v1/auth/refresh
  → Validate expired JWT (signature only, skip lifetime)
  → Look up refresh token in DB — must be active & match user
  → Revoke old refresh token (rotation)
  → Issue new access token + new refresh token
```

### RBAC Model

- Roles are stored in the `identity.roles` table and assigned to users via `identity.user_roles` (many-to-many).
- JWT carries `roles` claims (`ClaimTypes.Role`) — one per role.
- Admin-restricted endpoints use `.RequireAuthorization("Admin")` which maps to `RequireRole("Admin")` policy.
- Permissions (`resource:action` value objects) are stored on `Role` and available for fine-grained checks.

### Configuration

```json
"Jwt": {
  "Issuer": "NovaStack",
  "Audience": "NovaStack.Clients",
  "SecretKey": "CHANGE_ME_super_secret_key_at_least_32_chars!",
  "ExpiryMinutes": 60,
  "RefreshTokenExpiryDays": 7,
  "OpenId": {
    "Authority": "https://identity.example.com",
    "SupportedScopes": "openid profile email",
    "SupportedResponseTypes": "code token id_token",
    "SupportedGrantTypes": "authorization_code password refresh_token"
  }
}
```

### Standalone Service JWT Validation (without Identity Service)

To run a microservice (such as `Product.Api`) as a standalone service validating JWTs locally (avoiding external JWKS lookup to Identity Service), configure its `appsettings.json` Authentication section as follows:

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

### Run Identity API

```bash
# Start DB and cache
docker-compose up postgres redis -d

# Run Identity API (port 5010)
cd src/Services/Identity.Api && dotnet run

# OIDC Discovery
curl http://localhost:5010/.well-known/openid-configuration

# Scalar UI
open http://localhost:5010/scalar/v1
```

### EF Core Migrations (Identity)

```bash
dotnet ef migrations add InitialIdentity \
  --project src/Services/Identity.Infrastructure \
  --startup-project src/Services/Identity.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/Services/Identity.Infrastructure \
  --startup-project src/Services/Identity.Api
```

