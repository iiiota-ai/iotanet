public sealed class RudpSendResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = "";

    public static RudpSendResult Ok(string message = "OK") => new () { Success = true, Message = message };

    public static RudpSendResult Fail(string message) => new () { Success = false, Message = message };
}