using System.Globalization;
using FluentValidation;
using FluentValidation.Results;

namespace NexUS.User.Api.Controllers;

/// <summary>Header <c>If-Match</c> berisi <c>ROW_VERSION</c> (CONTEXT.md bagian 6); ETag di response memakai format yang sama.</summary>
internal static class IfMatchHeader
{
    public static long Require(HttpRequest request)
    {
        var raw = request.Headers.IfMatch.ToString().Trim().Trim('"');
        if (long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var version)) return version;

        throw new ValidationException([new ValidationFailure("If-Match", "Header If-Match berisi versi data (rowVersion) wajib diisi.")]);
    }

    public static void Write(HttpResponse response, long rowVersion) =>
        response.Headers.ETag = $"\"{rowVersion.ToString(CultureInfo.InvariantCulture)}\"";
}
