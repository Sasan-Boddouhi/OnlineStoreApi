namespace Application.Common.Queries;

public sealed class ParseResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public IReadOnlyList<ParseError> Errors { get; init; }
        = Array.Empty<ParseError>();

    public static ParseResult<T> Ok(T value)
    {
        return new ParseResult<T>
        {
            Success = true,
            Value = value
        };
    }

    public static ParseResult<T> Fail(params ParseError[] errors)
    {
        return new ParseResult<T>
        {
            Success = false,
            Errors = errors
        };
    }
}