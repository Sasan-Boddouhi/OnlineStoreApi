using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Application.Common.Specifications;

namespace Application.Common.Helpers
{
    public class FilterParser
    {
        private List<Token> _tokens;
        private int _position;
        private IReadOnlyList<string> _allowedFields;

        public Expression<Func<T, bool>> Parse<T>(string filter, IReadOnlyList<string> allowedFields)
        {
            _allowedFields = allowedFields;
            var lexer = new FilterLexer(filter);
            _tokens = lexer.Tokenize();
            _position = 0;
            var expression = ParseExpression<T>();
            if (!IsAtEnd() && Peek().Type != TokenType.Eof)
                throw new Exception($"Unexpected tokens at position {Peek().Position}");
            return expression;
        }

        private Expression<Func<T, bool>> ParseExpression<T>()
        {
            var left = ParseTerm<T>();
            while (Match(TokenType.And) || Match(TokenType.Or))
            {
                var op = Previous().Type;
                var right = ParseTerm<T>();
                left = op == TokenType.And ? left.And(right) : left.Or(right);
            }
            return left;
        }

        private Expression<Func<T, bool>> ParseTerm<T>()
        {
            if (Match(TokenType.Not))
            {
                var factor = ParseFactor<T>();
                return factor.Not(); // نیاز به متد Not در PredicateBuilder
            }
            return ParseFactor<T>();
        }

        private Expression<Func<T, bool>> ParseFactor<T>()
        {
            if (Match(TokenType.LeftParen))
            {
                var expr = ParseExpression<T>();
                Consume(TokenType.RightParen, "Expected ')'");
                return expr;
            }
            return ParseComparison<T>();
        }

        private Expression<Func<T, bool>> ParseComparison<T>()
        {
            // نام فیلد
            var fieldToken = Consume(TokenType.Identifier, "Expected field name");
            var fieldPath = fieldToken.Value;

            // اعتبارسنجی فیلد
            if (_allowedFields != null &&
                !_allowedFields.Contains(fieldPath,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new Exception($"Field '{fieldPath}' is not allowed");
            }

            // عملگر
            var opToken = Consume(new[] {
                TokenType.Eq, TokenType.Ne, TokenType.Gt, TokenType.Ge,
                TokenType.Lt, TokenType.Le, TokenType.Contains,
                TokenType.StartsWith, TokenType.EndsWith
            }, "Expected operator");

            // مقدار
            var valueToken = Consume(new[] { TokenType.String, TokenType.Number, TokenType.Boolean }, "Expected value");

            // ساخت Expression
            var parameter = Expression.Parameter(typeof(T), "x");
            var lambda = ExpressionBuilder.BuildPropertyLambdaCached<T>(fieldPath);
            var property = lambda.Body; // دسترسی به بدنه

            // تبدیل مقدار به نوع مناسب
            object? convertedValue;
            if (valueToken.Type == TokenType.String)
                convertedValue = valueToken.Value;
            else if (valueToken.Type == TokenType.Number)
            {
                if (property.Type == typeof(int))
                    convertedValue = int.Parse(valueToken.Value);
                else if (property.Type == typeof(decimal))
                    convertedValue = decimal.Parse(valueToken.Value);
                else if (property.Type == typeof(double))
                    convertedValue = double.Parse(valueToken.Value);
                else if (property.Type == typeof(float))
                    convertedValue = float.Parse(valueToken.Value);
                else
                    convertedValue = Convert.ChangeType(valueToken.Value, property.Type);
            }
            else if (valueToken.Type == TokenType.Boolean)
                convertedValue = bool.Parse(valueToken.Value);
            else
                convertedValue = null;

            var constant = Expression.Constant(convertedValue, property.Type);

            Expression body = opToken.Type switch
            {
                TokenType.Eq => Expression.Equal(property, constant),
                TokenType.Ne => Expression.NotEqual(property, constant),
                TokenType.Gt => Expression.GreaterThan(property, constant),
                TokenType.Ge => Expression.GreaterThanOrEqual(property, constant),
                TokenType.Lt => Expression.LessThan(property, constant),
                TokenType.Le => Expression.LessThanOrEqual(property, constant),
                TokenType.Contains when property.Type == typeof(string) =>
                    Expression.Call(property, typeof(string).GetMethod("Contains", new[] { typeof(string) })!, constant),
                TokenType.StartsWith when property.Type == typeof(string) =>
                    Expression.Call(property, typeof(string).GetMethod("StartsWith", new[] { typeof(string) })!, constant),
                TokenType.EndsWith when property.Type == typeof(string) =>
                    Expression.Call(property, typeof(string).GetMethod("EndsWith", new[] { typeof(string) })!, constant),
                _ => throw new NotSupportedException($"Operator {opToken.Type} not supported for type {property.Type}")
            };

            return Expression.Lambda<Func<T, bool>>(body, parameter);
        }

        // متدهای کمکی برای پیمایش توکن‌ها
        private Token Peek() => _tokens[_position];
        private Token Previous() => _tokens[_position - 1];
        private bool IsAtEnd() => _position >= _tokens.Count - 1; // آخرین توکن Eof است
        private bool Check(TokenType type) => !IsAtEnd() && Peek().Type == type;

        private bool Match(TokenType type)
        {
            if (Check(type))
            {
                _position++;
                return true;
            }
            return false;
        }

        private bool MatchAny(TokenType[] types)
        {
            foreach (var type in types)
                if (Check(type))
                {
                    _position++;
                    return true;
                }
            return false;
        }

        private Token Consume(TokenType type, string message)
        {
            if (Check(type))
                return _tokens[_position++];
            throw new Exception($"{message} at position {Peek().Position}");
        }

        private Token Consume(TokenType[] types, string message)
        {
            if (MatchAny(types))
                return Previous();
            throw new Exception($"{message} at position {Peek().Position}");
        }
    }
}