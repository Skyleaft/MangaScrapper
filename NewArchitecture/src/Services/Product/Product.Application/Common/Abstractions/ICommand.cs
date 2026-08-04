using MediatR;
using NovaStack.SharedKernel.Results;

namespace Product.Application.Common.Abstractions;

/// <summary>Command that returns a <see cref="Result" />.</summary>
public interface ICommand : IRequest<Result>
{
}

/// <summary>Command that returns a <see cref="Result{TResponse}" />.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>
{
}

/// <summary>Handler for <see cref="ICommand" />.</summary>
public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand
{
}

/// <summary>Handler for <see cref="ICommand{TResponse}" />.</summary>
public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>
{
}