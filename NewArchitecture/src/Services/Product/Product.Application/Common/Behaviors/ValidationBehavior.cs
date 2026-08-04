using FluentValidation;
using MediatR;
using NovaStack.SharedKernel.Results;

namespace Product.Application.Common.Behaviors;

/// <summary>
///     MediatR pipeline behavior that runs FluentValidation before each command/query.
///     Returns a validation <see cref="Error" /> instead of throwing an exception.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0) return await next();

        var errors = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray());

        // Build a validation Error to return in the Result
        var errorMessage = string.Join("; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}"));
        var error = Error.Validation("Validation.Failed", errorMessage);

        // Construct the failed Result<TResponse>
        var failedResult = typeof(TResponse) == typeof(Result)
            ? (TResponse)Result.Failure(error)
            : (TResponse)typeof(Result)
                .GetMethods()
                .First(m => m.Name == nameof(Result.Failure) && m.IsGenericMethod)
                .MakeGenericMethod(typeof(TResponse).GetGenericArguments()[0])
                .Invoke(null, [error])!;

        return failedResult;
    }
}