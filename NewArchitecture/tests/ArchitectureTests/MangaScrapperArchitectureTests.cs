using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace ArchitectureTests;

public sealed class MangaScrapperArchitectureTests
{
    private const string DomainAssembly = "MangaScrapper.Domain";
    private const string ApplicationAssembly = "MangaScrapper.Application";
    private const string InfrastructureAssembly = "MangaScrapper.Infrastructure";

    [Fact]
    public void Domain_Should_Not_DependOn_Application_Or_Infrastructure()
    {
        var result = Types.InAssembly(typeof(MangaScrapper.Domain.Aggregates.Manga).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationAssembly, InfrastructureAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue("MangaScrapper.Domain must not depend on Application or Infrastructure.");
    }

    [Fact]
    public void Application_Should_Not_DependOn_Infrastructure()
    {
        var result = Types.InAssembly(typeof(MangaScrapper.Application.Features.Manga.GetPagedManga.GetPagedMangaQuery).Assembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue("MangaScrapper.Application must not depend on MangaScrapper.Infrastructure.");
    }

    [Fact]
    public void Handlers_Should_Be_Sealed()
    {
        var result = Types.InAssembly(typeof(MangaScrapper.Application.Features.Manga.GetPagedManga.GetPagedMangaQuery).Assembly)
            .That()
            .HaveNameEndingWith("Handler")
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue("All MediatR handlers in MangaScrapper.Application should be sealed.");
    }

    [Fact]
    public void Endpoints_Should_Implement_IEndpointDefinition()
    {
        var result = Types.InAssembly(typeof(MangaScrapper.Application.Features.Manga.GetPagedManga.GetPagedMangaQuery).Assembly)
            .That()
            .HaveNameEndingWith("Endpoint")
            .Should()
            .ImplementInterface(typeof(MangaScrapper.Application.Common.Abstractions.IEndpointDefinition))
            .GetResult();

        result.IsSuccessful.Should().BeTrue("All Minimal API endpoint classes should implement IEndpointDefinition.");
    }
}
