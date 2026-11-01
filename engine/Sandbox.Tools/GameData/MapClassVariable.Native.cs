using Native;
using System;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Editor;

public partial class MapClassVariable
{
	internal void ToNative( CGameDataVariable gd )
	{
		gd.SetName( Name );
		gd.SetLongName( LongName );
		gd.SetDescription( Description );
		gd.SetGroupName( GroupName );

		// Editor override
		var editor = VariableEditor();
		if ( !string.IsNullOrEmpty( editor ) )
		{
			Metadata.Add( "editor", editor );
		}

		// Nullable for ModelDoc
		if ( PropertyType != null && Nullable.GetUnderlyingType( PropertyType ) != null )
		{
			Metadata.Add( "nullable", "true" );
		}

		// Metadata
		// TODO: This could probably be done better
		if ( Metadata.Count > 0 )
		{
			StringBuilder output = new();
			output.AppendLine( "{" );
			foreach ( var kvp in Metadata )
			{
				bool isNotString = bool.TryParse( kvp.Value, out bool _ ) || float.TryParse( kvp.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float _ );
				output.AppendLine( $"\t{kvp.Key} = {(isNotString ? kvp.Value : kvp.Value.QuoteSafe())}" );
			}
			output.AppendLine( "}" );

			gd.SetMetadataFromString( output.ToString() );
		}

		// Default value
		var defaultValue = EditorDefaultValue();
		if ( (PropertyType == typeof( int ) || PropertyType == typeof( int? )) && defaultValue is int defVal )
		{
			// idk why valve do this, maybe i can change it
			// This is necessary for types: int, integer, flags, lod_level, node_dest, node_id
			gd.SetDefaultValueNumber( defVal );
		}
		else
		{
			gd.SetDefaultValue( defaultValue.ToString() );
		}

		// Type
		if ( string.IsNullOrEmpty( PropertyTypeOverride ) )
		{
			gd.SetTypeFromString( SerializedType( PropertyType ) );

			if ( PropertyType.IsEnum )
			{
				var itemSet = gd.GetItemSet();
				Assert.True( itemSet.IsValid );

				var isFlags = PropertyType.HasAttribute( typeof( FlagsAttribute ) );

				int defaultFlags = defaultValue is int @int ? @int : 0;

				var enumFields = PropertyType.GetFields().Where( e => e.IsLiteral && !e.HasAttribute( typeof( HideAttribute ) ) );
				foreach ( var field in enumFields )
				{
					var name = field.Name.ToTitleCase();
					var value = field.GetRawConstantValue();

					if ( isFlags )
					{
						itemSet.AddFlagItem( (int)value, name, (defaultFlags & (int)value) != 0 );
					}
					else
					{
						itemSet.AddChoiceItem( value.ToString(), name );
					}
				}
			}
		}
		else
		{
			gd.SetTypeFromString( PropertyTypeOverride );
		}
	}

	/// <summary>
	/// Translates our C# type to a type mapdoc understands.
	/// I doubt we actually need to use a string here for it, but it does make things easier.
	/// e.g an array
	/// </summary>
	internal string SerializedType( Type type )
	{
		if ( type == typeof( int ) ) return "integer";
		if ( type == typeof( float ) ) return "float";
		if ( type == typeof( double ) ) return "float";
		if ( type == typeof( bool ) ) return "boolean";
		if ( type == typeof( Vector3 ) ) return "vector";
		if ( type == typeof( Vector2 ) ) return "vector2";
		if ( type == typeof( string ) ) return "string";
		if ( type == typeof( Rotation ) ) return "angle";
		if ( type == typeof( Angles ) ) return "angle";
		if ( type == typeof( Color ) ) return "color255"; // TODO: this seems fucked, we should have a proper float Color type in Hammer
		if ( type == typeof( Color32 ) ) return "color255";
		if ( type == typeof( RangedFloat ) ) return "vector";
		if ( type == typeof( Material ) ) return "material";
		if ( type == typeof( Model ) ) return "resource:vmdl";
		if ( type.Name == "TagList" ) return "tags";
		if ( type.Name == "SoundEvent" ) return "sound";

		//
		// The type itself knows it's Resource type
		// e.g Model = resource:vmdl, Texture = resource:vtex, etc..
		//
		var fgdTypeAttr = type.GetCustomAttribute<FGDTypeAttribute>( false );
		if ( fgdTypeAttr != null )
		{
			return fgdTypeAttr.Type;
		}

		if ( type.IsEnum )
		{
			if ( type.HasAttribute( typeof( FlagsAttribute ) ) ) return "flags";
			return "choices";
		}

		// Nullable
		var nullableType = Nullable.GetUnderlyingType( type );
		if ( nullableType != null )
		{
			return SerializedType( nullableType );
		}

		// Game Resources
		var gameRes = type.GetCustomAttribute<AssetTypeAttribute>( false );
		if ( gameRes != null )
		{
			return $"resource:{gameRes.Extension}";
		}

		Log.Warning( $"{this}: Missing type for: {(type.IsGenericType ? type.GetGenericTypeDefinition() : type.Name)}" );
		return "string";
	}

	// Should return either an int or a string!
	internal object EditorDefaultValue()
	{
		//
		// For every primitive or struct type in SerializedType
		// we also wanna turn the values into a string Hammer can parse
		//
		if ( DefaultValue is int ) return DefaultValue;
		if ( DefaultValue is float ) return DefaultValue.ToString();
		if ( DefaultValue is double ) return DefaultValue.ToString();
		if ( DefaultValue is bool ) return ((bool)DefaultValue == true ? "1" : "0");
		if ( DefaultValue is string ) return DefaultValue.ToString();
		if ( DefaultValue is Color32 color32 ) return $"{color32.r} {color32.g} {color32.b} {color32.a}";
		if ( DefaultValue is Vector2 vector2 ) return $"{vector2.x} {vector2.y}";
		if ( DefaultValue is Vector3 vector3 ) return $"{vector3.x} {vector3.y} {vector3.z}";
		if ( DefaultValue is Angles angles ) return $"{angles.pitch} {angles.yaw} {angles.roll}";
		if ( DefaultValue != null && DefaultValue.GetType().IsEnum ) return Convert.ToInt32( DefaultValue );

		// todo: this is fucked, Hammer should have a proper float based color type
		if ( DefaultValue is Color color )
		{
			var color322 = color.ToColor32();
			return $"{color322.r} {color322.g} {color322.b} {color322.a}";
		}

		if ( DefaultValue is Rotation rotation )
		{
			var rotAngles = rotation.Angles();
			return $"{rotAngles.pitch} {rotAngles.yaw} {rotAngles.roll}";
		}

		//
		// If the default is not set, deduce it from the property type
		// This is useful for cases like chagning a vector to "0 0 0" and it still counting as changed
		//
		if ( PropertyType == typeof( int ) ) return 0;
		if ( PropertyType == typeof( float ) ) return "0";
		if ( PropertyType == typeof( double ) ) return "0";
		if ( PropertyType == typeof( bool ) ) return "0";
		if ( PropertyType == typeof( Vector3 ) ) return "0 0 0";
		if ( PropertyType == typeof( Vector2 ) ) return "0 0";
		if ( PropertyType == typeof( Rotation ) ) return "0 0 0";
		if ( PropertyType == typeof( Angles ) ) return "0 0 0";
		if ( PropertyType == typeof( Color ) ) return "255 255 255 255"; // TODO: For assets/modeldoc this should be "1 1 1 1"
		if ( PropertyType == typeof( Color32 ) ) return "255 255 255 255";
		//if ( PropertyType == typeof( RangedFloat ) ) return "0 0 0";

		return "";
	}

	internal string VariableEditor()
	{
		if ( PropertyType == null ) return null;
		if ( PropertyType == typeof( RangedFloat ) ) return "Ranged()";

		// The type itself knows the editor
		var fgdTypeAttr = PropertyType.GetCustomAttribute<FGDTypeAttribute>( false );
		if ( fgdTypeAttr != null && !string.IsNullOrEmpty( fgdTypeAttr.Editor ) )
		{
			return fgdTypeAttr.Editor;
		}

		return null;
	}
}
