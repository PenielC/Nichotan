using System.Reflection;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Nichotan.Application.Applications;
using Nichotan.Application.Common;
using Nichotan.Application.Identity;

namespace Nichotan.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.PipelineBehaviors = [typeof(ValidationBehavior<,>)];
        });
        services.AddScoped<SessionIssuer>();
        services.AddScoped<Reminders.ReminderService>();
        // The built-in drawn signature. To use a third-party e-signature provider, register its IDeclarationSigner instead.
        services.AddSingleton<IDeclarationSigner, NativeDeclarationSigner>();
        return services;
    }
}
