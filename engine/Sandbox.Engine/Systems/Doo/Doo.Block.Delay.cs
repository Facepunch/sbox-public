namespace Sandbox;

using System.Text.Json.Serialization;

public partial class Doo
{
	/// <summary>
	/// Wait for a number of seconds
	/// </summary>
	[Icon( "✋", "#5C4837", "#fff" )]
	[Title( "Delay" )]
	[Expose]
	public class DelayBlock : Block
	{
		/// <summary>
		/// The number of seconds to wait before continuing.
		/// </summary>
		[JsonInclude]
		public Expression Seconds { get; set; }

		public override string GetNodeString()
		{
			return Seconds == null ? "Delay (none)" : $"Delay {Seconds.GetDebugText()}s";
		}

		public override void CollectArguments( HashSet<string> arguments )
		{
			base.CollectArguments( arguments );

			Seconds?.CollectArguments( arguments );
		}

		public override void Reset()
		{
			Seconds = new LiteralExpression() { LiteralValue = 1.0f };
		}
	}
}
