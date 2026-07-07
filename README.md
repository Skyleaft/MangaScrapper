# MangaScrapper

MangaScrapper is a comprehensive full-stack solution for scraping, managing, and reading manga. It features a robust ASP.NET Core backend that scrapes metadata and individual chapter pages across multiple sources, storing data in MongoDB and optimizing images locally. The project includes a modern, high-performance Blazor WebAssembly admin panel for seamless management, along with user library and reading progression tracking.

For the mobile-first reading experience, check out the [Open Manga Reader](https://github.com/Skyleaft/Open-Manga-Reader) client.

> **⚠️ License — Educational Purpose Only**
>
> This project is provided **strictly for educational and personal research purposes**. It is not intended for commercial use, redistribution, or deployment in any production environment that violates the terms of service of the scraped websites. The author assumes no responsibility for misuse.

## 🚀 Key Features

- **Multi-Source Scraping**: Provider-based design supporting multiple manga sources:
  - **Komiku** — Indonesian comics aggregator
  - **Kiryuu** — Popular Indonesian manga site
  - **Komikcast** — Indonesian manga platform
  - **MangaDex** — International manga database
- **Cloudflare Bypass**: Integrated **FlareSolverr** support for scraping Cloudflare-protected sites.
- **Playwright Automation**: Browser-based scraping with **Microsoft Playwright** for JavaScript-rendered pages.
- **Image Proxy**: Server-side image proxy that spoofs browser User-Agent headers to bypass hotlink protection on external providers.
- **Smart Search**: Typo-tolerant, lightning-fast full-text search powered by **Meilisearch**.
- **AI Recommendations**: Personalized manga recommendations based on reading history using **Qdrant** vector embeddings and a **BGE-Base Embedding Service**.
- **Smart Background Processing**: Integration with **Hangfire** for reliable, queued background scraping jobs and recurring sync tasks.
- **Discord Notifications**: Webhook-based Discord notifications for scraping events and job completions.
- **User Library & Progression**: Track per-user manga libraries and reading progression (chapter-level tracking) backed by Firebase authentication.
- **Admin Dashboard**: Real-time statistics, monthly scrap charts, and recent activity monitoring.
- **Advanced Management**:
  - Dynamic manga list with pagination, multi-genre filtering, and advanced sorting.
  - Interactive Manga Detail Modal for editing metadata and managing chapters.
  - Manual `TotalView` overrides and chapter availability indicators.
- **Optimized Storage**: Automatic image conversion using **SkiaSharp** and centralized local storage with optional sync service.
- **Secure Access**: Cookie-based authentication with Argon2 password hashing, Data Protection key persistence, and a professional login interface.
- **Observability**: Full **OpenTelemetry** integration with Prometheus metrics scraping (`/metrics`), OTLP trace/metric/log export, and runtime instrumentation.

## 🛠️ Technical Stack

### Backend (MangaScrapper API)

- **.NET 10** Web API + Blazor Server (WASM host)
- **FastEndpoints** (minimal API alternative with REPR pattern)
- **Hangfire** with MongoDB storage for job orchestration and recurring jobs
- **MongoDB.Driver** v3 for high-performance NoSQL operations
- **Meilisearch** for lightning-fast, typo-tolerant full-text search
- **Qdrant** for high-performance vector search and similarity-based recommendations
- **BGE-Base Embedding Service** for semantic metadata vectorization
- **SkiaSharp** for WebP conversion and image processing (Linux-compatible)
- **HtmlAgilityPack** for robust DOM parsing
- **Microsoft Playwright** for JavaScript-rendered page scraping
- **FlareSolverr** integration for Cloudflare-protected site bypass
- **FirebaseAdmin** + **Google.Apis.Auth** for Firebase-based user authentication
- **Isopoh.Cryptography.Argon2** for secure password hashing
- **OpenTelemetry** for advanced tracing, metrics (Prometheus), and structured logging
- **Discord Webhook** integration for event notifications
- **ASP.NET Core Data Protection** for persistent cookie encryption keys

### Frontend (MangaPanel — Blazor WASM)

- **Blazor WebAssembly** (.NET 10)
- **Blazored.LocalStorage** for client-side state persistence
- **Cookie Handler** for transparent cookie forwarding from WASM to the API
- **JWT Authentication State Provider** for Blazor auth integration
- **Tailwind CSS** for modern, responsive, and premium UI
- **Lucide Icons** & Custom SVG iconography
- **Glassmorphism Design** for a state-of-the-art admin experience

## 📁 Project Structure

- `MangaScrapper/`: The core API + WASM host project.
  - `Features/`: Organized by feature sets:
    - `Manga/` — Manga CRUD and metadata endpoints
    - `Scrapper/` — Generic scraper and file-fix utilities
    - `ScrapperKomiku/`, `ScrapperKiryuu/`, `ScrapperKomikcast/`, `ScrapperMangadex/` — Source-specific scrapers
    - `Auth/` — Login, logout, and Firebase-based auth
    - `Dashboard/` — Stats and activity endpoints
    - `Images/` — Image proxy endpoint
    - `UserLibrary/` — User manga library management
    - `UserProgression/` — Reading progress tracking
    - `RecurringJobs/` — Scheduled Hangfire recurring job definitions
  - `Infrastructure/`: Repositories, Mongo context, background job implementations, security utilities.
  - `provider/`: JSON configurations for scraping selectors.
  - `secrets/`: Firebase Admin SDK credentials (not committed to source control).
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
  "MeiliSettings": {
    "Host": "http://localhost:7700",
    "ApiKey": "your_meilisearch_key"
  },
  "QdrantSettings": {
    "Host": "localhost",
    "Port": 6334
  },
  "EmbeddingSettings": {
    "Host": "http://localhost:8222"
  },
  "FlareSolverrSettings": {
    "Host": "http://localhost:8191"
  },
  "DiscordWebhookSettings": {
    "WebhookUrl": "https://discord.com/api/webhooks/..."
  },
  "DomainSettings": {
    "BaseUrl": "http://localhost:5000"
  },
  "Firebase": {
    "CredentialPath": "secrets/mykomikid-firebase-adminsdk.json"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:3000"],
    "AllowCredentials": true
  },
  "JwtSigningKey": "your_secure_signing_key_here"
}
```

## 🏁 Getting Started

### Prerequisites

- .NET 10 SDK
- MongoDB Server
- (Optional) Meilisearch, Qdrant, FlareSolverr, Embedding Service

### Local Development

1. **Clone the repository**
2. **Setup Database**: Ensure MongoDB is running.
3. **Add Firebase credentials** (if using user features): Place the Firebase Admin SDK JSON in `MangaScrapper/secrets/`.
4. **Run the API**:

   ```powershell
   dotnet run --project .\MangaScrapper\MangaScrapper.csproj
   ```

5. **Run the Panel** (standalone WASM dev):

   ```powershell
   dotnet run --project .\MangaPanel\MangaPanel.Client\MangaPanel.Client.csproj
   ```

> **Note**: The MangaPanel client is also hosted directly by the API server at the root URL, so running only the API is sufficient for most development workflows.

### 🐳 Docker Compose

The easiest way to run the entire stack (API, MongoDB, Meilisearch, Qdrant, FlareSolverr, and Embedding Service) is using Docker Compose:

1. **Clone the repository**
2. **Setup environment**: Place your Firebase Admin SDK JSON in `MangaScrapper/secrets/`.
3. **Bring up the services**:

   ```bash
   docker-compose up -d --build
   ```

4. **Access the services**:
   - API / Admin Panel: `http://localhost:5000`
   - Meilisearch: `http://localhost:7700`
   - Qdrant: `http://localhost:6333`
   - Embedding Service: `http://localhost:8222`
   - FlareSolverr: `http://localhost:8191`
   - Prometheus Metrics: `http://localhost:5000/metrics`

### Admin Access

- **Main URL**: `http://localhost:<port>/`
- **Hangfire Dashboard**: `http://localhost:<port>/hangfire` (Requires login)
- **API Documentation**: `/swagger` or `/openapi/v1.json`
- **Prometheus Metrics**: `/metrics`

## 📊 Management Workflow

1. **Dashboard**: Monitor monthly growth and recent additions.
2. **Manage Manga**: Search, filter, and find manga to update.
3. **Manga Detail**:
   - Edit release date, genres, and status.
   - Click **"Scrap Missing"** to automatically queue Hangfire jobs for missing chapter pages.
   - Delete specific chapters or the entire manga including local files.
4. **User Library**: Users can track their personal manga reading lists via the API.
5. **User Progression**: Reading progress is saved per-user, per-manga (chapter-level).

---

## 📜 License

This project is intended **for educational purposes only**.

- ✅ You may study, fork, and modify this code for learning.
- ❌ You may **not** use it commercially or in violation of any third-party website's Terms of Service.
- ❌ You may **not** redistribute scraped content or deploy it as a public service.

The author provides no warranty and accepts no liability for any misuse of this software.

---
*© 2026 MangaScrapper Engine v2.1*
