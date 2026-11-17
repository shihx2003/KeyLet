namespace KeyLet.Core.Totp;

/// <summary>
/// 表示某一时刻的动态验证码快照。
/// </summary>
public sealed record TotpCodeSnapshot(
    string RawCode,
    string FormattedDisplayCode,
    int RemainingSeconds,
    int PeriodSeconds);
