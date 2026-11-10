using System;
using System.IO;
using System.Runtime.InteropServices;
using static Sandbox.Mounting.Source1.BspGeometry;
using static Sandbox.Mounting.Source1.BspWorldLightType;

namespace Sandbox.Mounting.Source1;

internal partial class BspFile
{
	internal PropWorldLight[] worldLights = [];
	internal byte[] visibility = [];
	internal float[] lightingStyles = [];
	internal ushort[] lightingLeafBrushes = [];
	internal readonly List<LightingDisplacement> lightingDisplacements = [];
	internal sealed record LightingDisplacement( BBox Bounds, Vector3[] Positions, Vector2[] Coordinates, int[] Indices, int Face );
	internal void ReadPropLighting( Stream stream, long fileStart, (int offset, int length, int version, int uncompressedSize)[] lumps )
	{
		if ( StaticProps.Length == 0 && ambientLighting is null )
			return;
		ReadSpatialData( stream, fileStart, lumps );
		var lightLump = Hdr && lumps[54].length > 0 ? lumps[54] : lumps[15];
		if ( lightLump.version == 1 )
		{
			var data = ReadRaw( stream, fileStart, lightLump, 1 );
			if ( data.Length % 100 != 0 )
				throw new InvalidDataException( "BSP world light lump contains a partial record." );
			worldLights = new PropWorldLight[data.Length / 100];
			Span<byte> record = stackalloc byte[88];
			for ( int index = 0; index < worldLights.Length; index++ )
			{
				data.AsSpan( index * 100, 36 ).CopyTo( record );
				data.AsSpan( index * 100 + 48, 52 ).CopyTo( record[36..] );
				worldLights[index] = MemoryMarshal.Read<PropWorldLight>( record );
			}
		}
		else
			worldLights = ReadLump<PropWorldLight>( stream, fileStart, lightLump );
		visibility = ReadRaw( stream, fileStart, lumps[4] );
		lightingLeafBrushes = ReadLump<ushort>( stream, fileStart, lumps[17] );
		foreach ( int brush in lightingLeafBrushes )
			ValidateRange( brush, 1, collisionBrushes.Length, "Lighting leaf brush" );
		foreach ( var leaf in leaves )
			ValidateRange( leaf.FirstBrush, leaf.BrushCount, lightingLeafBrushes.Length, "Lighting leaf brushes" );
		for ( int index = 0; index < worldLights.Length; index++ )
		{
			ref var light = ref worldLights[index];
			if ( light.Type is < BspWorldLightType.Surface or > SkyAmbient || light.Style is < 0 or >= LightmapCodec.StyleCount || !Finite( light.Origin ) || !Finite( light.Normal ) || !Finite( light.Intensity ) || !float.IsFinite( light.Radius ) || !float.IsFinite( light.Constant ) || !float.IsFinite( light.Linear ) || !float.IsFinite( light.Quadratic ) || !float.IsFinite( light.InnerCone ) || !float.IsFinite( light.OuterCone ) || !float.IsFinite( light.Exponent ) )
				throw new InvalidDataException( $"Invalid BSP world light {index}." );
			if ( light.Type is Point or Spotlight && light.Constant == 0 && light.Linear == 0 && light.Quadratic == 0 )
				light.Quadratic = 1;
			if ( light.Type == Spotlight && light.Exponent == 0 )
				light.Exponent = 1;
			if ( light.Radius == 0 )
			{
				float intensity = light.Intensity.Length / (Hdr ? 0.015f : 0.03f);
				float discriminant = light.Linear * light.Linear - 4 * light.Quadratic * (light.Constant - intensity);
				light.Radius = light.Quadratic == 0 ? light.Linear == 0 ? 2000 : (intensity - light.Constant) / light.Linear : discriminant < 0 ? 2000 : Math.Max( 0, (-light.Linear + MathF.Sqrt( discriminant )) / (2 * light.Quadratic) );
			}
		}

		var patterns = BuildLightstylePatterns( this );
		lightingStyles = patterns.Select( pattern => LightmapCodec.StyleValue( pattern, 0 ) ).ToArray();
		BuildLightingDisplacements();
	}

	public PropLightingState GetPropLighting( Vector3 origin, BspLightingContext lighting )
	{
		if ( lighting.Props.TryGetValue( origin, out var cached ) )
			return cached;
		var state = new PropLightingState
		{
			StyleZero = lightingStyles[0]
		};
		var weights = new float[6];
		var sky = worldLights.FirstOrDefault( light => light.Type == SkyAmbient ).Intensity;
		foreach ( var direction in PropLightingState.AmbientDirections )
		{
			var radiance = TraceRadiance( origin, direction * (32768 * 1.74f), sky );
			for ( int side = 0; side < 6; side++ )
			{
				float weight = Math.Max( 0, Vector3.Dot( direction, PropLightingState.Axes[side] ) );
				weights[side] += weight;
				state.Ambient[side] += radiance * weight;
			}
		}

		for ( int side = 0; side < 6; side++ )
			state.Ambient[side] /= weights[side];
		var leaf = leaves[FindLeaf( origin )];
		var pvs = ReadPvs( leaf.Cluster );
		var brightness = new List<float>();
		bool hasSky = false;
		foreach ( var light in worldLights )
		{
			if ( !UsesImportedLightstyle( light ) || light.Type == SkyAmbient )
				continue;
			if ( light.Type == SkyLight )
			{
				if ( hasSky || (leaf.Flags & 5) == 0 )
					continue;
			}
			else if ( light.Cluster < 0 || pvs != null && (light.Cluster / 8 >= pvs.Length || (pvs[light.Cluster / 8] & (1 << (light.Cluster & 7))) == 0) )
				continue;
			var direction = light.Type == SkyLight ? -light.Normal : (light.Origin - origin).Normal;
			if ( !TouchesLightingBucket( light, origin ) )
				continue;
			float ratio = light.Falloff( origin ) * lightingStyles[light.Style];
			if ( light.Type != BspWorldLightType.Surface && ratio * light.Intensity.Length < 0.0002f )
				continue;
			if ( light.Type == SkyLight )
			{
				var hit = TraceOpaque( origin, direction * (32768 * 1.74f), lighting );
				if ( hit.Fraction == 1 || !hit.Sky )
					continue;
				hasSky = true;
			}
			else
			{
				float distance = (origin - light.Origin).Length;
				if ( distance > distance * TraceOpaque( light.Origin, origin - light.Origin, lighting ).Fraction + 8 )
					continue;
			}

			float illum = ratio * Vector3.Dot( light.Intensity, new Vector3( 0.299f, 0.587f, 0.114f ) );
			var folded = light;
			if ( light.Type == BspWorldLightType.Surface || illum >= 0.0002f )
			{
				if ( state.Lights.Count < 4 )
				{
					state.Lights.Add( light );
					brightness.Add( illum );
					continue;
				}

				int darkest = -1;
				for ( int index = 0; index < brightness.Count; index++ )
					if ( brightness[index] < illum && (darkest < 0 || brightness[index] < brightness[darkest]) )
						darkest = index;
				if ( darkest >= 0 )
				{
					folded = state.Lights[darkest];
					ratio = brightness[darkest] / Vector3.Dot( folded.Intensity, new Vector3( 0.299f, 0.587f, 0.114f ) );
					state.Lights[darkest] = light;
					brightness[darkest] = illum;
					direction = folded.Type == SkyLight ? Vector3.Zero : (folded.Origin - origin).Normal;
				}
			}

			ratio *= folded.Type == SkyLight ? 1 : folded.Angular( direction );
			for ( int side = 0; side < 6; side++ )
				state.Ambient[side] += folded.Intensity * (ratio * Math.Max( 0, Vector3.Dot( direction, PropLightingState.Axes[side] ) ));
		}

		lighting.Props.Add( origin, state );
		return state;
	}

	internal static bool Finite( Vector3 value ) => float.IsFinite( value.x ) && float.IsFinite( value.y ) && float.IsFinite( value.z );
	internal static bool TouchesLightingBucket( PropWorldLight light, Vector3 origin )
	{
		if ( light.Type is SkyLight or QuakeLight )
			return true;
		var min = new Vector3( Minimum( origin.x, 8 ), Minimum( origin.y, 8 ), Minimum( origin.z, 16 ) );
		var max = min + new Vector3( 8, 8, 16 );
		if ( light.Type is Point or Spotlight && (light.Origin - Vector3.Clamp( light.Origin, min, max )).LengthSquared > light.Radius * light.Radius )
			return false;
		if ( light.Type == Point )
			return true;
		var delta = origin - light.Origin;
		float radius = (max - origin).Length;
		if ( delta.Length > radius + light.Radius )
			return false;
		float cosine = light.Type == BspWorldLightType.Surface ? 0 : light.OuterCone;
		float sine = MathF.Sqrt( Math.Max( 0, 1 - cosine * cosine ) );
		var extended = delta + light.Normal * (radius / Math.Max( sine, float.Epsilon ));
		float projection = Vector3.Dot( extended, light.Normal );
		if ( projection < 0 || projection * projection < extended.LengthSquared * cosine * cosine )
			return false;
		projection = Vector3.Dot( delta, light.Normal );
		return projection >= 0 || delta.LengthSquared <= radius * radius;
		static float Minimum( float value, int size ) => value >= 0 ? (int)value / size * size : -((int)Math.Abs( value ) / size + 1) * size;
	}

	internal byte[] ReadPvs( int cluster )
	{
		if ( visibility.Length == 0 || cluster < 0 )
			return null;
		ValidateRange( 0, 4, visibility.Length, "Visibility header" );
		int clusters = BitConverter.ToInt32( visibility );
		ValidateRange( 4, (long)clusters * 8, visibility.Length, "Visibility directory" );
		ValidateRange( cluster, 1, clusters, "Lighting origin cluster" );
		int offset = BitConverter.ToInt32( visibility, 4 + cluster * 8 );
		var result = new byte[(clusters + 7) / 8];
		for ( int index = 0; index < result.Length; )
		{
			ValidateRange( offset, 1, visibility.Length, "Visibility run" );
			byte value = visibility[offset++];
			if ( value != 0 )
				result[index++] = value;
			else
			{
				ValidateRange( offset, 1, visibility.Length, "Visibility zero run" );
				int count = visibility[offset++];
				if ( count == 0 || count > result.Length - index )
					throw new InvalidDataException( "Invalid visibility zero run." );
				index += count;
			}
		}

		return result;
	}

	internal Vector3 TraceRadiance( Vector3 start, Vector3 delta, Vector3 sky )
	{
		int found = -1;
		bool foundSky = false;
		float closest = 1;
		Vector2 coordinates = default;
		Visit( Models[0].HeadNode, 0, 1 );
		foreach ( var disp in lightingDisplacements )
		{
			var surface = Faces[disp.Face];
			if ( surface.LightOffset < 0 || surface.Style0 == 255 || (TexInfos[surface.TexInfo].Flags & (BspSurfaceFlags.NoDraw | BspSurfaceFlags.Warp | BspSurfaceFlags.SkyMask)) != 0 )
				continue;
			if ( !Intersects( disp.Bounds, start, delta, closest ) )
				continue;
			for ( int index = 0; index < disp.Indices.Length; index += 3 )
			{
				int a = disp.Indices[index], b = disp.Indices[index + 1], c = disp.Indices[index + 2];
				if ( !Triangle( start, delta, disp.Positions[a], disp.Positions[b], disp.Positions[c], out float fraction, out var uv ) || fraction >= closest )
					continue;
				closest = fraction;
				found = disp.Face;
				coordinates = disp.Coordinates[a] * (1 - uv.x - uv.y) + disp.Coordinates[b] * uv.x + disp.Coordinates[c] * uv.y;
			}
		}

		if ( found < 0 )
			return foundSky ? sky : Vector3.Zero;
		var face = Faces[found];
		int offset = face.DispInfo >= 0 ? face.LightOffset + 4 * (Math.Clamp( (int)coordinates.x, 0, face.LightmapSizeS ) + Math.Clamp( (int)coordinates.y, 0, face.LightmapSizeT ) * (face.LightmapSizeS + 1)) : face.LightOffset - 4;
		ValidateRange( offset, 4, LightingData.Length, "Prop ambient lightmap sample" );
		var decoded = LightmapCodec.Decode( LightingData.AsSpan( offset, 4 ) );
		return new Vector3( decoded.x, decoded.y, decoded.z ) * TexDatas[TexInfos[face.TexInfo].TexData].Reflectivity * (face.Style0 < lightingStyles.Length ? lightingStyles[face.Style0] : 1);
		void Surface( int index, float fraction, bool detail )
		{
			var face = Faces[index];
			if ( face.TexInfo < 0 )
				return;
			var info = TexInfos[face.TexInfo];
			if ( (info.Flags & BspSurfaceFlags.SkyMask) != 0 )
			{
				foundSky = true;
				return;
			}

			if ( fraction >= closest || face.DispInfo >= 0 || face.LightOffset < 0 || face.Style0 == 255 || (info.Flags & (BspSurfaceFlags.NoDraw | BspSurfaceFlags.Warp)) != 0 )
				return;
			var plane = Planes[face.PlaneNum];
			if ( detail && Vector3.Dot( delta, plane.Normal ) * (face.Side == 0 ? 1 : -1) >= 0 )
				return;
			var point = start + delta * fraction;
			float s = Vector3.Dot( point, new Vector3( info.LightmapVecsS.x, info.LightmapVecsS.y, info.LightmapVecsS.z ) ) + info.LightmapVecsS.w - face.LightmapMinsS;
			float t = Vector3.Dot( point, new Vector3( info.LightmapVecsT.x, info.LightmapVecsT.y, info.LightmapVecsT.z ) ) + info.LightmapVecsT.w - face.LightmapMinsT;
			if ( s < 0 || t < 0 || s > face.LightmapSizeS || t > face.LightmapSizeT )
				return;
			found = index;
			closest = fraction;
		}

		void Visit( int index, float from, float to )
		{
			if ( from >= closest )
				return;
			if ( index < 0 )
			{
				var leaf = leaves[-1 - index];
				for ( int entry = leaf.FirstFace; entry < leaf.FirstFace + leaf.FaceCount; entry++ )
				{
					int faceIndex = leafFaces[entry];
					var face = Faces[faceIndex];
					if ( face.OnNode != 0 || face.DispInfo >= 0 )
						continue;
					var plane = Planes[face.PlaneNum];
					float denominator = Vector3.Dot( delta, plane.Normal );
					if ( denominator == 0 )
						continue;
					float fraction = (plane.Dist - Vector3.Dot( start, plane.Normal )) / denominator;
					if ( fraction >= from && fraction <= to )
						Surface( faceIndex, fraction, true );
				}

				return;
			}

			var node = nodes[index];
			var split = Planes[node.Plane];
			float distance = Vector3.Dot( start, split.Normal ) - split.Dist;
			float rate = Vector3.Dot( delta, split.Normal );
			float first = distance + rate * from, last = distance + rate * to;
			if ( first >= 0 && last >= 0 )
			{
				Visit( node.Front, from, to );
				return;
			}

			if ( first < 0 && last < 0 )
			{
				Visit( node.Back, from, to );
				return;
			}

			float mid = Math.Clamp( -distance / rate, from, to );
			Visit( first < 0 ? node.Back : node.Front, from, mid );
			for ( int face = node.FirstFace; face < node.FirstFace + node.FaceCount; face++ )
				Surface( face, mid, false );
			Visit( first < 0 ? node.Front : node.Back, mid, to );
		}
	}

	internal (float Fraction, bool Sky) TraceOpaque( Vector3 start, Vector3 delta, BspLightingContext lighting )
	{
		float closest = 1;
		bool sky = false;
		int generation = lighting.BeginTrace( collisionBrushes.Length );
		Visit( Models[0].HeadNode, 0, 1 );
		foreach ( var disp in lightingDisplacements )
		{
			if ( (DispInfos[Faces[disp.Face].DispInfo].Contents & BspContents.OpaqueMask) == 0 || !Intersects( disp.Bounds, start, delta, closest ) )
				continue;
			for ( int index = 0; index < disp.Indices.Length; index += 3 )
				if ( Triangle( start, delta, disp.Positions[disp.Indices[index]], disp.Positions[disp.Indices[index + 1]], disp.Positions[disp.Indices[index + 2]], out float fraction, out _ ) && fraction < closest )
				{
					closest = fraction;
					sky = false;
				}
		}

		return (closest, sky);
		void Visit( int index, float from, float to )
		{
			if ( from >= closest )
				return;
			if ( index < 0 )
			{
				var leaf = leaves[-1 - index];
				for ( int entry = leaf.FirstBrush; entry < leaf.FirstBrush + leaf.BrushCount; entry++ )
				{
					int brushIndex = lightingLeafBrushes[entry];
					if ( !lighting.VisitBrush( brushIndex, generation ) )
						continue;
					var brush = collisionBrushes[brushIndex];
					if ( (brush.Contents & BspContents.OpaqueMask) == 0 )
						continue;
					float enter = -1, leave = 1;
					int texture = -1;
					bool outside = false, missed = false;
					for ( int sideIndex = brush.FirstSide; sideIndex < brush.FirstSide + brush.SideCount; sideIndex++ )
					{
						var side = collisionBrushSides[sideIndex];
						var plane = Planes[side.Plane];
						float first = Vector3.Dot( start, plane.Normal ) - plane.Dist;
						float last = first + Vector3.Dot( delta, plane.Normal );
						outside |= first > 0;
						if ( first > 0 && last > 0 )
						{
							missed = true;
							break;
						}

						if ( first <= 0 && last <= 0 )
							continue;
						if ( first > last )
						{
							float fraction = (first - 0.03125f) / (first - last);
							if ( fraction > enter )
							{
								enter = fraction;
								texture = side.TexInfo;
							}
						}
						else
							leave = Math.Min( leave, (first + 0.03125f) / (first - last) );
					}

					if ( missed || outside && (enter >= leave || enter < -0.03125f) )
						continue;
					float hit = outside ? Math.Max( 0, enter ) : 0;
					if ( hit >= closest )
						continue;
					closest = hit;
					sky = texture >= 0 && (TexInfos[texture].Flags & BspSurfaceFlags.SkyMask) != 0;
				}

				return;
			}

			var node = nodes[index];
			var planeSplit = Planes[node.Plane];
			float distance = Vector3.Dot( start, planeSplit.Normal ) - planeSplit.Dist;
			float rate = Vector3.Dot( delta, planeSplit.Normal );
			float firstDistance = distance + rate * from, lastDistance = distance + rate * to;
			if ( firstDistance >= 0 && lastDistance >= 0 )
			{
				Visit( node.Front, from, to );
				return;
			}

			if ( firstDistance < 0 && lastDistance < 0 )
			{
				Visit( node.Back, from, to );
				return;
			}

			float mid = Math.Clamp( -distance / rate, from, to );
			Visit( firstDistance < 0 ? node.Back : node.Front, from, mid );
			Visit( firstDistance < 0 ? node.Front : node.Back, mid, to );
		}
	}

	internal void BuildLightingDisplacements()
	{
		Span<Vector3> corners = stackalloc Vector3[4];
		for ( int index = Models[0].FirstFace; index < Models[0].FirstFace + Models[0].FaceCount; index++ )
		{
			var face = Faces[index];
			if ( face.DispInfo < 0 )
				continue;
			var disp = DispInfos[face.DispInfo];
			int size = (1 << disp.Power) + 1;
			GetDisplacementCorners( this, face, corners );
			var positions = new Vector3[size * size];
			var coordinates = new Vector2[positions.Length];
			var min = new Vector3( float.MaxValue );
			var max = new Vector3( float.MinValue );
			for ( int y = 0; y < size; y++ )
				for ( int x = 0; x < size; x++ )
				{
					int vertex = x + y * size;
					var offset = DispVerts[disp.DispVertStart + vertex];
					positions[vertex] = DisplacementBasePosition( corners, x / (float)(size - 1), y / (float)(size - 1) ) + offset.Vector * offset.Dist;
					coordinates[vertex] = new Vector2( x * face.LightmapSizeS / (float)(size - 1), y * face.LightmapSizeT / (float)(size - 1) );
					min = Vector3.Min( min, positions[vertex] );
					max = Vector3.Max( max, positions[vertex] );
				}

			var indices = new List<int>();
			AddDisplacementIndices( indices, 0, size, DisplacementTriangleTags( this, disp ) );
			lightingDisplacements.Add( new LightingDisplacement( new BBox( min - Vector3.One, max + Vector3.One ), positions, coordinates, indices.ToArray(), index ) );
		}
	}

	internal static bool Intersects( BBox bounds, Vector3 start, Vector3 delta, float maximum )
	{
		float first = 0, last = maximum;
		for ( int axis = 0; axis < 3; axis++ )
		{
			if ( delta[axis] == 0 )
			{
				if ( start[axis] < bounds.Mins[axis] || start[axis] > bounds.Maxs[axis] )
					return false;
				continue;
			}

			float a = (bounds.Mins[axis] - start[axis]) / delta[axis], b = (bounds.Maxs[axis] - start[axis]) / delta[axis];
			first = Math.Max( first, Math.Min( a, b ) );
			last = Math.Min( last, Math.Max( a, b ) );
			if ( first > last )
				return false;
		}

		return true;
	}

	internal static bool Triangle( Vector3 start, Vector3 delta, Vector3 a, Vector3 b, Vector3 c, out float fraction, out Vector2 uv )
	{
		var ab = b - a;
		var ac = c - a;
		var cross = Vector3.Cross( delta, ac );
		float determinant = Vector3.Dot( ab, cross );
		fraction = 1;
		uv = default;
		if ( Math.Abs( determinant ) < 0.000001f )
			return false;
		var offset = start - a;
		float u = Vector3.Dot( offset, cross ) / determinant;
		var perpendicular = Vector3.Cross( offset, ab );
		float v = Vector3.Dot( delta, perpendicular ) / determinant;
		if ( u < 0 || v < 0 || u + v > 1 )
			return false;
		fraction = Vector3.Dot( ac, perpendicular ) / determinant;
		uv = new Vector2( u, v );
		return fraction >= 0 && fraction < 1;
	}
}
