using System.Text.Json;

namespace Sandbox;

public partial class Doo
{
	/// <summary>
	/// Abstracts a link to a component - which can be
	/// * An actual component
	/// * A GameObject and a component type
	/// * A Variable (GameObject or Component) and a component type
	/// </summary>
	[Expose]
	public class TargetComponent : IJsonConvert
	{
		/// <summary>
		/// Resolves the component
		/// </summary>
		internal Component Evaluate( RunContext ctx )
		{
			if ( Type == TargetType.Direct )
			{
				return ComponentValue;
			}

			var type = GetComponentType();
			if ( type == null ) return default;

			if ( Type == TargetType.GameObject )
			{
				return GameObjectValue?.Components.Get( type, FindMode );
			}

			if ( Type == TargetType.Variable )
			{
				var target = ctx.Engine.GetVariable( ctx, VariableName );

				if ( target is GameObject go && go.IsValid() )
					return go.Components.Get( type, FindMode );

				if ( target is Component c && c.IsValid() )
					return c;
			}

			return default;
		}

		public Type GetComponentType()
		{
			if ( Type == TargetType.Direct ) return ComponentValue?.GetType();
			return Game.TypeLibrary.GetType( ComponentType )?.TargetType;
		}

		public enum TargetType
		{
			Direct,
			GameObject,
			Variable
		}

		public TargetType Type { get; set; }

		/// <summary>
		/// The Component we want to target directly.
		/// </summary>
		[Title( "Component" )]
		public Component ComponentValue { get; set; }

		/// <summary>
		/// The GameObject that contains the target component.
		/// </summary>
		[Title( "GameObject" )]
		public GameObject GameObjectValue { get; set; }

		/// <summary>
		/// The type of Component we want to access. This allows us to select members that exist on this type.
		/// </summary>
		public string ComponentType { get; set; }

		/// <summary>
		/// The name of the variable we're going to use. This can be a GameObject or a Component.
		/// </summary>
		[Title( "Variable" )]
		public string VariableName { get; set; }

		public FindMode FindMode { get; set; } = FindMode.EnabledInSelf;

		/// <summary>
		/// Collects all variable names referenced by this expression.
		/// </summary>
		public void CollectArguments( HashSet<string> arguments )
		{
			if ( !string.IsNullOrWhiteSpace( VariableName ) )
			{
				arguments.Add( VariableName );
			}
		}

		public static object JsonRead( ref Utf8JsonReader reader, Type typeToConvert )
		{
			if ( reader.TokenType != JsonTokenType.StartObject )
			{
				reader.Read();
				return null;
			}

			var t = new TargetComponent();

			while ( reader.Read() )
			{
				if ( reader.TokenType == JsonTokenType.EndObject )
					break;

				if ( reader.TokenType != JsonTokenType.PropertyName )
					throw new JsonException( "Expected PropertyName" );

				var propName = reader.GetString();
				reader.Read();

				switch ( propName )
				{
					case "t":
						t.Type = Json.Deserialize<TargetType>( ref reader );
						break;
					case "v":
						t.VariableName = reader.GetString();
						break;
					case "ct":
						t.ComponentType = reader.GetString();
						break;
					case "cv":
						t.ComponentValue = Json.Deserialize<Component>( ref reader );
						break;
					case "g":
						t.GameObjectValue = Json.Deserialize<GameObject>( ref reader );
						break;
					case "fm":
						t.FindMode = Json.Deserialize<FindMode>( ref reader );
						break;
					default:
						reader.Skip();
						break;
				}
			}

			return t;
		}

		public static void JsonWrite( object value, Utf8JsonWriter writer )
		{
			if ( value is not TargetComponent tc )
			{
				writer.WriteNullValue();
				return;
			}

			writer.WriteStartObject();
			{
				writer.WritePropertyName( "t" );
				Json.Serialize( writer, tc.Type, typeof( TargetType ) );

				if ( tc.Type == TargetType.Direct )
				{
					writer.WritePropertyName( "cv" );
					Json.Serialize( writer, tc.ComponentValue, typeof( Component ) );

				}
				else if ( tc.Type == TargetType.Variable )
				{
					writer.WriteString( "v", tc.VariableName );
					writer.WriteString( "ct", tc.ComponentType );
					writer.WritePropertyName( "fm" );
					Json.Serialize( writer, tc.FindMode, typeof( FindMode ) );
				}
				else if ( tc.Type == TargetType.GameObject )
				{
					writer.WritePropertyName( "g" );
					Json.Serialize( writer, tc.GameObjectValue, typeof( GameObject ) );

					writer.WriteString( "ct", tc.ComponentType );
					writer.WritePropertyName( "fm" );
					Json.Serialize( writer, tc.FindMode, typeof( FindMode ) );
				}
			}
			writer.WriteEndObject();
		}

		internal string GetNodeString()
		{
			if ( Type == TargetType.Direct )
			{
				return ComponentValue?.ToString();
			}

			var type = GetComponentType();
			if ( type == null ) return "Null";

			if ( Type == TargetType.GameObject )
			{
				if ( GameObjectValue == null )
					return $"Null";

				return $"{GameObjectValue?.Name}.{type.Name}";
			}

			if ( Type == TargetType.Variable )
			{
				if ( string.IsNullOrWhiteSpace( VariableName ) )
					return $"Null";

				return $"{VariableName}.{type.Name}";
			}

			return "Null";
		}
	}

}
