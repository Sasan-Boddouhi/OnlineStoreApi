using System.Globalization;
using System.Linq.Expressions;
using Application.Common.Helpers;
using Application.Common.Specifications;

namespace Application.Common.Queries;

public sealed class FilterParser
{
    private sealed class ParsingContext
    {
        public required List<Token> Tokens { get; init; }

        public required HashSet<string>? AllowedFields { get; init; }

        public int Position { get; set; }
    }

    public ParseResult<Expression<Func<T, bool>>> TryParse<T>(
        string filter,
        QueryParseContext<T> context)
        where T : class
    {
        try
        {
            var lexer = new FilterLexer(filter);

            var tokens = lexer.Tokenize();

            var parsingContext = new ParsingContext
            {
                Tokens = tokens,
                AllowedFields = context.AllowedFields.Any()
                    ? new HashSet<string>(
                        context.AllowedFields,
                        context.CaseInsensitive
                            ? StringComparer.OrdinalIgnoreCase
                            : StringComparer.Ordinal)
                    : null
            };

            var expression = ParseExpression<T>(parsingContext);

            return ParseResult<Expression<Func<T, bool>>>.Ok(expression);
        }
        catch (Exception ex)
        {
            return ParseResult<Expression<Func<T, bool>>>.Fail(
                new ParseError
                {
                    Code = "filter_parse_error",
                    Message = ex.Message,
                    Target = "filter"
                });
        }
    }

    private Expression<Func<T, bool>> ParseExpression<T>(
        ParsingContext ctx)
        where T : class
    {
        return ParseComparison<T>(ctx);
    }

    private Expression<Func<T, bool>> ParseComparison<T>(
        ParsingContext ctx)
        where T : class
    {
        var field = Consume(ctx).Value;

        if (ctx.AllowedFields != null &&
            !ctx.AllowedFields.Contains(field))
        {
            throw new Exception($"Field '{field}' is not allowed.");
        }

        var op = Consume(ctx).Value;

        var valueToken = Consume(ctx);

        var parameter = Expression.Parameter(typeof(T), "x");

        var propertyLambda =
            ExpressionBuilder.BuildPropertyLambdaCached<T>(field);

        var property = new ReplaceExpressionVisitor(
            propertyLambda.Parameters[0],
            parameter)
            .Visit(propertyLambda.Body)!;

        var propertyType =
            Nullable.GetUnderlyingType(property.Type)
            ?? property.Type;

        var convertedValue = ConvertValue(
            valueToken.Value,
            propertyType);

        var constant =
            Expression.Constant(convertedValue, property.Type);

        Expression body = op.ToLowerInvariant() switch
        {
            "eq" => Expression.Equal(property, constant),

            "ne" => Expression.NotEqual(property, constant),

            "gt" => Expression.GreaterThan(property, constant),

            "ge" => Expression.GreaterThanOrEqual(property, constant),

            "lt" => Expression.LessThan(property, constant),

            "le" => Expression.LessThanOrEqual(property, constant),

            "contains" => BuildStringMethod(
                property,
                constant,
                nameof(string.Contains)),

            "startswith" => BuildStringMethod(
                property,
                constant,
                nameof(string.StartsWith)),

            "endswith" => BuildStringMethod(
                property,
                constant,
                nameof(string.EndsWith)),

            _ => throw new NotSupportedException(
                $"Operator '{op}' is not supported.")
        };

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static Expression BuildStringMethod(
        Expression property,
        Expression constant,
        string method)
    {
        var notNull = Expression.NotEqual(
            property,
            Expression.Constant(null, typeof(string)));

        var call = Expression.Call(
            property,
            method,
            Type.EmptyTypes,
            constant);

        return Expression.AndAlso(notNull, call);
    }

    private static object? ConvertValue(
        string raw,
        Type type)
    {
        if (type == typeof(string))
            return raw;

        if (type == typeof(int))
            return int.Parse(raw, CultureInfo.InvariantCulture);

        if (type == typeof(decimal))
            return decimal.Parse(raw, CultureInfo.InvariantCulture);

        if (type == typeof(double))
            return double.Parse(raw, CultureInfo.InvariantCulture);

        if (type == typeof(float))
            return float.Parse(raw, CultureInfo.InvariantCulture);

        if (type == typeof(bool))
            return bool.Parse(raw);

        if (type == typeof(Guid))
            return Guid.Parse(raw);

        if (type == typeof(DateTime))
            return DateTime.Parse(raw, CultureInfo.InvariantCulture);

        return Convert.ChangeType(
            raw,
            type,
            CultureInfo.InvariantCulture);
    }

    private Token Consume(ParsingContext ctx)
    {
        return ctx.Tokens[ctx.Position++];
    }
}