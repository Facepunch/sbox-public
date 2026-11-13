using System.Numerics;

namespace Nav.Build;

internal static class VolumeMarker
{
	/// <summary>
	/// Writes the volume's area into every walkable span whose centre is inside it. Area 0 always wins,
	/// otherwise the higher id does, so the order of volumes does not matter.
	/// </summary>
	public static void Mark( CompactField field, in FieldFrame frame, float cellHeight, in NavVolume volume )
	{
		int w = field.Width;
		int h = field.Height;
		var index = field.Index;
		var floor = field.Floor;
		var areas = field.Area;
		byte area = volume.Area;

		if ( volume.Shape == VolumeShape.Everything )
		{
			for ( int i = 0, n = field.SpanCount; i < n; i++ )
			{
				if ( areas[i] != NavFilter.NoArea && (area == NavFilter.NoArea || area > areas[i]) ) areas[i] = area;
			}
			return;
		}

		if ( !Matrix4x4.Invert( volume.LocalToWorld, out var inverse ) ) return;
		if ( !Local( volume, out var min, out var max, out float radius ) || !WorldBounds( volume, out var worldMin, out var worldMax ) ) return;
		var a = volume.A;
		var b = volume.Shape == VolumeShape.Sphere ? a : volume.B;

		float inverseCellSize = frame.InverseCellSize;
		int x0 = (int)MathF.Max( MathF.Floor( (worldMin.X - frame.MinX) * inverseCellSize ), 0 );
		int y0 = (int)MathF.Max( MathF.Floor( (worldMin.Y - frame.MinY) * inverseCellSize ), 0 );
		int x1 = (int)MathF.Min( MathF.Floor( (worldMax.X - frame.MinX) * inverseCellSize ), w - 1 );
		int y1 = (int)MathF.Min( MathF.Floor( (worldMax.Y - frame.MinY) * inverseCellSize ), h - 1 );

		// World to local, split so a cell pays for x and y once and a span only for z
		var fromX = new Vector3( inverse.M11, inverse.M12, inverse.M13 );
		var fromY = new Vector3( inverse.M21, inverse.M22, inverse.M23 );
		var fromZ = new Vector3( inverse.M31, inverse.M32, inverse.M33 );
		var origin = new Vector3( inverse.M41, inverse.M42, inverse.M43 );

		var axis = b - a;
		float axisLengthSquared = axis.LengthSquared();
		float radiusSquared = radius * radius;
		bool box = volume.Shape == VolumeShape.Box;

		for ( int y = y0; y <= y1; y++ )
		{
			float worldY = frame.MinY + (y + 0.5f) * frame.CellSize;
			for ( int x = x0; x <= x1; x++ )
			{
				int column = x + y * w;
				int end = index[column + 1];
				int i = index[column];
				if ( i == end ) continue;

				float worldX = frame.MinX + (x + 0.5f) * frame.CellSize;
				var cell = fromX * worldX + fromY * worldY + origin;
				for ( ; i < end; i++ )
				{
					int current = areas[i];
					if ( current == NavFilter.NoArea || (area != NavFilter.NoArea && area <= current) ) continue;

					var point = cell + fromZ * (frame.MinZ + (floor[i] + 0.5f) * cellHeight);
					bool inside;
					if ( box )
					{
						inside = point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y && point.Z >= min.Z && point.Z <= max.Z;
					}
					else
					{
						var offset = point - a;
						float along = axisLengthSquared > 0 ? Math.Clamp( Vector3.Dot( offset, axis ) / axisLengthSquared, 0f, 1f ) : 0;
						inside = (offset - axis * along).LengthSquared() <= radiusSquared;
					}

					if ( inside ) areas[i] = area;
				}
			}
		}
	}

	// The local box around the shape, grown by the margin, and the grown radius of a sphere or capsule.
	// False for a radius that is negative or not a number.
	static bool Local( in NavVolume volume, out Vector3 min, out Vector3 max, out float radius )
	{
		var matrix = volume.LocalToWorld;
		var a = volume.A;
		var b = volume.B;
		radius = 0;
		if ( volume.Shape == VolumeShape.Box )
		{
			// The margin is horizontal in the world: each local axis grows by its horizontal share
			var axisX = new Vector3( matrix.M11, matrix.M12, matrix.M13 );
			var axisY = new Vector3( matrix.M21, matrix.M22, matrix.M23 );
			var axisZ = new Vector3( matrix.M31, matrix.M32, matrix.M33 );
			var grow = new Vector3( HorizontalShare( axisX ), HorizontalShare( axisY ), HorizontalShare( axisZ ) ) * volume.Margin;
			min = Vector3.Min( a, b ) - grow;
			max = Vector3.Max( a, b ) + grow;
			return true;
		}

		float scale = MathF.Cbrt( MathF.Abs( matrix.GetDeterminant() ) );
		radius = volume.Radius + (scale > 0 ? volume.Margin / scale : 0);
		if ( volume.Shape == VolumeShape.Sphere ) b = a;
		min = Vector3.Min( a, b ) - new Vector3( radius );
		max = Vector3.Max( a, b ) + new Vector3( radius );
		return radius >= 0;
	}

	/// <summary>
	/// The world box a volume can mark spans in. False when it cannot mark any: a transform that cannot be
	/// inverted or holds a NaN, a negative radius. An Everything volume has no box and returns true with
	/// an infinite one.
	/// </summary>
	public static bool WorldBounds( in NavVolume volume, out Vector3 worldMin, out Vector3 worldMax )
	{
		worldMin = new Vector3( float.NegativeInfinity );
		worldMax = new Vector3( float.PositiveInfinity );
		if ( volume.Shape == VolumeShape.Everything ) return true;

		var matrix = volume.LocalToWorld;
		if ( !Matrix4x4.Invert( matrix, out _ ) || !Local( volume, out var min, out var max, out _ ) ) return false;

		worldMin = new Vector3( float.PositiveInfinity );
		worldMax = new Vector3( float.NegativeInfinity );
		for ( int corner = 0; corner < 8; corner++ )
		{
			var local = new Vector3( (corner & 1) != 0 ? max.X : min.X, (corner & 2) != 0 ? max.Y : min.Y, (corner & 4) != 0 ? max.Z : min.Z );
			var world = Vector3.Transform( local, matrix );
			worldMin = Vector3.Min( worldMin, world );
			worldMax = Vector3.Max( worldMax, world );
		}

		// Also false for a transform with a NaN in it
		return worldMin.X <= worldMax.X && worldMin.Y <= worldMax.Y;
	}

	// How much of a world unit along this local axis is horizontal, in local units.
	static float HorizontalShare( Vector3 axis )
	{
		float lengthSquared = axis.LengthSquared();
		return lengthSquared > 0 ? MathF.Sqrt( axis.X * axis.X + axis.Y * axis.Y ) / lengthSquared : 0;
	}
}
