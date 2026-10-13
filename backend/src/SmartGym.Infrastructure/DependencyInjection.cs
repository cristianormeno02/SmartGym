using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Infrastructure.Persistence;
using SmartGym.Infrastructure.Services.Auth;
using SmartGym.Infrastructure.Services.FileStorage;

namespace SmartGym.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Falta la cadena de conexión 'ConnectionStrings:DefaultConnection'. Configurala con user-secrets o variables de entorno.");
        }

        services.AddDbContext<SmartGymDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(SmartGymDbContext).Assembly.FullName);
            }));

        services.AddScoped<ISmartGymDbContext>(provider => provider.GetRequiredService<SmartGymDbContext>());
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // File Storage Configuration
        services.Configure<S3StorageOptions>(configuration.GetSection(S3StorageOptions.SectionName));
        var s3Options = configuration.GetSection(S3StorageOptions.SectionName).Get<S3StorageOptions>() ?? new S3StorageOptions();

        if (s3Options.UseLocalStorage)
        {
            services.AddSingleton<IFileStorageService, LocalStorageService>();
        }
        else
        {
            services.AddSingleton<IFileStorageService, S3StorageService>();
        }

        // Public Storage Configuration
        services.Configure<PublicS3StorageOptions>(configuration.GetSection(PublicS3StorageOptions.SectionName));
        var publicS3Options = configuration.GetSection(PublicS3StorageOptions.SectionName).Get<PublicS3StorageOptions>() ?? new PublicS3StorageOptions();

        if (publicS3Options.UseLocalStorage)
        {
            services.AddSingleton<IPublicFileStorageService, LocalPublicStorageService>();
        }
        else
        {
            services.AddSingleton<IPublicFileStorageService, PublicS3StorageService>();
        }

        var imageProcessor = new ProfileImageProcessor();
        services.AddSingleton<IProfileImageProcessor>(imageProcessor);
        services.AddSingleton<IImageProcessor>(imageProcessor);

        // Authentication & JWT Services
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        // Google OAuth Services
        services.Configure<GoogleOptions>(configuration.GetSection(GoogleOptions.SectionName));
        services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();

        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        if (Encoding.UTF8.GetByteCount(jwtOptions.SecretKey) < JwtOptions.MinSecretKeyBytes)
        {
            throw new InvalidOperationException(
                $"Falta 'Jwt:SecretKey' o tiene menos de {JwtOptions.MinSecretKeyBytes} bytes. Configurala con user-secrets o variables de entorno.");
        }
        var key = Encoding.UTF8.GetBytes(jwtOptions.SecretKey);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });

        // Authorization Policies
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.RequireAdministrator, policy =>
                policy.RequireRole(Roles.Administrator));

            options.AddPolicy(Policies.RequireSecretary, policy =>
                policy.RequireRole(Roles.Secretary, Roles.Administrator));

            options.AddPolicy(Policies.RequireInstructor, policy =>
                policy.RequireRole(Roles.Instructor, Roles.Administrator));

            options.AddPolicy(Policies.RequireStudent, policy =>
                policy.RequireRole(Roles.Student));

            options.AddPolicy(Policies.RequireStaff, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));

            options.AddPolicy(Policies.RequireTeachingAndStaff, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary, Roles.Instructor));

            options.AddPolicy(Policies.RequireAllAuthenticated, policy =>
                policy.RequireAuthenticatedUser());

            options.AddPolicy(Policies.MembershipsRead, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.MembershipsCreate, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.MembershipsActivate, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.MembershipsSuspend, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.MembershipsReactivate, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.MembershipsCancel, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.MembershipsRenew, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.MembershipsManage, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));

            options.AddPolicy(Policies.ReservationsRead, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.ReservationsCreate, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary, Roles.Student));
            options.AddPolicy(Policies.ReservationsCancel, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary, Roles.Student));
            options.AddPolicy(Policies.ReservationsManage, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.ReservationsWaitlistRead, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary, Roles.Instructor));
            options.AddPolicy(Policies.ReservationsOverridePolicy, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));

            options.AddPolicy(Policies.PromotionsRead, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.PromotionsManage, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.PromotionsRedemptionsRead, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));

            options.AddPolicy(Policies.PaymentsRead, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.PaymentsCreate, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.PaymentsConfirm, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.PaymentsCancel, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.PaymentsAllocate, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            // Las devoluciones quedan reservadas al administrador
            options.AddPolicy(Policies.PaymentsRefund, policy =>
                policy.RequireRole(Roles.Administrator));
            // Conciliar y resolver discrepancias con la pasarela, también
            options.AddPolicy(Policies.PaymentsReconcile, policy =>
                policy.RequireRole(Roles.Administrator));

            options.AddPolicy(Policies.NotificationsReadOwn, policy =>
                policy.RequireAuthenticatedUser());
            options.AddPolicy(Policies.NotificationsMarkReadOwn, policy =>
                policy.RequireAuthenticatedUser());
            options.AddPolicy(Policies.NotificationsPreferencesManage, policy =>
                policy.RequireAuthenticatedUser());
            options.AddPolicy(Policies.NotificationsReadAll, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.NotificationsManage, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.NotificationsDeliveryRead, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.NotificationsRetryFailed, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.NotificationsTemplatesRead, policy =>
                policy.RequireRole(Roles.Administrator, Roles.Secretary));
            options.AddPolicy(Policies.NotificationsTemplatesManage, policy =>
                policy.RequireRole(Roles.Administrator));
        });

        // Notifications Options & Providers
        services.Configure<SmartGym.Application.Common.NotificationsOptions>(
            configuration.GetSection(SmartGym.Application.Common.NotificationsOptions.SectionName));
        services.Configure<SmartGym.Application.Common.EmailNotificationOptions>(
            configuration.GetSection(SmartGym.Application.Common.EmailNotificationOptions.SectionName));
        services.AddScoped<SmartGym.Application.Modules.Operations.Services.IEmailNotificationProvider, SmartGym.Infrastructure.Services.Notifications.ConfigurableEmailNotificationProvider>();
        services.AddScoped<SmartGym.Application.Modules.Operations.Services.INotificationChannelProvider>(sp =>
            sp.GetRequiredService<SmartGym.Application.Modules.Operations.Services.IEmailNotificationProvider>());

        // Proveedores de pago: sin credenciales el proveedor no se registra y el checkout responde PROVIDER_NOT_SUPPORTED
        services.Configure<SmartGym.Application.Common.PaymentsOptions>(
            configuration.GetSection(SmartGym.Application.Common.PaymentsOptions.SectionName));
        services.Configure<SmartGym.Infrastructure.Services.Payments.MercadoPagoOptions>(
            configuration.GetSection(SmartGym.Infrastructure.Services.Payments.MercadoPagoOptions.SectionName));

        var mercadoPagoOptions = configuration
            .GetSection(SmartGym.Infrastructure.Services.Payments.MercadoPagoOptions.SectionName)
            .Get<SmartGym.Infrastructure.Services.Payments.MercadoPagoOptions>() ?? new SmartGym.Infrastructure.Services.Payments.MercadoPagoOptions();

        if (mercadoPagoOptions.IsConfigured)
        {
            services.AddSingleton<SmartGym.Application.Modules.Operations.Interfaces.IPaymentProvider>(sp =>
                new SmartGym.Infrastructure.Services.Payments.MercadoPagoPaymentProvider(
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmartGym.Infrastructure.Services.Payments.MercadoPagoOptions>>(),
                    new HttpClient
                    {
                        BaseAddress = new Uri(mercadoPagoOptions.BaseUrl),
                        Timeout = TimeSpan.FromSeconds(Math.Max(1, mercadoPagoOptions.TimeoutSeconds))
                    },
                    sp.GetService<TimeProvider>()));
        }

        services.AddHostedService<SmartGym.Infrastructure.Services.Auth.AdminUserSeedHostedService>();

        // Background Workers
        services.AddHostedService<SmartGym.Infrastructure.Services.BackgroundJobs.PaymentExpirationWorker>();
        services.AddHostedService<SmartGym.Infrastructure.Services.BackgroundJobs.ClassSessionGenerationWorker>();
        services.AddHostedService<SmartGym.Infrastructure.Services.BackgroundJobs.MembershipExpirationWorker>();
        services.AddHostedService<SmartGym.Infrastructure.Services.BackgroundJobs.ReservationWaitlistWorker>();
        services.AddHostedService<SmartGym.Infrastructure.Services.Notifications.NotificationDeliveryWorker>();
        services.AddHostedService<SmartGym.Infrastructure.Services.Notifications.ClassReminderWorker>();

        return services;
    }
}
