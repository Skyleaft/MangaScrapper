using Microsoft.AspNetCore.Routing;

namespace Product.Application.Common.Abstractions;

/// <summary>
///     Implemented by every vertical slice endpoint class.
///     Scanned and registered automatically at startup.
/// </summary>
public interface IEndpointDefinition
{
    void DefineEndpoints(IEndpointRouteBuilder app);
}