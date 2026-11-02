using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

internal sealed class BspPhysicsMesh
{
	public readonly List<Vector3> Vertices = [];
	public readonly List<uint> Indices = [];
	public readonly List<string> SurfaceNames = [];
	public string SurfaceName = "default";
	public bool PassAudio;
}

internal sealed record BspPhysicsHull( List<Vector3> Vertices, string SurfaceName, bool MixedSurfaces, bool PassAudio );
internal partial class BspFile
{
	internal const int ContentsClip = 0x30000;
	internal const int ContentsBlockAudio = 0x4003;
	internal const int SurfaceSky = 6;
	internal readonly HashSet<byte> _missingPhysicsMaterials = [];
	internal CollisionBrush[] collisionBrushes = [];
	internal CollisionBrushSide[] collisionBrushSides = [];
	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	internal struct CollisionBrush
	{
		public int FirstSide, SideCount, Contents;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	internal struct CollisionBrushSide
	{
		public ushort Plane;
		public short TexInfo, DispInfo, Bevel;
	}

	internal void ReadCollisionBrushes( Stream stream, long fileStart, (int offset, int length, int version, int uncompressedSize)[] lumps )
	{
		if ( PhysCollideData.Length == 0 && StaticProps.Length == 0 && ambientLighting is null )
			return;
		collisionBrushes = ReadLump<CollisionBrush>( stream, fileStart, lumps[18] );
		collisionBrushSides = ReadLump<CollisionBrushSide>( stream, fileStart, lumps[19] );
		foreach ( var brush in collisionBrushes )
			ValidateRange( brush.FirstSide, brush.SideCount, collisionBrushSides.Length, "Collision brush sides" );
		foreach ( var side in collisionBrushSides )
		{
			ValidateRange( side.Plane, 1, Planes.Length, "Collision brush plane" );
			if ( side.TexInfo >= 0 )
				ValidateRange( side.TexInfo, 1, TexInfos.Length, "Collision brush texture" );
		}
	}

	public (List<BspPhysicsHull> Hulls, List<BspPhysicsMesh> Meshes, bool VirtualTerrain) ParsePhysicsGeometry( int requestedModel = 0 )
	{
		var hulls = new List<BspPhysicsHull>();
		var meshes = new List<BspPhysicsMesh>();
		bool virtualTerrain = false;
		if ( PhysCollideData.Length == 0 )
			return (hulls, meshes, virtualTerrain);
		const float IVP_TO_HL = 1.0f / 0.0254f;
		const int CONTENTS_WATER = 0x20;
		const int CONTENTS_SLIME = 0x10;
		int pos = 0;
		while ( pos < PhysCollideData.Length )
		{
			ValidateRange( pos, 16, PhysCollideData.Length, "Physics model header" );
			int modelIndex = BitConverter.ToInt32( PhysCollideData, pos );
			int dataSize = BitConverter.ToInt32( PhysCollideData, pos + 4 );
			int keydataSize = BitConverter.ToInt32( PhysCollideData, pos + 8 );
			int solidCount = BitConverter.ToInt32( PhysCollideData, pos + 12 );
			pos += 16;
			if ( modelIndex == -1 )
				break;
			int dataStart = pos;
			ValidateRange( modelIndex, 1, Models.Length, "Physics model index" );
			ValidateRange( dataStart, dataSize, PhysCollideData.Length, "Physics model data" );
			int dataEnd = dataStart + dataSize;
			ValidateRange( dataEnd, keydataSize, PhysCollideData.Length, "Physics model keydata" );
			if ( solidCount < 0 || solidCount > dataSize / 4 )
				throw new InvalidDataException( "Invalid BSP physics solid count." );
			if ( modelIndex == requestedModel )
			{
				var (skipSolids, passAudioSolids, usesVirtualTerrain, solidSurfaces, materialTable) = ParsePhysicsKeydata( PhysCollideData, dataEnd, keydataSize, solidCount, CONTENTS_WATER | CONTENTS_SLIME );
				virtualTerrain |= usesVirtualTerrain;
				for ( int s = 0; s < solidCount; s++ )
				{
					ValidateRange( pos, 4, dataEnd, "Physics solid size" );
					int solidSize = BitConverter.ToInt32( PhysCollideData, pos );
					pos += 4;
					ValidateRange( pos, solidSize, dataEnd, "Physics solid data" );
					if ( solidSize == 0 )
						throw new InvalidDataException( "Empty BSP physics solid." );
					if ( !skipSolids.Contains( s ) )
						ParseIvpSolid( PhysCollideData, pos, solidSize, hulls, meshes, IVP_TO_HL, solidSurfaces.GetValueOrDefault( s, "default" ), materialTable, passAudioSolids.Contains( s ) );
					pos += solidSize;
				}

				if ( pos != dataEnd )
					throw new InvalidDataException( "BSP physics solid sizes do not match model data size." );
			}

			pos = dataEnd + keydataSize;
		}

		return (hulls, meshes, virtualTerrain);
	}

	internal static (HashSet<int> SkipSolids, HashSet<int> PassAudioSolids, bool VirtualTerrain, Dictionary<int, string> SolidSurfaces, Dictionary<byte, string> MaterialTable) ParsePhysicsKeydata( byte[] data, int offset, int length, int solidCount, int contentsMask )
	{
		var skip = new HashSet<int>();
		var passAudio = new HashSet<int>();
		bool virtualTerrain = false;
		var solidSurfaces = new Dictionary<int, string>();
		var materialTable = new Dictionary<byte, string>();
		ValidateRange( offset, length, data.Length, "Physics keydata" );
		if ( length == 0 )
			return (skip, passAudio, virtualTerrain, solidSurfaces, materialTable);
		var text = System.Text.Encoding.ASCII.GetString( data, offset, length ).TrimEnd( '\0' );
		foreach ( var block in VmtFile.ParseKeyValues( text, "BSP physics keydata", diagnostic => throw new InvalidDataException( $"Unsupported BSP physics KeyValues: {diagnostic.Message}" ) ) )
		{
			if ( !block.IsBlock )
				throw new InvalidDataException( "BSP physics keydata must contain blocks." );
			if ( block.Name.Equals( "virtualterrain", StringComparison.OrdinalIgnoreCase ) )
			{
				virtualTerrain = true;
				continue;
			}

			if ( block.Name.Equals( "materialtable", StringComparison.OrdinalIgnoreCase ) )
			{
				foreach ( var entry in block.Children )
				{
					if ( entry.IsBlock || !byte.TryParse( entry.Value, out var material ) || material is 0 or > 127 || !materialTable.TryAdd( material, entry.Name ) )
						throw new InvalidDataException( "BSP physics material table has an invalid or duplicate index." );
				}

				continue;
			}

			if ( !block.Name.Equals( "staticsolid", StringComparison.OrdinalIgnoreCase ) && !block.Name.Equals( "solid", StringComparison.OrdinalIgnoreCase ) && !block.Name.Equals( "fluid", StringComparison.OrdinalIgnoreCase ) )
				continue;
			if ( !block.Values.TryGetValue( "index", out var indexValue ) || !int.TryParse( indexValue, out int index ) )
				throw new InvalidDataException( "BSP physics solid has an invalid index." );
			ValidateRange( index, 1, solidCount, "Physics keydata solid index" );
			if ( block.Values.TryGetValue( "surfaceprop", out var surface ) )
				solidSurfaces[index] = surface;
			if ( block.Values.TryGetValue( "contents", out var contentsValue ) )
			{
				if ( !int.TryParse( contentsValue, out int contents ) )
					throw new InvalidDataException( "BSP physics solid has invalid contents." );
				if ( (contents & contentsMask) != 0 )
					skip.Add( index );
				if ( (contents & ContentsClip) != 0 && (contents & ContentsBlockAudio) == 0 )
					passAudio.Add( index );
			}
		}

		return (skip, passAudio, virtualTerrain, solidSurfaces, materialTable);
	}

	internal void ParseIvpSolid( byte[] data, int blobStart, int blobSize, List<BspPhysicsHull> hulls, List<BspPhysicsMesh> meshes, float scale, string surface, Dictionary<byte, string> materialTable, bool passAudio )
	{
		int blobEnd = blobStart + blobSize;
		int pos = blobStart;
		ValidateRange( pos, 28, blobEnd, "Physics surface header" );
		int vphysicsId = BitConverter.ToInt32( data, pos );
		int ivpSurfaceStart;
		if ( vphysicsId == 0x59485056 )
		{
			short modelType = BitConverter.ToInt16( data, pos + 6 );
			if ( modelType == 1 )
			{
				Log.Warning( "Skipping BSP type-1 physics solid; world collision will be incomplete." );
				return;
			}

			if ( modelType != 0 )
				throw new NotSupportedException( $"Unsupported BSP physics model type {modelType}; expected an IVP compact surface." );
			ivpSurfaceStart = pos + 28;
		}
		else
		{
			ivpSurfaceStart = pos;
		}

		ValidateRange( ivpSurfaceStart, 48, blobEnd, "IVP compact surface" );
		int offsetLedgetreeRoot = BitConverter.ToInt32( data, ivpSurfaceStart + 32 );
		int ivpsMagic = BitConverter.ToInt32( data, ivpSurfaceStart + 44 );
		if ( ivpsMagic != 0x53505649 )
			throw new NotSupportedException( "BSP collision data is not an IVPS compact surface." );
		int treeRoot = ResolvePhysicsOffset( ivpSurfaceStart, offsetLedgetreeRoot, 28, blobStart, blobEnd );
		var stack = new Stack<int>();
		var visited = new HashSet<int>();
		BspPhysicsMesh mesh = null;
		Dictionary<int, uint> meshVertices = null;
		stack.Push( treeRoot );
		while ( stack.Count > 0 )
		{
			int nodePos = stack.Pop();
			if ( !visited.Add( nodePos ) )
				throw new InvalidDataException( "BSP physics ledge tree contains a cycle or shared child." );
			int offsetRightNode = BitConverter.ToInt32( data, nodePos );
			int offsetCompactLedge = BitConverter.ToInt32( data, nodePos + 4 );
			if ( offsetRightNode == 0 )
			{
				int ledgePos = ResolvePhysicsOffset( nodePos, offsetCompactLedge, 16, blobStart, blobEnd );
				if ( BitConverter.ToInt16( data, ledgePos + 12 ) == 2 )
				{
					mesh ??= new BspPhysicsMesh
					{
						SurfaceName = surface,
						PassAudio = passAudio
					};
					meshVertices ??= [];
					ExtractLedgeMesh( data, ledgePos, blobStart, blobEnd, mesh, meshVertices, scale, surface, materialTable );
				}
				else
				{
					ExtractLedgeHull( data, ledgePos, blobStart, blobEnd, hulls, meshes, scale, surface, materialTable, passAudio );
				}
			}
			else
			{
				stack.Push( ResolvePhysicsOffset( nodePos, 28, 28, blobStart, blobEnd ) );
				stack.Push( ResolvePhysicsOffset( nodePos, offsetRightNode, 28, blobStart, blobEnd ) );
			}
		}

		if ( mesh is not null )
			meshes.Add( mesh );
	}

	internal static int ResolvePhysicsOffset( int origin, int offset, int size, int blobStart, int blobEnd )
	{
		long position = (long)origin + offset;
		ValidateRange( position - blobStart, size, blobEnd - blobStart, "IVP relative offset" );
		return (int)position;
	}

	internal static Vector3 ReadPhysicsVertex( byte[] data, int position, float scale )
	{
		float x = BitConverter.ToSingle( data, position );
		float y = BitConverter.ToSingle( data, position + 4 );
		float z = BitConverter.ToSingle( data, position + 8 );
		var vertex = new Vector3( x * scale, z * scale, -y * scale );
		if ( !float.IsFinite( vertex.x ) || !float.IsFinite( vertex.y ) || !float.IsFinite( vertex.z ) )
			throw new InvalidDataException( "IVP ledge contains a non-finite vertex." );
		return vertex;
	}

	internal void ExtractLedgeMesh( byte[] data, int ledgePos, int blobStart, int blobEnd, BspPhysicsMesh mesh, Dictionary<int, uint> vertices, float scale, string surface, Dictionary<byte, string> materialTable )
	{
		ValidateRange( ledgePos, 16 + 2 * 16, blobEnd, "IVP triangle ledge" );
		int poolBase = ResolvePhysicsOffset( ledgePos, BitConverter.ToInt32( data, ledgePos ), 0, blobStart, blobEnd );
		Span<uint> indices = stackalloc uint[6];
		for ( int triangle = 0; triangle < 2; triangle++ )
		{
			mesh.SurfaceNames.Add( TriangleSurface( data, ledgePos + 16 + triangle * 16, surface, materialTable ) );
			for ( int edge = 0; edge < 3; edge++ )
			{
				int vertex = BitConverter.ToUInt16( data, ledgePos + 16 + triangle * 16 + 4 + edge * 4 );
				int position = ResolvePhysicsOffset( poolBase, vertex * 16, 16, blobStart, blobEnd );
				if ( !vertices.TryGetValue( position, out uint index ) )
				{
					index = (uint)mesh.Vertices.Count;
					mesh.Vertices.Add( ReadPhysicsVertex( data, position, scale ) );
					vertices.Add( position, index );
				}

				indices[triangle * 3 + edge] = index;
			}
		}

		int reverseStart = indices[..3].IndexOf( indices[3] );
		if ( indices[0] == indices[1] || indices[0] == indices[2] || indices[1] == indices[2] || reverseStart < 0 || indices[4] != indices[(reverseStart + 2) % 3] || indices[5] != indices[(reverseStart + 1) % 3] )
			throw new InvalidDataException( "IVP triangle ledge must contain two opposite faces sharing three distinct vertices." );
		mesh.Indices.AddRange( indices );
	}

	internal void ExtractLedgeHull( byte[] data, int ledgePos, int blobStart, int blobEnd, List<BspPhysicsHull> hulls, List<BspPhysicsMesh> meshes, float scale, string surface, Dictionary<byte, string> materialTable, bool passAudio )
	{
		int cPointOffset = BitConverter.ToInt32( data, ledgePos );
		short nTriangles = BitConverter.ToInt16( data, ledgePos + 12 );
		if ( nTriangles <= 0 )
			throw new InvalidDataException( "Invalid IVP ledge triangle count." );
		int triStart = ledgePos + 16;
		ValidateRange( triStart, nTriangles * 16, blobEnd, "IVP ledge triangles" );
		int poolBase = ResolvePhysicsOffset( ledgePos, cPointOffset, 0, blobStart, blobEnd );
		var uniqueIndices = new HashSet<int>();
		var surfaceNames = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		for ( int t = 0; t < nTriangles; t++ )
		{
			int tp = triStart + t * 16;
			if ( (BitConverter.ToUInt32( data, tp ) & 0x80000000) == 0 )
				surfaceNames.Add( TriangleSurface( data, tp, surface, materialTable ) );
			int v0 = BitConverter.ToUInt16( data, tp + 4 );
			int v1 = BitConverter.ToUInt16( data, tp + 8 );
			int v2 = BitConverter.ToUInt16( data, tp + 12 );
			uniqueIndices.Add( v0 );
			uniqueIndices.Add( v1 );
			uniqueIndices.Add( v2 );
		}

		if ( uniqueIndices.Count < 3 )
			throw new InvalidDataException( "IVP ledge has fewer than three unique vertices." );
		var vertices = new List<Vector3>( uniqueIndices.Count );
		foreach ( int idx in uniqueIndices )
		{
			int vp = ResolvePhysicsOffset( poolBase, idx * 16, 16, blobStart, blobEnd );
			vertices.Add( ReadPhysicsVertex( data, vp, scale ) );
		}

		int brushIndex = BitConverter.ToInt32( data, ledgePos + 4 );
		ValidateRange( brushIndex, 1, collisionBrushes.Length, "IVP collision brush index" );
		var brush = collisionBrushes[brushIndex];
		passAudio |= (brush.Contents & ContentsClip) != 0 && (brush.Contents & ContentsBlockAudio) == 0;
		bool hasSky = false;
		for ( int index = brush.FirstSide; index < brush.FirstSide + brush.SideCount && !hasSky; index++ )
		{
			int texInfo = collisionBrushSides[index].TexInfo;
			hasSky = texInfo >= 0 && (TexInfos[texInfo].Flags & SurfaceSky) != 0;
		}

		if ( passAudio || !hasSky )
		{
			hulls.Add( new( vertices, surfaceNames.Count == 1 ? surfaceNames.First() : surface, surfaceNames.Count > 1, passAudio ) );
			return;
		}

		var vertexMap = new Dictionary<int, uint>( uniqueIndices.Count );
		foreach ( int idx in uniqueIndices )
			vertexMap.Add( idx, (uint)vertexMap.Count );
		var triangles = new uint[nTriangles * 3];
		var audioFaces = new bool[nTriangles];
		bool anyPassAudio = false, anyBlocking = false;
		for ( int triangle = 0; triangle < nTriangles; triangle++ )
		{
			int tp = triStart + triangle * 16;
			for ( int corner = 0; corner < 3; corner++ )
				triangles[triangle * 3 + corner] = vertexMap[BitConverter.ToUInt16( data, tp + 4 + corner * 4 )];
			var a = vertices[(int)triangles[triangle * 3]];
			var b = vertices[(int)triangles[triangle * 3 + 1]];
			var c = vertices[(int)triangles[triangle * 3 + 2]];
			var normal = Vector3.Cross( c - a, b - a ).Normal;
			audioFaces[triangle] = BrushSidePassesAudio( brushIndex, normal );
			anyPassAudio |= audioFaces[triangle];
			anyBlocking |= !audioFaces[triangle];
		}

		if ( !anyPassAudio || !anyBlocking )
		{
			hulls.Add( new( vertices, surfaceNames.Count == 1 ? surfaceNames.First() : surface, surfaceNames.Count > 1, anyPassAudio ) );
			return;
		}

		foreach ( bool transparent in new[]
		{
			false,
			true
		}

		)
		{
			var mesh = new BspPhysicsMesh
			{
				SurfaceName = surface,
				PassAudio = transparent
			};
			mesh.Vertices.AddRange( vertices );
			for ( int triangle = 0; triangle < nTriangles; triangle++ )
			{
				if ( audioFaces[triangle] != transparent )
					continue;
				var name = TriangleSurface( data, triStart + triangle * 16, surface, materialTable );
				mesh.SurfaceNames.Add( name );
				mesh.SurfaceNames.Add( name );
				mesh.Indices.Add( triangles[triangle * 3] );
				mesh.Indices.Add( triangles[triangle * 3 + 1] );
				mesh.Indices.Add( triangles[triangle * 3 + 2] );
				mesh.Indices.Add( triangles[triangle * 3] );
				mesh.Indices.Add( triangles[triangle * 3 + 2] );
				mesh.Indices.Add( triangles[triangle * 3 + 1] );
			}

			meshes.Add( mesh );
		}
	}

	internal bool BrushSidePassesAudio( int brushIndex, Vector3 normal )
	{
		var brush = collisionBrushes[brushIndex];
		float best = -1;
		int texInfo = -1;
		for ( int index = brush.FirstSide; index < brush.FirstSide + brush.SideCount; index++ )
		{
			var side = collisionBrushSides[index];
			float dot = Vector3.Dot( normal, Planes[side.Plane].Normal );
			if ( dot <= best )
				continue;
			best = dot;
			texInfo = side.TexInfo;
		}

		return texInfo >= 0 && (TexInfos[texInfo].Flags & SurfaceSky) != 0;
	}

	internal string TriangleSurface( byte[] data, int position, string surface, Dictionary<byte, string> materialTable )
	{
		byte index = (byte)((BitConverter.ToUInt32( data, position ) >> 24) & 0x7f);
		if ( index == 0 )
			return surface;
		if ( materialTable.TryGetValue( index, out var name ) )
			return name;
		if ( _missingPhysicsMaterials.Add( index ) )
			Log.Warning( $"BSP physics material index {index} has no compiled mapping; using the solid's surface '{surface}'." );
		return surface;
	}
}
