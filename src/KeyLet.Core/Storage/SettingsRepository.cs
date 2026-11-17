using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyLet.Core.Models;

namespace KeyLet.Core.Storage;

/// <summary>
/// 应用便携配置文件读写服务（data/settings.json）。
/// </summary>
public sealed class SettingsRepository
{
    public const string SettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 读取配置；若不存在则返回默认配置。
    /// </summary>
    public AppSettings Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        string path = Path.Combine(dataDirectory, SettingsFileName);
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        string json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    /// <summary>
    /// 原子保存配置到 data/settings.json。
    /// </summary>
    public void Save(string dataDirectory, AppSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(dataDirectory);
        string path = Path.Combine(dataDirectory, SettingsFileName);
        string tempPath = Path.Combine(dataDirectory, $"{SettingsFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
