using System.Text.Json.Nodes;

namespace Sandbox;

public sealed partial class PlayerController
{
	public override int ComponentVersion => 1;

	/// <summary>
	/// Approximate the old contact-friction brake with the new velocity brake.
	/// </summary>
	[Expose, JsonUpgrader( typeof( PlayerController ), 1 )]
	static void Upgrader_v1( JsonObject json )
	{
		if ( json["BrakePower"] is not { } brakePower ) return;

		json["BrakePower"] = ((float)brakePower * 5.0f).Clamp( 0.1f, 1.0f );
	}
}
