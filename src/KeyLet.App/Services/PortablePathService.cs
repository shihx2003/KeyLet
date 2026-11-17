using System.IO;

namespace KeyLet.App.Services;

/// <summary>
/// 便携路径解析服务：严格将所有运行数据限定在可执行文件同级的 data/ 目录下。
/// </summary>
public sealed class PortablePathService
{
    public const string DataFolderName = "data";
    public const string UnlockFileName = "local.unlock";

    public PortablePathService()
    {
        string? processPath = Environment.ProcessPath;
        ExeDirectory = !string.IsNullOrWhiteSpace(processPath)
            ? Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory
            : AppContext.BaseDirectory;
        DataDirectory = Path.Combine(ExeDirectory, DataFolderName);
    }

    public string ExeDirectory { get; }

    public string DataDirectory { get; }

    public string UnlockFilePath => Path.Combine(DataDirectory, UnlockFileName);

    public void EnsureDataDirectoryExists()
    {
        Directory.CreateDirectory(DataDirectory);
    }
}
