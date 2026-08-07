using Microsoft.AspNetCore.Routing;

namespace MangaScrapper.Application.Common.Abstractions;

public interface IEndpointDefinition
{
    void DefineEndpoints(IEndpointRouteBuilder app);
}
