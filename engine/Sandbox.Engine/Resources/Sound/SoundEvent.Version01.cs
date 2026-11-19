using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

public partial class SoundEvent
{
	[Expose, JsonUpgrader( typeof( SoundEvent ), 1 )]
	static void Upgrader_v1( JsonObject json )
	{
		// This upgrader is meant to convert "Volume" values to "Distance" values,
		// reseting the volume to 1.0f so eardrums will no longer be blown out
		if ( json.TryGetPropertyValue( "Volume", out var volumeProp ) && json.TryGetPropertyValue( "DistanceAttenuation", out var attenuationProp ) )
		{
			// If the sound is 2D, they're using Volume as intended
			if ( json.TryGetPropertyValue( "UI", out var uiProp ) && (bool)uiProp )
			{
				return;
			}

			var volumeString = (string)volumeProp;
			var volume = 1f;
			if ( volumeString.Contains( "," ) || volumeString.Contains( " " ) )
			{
				// Check for RangedFloats
				var rangedFloat = RangedFloat.Parse( volumeString );
				if ( rangedFloat.Min == rangedFloat.Max )
				{
					volume = rangedFloat.Min;
				}
				else
				{
					// If they're using a ranged float, they're using Volume as intended
					return;
				}
			}
			else
			{
				volume = volumeString.ToFloat();
			}
			var distanceAttenuation = (bool)attenuationProp;

			// Only perform the upgrade if the sound uses distance attenuation,
			// otherwise they're presumably using Volume as intended
			if ( distanceAttenuation )
			{
				float maxDistance = volume * 10_000f;
				float oldAttenuation = 1.0f / MathF.Max( maxDistance, 1.0f );
				float distance = MathX.Remap( 1.0f - oldAttenuation, 0.0f, 1.0f, 0.0f, maxDistance );

				json["Distance"] = distance.SnapToGrid( 5 );
				json["Volume"] = 1.0f;
			}
		}
	}
}
