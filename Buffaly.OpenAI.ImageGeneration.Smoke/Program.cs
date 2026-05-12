using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Text.Json;
using Buffaly.OpenAI.ImageGeneration;

if (args.Any(arg => string.Equals(arg, "--facade-tests", StringComparison.OrdinalIgnoreCase)))
{
    return RunFacadeTests();
}

if (args.Any(arg => string.Equals(arg, "--masked-edit-proof", StringComparison.OrdinalIgnoreCase)))
{
    return RunMaskedEditProof(args);
}

var apiKey = ResolveApiKey(args);
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("No API key found. Set OPENAI_API_KEY or keep OpenAI.NonZdrToken/OpenAI.Token in the configured appsettings file.");
    return 1;
}

var outputRoot = Path.Combine("C:\\temp", "generated-images", "smoke");
Directory.CreateDirectory(outputRoot);

var model = ResolveModel(args);
var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
var safeModelName = model.Replace(".", "-");
var generatedPath = Path.Combine(outputRoot, $"{safeModelName}-generated-{timestamp}.png");
var editedPath = Path.Combine(outputRoot, $"{safeModelName}-edited-{timestamp}.png");

var generationPrompt = "Create a clean square test image: a matte blue coffee mug on a light desk, with a small white label on the mug that says BUFFALY.";
var generationResult = OpenAIImageGenerator.GenerateImageToFile(
    apiKey,
    generationPrompt,
    generatedPath,
    "1024x1024",
    model,
    "low",
    "opaque",
    "png",
    string.Empty);

Console.WriteLine("Generation result:");
Console.WriteLine(SummarizeResult(generationResult));

if (!IsSuccess(generationResult))
{
    return 1;
}

var editPrompt = "Edit this image by changing the mug color to warm red while keeping the desk, composition, and BUFFALY label.";
var editResult = OpenAIImageGenerator.EditImageToFile(
    apiKey,
    editPrompt,
    generatedPath,
    editedPath,
    string.Empty,
    "1024x1024",
    model,
    "low",
    "opaque",
    "png",
    string.Empty);

Console.WriteLine("Edit result:");
Console.WriteLine(SummarizeResult(editResult));

return IsSuccess(editResult) ? 0 : 1;

static string ResolveApiKey(string[] args)
{
    var explicitSettingsPath = args.FirstOrDefault(arg => arg.StartsWith("--settings=", StringComparison.OrdinalIgnoreCase));
    var settingsPath = explicitSettingsPath is null
        ? "C:\\dev\\BuffalyNet6\\Buffaly.Test\\appsettings.json"
        : explicitSettingsPath["--settings=".Length..].Trim('"');

    var envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (!string.IsNullOrWhiteSpace(envKey))
    {
        return envKey;
    }

    if (!File.Exists(settingsPath))
    {
        return string.Empty;
    }

    using var stream = File.OpenRead(settingsPath);
    using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    });

    if (!document.RootElement.TryGetProperty("AppSettings", out var appSettings))
    {
        return string.Empty;
    }

    var nonZdrToken = ReadString(appSettings, "OpenAI.NonZdrToken");
    if (!string.IsNullOrWhiteSpace(nonZdrToken))
    {
        return nonZdrToken;
    }

    return ReadString(appSettings, "OpenAI.Token");
}

static string ResolveModel(string[] args)
{
    var explicitModel = args.FirstOrDefault(arg => arg.StartsWith("--model=", StringComparison.OrdinalIgnoreCase));
    if (explicitModel is null)
    {
        return "gpt-image-1.5";
    }

    var model = explicitModel["--model=".Length..].Trim('"');
    return string.IsNullOrWhiteSpace(model) ? "gpt-image-1.5" : model;
}

static int RunMaskedEditProof(string[] args)
{
    if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
    {
        Console.Error.WriteLine("Masked edit proof requires Windows because System.Drawing.Common is Windows-only.");
        return 1;
    }

    var apiKey = ResolveApiKey(args);
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.Error.WriteLine("No API key found. Set OPENAI_API_KEY or keep OpenAI.NonZdrToken/OpenAI.Token in the configured appsettings file.");
        return 1;
    }

    var outputRoot = Path.Combine("C:\\temp", "generated-images", "smoke", "masked-proof", DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
    Directory.CreateDirectory(outputRoot);
    var sourcePath = Path.Combine(outputRoot, "source.png");
    var maskPath = Path.Combine(outputRoot, "mask.png");
    var editedPath = Path.Combine(outputRoot, "edited.png");

    CreateDeterministicSource(sourcePath);
    CreateTransparentEditMask(maskPath);

    var resultJson = OpenAIImageGenerator.EditImageToFile(
        apiKey,
        "Put one small bright red bird in the masked square only. Preserve the rest of the image exactly.",
        sourcePath,
        editedPath,
        maskPath,
        "1024x1024",
        ResolveModel(args),
        "low",
        "opaque",
        "png",
        string.Empty);

    Console.WriteLine("Masked edit result:");
    Console.WriteLine(SummarizeResult(resultJson));
    Console.WriteLine("Raw masked edit result:");
    Console.WriteLine(resultJson);

    if (!IsSuccess(resultJson))
    {
        return 1;
    }

    if (!File.Exists(editedPath))
    {
        Console.Error.WriteLine("Masked edit result did not write the expected OpenAI final image output.");
        return 1;
    }

    var proof = AnalyzeMaskedEditResult(sourcePath, maskPath, editedPath);
    Console.WriteLine(JsonSerializer.Serialize(proof, new JsonSerializerOptions { WriteIndented = true }));
    return proof.Passed ? 0 : 1;
}

[SupportedOSPlatform("windows6.1")]
static void CreateDeterministicSource(string path)
{
    using var image = new Bitmap(1024, 1024, PixelFormat.Format32bppArgb);
    for (var y = 0; y < image.Height; y++)
    {
        for (var x = 0; x < image.Width; x++)
        {
            image.SetPixel(x, y, Color.FromArgb(255, x % 256, y % 256, (x + y) % 256));
        }
    }

    using var graphics = Graphics.FromImage(image);
    using var brush = new SolidBrush(Color.FromArgb(255, 30, 90, 170));
    graphics.FillRectangle(brush, 420, 420, 184, 184);
    image.Save(path, ImageFormat.Png);
}

[SupportedOSPlatform("windows6.1")]
static void CreateTransparentEditMask(string path)
{
    using var mask = new Bitmap(1024, 1024, PixelFormat.Format32bppArgb);
    for (var y = 0; y < mask.Height; y++)
    {
        for (var x = 0; x < mask.Width; x++)
        {
            var inEditRegion = x >= 420 && x < 604 && y >= 420 && y < 604;
            mask.SetPixel(x, y, Color.FromArgb(inEditRegion ? 0 : 255, 255, 255, 255));
        }
    }

    mask.Save(path, ImageFormat.Png);
}

[SupportedOSPlatform("windows6.1")]
static MaskedProofResult AnalyzeMaskedEditResult(string sourcePath, string maskPath, string editedPath)
{
    using var source = new Bitmap(sourcePath);
    using var mask = new Bitmap(maskPath);
    using var edited = new Bitmap(editedPath);

    var changedOutsideMask = 0;
    var checkedOutsideMask = 0;
    var changedInsideMask = 0;
    var checkedInsideMask = 0;

    for (var y = 0; y < source.Height; y++)
    {
        for (var x = 0; x < source.Width; x++)
        {
            var sourcePixel = source.GetPixel(x, y);
            var editedPixel = edited.GetPixel(x, y);
            var isInsideMask = mask.GetPixel(x, y).A < 128;
            if (isInsideMask)
            {
                checkedInsideMask++;
                if (sourcePixel.ToArgb() != editedPixel.ToArgb()) changedInsideMask++;
            }
            else
            {
                checkedOutsideMask++;
                if (sourcePixel.ToArgb() != editedPixel.ToArgb()) changedOutsideMask++;
            }
        }
    }

    return new MaskedProofResult(
        Passed: checkedInsideMask > 0 && changedInsideMask > 0,
        CheckedOutsideMask: checkedOutsideMask,
        ChangedOutsideMask: changedOutsideMask,
        CheckedInsideMask: checkedInsideMask,
        ChangedInsideMask: changedInsideMask,
        SourcePath: sourcePath,
        MaskPath: maskPath,
        EditedPath: editedPath);
}

static string ReadString(JsonElement element, string propertyName)
{
    return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : string.Empty;
}

static bool IsSuccess(string json)
{
    using var document = JsonDocument.Parse(json);
    return document.RootElement.TryGetProperty("success", out var success)
        && success.ValueKind == JsonValueKind.True;
}

static string SummarizeResult(string json)
{
    using var document = JsonDocument.Parse(json);
    var root = document.RootElement;
    var summary = new Dictionary<string, object?>
    {
        ["success"] = root.TryGetProperty("success", out var success) && success.GetBoolean()
    };

    CopyIfPresent(root, summary, "statusCode");
    CopyIfPresent(root, summary, "error");
    CopyIfPresent(root, summary, "outputFilePath");
    CopyIfPresent(root, summary, "model");
    CopyIfPresent(root, summary, "size");
    CopyIfPresent(root, summary, "quality");
    CopyIfPresent(root, summary, "outputFormat");
    CopyIfPresent(root, summary, "bytes");
    CopyIfPresent(root, summary, "maskedEditResultSource");
    CopyIfPresent(root, summary, "revisedPrompt");

    return JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
}

static void CopyIfPresent(JsonElement root, Dictionary<string, object?> summary, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out var value))
    {
        return;
    }

    summary[propertyName] = value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => value.ToString()
    };
}

static int RunFacadeTests()
{
    var failures = new List<string>();

    AssertThrows<InvalidOperationException>(() => OpenAIImageGenerationServiceRuntime.CreateImage(new OpenAIImageCreateRequest { Prompt = "test" }), "create before configure should throw", failures);
    AssertThrows<ArgumentException>(() => OpenAIImageGenerationServiceRuntime.Configure(string.Empty, @"C:\temp\generated-images\facade-tests", "artifacts/images"), "missing api key should throw", failures);
    AssertThrows<ArgumentException>(() => OpenAIImageGenerationServiceRuntime.Configure("test-key", string.Empty, "artifacts/images"), "missing output directory should throw", failures);

    var outputRoot = @"C:\temp\generated-images\facade-tests";
    OpenAIImageGenerationServiceRuntime.Configure("test-key", outputRoot, "artifacts/images");
    AssertThrows<ArgumentException>(() => OpenAIImageGenerationServiceRuntime.CreateImage(new OpenAIImageCreateRequest()), "missing create prompt should throw", failures);
    AssertThrows<ArgumentException>(() => OpenAIImageGenerationServiceRuntime.EditImage(new OpenAIImageEditRequest { Prompt = "edit" }), "missing edit image path should throw", failures);

    var path = OpenAIImageGenerationServiceRuntime.BuildOutputPath(outputRoot, "created", "gpt-image-2", "jpeg");
    AssertTrue(path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase), "jpeg output should use jpg extension", failures);
    AssertTrue(path.Contains(outputRoot, StringComparison.OrdinalIgnoreCase), "custom output directory should be used", failures);

    AssertEqual("gpt-image-1.5", OpenAIImageGenerationServiceRuntime.NormalizeModel("not-a-model"), "invalid model fallback", failures);
    AssertEqual("gpt-image-2", OpenAIImageGenerationServiceRuntime.NormalizeModel("GPT-IMAGE-2"), "model normalization", failures);
    AssertEqual("jpeg", OpenAIImageGenerationServiceRuntime.NormalizeOutputFormat("jpg"), "jpg normalizes to jpeg request format", failures);

    if (failures.Count > 0)
    {
        Console.Error.WriteLine("Facade tests failed:");
        foreach (var failure in failures)
        {
            Console.Error.WriteLine("- " + failure);
        }
        return 1;
    }

    Console.WriteLine("Facade tests passed.");
    return 0;
}

static void AssertThrows<TException>(Action action, string name, List<string> failures)
    where TException : Exception
{
    try
    {
        action();
        failures.Add(name + ": expected " + typeof(TException).Name);
    }
    catch (TException)
    {
    }
}

static void AssertTrue(bool condition, string name, List<string> failures)
{
    if (!condition)
    {
        failures.Add(name);
    }
}

static void AssertEqual(string expected, string actual, string name, List<string> failures)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        failures.Add($"{name}: expected '{expected}', actual '{actual}'");
    }
}
public sealed record MaskedProofResult(
    bool Passed,
    int CheckedOutsideMask,
    int ChangedOutsideMask,
    int CheckedInsideMask,
    int ChangedInsideMask,
    string SourcePath,
    string MaskPath,
    string EditedPath);


