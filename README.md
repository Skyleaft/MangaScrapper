# MangaScrapper

MangaScrapper is an ASP.NET Core API that scrapes manga metadata and chapter pages, stores data in MongoDB, and saves images as local WebP files.

It uses a provider-based design, so each source can be implemented as:

`ProviderNameService : ScrapperServiceBase`

Current providers:
- `KomikuService`
- `KiryuuService`

## Tech Stack

- .NET 10 Web API
- FastEndpoints + Swagger
- MongoDB.Driver
- HtmlAgilityPack
- ImageSharp (WebP conversion)
- Background queue worker for chapter page scraping

## Project Structure

- `MangaScrapper/Program.cs`: app bootstrap, DI, middleware, static files
- `MangaScrapper/Infrastructure/Services/ScrapperServiceBase.cs`: shared scraper functionality
- `MangaScrapper/Features/ScrapperKomiku/Services/KomikuService.cs`: Komiku provider implementation
- `MangaScrapper/Features/ScrapperKiryuu/Services/KiryuuService.cs`: Kiryuu provider implementation
- `MangaScrapper/provider/*.json`: provider configs (base URL + selectors)
- `MangaScrapper/Infrastructure/BackgroundJobs/*`: background queue and worker
- `MangaScrapper/Infrastructure/Repositories/*`: Mongo repository layer

## Requirements

- .NET SDK 10
- MongoDB

## Configuration

App settings are in:
- `MangaScrapper/appsettings.json`
- `MangaScrapper/appsettings.Development.json`

Main sections:

```json
{
  "MongoSettings": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "manga-scrap"
  },
  "ScrapperSettings": {
    "MaxParallelDownloads": 10,
    "ImageStoragePath": "images"
  }
}
```

Notes:
- `ImageStoragePath` can be relative (`images`) or absolute (`Z:\\Manga`).
- Images are served from `/images/*` by static file middleware.

## Run Locally

From repository root:

```powershell
dotnet restore .\MangaScrapper.sln
dotnet run --project .\MangaScrapper\MangaScrapper.csproj
```

Typical local URLs:
- Swagger UI: `https://localhost:<port>/swagger`
- OpenAPI JSON: `https://localhost:<port>/openapi/v1.json`

## Run With Docker

```powershell
docker compose up --build
```

`docker-compose.yml` starts:
- API on `http://localhost:5000`
- MongoDB on `localhost:27017`

## API Endpoints

### Manga

- `GET /api/manga/paged`
  - Query: `search`, `genres`, `status`, `type`, `page`, `pageSize`, `sortBy`, `orderBy`
- `GET /api/manga/{MangaId}`
- `GET /api/manga/{MangaId}/chapters`
- `GET /api/manga/{MangaId}/chapter/{Chapter}`
- `GET /api/manga/genres`
- `GET /api/manga/types`
- `DELETE /api/manga/{MangaId}`

### Images

- `GET /api/images/{*FilePath}`

### Scrapper Utility

- `GET /api/scrapper/queue`
- `GET /api/scrapper/fixfile`

### Komiku Provider

- `POST /api/scrapper/komiku/manga`
  - Body:
  ```json
  {
    "mangaUrl": "https://komiku.org/manga/<slug>",
    "scrapChapters": true
  }
  ```
- `GET /api/scrapper/komiku/manga/{MangaId}/chapter-pages`
- `GET /api/scrapper/komiku/manga/search?query=<keyword>`

### Kiryuu Provider

- `GET /api/scrapper/kiryuu/manga/{MangaId}`
- `POST /api/scrapper/kiryuu/manga/pages`
  - Body:
  ```json
  {
    "chapterUrl": "https://kiryuu03.com/<chapter-path>"
  }
  ```

## How Background Scraping Works

When scraping manga with `scrapChapters: true`, chapter page jobs are enqueued.

- Queue is bounded (`BackgroundTaskQueue`, capacity 100).
- `BackgroundWorker` consumes jobs and updates queue status.
- Job progress can be checked at `GET /api/scrapper/queue`.

## Adding a New Provider

1. Create a provider config JSON in `MangaScrapper/provider/` (base URL + selectors).
2. Add service class:

```csharp
public class NewProviderService : ScrapperServiceBase
{
    public NewProviderService(
        HttpClient httpClient,
        IMangaRepository mangaRepository,
        IBackgroundTaskQueue taskQueue,
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapperSettings> settings,
        SemaphoreSlim semaphore)
        : base(httpClient, mangaRepository, taskQueue, scopeFactory, settings, semaphore)
    {
        LoadProvider("new-provider.json");
    }
}
```

3. Implement provider-specific methods (metadata, chapter pages, search, etc.).
4. Register in DI:

```csharp
builder.Services.AddHttpClient<NewProviderService>();
```

5. Add provider endpoints under `Features/ScrapperNewProvider`.

## Notes

- Manga title is indexed as unique in MongoDB.
- Images are converted and saved as `.webp`.
- If file paths have legacy formats (leading slash / wrong file name), use `GET /api/scrapper/fixfile`.
