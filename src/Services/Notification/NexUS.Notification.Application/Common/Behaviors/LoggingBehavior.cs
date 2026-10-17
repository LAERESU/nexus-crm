using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace NexUS.Notification.Application.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next(ct);
            logger.LogInformation("{RequestName} selesai dalam {ElapsedMs} ms", requestName, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception)
        {
            logger.LogWarning("{RequestName} gagal setelah {ElapsedMs} ms", requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
