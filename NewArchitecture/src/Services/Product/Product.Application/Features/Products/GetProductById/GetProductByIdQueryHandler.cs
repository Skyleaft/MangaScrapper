using MapsterMapper;
using NovaStack.SharedKernel.Results;
using Product.Application.Common.Abstractions;
using Product.Domain.Repositories;
using Product.Domain.ValueObjects;

namespace Product.Application.Features.Products.GetProductById;

/// <summary>Handles the <see cref="GetProductByIdQuery" />.</summary>
internal sealed class GetProductByIdQueryHandler(IProductRepository productRepository, IMapper mapper)
    : IQueryHandler<GetProductByIdQuery, ProductResponse>
{
    public async Task<Result<ProductResponse>> Handle(
        GetProductByIdQuery query,
        CancellationToken ct)
    {
        var product = await productRepository.GetByIdAsync(
            ProductId.From(query.Id), ct);

        if (product is null)
            return Error.NotFound("Product.NotFound", $"Product with id '{query.Id}' was not found.");

        return mapper.Map<ProductResponse>(product);
    }
}