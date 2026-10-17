using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.Notification.Api.Middleware;

namespace NexUS.Notification.IntegrationTests;

public class ExceptionHandlingTests
{
    private static async Task<IHost> StartAsync()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => services.AddRouting())
                .Configure(app =>
                {
                    app.UseMiddleware<ExceptionHandlingMiddleware>();
                    app.UseRouting();
                    app.UseEndpoints(e =>
                    {
                        e.MapGet("/boom", () => { throw new InvalidOperationException("rahasia: stack detail"); });
                        e.MapGet("/domain", () => { throw new DomainException("Transisi tidak diizinkan."); });
                        e.MapGet("/missing", () => { throw new NotFoundException("Tidak ada."); });
                        e.MapGet("/forbidden", () => { throw new ForbiddenException("Bukan anggota."); });
                        e.MapGet("/conflict", () => { throw new ConcurrencyException("ROW_VERSION berbeda."); });
                    });
                }))
            .Build();
        await host.StartAsync();
        return host;
    }

    [Theory]
    [InlineData("/domain", HttpStatusCode.UnprocessableEntity)]
    [InlineData("/missing", HttpStatusCode.NotFound)]
    [InlineData("/forbidden", HttpStatusCode.Forbidden)]
    [InlineData("/conflict", HttpStatusCode.Conflict)]
    public async Task Exception_dipetakan_ke_status_yang_sesuai(string path, HttpStatusCode expected)
    {
        using var host = await StartAsync();

        var response = await host.GetTestClient().GetAsync(path);

        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Exception_tidak_tertangani_menghasilkan_ProblemDetails_500_tanpa_stack_trace()
    {
        using var host = await StartAsync();

        var response = await host.GetTestClient().GetAsync("/boom");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        body.Should().NotContain("rahasia").And.NotContain("InvalidOperationException").And.NotContain(" at ");
        JsonDocument.Parse(body).RootElement.GetProperty("status").GetInt32().Should().Be(500);
    }
}
