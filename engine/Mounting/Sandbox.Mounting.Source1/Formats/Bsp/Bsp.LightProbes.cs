using System;
using System.IO;
using static Sandbox.Mounting.Source1.BspGeometry;
using static Sandbox.Mounting.Source1.BspWorldLightType;

namespace Sandbox.Mounting.Source1;

internal partial class BspFile
{
	internal Vector3Int ProbeCounts( int leafIndex, bool hasDirectLights )
	{
		var size = leaves[leafIndex].Bounds.Size;
		int budget = Math.Clamp( ambientLighting.Samples( leafIndex ).Length * 8, hasDirectLights ? 8 : 1, 64 );
		var counts = new Vector3Int( Count( size.x ), Count( size.y ), Count( size.z ) );
		while ( counts.x * counts.y * counts.z > budget )
		{
			int axis = -1;
			float spacing = float.MaxValue;
			for ( int candidate = 0; candidate < 3; candidate++ )
			{
				if ( counts[candidate] <= 1 || size[candidate] / counts[candidate] >= spacing )
					continue;
				axis = candidate;
				spacing = size[candidate] / counts[candidate];
			}

			counts[axis]--;
		}

		return counts;
		static int Count( float extent ) => Math.Clamp( (int)Math.Ceiling( extent / 32 ), 1, 4 );
	}
}

internal partial class BspFile
{
	internal BspAmbientLighting ambientLighting;
	internal void ReadLeafAmbient( Stream stream, long fileStart, (int offset, int length, int version, int uncompressedSize)[] lumps )
	{
		bool embedded = lumps[10].version == 0;
		bool hdr = !embedded && Hdr && lumps[55].length > 0;
		int sampleLump = hdr ? 55 : 56;
		int indexLump = hdr ? 51 : 52;
		if ( lumps[sampleLump].length == 0 && lumps[indexLump].length == 0 && (lumps[10].length == 0 || lumps[10].version != 0) )
			return;
		ReadSpatialData( stream, fileStart, lumps );
		if ( Hdr && !hdr && !embedded )
			Log.Warning( "BSP has no HDR leaf ambient samples; using its LDR leaf ambient data." );
		var leafData = embedded ? ReadRaw( stream, fileStart, lumps[10], 1 ) : [];
		var indices = !embedded && lumps[sampleLump].version == 1
			? ReadRaw( stream, fileStart, lumps[indexLump], version: null ) : [];
		var lighting = embedded ? [] : ReadRaw( stream, fileStart, lumps[sampleLump], version: null );
		ambientLighting = new BspAmbientLighting( leaves.Select( leaf => leaf.Bounds ).ToArray(), leafData, lumps[10].version,
			indices, lighting, indices.Length > 0 ? 1 : 0 );
	}

	internal MapEntity ShadowControl => Entities.LastOrDefault( entity => string.Equals( entity.Get( "classname" ), "shadow_control", StringComparison.OrdinalIgnoreCase ) );

	internal bool SunShadowsEnabled( bool skybox ) => !skybox && (ShadowControl is not { } control || control.Integer( "disableallshadows" ) == 0);
	internal bool UsesImportedLightstyle( PropWorldLight light ) => (light.Style == 0 || light.Type == SkyLight) && lightingStyles[light.Style] > 0;
	internal int ImportedSunIndex => Array.FindIndex( worldLights, light => light.Type == SkyLight && UsesImportedLightstyle( light ) && (light.Flags & BspWorldLightFlags.InAmbientCube) == 0 );

	internal int[] ProbeLightCandidates( BspLeaf leaf, BspLightingContext lighting )
	{
		int sun = ImportedSunIndex;
		if ( !lighting.Pvs.TryGetValue( leaf.Cluster, out var pvs ) )
			lighting.Pvs.Add( leaf.Cluster, pvs = ReadPvs( leaf.Cluster ) );
		return Enumerable.Range( 0, worldLights.Length ).Where( index =>
		{
			var light = worldLights[index];
			if ( !UsesImportedLightstyle( light ) || light.Type == SkyAmbient || (light.Flags & BspWorldLightFlags.InAmbientCube) != 0 )
				return false;
			if ( light.Type == SkyLight )
				return index == sun;
			if ( light.Cluster < 0 || pvs is not null && (light.Cluster / 8 >= pvs.Length || (pvs[light.Cluster / 8] & (1 << (light.Cluster & 7))) == 0) )
				return false;
			return light.Type == QuakeLight || (light.Origin - Vector3.Clamp( light.Origin, leaf.Bounds.Mins, leaf.Bounds.Maxs )).LengthSquared <= light.Radius * light.Radius;
		} ).ToArray();
	}

	internal int[] SelectProbeLights( int leafIndex, int[] candidates, int[] indices, BspLightingContext lighting )
	{
		var leaf = leaves[leafIndex];
		var ranked = new List<(int Index, float Brightness)>();
		foreach ( int index in candidates )
		{
			if ( indices[index] < 0 )
				continue;
			var light = worldLights[index];
			float maximum = Brightness( leaf.Bounds.Center );
			foreach ( var sample in ambientLighting.Samples( leafIndex ) )
				maximum = Math.Max( maximum, Brightness( sample.Position ) );
			if ( maximum > 0 )
				ranked.Add( (index, maximum) );
			float Brightness( Vector3 position ) => ProbeLightStrength( light, position, leaf, lighting ) * Vector3.Dot( light.Intensity, new Vector3( 0.299f, 0.587f, 0.114f ) );
		}

		return ranked.OrderByDescending( item => worldLights[item.Index].Type == SkyLight ).ThenByDescending( item => item.Brightness ).ThenBy( item => item.Index ).Take( 4 ).Select( item => item.Index ).ToArray();
	}

	internal float ProbeLightStrength( PropWorldLight light, Vector3 position, BspLeaf leaf, BspLightingContext lighting )
	{
		if ( light.Type == SkyLight )
			return 1;
		if ( !TouchesLightingBucket( light, position ) )
			return 0;
		var direction = (light.Origin - position).Normal;
		float strength = lightingStyles[light.Style] * light.Angular( direction ) * light.Falloff( position );
		if ( strength <= 0 || light.Type != BspWorldLightType.Surface && strength * light.Intensity.Length < 0.0002f )
			return 0;
		float distance = (light.Origin - position).Length;
		if ( light.Type != QuakeLight && distance > light.Radius )
			return 0;
		return distance > distance * TraceOpaque( light.Origin, position - light.Origin, lighting ).Fraction + 8 ? 0 : strength;
	}
}
