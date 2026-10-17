using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Notification.Api.Middleware;

/// <summary>Memetakan exception ke ProblemDetails (RFC 7807) tanpa membocorkan stack trace.</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
        {
            var problem = ToProblem(ex);
            if (problem.Status >= 500)
                logger.LogError(ex, "Exception tidak tertangani pada {Path}", context.Request.Path);
            else
                logger.LogInformation("Request ditolak {Status} pada {Path}: {Reason}", problem.Status, context.Request.Path, ex.Message);

            context.Response.StatusCode = problem.Status!.Value;
            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", context.RequestAborted);
        }
    }

    private static ProblemDetails ToProblem(Exception ex)
    {
        var (status, title) = ex switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validasi gagal"),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Autentikasi gagal"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Akses ditolak"),
            NotFoundException => (StatusCodes.Status404NotFound, "Data tidak ditemukan"),
            ConcurrencyException => (StatusCodes.Status409Conflict, "Konflik versi data"),
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Aturan bisnis dilanggar"),
            _ => (StatusCodes.Status500InternalServerError, "Terjadi kesalahan pada server")
        };

        var problem = new ProblemDetails { Status = status, Title = title };

        if (ex is ValidationException validation)
        {
            problem.Extensions["errors"] = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }
        else if (status != StatusCodes.Status500InternalServerError)
        {
            problem.Detail = ex.Message;
        }

        return problem;
    }
}
