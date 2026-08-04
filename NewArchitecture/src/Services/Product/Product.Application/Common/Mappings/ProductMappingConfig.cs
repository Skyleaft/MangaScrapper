using Mapster;
using Product.Application.Features.Products.GetProductById;
using DomainProduct = Product.Domain.Aggregates.Product;

namespace Product.Application.Common.Mappings;

/// <summary>
///     Mapster registration for Product mappings.
/// </summary>
public sealed class ProductMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<DomainProduct, ProductResponse>()
            .Map(dest => dest.Id, src => src.Id.Value)
            .Map(dest => dest.Price, src => src.Price.Amount)
            .Map(dest => dest.Currency, src => src.Price.Currency);
    }
}