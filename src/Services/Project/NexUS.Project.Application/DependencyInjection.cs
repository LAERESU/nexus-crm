using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NexUS.Project.Application.Common.Behaviors;

namespace NexUS.Project.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddProjectApplication(this IServiceCollection services)
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
