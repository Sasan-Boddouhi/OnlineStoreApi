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

    public ParseResult<Expression<Func<T, bool>>> TryParse<T>(string filter, QueryParseContext<T> context) where T : class
    {
        try
        {
            var tokens = new FilterLexer(filter).Tokenize();
            var ctx = new ParsingContext
            {
                Tokens = tokens,
                AllowedFields = BuildAllowedSet(context)
            };
            var expression = ParseComparison<T>(ctx);
            return ParseResult<Expression<Func<T, bool>>>.Ok(expression);
        }
        catch (Exception ex)
        {
            return ParseResult<Expression<Func<T, bool>>>.Fail(
                new ParseError { Code = "filter_parse_error", Message = ex.Message, Target = "filter" });
        }
    }

    private Expression<Func<T, bool>> ParseComparison<T>(ParsingContext ctx) where T : class
    {
        var field = ConsumeField(ctx);
        var op = ConsumeOperator(ctx);
        var valueToken = Consume(ctx);

        var (parameter, property) = BuildPropertyAccess<T>(field);

        if (IsNullToken(valueToken))
            return BuildNullComparison<T>(parameter, property, field, op);

        var underlyingType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
        EnsureTypeCompatibility(valueToken, underlyingType);

        var converted = ConvertValue(valueToken.Value, underlyingType);
        var constant = Expression.Constant(converted, property.Type);
        var body = BuildOperatorBody(op, property, constant);

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static HashSet<string>? BuildAllowedSet<T>(QueryParseContext<T> context) where T : class
    {
        if (!context.AllowedFields.Any()) return null;
        return new HashSet<string>(context.AllowedFields,
            context.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    private string ConsumeField(ParsingContext ctx)
    {
        var token = Consume(ctx);
        if (ctx.AllowedFields != null && !ctx.AllowedFields.Contains(token.Value))
            throw new Exception($"Field '{token.Value}' is not allowed.");
        return token.Value;
    }

    private static string ConsumeOperator(ParsingContext ctx) => Consume(ctx).Value.ToLowerInvariant();

    private static (ParameterExpression param, Expression prop) BuildPropertyAccess<T>(string field) where T : class
    {
        var param = Expression.Parameter(typeof(T), "x");
        var lambda = ExpressionBuilder.BuildPropertyLambdaCached<T>(field);
        var prop = new ReplaceExpressionVisitor(lambda.Parameters[0], param).Visit(lambda.Body)!;
        return (param, prop);
    }

    private static bool IsNullToken(Token t) => t.Value.Equals("null", StringComparison.OrdinalIgnoreCase);

    private static Expression<Func<T, bool>> BuildNullComparison<T>(ParameterExpression param, Expression prop, string field, string op) where T : class
    {
        var type = prop.Type;
        if (Nullable.GetUnderlyingType(type) == null && type.IsValueType)
            throw new Exception($"Field '{field}' is not nullable, cannot compare with null.");

        var nullConst = Expression.Constant(null, type);
        Expression body = op switch
        {
            "eq" => Expression.Equal(prop, nullConst),
            "ne" => Expression.NotEqual(prop, nullConst),
            _ => throw new Exception($"Operator '{op}' cannot be used with null.")
        };
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    private static void EnsureTypeCompatibility(Token valueToken, Type targetType)
    {
        if (IsTypeMismatch(valueToken, targetType, out var msg))
            throw new Exception(msg);
    }

    private static bool IsTypeMismatch(Token token, Type target, out string message)
    {
        message = string.Empty;
        if (token.Type == TokenType.String && target != typeof(string))
            message = $"Cannot compare string with non-string field of type '{target.Name}'.";
        else if (token.Type == TokenType.Number && target == typeof(string))
            message = "Cannot compare number with string field. Use quotes for string values.";
        else if (token.Type == TokenType.Boolean && target != typeof(bool))
            message = "Cannot compare boolean with non-boolean field.";
        else if (token.Type == TokenType.Identifier && target != typeof(string))
            message = $"Unquoted identifier '{token.Value}' is not allowed. Use quotes for strings or 'null' for null.";
        else
            return false;
        return true;
    }

    private static readonly Dictionary<string, Func<Expression, Expression, Expression>> OperatorFactories = new()
    {
        ["eq"] = Expression.Equal,
        ["ne"] = Expression.NotEqual,
        ["gt"] = Expression.GreaterThan,
        ["ge"] = Expression.GreaterThanOrEqual,
        ["lt"] = Expression.LessThan,
        ["le"] = Expression.LessThanOrEqual,
        ["contains"] = (p, c) => BuildStringMethod(p, c, nameof(string.Contains)),
        ["startswith"] = (p, c) => BuildStringMethod(p, c, nameof(string.StartsWith)),
        ["endswith"] = (p, c) => BuildStringMethod(p, c, nameof(string.EndsWith))
    };

    private static Expression BuildOperatorBody(string op, Expression prop, Expression constant)
    {
        if (OperatorFactories.TryGetValue(op, out var factory))
            return factory(prop, constant);

        throw new NotSupportedException($"Operator '{op}' is not supported.");
    }

    private static Expression BuildStringMethod(Expression prop, Expression constant, string method)
    {
        var notNull = Expression.NotEqual(prop, Expression.Constant(null, typeof(string)));
        var call = Expression.Call(prop, method, Type.EmptyTypes, constant);
        return Expression.AndAlso(notNull, call);
    }

    private static object? ConvertValue(string raw, Type type)
    {
        if (type == typeof(string)) return raw;
        if (type == typeof(int)) return int.Parse(raw, CultureInfo.InvariantCulture);
        if (type == typeof(long)) return long.Parse(raw, CultureInfo.InvariantCulture);
        if (type == typeof(decimal)) return decimal.Parse(raw, CultureInfo.InvariantCulture);
        if (type == typeof(double)) return double.Parse(raw, CultureInfo.InvariantCulture);
        if (type == typeof(float)) return float.Parse(raw, CultureInfo.InvariantCulture);
        if (type == typeof(bool)) return bool.Parse(raw);
        if (type == typeof(Guid)) return Guid.Parse(raw);
        if (type == typeof(DateTime)) return DateTime.Parse(raw, CultureInfo.InvariantCulture);
        return Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
    }

    private static Token Consume(ParsingContext ctx) => ctx.Tokens[ctx.Position++];
}