using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

public partial class GameObject
{
	/// <summary>
	/// Converts NetworkInterpolation property to NetworkFlags.
	/// </summary>
	[Expose, JsonUpgrader( typeof( GameObject ), 2 )]
	internal static void Upgrader_v2( JsonObject obj )
	{
		if ( !obj.ContainsKey( JsonKeys.NetworkInterpolation ) )
			return;

		var interpolation = obj[JsonKeys.NetworkInterpolation].Deserialize<bool>();
		NetworkFlags flags = NetworkFlags.None;

		if ( obj.TryGetPropertyValue( JsonKeys.NetworkFlags, out var existingFlags ) )
			flags = (NetworkFlags)existingFlags.Deserialize<int>();

		if ( !interpolation )
			flags |= NetworkFlags.NoInterpolation;

		obj[JsonKeys.NetworkFlags] = (int)flags;
	}
}
