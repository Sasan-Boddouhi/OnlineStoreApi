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

    private Expression<Func<T, bool>> ParseExpression<T>(ParsingContext ctx)
        where T : class
    {
        return ParseComparison<T>(ctx);
    }

    private Expression<Func<T, bool>> ParseComparison<T>(ParsingContext ctx)
        where T : class
    {
        var fieldToken = Consume(ctx);
        var field = fieldToken.Value;

        if (ctx.AllowedFields != null && !ctx.AllowedFields.Contains(field))
        {
            throw new Exception($"Field '{field}' is not allowed.");
        }

        var opToken = Consume(ctx);
        var op = opToken.Value.ToLowerInvariant();

        var valueToken = Consume(ctx);

        var parameter = Expression.Parameter(typeof(T), "x");
        var propertyLambda = ExpressionBuilder.BuildPropertyLambdaCached<T>(field);
        var property = new ReplaceExpressionVisitor(
            propertyLambda.Parameters[0],
            parameter)
            .Visit(propertyLambda.Body)!;

        var propertyType = property.Type;
        var isNullable = Nullable.GetUnderlyingType(propertyType) != null;
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        // ----- 1. پشتیبانی از null -----
        if (valueToken.Value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            // فقط برای فیلدهای nullable یا رشته‌ای (مرجع) مجاز است
            if (isNullable || !underlyingType.IsValueType)
            {
                var constantNull = Expression.Constant(null, propertyType);
                Expression body = op switch
                {
                    "eq" => Expression.Equal(property, constantNull),
                    "ne" => Expression.NotEqual(property, constantNull),
                    _ => throw new Exception($"Operator '{op}' cannot be used with null.")
                };
                return Expression.Lambda<Func<T, bool>>(body, parameter);
            }
            else
            {
                throw new Exception($"Field '{field}' is not nullable, cannot compare with null.");
            }
        }

        // ----- 2. تشخیص نوع ناهماهنگ -----
        // بررسی کنید که آیا نوع توکن با نوع پراپرتی سازگار است
        if (IsTypeMismatch(valueToken, underlyingType, out var mismatchMessage))
        {
            throw new Exception(mismatchMessage);
        }

        // تبدیل مقدار به نوع پراپرتی
        var convertedValue = ConvertValue(valueToken.Value, underlyingType);

        var constant = Expression.Constant(convertedValue, propertyType);

        Expression bodyExpr = op switch
        {
            "eq" => Expression.Equal(property, constant),
            "ne" => Expression.NotEqual(property, constant),
            "gt" => Expression.GreaterThan(property, constant),
            "ge" => Expression.GreaterThanOrEqual(property, constant),
            "lt" => Expression.LessThan(property, constant),
            "le" => Expression.LessThanOrEqual(property, constant),
            "contains" => BuildStringMethod(property, constant, nameof(string.Contains)),
            "startswith" => BuildStringMethod(property, constant, nameof(string.StartsWith)),
            "endswith" => BuildStringMethod(property, constant, nameof(string.EndsWith)),
            _ => throw new NotSupportedException($"Operator '{op}' is not supported.")
        };

        return Expression.Lambda<Func<T, bool>>(bodyExpr, parameter);
    }

    private static bool IsTypeMismatch(Token valueToken, Type targetType, out string message)
    {
        message = string.Empty;

        // اگر مقدار رشته‌ای است و فیلد عددی یا بولی است → mismatch
        if (valueToken.Type == TokenType.String && targetType != typeof(string))
        {
            message = $"Cannot compare string with non-string field of type '{targetType.Name}'.";
            return true;
        }

        // اگر مقدار عددی است و فیلد رشته‌ای است → mismatch
        if (valueToken.Type == TokenType.Number && targetType == typeof(string))
        {
            message = $"Cannot compare number with string field. Use quotes for string values.";
            return true;
        }

        // اگر مقدار بولی است و فیلد غیر بولی است → mismatch
        if (valueToken.Type == TokenType.Boolean && targetType != typeof(bool))
        {
            message = $"Cannot compare boolean with non-boolean field.";
            return true;
        }

        // اگر مقدار یک شناسه غیر null و غیر بولی باشد (مثلاً چیزی مثل "abc" بدون نقل قول)
        if (valueToken.Type == TokenType.Identifier && targetType != typeof(string))
        {
            // اگر قرار بود مقدار واقعی باشد باید در نقل قول باشد
            message = $"Unquoted identifier '{valueToken.Value}' is not allowed. Use quotes for strings or 'null' for null.";
            return true;
        }

        return false;
    }

    private static Expression BuildStringMethod(Expression property, Expression constant, string method)
    {
        var notNull = Expression.NotEqual(property, Expression.Constant(null, typeof(string)));
        var call = Expression.Call(property, method, Type.EmptyTypes, constant);
        return Expression.AndAlso(notNull, call);
    }

    private static object? ConvertValue(string raw, Type type)
    {
        if (type == typeof(string))
            return raw;

        if (type == typeof(int))
            return int.Parse(raw, CultureInfo.InvariantCulture);
        if (type == typeof(long))
            return long.Parse(raw, CultureInfo.InvariantCulture);
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

        return Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
    }

    private Token Consume(ParsingContext ctx)
    {
        return ctx.Tokens[ctx.Position++];
    }
}