using System.Text.Json;
using Buffaly.OpenAI.ImageGeneration;
using Buffaly.Agent.Host;
using WebAppUtilities;

namespace Buffaly.OpenAI.ImageGeneration.WebHarness;

public sealed class ImageGenerationHarnessJsonWsService : JsonWs
{
    private static string s_apiKey = string.Empty;

    [JsonWsSerialize(SerializeResultsOptions.Full)]
    public static ImageHarnessConfigContract Initialize(ImageHarnessInitializeRequestContract request)
    {
        if (request == null) throw new JsonWsException("request is required.");
        s_apiKey = NormalizeOptionalText(request.ApiKey);
        return new ImageHarnessConfigContract
        {
            HasApiKey = !string.IsNullOrWhiteSpace(s_apiKey),
            RootDirectory = string.Empty,
            FileName = string.Empty,
            Models = BuildModelOptions(),
            Sizes = BuildSizeOptions(),
            Qualities = ["low", "medium", "high", "auto"],
            OutputFormats = ["png", "jpeg", "webp"]
        };
    }

    [JsonWsSerialize(SerializeResultsOptions.Full)]
    public static ImageHarnessConfigContract GetConfig(ImageHarnessContextRequestContract request)
    {
        var runtime = BuildRuntime(request);
        return new ImageHarnessConfigContract
        {
            HasApiKey = !string.IsNullOrWhiteSpace(runtime.ApiKey),
            RootDirectory = runtime.RootDirectory,
            FileName = NormalizeOptionalText(request?.FileName),
            Models = BuildModelOptions(),
            Sizes = BuildSizeOptions(),
            Qualities = ["low", "medium", "high", "auto"],
            OutputFormats = ["png", "jpeg", "webp"]
        };
    }

    [JsonWsSerialize(SerializeResultsOptions.Full)]
    public static ImageHarnessResultContract GenerateImage(ImageHarnessGenerateRequestContract request)
    {
        if (request == null) throw new JsonWsException("request is required.");
        var runtime = BuildRuntime(request);
        if (string.IsNullOrWhiteSpace(runtime.ApiKey)) return ImageHarnessResultContract.Fail("OpenAI API key was not found.");

        var outputFilePath = runtime.CreateOutputPath("generated", NormalizeModel(request.Model), NormalizeOutputFormat(request.OutputFormat));
        var rawResult = OpenAIImageGenerator.GenerateImageToFile(
            runtime.ApiKey,
            NormalizeRequiredText(request.Prompt, "Prompt"),
            outputFilePath,
            NormalizeOption(request.Size, "1024x1024"),
            NormalizeModel(request.Model),
            NormalizeOption(request.Quality, "low"),
            NormalizeOption(request.Background, "opaque"),
            NormalizeOutputFormat(request.OutputFormat),
            NormalizeOptionalText(request.OutputCompression));

        return ToContract(runtime.ToClientResult(rawResult));
    }

    [JsonWsSerialize(SerializeResultsOptions.Full)]
    public static ImageHarnessResultContract EditImage(ImageHarnessEditRequestContract request)
    {
        if (request == null) throw new JsonWsException("request is required.");
        var runtime = BuildRuntime(request);
        if (string.IsNullOrWhiteSpace(runtime.ApiKey)) return ImageHarnessResultContract.Fail("OpenAI API key was not found.");
        if (request.Images == null || request.Images.Length == 0) return ImageHarnessResultContract.Fail("At least one source image is required.");

        var imagePaths = request.Images
            .Where(image => image != null && !string.IsNullOrWhiteSpace(image.Base64Data))
            .Select((image, index) => runtime.SaveBase64Upload(image.Base64Data, image.FileName, "image-" + index))
            .ToArray();
        if (imagePaths.Length == 0) return ImageHarnessResultContract.Fail("At least one source image is required.");

        var maskPath = request.Mask == null || string.IsNullOrWhiteSpace(request.Mask.Base64Data)
            ? string.Empty
            : runtime.SaveBase64Upload(request.Mask.Base64Data, request.Mask.FileName, "mask");

        var outputFilePath = runtime.CreateOutputPath("edited", NormalizeModel(request.Model), NormalizeOutputFormat(request.OutputFormat));
        var rawResult = OpenAIImageGenerator.EditImageToFile(
            runtime.ApiKey,
            NormalizeRequiredText(request.Prompt, "Prompt"),
            imagePaths,
            outputFilePath,
            maskPath,
            NormalizeOption(request.Size, "1024x1024"),
            NormalizeModel(request.Model),
            NormalizeOption(request.Quality, "low"),
            NormalizeOption(request.Background, "opaque"),
            NormalizeOutputFormat(request.OutputFormat),
            NormalizeOptionalText(request.OutputCompression));

        var result = runtime.ToClientResult(rawResult);
        if (!string.IsNullOrWhiteSpace(maskPath))
        {
            result.Raw["debugUploads"] = new
            {
                Images = imagePaths.Select(path => new { FilePath = path, Url = runtime.CreateImageUrl(path) }).ToArray(),
                Mask = new { FilePath = maskPath, Url = runtime.CreateImageUrl(maskPath) }
            };
        }
        return ToContract(result);
    }

    [JsonWsSerialize(SerializeResultsOptions.Full)]
    public static ImageHarnessOutputsContract ListOutputs(ImageHarnessContextRequestContract request)
    {
        var runtime = BuildRuntime(request);
        return new ImageHarnessOutputsContract { Outputs = runtime.ListOutputs().ToArray() };
    }

    [JsonWsSerialize(SerializeResultsOptions.Full)]
    public static ImageHarnessDeleteOutputResultContract DeleteOutput(ImageHarnessDeleteOutputRequestContract request)
    {
        if (request == null) throw new JsonWsException("request is required.");
        var runtime = BuildRuntime(request);
        var fileName = NormalizeOptionalText(request.FileName);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = Path.GetFileName(NormalizeOptionalText(request.FilePath));
        if (string.IsNullOrWhiteSpace(fileName)) return ImageHarnessDeleteOutputResultContract.Fail("Output file name is required.");
        var deleted = runtime.DeleteGeneratedFile(fileName, request.FilePath);
        return deleted ? ImageHarnessDeleteOutputResultContract.Ok(fileName) : ImageHarnessDeleteOutputResultContract.Fail("Output image was not found or could not be deleted.");
    }

    private static ImageHarnessRuntime BuildRuntime(ImageHarnessContextRequestContract? request)
    {
        string apiKey = string.IsNullOrWhiteSpace(s_apiKey) ? OpenAIFeature.Feature.ApiKey : s_apiKey;
        string rootDirectory = string.IsNullOrWhiteSpace(request?.RootDirectory)
            ? GetDefaultRootDirectory()
            : request.RootDirectory;
        return ImageHarnessRuntime.Create(apiKey, rootDirectory);
    }

    // Keeps default page loads on the harness-owned output root when no context root is supplied.
    private static string GetDefaultRootDirectory()
    {
        return Path.Combine("C:\\temp", "generated-images", "web-harness");
    }

    private static ImageHarnessOptionContract[] BuildModelOptions()
    {
        return
        [
            new ImageHarnessOptionContract { Value = "gpt-image-1.5", Label = "GPT Image 1.5", SupportsFlexibleSize = false },
            new ImageHarnessOptionContract { Value = "gpt-image-2", Label = "GPT Image 2", SupportsFlexibleSize = true }
        ];
    }

    private static ImageHarnessOptionContract[] BuildSizeOptions()
    {
        return
        [
            new ImageHarnessOptionContract { Value = "1024x1024", Label = "Square 1024" },
            new ImageHarnessOptionContract { Value = "1024x1536", Label = "Portrait 1024x1536" },
            new ImageHarnessOptionContract { Value = "1536x1024", Label = "Landscape 1024x1536" },
            new ImageHarnessOptionContract { Value = "2048x2048", Label = "2K square (GPT Image 2)" },
            new ImageHarnessOptionContract { Value = "2048x1152", Label = "2K landscape (GPT Image 2)" },
            new ImageHarnessOptionContract { Value = "3840x2160", Label = "4K landscape (GPT Image 2)" },
            new ImageHarnessOptionContract { Value = "auto", Label = "Auto" }
        ];
    }

    private static ImageHarnessResultContract ToContract(HarnessResult result)
    {
        return new ImageHarnessResultContract
        {
            Success = result.Success,
            Error = result.Error,
            OutputFilePath = result.OutputFilePath,
            ImageUrl = result.ImageUrl,
            FileName = result.FileName,
            RawJson = JsonSerializer.Serialize(result.Raw)
        };
    }

    private static string NormalizeRequiredText(string? value, string name)
    {
        var normalized = NormalizeOptionalText(value);
        if (string.IsNullOrWhiteSpace(normalized)) throw new JsonWsException(name + " is required.");
        return normalized;
    }

    private static string NormalizeOptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    private static string NormalizeOption(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string NormalizeModel(string? value)
    {
        var normalized = NormalizeOption(value, "gpt-image-1.5").ToLowerInvariant();
        return normalized is "gpt-image-1.5" or "gpt-image-2" ? normalized : "gpt-image-1.5";
    }
    private static string NormalizeOutputFormat(string? value)
    {
        var normalized = NormalizeOption(value, "png").ToLowerInvariant();
        return normalized is "jpeg" or "jpg" or "webp" ? normalized : "png";
    }
}

public class ImageHarnessContextRequestContract
{
    public string RootDirectory { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

public sealed class ImageHarnessInitializeRequestContract
{
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class ImageHarnessConfigContract
{
    public bool HasApiKey { get; set; }
    public string RootDirectory { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public ImageHarnessOptionContract[] Models { get; set; } = [];
    public ImageHarnessOptionContract[] Sizes { get; set; } = [];
    public string[] Qualities { get; set; } = [];
    public string[] OutputFormats { get; set; } = [];
}

public sealed class ImageHarnessOptionContract
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool SupportsFlexibleSize { get; set; }
}

public class ImageHarnessGenerateRequestContract : ImageHarnessContextRequestContract
{
    public string Prompt { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = string.Empty;
    public string OutputCompression { get; set; } = string.Empty;
}

public sealed class ImageHarnessEditRequestContract : ImageHarnessGenerateRequestContract
{
    public ImageHarnessImagePayloadContract[] Images { get; set; } = [];
    public ImageHarnessImagePayloadContract? Mask { get; set; }
}

public sealed class ImageHarnessImagePayloadContract
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Base64Data { get; set; } = string.Empty;
}

public sealed class ImageHarnessResultContract
{
    public bool Success { get; set; }
    public string Error { get; set; } = string.Empty;
    public string OutputFilePath { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RawJson { get; set; } = string.Empty;
    public static ImageHarnessResultContract Fail(string error) => new() { Success = false, Error = error, RawJson = JsonSerializer.Serialize(new { success = false, error }) };
}

public sealed class ImageHarnessOutputsContract
{
    public ImageHarnessOutputContract[] Outputs { get; set; } = [];
}

public sealed class ImageHarnessOutputContract
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string LastWriteUtc { get; set; } = string.Empty;
}

public sealed class ImageHarnessDeleteOutputRequestContract : ImageHarnessContextRequestContract
{
    public string FilePath { get; set; } = string.Empty;
}

public sealed class ImageHarnessDeleteOutputResultContract
{
    public bool Success { get; set; }
    public string Error { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public static ImageHarnessDeleteOutputResultContract Ok(string fileName) => new() { Success = true, FileName = fileName };
    public static ImageHarnessDeleteOutputResultContract Fail(string error) => new() { Success = false, Error = error };
}
