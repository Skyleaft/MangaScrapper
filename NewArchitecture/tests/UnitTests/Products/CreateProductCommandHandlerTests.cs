using FluentAssertions;
using Moq;
using NovaStack.SharedKernel.Abstractions;
using Product.Application.Features.Products.CreateProduct;
using Product.Domain.Repositories;
using Xunit;
using DomainProduct = Product.Domain.Aggregates.Product;

namespace UnitTests.Products;

/// <summary>Unit tests for <see cref="CreateProductCommandHandler"/>.</summary>
public sealed class CreateProductCommandHandlerTests
{
    private readonly Mock<IProductRepository> _repositoryMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IClaimService> _claimServiceMock = new();
    private readonly CreateProductCommandHandler _handler;

    public CreateProductCommandHandlerTests()
    {
        _handler = new CreateProductCommandHandler(
            _repositoryMock.Object,
            _unitOfWorkMock.Object,
            _claimServiceMock.Object);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithProductId()
    {
        // Arrange
        var command = new CreateProductCommand(
            Name: "Test Widget",
            Description: "A test product",
            Price: 9.99m,
            Currency: "USD",
            StockQuantity: 100);

        _repositoryMock
            .Setup(r => r.ExistsByNameAsync(command.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<DomainProduct>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_DuplicateName_ReturnsConflictError()
    {
        // Arrange
        var command = new CreateProductCommand(
            Name: "Existing Product",
            Description: "Duplicate",
            Price: 5.00m,
            Currency: "USD",
            StockQuantity: 10);

        _repositoryMock
            .Setup(r => r.ExistsByNameAsync(command.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(NovaStack.SharedKernel.Results.ErrorType.Conflict);
        result.Error.Code.Should().Be("Product.NameConflict");

        _repositoryMock.Verify(
            r => r.AddAsync(It.IsAny<DomainProduct>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NegativePrice_ThrowsArgumentException()
    {
        // Arrange — negative price violates Money value object invariant
        var command = new CreateProductCommand(
            Name: "Bad Price",
            Description: "Test",
            Price: -1m,
            Currency: "USD",
            StockQuantity: 10);

        _repositoryMock
            .Setup(r => r.ExistsByNameAsync(command.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _handler.Handle(command, CancellationToken.None));
    }
}
