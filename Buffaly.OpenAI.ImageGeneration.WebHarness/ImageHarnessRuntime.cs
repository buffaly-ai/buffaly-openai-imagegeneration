using System.Text.Json;

namespace Buffaly.OpenAI.ImageGeneration.WebHarness;

internal sealed class ImageHarnessRuntime
{
	private readonly JsonSerializerOptions m_jsonOptions = new(JsonSerializerDefaults.Web);
	private readonly string m_rootPrefix;

	private ImageHarnessRuntime(string apiKey, string rootDirectory)
	{
		this.ApiKey = NormalizeOptional(apiKey);
		var normalizedRootDirectory = NormalizeOptional(rootDirectory);
		if (string.IsNullOrWhiteSpace(normalizedRootDirectory))
		{
			throw new InvalidOperationException("Image harness root directory is required.");
		}

		this.RootDirectory = Path.GetFullPath(normalizedRootDirectory);

		Directory.CreateDirectory(this.RootDirectory);
		Directory.CreateDirectory(Path.Combine(this.RootDirectory, "uploads"));
		this.m_rootPrefix = this.RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
	}

	public string ApiKey { get; }

	public string RootDirectory { get; }

	public string OutputRoot => this.RootDirectory;

	public string HistoryRoot => this.RootDirectory;

	public static ImageHarnessRuntime Create(string? rootDirectory)
	{
		return Create(string.Empty, rootDirectory);
	}

	// Receive the OpenAI key only from the ProtoScript service initializer that reads OpenAIFeature.
	public static ImageHarnessRuntime Create(string apiKey, string? rootDirectory)
	{
		return new ImageHarnessRuntime(apiKey, rootDirectory ?? string.Empty);
	}

	public string CreateOutputPath(string operation, string model, string outputFormat)
	{
		var safeModel = SanitizeFilePart(model);
		var safeOperation = SanitizeFilePart(operation);
		var extension = NormalizeOutputExtension(outputFormat);
		var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{safeOperation}-{safeModel}.{extension}";
		return Path.Combine(this.RootDirectory, fileName);
	}

	public string ResolveFilePath(string fileName)
	{
		string safeFileName = Path.GetFileName(NormalizeOptional(fileName));
		if (string.IsNullOrWhiteSpace(safeFileName))
		{
			return string.Empty;
		}

		string candidate = Path.GetFullPath(Path.Combine(this.RootDirectory, safeFileName));
		if (!candidate.StartsWith(this.m_rootPrefix, StringComparison.OrdinalIgnoreCase) && !string.Equals(candidate, this.RootDirectory, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Requested file is outside the image harness root directory.");
		}

		return candidate;
	}

	public string SaveBase64Upload(string base64Data, string fileName, string prefix)
	{
		if (string.IsNullOrWhiteSpace(base64Data))
		{
			throw new InvalidOperationException("Image data is required.");
		}

		var commaIndex = base64Data.IndexOf(',');
		var cleanBase64 = commaIndex >= 0 ? base64Data[(commaIndex + 1)..] : base64Data;
		var bytes = Convert.FromBase64String(cleanBase64);
		var extension = NormalizeUploadExtension(Path.GetExtension(fileName));
		var uploadFileName = $"{SanitizeFilePart(prefix)}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.{extension}";
		var path = Path.Combine(this.RootDirectory, "uploads", uploadFileName);
		File.WriteAllBytes(path, bytes);
		return path;
	}

	public HarnessResult ToClientResult(string rawJson)
	{
		using var document = JsonDocument.Parse(rawJson);
		var root = document.RootElement;
		var success = root.TryGetProperty("success", out var successProperty) && successProperty.ValueKind == JsonValueKind.True;
		var result = new HarnessResult
		{
			Success = success,
			Raw = JsonSerializer.Deserialize<Dictionary<string, object?>>(rawJson, this.m_jsonOptions) ?? new Dictionary<string, object?>()
		};

		if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
		{
			result.Error = error.GetString() ?? string.Empty;
		}

		if (root.TryGetProperty("outputFilePath", out var pathProperty) && pathProperty.ValueKind == JsonValueKind.String)
		{
			var path = pathProperty.GetString() ?? string.Empty;
			if (!success)
			{
				DeleteRejectedOutput(path);
				return result;
			}

			result.OutputFilePath = path;
			result.FileName = Path.GetFileName(path);
			result.ImageUrl = CreateImageUrl(path);
		}

		return result;
	}

	public IReadOnlyList<ImageHarnessOutputContract> ListOutputs()
	{
		return Directory.Exists(this.RootDirectory)
			? Directory.GetFiles(this.RootDirectory, "*", SearchOption.TopDirectoryOnly)
				.Where(IsImageFile)
				.Select(path => new FileInfo(path))
				.OrderByDescending(file => file.LastWriteTimeUtc)
				.Take(80)
				.Select(file => new ImageHarnessOutputContract
				{
					FileName = file.Name,
					FilePath = file.FullName,
					ImageUrl = CreateImageUrl(file.FullName),
					Bytes = file.Length,
					LastWriteUtc = file.LastWriteTimeUtc.ToString("O")
				})
				.ToArray()
			: [];
	}

	public bool DeleteGeneratedFile(string fileName, string filePath)
	{
		string resolvedPath = !string.IsNullOrWhiteSpace(filePath) ? Path.GetFullPath(filePath) : ResolveFilePath(fileName);
		if (!resolvedPath.StartsWith(this.m_rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolvedPath))
		{
			return false;
		}

		File.Delete(resolvedPath);
		return true;
	}

	public string GetContentType(string filePath)
	{
		return Path.GetExtension(filePath).ToLowerInvariant() switch
		{
			".jpg" or ".jpeg" => "image/jpeg",
			".webp" => "image/webp",
			_ => "image/png"
		};
	}

	public string CreateImageUrl(string outputFilePath)
	{
		return string.IsNullOrWhiteSpace(outputFilePath) ? string.Empty : "/api/local-file-image?path=" + Uri.EscapeDataString(Path.GetFullPath(outputFilePath));
	}

	private void DeleteRejectedOutput(string outputFilePath)
	{
		if (string.IsNullOrWhiteSpace(outputFilePath))
		{
			return;
		}

		var resolvedPath = Path.GetFullPath(outputFilePath);
		if (!resolvedPath.StartsWith(this.m_rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolvedPath))
		{
			return;
		}

		File.Delete(resolvedPath);
	}

	private static string NormalizeOptional(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
	}

	private static string NormalizeOutputExtension(string outputFormat)
	{
		return outputFormat.Trim().ToLowerInvariant() switch
		{
			"jpg" or "jpeg" => "jpg",
			"webp" => "webp",
			_ => "png"
		};
	}

	private static string NormalizeUploadExtension(string extension)
	{
		return extension.TrimStart('.').ToLowerInvariant() switch
		{
			"jpg" or "jpeg" => "jpg",
			"webp" => "webp",
			_ => "png"
		};
	}

	private static bool IsImageFile(string path)
	{
		return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";
	}

	private static string SanitizeFilePart(string value)
	{
		var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
		var text = new string(chars).Trim('-');
		return string.IsNullOrWhiteSpace(text) ? "image" : text;
	}
}

internal sealed class HarnessResult
{
	public bool Success { get; set; }
	public string Error { get; set; } = string.Empty;
	public string OutputFilePath { get; set; } = string.Empty;
	public string FileName { get; set; } = string.Empty;
	public string ImageUrl { get; set; } = string.Empty;
	public Dictionary<string, object?> Raw { get; set; } = new();

	public static HarnessResult Fail(string error)
	{
		return new HarnessResult
		{
			Success = false,
			Error = error,
			Raw = new Dictionary<string, object?> { ["success"] = false, ["error"] = error }
		};
	}
}
