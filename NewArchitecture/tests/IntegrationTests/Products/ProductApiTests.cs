using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using IntegrationTests.Fixtures;
using NovaStack.Contracts.Responses;
using Product.Application.Features.Products.CreateProduct;
using Product.Application.Features.Products.GetProductById;
using Xunit;

namespace IntegrationTests.Products;

/// <summary>Integration tests for Product API endpoints.</summary>
public sealed class ProductApiTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task POST_Products_ValidPayload_Returns201WithId()
    {
        // Arrange
        var command = new CreateProductCommand(
            Name: "Integration Test Product",
            Description: "Created during integration test",
            Price: 29.99m,
            Currency: "USD",
            StockQuantity: 50);

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/products", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<Guid>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Data.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GET_Products_ById_ExistingProduct_Returns200()
    {
        // Arrange — create first
        var createCommand = new CreateProductCommand(
            Name: "Findable Product",
            Description: "Should be retrievable",
            Price: 49.99m,
            Currency: "USD",
            StockQuantity: 10);

        var createResponse = await _client.PostAsJsonAsync("/api/v1/products", createCommand);
        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<Guid>>();
        var productId = created!.Data;

        // Act
        var response = await _client.GetAsync($"/api/v1/products/{productId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ProductResponse>>();
        body!.Data!.Name.Should().Be("Findable Product");
    }

    [Fact]
    public async Task GET_Products_ById_NotFound_Returns404()
    {
        // Act
        var response = await _client.GetAsync($"/api/v1/products/{Guid.CreateVersion7()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_Products_ReturnsPagedList()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/products?page=1&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_Products_DuplicateName_Returns409()
    {
        // Arrange
        var command = new CreateProductCommand(
            Name: "Duplicate Name Test",
            Description: "First",
            Price: 10m,
            Currency: "USD",
            StockQuantity: 1);

        await _client.PostAsJsonAsync("/api/v1/products", command);

        // Act — send same name again
        var response = await _client.PostAsJsonAsync("/api/v1/products", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
