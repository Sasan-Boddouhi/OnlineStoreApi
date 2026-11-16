using System.Globalization;
using System.Linq.Expressions;
using Application.Common.Helpers;
using Application.Common.Specifications;

namespace Application.Common.Queries;

/// <summary>
/// تجزیه‌گر زبان کوچک فیلتر (پارامتر <c>filter</c> در فهرست‌ها) به یک <see cref="Expression"/> قابل ترجمه با EF.
/// <para>
/// دستور زبان (Batch 60، F-24) — کلیدواژه‌ها و عملگرها به حروف بزرگ و کوچک حساس نیستند:
/// <code>
/// filter     := or_expr EOF
/// or_expr    := and_expr ( "or" and_expr )*
/// and_expr   := unary ( "and" unary )*
/// unary      := "not" unary | primary
/// primary    := "(" or_expr ")" | comparison
/// comparison := FIELD OP VALUE
/// FIELD      := نام فیلد مجاز، با نقطه برای ناوبری (مثل subcategory.subcategoryname)
/// OP         := eq | ne | gt | ge | lt | le | contains | startswith | endswith
/// VALUE      := 'متن' | "متن" | عدد | true | false | null
/// </code>
/// اولویت: <c>not</c> سپس <c>and</c> سپس <c>or</c>؛ پرانتز اولویت را تغییر می‌دهد.
/// </para>
/// <para>
/// قواعد: هر توکنی که دستور زبان مصرف نکند (توکن اضافه، مقدار جاافتاده، پرانتز بازمانده، ویرگول)
/// کل فیلتر را رد می‌کند؛ هیچ فیلتری نیمه‌کاره اعمال نمی‌شود. مقدار متنی باید در کوتیشن باشد
/// (شناسهٔ بی‌کوتیشن جز <c>null</c> پذیرفته نیست). <c>null</c> فقط با <c>eq</c>/<c>ne</c> و برای فیلد nullable.
/// عملگرهای متنی فقط برای فیلد متنی. سقف‌ها: <see cref="MaxLength"/> نویسه، <see cref="MaxComparisons"/> مقایسه،
/// عمق <see cref="MaxDepth"/> برای پرانتز و <c>not</c>.
/// </para>
/// خطا همیشه یک <see cref="ParseError"/> با کد <c>filter_parse_error</c> و پیام فارسی است؛ جزئیات داخلی برنمی‌گردد.
/// </summary>
public sealed class FilterParser
{
    /// <summary>بیشترین طول متن فیلتر (نویسه).</summary>
    public const int MaxLength = 1000;

    /// <summary>بیشترین تعداد مقایسه در یک فیلتر.</summary>
    public const int MaxComparisons = 20;

    /// <summary>بیشترین عمق تودرتویی پرانتز و <c>not</c>.</summary>
    public const int MaxDepth = 8;

    private const string ErrorCode = "filter_parse_error";

    private sealed class ParsingContext
    {
        public required List<Token> Tokens { get; init; }
        public required HashSet<string>? AllowedFields { get; init; }
        public required ParameterExpression Parameter { get; init; }
        public int Position { get; set; }
        public int Depth { get; set; }
        public int Comparisons { get; set; }

        public Token Current => Tokens[Position];
    }

    /// <summary>خطای دستوری با پیام فارسی و موقعیت در متن فیلتر.</summary>
    private sealed class FilterSyntaxException : Exception
    {
        public FilterSyntaxException(string message, int? position = null) : base(message) => Position = position;
        public int? Position { get; }
    }

    public ParseResult<Expression<Func<T, bool>>> TryParse<T>(string filter, QueryParseContext<T> context) where T : class
    {
        try
        {
            if (filter.Length > MaxLength)
                throw new FilterSyntaxException($"طول فیلتر نباید بیش از {MaxLength} نویسه باشد.");

            List<Token> tokens;
            try
            {
                tokens = new FilterLexer(filter).Tokenize();
            }
            catch (InvalidOperationException)
            {
                throw new FilterSyntaxException("متن فیلتر نویسهٔ نامعتبر یا رشتهٔ بسته‌نشده دارد.");
            }

            var ctx = new ParsingContext
            {
                Tokens = tokens,
                AllowedFields = BuildAllowedSet(context),
                Parameter = Expression.Parameter(typeof(T), "x")
            };

            var body = ParseOr<T>(ctx);

            if (ctx.Current.Type != TokenType.Eof)
                throw new FilterSyntaxException(
                    $"توکن اضافهٔ «{ctx.Current.Value}» در موقعیت {ctx.Current.Position}؛ شرط‌ها را با and یا or به هم وصل کنید.",
                    ctx.Current.Position);

            return ParseResult<Expression<Func<T, bool>>>.Ok(Expression.Lambda<Func<T, bool>>(body, ctx.Parameter));
        }
        catch (FilterSyntaxException ex)
        {
            return Fail<T>(ex.Message, ex.Position);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return Fail<T>("مقدار با نوع فیلد سازگار نیست.", null);
        }
        catch (Exception)
        {
            // جزئیات داخلی (نام نوع‌ها، پیام Expression) عمداً برنمی‌گردد.
            return Fail<T>("فیلتر نامعتبر است.", null);
        }
    }

    private static ParseResult<Expression<Func<T, bool>>> Fail<T>(string message, int? position)
        => ParseResult<Expression<Func<T, bool>>>.Fail(
            new ParseError { Code = ErrorCode, Message = message, Target = "filter", Position = position });

    // ==================== دستور زبان ====================

    private Expression ParseOr<T>(ParsingContext ctx) where T : class
    {
        var left = ParseAnd<T>(ctx);
        while (ctx.Current.Type == TokenType.Or)
        {
            ctx.Position++;
            left = Expression.OrElse(left, ParseAnd<T>(ctx));
        }
        return left;
    }

    private Expression ParseAnd<T>(ParsingContext ctx) where T : class
    {
        var left = ParseUnary<T>(ctx);
        while (ctx.Current.Type == TokenType.And)
        {
            ctx.Position++;
            left = Expression.AndAlso(left, ParseUnary<T>(ctx));
        }
        return left;
    }

    private Expression ParseUnary<T>(ParsingContext ctx) where T : class
    {
        if (ctx.Current.Type == TokenType.Not)
        {
            ctx.Position++;
            Enter(ctx);
            var operand = ParseUnary<T>(ctx);
            ctx.Depth--;
            return Expression.Not(operand);
        }
        return ParsePrimary<T>(ctx);
    }

    private Expression ParsePrimary<T>(ParsingContext ctx) where T : class
    {
        if (ctx.Current.Type == TokenType.LeftParen)
        {
            var open = ctx.Current;
            ctx.Position++;
            Enter(ctx);
            var inner = ParseOr<T>(ctx);
            if (ctx.Current.Type != TokenType.RightParen)
                throw new FilterSyntaxException($"پرانتز بازشده در موقعیت {open.Position} بسته نشده است.", open.Position);
            ctx.Position++;
            ctx.Depth--;
            return inner;
        }
        return ParseComparison<T>(ctx);
    }

    private static void Enter(ParsingContext ctx)
    {
        if (++ctx.Depth > MaxDepth)
            throw new FilterSyntaxException($"عمق تودرتویی فیلتر نباید بیش از {MaxDepth} باشد.");
    }

    private Expression ParseComparison<T>(ParsingContext ctx) where T : class
    {
        if (++ctx.Comparisons > MaxComparisons)
            throw new FilterSyntaxException($"فیلتر نباید بیش از {MaxComparisons} شرط داشته باشد.");

        var fieldToken = ctx.Current;
        if (fieldToken.Type != TokenType.Identifier)
            throw new FilterSyntaxException(
                fieldToken.Type == TokenType.Eof
                    ? "فیلتر ناقص است؛ نام فیلد انتظار می‌رفت."
                    : $"در موقعیت {fieldToken.Position} نام فیلد انتظار می‌رفت، نه «{fieldToken.Value}».",
                fieldToken.Position);
        ctx.Position++;
        var field = fieldToken.Value;

        if (ctx.AllowedFields != null && !ctx.AllowedFields.Contains(field))
            throw new FilterSyntaxException($"فیلتر روی فیلد «{field}» مجاز نیست.", fieldToken.Position);

        var opToken = ctx.Current;
        if (!IsOperator(opToken.Type))
            throw new FilterSyntaxException(
                opToken.Type == TokenType.Eof
                    ? $"پس از فیلد «{field}» عملگر انتظار می‌رفت."
                    : $"عملگر «{opToken.Value}» پشتیبانی نمی‌شود.",
                opToken.Position);
        ctx.Position++;
        var op = opToken.Value.ToLowerInvariant();

        var valueToken = ctx.Current;
        if (!IsValue(valueToken))
            throw new FilterSyntaxException(
                valueToken.Type == TokenType.Eof
                    ? $"پس از «{field} {op}» مقدار انتظار می‌رفت."
                    : $"مقدار «{valueToken.Value}» در موقعیت {valueToken.Position} نامعتبر است؛ متن را در کوتیشن بگذارید.",
                valueToken.Position);
        ctx.Position++;

        var property = BuildPropertyAccess<T>(field, ctx.Parameter, fieldToken.Position);

        if (IsNullToken(valueToken))
            return BuildNullComparison(property, field, op);

        var underlyingType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
        EnsureTypeCompatibility(valueToken, underlyingType, field);

        if ((op is "contains" or "startswith" or "endswith") && underlyingType != typeof(string))
            throw new FilterSyntaxException($"عملگر «{op}» فقط برای فیلد متنی است.", opToken.Position);

        var converted = ConvertValue(valueToken.Value, underlyingType);
        var constant = Expression.Constant(converted, property.Type);
        return BuildOperatorBody(op, property, constant);
    }

    private static bool IsOperator(TokenType type) => type is TokenType.Eq or TokenType.Ne or TokenType.Gt
        or TokenType.Ge or TokenType.Lt or TokenType.Le or TokenType.Contains or TokenType.StartsWith or TokenType.EndsWith;

    private static bool IsValue(Token token) => token.Type is TokenType.String or TokenType.Number or TokenType.Boolean
        || (token.Type == TokenType.Identifier && IsNullToken(token));

    private static HashSet<string>? BuildAllowedSet<T>(QueryParseContext<T> context) where T : class
    {
        if (!context.AllowedFields.Any()) return null;
        return new HashSet<string>(context.AllowedFields,
            context.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    private static Expression BuildPropertyAccess<T>(string field, ParameterExpression param, int position) where T : class
    {
        LambdaExpression lambda;
        try
        {
            lambda = ExpressionBuilder.BuildPropertyLambdaCached<T>(field);
        }
        catch (ArgumentException)
        {
            throw new FilterSyntaxException($"فیلد «{field}» وجود ندارد.", position);
        }
        return new ReplaceExpressionVisitor(lambda.Parameters[0], param).Visit(lambda.Body)!;
    }

    private static bool IsNullToken(Token t)
        => t.Type == TokenType.Identifier && t.Value.Equals("null", StringComparison.OrdinalIgnoreCase);

    private static Expression BuildNullComparison(Expression prop, string field, string op)
    {
        var type = prop.Type;
        if (Nullable.GetUnderlyingType(type) == null && type.IsValueType)
            throw new FilterSyntaxException($"فیلد «{field}» nullable نیست و با null مقایسه نمی‌شود.");

        var nullConst = Expression.Constant(null, type);
        return op switch
        {
            "eq" => Expression.Equal(prop, nullConst),
            "ne" => Expression.NotEqual(prop, nullConst),
            _ => throw new FilterSyntaxException($"عملگر «{op}» با null به کار نمی‌رود؛ فقط eq یا ne.")
        };
    }

    private static void EnsureTypeCompatibility(Token valueToken, Type targetType, string field)
    {
        var mismatch = valueToken.Type switch
        {
            TokenType.String => targetType != typeof(string),
            TokenType.Number => targetType == typeof(string) || targetType == typeof(bool),
            TokenType.Boolean => targetType != typeof(bool),
            _ => false
        };

        if (mismatch)
            throw new FilterSyntaxException(
                targetType == typeof(string)
                    ? $"مقدار فیلد متنی «{field}» باید در کوتیشن باشد."
                    : $"مقدار «{valueToken.Value}» با نوع فیلد «{field}» سازگار نیست.",
                valueToken.Position);
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
        if (!OperatorFactories.TryGetValue(op, out var factory))
            throw new FilterSyntaxException($"عملگر «{op}» پشتیبانی نمی‌شود.");

        try
        {
            return factory(prop, constant);
        }
        catch (InvalidOperationException)
        {
            // مثل gt روی فیلد متنی یا bool
            throw new FilterSyntaxException($"عملگر «{op}» برای این فیلد تعریف نشده است.");
        }
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
        [typeof(int)] = raw => int.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture),
        [typeof(long)] = raw => long.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture),
        [typeof(decimal)] = raw => decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture),
        [typeof(double)] = raw => double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture),
        [typeof(float)] = raw => float.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture),
        [typeof(bool)] = raw => bool.Parse(raw),
        [typeof(Guid)] = raw => Guid.Parse(raw),
        [typeof(DateTime)] = raw => DateTime.Parse(raw, CultureInfo.InvariantCulture)
    };

    private static object? ConvertValue(string raw, Type type)
    {
        if (TypeConverters.TryGetValue(type, out var converter))
            return converter(raw);

        if (type.IsEnum)
            throw new FilterSyntaxException("فیلتر روی فیلد شمارشی پشتیبانی نمی‌شود.");

        return Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
    }
}
