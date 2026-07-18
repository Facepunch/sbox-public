using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

public partial class Sprite
{
	[Hide, JsonIgnore] public override int ResourceVersion => 2;

	/// <summary>
	/// v1
	/// - Convert raw image file paths in Frame.Texture to proper ImageFileGenerator embedded resources.
	/// </summary>
	[Expose, JsonUpgrader( typeof( Sprite ), 1 )]
	static void Upgrader_v1( JsonObject json )
	{
		if ( !json.TryGetPropertyValue( "Animations", out var animationsNode ) )
			return;

		if ( animationsNode is not JsonArray animations )
			return;

		foreach ( var animNode in animations )
		{
			if ( animNode is not JsonObject animation )
				continue;

			if ( !animation.TryGetPropertyValue( "Frames", out var framesNode ) )
				continue;

			if ( framesNode is not JsonArray frames )
				continue;

			foreach ( var frameNode in frames )
			{
				if ( frameNode is not JsonObject frame )
					continue;

				if ( !frame.TryGetPropertyValue( "Texture", out var textureNode ) )
					continue;

				// Only upgrade if the texture is a bare string path (the broken format)
				if ( textureNode is not JsonValue textureValue )
					continue;

				if ( !textureValue.TryGetValue<string>( out var filePath ) )
					continue;

				if ( string.IsNullOrWhiteSpace( filePath ) )
					continue;

				// Replace with the proper embedded texture compiler format
				frame["Texture"] = new JsonObject
				{
					["$compiler"] = "texture",
					["$source"] = "imagefile",
					["data"] = new JsonObject
					{
						["FilePath"] = filePath,
						["MaxSize"] = 4096
					},
					["compiled"] = null
				};
			}
		}
	}

	/// <summary>
	/// v2
	/// - Convert legacy null loop bounds to the proper -1 value
	/// </summary>
	[Expose, JsonUpgrader( typeof( Sprite ), 2 )]
	static void Upgrader_v2( JsonObject json )
	{
		if ( !json.TryGetPropertyValue( "Animations", out var animationsNode ) )
			return;

		if ( animationsNode is not JsonArray animations )
			return;

		foreach ( var animNode in animations )
		{
			if ( animNode is not JsonObject animation )
				continue;

			if ( animation.TryGetPropertyValue( "LoopStart", out var loopStart ) && loopStart is null )
			{
				animation["LoopStart"] = -1;
			}

			if ( animation.TryGetPropertyValue( "LoopEnd", out var loopEnd ) && loopEnd is null )
			{
				animation["LoopEnd"] = -1;
			}
		}
	}
}
