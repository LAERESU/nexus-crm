using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NexUS.User.Domain.Roles;

namespace NexUS.User.UnitTests;

public class PolicyCatalogTests
{
    private static IEnumerable<Type> Controllers() => typeof(NexUS.User.Api.Policies).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    [Fact]
    public void Setiap_endpoint_punya_policy_yang_dikenal_katalog_permission_atau_authenticated()
    {
        // Salah ketik nama policy tidak boleh lolos: policy dinamis menolak semua user, bukan gagal saat startup.
        var policies = Controllers()
            .SelectMany(t => t.GetCustomAttributes<AuthorizeAttribute>().Concat(
                t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>())))
            .Select(a => a.Policy ?? string.Empty)
            .Distinct()
            .ToList();
        var known = DefaultRoleCatalog.Permissions.Select(p => p.Code).Append(NexUS.User.Api.Policies.Authenticated);

        policies.Should().NotBeEmpty().And.BeSubsetOf(known);
    }

    [Fact]
    public void Tidak_ada_endpoint_tanpa_policy()
    {
        var actions = Controllers().SelectMany(t => t
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().Any(a => a is HttpMethodAttribute))
            .Select(m => (Type: t, Method: m)));

        foreach (var (type, method) in actions)
            (method.GetCustomAttributes<AuthorizeAttribute>().Any() || type.GetCustomAttributes<AuthorizeAttribute>().Any())
                .Should().BeTrue($"{type.Name}.{method.Name} wajib punya [Authorize(Policy = ...)]");
    }
}
