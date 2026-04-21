namespace SmartCollect.Application.DTOs.Common;

public sealed record QuickSendResult(bool Sent, string Detail)
{
    public static QuickSendResult Success(string detail = "OK") => new(true, detail);
    public static QuickSendResult Fail(string detail) => new(false, detail);
}
