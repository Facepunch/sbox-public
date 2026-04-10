namespace Sandbox;

using System.Text.Json.Serialization;

public partial class Doo
{
	/// <summary>
	/// Base class for all value expressions used as arguments and assignments within blocks.
	/// </summary>
	[JsonDerivedType( typeof( LiteralExpression ), "lit" )]
	[JsonDerivedType( typeof( VariableExpression ), "var" )]
	[Expose]
	public abstract class Expression
	{
		/// <summary>
		/// Evaluates this expression and returns its value.
		/// </summary>
		public virtual Variant Evaluate() => default;

		/// <summary>
		/// Returns a human-readable string representation of this expression for the editor.
		/// </summary>
		public virtual string GetDebugText() => "";

		/// <summary>
		/// Collects all variable names referenced by this expression.
		/// </summary>
		public virtual void CollectArguments( HashSet<string> arguments ) { }
	}

	/// <summary>
	/// An expression that evaluates to a constant literal value.
	/// </summary>
	[Icon( "tag" )]
	[Expose]
	public class LiteralExpression : Expression
	{
		/// <summary>
		/// The constant value this expression evaluates to.
		/// </summary>
		[JsonInclude]
		public Variant LiteralValue { get; set; }

		public override Variant Evaluate() => LiteralValue;
		public override string GetDebugText()
		{
			if ( LiteralValue.Type == typeof( string ) ) return $"\"{LiteralValue}\"";
			if ( LiteralValue.Value is bool b ) return b ? "true" : "false";
			if ( LiteralValue.Value is GameObject go ) return $"[{go?.Name ?? "null"}]";

			return LiteralValue.ToString();
		}
	}

	/// <summary>
	/// An expression that evaluates to the current value of a named variable.
	/// </summary>
	[Icon( "abc" )]
	[Expose]
	public class VariableExpression : Expression
	{
		/// <summary>
		/// The name of the variable to read.
		/// </summary>
		[JsonInclude]
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public string VariableName { get; set; }

		public override Variant Evaluate() => default;
		public override string GetDebugText() => $"{VariableName}";

		public override void CollectArguments( HashSet<string> arguments )
		{
			if ( !string.IsNullOrWhiteSpace( VariableName ) )
			{
				arguments.Add( VariableName );
			}
		}
	}
}
