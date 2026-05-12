using Buffaly.Agent.Web.Common;
using Microsoft.AspNetCore.Builder;

namespace Buffaly.OpenAI.ImageGeneration.WebHarness;

public sealed class ImageGenerationHarnessModule : IBuffalyWebModule
{
    public string ModuleName => "OpenAIImageGeneration";

    public void Configure(string contentRootPath, string webRootPath)
    {
        _ = contentRootPath;
        _ = webRootPath;
    }

    public void InstallArtifacts(string contentRootPath, string webRootPath)
    {
        _ = contentRootPath;
        _ = webRootPath;
    }

    public void MapRoutes(WebApplication app)
    {
        _ = app;
    }

    public static void RegisterStandaloneRoutes(WebApplication app)
    {
        WebAppUtilities.JsonWsOptions jsonWsOptions = new();
        Buffaly.Common.JsonWsHandlerService.RegisterApis(app, jsonWsOptions, new[] { "*.json" });
    }
}