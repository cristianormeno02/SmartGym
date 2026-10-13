using SmartGym.Application;
using SmartGym.Infrastructure;
using SmartGym.Infrastructure.Services.FileStorage;
using SmartGym.WebApi.Common;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

builder.Services.AddOptions<SmartGym.Application.Common.GymOptions>()
    .Bind(builder.Configuration.GetSection(SmartGym.Application.Common.GymOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(opts =>
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(opts.TimeZoneId);
            return true;
        }
        catch
        {
            return false;
        }
    }, "TimeZoneId must be a valid IANA or Windows TimeZone identifier")
    .ValidateOnStart();

builder.Services.AddOptions<SmartGym.Application.Common.ClassSessionsOptions>()
    .Bind(builder.Configuration.GetSection(SmartGym.Application.Common.ClassSessionsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SmartGym.Application.Common.MembershipsOptions>()
    .Bind(builder.Configuration.GetSection(SmartGym.Application.Common.MembershipsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SmartGym.Application.Common.ReservationsOptions>()
    .Bind(builder.Configuration.GetSection(SmartGym.Application.Common.ReservationsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SmartGym.Application.Common.PaymentsOptions>()
    .Bind(builder.Configuration.GetSection(SmartGym.Application.Common.PaymentsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Opcional: sólo se configura (user-secrets o variables de entorno) para crear el administrador inicial.
builder.Services.AddOptions<SmartGym.Application.Common.AdminSeedOptions>()
    .Bind(builder.Configuration.GetSection(SmartGym.Application.Common.AdminSeedOptions.SectionName));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();

// El webhook de pagos es anónimo: se limita por origen para acotar el costo de verificar firmas
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(SmartGym.WebApi.Controllers.PaymentsController.WebhookRateLimitPolicy, httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Con almacenamiento local (desarrollo) las fotos de perfil se sirven desde la propia API.
var storageOptions = builder.Configuration.GetSection(S3StorageOptions.SectionName).Get<S3StorageOptions>() ?? new S3StorageOptions();
if (storageOptions.UseLocalStorage)
{
    app.UseStaticFiles(LocalAvatarStaticFiles.CreateOptions(LocalStorageService.DefaultRootPath));
}

var publicStorageOptions = builder.Configuration.GetSection(PublicS3StorageOptions.SectionName).Get<PublicS3StorageOptions>() ?? new PublicS3StorageOptions();
if (publicStorageOptions.UseLocalStorage)
{
    app.UseStaticFiles(LocalPublicStaticFiles.CreateOptions(LocalPublicStorageService.DefaultRootPath));
}
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }))
   .WithName("HealthCheck");

app.Run();
