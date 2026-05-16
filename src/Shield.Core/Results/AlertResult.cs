namespace Shield.Core.Results;

public sealed record AlertResult(int Delivered, int Failed, bool Success, string? Error)
{
    public static AlertResult Ok(int delivered, int failed = 0) =>
        new(delivered, failed, true, null);

    public static AlertResult Fail(string error) => new(0, 0, false, error);
}
