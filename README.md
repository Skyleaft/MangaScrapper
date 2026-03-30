# MangaScrapper

MangaScrapper is a comprehensive full-stack solution for scraping, managing, and reading manga. It features a robust ASP.NET Core backend that scrapes metadata and individual chapter pages, storing data in MongoDB and optimizing images as local WebP files. The project includes a modern, high-performance Blazor WebAssembly admin panel for seamless management.

For the mobile-first reading experience, check out the [Open Manga Reader](https://github.com/Skyleaft/Open-Manga-Reader) client.

## 🚀 Key Features

- **Multi-Source Scraping**: Provider-based design supporting multiple sources (Komiku, Kiryuu, etc.).
- **Smart Search**: Typo-tolerant, lightning-fast full-text search powered by **Meilisearch**.
- **AI Recommendations**: Personalized manga recommendations based on reading history using **Qdrant** vector embeddings.
- **Smart Background Processing**: Integration with **Hangfire** for reliable, queued background scraping jobs.
- **Admin Dashboard**: Real-time statistics, monthly scrap charts, and recent activity monitoring.
- **Advanced Management**:
  - Dynamic manga list with pagination, multi-genre filtering, and advanced sorting.
  - Interactive Manga Detail Modal for editing metadata and managing chapters.
  - Manual `TotalView` overrides and chapter availability indicators.
- **Optimized Storage**: Automatic image conversion to WebP and centralized local storage.
- **Secure Access**: Cookie-based authentication with a professional login interface.

## 🛠️ Technical Stack

### Backend (MangaScrapper API)

- **.NET 10** Web API
- **FastEndpoints** (minimal API alternative with REPR pattern)
- **Hangfire** with MongoDB storage for job orchestration
- **MongoDB.Driver** for high-performance NoSQL operations
- **Meilisearch** for lightning-fast, typo-tolerant full-text search
- **Qdrant** for high-performance vector search and similarity-based recommendations
- **BGE-Base Embedding Service** for semantic metadata vectorization
- **ImageSharp** for WebP conversion and image processing
- **HtmlAgilityPack** for robust DOM parsing
- **OpenTelemetry** for advanced tracing and monitoring

### Frontend (MangaPanel)

- **Blazor WebAssembly** (.NET 10)
- **Tailwind CSS** for modern, responsive, and premium UI
- **Lucide Icons** & Custom SVG iconography
- **Glassmorphism Design** for a state-of-the-art admin experience

## 📁 Project Structure

- `MangaScrapper/`: The core API project.
  - `Features/`: Organized by feature sets (Manga, Scrapper, Auth, Dashboard).
  - `Infrastructure/`: Repositories, Mongo context, background job implementations.
  - `provider/`: JSON configurations for scraping selectors.
- `MangaPanel/`: The Blazor WASM client.
  - `Pages/`: Admin dashboard, Manage Manga, and Login pages.
  - `Components/`: Reusable UI elements like `MangaCard`, `MangaDetailModal`, and `StatsCard`.
  - `Layout/`: Professional sidebar-based admin layout with sticky headers.
- `MangaScrapper.Shared/`: Shared DTOs and models between API and Client.

## ⚙️ Configuration

Main configuration is handled via `appsettings.json` in the API project:

```json
{
  "MongoSettings": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "manga-scrap"
  },
  "ScrapperSettings": {
    "MaxParallelDownloads": 10,
    "ImageStoragePath": "images"
  },
  "JwtSigningKey": "your_secure_signing_key_here"
}
```

## 🏁 Getting Started

### Prerequisites

- .NET 10 SDK
- MongoDB Server

### Local Development

1. **Clone the repository**
2. **Setup Database**: Ensure MongoDB is running.
3. **Run the API**:

   ```powershell
   dotnet run --project .\MangaScrapper\MangaScrapper.csproj
   ```

4. **Run the Panel**:

   ```powershell
   dotnet run --project .\MangaPanel\MangaPanel.Client\MangaPanel.Client.csproj
   ```

### 🐳 Docker Compose

The easiest way to run the entire stack (API, MongoDB, Meilisearch, Qdrant, and Embedding Service) is using Docker Compose:

1. **Clone the repository**
2. **Setup environment**: Ensure you have a `firebase.json` in `MangaScrapper/secrets/` if needed by the configuration.
3. **Bring up the services**:

   ```bash
   docker-compose up -d --build
   ```

4. **Access the services**:
   - API: `http://localhost:5000`
   - Meilisearch: `http://localhost:7700`
   - Qdrant: `http://localhost:6333`
   - Embedding Service: `http://localhost:8222`

### Admin Access

- **Main URL**: `http://localhost:<port>/`
- **Hangfire Dashboard**: `http://localhost:<port>/hangfire` (Requires login)
- **API Documentation**: `/swagger` or `/openapi/v1.json`

## 📊 Management Workflow

1. **Dashboard**: Monitor monthly growth and recent additions.
2. **Manage Manga**: Search, filter, and find manga to update.
3. **Manga Detail**:
   - Edit release date, genres, and status.
   - Click **"Scrap Missing"** to automatically queue Hangfire jobs for missing chapter pages.
   - Delete specific chapters or the entire manga including local files.

---
*© 2026 MangaScrapper Engine v2.0*
