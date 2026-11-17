using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace KeyLet.App.Services;

/// <summary>
/// 剪贴板安全写入与定时自动清理服务。
/// </summary>
public sealed class ClipboardService : IDisposable
{
    private CancellationTokenSource? _clearCts;
    private string? _lastCopiedText;

    /// <summary>
    /// 将文本写入剪贴板，并在指定秒数后校验内容未变动时自动清理。
    /// </summary>
    public void CopyWithAutoClear(string text, int autoClearSeconds, Action? onCleared = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        SetClipboardTextWithRetry(text);
        _lastCopiedText = text;

        _clearCts?.Cancel();
        _clearCts?.Dispose();
        _clearCts = null;

        if (autoClearSeconds <= 0)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _clearCts = cts;
        Dispatcher dispatcher = Application.Current.Dispatcher;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(autoClearSeconds), cts.Token);
                await dispatcher.InvokeAsync(() =>
                {
                    if (cts.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        if (Clipboard.ContainsText() &&
                            string.Equals(Clipboard.GetText(), text, StringComparison.Ordinal))
                        {
                            Clipboard.Clear();
                            onCleared?.Invoke();
                        }
                    }
                    catch (ExternalException)
                    {
                        // 剪贴板被其他进程短暂占用时忽略清理异常
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // 已被下一次复制或退出取消
            }
        });
    }

    public void Dispose()
    {
        _clearCts?.Cancel();
        _clearCts?.Dispose();
        _clearCts = null;
        _lastCopiedText = null;
    }

    private static void SetClipboardTextWithRetry(string text)
    {
        const int maxAttempts = 5;
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return;
            }
            catch (ExternalException) when (i < maxAttempts - 1)
            {
                Thread.Sleep(25);
            }
        }
    }
}
