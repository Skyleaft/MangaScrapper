using NovaStack.SharedKernel.Exceptions;

namespace Product.Domain.Exceptions;

/// <summary>Thrown when a product is not found.</summary>
public sealed class ProductNotFoundException : NotFoundException
{
    public ProductNotFoundException(Guid productId)
        : base("Product", productId)
    {
    }

    public ProductNotFoundException(string name)
        : base("Product", name)
    {
    }
}