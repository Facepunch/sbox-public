using NativeEngine;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Internal;

static class BarkMaterialUpgrader
{
	static readonly (string Array, string Value)[] numericParameters =
	[
		("m_intParams", "m_nValue"),
		("m_floatParams", "m_flValue"),
		("m_vectorParams", "m_value")
	];

	/// <summary>
	/// Replaces bark materials with foliage while preserving their surface settings.
	/// </summary>
	/// <returns>The upgraded material source, or null when this migration does not apply.</returns>
	[Editor.AssetUpgrader( ".vmat", SourceReference = "bark.shader" )]
	static string UpgradeSource( string source )
	{
		if ( !source.Contains( "bark.shader", StringComparison.OrdinalIgnoreCase ) )
			return null;

		var json = EngineGlue.KeyValuesToJson( source );
		if ( json is null )
			return null;

		var options = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
		var material = JsonNode.Parse( json, options ).AsObject();
		if ( NormalizeShaderName( material["shader"]?.ToString() ) != "bark.shader" )
			return null;

		const string versionKey = "material_version:bark.shader";
		if ( (material["ToolAttributes"]?[versionKey]?.GetValue<int>() ?? 0) >= 1 )
			return null;

		var animationValue = material["F_BARK_ANIMATION"]?.ToString();
		float.TryParse( animationValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var animation );
		material["shader"] = "shaders/foliage.shader";
		material["F_FOLIAGE_ANIMATION"] = GetFoliageAnimation( animation );
		material.Remove( "F_BARK_ANIMATION" );

		// Bark had no leaf normal variation or roughness floor.
		material["g_flMinRoughness"] = 0;
		material["g_flNormalVariation"] = 0;
		material["g_flSpecularOcclusion"] ??= 0.5f;

		// Record the bark version even though this upgrade replaces the shader.
		material["ToolAttributes"] ??= new JsonObject( options );
		material["ToolAttributes"][versionKey] = 1;

		var text = new StringBuilder();
		text.AppendLine( "Layer0" );
		text.AppendLine( "{" );
		WriteValues( material, text, "\t" );
		text.AppendLine( "}" );
		return text.ToString();
	}

	// Both paths use trunk-only animation and bark's original surface defaults.
	static int GetFoliageAnimation( float animation ) => (int)animation != 0 ? 2 : 0;

	/// <summary>
	/// Upgrades the decoded material without rewriting the compiled file or its texture references.
	/// </summary>
	[Editor.AssetUpgrader( ".vmat", Target = Editor.AssetUpgradeTarget.Compiled )]
	internal static void UpgradeCompiled( KeyValues3 material )
	{
		if ( NormalizeShaderName( material.GetMemberString( "m_shaderName" ) ) != "bark.shader" )
			return;

		// Work on a copy so a malformed legacy material stays untouched if the upgrade fails.
		var upgraded = KeyValues3.Create();
		try
		{
			upgraded.CopyFrom( material );

			var (animation, specularOcclusion) = RemoveBarkParameters( upgraded );
			var parameters = upgraded.FindOrCreateMember( "m_floatParams" );
			if ( !parameters.IsArray() )
				parameters.SetToEmptyArray();

			AddParameter( parameters, "F_FOLIAGE_ANIMATION", GetFoliageAnimation( animation ) );
			AddParameter( parameters, "g_flMinRoughness", 0 );
			AddParameter( parameters, "g_flNormalVariation", 0 );
			AddParameter( parameters, "g_flSpecularOcclusion", specularOcclusion );
			upgraded.SetMemberString( "m_shaderName", "shaders/foliage.shader" );

			material.CopyFrom( upgraded );
		}
		finally
		{
			upgraded.DeleteThis();
		}
	}

	// Read the first matching value, as native does, while removing every value we replace.
	static (float Animation, float SpecularOcclusion) RemoveBarkParameters( KeyValues3 material )
	{
		float? animation = null;
		float? specularOcclusion = null;

		foreach ( var (arrayName, valueName) in numericParameters )
		{
			var array = material.FindMember( arrayName );
			if ( array.IsNull )
				continue;

			if ( !array.IsArray() )
				throw new InvalidDataException( $"Material parameter list '{arrayName}' is not an array." );

			for ( var i = 0; i < array.GetArrayLength(); )
			{
				var parameter = array.GetArrayElement( i );
				if ( !parameter.IsTable() )
					throw new InvalidDataException( "Material parameter is not a table." );

				var name = parameter.FindMember( "m_name" );
				if ( name.IsNull || name.GetType_Native() != KeyValues3Type_t.KEYVALUES3_TYPE_STRING )
					throw new InvalidDataException( "Material parameter has no valid name." );

				switch ( name.GetValueString().ToLowerInvariant() )
				{
					case "f_bark_animation":
						animation ??= ReadScalar( parameter.FindMember( valueName ) );
						break;
					case "g_flspecularocclusion":
						specularOcclusion ??= ReadScalar( parameter.FindMember( valueName ) );
						break;
					case "f_foliage_animation":
					case "g_flminroughness":
					case "g_flnormalvariation":
						break;
					default:
						i++;
						continue;
				}

				array.ArrayRemoveMultiple( i, 1 );
			}
		}

		return (animation ?? 0, specularOcclusion ?? 0.5f);
	}

	static float ReadScalar( KeyValues3 value )
	{
		if ( value.IsNull )
			throw new InvalidDataException( "Material parameter has no value." );

		// Native material scalar reads use the first component of a vector.
		if ( value.IsArray() )
		{
			if ( value.GetArrayLength() == 0 )
				throw new InvalidDataException( "Material parameter is empty." );

			value = value.GetArrayElement( 0 );
		}

		return value.GetValueFloat();
	}

	static void AddParameter( KeyValues3 parameters, string name, float value )
	{
		var parameter = parameters.ArrayAddToTail();
		parameter.SetMemberString( "m_name", name );
		parameter.SetMemberFloat( "m_flValue", value );
	}

	static string NormalizeShaderName( string shader )
	{
		var name = shader?.Replace( '\\', '/' ).ToLowerInvariant() ?? "";
		if ( name.StartsWith( "shaders/" ) )
			name = name[8..];

		if ( name.EndsWith( ".shader_c" ) )
			name = name[..^2];

		return name;
	}

	static void WriteValues( JsonObject values, StringBuilder text, string indent )
	{
		foreach ( var (name, value) in values )
		{
			if ( value is JsonObject children )
			{
				text.AppendLine( $"{indent}\"{name}\"" );
				text.AppendLine( $"{indent}{{" );
				WriteValues( children, text, indent + "\t" );
				text.AppendLine( $"{indent}}}" );
				continue;
			}

			// KeyValues material strings keep backslashes literally, unlike JSON.
			var content = value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
			text.AppendLine( $"{indent}\"{name}\" \"{content}\"" );
		}
	}
}
