using System.Runtime.CompilerServices;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Box of baked probes (<c>CSceneLightProbeVolumeObject</c>).
/// Each object selects one volume from all world volumes, including off-screen ones.
/// </summary>
public sealed class LightProbeVolume
{
	/// <summary>
	/// Selection-data revision for cached probe choices.
	/// </summary>
	internal int Version { get; private set; }

	/// <summary>
	/// Cache key covering volume identity, order and selection revisions.
	/// </summary>
	internal static long Key( List<LightProbeVolume> volumes )
	{
		long key = volumes.Count;
		for ( int i = 0; i < volumes.Count; i++ )
		{
			var volume = volumes[i];
			key = key * 1000003 + ((long)RuntimeHelpers.GetHashCode( volume ) << 20 ^ volume.Version);
		}

		return key;
	}

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
	internal RenderAttributes Attributes { get; set { field = value; Version++; } }

	/// <summary>
	/// Select by origin containment, priority, then distance (<c>CLightBinner2::ChooseLightProbeVolume</c>).
	/// Non-containing candidates must touch object bounds. Returns -1 for none.
	/// </summary>
	internal static int Choose( List<LightProbeVolume> volumes, Vector3 origin, Vector3 boundsCenter, Vector3 boundsExtents, uint lightGroup )
	{
		var boundsRadiusSq = boundsExtents.LengthSquared;

		var nearestPriority = int.MinValue;
		var nearestDistSq = float.MaxValue;
		var nearestContains = false;
		var nearest = -1;

		for ( int i = 0; i < volumes.Count; i++ )
		{
			var volume = volumes[i];
			if ( !volume.Enabled || volume.Attributes is null ) continue;
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
