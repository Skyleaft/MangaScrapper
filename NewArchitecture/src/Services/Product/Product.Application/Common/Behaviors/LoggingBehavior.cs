using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using NovaStack.SharedKernel.Results;

namespace Product.Application.Common.Behaviors;

/// <summary>
///     MediatR pipeline behavior that logs request execution time and failures.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();

        logger.LogInformation("[{RequestName}] Handling request", requestName);

        try
        {
            var response = await next();
            sw.Stop();

            if (response.IsSuccess)
                logger.LogInformation(
                    "[{RequestName}] Handled successfully in {ElapsedMs}ms",
                    requestName, sw.ElapsedMilliseconds);
            else
                logger.LogWarning(
                    "[{RequestName}] Handled with failure in {ElapsedMs}ms. Error: {@Error}",
                    requestName, sw.ElapsedMilliseconds, response.Error);

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex,
                "[{RequestName}] Unhandled exception after {ElapsedMs}ms",
                requestName, sw.ElapsedMilliseconds);
            throw;
        }
    }
}