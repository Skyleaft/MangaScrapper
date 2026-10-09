using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Configuration;
using MangaScrapper.Core.Persistence;
using MangaScrapper.Core.Persistence.Documents;
using Mapster;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using System.Text.RegularExpressions;
using Match = Qdrant.Client.Grpc.Match;

namespace MangaScrapper.Core.Services;

/// <summary>
/// Result item containing the Manga ID and its similarity/relevance score from Qdrant.
/// </summary>
public record ScoredMangaResult(Guid Id, float Score);

public partial class QdrantService
{
    private const string CollectionName = "mangas";
    public const string DenseVectorName = "dense";
    public const string SparseVectorName = "sparse";

    private readonly QdrantClient _client;
    private readonly MangaMongoDbContext _dbContext;
    private readonly ILogger<QdrantService> _logger;
    private readonly IEmbeddingService _embeddingService;
    private readonly ulong _vectorSize;
    public const ulong DefaultVectorSize = 1024;

    private static readonly char[] TokenSeparators =
    [
        ' ', '.', ',', ':', ';', '!', '?', '-', '_', '/', '(', ')', '[', ']', '"', '\'', '\n', '\r', '\t'
    ];

    public QdrantService(
        IOptions<QdrantConfig> config,
        MangaMongoDbContext dbContext,
        ILogger<QdrantService> logger,
        IEmbeddingService embeddingService,
        IOptions<EmbeddingConfig> embeddingConfig)
    {
        var host = config.Value.Host;
        bool isHttps = false;

        if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            isHttps = true;
            host = host.Substring(8);
        }
        else if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            host = host.Substring(7);
        }

        var port = config.Value.Port;
        if (port == 6333)
        {
            // Auto-correct to gRPC port if REST port is provided
            port = 6334;
        }

        _client = new QdrantClient(host, port: port, https: isHttps, apiKey: config.Value.ApiKey);
        _dbContext = dbContext;
        _logger = logger;
        _embeddingService = embeddingService;
        _vectorSize = embeddingConfig.Value.VectorSize > 0 ? embeddingConfig.Value.VectorSize : DefaultVectorSize;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing Qdrant collection '{CollectionName}' with Hybrid vectors...", CollectionName);

        var collections = await _client.ListCollectionsAsync(cancellationToken: ct);
        if (!collections.Contains(CollectionName))
        {
            await _client.CreateCollectionAsync(
                CollectionName,
                vectorsConfig: new VectorParamsMap
                {
                    Map =
                    {
                        [DenseVectorName] = new VectorParams
                        {
                            Size = _vectorSize,
                            Distance = Distance.Cosine
                        }
                    }
                },
                sparseVectorsConfig: new SparseVectorConfig
                {
                    Map =
                    {
                        [SparseVectorName] = new SparseVectorParams()
                    }
                },
                cancellationToken: ct);
            _logger.LogInformation("Qdrant collection '{CollectionName}' created successfully with hybrid vectors.", CollectionName);
        }
        else
        {
            _logger.LogInformation("Qdrant collection '{CollectionName}' already exists.", CollectionName);
        }

        await EnsurePayloadIndexesAsync(ct);
    }

    private async Task EnsurePayloadIndexesAsync(CancellationToken ct)
    {
        try
        {
            var info = await _client.GetCollectionInfoAsync(CollectionName, cancellationToken: ct);
            var fieldsToIndex = new[] { "status", "type", "genres" };
            foreach (var field in fieldsToIndex)
            {
                if (!info.PayloadSchema.ContainsKey(field))
                {
                    try
                    {
                        await _client.CreatePayloadIndexAsync(
                            CollectionName,
                            field,
                            schemaType: PayloadSchemaType.Keyword,
                            cancellationToken: ct);
                        _logger.LogInformation("Created payload index for '{Field}' in Qdrant.", field);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create payload index for '{Field}'.", field);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check or create payload indexes for '{CollectionName}'.", CollectionName);
        }
    }

    public async Task SyncAllAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting full sync from MongoDB to Qdrant...");

        await InitializeAsync(ct);

        long totalCount = await _dbContext.Mangas.CountDocumentsAsync(_ => true, cancellationToken: ct);
        if (totalCount == 0)
        {
            _logger.LogWarning("No manga documents found in MongoDB. Nothing to sync to Qdrant.");
            return;
        }

        var projection = Builders<MangaDocument>.Projection.Exclude(x => x.Chapters);
        using var cursor = await _dbContext.Mangas
            .Find(_ => true)
            .Project<MangaDocument>(projection)
            .ToCursorAsync(ct);

        int processed = 0;
        const int batchUpsertThreshold = 200;
        var batch = new List<PointStruct>(batchUpsertThreshold);
        int maxConcurrency = Math.Max(2, Math.Min(Environment.ProcessorCount, 8));
        using var semaphore = new SemaphoreSlim(maxConcurrency);

        while (await cursor.MoveNextAsync(ct))
        {
            var docs = cursor.Current.ToList();
            if (docs.Count == 0) continue;

            var tasks = docs.Select(async doc =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var manga = doc.Adapt<Manga>();
                    return await MapToPointStructAsync(manga, ct);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var pointStructs = await Task.WhenAll(tasks);
            batch.AddRange(pointStructs);

            while (batch.Count >= batchUpsertThreshold)
            {
                var upsertChunk = batch.Take(batchUpsertThreshold).ToList();
                batch.RemoveRange(0, batchUpsertThreshold);

                await _client.UpsertAsync(CollectionName, upsertChunk, cancellationToken: ct);
                processed += upsertChunk.Count;
                _logger.LogInformation("Qdrant synced {Processed} of {Total} documents.", processed, totalCount);
            }
        }

        if (batch.Count > 0)
        {
            await _client.UpsertAsync(CollectionName, batch, cancellationToken: ct);
            processed += batch.Count;
            _logger.LogInformation("Qdrant synced {Processed} of {Total} documents.", processed, totalCount);
        }

        _logger.LogInformation("Full sync completed. {Count} manga documents synced to Qdrant.", processed);
    }

    public async Task UpsertMangaAsync(Manga manga, CancellationToken ct = default)
    {
        var point = await MapToPointStructAsync(manga, ct);
        await _client.UpsertAsync(CollectionName, new[] { point }, cancellationToken: ct);
        _logger.LogInformation("Upserted manga '{Title}' (ID: {Id}) to Qdrant.", manga.Title, manga.Id.Value);
    }

    public async Task DeleteMangaAsync(Guid id, CancellationToken ct = default)
    {
        await _client.DeleteAsync(CollectionName, (PointId)id, cancellationToken: ct);
        _logger.LogInformation("Deleted manga (ID: {Id}) from Qdrant.", id);
    }

    /// <summary>
    /// History-based recommendation: uses Qdrant's native recommendation engine with positive example
    /// point IDs and returns nearest neighbors with scores, excluding already-read manga.
    /// </summary>
    public async Task<List<ScoredMangaResult>> RecommendAsync(List<Guid> readingHistoryIds, int limit = 10, CancellationToken ct = default)
    {
        if (readingHistoryIds == null || !readingHistoryIds.Any())
            return new List<ScoredMangaResult>();

        var positives = readingHistoryIds.Select(id => (PointId)id).ToList();

        var filter = new Filter();
        filter.MustNot.Add(new Condition
        {
            HasId = new HasIdCondition { HasId = { positives } }
        });

        var recommend = new RecommendInput();
        recommend.Positive.AddRange(positives.Select(p => (VectorInput)p));

        var searchResult = await _client.QueryAsync(
            CollectionName,
            query: recommend,
            usingVector: DenseVectorName,
            filter: filter,
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult
            .Select(r => new ScoredMangaResult(Guid.Parse(r.Id.Uuid), r.Score))
            .ToList();
    }

    /// <summary>
    /// Hybrid similarity search seeded from a single manga returning IDs and similarity scores (Dense + Sparse vectors with RRF fusion).
    /// </summary>
    public async Task<List<ScoredMangaResult>> SearchSimilarAsync(Guid mangaId, int limit = 10, CancellationToken ct = default)
    {
        var points = await _client.RetrieveAsync(
            CollectionName,
            new List<PointId> { (PointId)mangaId },
            withVectors: true,
            cancellationToken: ct);

        if (points.Count == 0)
        {
            _logger.LogWarning("Manga (ID: {Id}) not found in Qdrant. Cannot compute similar mangas.", mangaId);
            return new List<ScoredMangaResult>();
        }

        var targetPoint = points[0];
        var denseData = ExtractDenseVector(targetPoint);
        var sparseData = ExtractSparseVector(targetPoint);

        if (denseData == null || denseData.Length == 0)
        {
            _logger.LogWarning("No dense vector found for manga (ID: {Id}) in Qdrant.", mangaId);
            return new List<ScoredMangaResult>();
        }

        var filter = new Filter();
        filter.MustNot.Add(new Condition
        {
            HasId = new HasIdCondition { HasId = { (PointId)mangaId } }
        });

        var prefetchList = new List<PrefetchQuery>
        {
            new()
            {
                Query = new Query { Nearest = new VectorInput(denseData) },
                Using = DenseVectorName,
                Limit = (ulong)(limit * 2),
                Filter = filter
            }
        };

        if (sparseData != null && sparseData.Indices.Count > 0)
        {
            var sparseVector = new SparseVector();
            sparseVector.Indices.AddRange(sparseData.Indices);
            sparseVector.Values.AddRange(sparseData.Values);

            prefetchList.Add(new PrefetchQuery
            {
                Query = new Query
                {
                    Nearest = new VectorInput { Sparse = sparseVector }
                },
                Using = SparseVectorName,
                Limit = (ulong)(limit * 2),
                Filter = filter
            });
        }

        var searchResult = await _client.QueryAsync(
            CollectionName,
            prefetch: prefetchList,
            query: new Query { Fusion = Fusion.Rrf },
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult
            .Select(r => new ScoredMangaResult(Guid.Parse(r.Id.Uuid), r.Score))
            .ToList();
    }

    /// <summary>
    /// Multilingual hybrid semantic search using dense vector embeddings + BM25 sparse vectors with RRF fusion.
    /// </summary>
    public async Task<List<ScoredMangaResult>> SemanticSearchAsync(string queryText, int limit = 10, CancellationToken ct = default)
    {
        var embedding = await _embeddingService.GenerateEmbeddingAsync(queryText, mode: "query", ct);
        if (embedding == null || embedding.Length == 0)
        {
            _logger.LogWarning("Failed to get embedding for semantic search query.");
            return new List<ScoredMangaResult>();
        }

        var prefetchList = new List<PrefetchQuery>
        {
            new()
            {
                Query = new Query { Nearest = new VectorInput(embedding) },
                Using = DenseVectorName,
                Limit = (ulong)(limit * 3)
            }
        };

        var querySparse = ComputeSparseVector(queryText);
        if (querySparse.Indices.Count > 0)
        {
            prefetchList.Add(new PrefetchQuery
            {
                Query = new Query { Nearest = new VectorInput { Sparse = querySparse } },
                Using = SparseVectorName,
                Limit = (ulong)(limit * 3)
            });
        }

        var searchResult = await _client.QueryAsync(
            CollectionName,
            prefetch: prefetchList,
            query: new Query { Fusion = Fusion.Rrf },
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult
            .Select(r => new ScoredMangaResult(Guid.Parse(r.Id.Uuid), r.Score))
            .ToList();
    }

    /// <summary>
    /// Filtered hybrid vector similarity search seeded from a single manga returning IDs and similarity scores.
    /// Applies Qdrant payload filters (status, type, genres) with RRF fusion.
    /// </summary>
    public async Task<List<ScoredMangaResult>> SearchSimilarFilteredAsync(
        Guid mangaId,
        string? status,
        string? type,
        List<string>? genres,
        int limit = 10,
        CancellationToken ct = default)
    {
        var points = await _client.RetrieveAsync(
            CollectionName,
            new List<PointId> { (PointId)mangaId },
            withVectors: true,
            cancellationToken: ct);

        if (points.Count == 0)
        {
            _logger.LogWarning("Manga (ID: {Id}) not found in Qdrant for filtered similarity.", mangaId);
            return new List<ScoredMangaResult>();
        }

        var targetPoint = points[0];
        var denseData = ExtractDenseVector(targetPoint);
        var sparseData = ExtractSparseVector(targetPoint);

        if (denseData == null || denseData.Length == 0)
        {
            _logger.LogWarning("No dense vector found for manga (ID: {Id}) in Qdrant.", mangaId);
            return new List<ScoredMangaResult>();
        }

        var filter = new Filter();

        // Always exclude the source manga
        filter.MustNot.Add(new Condition
        {
            HasId = new HasIdCondition { HasId = { (PointId)mangaId } }
        });

        // Apply payload field filters
        if (!string.IsNullOrWhiteSpace(status))
        {
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "status",
                    Match = new Match { Keyword = status }
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "type",
                    Match = new Match { Keyword = type }
                }
            });
        }

        if (genres != null && genres.Any())
        {
            foreach (var genre in genres)
            {
                filter.Must.Add(new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "genres",
                        Match = new Match { Keyword = genre }
                    }
                });
            }
        }

        var prefetchList = new List<PrefetchQuery>
        {
            new()
            {
                Query = new Query { Nearest = new VectorInput(denseData) },
                Using = DenseVectorName,
                Limit = (ulong)(limit * 2),
                Filter = filter
            }
        };

        if (sparseData != null && sparseData.Indices.Count > 0)
        {
            var sparseVector = new SparseVector();
            sparseVector.Indices.AddRange(sparseData.Indices);
            sparseVector.Values.AddRange(sparseData.Values);

            prefetchList.Add(new PrefetchQuery
            {
                Query = new Query
                {
                    Nearest = new VectorInput { Sparse = sparseVector }
                },
                Using = SparseVectorName,
                Limit = (ulong)(limit * 2),
                Filter = filter
            });
        }

        var searchResult = await _client.QueryAsync(
            CollectionName,
            prefetch: prefetchList,
            query: new Query { Fusion = Fusion.Rrf },
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult
            .Select(r => new ScoredMangaResult(Guid.Parse(r.Id.Uuid), r.Score))
            .ToList();
    }

    /// <summary>
    /// Advanced recommendation using Qdrant's native positive/negative example API returning IDs and scores.
    /// </summary>
    public async Task<List<ScoredMangaResult>> RecommendAdvancedAsync(
        List<Guid> likedIds,
        List<Guid> dislikedIds,
        int limit = 10,
        CancellationToken ct = default)
    {
        if (!likedIds.Any())
            return new List<ScoredMangaResult>();

        var positives = likedIds.Select(id => (PointId)id).ToList();
        var negatives = dislikedIds.Select(id => (PointId)id).ToList();

        // Exclude all input IDs from results
        var excludedIds = likedIds.Concat(dislikedIds).Select(id => (PointId)id);
        var filter = new Filter();
        filter.MustNot.Add(new Condition
        {
            HasId = new HasIdCondition { HasId = { excludedIds } }
        });

        var recommend = new RecommendInput();
        recommend.Positive.AddRange(positives.Select(p => (VectorInput)p));
        recommend.Negative.AddRange(negatives.Select(n => (VectorInput)n));

        var result = await _client.QueryAsync(
            CollectionName,
            query: recommend,
            usingVector: DenseVectorName,
            filter: filter,
            limit: (ulong)limit,
            cancellationToken: ct);

        return result
            .Select(r => new ScoredMangaResult(Guid.Parse(r.Id.Uuid), r.Score))
            .ToList();
    }

    /// <summary>
    /// Hybrid category / trope similarity search using dense vector embeddings + BM25 sparse vectors with RRF fusion.
    /// Supports optional payload filtering (status, type, genres) and source manga exclusion.
    /// </summary>
    public async Task<List<ScoredMangaResult>> SimilarByCategoryAsync(
        IEnumerable<string> categories,
        string? status = null,
        string? type = null,
        List<string>? genres = null,
        Guid? excludeMangaId = null,
        int limit = 10,
        CancellationToken ct = default)
    {
        var cleanedCategories = ParseAndCleanCategories(categories);
        if (cleanedCategories.Count == 0)
        {
            _logger.LogWarning("No valid categories provided for SimilarByCategory.");
            return new List<ScoredMangaResult>();
        }

        var filter = BuildCategorySearchFilter(status, type, genres, excludeMangaId);

        // 1. Construct dense query text emphasizing tropes & themes
        var queryText = $"Tropes & Themes: {string.Join(", ", cleanedCategories)}";
        var embedding = await _embeddingService.GenerateEmbeddingAsync(queryText, mode: "query", ct);

        var prefetchList = new List<PrefetchQuery>();

        // 2. Add Dense prefetch
        if (embedding != null && embedding.Length == (int)_vectorSize)
        {
            var prefetchDense = new PrefetchQuery
            {
                Query = new Query { Nearest = new VectorInput(embedding) },
                Using = DenseVectorName,
                Limit = (ulong)(limit * 3)
            };
            if (filter != null) prefetchDense.Filter = filter;
            prefetchList.Add(prefetchDense);
        }

        // 3. Add Sparse BM25 prefetch with bonus phrase terms (2.5f boost per category)
        var querySparse = ComputeSparseVector(queryText, cleanedCategories);
        if (querySparse.Indices.Count > 0)
        {
            var prefetchSparse = new PrefetchQuery
            {
                Query = new Query { Nearest = new VectorInput { Sparse = querySparse } },
                Using = SparseVectorName,
                Limit = (ulong)(limit * 3)
            };
            if (filter != null) prefetchSparse.Filter = filter;
            prefetchList.Add(prefetchSparse);
        }

        if (prefetchList.Count == 0)
        {
            _logger.LogWarning("Failed to create any prefetch query for SimilarByCategory.");
            return new List<ScoredMangaResult>();
        }

        // If only one prefetch exists (e.g. dense embedding failed), query that vector directly
        if (prefetchList.Count == 1)
        {
            var singlePrefetch = prefetchList[0];
            var singleResult = await _client.QueryAsync(
                CollectionName,
                query: singlePrefetch.Query,
                usingVector: singlePrefetch.Using,
                filter: filter,
                limit: (ulong)limit,
                cancellationToken: ct);

            return singleResult
                .Select(r => new ScoredMangaResult(Guid.Parse(r.Id.Uuid), r.Score))
                .ToList();
        }

        // Hybrid Dense + Sparse RRF fusion query
        var searchResult = await _client.QueryAsync(
            CollectionName,
            prefetch: prefetchList,
            query: new Query { Fusion = Fusion.Rrf },
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult
            .Select(r => new ScoredMangaResult(Guid.Parse(r.Id.Uuid), r.Score))
            .ToList();
    }

    /// <summary>
    /// Overload accepting a comma-separated string of categories (e.g. from query string or tag list).
    /// </summary>
    public Task<List<ScoredMangaResult>> SimilarByCategoryAsync(
        string categories,
        string? status = null,
        string? type = null,
        List<string>? genres = null,
        Guid? excludeMangaId = null,
        int limit = 10,
        CancellationToken ct = default)
    {
        return SimilarByCategoryAsync(new[] { categories }, status, type, genres, excludeMangaId, limit, ct);
    }

    /// <summary>
    /// Alias method matching function name 'SimilarByCategory'.
    /// </summary>
    public Task<List<ScoredMangaResult>> SimilarByCategory(
        IEnumerable<string> categories,
        string? status = null,
        string? type = null,
        List<string>? genres = null,
        Guid? excludeMangaId = null,
        int limit = 10,
        CancellationToken ct = default)
    {
        return SimilarByCategoryAsync(categories, status, type, genres, excludeMangaId, limit, ct);
    }

    /// <summary>
    /// Alias method matching function name 'SimilarByCategory' for a single comma-separated string.
    /// </summary>
    public Task<List<ScoredMangaResult>> SimilarByCategory(
        string categories,
        string? status = null,
        string? type = null,
        List<string>? genres = null,
        Guid? excludeMangaId = null,
        int limit = 10,
        CancellationToken ct = default)
    {
        return SimilarByCategoryAsync(new[] { categories }, status, type, genres, excludeMangaId, limit, ct);
    }

    public static List<string> ParseAndCleanCategories(IEnumerable<string>? categories)
    {
        if (categories == null) return new List<string>();

        return categories
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .SelectMany(c => c.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(CleanCategory)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Filter? BuildCategorySearchFilter(string? status, string? type, List<string>? genres, Guid? excludeMangaId)
    {
        var filter = new Filter();
        bool hasCondition = false;

        if (excludeMangaId.HasValue && excludeMangaId.Value != Guid.Empty)
        {
            filter.MustNot.Add(new Condition
            {
                HasId = new HasIdCondition { HasId = { (PointId)excludeMangaId.Value } }
            });
            hasCondition = true;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "status",
                    Match = new Match { Keyword = status }
                }
            });
            hasCondition = true;
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "type",
                    Match = new Match { Keyword = type }
                }
            });
            hasCondition = true;
        }

        if (genres != null && genres.Any())
        {
            foreach (var genre in genres.Where(g => !string.IsNullOrWhiteSpace(g)))
            {
                filter.Must.Add(new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "genres",
                        Match = new Match { Keyword = genre }
                    }
                });
                hasCondition = true;
            }
        }

        return hasCondition ? filter : null;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private float[]? ExtractDenseVector(RetrievedPoint point)
    {
        if (point.Vectors == null)
            return null;

        // 1. Try NamedVectors (hybrid collection format: point.Vectors.Vectors.Vectors)
        var map = point.Vectors.Vectors?.Vectors;
        if (map != null && map.TryGetValue(DenseVectorName, out var namedVector))
        {
            var dense = namedVector.GetDenseVector();
            if (dense?.Data != null && dense.Data.Count > 0)
                return dense.Data.ToArray();

            if (namedVector.Dense?.Data != null && namedVector.Dense.Data.Count > 0)
                return namedVector.Dense.Data.ToArray();
        }

        // 2. Try single default vector (fallback for legacy or un-named collections)
        if (point.Vectors.Vector != null)
        {
            var singleDense = point.Vectors.Vector.GetDenseVector();
            if (singleDense?.Data != null && singleDense.Data.Count > 0)
                return singleDense.Data.ToArray();

            if (point.Vectors.Vector.Dense?.Data != null && point.Vectors.Vector.Dense.Data.Count > 0)
                return point.Vectors.Vector.Dense.Data.ToArray();
        }

        return null;
    }

    private SparseVector? ExtractSparseVector(RetrievedPoint point)
    {
        if (point.Vectors == null)
            return null;

        var map = point.Vectors.Vectors?.Vectors;
        if (map != null && map.TryGetValue(SparseVectorName, out var namedVector))
        {
            var sparse = namedVector.GetSparseVector();
            if (sparse != null && sparse.Indices.Count > 0)
                return sparse;

            if (namedVector.Sparse != null && namedVector.Sparse.Indices.Count > 0)
                return namedVector.Sparse;
        }

        return null;
    }

    public async Task UpsertMangaDirectAsync(Manga manga, CancellationToken ct = default)
    {
        await UpsertMangaAsync(manga, ct);
    }

    private async Task<PointStruct> MapToPointStructAsync(Manga manga, CancellationToken ct = default)
    {
        var title = manga.Title?.Trim() ?? string.Empty;

        // 1. Deduplicate synonyms against each other and against Title (case-insensitive)
        var distinctSynonyms = (manga.Synonyms ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .Where(s => !string.Equals(s, title, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        // 2. Extract distinct genres
        var distinctGenres = (manga.Genres ?? new List<string>())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var genreSet = new HashSet<string>(distinctGenres, StringComparer.OrdinalIgnoreCase);

        // 3. Deduplicate and clean categories against each other and exclude any already in genres
        var distinctCategories = (manga.Categories ?? new List<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(CleanCategory)
            .Where(c => !string.IsNullOrWhiteSpace(c) && !genreSet.Contains(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();

        var textParts = new List<string>(8);

        if (!string.IsNullOrWhiteSpace(title))
            textParts.Add($"Title: {title}");

        if (distinctGenres.Count > 0)
            textParts.Add($"Genres: {string.Join(", ", distinctGenres)}");

        if (distinctCategories.Count > 0)
            textParts.Add($"Tropes & Themes: {string.Join(", ", distinctCategories)}");

        if (!string.IsNullOrWhiteSpace(manga.Type) && !string.Equals(manga.Type, "Unknown", StringComparison.OrdinalIgnoreCase))
            textParts.Add($"Type: {manga.Type.Trim()}");

        if (distinctSynonyms.Count > 0)
            textParts.Add($"Alternative Titles: {string.Join(", ", distinctSynonyms.Take(5))}");

        if (!string.IsNullOrWhiteSpace(manga.Status) && !string.Equals(manga.Status, "Unknown", StringComparison.OrdinalIgnoreCase))
            textParts.Add($"Status: {manga.Status.Trim()}");

        if (!string.IsNullOrWhiteSpace(manga.Author) && !string.Equals(manga.Author, "Unknown", StringComparison.OrdinalIgnoreCase))
            textParts.Add($"Author: {manga.Author.Trim()}");

        var cleanSynopsis = CleanSynopsis(manga.Description);
        if (!string.IsNullOrWhiteSpace(cleanSynopsis))
        {
            textParts.Add($"Synopsis: {cleanSynopsis}");
        }

        var text = string.Join(". ", textParts);
        var embedding = await _embeddingService.GenerateEmbeddingAsync(text, mode: "passage", ct);

        var namedVectors = new NamedVectors();
        if (embedding != null && embedding.Length == (int)_vectorSize)
        {
            var denseVec = new Vector { Dense = new DenseVector() };
            denseVec.Dense.Data.AddRange(embedding);
            namedVectors.Vectors[DenseVectorName] = denseVec;
        }
        else
        {
            _logger.LogWarning("Using zero dense vector for manga {Id} due to embedding failure.", manga.Id.Value);
            var zeroVec = new Vector { Dense = new DenseVector() };
            zeroVec.Dense.Data.AddRange(new float[_vectorSize]);
            namedVectors.Vectors[DenseVectorName] = zeroVec;
        }

        var sparseVec = ComputeSparseVector(text, distinctCategories.Concat(distinctGenres));
        if (sparseVec.Indices.Count > 0)
        {
            namedVectors.Vectors[SparseVectorName] = new Vector { Sparse = sparseVec };
        }

        return new PointStruct
        {
            Id = (PointId)manga.Id.Value,
            Vectors = new Vectors { Vectors_ = namedVectors },
            Payload =
            {
                ["title"] = title,
                ["synonyms"] = distinctSynonyms.ToArray(),
                ["description"] = manga.Description ?? string.Empty,
                ["author"] = !string.IsNullOrWhiteSpace(manga.Author) ? manga.Author : "Unknown",
                ["status"] = !string.IsNullOrWhiteSpace(manga.Status) ? manga.Status : "Unknown",
                ["type"] = !string.IsNullOrWhiteSpace(manga.Type) ? manga.Type : "Unknown",
                ["genres"] = distinctGenres.ToArray(),
                ["categories"] = distinctCategories.ToArray()
            }
        };
    }

    private static SparseVector ComputeSparseVector(string text, IEnumerable<string>? bonusPhrases = null)
    {
        var sparse = new SparseVector();
        if (string.IsNullOrWhiteSpace(text)) return sparse;

        var termCounts = new Dictionary<uint, float>();

        static uint HashTerm(ReadOnlySpan<char> term)
        {
            if (term.Length <= 1) return 0;
            uint hash = 2166136261;
            bool allAscii = true;
            for (int i = 0; i < term.Length; i++)
            {
                if (term[i] > 127)
                {
                    allAscii = false;
                    break;
                }
            }

            if (allAscii)
            {
                for (int i = 0; i < term.Length; i++)
                {
                    hash = (hash ^ (byte)term[i]) * 16777619;
                }
            }
            else
            {
                Span<byte> utf8Bytes = stackalloc byte[Math.Min(term.Length * 3, 512)];
                int bytesWritten = System.Text.Encoding.UTF8.GetBytes(term, utf8Bytes);
                for (int i = 0; i < bytesWritten; i++)
                {
                    hash = (hash ^ utf8Bytes[i]) * 16777619;
                }
            }
            return (hash % 999999) + 1;
        }

        static uint HashBigram(ReadOnlySpan<char> term1, ReadOnlySpan<char> term2)
        {
            uint hash = 2166136261;
            bool allAscii = true;
            for (int i = 0; i < term1.Length; i++)
            {
                if (term1[i] > 127) { allAscii = false; break; }
            }
            if (allAscii)
            {
                for (int i = 0; i < term2.Length; i++)
                {
                    if (term2[i] > 127) { allAscii = false; break; }
                }
            }

            if (allAscii)
            {
                for (int i = 0; i < term1.Length; i++)
                {
                    hash = (hash ^ (byte)term1[i]) * 16777619;
                }
                hash = (hash ^ (byte)' ') * 16777619;
                for (int i = 0; i < term2.Length; i++)
                {
                    hash = (hash ^ (byte)term2[i]) * 16777619;
                }
            }
            else
            {
                int maxLen = (term1.Length + 1 + term2.Length) * 3;
                Span<byte> utf8Bytes = stackalloc byte[Math.Min(maxLen, 512)];
                int w1 = System.Text.Encoding.UTF8.GetBytes(term1, utf8Bytes);
                utf8Bytes[w1] = (byte)' ';
                int w2 = System.Text.Encoding.UTF8.GetBytes(term2, utf8Bytes.Slice(w1 + 1));
                int totalBytes = w1 + 1 + w2;
                for (int i = 0; i < totalBytes; i++)
                {
                    hash = (hash ^ utf8Bytes[i]) * 16777619;
                }
            }
            return (hash % 999999) + 1;
        }

        void AddTermHash(uint index, float weight)
        {
            if (index == 0) return;
            if (termCounts.TryGetValue(index, out float count))
                termCounts[index] = count + weight;
            else
                termCounts[index] = weight;
        }

        // 1. Index high-salience bonus phrases (categories, tropes, genres)
        if (bonusPhrases != null)
        {
            foreach (var phrase in bonusPhrases)
            {
                var clean = CleanCategory(phrase).ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(clean) && clean.Length > 2)
                {
                    AddTermHash(HashTerm(clean.AsSpan()), 2.5f);
                }
            }
        }

        // 2. Tokenize text into words
        var words = text.ToLowerInvariant()
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);

        // 3. Index unigrams & bigrams
        for (int i = 0; i < words.Length; i++)
        {
            var w = words[i];
            AddTermHash(HashTerm(w.AsSpan()), 1.0f);

            if (i < words.Length - 1)
            {
                AddTermHash(HashBigram(w.AsSpan(), words[i + 1].AsSpan()), 1.5f);
            }
        }

        // Apply sublinear term frequency weight: 1.0 + ln(tf)
        var sortedKeys = new List<uint>(termCounts.Keys);
        sortedKeys.Sort();
        foreach (var key in sortedKeys)
        {
            sparse.Indices.Add(key);
            sparse.Values.Add((float)(1.0 + Math.Log(termCounts[key])));
        }

        return sparse;
    }

    private static string CleanCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return string.Empty;
        // Normalize "/s" or "/es" e.g. "Misunderstanding/s" -> "Misunderstandings"
        return category.Replace("/s", "s", StringComparison.OrdinalIgnoreCase)
                       .Replace("/es", "es", StringComparison.OrdinalIgnoreCase)
                       .Replace("/", " ")
                       .Trim();
    }

    [GeneratedRegex(@"<[^>]+>", RegexOptions.None)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\[/?[a-zA-Z0-9_-]+(?:=[^\]]+)?\]", RegexOptions.None)]
    private static partial Regex BbCodeRegex();

    [GeneratedRegex(@"^(?:sinopsis|synopsis|deskripsi|summary)\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ScraperPrefixRegex();

    [GeneratedRegex(@"baca\s+(?:manga|manhwa|manhua|komik)[^.\n]*?(?:bahasa\s+indonesia|terlengkap|gratis)[^.\n]*[.]?", RegexOptions.IgnoreCase)]
    private static partial Regex ScraperPromoRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None)]
    private static partial Regex MultipleWhitespaceRegex();

    private static string CleanSynopsis(string? rawDescription)
    {
        if (string.IsNullOrWhiteSpace(rawDescription)) return string.Empty;

        // 1. Decode HTML entities (e.g. &amp;, &quot;, &#039;, &nbsp;)
        var text = System.Net.WebUtility.HtmlDecode(rawDescription);

        // 2. Remove HTML tags
        text = HtmlTagRegex().Replace(text, " ");

        // 3. Remove BBCode tags (e.g. [b], [/b], [url=...])
        text = BbCodeRegex().Replace(text, " ");

        // 4. Remove scraper prefixes and promo headers
        text = ScraperPrefixRegex().Replace(text, string.Empty);

        // 5. Remove scraper promotional boilerplate lines (e.g. "Baca komik ... bahasa indonesia di ...")
        text = ScraperPromoRegex().Replace(text, string.Empty);

        // 6. Normalize multiple whitespaces into a single space
        text = MultipleWhitespaceRegex().Replace(text, " ").Trim();

        // 7. Cap synopsis length to ~1000 characters without splitting words
        if (text.Length > 1000)
        {
            int lastSpace = text.LastIndexOf(' ', 1000);
            text = lastSpace > 200 ? string.Concat(text.AsSpan(0, lastSpace), "...") : string.Concat(text.AsSpan(0, 1000), "...");
        }

        return text;
    }
}
