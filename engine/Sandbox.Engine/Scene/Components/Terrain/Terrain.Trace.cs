namespace Sandbox;

public partial class Terrain
{
	internal bool TraceHeightField( Vector3 worldStart, Vector3 worldEnd, out Vector3 position, out Vector3 normal, out float fraction )
	{
		position = default;
		normal = WorldTransform.Rotation.Up;
		fraction = 1f;

		var delta = worldEnd - worldStart;
		var distance = delta.Length;
		if ( distance <= 0f )
			return false;

		var ray = new Ray( worldStart, delta / distance );

		if ( !RayIntersects( ray, distance, out var local, out var localNormal, out fraction ) )
			return false;

		position = WorldTransform.PointToWorld( local );
		normal = WorldTransform.NormalToWorld( localNormal );
		return true;
	}
}
