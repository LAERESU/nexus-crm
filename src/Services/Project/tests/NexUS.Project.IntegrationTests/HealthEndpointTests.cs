using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NexUS.Project.IntegrationTests;

public class HealthEndpointTests(ProjectApiFactory factory) : IClassFixture<ProjectApiFactory>
{
    [Fact]
    public async Task Health_live_mengembalikan_200()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_tampil_pada_environment_Development()
    {
        var client = factory.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>Host tanpa database: hanya konfigurasi wajib yang diisi.</summary>
public sealed class ProjectApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("Services:User:BaseUrl", "http://user.invalid");
}
