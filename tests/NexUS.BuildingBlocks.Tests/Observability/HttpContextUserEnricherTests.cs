using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NexUS.BuildingBlocks.Observability;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace NexUS.BuildingBlocks.Tests.Observability;

public class HttpContextUserEnricherTests
{
    private sealed class CapturingSink : ILogEventSink
    {
        public LogEvent? Last { get; private set; }
        public void Emit(LogEvent logEvent) => Last = logEvent;
    }

    private static LogEvent Log(ClaimsPrincipal? user)
    {
        var accessor = new HttpContextAccessor();
        if (user is not null) accessor.HttpContext = new DefaultHttpContext { User = user };
        var sink = new CapturingSink();
        new LoggerConfiguration().Enrich.With(new HttpContextUserEnricher(accessor)).WriteTo.Sink(sink).CreateLogger()
            .Information("pesan");
        return sink.Last!;
    }

    [Fact]
    public void User_terautentikasi_menambahkan_TenantId_dan_UserId_dari_klaim_tid_dan_sub()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", "7"), new Claim("sub", "user-1")], "Bearer"));

        var logEvent = Log(user);

        logEvent.Properties["TenantId"].ToString().Should().Be("\"7\"");
        logEvent.Properties["UserId"].ToString().Should().Be("\"user-1\"");
    }

    [Fact]
    public void Tanpa_request_atau_user_anonim_tidak_menambahkan_properti()
    {
        Log(null).Properties.Should().NotContainKey("TenantId");
        Log(new ClaimsPrincipal(new ClaimsIdentity())).Properties.Should().NotContainKey("UserId");
    }
}
