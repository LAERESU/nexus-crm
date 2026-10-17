using Microsoft.Extensions.DependencyInjection;

namespace NexUS.Audit.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Titik registrasi Oracle, Redis, dan messaging; diisi saat story fitur service ini dikerjakan.</summary>
    public static IServiceCollection AddAuditInfrastructure(this IServiceCollection services) => services;
}
