using System.Runtime.CompilerServices;
namespace FastEndpoints.Swagger;
internal static class SwaggerExportPathInitializer
{
    [ModuleInitializer]
    internal static void Initialize() => DocumentOptions.SwaggerExportPath = "wwwroot/openapi";
}
