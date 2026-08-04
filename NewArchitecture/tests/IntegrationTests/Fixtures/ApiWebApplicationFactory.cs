using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Product.Api;
using Product.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace IntegrationTests.Fixtures;

/// <summary>
/// Test fixture that spins up a PostgreSQL Testcontainer and configures the app
/// to use it via WebApplicationFactory.
/// </summary>
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("novastack_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real ProductDbContext registration
            services.RemoveAll<DbContextOptions<ProductDbContext>>();

            // Replace with test container connection
            services.AddDbContext<ProductDbContext>(options =>
                options.UseNpgsql(_postgresContainer.GetConnectionString()));

            // Apply migrations
            using var scope = services.BuildServiceProvider().CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            dbContext.Database.EnsureCreated();
        });

        builder.UseEnvironment("Testing");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }
}
