namespace Sandbox;

using System.Text.Json.Serialization;

public partial class Doo
{
	/// <summary>
	/// Get a property value from a component and store it in a variable.
	/// </summary>
	[Icon( "tune", "#2f6b66", "white" )]
	[Title( "Get Property" )]
	[Expose]
	public class GetPropertyBlock : Block
	{
		/// <summary>
		/// The component instance to read the property from.
		/// </summary>
		[JsonInclude]
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public TargetComponent TargetComponent { get; set; }

		/// <summary>
		/// The fully qualified property path to read (e.g. "TypeName.PropertyName").
		/// </summary>
		[JsonInclude]
		public string Property { get; set; }

		/// <summary>
		/// Variable name to assign the property value to.
		/// </summary>
		[JsonInclude]
		[Title( "Variable" )]
		public string ReturnVariable { get; set; }

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

			var returnValue = "";
			if ( !string.IsNullOrWhiteSpace( ReturnVariable ) )
			{
				returnValue = $"{ReturnVariable} = ";
			}

			return $"{returnValue}{targetName}.{prop.Name}";
		}

		public override void Reset()
		{
			TargetComponent = new TargetComponent { Type = TargetComponent.TargetType.Direct };
			ReturnVariable = "x";
		}

		public override void CollectArguments( HashSet<string> arguments )
		{
			base.CollectArguments( arguments );

			TargetComponent?.CollectArguments( arguments );

			if ( !string.IsNullOrWhiteSpace( ReturnVariable ) )
			{
				arguments.Add( ReturnVariable );
			}
		}
	}
}
