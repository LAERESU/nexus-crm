using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NexUS.Tasks.Application.Common.Behaviors;

namespace NexUS.Tasks.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddTaskApplication(this IServiceCollection services)
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
