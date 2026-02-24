using System.Collections.Generic;
using System.Text;

namespace Application.Common.Helpers
{
    public enum TokenType
    {
        Identifier, String, Number, Boolean,
        And, Or, Not,
        LeftParen, RightParen,
        Eq, Ne, Gt, Ge, Lt, Le, Contains, StartsWith, EndsWith,
        Comma, Eof
    }

    public class Token
    {
        public TokenType Type { get; set; }
        public string Value { get; set; }
        public int Position { get; set; }
    }

    public class FilterLexer
    {
        private readonly string _input;
        private int _position;

        public FilterLexer(string input)
        {
            _input = input;
            _position = 0;
        }

        public List<Token> Tokenize()
        {
            var tokens = new List<Token>();
            while (_position < _input.Length)
            {
                var ch = _input[_position];
                if (char.IsWhiteSpace(ch))
                {
                    _position++;
                    continue;
                }

                if (ch == '(')
                {
                    tokens.Add(new Token { Type = TokenType.LeftParen, Value = "(", Position = _position });
                    _position++;
                    continue;
                }
                if (ch == ')')
                {
                    tokens.Add(new Token { Type = TokenType.RightParen, Value = ")", Position = _position });
                    _position++;
                    continue;
                }
                if (ch == ',')
                {
                    tokens.Add(new Token { Type = TokenType.Comma, Value = ",", Position = _position });
                    _position++;
                    continue;
                }
                if (ch == '\'' || ch == '"')
                {
                    tokens.Add(ReadString());
                    continue;
                }
                if (char.IsDigit(ch) || ch == '-' || ch == '.')
                {
                    tokens.Add(ReadNumber());
                    continue;
                }
                if (char.IsLetter(ch) || ch == '_')
                {
                    tokens.Add(ReadIdentifierOrKeyword());
                    continue;
                }
                throw new Exception($"Unexpected character '{ch}' at position {_position}");
            }
            tokens.Add(new Token { Type = TokenType.Eof, Value = "", Position = _position });
            return tokens;
        }

        private Token ReadString()
        {
            var quote = _input[_position];
            var start = _position;
            _position++; // skip opening quote
            var sb = new StringBuilder();
            while (_position < _input.Length && _input[_position] != quote)
            {
                sb.Append(_input[_position]);
                _position++;
            }
            if (_position >= _input.Length)
                throw new Exception($"Unterminated string at position {start}");
            _position++; // skip closing quote
            return new Token
            {
                Type = TokenType.String,
                Value = sb.ToString(),
                Position = start
            };
        }

        private Token ReadNumber()
        {
            var start = _position;
            var sb = new StringBuilder();
            while (_position < _input.Length && (char.IsDigit(_input[_position]) || _input[_position] == '.' || _input[_position] == '-'))
            {
                sb.Append(_input[_position]);
                _position++;
            }
            return new Token
            {
                Type = TokenType.Number,
                Value = sb.ToString(),
                Position = start
            };
        }

        private Token ReadIdentifierOrKeyword()
        {
            var start = _position;
            var sb = new StringBuilder();
            while (_position < _input.Length && (char.IsLetterOrDigit(_input[_position]) || _input[_position] == '_' || _input[_position] == '.'))
            {
                sb.Append(_input[_position]);
                _position++;
            }
            var value = sb.ToString();
            var type = value.ToLower() switch
            {
                "and" => TokenType.And,
                "or" => TokenType.Or,
                "not" => TokenType.Not,
                "eq" => TokenType.Eq,
                "ne" => TokenType.Ne,
                "gt" => TokenType.Gt,
                "ge" => TokenType.Ge,
                "lt" => TokenType.Lt,
                "le" => TokenType.Le,
                "contains" => TokenType.Contains,
                "startswith" => TokenType.StartsWith,
                "endswith" => TokenType.EndsWith,
                "true" => TokenType.Boolean,
                "false" => TokenType.Boolean,
                _ => TokenType.Identifier
            };
            return new Token { Type = type, Value = value, Position = start };
        }
    }
}