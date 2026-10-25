using Application.Common.Helpers;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Helpers;

public class FilterLexerTests
{
    [Fact]
    public void Tokenize_Empty_ReturnsOnlyEof()
    {
        var lexer = new FilterLexer("");
        var tokens = lexer.Tokenize();
        tokens.Should().ContainSingle().Which.Type.Should().Be(TokenType.Eof);
    }

    [Fact]
    public void Tokenize_Number_WithDecimal()
    {
        var lexer = new FilterLexer("3.14");
        var tokens = lexer.Tokenize();
        tokens.Should().HaveCount(2); // number + EOF
        tokens[0].Type.Should().Be(TokenType.Number);
        tokens[0].Value.Should().Be("3.14");
    }

    [Fact]
    public void Tokenize_NegativeNumber()
    {
        var lexer = new FilterLexer("-10");
        var tokens = lexer.Tokenize();
        tokens[0].Type.Should().Be(TokenType.Number);
        tokens[0].Value.Should().Be("-10");
    }

    [Fact]
    public void Tokenize_ParenthesesAndComma()
    {
        var lexer = new FilterLexer("(a,b)");
        var tokens = lexer.Tokenize();
        tokens[0].Type.Should().Be(TokenType.LeftParen);
        tokens[1].Type.Should().Be(TokenType.Identifier);
        tokens[2].Type.Should().Be(TokenType.Comma);
        tokens[3].Type.Should().Be(TokenType.Identifier);
        tokens[4].Type.Should().Be(TokenType.RightParen);
    }

    [Fact]
    public void Tokenize_BooleanTrueFalse()
    {
        var lexer = new FilterLexer("true false");
        var tokens = lexer.Tokenize();
        tokens[0].Type.Should().Be(TokenType.Boolean);
        tokens[0].Value.Should().Be("true");
        tokens[1].Type.Should().Be(TokenType.Boolean);
        tokens[1].Value.Should().Be("false");
    }

    [Fact]
    public void Tokenize_UnexpectedCharacter_Throws()
    {
        var lexer = new FilterLexer("test @");
        Action act = () => lexer.Tokenize();
        act.Should().Throw<Exception>().WithMessage("*Unexpected character*");
    }
}