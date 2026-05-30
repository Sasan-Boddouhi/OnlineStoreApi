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

    private static readonly (Func<Token, Type, bool> Condition, Func<Token, Type, string> Message)[] MismatchRules =
    {
    (
        (t, type) => t.Type == TokenType.String && type != typeof(string),
        (t, type) => $"Cannot compare string with non-string field of type '{type.Name}'."
    ),
    (
        (t, type) => t.Type == TokenType.Number && type == typeof(string),
        (t, type) => "Cannot compare number with string field. Use quotes for string values."
    ),
    (
        (t, type) => t.Type == TokenType.Boolean && type != typeof(bool),
        (t, type) => "Cannot compare boolean with non-boolean field."
    ),
    (
        (t, type) => t.Type == TokenType.Identifier && type != typeof(string),
        (t, type) => $"Unquoted identifier '{t.Value}' is not allowed. Use quotes for strings or 'null' for null."
    ),
};

    private static bool IsTypeMismatch(Token token, Type target, out string message)
    {
        foreach (var (condition, msgFunc) in MismatchRules)
        {
            if (condition(token, target))
            {
                message = msgFunc(token, target);
                return true;
            }
        }
        message = string.Empty;
        return false;
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

    private static readonly Dictionary<Type, Func<string, object>> TypeConverters = new()
    {
        [typeof(string)] = raw => raw,
        [typeof(int)] = raw => int.Parse(raw, CultureInfo.InvariantCulture),
        [typeof(long)] = raw => long.Parse(raw, CultureInfo.InvariantCulture),
        [typeof(decimal)] = raw => decimal.Parse(raw, CultureInfo.InvariantCulture),
        [typeof(double)] = raw => double.Parse(raw, CultureInfo.InvariantCulture),
        [typeof(float)] = raw => float.Parse(raw, CultureInfo.InvariantCulture),
        [typeof(bool)] = raw => bool.Parse(raw),
        [typeof(Guid)] = raw => Guid.Parse(raw),
        [typeof(DateTime)] = raw => DateTime.Parse(raw, CultureInfo.InvariantCulture)
    };

    private static object? ConvertValue(string raw, Type type)
    {
        if (TypeConverters.TryGetValue(type, out var converter))
            return converter(raw);

        return Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
    }

    private static Token Consume(ParsingContext ctx) => ctx.Tokens[ctx.Position++];
}