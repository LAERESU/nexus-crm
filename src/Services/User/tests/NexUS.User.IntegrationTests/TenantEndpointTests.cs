using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace NexUS.User.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class TenantEndpointTests(OracleUserFixture fixture)
{
    private sealed record TenantBody(Guid Id, string Code, string Name, bool IsActive, long RowVersion);

    [Fact]
    public async Task Current_hanya_mengembalikan_tenant_milik_user()
    {
        var a = await fixture.ClientAs(OracleUserFixture.TenantA).GetFromJsonAsync<TenantBody>("/api/v1/tenants/current");
        var b = await fixture.ClientAs(OracleUserFixture.TenantB).GetFromJsonAsync<TenantBody>("/api/v1/tenants/current");

        a!.Code.Should().Be("tenant-a");
        b!.Code.Should().Be("tenant-b");
    }

    [Fact]
    public async Task Current_untuk_tenant_yang_tidak_ada_mengembalikan_404()
    {
        var response = await fixture.ClientAs(tenantId: 999).GetAsync("/api/v1/tenants/current");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
