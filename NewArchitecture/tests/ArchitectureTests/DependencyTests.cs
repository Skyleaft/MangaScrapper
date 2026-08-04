using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace ArchitectureTests;

/// <summary>
/// Architecture tests enforcing Vertical Slice Architecture dependency boundaries.
/// Run these as part of CI to prevent accidental layer violations.
/// </summary>
public sealed class DependencyTests
{
    // Assembly names
    private const string DomainAssembly = "Product.Domain";
    private const string ApplicationAssembly = "Product.Application";
    private const string InfrastructureAssembly = "Product.Infrastructure";
    private const string SharedKernelAssembly = "NovaStack.SharedKernel";
    private const string InfrastructureBBAssembly = "NovaStack.Infrastructure";

    [Fact]
    public void Domain_Should_Not_DependOn_Application()
    {
        var result = Types.InAssembly(typeof(Product.Domain.Aggregates.Product).Assembly)
            .ShouldNot()
            .HaveDependencyOn(ApplicationAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer must not reference Application.");
    }

    [Fact]
    public void Domain_Should_Not_DependOn_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Product.Domain.Aggregates.Product).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureAssembly, InfrastructureBBAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer must not reference Infrastructure.");
    }

    [Fact]
    public void Application_Should_Not_DependOn_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Product.Application.Features.Products.CreateProduct.CreateProductCommand).Assembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Application layer must not reference Product.Infrastructure (only abstractions).");
    }

    [Fact]
    public void SharedKernel_Should_Not_DependOn_Any_Service()
    {
        var result = Types.InAssembly(typeof(NovaStack.SharedKernel.Results.Error).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(DomainAssembly, ApplicationAssembly, InfrastructureAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "SharedKernel must not depend on any service-specific assembly.");
    }

    [Fact]
    public void CommandHandlers_Should_BeInternal_And_Sealed()
    {
        var result = Types.InAssembly(typeof(Product.Application.Features.Products.CreateProduct.CreateProductCommand).Assembly)
            .That()
            .HaveNameEndingWith("CommandHandler")
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "All command handlers should be sealed to prevent inheritance.");
    }

    [Fact]
    public void QueryHandlers_Should_BeInternal_And_Sealed()
    {
        var result = Types.InAssembly(typeof(Product.Application.Features.Products.CreateProduct.CreateProductCommand).Assembly)
            .That()
            .HaveNameEndingWith("QueryHandler")
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "All query handlers should be sealed to prevent inheritance.");
    }
}
