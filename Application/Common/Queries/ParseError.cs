namespace Application.Common.Queries;

public sealed class ParseError
{
    public string Code { get; init; } = default!;

    public string Message { get; init; } = default!;

    public string? Target { get; init; }

    public int? Position { get; init; }

    public string? RawValue { get; init; }
}