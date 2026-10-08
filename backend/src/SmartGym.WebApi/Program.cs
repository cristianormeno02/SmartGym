using SmartGym.Application;
using SmartGym.Infrastructure;
using SmartGym.Infrastructure.Services.FileStorage;
using SmartGym.WebApi.Common;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();

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
app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }))
   .WithName("HealthCheck");

app.Run();
