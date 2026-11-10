using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

internal partial class BspFile
{
	internal readonly HashSet<int> skyboxAreas = [];
	internal BspNode[] nodes = [];
	internal BspLeaf[] leaves = [];
	internal bool[] skyboxFaces = [];
	internal bool[] activeSkyboxFaces = [];
	internal ushort[] leafFaces = [];
	internal int activeSkyboxArea = -1;
	internal byte[] skyboxPvs;
	public MapEntity SkyCamera { get; private set; }

	internal readonly record struct BspNode( int Plane, int Front, int Back, int FirstFace, int FaceCount );
	internal readonly record struct BspLeaf( int Contents, int Area, int FirstFace, int FaceCount, int Cluster, int Flags, int FirstBrush, int BrushCount, BBox Bounds );
	public bool IsSkyboxFace( int face ) => skyboxFaces.Length > 0 && skyboxFaces[face];
	public bool IsActiveSkyboxFace( int face ) => activeSkyboxFaces.Length > 0 && activeSkyboxFaces[face];
	public bool IsSkyboxPosition( Vector3 position ) => skyboxAreas.Count > 0 && skyboxAreas.Contains( leaves[FindLeaf( position )].Area );
	public bool IsActiveSkyboxPosition( Vector3 position ) => activeSkyboxArea >= 0 && IsActiveSkyboxLeaf( leaves[FindLeaf( position )] );
	public bool IsSkyboxProp( MapProp prop, bool active )
	{
		if ( prop.Leaves is not { Length: > 0 } || skyboxAreas.Count == 0 )
			return active ? IsActiveSkyboxPosition( prop.Position ) : !IsSkyboxPosition( prop.Position );
		foreach ( var index in prop.Leaves )
		{
			ValidateRange( index, 1, leaves.Length, "Static-prop leaf index" );
			var leaf = leaves[index];
			if ( active ? IsActiveSkyboxLeaf( leaf ) : (leaf.Contents & 1) == 0 && !skyboxAreas.Contains( leaf.Area ) )
				return true;
		}

		return false;
	}

	internal bool IsActiveSkyboxLeaf( BspLeaf leaf ) => leaf.Area == activeSkyboxArea && (leaf.Contents & 1) == 0 && (skyboxPvs is null || leaf.Cluster >= 0 && leaf.Cluster / 8 < skyboxPvs.Length && (skyboxPvs[leaf.Cluster / 8] & (1 << (leaf.Cluster & 7))) != 0);
	internal void ReadSkyboxAreas( Stream stream, long fileStart, (int offset, int length, int version, int uncompressedSize)[] lumps )
	{
		var camera = Entities.LastOrDefault( entity => string.Equals( entity.Get( "classname" ), "sky_camera", StringComparison.OrdinalIgnoreCase ) );
		if ( camera is null )
			return;
		ReadSpatialData( stream, fileStart, lumps );
		visibility = ReadRaw( stream, fileStart, lumps[4] );
		var cameraLeaf = leaves[FindLeaf( camera.Vector( "origin" ) )];
		if ( (cameraLeaf.Contents & 1) != 0 )
			throw new InvalidDataException( "BSP sky_camera is inside solid geometry." );
		if ( cameraLeaf.Area == 255 )
			return;
		SkyCamera = camera;
		activeSkyboxArea = cameraLeaf.Area;
		if ( cameraLeaf.Area == 0 )
		{
			Log.Warning( "BSP sky_camera has no isolated area; shared geometry is retained in both views." );
		}
		else
		{
			skyboxAreas.Add( cameraLeaf.Area );
		}

		skyboxPvs = ReadPvs( cameraLeaf.Cluster );
		skyboxFaces = new bool[Faces.Length];
		activeSkyboxFaces = new bool[Faces.Length];
		var mainFaces = new bool[Faces.Length];
		foreach ( var leaf in leaves )
		{
			if ( (leaf.Contents & 1) != 0 )
				continue;
			var faces = skyboxAreas.Contains( leaf.Area ) ? skyboxFaces : mainFaces;
			for ( int i = leaf.FirstFace; i < leaf.FirstFace + leaf.FaceCount; i++ )
			{
				faces[leafFaces[i]] = true;
				if ( IsActiveSkyboxLeaf( leaf ) )
					activeSkyboxFaces[leafFaces[i]] = true;
			}
		}

		for ( int i = 0; i < skyboxFaces.Length; i++ )
			skyboxFaces[i] &= !mainFaces[i];
		ClassifySkyboxDisplacements();
	}

	internal void ReadSpatialData( Stream stream, long fileStart, (int offset, int length, int version, int uncompressedSize)[] lumps )
	{
		if ( leaves.Length > 0 )
			return;
		var nodeData = ReadRaw( stream, fileStart, lumps[5] );
		if ( nodeData.Length % 32 != 0 )
			throw new InvalidDataException( "Invalid BSP node lump for 3D skybox classification." );
		nodes = new BspNode[nodeData.Length / 32];
		for ( int i = 0; i < nodes.Length; i++ )
		{
			int offset = i * 32;
			nodes[i] = new BspNode( BitConverter.ToInt32( nodeData, offset ), BitConverter.ToInt32( nodeData, offset + 4 ), BitConverter.ToInt32( nodeData, offset + 8 ), BitConverter.ToUInt16( nodeData, offset + 24 ), BitConverter.ToUInt16( nodeData, offset + 26 ) );
			ValidateRange( nodes[i].FirstFace, nodes[i].FaceCount, Faces.Length, "Node faces" );
		}

		var leafData = ReadRaw( stream, fileStart, lumps[10], 1 );
		int stride = lumps[10].version == 0 ? 56 : 32;
		if ( leafData.Length == 0 || leafData.Length % stride != 0 )
			throw new InvalidDataException( "Invalid BSP leaf lump for 3D skybox classification." );
		leaves = new BspLeaf[leafData.Length / stride];
		leafFaces = ReadLump<ushort>( stream, fileStart, lumps[16] );
		for ( int i = 0; i < leaves.Length; i++ )
		{
			int offset = i * stride;
			var leaf = new BspLeaf( BitConverter.ToInt32( leafData, offset ), BitConverter.ToUInt16( leafData, offset + 6 ) & 0x1ff, BitConverter.ToUInt16( leafData, offset + 20 ), BitConverter.ToUInt16( leafData, offset + 22 ), BitConverter.ToInt16( leafData, offset + 4 ), BitConverter.ToUInt16( leafData, offset + 6 ) >> 9, BitConverter.ToUInt16( leafData, offset + 24 ), BitConverter.ToUInt16( leafData, offset + 26 ), new BBox( new Vector3( BitConverter.ToInt16( leafData, offset + 8 ), BitConverter.ToInt16( leafData, offset + 10 ), BitConverter.ToInt16( leafData, offset + 12 ) ), new Vector3( BitConverter.ToInt16( leafData, offset + 14 ), BitConverter.ToInt16( leafData, offset + 16 ), BitConverter.ToInt16( leafData, offset + 18 ) ) ) );
			ValidateRange( leaf.FirstFace, leaf.FaceCount, leafFaces.Length, "Leaf faces" );
			leaves[i] = leaf;
		}

		foreach ( var face in leafFaces )
			ValidateRange( face, 1, Faces.Length, "Leaf face index" );
		ValidateNodes();
	}

	internal void ClassifySkyboxDisplacements()
	{
		if ( skyboxAreas.Count == 0 )
			return;
		Span<Vector3> corners = stackalloc Vector3[4];
		var pending = new Stack<int>();
		var world = Models[0];
		for ( int i = world.FirstFace; i < world.FirstFace + world.FaceCount; i++ )
		{
			var face = Faces[i];
			if ( face.DispInfo < 0 )
				continue;
			var disp = DispInfos[face.DispInfo];
			GetDisplacementCorners( this, face, corners );
			int size = (1 << disp.Power) + 1;
			var bounds = default( BBox );
			for ( int y = 0; y < size; y++ )
				for ( int x = 0; x < size; x++ )
				{
					int vertex = x + y * size;
					var offset = DispVerts[disp.DispVertStart + vertex];
					var position = DisplacementBasePosition( corners, x / (float)(size - 1), y / (float)(size - 1) ) + offset.Vector * offset.Dist;
					if ( !IsFinite( position ) )
						throw new InvalidDataException( $"Displacement {face.DispInfo} has non-finite bounds." );
					bounds = vertex == 0 ? new BBox( position, position ) : bounds.AddPoint( position );
				}

			bounds = new BBox( bounds.Mins - Vector3.One, bounds.Maxs + Vector3.One );
			skyboxFaces[i] = IsSkyboxBounds( bounds, pending, out activeSkyboxFaces[i] );
		}
	}

	internal bool IsSkyboxBounds( BBox bounds, Stack<int> pending, out bool activeSkybox )
	{
		bool skybox = false;
		bool mainWorld = false;
		activeSkybox = false;
		pending.Clear();
		pending.Push( Models[0].HeadNode );
		while ( pending.TryPop( out int index ) )
		{
			if ( index < 0 )
			{
				var leaf = leaves[-1 - index];
				if ( (leaf.Contents & 1) != 0 )
					continue;
				mainWorld |= !skyboxAreas.Contains( leaf.Area );
				skybox |= skyboxAreas.Contains( leaf.Area );
				activeSkybox |= IsActiveSkyboxLeaf( leaf );
				continue;
			}

			var node = nodes[index];
			var plane = Planes[node.Plane];
			int sides;
			if ( plane.Type is >= 0 and < 3 )
			{
				float min = plane.Type == 0 ? bounds.Mins.x : plane.Type == 1 ? bounds.Mins.y : bounds.Mins.z;
				float max = plane.Type == 0 ? bounds.Maxs.x : plane.Type == 1 ? bounds.Maxs.y : bounds.Maxs.z;
				sides = plane.Dist <= min ? 1 : plane.Dist >= max ? 2 : 3;
			}
			else
			{
				var leading = new Vector3( plane.Normal.x < 0 ? bounds.Mins.x : bounds.Maxs.x, plane.Normal.y < 0 ? bounds.Mins.y : bounds.Maxs.y, plane.Normal.z < 0 ? bounds.Mins.z : bounds.Maxs.z );
				var trailing = new Vector3( plane.Normal.x < 0 ? bounds.Maxs.x : bounds.Mins.x, plane.Normal.y < 0 ? bounds.Maxs.y : bounds.Mins.y, plane.Normal.z < 0 ? bounds.Maxs.z : bounds.Mins.z );
				sides = Vector3.Dot( leading, plane.Normal ) >= plane.Dist ? 1 : 0;
				if ( Vector3.Dot( trailing, plane.Normal ) < plane.Dist )
					sides |= 2;
			}

			if ( (sides & 1) != 0 )
				pending.Push( node.Front );
			if ( (sides & 2) != 0 )
				pending.Push( node.Back );
		}

		return skybox && !mainWorld;
	}

	public void FilterSkyboxPhysics( List<BspPhysicsHull> hulls, List<BspPhysicsMesh> meshes )
	{
		var pending = new Stack<int>();
		hulls.RemoveAll( hull => !IsActive( CollectionsMarshal.AsSpan( hull.Vertices ) ) );
		Span<Vector3> triangle = stackalloc Vector3[3];
		for ( int index = meshes.Count - 1; index >= 0; index-- )
		{
			var source = meshes[index];
			var filtered = new BspPhysicsMesh
			{
				SurfaceName = source.SurfaceName,
				PassAudio = source.PassAudio
			};
			var vertices = new Dictionary<uint, uint>();
			for ( int offset = 0; offset < source.Indices.Count; offset += 3 )
			{
				for ( int corner = 0; corner < 3; corner++ )
					triangle[corner] = source.Vertices[(int)source.Indices[offset + corner]];
				if ( !IsActive( triangle ) )
					continue;
				filtered.SurfaceNames.Add( source.SurfaceNames[offset / 3] );
				for ( int corner = 0; corner < 3; corner++ )
				{
					uint original = source.Indices[offset + corner];
					if ( !vertices.TryGetValue( original, out uint output ) )
					{
						output = (uint)filtered.Vertices.Count;
						vertices.Add( original, output );
						filtered.Vertices.Add( source.Vertices[(int)original] );
					}

					filtered.Indices.Add( output );
				}
			}

			if ( filtered.Indices.Count == 0 )
				meshes.RemoveAt( index );
			else
				meshes[index] = filtered;
		}

		bool IsActive( ReadOnlySpan<Vector3> vertices )
		{
			if ( SkyCamera is null || vertices.IsEmpty )
				return false;
			var bounds = new BBox( vertices[0], vertices[0] );
			foreach ( var vertex in vertices )
				bounds = bounds.AddPoint( vertex );
			IsSkyboxBounds( new BBox( bounds.Mins - Vector3.One, bounds.Maxs + Vector3.One ), pending, out bool active );
			return active;
		}
	}

	internal int FindLeaf( Vector3 position )
	{
		int index = Models[0].HeadNode;
		while ( index >= 0 )
		{
			var node = nodes[index];
			var plane = Planes[node.Plane];
			float distance = plane.Type switch
			{
				0 => position.x - plane.Dist,
				1 => position.y - plane.Dist,
				2 => position.z - plane.Dist,
				_ => Vector3.Dot( position, plane.Normal ) - plane.Dist
			};
			index = distance < 0 ? node.Back : node.Front;
		}

		return -1 - index;
	}

	internal void ValidateNodes()
	{
		ValidateChild( Models[0].HeadNode );
		foreach ( var node in nodes )
		{
			ValidateRange( node.Plane, 1, Planes.Length, "Node plane" );
			ValidateChild( node.Front );
			ValidateChild( node.Back );
		}

		var states = new byte[nodes.Length];
		var pending = new Stack<(int Index, bool Exit)>();
		for ( int root = 0; root < nodes.Length; root++ )
		{
			pending.Push( (root, false) );
			while ( pending.TryPop( out var entry ) )
			{
				if ( entry.Index < 0 )
					continue;
				if ( entry.Exit )
				{
					states[entry.Index] = 2;
					continue;
				}

				if ( states[entry.Index] == 2 )
					continue;
				if ( states[entry.Index] == 1 )
					throw new InvalidDataException( "BSP node tree contains a cycle." );
				states[entry.Index] = 1;
				var node = nodes[entry.Index];
				pending.Push( (entry.Index, true) );
				pending.Push( (node.Back, false) );
				pending.Push( (node.Front, false) );
			}
		}

		void ValidateChild( int child )
		{
			if ( child >= 0 )
				ValidateRange( child, 1, nodes.Length, "Node child" );
			else
				ValidateRange( -1L - child, 1, leaves.Length, "Node leaf" );
		}
	}
}
