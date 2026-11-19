using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nichotan.Application.Applications;
using Nichotan.Application.Common;
using Nichotan.Infrastructure.Pdf;
using Nichotan.Application.Identity;
using Nichotan.Infrastructure.Identity;
using Nichotan.Infrastructure.Persistence;
using Nichotan.Infrastructure.Services;
using Nichotan.Infrastructure.Storage;

namespace Nichotan.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Database"), npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory")));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            // Only the authenticator-app provider: link tokens (verification, reset, invitations) are our own hashed tokens.
            .AddTokenProvider<AuthenticatorTokenProvider<AppUser>>(TokenOptions.DefaultAuthenticatorProvider);

        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.Configure<SessionOptions>(configuration.GetSection("Session"));
        services.Configure<SecurityOptions>(configuration.GetSection("Security"));
        services.Configure<EnquiryOptions>(configuration.GetSection("Enquiries"));
        services.Configure<AdmissionNotificationOptions>(configuration.GetSection("Admissions"));
        services.Configure<AppOptions>(configuration.GetSection("App"));
        services.Configure<Nichotan.Application.Reminders.ReminderOptions>(configuration.GetSection("Reminders"));
        services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));
        services.Configure<AcsEmailOptions>(configuration.GetSection("Email:Acs"));
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.Configure<BootstrapOptions>(configuration.GetSection("Bootstrap"));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAppUrls, AppUrls>();
        // Outgoing email: SMTP (default; Mailpit locally) or Azure Communication Services. Either way it is wrapped by the message log.
        if (string.Equals(configuration["Email:Provider"], "Acs", StringComparison.OrdinalIgnoreCase))
        {
            services.AddKeyedScoped<IEmailSender, AcsEmailSender>(LoggingEmailSender.InnerKey);
        }
        else
        {
            services.AddKeyedScoped<IEmailSender, SmtpEmailSender>(LoggingEmailSender.InnerKey);
        }
        services.AddScoped<IEmailSender, LoggingEmailSender>();
        if (string.Equals(configuration["Storage:Provider"], "AzureBlob", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<AzureBlobObjectStorage>();
            services.AddSingleton<IObjectStorage>(sp => sp.GetRequiredService<AzureBlobObjectStorage>());
            services.AddSingleton<IStorageInitializer>(sp => sp.GetRequiredService<AzureBlobObjectStorage>());
        }
        else
        {
            services.AddSingleton<S3ObjectStorage>();
            services.AddSingleton<IObjectStorage>(sp => sp.GetRequiredService<S3ObjectStorage>());
            services.AddSingleton<IStorageInitializer>(sp => sp.GetRequiredService<S3ObjectStorage>());
        }
        services.AddScoped<DbInitializer>();
        services.AddSingleton<IApplicationFormPdf, ApplicationFormPdf>();
        return services;
    }
}
