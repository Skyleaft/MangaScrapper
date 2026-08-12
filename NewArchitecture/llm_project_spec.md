# 🚀 MangaScrapper LLM Context & Coding Specification

This document provides a token-efficient, high-density architectural and coding specification of **MangaScrapper**. It is designed to get LLM agents up to speed instantly with the project's refactored Vertical Slice Architecture (VSA), design patterns, coding conventions, and unified solution stack.

---

## 🛠️ Technology Stack

- **Runtime & Language**: .NET 10.0, C# 13.0
- **Architectural Style**: Vertical Slice Architecture (VSA) — Unified Two-Tier Structure (`MangaScrapper.Core` + Thin Host Executables)
- **CQRS & Mediator**: MediatR 14 (with logging and validation pipeline behaviors)
- **APIs**: ASP.NET Core Minimal APIs with automatic endpoint discovery (`IEndpointDefinition`)
- **Database & Persistence**: MongoDB 8 via `MongoDB.Driver` 3.x, Meilisearch 0.17 (full-text search), Qdrant 1.13 (vector search & embeddings)
- **Object Mapping**: Mapster 10.x (centralized in `MangaInfrastructureMapping.cs` & `MangaMappingConfig.cs`)
- **Validation**: FluentValidation 12
- **Error Handling**: Railway-oriented `Result<T>` and `Error` types (avoid domain exceptions for control flow)
- **Background Jobs**: Hangfire 1.8 with MongoDB Storage (`Hangfire.Mongo`)
- **Messaging & Event Bus**: Native RabbitMQ EventBus (`NovaStack.Infrastructure`) & Integration Event Handlers
- **Web Scrapers**: HtmlAgilityPack, Playwright, FlareSolverr HTTP integration
- **Testing**: xUnit, Moq, FluentAssertions, NetArchTest (Architecture validation)

---

## 📂 Project Structure

```text
NewArchitecture/
├── src/
│   ├── BuildingBlocks/
│   │   ├── NovaStack.SharedKernel/        # Result<T>, Error, ICommand/IQuery, Base Entity, Result extensions
│   │   ├── NovaStack.Infrastructure/      # Shared Auth, RabbitMQ EventBus, MongoDB base extensions
│   │   └── NovaStack.Contracts/           # Shared Responses, Integration Events, DTO contracts
│   │
│   ├── Services/MangaScrapper/
│   │   ├── MangaScrapper.Core/            # Unified VSA Core Library Project
│   │   │   ├── Features/                  # 26 Vertical Feature Slices (Co-located Request, Handler, Endpoint)
│   │   │   │   ├── Mangas/                # GetPagedManga, GetMangaById, GetChapter, DeleteManga, UpdateManga
│   │   │   │   ├── ProviderScrapers/      # Komiku, Kiryuu, Komikcast, MangaDex slices
│   │   │   │   ├── Scrapper/              # GetAllProviders, ScrapChapterPages, GetQueue, FixFile
│   │   │   │   ├── UserLibrary/           # AddOrUpdateUserLibrary, GetUserLibrary, RemoveUserLibrary
│   │   │   │   ├── UserProgression/       # UpdateUserProgression, GetUserProgression, GetMangaProgression
│   │   │   │   ├── Users/                 # GetPagedUser, GetUserById, PatchUserActivity
│   │   │   │   ├── Providers/             # GetProvider
│   │   │   │   ├── Dashboard/             # GetStatistics, SyncStorage
│   │   │   │   ├── Images/                # ProxyImage
│   │   │   │   └── RecurringJobs/         # GetRecurringJobs, CreateOrUpdate, Delete, Trigger
│   │   │   │
│   │   │   ├── Domain/                    # Domain Layer
│   │   │   │   ├── Aggregates/            # Manga (Aggregate Root), Chapter, Page, User, UserLibrary, UserProgression
│   │   │   │   ├── ValueObjects/          # MangaId, UserId, ChapterId, PageId
│   │   │   │   ├── DomainEvents/          # ChapterAddedDomainEvent, MangaCreatedDomainEvent
│   │   │   │   └── Repositories/          # IMangaRepository, IUserRepository, IUserLibraryRepository
│   │   │   │
│   │   │   ├── Scrapers/                  # Scraper Provider Implementations
│   │   │   │   ├── Abstractions/          # ScrapperServiceBase, IScrapperService, IScrapperRepository
│   │   │   │   ├── Komiku/                # KomikuService
│   │   │   │   ├── Kiryuu/                # KiryuuService
│   │   │   │   ├── Komikcast/             # KomikcastService, KomikcastModel
│   │   │   │   └── MangaDex/              # MangaDexService, MangaDexModel
│   │   │   │
│   │   │   ├── Persistence/               # Mongo DbContext, BSON Document schemas (MangaDocument, etc.)
│   │   │   ├── Repositories/              # MongoMangaRepository, MongoUserRepository, MongoUserLibraryRepository
│   │   │   ├── Services/                  # MeilisearchService, QdrantService, DiscordWebhookService, StorageSyncService
│   │   │   ├── BackgroundJobs/            # Hangfire background jobs (MeiliSyncJob, DeleteMangaJob, LatestChapterScrapingJob)
│   │   │   ├── Messaging/                 # RabbitMQ integration event handlers (ScrapChapterPagesHandler, DeleteMangaHandler)
│   │   │   ├── Security/                  # Custom Auth validation & JwtAuthTokenService
│   │   │   └── DependencyInjection/       # InfrastructureExtensions (AddMangaScrapperInfrastructure)
│   │   │
│   │   ├── MangaScrapper.Api/             # Thin Web API Host entry point (Program.cs, Swagger/Scalar, Auth)
│   │   └── MangaPanel.Client/             # Blazor WebAssembly Frontend Client
│   │
│   └── Workers/
│       └── Scrapper.Worker/               # Thin Background Worker Host entry point (Hangfire Server & RabbitMQ Consumer)
│
└── tests/
    ├── UnitTests/                         # Business logic tests (Moq + FluentAssertions)
    ├── IntegrationTests/                  # Integration test harness
    └── ArchitectureTests/                 # Architectural constraint tests (NetArchTest)
```

---

## 🧱 Key Design Patterns & Coding Conventions

LLM agents MUST strictly adhere to these patterns when maintaining or extending this repository:

### 1. Single-File Vertical Slice Co-location Pattern
All feature slices inside `MangaScrapper.Core/Features/[Category]/[SliceName]/` MUST be co-located inside a single `{SliceName}.cs` file containing:
1. `Command` or `Query` record (implementing `ICommand<T>` or `IQuery<T>`)
2. Handler class (`internal sealed` or `public sealed`, implementing `ICommandHandler` or `IQueryHandler`)
3. Optional `Validator` class (implementing `AbstractValidator<T>`)
4. Endpoint Definition class (`public sealed class [SliceName]Endpoint : IEndpointDefinition`)

#### Standard Vertical Slice Template:
```csharp
using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.SampleCategory.SampleFeature;

// 1. Command / Query Record
public sealed record SampleCommand(string Name) : ICommand<Guid>;

// 2. Handler
public sealed class SampleCommandHandler(IMangaRepository mangaRepository)
    : ICommandHandler<SampleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SampleCommand command, CancellationToken ct)
    {
        // Business logic operating on Domain Aggregate
        return Guid.NewGuid();
    }
}

// 3. Minimal API Endpoint Definition
public sealed class SampleFeatureEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/sample", HandleAsync)
            .WithName("SampleFeature")
            .WithSummary("Execute sample command")
            .WithTags("Sample")
            .Produces<ApiResponse<Guid>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> HandleAsync(
        SampleCommand command,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return result.IsSuccess
            ? Results.Ok(ApiResponse.Ok(result.Value))
            : result.Error.ToHttpResult();
    }
}
```

---

### 2. Domain Model Centralization & Mapster Projections

1. **Domain Aggregates**: All business rules and domain operations center on the `Manga` Domain Aggregate (`MangaScrapper.Core.Domain.Aggregates.Manga`).
2. **Document Mapping**: Mongo persistence converts transparently between `Manga` domain aggregates and `MangaDocument` BSON schemas using Mapster `.Adapt<T>()`.
3. **Mapster Registration**:
   - `MangaMappingConfig.cs` in Application handles `Manga` $\rightarrow$ `MangaSummaryResponse` / `ChapterResponse`.
   - `MangaInfrastructureMapping.cs` in Repositories handles `Manga` $\leftrightarrow$ `MangaDocument` and `MeiliMangaDocument`.

#### Repository Pattern Example:
```csharp
public async Task<Manga?> GetByIdAsync(MangaId id, CancellationToken ct = default)
{
    var doc = await dbContext.Mangas.Find(m => m.Id == id.Value).FirstOrDefaultAsync(ct);
    return doc is null ? null : doc.Adapt<Manga>();
}

public async Task AddAsync(Manga manga, CancellationToken ct = default)
{
    var doc = manga.Adapt<MangaDocument>();
    await dbContext.Mangas.InsertOneAsync(doc, cancellationToken: ct);
}
```

---

### 3. Automatic Minimal API Endpoint Registration

Endpoints are automatically discovered via reflection at startup by `MapMangaScrapperEndpoints()` scanning `MangaScrapper.Core`:
```csharp
public static IEndpointRouteBuilder MapMangaScrapperEndpoints(this IEndpointRouteBuilder app)
{
    var endpointTypes = typeof(IEndpointDefinition).Assembly.GetTypes()
        .Where(t => typeof(IEndpointDefinition).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

    foreach (var type in endpointTypes)
    {
        var endpoint = (IEndpointDefinition)Activator.CreateInstance(type)!;
        endpoint.DefineEndpoints(app);
    }

    return app;
}
```

---

### 4. Railway-Oriented Error Handling (`Result<T>`)

Do NOT throw domain exceptions for business validation failures. Use `Result.Success()` or `Result.Failure(Error)`:

```csharp
// Standard Error Definitions
Error.NotFound("Manga.NotFound", "Manga was not found.");
Error.Conflict("User.AlreadyExists", "Username is already taken.");
Error.Validation("Request.Invalid", "Search parameter cannot be empty.");
```

Handlers return `Result<T>`, and endpoints convert errors to HTTP results via `result.Error.ToHttpResult()`.

---

## 🧪 Verification & Testing Commands

To verify changes in this solution:

- **Build Solution**:
  ```bash
  dotnet build NewArchitecture/MangaScrapperStack.sln
  ```
- **Run Unit Tests**:
  ```bash
  dotnet test NewArchitecture/tests/UnitTests/UnitTests.csproj
  ```
- **Run Architecture Tests**:
  ```bash
  dotnet test NewArchitecture/tests/ArchitectureTests/ArchitectureTests.csproj
  ```

---

## 📌 Rules for LLM Agents

1. **Keep Vertical Slices Co-Located**: Do NOT break `{SliceName}.cs` back into separate files unless explicitly requested by the user.
2. **Never Bypass Domain Aggregates**: Always use `IMangaRepository` and `Manga` Domain Aggregates rather than raw MongoDB documents in features and services.
3. **Use Mapster for Mappings**: Use `.Adapt<T>()` for object mapping instead of manual properties mapping loops.
4. **Always Verify**: Run `dotnet build` and `dotnet test` before declaring task completion.
