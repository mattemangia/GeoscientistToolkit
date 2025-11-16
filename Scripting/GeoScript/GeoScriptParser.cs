using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoscientistToolkit.Scripting.GeoScript
{
    /// <summary>
    /// Parser for GeoScript language
    /// </summary>
    public class GeoScriptParser
    {
        private List<GeoScriptToken> _tokens;
        private int _current = 0;

        public List<string> Errors { get; private set; } = new List<string>();

        public GeoScriptParser(List<GeoScriptToken> tokens)
        {
            _tokens = tokens ?? new List<GeoScriptToken>();
        }

        /// <summary>
        /// Parse the tokens into an AST
        /// </summary>
        public ProgramNode Parse()
        {
            var program = new ProgramNode();
            Errors.Clear();

            while (!IsAtEnd())
            {
                // Skip newlines
                if (Match(TokenType.NEWLINE))
                    continue;

                try
                {
                    var statement = ParseStatement();
                    if (statement != null)
                        program.Statements.Add(statement);
                }
                catch (Exception ex)
                {
                    Errors.Add($"Parse error at line {CurrentToken().Line}: {ex.Message}");
                    Synchronize();
                }
            }

            return program;
        }

        private StatementNode ParseStatement()
        {
            // Skip newlines
            while (Match(TokenType.NEWLINE)) { }

            if (IsAtEnd())
                return null;

            // Check for WITH statement
            if (Match(TokenType.WITH))
                return ParseWithStatement();

            // Check for pipeline syntax: "dataset" |> ...
            if (Check(TokenType.STRING))
            {
                var token = Peek();
                if (PeekNext().Type == TokenType.PIPE)
                    return ParsePipelineStatement();
            }

            throw new Exception($"Unexpected token: {CurrentToken().Value}");
        }

        private StatementNode ParseWithStatement()
        {
            // WITH "dataset" ...
            var datasetToken = Consume(TokenType.STRING, "Expected dataset name after WITH");
            string datasetName = datasetToken.Value;

            // Check what comes next
            if (Match(TokenType.LISTOPS))
            {
                return new ListOpsStatementNode
                {
                    DatasetName = datasetName,
                    Line = datasetToken.Line,
                    Column = datasetToken.Column
                };
            }

            if (Match(TokenType.DISPTYPE))
            {
                return new DispTypeStatementNode
                {
                    DatasetName = datasetName,
                    Line = datasetToken.Line,
                    Column = datasetToken.Column
                };
            }

            if (Match(TokenType.UNLOAD))
            {
                return new UnloadStatementNode
                {
                    DatasetName = datasetName,
                    Line = datasetToken.Line,
                    Column = datasetToken.Column
                };
            }

            // Must be an operation statement
            Consume(TokenType.DO, "Expected DO, LISTOPS, DISPTYPE, or UNLOAD after dataset name");

            var operations = new List<OperationChainNode>();

            // Parse first operation
            operations.Add(ParseOperationChain());

            // Parse additional operations with THEN
            while (Match(TokenType.THEN))
            {
                operations.Add(ParseOperationChain());
            }

            return new OperationStatementNode
            {
                DatasetName = datasetName,
                Operations = operations,
                Line = datasetToken.Line,
                Column = datasetToken.Column
            };
        }

        private OperationChainNode ParseOperationChain()
        {
            // OPERATION_NAME params TO "output"
            var operationToken = Consume(TokenType.IDENTIFIER, "Expected operation name");
            var operation = new OperationChainNode
            {
                OperationName = operationToken.Value,
                Line = operationToken.Line,
                Column = operationToken.Column
            };

            // Parse parameters until we hit TO
            operation.Parameters = ParseParameters();

            // TO "output"
            Consume(TokenType.TO, "Expected TO after operation parameters");
            var outputToken = Consume(TokenType.STRING, "Expected output dataset name after TO");
            operation.OutputName = outputToken.Value;

            return operation;
        }

        private List<ParameterNode> ParseParameters()
        {
            var parameters = new List<ParameterNode>();

            // Parameters are comma-separated and end when we hit TO or THEN
            while (!Check(TokenType.TO) && !Check(TokenType.THEN) && !IsAtEnd())
            {
                // Skip newlines
                if (Match(TokenType.NEWLINE))
                    continue;

                if (Check(TokenType.TO) || Check(TokenType.THEN))
                    break;

                var param = new ParameterNode
                {
                    Line = CurrentToken().Line,
                    Column = CurrentToken().Column
                };

                // Check for empty parameter (starts with comma)
                if (Check(TokenType.COMMA))
                {
                    param.IsEmpty = true;
                    param.Value = null;
                }
                else if (Match(TokenType.NUMBER))
                {
                    param.Value = double.Parse(Previous().Value);
                    param.IsEmpty = false;
                }
                else if (Match(TokenType.STRING))
                {
                    param.Value = Previous().Value;
                    param.IsEmpty = false;
                }
                else if (Match(TokenType.IDENTIFIER))
                {
                    param.Value = Previous().Value;
                    param.IsEmpty = false;
                }
                else
                {
                    // If we hit something unexpected, break
                    break;
                }

                parameters.Add(param);

                // Optional comma
                Match(TokenType.COMMA);
            }

            return parameters;
        }

        private PipelineStatementNode ParsePipelineStatement()
        {
            // "dataset" |> OPERATION params |> OPERATION params |> "output"
            var inputToken = Consume(TokenType.STRING, "Expected input dataset name");
            var pipeline = new PipelineStatementNode
            {
                InputDataset = inputToken.Value,
                Line = inputToken.Line,
                Column = inputToken.Column
            };

            // Parse pipeline operations
            while (Match(TokenType.PIPE))
            {
                // Skip newlines
                while (Match(TokenType.NEWLINE)) { }

                // Check if next is a string (output)
                if (Check(TokenType.STRING))
                {
                    var outputToken = Consume(TokenType.STRING, "Expected output name");
                    pipeline.OutputName = outputToken.Value;
                    break;
                }

                // Parse operation
                var operationToken = Consume(TokenType.IDENTIFIER, "Expected operation name after |>");
                var operation = new PipelineOperationNode
                {
                    OperationName = operationToken.Value,
                    Line = operationToken.Line,
                    Column = operationToken.Column
                };

                // Parse parameters until next pipe or end
                operation.Parameters = ParsePipelineParameters();
                pipeline.Operations.Add(operation);
            }

            return pipeline;
        }

        private List<ParameterNode> ParsePipelineParameters()
        {
            var parameters = new List<ParameterNode>();

            // Parameters are comma-separated and end when we hit |> or end of line
            while (!Check(TokenType.PIPE) && !Check(TokenType.NEWLINE) && !IsAtEnd())
            {
                var param = new ParameterNode
                {
                    Line = CurrentToken().Line,
                    Column = CurrentToken().Column
                };

                // Check for empty parameter
                if (Check(TokenType.COMMA))
                {
                    param.IsEmpty = true;
                    param.Value = null;
                }
                else if (Match(TokenType.NUMBER))
                {
                    param.Value = double.Parse(Previous().Value);
                    param.IsEmpty = false;
                }
                else if (Match(TokenType.STRING))
                {
                    param.Value = Previous().Value;
                    param.IsEmpty = false;
                }
                else if (Match(TokenType.IDENTIFIER))
                {
                    param.Value = Previous().Value;
                    param.IsEmpty = false;
                }
                else
                {
                    break;
                }

                parameters.Add(param);

                // Optional comma
                Match(TokenType.COMMA);
            }

            return parameters;
        }

        private void Synchronize()
        {
            Advance();

            while (!IsAtEnd())
            {
                if (Previous().Type == TokenType.NEWLINE)
                    return;

                if (Check(TokenType.WITH))
                    return;

                Advance();
            }
        }

        #region Helper Methods

        private bool Match(params TokenType[] types)
        {
            foreach (var type in types)
            {
                if (Check(type))
                {
                    Advance();
                    return true;
                }
            }
            return false;
        }

        private bool Check(TokenType type)
        {
            if (IsAtEnd())
                return false;
            return Peek().Type == type;
        }

        private GeoScriptToken Advance()
        {
            if (!IsAtEnd())
                _current++;
            return Previous();
        }

        private bool IsAtEnd()
        {
            return Peek().Type == TokenType.EOF;
        }

        private GeoScriptToken Peek()
        {
            return _current < _tokens.Count ? _tokens[_current] : new GeoScriptToken(TokenType.EOF, "", 0, 0);
        }

        private GeoScriptToken PeekNext()
        {
            int next = _current + 1;
            return next < _tokens.Count ? _tokens[next] : new GeoScriptToken(TokenType.EOF, "", 0, 0);
        }

        private GeoScriptToken Previous()
        {
            return _current > 0 ? _tokens[_current - 1] : new GeoScriptToken(TokenType.EOF, "", 0, 0);
        }

        private GeoScriptToken CurrentToken()
        {
            return Peek();
        }

        private GeoScriptToken Consume(TokenType type, string message)
        {
            if (Check(type))
                return Advance();

            throw new Exception($"{message}. Got: {Peek().Type}({Peek().Value})");
        }

        #endregion
    }
}
