using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NexUS.Sop.Application.Common.Behaviors;

namespace NexUS.Sop.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddSopApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(AssemblyReference.Assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(AssemblyReference.Assembly);
        return services;
    }
}
