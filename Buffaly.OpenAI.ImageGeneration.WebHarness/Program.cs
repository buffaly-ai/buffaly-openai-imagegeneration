using System.Text.Json;
using Buffaly.Agent.Host;
using Buffaly.OpenAI.ImageGeneration.WebHarness;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();
var standaloneRootDirectory = Path.Combine("C:\\temp", "generated-images", "web-harness");
SetConnectionString(app.Configuration);

app.UseDefaultFiles();
app.UseStaticFiles();
var module = new ImageGenerationHarnessModule();
module.Configure(app.Environment.ContentRootPath, app.Environment.WebRootPath ?? string.Empty);
ImageGenerationHarnessModule.RegisterStandaloneRoutes(app);

app.MapGet("/api/image-harness/default-root", () => Results.Json(new { rootDirectory = standaloneRootDirectory }));

app.MapGet("/api/image-harness/diagnostics", () =>
{
	try
	{
		var rootImageCount = Directory.Exists(standaloneRootDirectory)
			? Directory.GetFiles(standaloneRootDirectory, "*", SearchOption.TopDirectoryOnly).Count(IsImageFile)
			: 0;
		var feature = OpenAIFeature.Feature;
		return Results.Json(new
		{
			rootDirectory = standaloneRootDirectory,
			rootExists = Directory.Exists(standaloneRootDirectory),
			rootImageCount,
			hasApiKey = !string.IsNullOrWhiteSpace(feature.ApiKey)
		});
	}
	catch (Exception ex)
	{
		return Results.Json(new
		{
			rootDirectory = standaloneRootDirectory,
			rootExists = Directory.Exists(standaloneRootDirectory),
			errorType = ex.GetType().FullName,
			error = ex.Message,
			innerErrorType = ex.InnerException?.GetType().FullName,
			innerError = ex.InnerException?.Message
		}, statusCode: StatusCodes.Status500InternalServerError);
	}
});

app.MapGet("/api/local-file-image", (string path) =>
{
	var resolvedPath = Path.GetFullPath(path ?? string.Empty);
	if (!System.IO.File.Exists(resolvedPath) || !IsImageFile(resolvedPath))
	{
		return Results.NotFound();
	}

	return Results.File(resolvedPath, GetContentType(resolvedPath));
});

app.Run();

static string GetContentType(string filePath)
{
	return Path.GetExtension(filePath).ToLowerInvariant() switch
	{
		".jpg" or ".jpeg" => "image/jpeg",
		".webp" => "image/webp",
		_ => "image/png"
	};
}

static bool IsImageFile(string filePath)
{
	return Path.GetExtension(filePath).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";
}

static void SetConnectionString(IConfiguration config)
{
	var connectionString = config.GetConnectionString("buffaly_sessions.readwrite");
	if (string.IsNullOrWhiteSpace(connectionString))
	{
		throw new InvalidOperationException("ConnectionStrings:buffaly_sessions.readwrite is required for standalone image harness feature loading.");
	}

	Buffaly.Sessions.DB.DataAccess.SetConnectionString(connectionString);
	Buffaly.Data.DataAccess.SetConnectionString(connectionString);
}

