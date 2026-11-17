namespace KeyLet.Core.Storage;

/// <summary>
/// 表示密钥库解锁、校验或读写过程中发生的业务异常。
/// </summary>
public sealed class VaultException : Exception
{
    public VaultException(string message)
        : base(message)
    {
    }

    public VaultException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
