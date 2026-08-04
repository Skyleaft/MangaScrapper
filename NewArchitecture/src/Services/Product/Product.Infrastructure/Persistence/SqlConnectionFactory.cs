using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NovaStack.Infrastructure.Persistence.Options;
using NovaStack.SharedKernel.Abstractions;
using Npgsql;

namespace Product.Infrastructure.Persistence;

/// <summary>
///     Implementation of <see cref="ISqlConnectionFactory" /> for the Product service,
///     resolving the current provider and connection string from <see cref="DatabaseOptions" />.
/// </summary>
public sealed class SqlConnectionFactory(IOptions<DatabaseOptions> databaseOptions) : ISqlConnectionFactory
{
    private readonly DatabaseOptions _options = databaseOptions.Value;

    public IDbConnection CreateConnection()
    {
        return _options.Provider switch
        {
            DatabaseProvider.PostgreSQL => new NpgsqlConnection(_options.ConnectionString),
            DatabaseProvider.SqlServer => new SqlConnection(_options.ConnectionString),
            _ => throw new InvalidOperationException($"Unsupported database provider: {_options.Provider}")
        };
    }
}