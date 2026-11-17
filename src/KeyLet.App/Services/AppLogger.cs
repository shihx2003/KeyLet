using System.IO;
using System.Text;

namespace KeyLet.App.Services;

/// <summary>
/// 便携运行日志服务：写入 data/logs/keylet-yyyyMMdd.log（严禁记录任何明文密钥或口令）。
/// </summary>
public static class AppLogger
{
    private static readonly object SyncRoot = new();
    private static string? _logDirectory;

    public static void Initialize(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _logDirectory = Path.Combine(dataDirectory, "logs");
        Directory.CreateDirectory(_logDirectory);
        PruneOldLogs(_logDirectory, keepDays: 14);
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        if (string.IsNullOrWhiteSpace(_logDirectory))
        {
            return;
        }

        try
        {
            DateTime now = DateTime.Now;
            string filePath = Path.Combine(_logDirectory, $"keylet-{now:yyyyMMdd}.log");
            var builder = new StringBuilder();
            builder.Append('[').Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
            builder.Append('[').Append(level).Append("] ");
            builder.AppendLine(message);

            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            lock (SyncRoot)
            {
                File.AppendAllText(filePath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // 日志写入失败不应影响主程序运行
        }
    }

    private static void PruneOldLogs(string logDirectory, int keepDays)
    {
        try
        {
            DateTime cutoff = DateTime.Now.Date.AddDays(-keepDays);
            foreach (string file in Directory.EnumerateFiles(logDirectory, "keylet-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // 忽略清理旧日志时的文件占用异常
        }
    }
}
