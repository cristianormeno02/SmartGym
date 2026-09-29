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
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Database=smartgym_db;Username=postgres;Password=postgres";

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

        // Authentication & JWT Services
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        // Google OAuth Services
        services.Configure<GoogleOptions>(configuration.GetSection(GoogleOptions.SectionName));
        services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();

        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
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
        });

        return services;
    }
}
