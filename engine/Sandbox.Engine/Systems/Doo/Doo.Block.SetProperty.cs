namespace Sandbox;

using System.Text.Json.Serialization;

public partial class Doo
{
	/// <summary>
	/// Set a property value on a component.
	/// </summary>
	[Icon( "tune", "#8b4b2b", "white" )]
	[Title( "Set Property" )]
	[Expose]
	public class SetPropertyBlock : Block
	{
		/// <summary>
		/// The component instance to write the property on.
		/// </summary>
		[JsonInclude]
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public TargetComponent TargetComponent { get; set; }

		/// <summary>
		/// The fully qualified property path to write (e.g. "TypeName.PropertyName").
		/// </summary>
		[JsonInclude]
		public string Property { get; set; }

		/// <summary>
		/// The expression whose value will be written to the property.
		/// </summary>
		[JsonInclude]
		public Expression Value { get; set; }

		public override string GetNodeString()
		{
			if ( Property == null ) return "(empty)";

			var targetName = TargetComponent?.GetNodeString();
			if ( targetName == null ) return "(empty)";

			var prop = Doo.Helpers.FindProperty( Property );
			if ( prop == null )
			{
				return $"Couldn't Find {Property}";
			}

			return $"{targetName}.{prop.Name} = {Value?.GetDebugText()}";
		}

		public override void Reset()
		{
			TargetComponent = new TargetComponent { Type = TargetComponent.TargetType.Direct };
			Value = new LiteralExpression { LiteralValue = new Variant( null, typeof( object ) ) };
		}

		public override void CollectArguments( HashSet<string> arguments )
		{
			base.CollectArguments( arguments );

			TargetComponent?.CollectArguments( arguments );
			Value?.CollectArguments( arguments );
		}
	}
}
