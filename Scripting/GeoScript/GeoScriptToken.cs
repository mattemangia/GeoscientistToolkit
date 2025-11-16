namespace GeoscientistToolkit.Scripting.GeoScript
{
    /// <summary>
    /// Token types for GeoScript lexer
    /// </summary>
    public enum TokenType
    {
        // Keywords
        WITH,
        DO,
        TO,
        THEN,
        LISTOPS,
        DISPTYPE,
        UNLOAD,

        // Operators
        PIPE,           // |>
        COMMA,          // ,

        // Literals
        STRING,         // "dataset_name"
        NUMBER,         // 123, 123.45
        IDENTIFIER,     // operation names, parameters

        // Special
        EOF,
        NEWLINE,
        UNKNOWN
    }

    /// <summary>
    /// Represents a token in the GeoScript language
    /// </summary>
    public class GeoScriptToken
    {
        public TokenType Type { get; set; }
        public string Value { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }

        public GeoScriptToken(TokenType type, string value, int line, int column)
        {
            Type = type;
            Value = value;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return $"{Type}({Value}) at {Line}:{Column}";
        }
    }
}
