# MangaScrapper → NovaStack Migration Task List

---

## Phase 1 — BuildingBlocks Extension

### 1.1 NovaStack.SharedKernel
- [x] Verify `Entity<TId>`, `IAggregateRoot<TId>`, `IHasDomainEvents` exist
- [x] Verify `Guard` class covers null, empty string, range checks
- [x] Verify `Result<T>` / `Result` / `Error` / `ErrorType` types are complete
- [x] Add `Guard.NotEmpty<T>(IEnumerable<T>)` if missing
- [x] Verify `DomainException` is present

### 1.2 NovaStack.Infrastructure — Persistence/MongoDb
- [x] Verify `IMongoDbContext` and `MongoDbContextBase` exist
- [x] Verify MongoDB camelCase convention registration helper is in place
- [x] Verify `GuidSerializer(GuidRepresentation.Standard)` registration in bootstrap

### 1.3 NovaStack.Infrastructure — Observability
- [x] Create `ObservabilityExtensions.AddMangaScrapperOtel(...)` — wraps current OTel setup from `Program.cs`:
  - Tracing: AspNetCore, HttpClient, MongoDB sources
  - Metrics: AspNetCore, HttpClient, Runtime, Process, Prometheus exporter
  - Logging: OTLP exporter (conditional on env var)

### 1.4 NovaStack.Infrastructure — Authentication
- [x] Move `CustomAuthSchemeOptions` and `CustomAuthValidation` into `MangaScrapper.Infrastructure/Security/`
- [x] Create `AuthExtensions.AddMangaScrapperAuth(...)` — Cookie + CustomAuth + Data Protection wiring

### 1.5 NovaStack.Infrastructure — Http
- [x] Move `HttpConfig` (ConfigureClient + CreateHandler) to `NovaStack.Infrastructure/Http/HttpConfig.cs`
- [x] Create named client registration helpers

### 1.6 NovaStack.Contracts
- [x] Verify `ApiResponse<T>` wrapper exists
- [x] Add `MangaSummaryResponse` record
- [x] Add `ChapterResponse` record
- [x] Add `UserLibraryResponse` record
- [x] Add `UserProgressionResponse` record
- [x] Add `DashboardStatisticResponse` record
- [x] Add `ScrapStatsResponse` record
- [x] Add `StorageSyncReportResponse` record
- [x] Migrate `MangaScrapper.Shared/Models/` DTOs → `NovaStack.Contracts/Responses/`

---

## Phase 2 — MangaScrapper.Domain (NEW PROJECT)

### 2.1 Project Setup
- [ ] Create `src/Services/MangaScrapper/MangaScrapper.Domain/MangaScrapper.Domain.csproj`
- [ ] Add reference to `NovaStack.SharedKernel`
- [ ] Add project to `MangaScrapperStack.sln` under `Services/MangaScrapper` folder

### 2.2 Value Objects
- [ ] Create `ValueObjects/MangaId.cs` (strongly-typed Guid wrapper)
- [ ] Create `ValueObjects/ChapterId.cs`
- [ ] Create `ValueObjects/UserId.cs`
- [ ] Create `ValueObjects/MangaSource.cs` (enum: Komiku, Kiryuu, Komikcast, MangaDex)
- [ ] Create `ValueObjects/MangaStatus.cs` (enum: Ongoing, Completed, Hiatus)

### 2.3 Domain Aggregates
- [ ] Create `Aggregates/Manga.cs` — `Entity<MangaId>` with:
  - Properties: `Title`, `Slug`, `CoverImage`, `Status`, `Source`, `Genres`, `Chapters`, `TotalView`, `LastUpdated`
  - Factory: `Manga.Create(...)`
  - Factory: `Manga.Reconstitute(...)` (for MongoDB hydration, no events raised)
  - Methods: `UpdateMetadata(...)`, `AddChapter(...)`, `DeleteChapter(...)`
  - Domain Events: `MangaCreatedDomainEvent`, `ChapterScrapedDomainEvent`
- [ ] Create `Aggregates/UserLibrary.cs` — `Entity<Guid>` with:
  - Properties: `UserId`, `MangaId`, `AddedAt`
  - Factory: `UserLibrary.Create(...)`, `UserLibrary.Reconstitute(...)`
  - Domain Event: `UserLibraryUpdatedDomainEvent`
- [ ] Create `Aggregates/UserProgression.cs` — `Entity<Guid>` with:
  - Properties: `UserId`, `MangaId`, `LastReadChapterId`, `LastReadAt`
  - Factory: `UserProgression.Create(...)`, `UserProgression.Reconstitute(...)`
  - Method: `UpdateProgression(...)`

### 2.4 Domain Events
- [ ] Create `DomainEvents/MangaCreatedDomainEvent.cs`
- [ ] Create `DomainEvents/ChapterScrapedDomainEvent.cs`
- [ ] Create `DomainEvents/UserLibraryUpdatedDomainEvent.cs`

### 2.5 Repository Interfaces
- [ ] Move `IMangaRepository.cs` → `Repositories/IMangaRepository.cs` (update to use domain types)
- [ ] Move `IUserLibraryRepository.cs` → `Repositories/IUserLibraryRepository.cs`
- [ ] Move `IUserProgressionRepository.cs` → `Repositories/IUserProgressionRepository.cs`

---

## Phase 3 — MangaScrapper.Application (NEW PROJECT)

### 3.1 Project Setup
- [ ] Create `src/Services/MangaScrapper/MangaScrapper.Application/MangaScrapper.Application.csproj`
- [ ] Add references: `MangaScrapper.Domain`, `NovaStack.SharedKernel`, `NovaStack.Contracts`
- [ ] Add NuGet: `MediatR`, `FluentValidation`, `Mapster`
- [ ] Add project to `MangaScrapperStack.sln`

### 3.2 Common Abstractions
- [ ] Create `Common/Abstractions/ICommand.cs` — `IRequest<Result>` / `IRequest<Result<T>>`
- [ ] Create `Common/Abstractions/IQuery.cs` — `IRequest<Result<T>>`
- [ ] Create `Common/Abstractions/ICommandHandler.cs`
- [ ] Create `Common/Abstractions/IQueryHandler.cs`
- [ ] Create `Common/Abstractions/IEndpointDefinition.cs`
- [ ] Create `Common/Behaviors/ValidationBehavior.cs` — MediatR pipeline behavior for FluentValidation
- [ ] Create `Common/Behaviors/LoggingBehavior.cs` — MediatR pipeline behavior for structured logging
- [ ] Create `Common/Extensions/ApplicationExtensions.cs` — `AddMangaScrapperApplication()` DI method

### 3.3 Feature: Manga
- [ ] `Features/Manga/GetPagedManga/` — Query + Handler (Mongo paged query) + Validator + Endpoint (`GET /api/v1/manga`)
- [ ] `Features/Manga/GetMangaById/` — Query + Handler + Endpoint (`GET /api/v1/manga/{id}`)
- [ ] `Features/Manga/GetAllChapters/` — Query + Handler + Endpoint
- [ ] `Features/Manga/GetChaptersPage/` — Query + Handler + Endpoint
- [ ] `Features/Manga/GetAllGenre/` — Query + Handler + Endpoint
- [ ] `Features/Manga/GetAllType/` — Query + Handler + Endpoint
- [ ] `Features/Manga/GetTrending/` — Query + Handler + Endpoint
- [ ] `Features/Manga/GetRecommendations/` — Query + Handler (Qdrant delegate) + Endpoint
- [ ] `Features/Manga/UpdateManga/` — Command + Handler + Validator + Endpoint (`PUT /api/v1/manga/{id}`)
- [ ] `Features/Manga/DeleteManga/` — Command + Handler + Endpoint (`DELETE /api/v1/manga/{id}`)
- [ ] `Features/Manga/DeleteChapter/` — Command + Handler + Endpoint
- [ ] `Features/Manga/SyncMeili/` — Command + Handler + Endpoint
- [ ] `Features/Manga/SyncQdrant/` — Command + Handler + Endpoint

### 3.4 Feature: Scrapper (generic)
- [ ] `Features/Scrapper/ScrapChapterPages/` — Command + Handler (queues Hangfire job) + Validator + Endpoint
- [ ] `Features/Scrapper/SearchJikan/` — Query + Handler (Jikan HTTP call) + Endpoint
- [ ] `Features/Scrapper/UpdateMangaMetaData/` — Command + Handler + Validator + Endpoint
- [ ] `Features/Scrapper/GetAllProvider/` — Query + Handler + Endpoint
- [ ] `Features/Scrapper/GetQueue/` — Query + Handler + Endpoint
- [ ] `Features/Scrapper/ClearQueueErrors/` — Command + Handler + Endpoint
- [ ] `Features/Scrapper/FixFile/` — Command + Handler + Endpoint
- [ ] `Features/Scrapper/FixLanguage/` — Command + Handler + Endpoint

### 3.5 Feature: ScrapperKomiku
- [ ] `Features/ScrapperKomiku/GetDetail/` — Query + Handler + Endpoint
- [ ] `Features/ScrapperKomiku/ScrapManga/` — Command + Handler + Endpoint
- [ ] `Features/ScrapperKomiku/Search/` — Query + Handler + Endpoint

### 3.6 Feature: ScrapperKiryuu
- [ ] `Features/ScrapperKiryuu/GetDetail/` — Query + Handler + Endpoint
- [ ] `Features/ScrapperKiryuu/ScrapManga/` — Command + Handler + Endpoint
- [ ] `Features/ScrapperKiryuu/Search/` — Query + Handler + Endpoint

### 3.7 Feature: ScrapperKomikcast
- [ ] `Features/ScrapperKomikcast/GetDetail/` — Query + Handler + Endpoint
- [ ] `Features/ScrapperKomikcast/ScrapManga/` — Command + Handler + Endpoint
- [ ] `Features/ScrapperKomikcast/Search/` — Query + Handler + Endpoint

### 3.8 Feature: ScrapperMangadex
- [ ] `Features/ScrapperMangadex/GetDetail/` — Query + Handler + Endpoint
- [ ] `Features/ScrapperMangadex/ScrapManga/` — Command + Handler + Endpoint
- [ ] `Features/ScrapperMangadex/Search/` — Query + Handler + Endpoint

### 3.9 Feature: Auth
- [ ] `Features/Auth/Login/` — Command + Handler + Validator + Endpoint (`POST /api/auth/login`)
- [ ] `Features/Auth/Logout/` — Command + Handler + Endpoint (`POST /api/auth/logout`)
- [ ] `Features/Auth/Register/` — Command + Handler + Validator + Endpoint (`POST /api/auth/register`)
- [ ] `Features/Auth/UserInfo/` — Query + Handler + Endpoint (`GET /api/auth/me`)
- [ ] `Features/Auth/FirebaseVerify/` — Command + Handler + Endpoint (`POST /api/auth/firebase-verify`)

### 3.10 Feature: Dashboard
- [ ] `Features/Dashboard/GetStatistics/` — Query + Handler + Endpoint (`GET /api/v1/dashboard/stats`)
- [ ] `Features/Dashboard/SyncStorage/` — Command + Handler + Endpoint (`POST /api/v1/dashboard/sync-storage`)

### 3.11 Feature: RecurringJobs
- [ ] `Features/RecurringJobs/CreateOrUpdateRecurringJob/` — Command + Handler + Endpoint
- [ ] `Features/RecurringJobs/DeleteRecurringJob/` — Command + Handler + Endpoint
- [ ] `Features/RecurringJobs/GetRecurringJobs/` — Query + Handler + Endpoint
- [ ] `Features/RecurringJobs/TriggerRecurringJob/` — Command + Handler + Endpoint

### 3.12 Feature: UserLibrary
- [ ] `Features/UserLibrary/AddOrUpdateUserLibrary/` — Command + Handler + Validator + Endpoint
- [ ] `Features/UserLibrary/GetUserLibrary/` — Query + Handler + Endpoint
- [ ] `Features/UserLibrary/RemoveUserLibrary/` — Command + Handler + Endpoint

### 3.13 Feature: UserProgression
- [ ] `Features/UserProgression/UpdateUserProgression/` — Command + Handler + Validator + Endpoint
- [ ] `Features/UserProgression/GetUserProgression/` — Query + Handler + Endpoint
- [ ] `Features/UserProgression/GetMangaProgression/` — Query + Handler + Endpoint

### 3.14 Feature: Images
- [ ] `Features/Images/ProxyImage/` — Query + Handler + Endpoint (`GET /api/v1/images/proxy`)

---

## Phase 4 — MangaScrapper.Infrastructure (NEW PROJECT)

### 4.1 Project Setup
- [ ] Create `src/Services/MangaScrapper/MangaScrapper.Infrastructure/MangaScrapper.Infrastructure.csproj`
- [ ] Add references: `MangaScrapper.Domain`, `NovaStack.Infrastructure`, `NovaStack.SharedKernel`
- [ ] Add NuGet: `MongoDB.Driver`, `Hangfire.AspNetCore`, `Hangfire.Mongo`, `MeiliSearch`, `Qdrant.Client`, `HtmlAgilityPack`, `Microsoft.Playwright`, `SkiaSharp`, `FirebaseAdmin`, `Google.Apis.Auth`, `Isopoh.Cryptography.Argon2`, `OpenTelemetry.*`
- [ ] Add project to `MangaScrapperStack.sln`

### 4.2 Persistence — MongoDB Context
- [ ] Create `Persistence/MangaMongoDbContext.cs` — extends `MongoDbContextBase`:
  - Exposes: `Mangas`, `UserLibraries`, `UserProgressions`, `Users` collections
- [ ] Create `Persistence/Documents/MangaDocument.cs` (migrate from `Infrastructure/Mongo/Collections/`)
- [ ] Create `Persistence/Documents/UserDocument.cs`
- [ ] Create `Persistence/Documents/UserLibraryDocument.cs`
- [ ] Create `Persistence/Documents/UserProgressionDocument.cs`

### 4.3 Repositories
- [ ] Create `Repositories/MongoMangaRepository.cs` — implements `IMangaRepository`:
  - `GetByIdAsync`, `GetByTitleAsync`, `GetPagedAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`
  - Uses `Manga.Reconstitute(...)` for mapping
- [ ] Create `Repositories/MongoUserLibraryRepository.cs` — implements `IUserLibraryRepository`
- [ ] Create `Repositories/MongoUserProgressionRepository.cs` — implements `IUserProgressionRepository`

### 4.4 External Services
- [ ] Migrate `Infrastructure/Services/MeilisearchService.cs` → `Services/MeilisearchService.cs`
- [ ] Migrate `Infrastructure/Services/QdrantService.cs` → `Services/QdrantService.cs`
- [ ] Migrate `Infrastructure/Services/StorageSyncService.cs` → `Services/StorageSyncService.cs`
- [ ] Migrate `Infrastructure/Services/FlareSolverrService.cs` → `Services/FlareSolverrService.cs`
- [ ] Migrate `Infrastructure/Services/DiscordWebhookService.cs` → `Services/DiscordWebhookService.cs`

### 4.5 Scraper HTTP Services
- [ ] Migrate `Features/ScrapperKomiku/Services/KomikuService.cs` → `Scrapers/KomikuService.cs`
- [ ] Migrate `Features/ScrapperKiryuu/Services/KiryuuService.cs` → `Scrapers/KiryuuService.cs`
- [ ] Migrate `Features/ScrapperKomikcast/Services/KomikcastService.cs` → `Scrapers/KomikcastService.cs`
- [ ] Migrate `Features/ScrapperMangadex/Services/MangaDexService.cs` → `Scrapers/MangaDexService.cs`
- [ ] Migrate `Infrastructure/Services/ScrapperService.cs` → `Scrapers/ScrapperService.cs`

### 4.6 Background Jobs (Hangfire)
- [ ] Migrate `Infrastructure/BackgroundJobs/ChapterScrapingJob.cs` → `BackgroundJobs/ChapterScrapingJob.cs`
- [ ] Migrate `Infrastructure/BackgroundJobs/MeiliSyncJob.cs` → `BackgroundJobs/MeiliSyncJob.cs`
- [ ] Migrate `Infrastructure/BackgroundJobs/DeleteMangaJob.cs` → `BackgroundJobs/DeleteMangaJob.cs`
- [ ] Migrate `Infrastructure/BackgroundJobs/LatestScrappingJob.cs` → `BackgroundJobs/LatestScrappingJob.cs`

### 4.7 Security
- [ ] Migrate `Infrastructure/Security/CustomAuthSchemeOptions.cs` → `Security/CustomAuthSchemeOptions.cs`
- [ ] Migrate `Infrastructure/Security/CustomAuthValidation.cs` → `Security/CustomAuthValidation.cs`
- [ ] Migrate `Infrastructure/Security/HangfireAuthFillter.cs` → `Security/HangfireAuthFilter.cs` (fix typo)

### 4.8 Configuration Models
- [ ] Migrate all `Infrastructure/Models/*.cs` settings POCOs → `Configuration/` folder:
  - `MongoSettings`, `ScrapperSettings`, `FlareSolverrSettings`, `MeiliConfig`, `QdrantConfig`, `EmbeddingConfig`, `DiscordWebhookSettings`, `DomainSettings`

### 4.9 DI Extension
- [ ] Create `DependencyInjection/InfrastructureExtensions.cs` — `AddMangaScrapperInfrastructure(IServiceCollection, IConfiguration)`:
  - MongoDB client + context registration
  - Repository registrations
  - Hangfire MongoDB storage setup
  - HTTP client registrations (Scraper, Komiku, Kiryuu, Komikcast, MangaDex, FlareSolverr, ImageProxy, Discord)
  - External service registrations (Meili, Qdrant, StorageSync)
  - SemaphoreSlim (MaxParallelDownloads) singleton

---

## Phase 5 — MangaScrapper.Api (NEW PROJECT)

### 5.1 Project Setup
- [ ] Create `src/Services/MangaScrapper/MangaScrapper.Api/MangaScrapper.Api.csproj`
- [ ] Add references: `MangaScrapper.Application`, `MangaScrapper.Infrastructure`, `NovaStack.Infrastructure`, `MangaPanel.Client` (Blazor WASM)
- [ ] Add project to `MangaScrapperStack.sln`

### 5.2 Program.cs — Composition Root (Thin)
- [ ] Wire `builder.Services.AddMangaScrapperApplication()`
- [ ] Wire `builder.Services.AddMangaScrapperInfrastructure(builder.Configuration)`
- [ ] Wire `builder.Services.AddMangaScrapperAuth(builder.Configuration)`
- [ ] Wire `builder.Services.AddMangaScrapperOtel(builder.Configuration)`
- [ ] Wire Razor Components + Blazor WASM (`AddRazorComponents` + `AddInteractiveWebAssemblyComponents`)
- [ ] Wire `IEndpointDefinition` scanner (`MapEndpointDefinitions()`)
- [ ] Map Hangfire dashboard (`/hangfire`)
- [ ] Map static file serving (image storage path from config)
- [ ] Map Swagger/OpenAPI
- [ ] Add MongoDB index bootstrap on startup (unique Title, composite UserLib, UserProgression indexes)
- [ ] Add CORS middleware from config
- [ ] Add Data Protection key persistence

### 5.3 Configuration & appsettings
- [ ] Create `appsettings.json` with all sections: MongoDB, Scrapper, Meili, Qdrant, Embedding, FlareSolverr, Discord, Domain, Firebase, Cors, Jwt, Authentication, OTEL
- [ ] Create `appsettings.Development.json`
- [ ] Copy `provider/` JSON scraping configs into project

### 5.4 Dockerfile
- [ ] Update `Dockerfile` for multi-stage build (build from solution root, publish Api project)

---

## Phase 6 — Scrapper.Worker (NEW PROJECT)

### 6.1 Project Setup
- [ ] Create `src/Workers/Scrapper.Worker/Scrapper.Worker.csproj`
- [ ] Add references: `MangaScrapper.Infrastructure`
- [ ] Add project to `MangaScrapperStack.sln`

### 6.2 Worker Host
- [ ] Create `Program.cs` — hosted service with Hangfire server
- [ ] Register all Hangfire background job classes as transient (`ChapterScrapingJob`, `MeiliSyncJob`, `DeleteMangaJob`, `LatestScrappingJob`)
- [ ] Configure Hangfire MongoDB storage (same DB as Api)

---

## Phase 7 — Tests

### 7.1 UnitTests
- [ ] Add project reference to `MangaScrapper.Application` and `MangaScrapper.Domain`
- [ ] Add NuGet: `xUnit`, `Moq`, `FluentAssertions`
- [ ] Write `Manga.Create_ValidArgs_RaisesCreatedEvent` test
- [ ] Write `Manga.Reconstitute_DoesNotRaiseEvents` test
- [ ] Write `GetPagedMangaQueryHandler_Returns_PagedList` test (mocked `IMangaRepository`)
- [ ] Write `UpdateMangaCommandHandler_NotFound_Returns404` test
- [ ] Write `LoginCommandHandler_InvalidPassword_ReturnsUnauthorized` test
- [ ] Write validator tests for `LoginCommandValidator`, `UpdateMangaCommandValidator`

### 7.2 IntegrationTests
- [ ] Add NuGet: `Testcontainers.MongoDb`, `Microsoft.AspNetCore.Mvc.Testing`
- [ ] Create `MangaScrapperWebApplicationFactory` using Testcontainers MongoDB
- [ ] Write `GET /api/v1/manga` returns 200 with empty list
- [ ] Write `POST /api/auth/login` with valid credentials returns auth cookie
- [ ] Write `POST /api/auth/login` with invalid credentials returns 401
- [ ] Write UserLibrary CRUD flow integration test

### 7.3 ArchitectureTests
- [ ] Add NuGet: `NetArchTest.Rules`
- [ ] Write: `Domain_Should_Not_Reference_Application`
- [ ] Write: `Domain_Should_Not_Reference_Infrastructure`
- [ ] Write: `Application_Should_Not_Reference_Infrastructure`
- [ ] Write: `Handlers_Should_Be_Internal_And_Sealed`
- [ ] Write: `Endpoints_Should_Implement_IEndpointDefinition`
- [ ] Write: `CommandHandlers_Should_NotThrow_DomainExceptions`

---

## Phase 8 — Solution Cleanup & Documentation

- [ ] Remove old `MangaScrapper` monolith project from solution (or archive)
- [ ] Remove `MangaPanel` project from old solution (now referenced from `MangaScrapper.Api`)
- [ ] Remove `MangaScrapper.Shared` project (contracts moved to `NovaStack.Contracts`)
- [ ] Update root `README.md` with new architecture diagram and setup instructions
- [ ] Update `docker-compose.yml` to target new `MangaScrapper.Api` Dockerfile path
- [ ] Run `dotnet build MangaScrapperStack.sln` → 0 errors
- [ ] Run `dotnet test` for all test projects → all pass
- [ ] Verify Swagger UI, Hangfire dashboard, Blazor panel all functional
