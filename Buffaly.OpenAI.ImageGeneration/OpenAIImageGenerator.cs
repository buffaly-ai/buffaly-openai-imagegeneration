using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Buffaly.OpenAI.ImageGeneration;

public static class OpenAIImageGenerator
{
    private const string DefaultModel = "gpt-image-1.5";
    private const string GptImage15Model = "gpt-image-1.5";
    private const string GptImage2Model = "gpt-image-2";
    private const string DefaultSize = "1024x1024";
    private const string DefaultQuality = "low";
    private const string DefaultBackground = "opaque";
    private const string DefaultOutputFormat = "png";
    private const string GenerationsEndpoint = "https://api.openai.com/v1/images/generations";
    private const string EditsEndpoint = "https://api.openai.com/v1/images/edits";

    public static string GenerateImageToFile(string apiKey, string prompt, string outputFilePath, string size, string model, string quality, string background)
    {
        return GenerateImageToFile(apiKey, prompt, outputFilePath, size, model, quality, background, DefaultOutputFormat, string.Empty);
    }

    public static string GenerateImageToFile(string apiKey, string prompt, string outputFilePath, string size, string quality, string background, string outputFormat, string outputCompression)
    {
        return GenerateImageToFile(apiKey, prompt, outputFilePath, size, DefaultModel, quality, background, outputFormat, outputCompression);
    }

    public static string GenerateImageToFile(string apiKey, string prompt, string outputFilePath, string size, string model, string quality, string background, string outputFormat, string outputCompression)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Serialize(new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "apiKey is required."
                });
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                return Serialize(new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "prompt is required."
                });
            }

            var resolvedModel = ResolveModel(model);
            if (string.IsNullOrWhiteSpace(resolvedModel))
            {
                return SerializeError($"model must be '{GptImage15Model}' or '{GptImage2Model}'.");
            }

            var resolvedSize = ResolveOption(size, DefaultSize);
            var resolvedQuality = ResolveOption(quality, DefaultQuality);
            var resolvedBackground = ResolveBackground(background);
            var resolvedOutputFormat = ResolveOutputFormat(outputFormat);
            var resolvedOutputFilePath = ResolveOutputFilePath(outputFilePath, resolvedOutputFormat);
            var validationError = ValidateOutputOptions(resolvedModel, resolvedSize, resolvedBackground, outputCompression);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                return SerializeError(validationError);
            }

            var requestBody = new Dictionary<string, object?>
            {
                ["model"] = resolvedModel,
                ["prompt"] = prompt,
                ["size"] = resolvedSize,
                ["quality"] = resolvedQuality,
                ["background"] = resolvedBackground,
                ["output_format"] = resolvedOutputFormat
            };

            AddOutputCompression(requestBody, outputCompression);

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

            using var request = new HttpRequestMessage(HttpMethod.Post, GenerationsEndpoint)
            {
                Content = new StringContent(Serialize(requestBody), Encoding.UTF8, "application/json")
            };

            using var response = httpClient.SendAsync(request).GetAwaiter().GetResult();
            var responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            return HandleImageResponse(response, responseContent, resolvedOutputFilePath, resolvedModel, resolvedSize, resolvedQuality, resolvedBackground, resolvedOutputFormat);
        }
        catch (Exception ex)
        {
            return SerializeError(ex.Message);
        }
    }

    public static string EditImageToFile(string apiKey, string prompt, string imageFilePath, string outputFilePath, string maskFilePath, string size, string quality, string background, string outputFormat, string outputCompression)
    {
        return EditImageToFile(apiKey, prompt, new[] { imageFilePath }, outputFilePath, maskFilePath, size, DefaultModel, quality, background, outputFormat, outputCompression);
    }

    public static string EditImageToFile(string apiKey, string prompt, string imageFilePath, string outputFilePath, string maskFilePath, string size, string model, string quality, string background, string outputFormat, string outputCompression)
    {
        return EditImageToFile(apiKey, prompt, new[] { imageFilePath }, outputFilePath, maskFilePath, size, model, quality, background, outputFormat, outputCompression);
    }

    public static string EditImageToFile(string apiKey, string prompt, IEnumerable<string> imageFilePaths, string outputFilePath, string maskFilePath, string size, string quality, string background, string outputFormat, string outputCompression)
    {
        return EditImageToFile(apiKey, prompt, imageFilePaths, outputFilePath, maskFilePath, size, DefaultModel, quality, background, outputFormat, outputCompression);
    }

    public static string EditImageToFile(string apiKey, string prompt, IEnumerable<string> imageFilePaths, string outputFilePath, string maskFilePath, string size, string model, string quality, string background, string outputFormat, string outputCompression)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return SerializeError("apiKey is required.");
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                return SerializeError("prompt is required.");
            }

            var resolvedImageFilePaths = (imageFilePaths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .ToArray();

            if (resolvedImageFilePaths.Length == 0)
            {
                return SerializeError("At least one imageFilePath is required.");
            }

            foreach (var imagePath in resolvedImageFilePaths)
            {
                if (!File.Exists(imagePath))
                {
                    return SerializeError($"Image file does not exist: {imagePath}");
                }
            }

            var resolvedMaskFilePath = string.IsNullOrWhiteSpace(maskFilePath) ? string.Empty : Path.GetFullPath(maskFilePath);
            if (!string.IsNullOrWhiteSpace(resolvedMaskFilePath) && !File.Exists(resolvedMaskFilePath))
            {
                return SerializeError($"Mask file does not exist: {resolvedMaskFilePath}");
            }

            var resolvedModel = ResolveModel(model);
            if (string.IsNullOrWhiteSpace(resolvedModel))
            {
                return SerializeError($"model must be '{GptImage15Model}' or '{GptImage2Model}'.");
            }

            var resolvedSize = ResolveOption(size, DefaultSize);
            var resolvedQuality = ResolveOption(quality, DefaultQuality);
            var resolvedBackground = ResolveBackground(background);
            var resolvedOutputFormat = ResolveOutputFormat(outputFormat);
            var resolvedOutputFilePath = ResolveOutputFilePath(outputFilePath, resolvedOutputFormat);
            var validationError = ValidateOutputOptions(resolvedModel, resolvedSize, resolvedBackground, outputCompression);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                return SerializeError(validationError);
            }

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(resolvedModel), "model");
            form.Add(new StringContent(prompt), "prompt");
            form.Add(new StringContent(resolvedSize), "size");
            form.Add(new StringContent(resolvedQuality), "quality");
            form.Add(new StringContent(resolvedBackground), "background");
            form.Add(new StringContent(resolvedOutputFormat), "output_format");
            AddOutputCompression(form, outputCompression);

            var fileContents = new List<IDisposable>();
            foreach (var imagePath in resolvedImageFilePaths)
            {
                var content = CreateFileContent(imagePath);
                fileContents.Add(content);
                form.Add(content, "image[]", Path.GetFileName(imagePath));
            }

            if (!string.IsNullOrWhiteSpace(resolvedMaskFilePath))
            {
                var maskContent = CreateFileContent(resolvedMaskFilePath);
                fileContents.Add(maskContent);
                form.Add(maskContent, "mask", Path.GetFileName(resolvedMaskFilePath));
            }

            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

                using var request = new HttpRequestMessage(HttpMethod.Post, EditsEndpoint)
                {
                    Content = form
                };

                using var response = httpClient.SendAsync(request).GetAwaiter().GetResult();
                var responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                return HandleImageResponse(response, responseContent, resolvedOutputFilePath, resolvedModel, resolvedSize, resolvedQuality, resolvedBackground, resolvedOutputFormat, resolvedImageFilePaths, resolvedMaskFilePath);
            }
            finally
            {
                foreach (var content in fileContents)
                {
                    content.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            return SerializeError(ex.Message);
        }
    }
    private static string HandleImageResponse(HttpResponseMessage response, string responseContent, string outputFilePath, string model, string size, string quality, string background, string outputFormat, string[]? inputImageFilePaths = null, string maskFilePath = "")
    {
        if (!response.IsSuccessStatusCode)
        {
            return Serialize(new Dictionary<string, object?>
            {
                ["success"] = false,
                ["statusCode"] = (int)response.StatusCode,
                ["error"] = ExtractErrorMessage(responseContent)
            });
        }

        using var document = JsonDocument.Parse(responseContent);
        if (!TryExtractFirstDataItem(document.RootElement, out var dataItem))
        {
            return SerializeError("Response did not include a valid data[0] item.");
        }

        byte[] imageBytes;
        if (TryGetString(dataItem, "b64_json", out var base64Image) && !string.IsNullOrWhiteSpace(base64Image))
        {
            imageBytes = Convert.FromBase64String(base64Image);
        }
        else if (TryGetString(dataItem, "url", out var imageUrl) && !string.IsNullOrWhiteSpace(imageUrl))
        {
            using var downloadClient = new HttpClient();
            imageBytes = downloadClient.GetByteArrayAsync(imageUrl).GetAwaiter().GetResult();
        }
        else
        {
            return SerializeError("Response did not include image data (b64_json or url).");
        }

        EnsureDirectoryForFile(outputFilePath);
        File.WriteAllBytes(outputFilePath, imageBytes);

        var successPayload = new Dictionary<string, object?>
        {
            ["success"] = true,
            ["outputFilePath"] = outputFilePath,
            ["model"] = model,
            ["size"] = size,
            ["quality"] = quality,
            ["background"] = background,
            ["outputFormat"] = outputFormat,
            ["bytes"] = new FileInfo(outputFilePath).Length
        };

        if (inputImageFilePaths is { Length: > 0 })
        {
            successPayload["inputImageFilePaths"] = inputImageFilePaths;
        }

        if (!string.IsNullOrWhiteSpace(maskFilePath))
        {
            successPayload["maskFilePath"] = maskFilePath;
            successPayload["maskedEditResultSource"] = "openai-final-image";
        }

        if (TryGetString(dataItem, "revised_prompt", out var revisedPrompt) && !string.IsNullOrWhiteSpace(revisedPrompt))
        {
            successPayload["revisedPrompt"] = revisedPrompt;
        }

        return Serialize(successPayload);
    }
    private static string ResolveOutputFilePath(string outputFilePath, string outputFormat)
    {
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            var fileName = $"openai-image-{DateTime.UtcNow:yyyyMMddHHmmssfff}.{outputFormat}";
            outputFilePath = Path.Combine("C:\\temp\\generated-images", fileName);
        }

        var resolvedPath = Path.GetFullPath(outputFilePath);
        var extension = "." + outputFormat;
        if (!resolvedPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            resolvedPath = Path.ChangeExtension(resolvedPath, outputFormat);
        }

        return resolvedPath;
    }

    private static void EnsureDirectoryForFile(string outputFilePath)
    {
        var directory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static bool TryExtractFirstDataItem(JsonElement root, out JsonElement dataItem)
    {
        dataItem = default;

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            return false;
        }

        dataItem = data[0];
        return true;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = prop.GetString() ?? string.Empty;
        return true;
    }

    private static string ResolveOption(string value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static string ResolveModel(string model)
    {
        var resolvedModel = ResolveOption(model, DefaultModel).ToLowerInvariant();
        return resolvedModel switch
        {
            GptImage15Model => GptImage15Model,
            GptImage2Model => GptImage2Model,
            _ => string.Empty
        };
    }

    private static string ResolveBackground(string background)
    {
        return ResolveOption(background, DefaultBackground).ToLowerInvariant();
    }

    private static string ResolveOutputFormat(string outputFormat)
    {
        var format = ResolveOption(outputFormat, DefaultOutputFormat).ToLowerInvariant();
        return format switch
        {
            "jpg" => "jpeg",
            "jpeg" or "png" or "webp" => format,
            _ => DefaultOutputFormat
        };
    }

    private static string ValidateOutputOptions(string model, string size, string background, string outputCompression)
    {
        if (background.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return "Transparent backgrounds are not supported by the configured GPT Image models.";
        }

        if (model.Equals(GptImage15Model, StringComparison.OrdinalIgnoreCase) && !IsSupportedGptImage15Size(size))
        {
            return "gpt-image-1.5 supports size values 'auto', '1024x1024', '1024x1536', and '1536x1024'.";
        }

        if (string.IsNullOrWhiteSpace(outputCompression))
        {
            return string.Empty;
        }

        if (!int.TryParse(outputCompression.Trim(), out var compression) || compression < 0 || compression > 100)
        {
            return "outputCompression must be an integer from 0 to 100.";
        }

        return string.Empty;
    }

    private static bool IsSupportedGptImage15Size(string size)
    {
        return size.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || size.Equals("1024x1024", StringComparison.OrdinalIgnoreCase)
            || size.Equals("1024x1536", StringComparison.OrdinalIgnoreCase)
            || size.Equals("1536x1024", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddOutputCompression(Dictionary<string, object?> requestBody, string outputCompression)
    {
        if (int.TryParse(outputCompression?.Trim(), out var compression))
        {
            requestBody["output_compression"] = compression;
        }
    }

    private static void AddOutputCompression(MultipartFormDataContent form, string outputCompression)
    {
        if (int.TryParse(outputCompression?.Trim(), out var compression))
        {
            form.Add(new StringContent(compression.ToString()), "output_compression");
        }
    }

    private static StreamContent CreateFileContent(string filePath)
    {
        var content = new StreamContent(File.OpenRead(filePath));
        content.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
        return content;
    }

    private static string GetContentType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png"
        };
    }

    private static string ExtractErrorMessage(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return "Request failed and no error details were returned.";
        }

        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            if (doc.RootElement.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.ValueKind == JsonValueKind.String)
                {
                    var errorText = errorObj.GetString();
                    if (!string.IsNullOrWhiteSpace(errorText))
                    {
                        return errorText;
                    }
                }

                if (errorObj.ValueKind == JsonValueKind.Object && TryGetString(errorObj, "message", out var message) && !string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
        }
        catch
        {
            // Fall through and return raw content.
        }

        return responseContent;
    }

    private static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static string SerializeError(string error)
    {
        return Serialize(new Dictionary<string, object?>
        {
            ["success"] = false,
            ["error"] = error
        });
    }
}




