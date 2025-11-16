using System.Collections.Generic;

namespace GeoscientistToolkit.Scripting.GeoScript
{
    /// <summary>
    /// Base class for all AST nodes
    /// </summary>
    public abstract class ASTNode
    {
        public int Line { get; set; }
        public int Column { get; set; }
    }

    /// <summary>
    /// Represents a complete GeoScript program
    /// </summary>
    public class ProgramNode : ASTNode
    {
        public List<StatementNode> Statements { get; set; } = new List<StatementNode>();
    }

    /// <summary>
    /// Base class for all statement nodes
    /// </summary>
    public abstract class StatementNode : ASTNode
    {
    }

    /// <summary>
    /// WITH "dataset" DO operation TO "output" [THEN operation TO "output" ...]
    /// </summary>
    public class OperationStatementNode : StatementNode
    {
        public string DatasetName { get; set; }
        public List<OperationChainNode> Operations { get; set; } = new List<OperationChainNode>();
    }

    /// <summary>
    /// Single operation in a chain: OPERATION params TO "output"
    /// </summary>
    public class OperationChainNode : ASTNode
    {
        public string OperationName { get; set; }
        public List<ParameterNode> Parameters { get; set; } = new List<ParameterNode>();
        public string OutputName { get; set; }
    }

    /// <summary>
    /// WITH "dataset" LISTOPS
    /// </summary>
    public class ListOpsStatementNode : StatementNode
    {
        public string DatasetName { get; set; }
    }

    /// <summary>
    /// WITH "dataset" DISPTYPE
    /// </summary>
    public class DispTypeStatementNode : StatementNode
    {
        public string DatasetName { get; set; }
    }

    /// <summary>
    /// WITH "dataset" UNLOAD
    /// </summary>
    public class UnloadStatementNode : StatementNode
    {
        public string DatasetName { get; set; }
    }

    /// <summary>
    /// Pipeline syntax: "dataset" |> OPERATION params |> OPERATION params |> "output"
    /// </summary>
    public class PipelineStatementNode : StatementNode
    {
        public string InputDataset { get; set; }
        public List<PipelineOperationNode> Operations { get; set; } = new List<PipelineOperationNode>();
        public string OutputName { get; set; }
    }

    /// <summary>
    /// Single operation in a pipeline
    /// </summary>
    public class PipelineOperationNode : ASTNode
    {
        public string OperationName { get; set; }
        public List<ParameterNode> Parameters { get; set; } = new List<ParameterNode>();
    }

    /// <summary>
    /// Parameter for operations
    /// </summary>
    public class ParameterNode : ASTNode
    {
        public object Value { get; set; } // Can be string, number, or null (empty param like ",256")
        public bool IsEmpty { get; set; } // True for parameters like in "128," or ",256"
    }
}
