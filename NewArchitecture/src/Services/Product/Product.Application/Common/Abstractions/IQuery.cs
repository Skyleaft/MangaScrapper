using MediatR;
using NovaStack.SharedKernel.Results;

namespace Product.Application.Common.Abstractions;

/// <summary>Query that returns a <see cref="Result{TResponse}" />.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>
{
}

/// <summary>Handler for <see cref="IQuery{TResponse}" />.</summary>
public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>
{
}