using System;
using System.Collections.Generic;
using System.Text;

namespace GeoscientistToolkit.Scripting.GeoScript
{
    /// <summary>
    /// Lexical analyzer for GeoScript language
    /// </summary>
    public class GeoScriptLexer
    {
        private readonly string _source;
        private int _position = 0;
        private int _line = 1;
        private int _column = 1;

        private static readonly Dictionary<string, TokenType> _keywords = new Dictionary<string, TokenType>(StringComparer.OrdinalIgnoreCase)
        {
            { "WITH", TokenType.WITH },
            { "DO", TokenType.DO },
            { "TO", TokenType.TO },
            { "THEN", TokenType.THEN },
            { "LISTOPS", TokenType.LISTOPS },
            { "DISPTYPE", TokenType.DISPTYPE },
            { "UNLOAD", TokenType.UNLOAD }
        };

        public GeoScriptLexer(string source)
        {
            _source = source ?? string.Empty;
        }

        /// <summary>
        /// Tokenize the entire source code
        /// </summary>
        public List<GeoScriptToken> Tokenize()
        {
            var tokens = new List<GeoScriptToken>();

            while (!IsAtEnd())
            {
                SkipWhitespaceExceptNewline();

                if (IsAtEnd())
                    break;

                var token = NextToken();
                if (token != null)
                    tokens.Add(token);
            }

            tokens.Add(new GeoScriptToken(TokenType.EOF, "", _line, _column));
            return tokens;
        }

        /// <summary>
        /// Get the next token from the source
        /// </summary>
        private GeoScriptToken NextToken()
        {
            if (IsAtEnd())
                return new GeoScriptToken(TokenType.EOF, "", _line, _column);

            char c = Peek();
            int startLine = _line;
            int startColumn = _column;

            // Handle newlines
            if (c == '\n' || c == '\r')
            {
                Advance();
                if (c == '\r' && Peek() == '\n')
                    Advance();
                _line++;
                _column = 1;
                return new GeoScriptToken(TokenType.NEWLINE, "\\n", startLine, startColumn);
            }

            // Handle strings
            if (c == '"')
                return ReadString(startLine, startColumn);

            // Handle pipe operator |>
            if (c == '|')
            {
                Advance();
                if (Peek() == '>')
                {
                    Advance();
                    return new GeoScriptToken(TokenType.PIPE, "|>", startLine, startColumn);
                }
                return new GeoScriptToken(TokenType.UNKNOWN, "|", startLine, startColumn);
            }

            // Handle comma
            if (c == ',')
            {
                Advance();
                return new GeoScriptToken(TokenType.COMMA, ",", startLine, startColumn);
            }

            // Handle numbers
            if (char.IsDigit(c) || (c == '-' && char.IsDigit(PeekNext())))
                return ReadNumber(startLine, startColumn);

            // Handle identifiers and keywords
            if (char.IsLetter(c) || c == '_')
                return ReadIdentifier(startLine, startColumn);

            // Handle comments
            if (c == '#' || (c == '/' && PeekNext() == '/'))
            {
                SkipComment();
                return NextToken();
            }

            // Unknown character
            Advance();
            return new GeoScriptToken(TokenType.UNKNOWN, c.ToString(), startLine, startColumn);
        }

        private GeoScriptToken ReadString(int startLine, int startColumn)
        {
            Advance(); // Skip opening quote
            var sb = new StringBuilder();

            while (!IsAtEnd() && Peek() != '"')
            {
                if (Peek() == '\\')
                {
                    Advance();
                    if (!IsAtEnd())
                    {
                        char escaped = Peek();
                        switch (escaped)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            default: sb.Append(escaped); break;
                        }
                        Advance();
                    }
                }
                else
                {
                    sb.Append(Peek());
                    Advance();
                }
            }

            if (!IsAtEnd())
                Advance(); // Skip closing quote

            return new GeoScriptToken(TokenType.STRING, sb.ToString(), startLine, startColumn);
        }

        private GeoScriptToken ReadNumber(int startLine, int startColumn)
        {
            var sb = new StringBuilder();

            if (Peek() == '-')
            {
                sb.Append(Peek());
                Advance();
            }

            while (!IsAtEnd() && (char.IsDigit(Peek()) || Peek() == '.'))
            {
                sb.Append(Peek());
                Advance();
            }

            return new GeoScriptToken(TokenType.NUMBER, sb.ToString(), startLine, startColumn);
        }

        private GeoScriptToken ReadIdentifier(int startLine, int startColumn)
        {
            var sb = new StringBuilder();

            while (!IsAtEnd() && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
            {
                sb.Append(Peek());
                Advance();
            }

            string identifier = sb.ToString();

            // Check if it's a keyword
            if (_keywords.TryGetValue(identifier, out TokenType keywordType))
                return new GeoScriptToken(keywordType, identifier, startLine, startColumn);

            return new GeoScriptToken(TokenType.IDENTIFIER, identifier, startLine, startColumn);
        }

        private void SkipComment()
        {
            while (!IsAtEnd() && Peek() != '\n' && Peek() != '\r')
                Advance();
        }

        private void SkipWhitespaceExceptNewline()
        {
            while (!IsAtEnd())
            {
                char c = Peek();
                if (c == ' ' || c == '\t')
                    Advance();
                else
                    break;
            }
        }

        private char Peek()
        {
            if (IsAtEnd())
                return '\0';
            return _source[_position];
        }

        private char PeekNext()
        {
            if (_position + 1 >= _source.Length)
                return '\0';
            return _source[_position + 1];
        }

        private void Advance()
        {
            if (!IsAtEnd())
            {
                _position++;
                _column++;
            }
        }

        private bool IsAtEnd()
        {
            return _position >= _source.Length;
        }
    }
}
