using Sandbox;
using System;
using System.Collections.Generic;

namespace RasLib;

// Minimal KF2 reader: meshes + materials + texture file names. Animations/lights/skin skipped.
public class Kf2File
{
	const uint ChunkTag = 0x0C;
	const uint Node = 0x00010000;
	const uint Camera = 0x00010001;
	const uint Mesh = 0x00010005;
	const uint Geometry = 0x00010006;
	const uint Polygons = 0x00010007;
	const uint Polygon = 0x00010008;
	const uint Smoothing = 0x0001000B;
	const uint PolygonMaterial = 0x0001000C;
	const uint UvMapping = 0x0001000E;
	const uint MaterialList = 0x0001000F;
	const uint Material = 0x00010010;
	const uint Texture = 0x00010011;
	const uint KeyframeAnimation = 0x00010012;
	const uint AnimationChunk = 0x00010013;
	const uint SkinChunk = 0x00010014;
	const uint ReferenceToData = 0x0001001A;

	public class Primitive
	{
		public Vector3[] Positions;
		public Vector3[] Normals;
		public Vector2[] Uvs;
		public int[] Indices;
		public string MaterialName;
		public string NodeName;
		public int VertexStart;
	}

	public class SkinData
	{
		public string[] BoneNames;
		public int[] Offsets;
		public int[] Counts;
		public int[] BoneIndices;
		public float[] Weights;
	}

	public class MeshNode
	{
		public string Name;
		public string Parent;
		public bool HasParent;
		public Mat43 Local;
	}

	public class NodeAnim
	{
		public string NodeName;
		public string ParentName;
		public float Fps;
		public bool Looping;
		public int TotalFrames;
		public List<(int Frame, Mat43 Local)> Keys = [];
	}

	public class MaterialDef
	{
		public string Name;
		public string DiffuseTexture;
		public bool TwoSided;
	}

	public List<Primitive> Primitives { get; } = [];
	public List<MeshNode> Nodes { get; } = [];
	public List<NodeAnim> Animations { get; } = [];
	public Dictionary<string, MaterialDef> Materials { get; } = new( StringComparer.OrdinalIgnoreCase );
	public string TextureDirs { get; private set; }
	public SkinData Skin { get; private set; }

	readonly Dictionary<string, MeshNode> _nodesByName = new( StringComparer.OrdinalIgnoreCase );
	readonly List<(MeshNode Node, int Start, int Count)> _meshRanges = [];
	readonly MaxReader _r;

	Kf2File( byte[] data )
	{
		_r = new MaxReader( data );
	}

	public static Kf2File Parse( byte[] data )
	{
		var kf2 = new Kf2File( data );
		kf2.ReadTopLevel();
		kf2.BakeNodeTransforms();
		return kf2;
	}

	void ReadTopLevel()
	{
		while ( !_r.Eof )
		{
			var start = _r.Position;
			if ( !TryReadHeader( out var id, out var version, out var size, out _ ) )
				return;

			switch ( id )
			{
				case Mesh: ReadMesh(); break;
				case MaterialList: ReadMaterialList(); break;
				case KeyframeAnimation: ReadKeyframeAnimation( version ); break;
				case SkinChunk when version == 1: ReadSkin(); break;
				default:
					// chunk size includes the 13-byte header (NODE is the lone liar; never skipped here)
					_r.Position = start + (int)size;
					if ( !_r.Eof && _r.Peek != ChunkTag ) return;
					break;
			}
		}
	}

	void BakeNodeTransforms()
	{
		foreach ( var (node, start, count) in _meshRanges )
		{
			var world = WorldOf( node );
			for ( var p = start; p < start + count; p++ )
			{
				var prim = Primitives[p];
				for ( var i = 0; i < prim.Positions.Length; i++ )
					prim.Positions[i] = world.Point( prim.Positions[i] );

				if ( prim.Normals is null )
					continue;

				for ( var i = 0; i < prim.Normals.Length; i++ )
					prim.Normals[i] = world.Direction( prim.Normals[i] ).Normal;
			}
		}
	}

	public MeshNode FindNode( string name )
		=> !string.IsNullOrEmpty( name ) && _nodesByName.TryGetValue( name, out var node ) ? node : null;

	public Mat43 WorldOf( MeshNode node ) => WorldOf( node, 0 );

	Mat43 WorldOf( MeshNode node, int depth )
	{
		if ( !node.HasParent || depth > 32 || string.IsNullOrEmpty( node.Parent ) || !_nodesByName.TryGetValue( node.Parent, out var parent ) )
			return node.Local;

		return node.Local.Then( WorldOf( parent, depth + 1 ) );
	}

	bool TryReadHeader( out uint id, out uint version, out uint size, out int payloadStart )
	{
		id = 0; version = 0; size = 0; payloadStart = 0;
		if ( _r.Position + 13 > _r.Length ) return false;
		if ( _r.RawByte() != ChunkTag ) { _r.Position--; return false; }
		id = _r.RawUInt32();
		version = _r.RawUInt32();
		size = _r.RawUInt32();
		payloadStart = _r.Position;
		return true;
	}

	void ReadMesh()
	{
		Vector3[] verts = null, normals = null;
		int[] vertsPerPrim = null;
		ushort[] localIndices = null;
		int[] trisPerPrim = null;
		string[] materialNames = null;
		Vector2[] uvs = null;
		MeshNode node = null;
		var primStart = Primitives.Count;
		var done = false;

		while ( !_r.Eof && !done )
		{
			var save = _r.Position;
			if ( !TryReadHeader( out var id, out var version, out var size, out _ ) )
				break;

			switch ( id )
			{
				case Node:
					node = ReadNode( version );
					break;
				case Geometry:
					ReadGeometry( version, out verts, out normals, out vertsPerPrim );
					break;
				case Polygons:
					ReadPolygons( version, out localIndices, out trisPerPrim );
					break;
				case PolygonMaterial:
					ReadPolygonMaterial( version, out materialNames );
					break;
				case UvMapping:
					ReadUvMapping( version, out var layerUvs, out var layer );
					if ( layer == 0 ) uvs = layerUvs;
					break;
				case Smoothing:
				case ReferenceToData:
					_r.Position = save + (int)size;
					break;
				default:
					_r.Position = save;
					done = true;
					break;
			}
		}

		BuildPrimitives( verts, normals, uvs, vertsPerPrim, localIndices, trisPerPrim, materialNames );

		if ( node is null )
			return;

		_nodesByName[node.Name] = node;
		Nodes.Add( node );

		if ( Primitives.Count > primStart )
		{
			_meshRanges.Add( (node, primStart, Primitives.Count - primStart) );
			for ( var p = primStart; p < Primitives.Count; p++ )
				Primitives[p].NodeName = node.Name;
		}
	}

	MeshNode ReadNode( uint version )
	{
		var node = new MeshNode { Name = _r.String(), Parent = _r.String() };
		node.Local = _r.Matrix4x3();
		node.HasParent = _r.Bool();
		if ( version > 0 ) _r.String(); // userDefinedString
		return node;
	}

	// v1: eight arrays, each prefixed by 1 raw byte + typed count
	void ReadSkin()
	{
		string[] StrArray()
		{
			_r.Skip( 1 );
			var n = _r.Int();
			var a = new string[n];
			for ( var i = 0; i < n; i++ ) a[i] = _r.String();
			return a;
		}

		int[] IntArray()
		{
			_r.Skip( 1 );
			var n = _r.Int();
			var a = new int[n];
			for ( var i = 0; i < n; i++ ) a[i] = _r.Int();
			return a;
		}

		float[] FloatArray()
		{
			_r.Skip( 1 );
			var n = _r.Int();
			var a = new float[n];
			for ( var i = 0; i < n; i++ ) a[i] = _r.Float();
			return a;
		}

		StrArray(); // skinObjectNames
		var skin = new SkinData { BoneNames = StrArray() };
		skin.Offsets = IntArray();
		skin.Counts = IntArray();
		skin.BoneIndices = IntArray();
		skin.Weights = FloatArray();
		IntArray(); // vertexNumPerPrimitive
		IntArray(); // vertexStartIndexPerPrimitive

		Skin = skin;
	}

	void ReadKeyframeAnimation( uint version )
	{
		if ( !TryReadHeader( out var id, out _, out _, out _ ) || id != AnimationChunk )
			return;

		var anim = new NodeAnim { NodeName = _r.String(), Fps = _r.Int() };
		anim.Looping = _r.Bool();

		// keys are local to THIS parent, which can differ from the skeleton hierarchy
		anim.ParentName = _r.String();
		_r.Bool(); // useLoopInterpolation

		var totalFrames = _r.Int();
		if ( version < 5 ) totalFrames++;
		anim.TotalFrames = totalFrames;

		var numKeys = _r.Int();
		for ( var i = 0; i < numKeys; i++ )
		{
			var frame = _r.Int();
			anim.Keys.Add( (frame, _r.Matrix4x3()) );
		}

		if ( version >= 1 )
		{
			var numVis = _r.Int();
			for ( var i = 0; i < numVis; i++ ) { _r.Int(); _r.Float(); }
		}

		if ( version >= 2 ) _r.Int(); // loopToFrame
		if ( version >= 3 ) _r.Int(); // interpolationMethod
		if ( version >= 4 ) _r.Bool(); // maintainMatrixScaling

		Animations.Add( anim );
	}

	void ReadGeometry( uint version, out Vector3[] verts, out Vector3[] normals, out int[] vertsPerPrim )
	{
		var numVertices = _r.Int();
		verts = new Vector3[numVertices];
		normals = null;

		if ( version == 0 )
		{
			for ( var i = 0; i < numVertices; i++ ) verts[i] = _r.Vector3();
			vertsPerPrim = [numVertices];
			return;
		}

		var posFloats = _r.RawFloats( numVertices * 3 );
		for ( var i = 0; i < numVertices; i++ )
			verts[i] = new Vector3( posFloats[i * 3], posFloats[i * 3 + 1], posFloats[i * 3 + 2] );

		var normFloats = _r.RawFloats( numVertices * 3 );
		normals = new Vector3[numVertices];
		for ( var i = 0; i < numVertices; i++ )
			normals[i] = new Vector3( normFloats[i * 3], normFloats[i * 3 + 1], normFloats[i * 3 + 2] );

		var numPrimitives = _r.Int();
		vertsPerPrim = new int[numPrimitives];
		for ( var i = 0; i < numPrimitives; i++ ) vertsPerPrim[i] = _r.Int();
	}

	void ReadPolygons( uint version, out ushort[] indices, out int[] trisPerPrim )
	{
		if ( version == 0 )
		{
			indices = [];
			trisPerPrim = [];
			throw new Exception( "KF2 mesh v1 (MP1) not supported" );
		}

		var numIndices = _r.Int();
		indices = _r.RawUInt16s( numIndices );
		var numPrimitives = _r.Int();
		trisPerPrim = new int[numPrimitives];
		for ( var i = 0; i < numPrimitives; i++ ) trisPerPrim[i] = _r.Int();
	}

	void ReadPolygonMaterial( uint version, out string[] names )
	{
		var numMaterials = _r.Int();
		names = new string[numMaterials];
		for ( var i = 0; i < numMaterials; i++ ) names[i] = _r.String();

		if ( version == 0 )
		{
			var numPolygons = _r.Int();
			for ( var i = 0; i < numPolygons; i++ ) _r.Int();
		}
	}

	void ReadUvMapping( uint version, out Vector2[] uvs, out int layer )
	{
		layer = _r.Int();

		if ( version == 0 )
		{
			var numPolygons = _r.Int();
			for ( var i = 0; i < numPolygons; i++ )
			{
				TryReadHeader( out _, out _, out _, out _ );
				var numVerts = _r.Int();
				for ( var j = 0; j < numVerts; j++ ) _r.Int();
			}
			_r.SkipTyped();
			_r.SkipTyped();
		}

		var numCoordinates = _r.Int();
		uvs = new Vector2[numCoordinates];

		if ( version == 0 )
		{
			for ( var i = 0; i < numCoordinates; i++ )
			{
				var v = _r.Vector3();
				uvs[i] = new Vector2( v.x, v.y );
			}
			return;
		}

		var floats = _r.RawFloats( numCoordinates * 3 );
		for ( var i = 0; i < numCoordinates; i++ )
			uvs[i] = new Vector2( floats[i * 3], floats[i * 3 + 1] );

		var numPrimitives = _r.Int();
		for ( var i = 0; i < numPrimitives; i++ ) _r.Int();
	}

	void BuildPrimitives( Vector3[] verts, Vector3[] normals, Vector2[] uvs, int[] vertsPerPrim, ushort[] localIndices, int[] trisPerPrim, string[] materialNames )
	{
		if ( verts is null || localIndices is null || vertsPerPrim is null || trisPerPrim is null )
			return;

		var vertexStart = new int[vertsPerPrim.Length + 1];
		for ( var p = 0; p < vertsPerPrim.Length; p++ )
			vertexStart[p + 1] = vertexStart[p] + vertsPerPrim[p];

		var indexCursor = 0;
		for ( var p = 0; p < trisPerPrim.Length; p++ )
		{
			var count = vertsPerPrim[p];
			var prim = new Primitive
			{
				Positions = new Vector3[count],
				Normals = normals is null ? null : new Vector3[count],
				Uvs = uvs is null ? null : new Vector2[count],
				MaterialName = materialNames is not null && p < materialNames.Length ? materialNames[p] : null,
				VertexStart = vertexStart[p],
			};

			Array.Copy( verts, vertexStart[p], prim.Positions, 0, count );
			if ( prim.Normals is not null ) Array.Copy( normals, vertexStart[p], prim.Normals, 0, count );
			if ( prim.Uvs is not null && vertexStart[p] + count <= uvs.Length ) Array.Copy( uvs, vertexStart[p], prim.Uvs, 0, count );

			var tris = trisPerPrim[p];
			prim.Indices = new int[tris * 3];
			for ( var i = 0; i < tris * 3; i++ )
				prim.Indices[i] = localIndices[indexCursor++];

			Primitives.Add( prim );
		}
	}

	void ReadMaterialList()
	{
		TextureDirs = _r.String();
		var numMaterials = _r.Int();
		for ( var i = 0; i < numMaterials; i++ )
		{
			if ( !TryReadHeader( out var id, out var version, out _, out _ ) || id != Material )
				return;

			ReadMaterial( version );
		}
	}

	void ReadMaterial( uint version )
	{
		var name = _r.String();
		var twoSided = _r.Bool();
		for ( var i = 0; i < 4; i++ ) _r.Bool();
		_r.Int(); _r.Int(); _r.Int();
		for ( var i = 0; i < 12; i++ ) _r.Float();
		_r.Float(); _r.Float();
		_r.Int(); _r.Int();
		_r.Float();

		var def = new MaterialDef { Name = name, TwoSided = twoSided };

		var hasDiffuse = _r.Bool();
		if ( hasDiffuse )
		{
			if ( TryReadHeader( out _, out var texVer, out _, out _ ) )
				def.DiffuseTexture = ReadTexture( texVer );
		}

		var hasReflection = _r.Bool();
		if ( hasReflection && TryReadHeader( out _, out var rv, out _, out _ ) ) ReadTexture( rv );

		var hasBump = _r.Bool();
		if ( hasBump && TryReadHeader( out _, out var bv, out _, out _ ) ) ReadTexture( bv );

		var hasOpacity = _r.Bool();
		if ( hasOpacity && TryReadHeader( out _, out var ov, out _, out _ ) ) ReadTexture( ov );

		if ( version >= 1 )
		{
			var hasMask = _r.Bool();
			if ( hasMask && TryReadHeader( out _, out var mv, out _, out _ ) ) ReadTexture( mv );
			_r.Int();
			_r.Bool();
		}

		if ( version >= 2 )
		{
			_r.Bool(); _r.Bool(); _r.Int();
		}

		Materials[name] = def;
	}

	string ReadTexture( uint version )
	{
		_r.String(); // name
		_r.Int(); // mipMapsNum
		_r.Int(); // filteringType
		var numTextures = _r.Int();

		string first = null;
		for ( var i = 0; i < numTextures; i++ )
		{
			var fn = _r.String();
			first ??= fn;
		}

		if ( numTextures > 1 )
		{
			_r.Bool(); _r.Bool();
			_r.Int(); _r.Int(); _r.Int();
		}

		return first;
	}
}
