using System.Globalization;
using System.Text.Json;

namespace Buffaly.OpenAI.ImageGeneration;

public sealed class OpenAIImageGenerationRuntimeOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public string ArtifactRelativeDirectory { get; set; } = "artifacts/images";
}

public sealed class OpenAIImageCreateRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = string.Empty;
    public string OutputCompression { get; set; } = string.Empty;
}

public sealed class OpenAIImageEditRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string ImageFilePath { get; set; } = string.Empty;
    public string MaskFilePath { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = string.Empty;
    public string OutputCompression { get; set; } = string.Empty;
}

public sealed class OpenAIImageOperationResult
{
    public string OutputFilePath { get; set; } = string.Empty;
    public string ArtifactRelativePath { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = string.Empty;
    public string RawJson { get; set; } = string.Empty;
}

public static class OpenAIImageGenerationServiceRuntime
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private static OpenAIImageGenerationRuntimeOptions? s_options;

	public static string CreateImageFromParameters(string apiKey, string outputDirectory, string artifactRelativeDirectory, string prompt, string size, string model, string quality, string background, string outputFormat, string outputCompression)
	{
		Configure(apiKey, outputDirectory, artifactRelativeDirectory);
		var request = new OpenAIImageCreateRequest
		{
			Prompt = prompt,
			Size = size,
			Model = model,
			Quality = quality,
			Background = background,
			OutputFormat = outputFormat,
			OutputCompression = outputCompression
		};
		return CreateImage(request);
	}

	public static string EditImageFromParameters(string apiKey, string outputDirectory, string artifactRelativeDirectory, string prompt, string imageFilePath, string maskFilePath, string size, string model, string quality, string background, string outputFormat, string outputCompression)
	{
		Configure(apiKey, outputDirectory, artifactRelativeDirectory);
		var request = new OpenAIImageEditRequest
		{
			Prompt = prompt,
			ImageFilePath = imageFilePath,
			MaskFilePath = maskFilePath,
			Size = size,
			Model = model,
			Quality = quality,
			Background = background,
			OutputFormat = outputFormat,
			OutputCompression = outputCompression
		};
		return EditImage(request);
	}

	public static string Configure(string apiKey, string outputDirectory, string artifactRelativeDirectory)
	{
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("OpenAI feature ApiKey is required.", nameof(apiKey));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        s_options = new OpenAIImageGenerationRuntimeOptions
        {
            ApiKey = apiKey.Trim(),
            OutputDirectory = Path.GetFullPath(outputDirectory.Trim()),
            ArtifactRelativeDirectory = string.IsNullOrWhiteSpace(artifactRelativeDirectory)
                ? "artifacts/images"
                : artifactRelativeDirectory.Trim().Replace('\\', '/')
        };

        Directory.CreateDirectory(s_options.OutputDirectory);
        return "configured";
    }

    public static string CreateImage(OpenAIImageCreateRequest request)
    {
        OpenAIImageGenerationRuntimeOptions options = RequireConfigured();
        ValidateCreateRequest(request);

        string outputFormat = NormalizeOutputFormat(request.OutputFormat);
        string model = NormalizeModel(request.Model);
        string size = NormalizeOption(request.Size, "1024x1024");
        string quality = NormalizeOption(request.Quality, "low");
        string background = NormalizeOption(request.Background, "opaque");
        string outputFilePath = BuildOutputPath(options.OutputDirectory, "created", model, outputFormat);

        string rawJson = OpenAIImageGenerator.GenerateImageToFile(
            options.ApiKey,
            request.Prompt.Trim(),
            outputFilePath,
            size,
            model,
            quality,
            background,
            outputFormat,
            NormalizeOptional(request.OutputCompression));

        return Serialize(ParseSuccessfulResult(rawJson, outputFilePath, options.ArtifactRelativeDirectory, "create", model, size, outputFormat));
    }

    public static string EditImage(OpenAIImageEditRequest request)
    {
        OpenAIImageGenerationRuntimeOptions options = RequireConfigured();
        ValidateEditRequest(request);

        string outputFormat = NormalizeOutputFormat(request.OutputFormat);
        string model = NormalizeModel(request.Model);
        string size = NormalizeOption(request.Size, "1024x1024");
        string quality = NormalizeOption(request.Quality, "low");
        string background = NormalizeOption(request.Background, "opaque");
        string outputFilePath = BuildOutputPath(options.OutputDirectory, "edited", model, outputFormat);

        string rawJson = OpenAIImageGenerator.EditImageToFile(
            options.ApiKey,
            request.Prompt.Trim(),
            request.ImageFilePath.Trim(),
            outputFilePath,
            NormalizeOptional(request.MaskFilePath),
            size,
            model,
            quality,
            background,
            outputFormat,
            NormalizeOptional(request.OutputCompression));

        return Serialize(ParseSuccessfulResult(rawJson, outputFilePath, options.ArtifactRelativeDirectory, "edit", model, size, outputFormat));
    }

    public static string BuildOutputPath(string outputDirectory, string operation, string model, string outputFormat)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        string safeOperation = SanitizeFilePart(operation);
        string safeModel = SanitizeFilePart(NormalizeModel(model));
        string extension = NormalizeOutputExtension(outputFormat);
        return Path.Combine(Path.GetFullPath(outputDirectory.Trim()), timestamp + "-" + safeOperation + "-" + safeModel + "." + extension);
    }

    public static string NormalizeModel(string? model)
    {
        string normalized = NormalizeOption(model, "gpt-image-1.5").ToLowerInvariant();
        return normalized is "gpt-image-1.5" or "gpt-image-2" ? normalized : "gpt-image-1.5";
    }

    public static string NormalizeOutputFormat(string? outputFormat)
    {
        string normalized = NormalizeOption(outputFormat, "png").ToLowerInvariant();
        return normalized is "jpg" or "jpeg" ? "jpeg" : normalized is "webp" ? "webp" : "png";
    }

    public static string NormalizeOutputExtension(string? outputFormat)
    {
        return NormalizeOutputFormat(outputFormat) == "jpeg" ? "jpg" : NormalizeOutputFormat(outputFormat);
    }

    private static OpenAIImageGenerationRuntimeOptions RequireConfigured()
    {
        if (s_options == null)
        {
            throw new InvalidOperationException("OpenAI image generation runtime is not configured. Call Configure before CreateImage or EditImage.");
        }

        return s_options;
    }

    private static void ValidateCreateRequest(OpenAIImageCreateRequest? request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(request));
        }
    }

    private static void ValidateEditRequest(OpenAIImageEditRequest? request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ImageFilePath))
        {
            throw new ArgumentException("ImageFilePath is required.", nameof(request));
        }
    }

    private static OpenAIImageOperationResult ParseSuccessfulResult(string rawJson, string fallbackOutputFilePath, string artifactRelativeDirectory, string operation, string model, string size, string outputFormat)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawJson);
            JsonElement root = document.RootElement;
            bool success = root.TryGetProperty("success", out JsonElement successElement) && successElement.ValueKind == JsonValueKind.True;
            if (!success)
            {
                string error = ReadString(root, "error", "OpenAI image operation failed.");
                throw new InvalidOperationException(error);
            }

            string outputFilePath = ReadString(root, "outputFilePath", fallbackOutputFilePath);
            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                throw new InvalidOperationException("OpenAI image operation did not return outputFilePath.");
            }

            return new OpenAIImageOperationResult
            {
                OutputFilePath = outputFilePath,
                ArtifactRelativePath = CombineArtifactRelativePath(artifactRelativeDirectory, Path.GetFileName(outputFilePath)),
                Operation = operation,
                Model = model,
                Size = size,
                OutputFormat = outputFormat,
                RawJson = rawJson
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("OpenAI image operation returned invalid JSON.", ex);
        }
    }

    private static string CombineArtifactRelativePath(string artifactRelativeDirectory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("fileName is required.", nameof(fileName));
        }

        string directory = string.IsNullOrWhiteSpace(artifactRelativeDirectory) ? "artifacts/images" : artifactRelativeDirectory.Trim().Replace('\\', '/').Trim('/');
        return directory + "/" + fileName;
    }

    private static string Serialize(OpenAIImageOperationResult result)
    {
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private static string NormalizeOption(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string ReadString(JsonElement element, string propertyName, string fallback)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static string SanitizeFilePart(string value)
    {
        char[] chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        string text = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(text) ? "image" : text;
    }
}
