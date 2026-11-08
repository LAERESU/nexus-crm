using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace NexUS.User.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class UserEndpointTests(OracleUserFixture fixture)
{
    private sealed record UserBody(Guid Id, string Email, string FullName, string Status, long RowVersion);

    private sealed record PagedBody(List<UserBody> Items, int Page, int Size, long TotalCount);

    private static string NewEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@contoh.com";

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string email, string name = "Budi Santoso") =>
        client.PostAsJsonAsync("/api/v1/users", new { email, fullName = name });

    [Fact]
    public async Task Create_201_dengan_Location_dan_ETag_serta_event_masuk_outbox()
    {
        var client = fixture.ClientAs(OracleUserFixture.TenantA);
        var email = NewEmail();

        var response = await CreateAsync(client, email);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.ETag.Should().NotBeNull();
        var body = await response.Content.ReadFromJsonAsync<UserBody>();
        body!.Status.Should().Be("active");

        var outbox = await fixture.ScalarAsync(
            $"SELECT COUNT(*) FROM OUTBOX_MESSAGES WHERE TYPE LIKE '%UserCreatedDomainEvent%' AND PAYLOAD LIKE '%{email}%'");
        Convert.ToInt32(outbox).Should().Be(1);
    }

    [Fact]
    public async Task Email_unik_per_tenant_tanpa_membedakan_huruf_besar_kecil()
    {
        var client = fixture.ClientAs(OracleUserFixture.TenantA);
        var email = NewEmail();
        (await CreateAsync(client, email)).StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await CreateAsync(client, email.ToUpperInvariant());

        duplicate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Email_yang_sama_boleh_dipakai_di_tenant_lain()
    {
        var email = NewEmail();
        (await CreateAsync(fixture.ClientAs(OracleUserFixture.TenantA), email)).StatusCode.Should().Be(HttpStatusCode.Created);

        var other = await CreateAsync(fixture.ClientAs(OracleUserFixture.TenantB), email);

        other.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task User_tenant_A_tidak_terlihat_dari_tenant_B()
    {
        var created = await (await CreateAsync(fixture.ClientAs(OracleUserFixture.TenantA), NewEmail("rahasia")))
            .Content.ReadFromJsonAsync<UserBody>();
        var tenantB = fixture.ClientAs(OracleUserFixture.TenantB);

        (await tenantB.GetAsync($"/api/v1/users/{created!.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await tenantB.GetFromJsonAsync<PagedBody>("/api/v1/users?search=rahasia");
        list!.Items.Should().NotContain(u => u.Id == created.Id);

        var update = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{created.Id}")
        {
            Content = JsonContent.Create(new { fullName = "Diubah tenant lain" }),
        };
        update.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        (await tenantB.SendAsync(update)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_menolak_size_di_atas_100_dan_mengembalikan_bentuk_paging()
    {
        var client = fixture.ClientAs(OracleUserFixture.TenantA);

        (await client.GetAsync("/api/v1/users?size=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await client.GetFromJsonAsync<PagedBody>("/api/v1/users?page=1&size=100");
        ok!.Page.Should().Be(1);
        ok.Size.Should().Be(100);
        ok.Items.Count.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task List_mencari_berdasarkan_nama_atau_email()
    {
        var client = fixture.ClientAs(OracleUserFixture.TenantA);
        var marker = Guid.NewGuid().ToString("N")[..10];
        await CreateAsync(client, NewEmail(), $"Cari {marker}");

        var result = await client.GetFromJsonAsync<PagedBody>($"/api/v1/users?search={marker.ToUpperInvariant()}");

        result!.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Update_dengan_If_Match_basi_mengembalikan_409()
    {
        var client = fixture.ClientAs(OracleUserFixture.TenantA);
        var created = await (await CreateAsync(client, NewEmail())).Content.ReadFromJsonAsync<UserBody>();

        HttpRequestMessage Update(string name, long version)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{created!.Id}") { Content = JsonContent.Create(new { fullName = name }) };
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            return request;
        }

        var first = await client.SendAsync(Update("Nama Baru", created!.RowVersion));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await client.SendAsync(Update("Nama Basi", created.RowVersion));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_tanpa_If_Match_mengembalikan_400()
    {
        var client = fixture.ClientAs(OracleUserFixture.TenantA);
        var created = await (await CreateAsync(client, NewEmail())).Content.ReadFromJsonAsync<UserBody>();

        var response = await client.PutAsJsonAsync($"/api/v1/users/{created!.Id}", new { fullName = "Tanpa Versi" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Status_dapat_dinonaktifkan_dan_diaktifkan_kembali()
    {
        var client = fixture.ClientAs(OracleUserFixture.TenantA);
        var created = await (await CreateAsync(client, NewEmail())).Content.ReadFromJsonAsync<UserBody>();

        HttpRequestMessage Patch(string status, long version)
        {
            var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/users/{created!.Id}/status") { Content = JsonContent.Create(new { status }) };
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            return request;
        }

        var inactive = await (await client.SendAsync(Patch("inactive", created!.RowVersion))).Content.ReadFromJsonAsync<UserBody>();
        inactive!.Status.Should().Be("inactive");

        var active = await (await client.SendAsync(Patch("active", inactive.RowVersion))).Content.ReadFromJsonAsync<UserBody>();
        active!.Status.Should().Be("active");

        var outbox = await fixture.ScalarAsync(
            $"SELECT COUNT(*) FROM OUTBOX_MESSAGES WHERE TYPE LIKE '%UserDeactivatedDomainEvent%' AND PAYLOAD LIKE '%{created.Id.ToString("D").ToLowerInvariant()}%'");
        Convert.ToInt32(outbox).Should().Be(1);
    }

    [Fact]
    public async Task Admin_tidak_dapat_menonaktifkan_akun_sendiri()
    {
        var self = fixture.ClientAs(OracleUserFixture.TenantA); // admin tenant itu sendiri
        var created = await self.GetFromJsonAsync<UserBody>($"/api/v1/users/{OracleUserFixture.AdminId(OracleUserFixture.TenantA)}");

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/users/{created!.Id}/status") { Content = JsonContent.Create(new { status = "inactive" }) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{created.RowVersion}\"");

        (await self.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Tanpa_permission_user_manage_endpoint_kelola_mengembalikan_403_tetapi_baca_boleh()
    {
        var member = fixture.MemberClient(OracleUserFixture.TenantA);

        (await CreateAsync(member, NewEmail())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync("/api/v1/users")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tanpa_autentikasi_mengembalikan_401()
    {
        var anonymous = fixture.Factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_mengembalikan_profil_user_yang_login()
    {
        var admin = fixture.ClientAs(OracleUserFixture.TenantA);
        var created = await (await CreateAsync(admin, NewEmail("saya"))).Content.ReadFromJsonAsync<UserBody>();
        var me = fixture.ClientAs(OracleUserFixture.TenantA, userId: created!.Id);

        var profile = await me.GetFromJsonAsync<UserBody>("/api/v1/users/me");

        profile!.Id.Should().Be(created.Id);
    }
}
