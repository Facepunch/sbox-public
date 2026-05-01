namespace Sandbox;

using System.Text.Json.Serialization;

public partial class Doo
{
	/// <summary>
	/// Set a variable to a value.
	/// </summary>
	[Icon( "📥", "#6b336c", "white" )]
	[Title( "Set Variable" )]
	[Expose]
	public class SetBlock : Block
	{
		/// <summary>
		/// The name of the variable to assign to.
		/// </summary>
		[JsonInclude]
		public string VariableName { get; set; }

		/// <summary>
		/// The expression whose result will be stored in the variable.
		/// </summary>
		[JsonInclude]
		public Expression Value { get; set; }

		public override string GetNodeString()
		{
			return $"{VariableName} = {Value?.GetDebugText()}";
		}

		public override void Reset()
		{
			VariableName = "x";
			Value = new LiteralExpression { LiteralValue = "hello" };
		}

		public override void CollectArguments( HashSet<string> arguments )
		{
			base.CollectArguments( arguments );

			Value?.CollectArguments( arguments );

			if ( !string.IsNullOrWhiteSpace( VariableName ) )
			{
				arguments.Add( VariableName );
			}
		}
	}
}
