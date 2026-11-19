using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

public partial class SoundEvent
{
	[Hide, JsonIgnore] public override int ResourceVersion => 2;

	/// <summary>
	/// Sounds from before falloff modes: the stock curve becomes physical with a min distance picked from the distance, so it
	/// sounds about the same; any other curve was authored and stays exactly as it was.
	/// </summary>
	[Expose, JsonUpgrader( typeof( SoundEvent ), 2 )]
	internal static void Upgrader_v2( JsonObject json )
	{
		json["MinDistance"] = 0f;
		json["FalloffMode"] = IsClassicFalloff( json["Falloff"] ) ? nameof( SoundFalloffMode.Physical ) : nameof( SoundFalloffMode.Curve );
	}

	static bool IsClassicFalloff( JsonNode node )
	{
		if ( node is null ) return true;
		if ( node is not JsonArray frames ) return false;

		var classic = Audio.SoundFalloff.ClassicCurve.Frames;
		if ( frames.Count != classic.Length ) return false;

		for ( int i = 0; i < frames.Count; i++ )
		{
			if ( frames[i] is not JsonObject f ) return false;
			if ( !Same( f["x"], classic[i].Time ) || !Same( f["y"], classic[i].Value ) || !Same( f["in"], classic[i].In ) || !Same( f["out"], classic[i].Out ) ) return false;
		}
		return true;

		static bool Same( JsonNode v, float expected ) => MathF.Abs( (v is null ? 0f : v.GetValue<float>()) - expected ) < 1e-4f;
	}
}
