using Microsoft.Extensions.FileProviders;

namespace SmartGym.WebApi.Common;

/// <summary>
/// Expone por HTTP únicamente la carpeta de avatares del almacenamiento local (desarrollo).
/// El resto del almacenamiento, como los certificados médicos, nunca se sirve como archivo estático.
/// </summary>
public static class LocalAvatarStaticFiles
{
    public const string RequestPath = "/storage/avatars";

    public static StaticFileOptions CreateOptions(string storageRoot)
    {
        var avatarsRoot = Path.Combine(storageRoot, "avatars");
        Directory.CreateDirectory(avatarsRoot);

        return new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(avatarsRoot),
            RequestPath = RequestPath
        };
    }
}
