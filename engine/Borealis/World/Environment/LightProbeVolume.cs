using System.Runtime.CompilerServices;

namespace Borealis;

/// <summary>
/// Box of baked probes (<c>CSceneLightProbeVolumeObject</c>).
/// Each object selects one volume from all world volumes the view draws, including off-screen ones.
/// </summary>
public sealed class LightProbeVolume
{
	/// <summary>
	/// Selection-data revision for cached probe choices.
	/// </summary>
	internal int Version { get; private set; }

	/// <summary>
	/// Cache key covering the identity, order and selection revisions of the volumes a view with
	/// <paramref name="subWorlds"/> shows.
	/// </summary>
	internal static long Key( List<LightProbeVolume> volumes, ulong subWorlds = ~0UL )
	{
		long key = volumes.Count;
		for ( int i = 0; i < volumes.Count; i++ )
		{
			var volume = volumes[i];
			if ( !volume.ShownIn( subWorlds ) ) continue;
			key = key * 1000003 + ((long)RuntimeHelpers.GetHashCode( volume ) << 20 ^ volume.Version);
		}

		return key;
	}

	/// <summary>
	/// 0 for the world's own volume, or the sub-world of another world drawn in this one's views (<see cref="RenderObject.SubWorld"/>),
	/// whose volumes light only views drawing it, as native's binner takes the volumes of the worlds a view renders.
	/// </summary>
	public int SubWorld { get; set { field = value; Version++; } }

	internal bool ShownIn( ulong subWorlds ) => SubWorld == 0 || (subWorlds & (1UL << (SubWorld - 1))) != 0;

	/// <summary>
	/// Local-to-world transform for probe bounds.
	/// </summary>
	public Transform Transform { get; set { field = value; Version++; } } = Transform.Zero;

	/// <summary>
	/// The box of probes' low corner, in the volume's space.
	/// </summary>
	public Vector3 BoxMins { get; set { field = value; Version++; } }

	/// <summary>
	/// The box of probes' high corner, in the volume's space.
	/// </summary>
	public Vector3 BoxMaxs { get; set { field = value; Version++; } }

	/// <summary>
	/// The handshake the map compiler gave the volume: objects bound to it (<see cref="MeshObject.LightProbeHandshake"/>)
	/// choose it before any by position. Zero for none.
	/// </summary>
	public int Handshake { get; set { field = value; Version++; } }

	/// <summary>
	/// Higher priority wins when both volumes contain the lighting origin.
	/// </summary>
	public int RenderPriority { get; set { field = value; Version++; } }

	/// <summary>
	/// The light groups (string token values) whose objects it lights. Native gives a map's volumes one, the empty name.
	/// </summary>
	public uint[] LightGroups { get; set { field = value; Version++; } } = [];

	/// <summary>
	/// Whether this volume can light objects.
	/// </summary>
	public bool Enabled { get; set { field = value; Version++; } } = true;

	/// <summary>
	/// Required probe constants and baked-lighting flags from <c>RenderTools.SetLightProbeVolumeAttributes</c>.
	/// </summary>
	internal ShaderParameters Attributes { get; set { field = value; Version++; } }

	/// <summary>
	/// Select the volume the object is bound to by <paramref name="handshake"/> if there's one, else by origin containment,
	/// priority, then distance (<c>ChooseLightProbeVolume</c>, <c>CLightBinner2::ChooseLightProbeVolume</c>). Non-containing
	/// candidates must touch object bounds. Returns -1 for none.
	/// </summary>
	internal static int Choose( List<LightProbeVolume> volumes, Vector3 origin, Vector3 boundsCenter, Vector3 boundsExtents, uint lightGroup, int handshake = 0, ulong subWorlds = ~0UL )
	{
		if ( handshake != 0 )
		{
			for ( int i = 0; i < volumes.Count; i++ )
			{
				if ( volumes[i].Handshake == handshake && volumes[i].Enabled && volumes[i].Attributes is not null && volumes[i].ShownIn( subWorlds ) ) return i;
			}
		}

		var boundsRadiusSq = boundsExtents.LengthSquared;

		var nearestPriority = int.MinValue;
		var nearestDistSq = float.MaxValue;
		var nearestContains = false;
		var nearest = -1;

		for ( int i = 0; i < volumes.Count; i++ )
		{
			var volume = volumes[i];
			if ( !volume.Enabled || volume.Attributes is null || !volume.ShownIn( subWorlds ) ) continue;
			if ( Array.IndexOf( volume.LightGroups, lightGroup ) < 0 ) continue;

			var mins = volume.BoxMins;
			var maxs = volume.BoxMaxs;
			var local = ToLocal( volume.Transform, origin );
			var distSq = local.LengthSquared;
			var toBoxSq = SqrDistanceToBox( mins, maxs, local );
			var contains = toBoxSq == 0.0f;
			if ( !contains ) distSq = toBoxSq;

			if ( contains == nearestContains )
			{
				if ( contains && volume.RenderPriority != nearestPriority )
				{
					// Prefer higher priority when both contain the origin.
					if ( volume.RenderPriority < nearestPriority ) continue;
				}
				else if ( distSq > nearestDistSq )
				{
					continue;
				}

				// Non-containing volumes must touch the object.
				if ( !contains && boundsRadiusSq < SqrDistanceToBox( mins, maxs, ToLocal( volume.Transform, boundsCenter ) ) ) continue;
			}
			else if ( !contains )
			{
				// Containment takes precedence.
				continue;
			}

			nearestPriority = volume.RenderPriority;
			nearestDistSq = distSq;
			nearestContains = contains;
			nearest = i;
		}

		return nearest;
	}

	/// <summary>
	/// World-to-local rotation and translation, ignoring scale (<c>VectorITransform</c>).
	/// </summary>
	static Vector3 ToLocal( in Transform transform, Vector3 point )
	{
		var offset = point - transform.Position;
		return transform.Rotation.Inverse * offset;
	}

	/// <summary>
	/// Native's <c>CalcSqrDistanceToAABB</c>.
	/// </summary>
	static float SqrDistanceToBox( Vector3 mins, Vector3 maxs, Vector3 point )
	{
		var below = Vector3.Max( mins - point, Vector3.Zero );
		var above = Vector3.Max( point - maxs, Vector3.Zero );
		return (below + above).LengthSquared;
	}
}
