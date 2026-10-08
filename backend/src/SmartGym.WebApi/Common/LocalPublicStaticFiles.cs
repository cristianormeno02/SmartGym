using Microsoft.Extensions.FileProviders;

namespace SmartGym.WebApi.Common;

/// <summary>
/// Expone por HTTP los archivos estáticos de medios públicos locales (desarrollo).
/// </summary>
public static class LocalPublicStaticFiles
{
    public const string RequestPath = "/public-media";

    public static StaticFileOptions CreateOptions(string storageRoot)
    {
        Directory.CreateDirectory(storageRoot);

        return new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(storageRoot),
            RequestPath = RequestPath
        };
    }
}
