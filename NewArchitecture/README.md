# MangaScrapperStack Documentation

This document provides an overview of the `MangaScrapperStack` solution. This solution is built on **.NET 10.0** and primarily uses a **Vertical Slice Architecture**, organizing features around business capabilities while utilizing shared building blocks for cross-cutting concerns.

## Tech Stack

The solution leverages a modern and robust technology stack:

- **Framework**: .NET 10.0
- **API & Web**: ASP.NET Core Minimal APIs (using `IEndpointDefinition` pattern), Blazor WebAssembly, and Scalar for OpenAPI reference (`Scalar.AspNetCore`).
- **Database & Search**:
  - **MongoDB**: Primary document database (`MongoDB.Driver`).
  - **MeiliSearch**: Fast, open-source search engine.
  - **Qdrant**: Vector database for AI embeddings and similarity search (`Qdrant.Client`).
  - **Redis / SQL**: Caching and relational mapping (`StackExchange.Redis`, `Dapper`, `EF Core`).
- **Background Jobs**: Hangfire backed by MongoDB (`Hangfire.AspNetCore`, `Hangfire.Mongo`) for asynchronous scraping tasks.
- **Scraping Engine**:
  - **HtmlAgilityPack**: For parsing and extracting DOM elements.
  - **Microsoft.Playwright**: For dynamic, JavaScript-heavy sites.
  - **FlareSolverr**: For bypassing Cloudflare protections.
- **Messaging & Mediator**: `MediatR` for in-process CQRS, `RabbitMQ` and `Confluent.Kafka` for distributed event streaming.
- **Observability**: OpenTelemetry (Metrics, Tracing), Prometheus, and Serilog (Structured Logging).
- **Security**: Firebase Admin SDK, Argon2 Hashing (`Isopoh.Cryptography.Argon2`), JWT Authentication.

---

## Project Structure & Architecture

The solution adopts a **Vertical Slice Architecture** approach nested within modular boundaries, minimizing tight coupling between distinct features while centralizing infrastructural logic. 

### 1. Services (`Services/MangaScrapper`)
This is the core execution boundary for the scraping service.
- **`MangaScrapper.Api`**: The entry point. Bootstraps the application (`Program.cs`), sets up dependency injection, registers Hangfire, sets up static file serving (for images), and maps endpoints via `IEndpointDefinition`.
- **`MangaScrapper.Application`**: Contains the business logic, CQRS handlers (via MediatR), and validation pipelines. Extended by `ApplicationExtensions.cs` to inject application-specific services.
- **`MangaScrapper.Infrastructure`**: Contains database context setups, Hangfire configurations, and integrations with external services (MeiliSearch, Qdrant, MongoDB). Extended by `InfrastructureExtensions.cs` for DI registration.
- **`MangaScrapper.Domain`**: Core domain entities and rules.
- **`MangaPanel.Client`**: Blazor WebAssembly components for the user interface.

### 2. Workers (`Workers`)
- **`Scrapper.Worker`**: A background service dedicated to running the scraping jobs independently from the API, ensuring heavy workloads do not impact user requests.

### 3. Building Blocks (`BuildingBlocks`)
Shared libraries providing standard implementations for all services:
- **`NovaStack.SharedKernel`**: Core domain primitives and base types.
- **`NovaStack.Infrastructure`**: Shared configurations for Serilog, OpenTelemetry, RabbitMQ/Kafka, and Caching.
- **`NovaStack.Contracts`**: Shared integration events and message definitions.

---

## Configuration

The application is highly configurable, relying on `appsettings.json` for infrastructure and JSON files for dynamic scraping rules.

### Core Configuration (`appsettings.json`)
Located in `MangaScrapper.Api/appsettings.json`, it controls:
- **MongoDB**: Connection string and database name.
- **Scrapper Settings**: Maximum parallel downloads and local image storage paths.
- **FlareSolverr**: Host configuration for bypassing anti-bot protections.
- **Meili & Qdrant**: Connection settings and API keys for the search engines.
- **Messaging**: RabbitMQ host, port, and credentials.
- **Discord**: Webhook integration for notifications.

### Provider Configurations (`provider/*.json`)
Scraping logic is abstracted into JSON provider files located in `MangaScrapper.Api/provider/`. Examples include:
- `kiryuu-provider.json`
- `komiku-provider.json`
- `komikcast-provider.json`
- `mangadex-provider.json`

These files define the exact CSS/XPath selectors needed to parse a specific source. A provider file includes:
- **Provider Info**: `ProviderName`, `BaseUrl`, `ProviderIcon`.
- **MangaSelectors**: Selectors for extracting `Title`, `Author`, `Description`, `Genres`, and `Thumbnail`.
- **ChapterSelectors**: Rules for finding the chapter list, links, and upload dates.
- **PageSelectors**: Selectors for extracting the actual image URLs (`Images`) from a chapter page.

---

## Getting Started

1. Ensure external dependencies (MongoDB, RabbitMQ, MeiliSearch, Qdrant, FlareSolverr) are running, ideally via Docker Compose.
2. Review and adjust `appsettings.Development.json` inside the `MangaScrapper.Api` and `Scrapper.Worker` projects.
3. Start the `MangaScrapper.Api` to serve the API and Blazor client.
4. Access the API documentation at `/scalar/v1` and the Hangfire dashboard at `/hangfire` (requires authentication).
