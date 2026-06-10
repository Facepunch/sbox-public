using Sandbox;
using System;
using System.Collections.Generic;

namespace RasLib;

// Reads an LDB2 level: embedded textures, materials, and per-room static meshes.
// Entity/FSM/trigger/dynamic-mesh sections are not parsed (rooms hold the static world).
public class Ldb2File
{
	const uint Magic = 0x3242444C;
	const int FormatVersion = 34;

	public class TextureData
	{
		public int FileType;
		public byte[] Data;
	}

	public class MaterialDef
	{
		public int FrameStart;
		public int VisibleFrame;
		public int LightmapId;
		public int BlendMode;
		public int SortPriority;
		public bool DualSided;
		public bool WritesZBuffer;

		// Maya importer: these blend modes read transparency from the diffuse alpha channel
		public bool UsesAlpha => BlendMode is 1 or 2 or 4 or 7 or 8 or 10 or 11;
	}

	public class SubMesh
	{
		public int MaterialId;
		public Vector3[] Positions;
		public Vector3[] Normals;
		public Vector2[] Uvs;
		public Vector2[] LightmapUvs;
		public ushort[] Indices;
	}

	public class Room
	{
		public string Name;
		public Mat43 Transform;
		public List<SubMesh> Meshes = [];
		public List<VolumeLight> VolumeLights = [];
	}

	public class VolumeLight
	{
		public Vector3 Min, Max;
		public float R, G, B;
	}

	public class DynamicLight
	{
		public Mat43 Transform;
		public int RoomId;
		public float R, G, B, A, Falloff;
	}

	public class Fsm
	{
		public Mat43 Transform;
		public int RoomId;
	}

	public class DynamicMesh
	{
		public int FsmId;
		public Vector3 AabbPivot;
		public List<SubMesh> Meshes;
	}

	public List<TextureData> Diffuse { get; } = [];
	public List<TextureData> Lightmaps { get; } = [];
	public List<MaterialDef> Materials { get; } = [];
	public List<Room> Rooms { get; } = [];
	public List<DynamicLight> Lights { get; } = [];
	public List<Mat43> Flares { get; } = [];
	public List<Fsm> Fsms { get; } = [];
	public List<DynamicMesh> DynamicMeshes { get; } = [];

	readonly MaxReader _r;

	Ldb2File( byte[] data )
	{
		_r = new MaxReader( data );
	}

	public static bool IsLdb2( byte[] data )
		=> data.Length >= 4 && BitConverter.ToUInt32( data, 0 ) == Magic;

	public static Ldb2File Parse( byte[] data )
	{
		var ldb = new Ldb2File( data );
		ldb.Read();
		return ldb;
	}

	void Read()
	{
		if ( _r.RawUInt32() != Magic )
			throw new Exception( "not an LDB2 file" );

		var version = _r.Int();
		if ( version != FormatVersion )
			throw new Exception( $"unsupported LDB2 version {version}" );

		var stringTableSize = _r.Int();
		_r.Skip( stringTableSize );
		_r.Float(); // physicalWorldSize

		ReadTextureGroup( Diffuse );
		ReadLightmaps();
		ReadTextureGroup( null ); // detail
		ReadTextureGroup( null ); // reflection
		ReadTextureGroup( null ); // gloss

		ReadMaterials();
		ReadRooms();

		try
		{
			ReadTrailingSections();
		}
		catch ( Exception )
		{
			// rooms are the critical content; entities/dynamic meshes degrade gracefully
		}
	}

	// validated section order: lights, flares, levelItems, portals, jumpPoints, wayPoints,
	// characters, FSMs, triggers, dynamicMeshes, mirrors
	void ReadTrailingSections()
	{
		var lights = _r.Int();
		for ( var i = 0; i < lights; i++ )
		{
			var light = new DynamicLight { Transform = _r.Matrix4x3(), RoomId = _r.Int() };
			light.R = _r.Float(); light.G = _r.Float(); light.B = _r.Float(); light.A = _r.Float();
			light.Falloff = _r.Float();
			Lights.Add( light );
		}

		var flares = _r.Int();
		for ( var i = 0; i < flares; i++ )
		{
			SkipName();
			Flares.Add( _r.Matrix4x3() );
			_r.Int(); // roomId
		}

		var items = _r.Int();
		for ( var i = 0; i < items; i++ ) { SkipName(); SkipName(); _r.Matrix4x3(); _r.Int(); }

		var portals = _r.Int();
		for ( var i = 0; i < portals; i++ )
		{
			SkipName(); _r.Vector3(); _r.SkipTyped(); _r.SkipTyped();
			var points = _r.Int();
			for ( var p = 0; p < points; p++ ) _r.Vector3();
		}

		var jumps = _r.Int();
		for ( var i = 0; i < jumps; i++ ) { SkipName(); _r.Matrix4x3(); _r.Int(); }

		var ways = _r.Int();
		for ( var i = 0; i < ways; i++ ) { SkipName(); _r.Matrix4x3(); _r.Int(); _r.SkipTyped(); }

		_r.Skip( 1 );
		var groups = _r.Int();
		for ( var i = 0; i < groups; i++ ) { SkipName(); _r.Int(); }
		var characters = _r.Int();
		for ( var i = 0; i < characters; i++ ) { SkipName(); SkipName(); _r.Matrix4x3(); _r.Int(); _r.Int(); _r.SkipTyped(); }

		var fsms = _r.Int();
		for ( var i = 0; i < fsms; i++ )
		{
			SkipName();
			var fsm = new Fsm { Transform = _r.Matrix4x3() };
			_r.Int(); // parentId
			_r.Matrix4x3(); // localTransform
			fsm.RoomId = _r.Int();
			Fsms.Add( fsm );

			SkipName(); // defaultState
			_r.Skip( 1 );
			var custom = _r.Int();
			for ( var c = 0; c < custom; c++ ) _r.SkipTyped();
			_r.Int(); _r.Int(); _r.Int(); _r.Int();
			var timers = _r.Int();
			for ( var t = 0; t < timers; t++ ) { SkipName(); _r.SkipTyped(); _r.SkipTyped(); _r.SkipTyped(); _r.SkipTyped(); }
		}

		var triggers = _r.Int();
		for ( var i = 0; i < triggers; i++ )
		{
			_r.Int(); _r.Float();
			for ( var f = 0; f < 6; f++ ) Flag();
			SkipName(); // activation state (not in the python spec)
			if ( Flag() == 1 )
			{
				var parent = _r.Int();
				if ( parent == -1 ) SkipCollisions();
			}
		}

		var prefabMeshes = new Dictionary<int, List<SubMesh>>();
		var dynCount = _r.Int();
		for ( var i = 0; i < dynCount; i++ )
		{
			var dyn = new DynamicMesh { FsmId = _r.Int() };
			var useLightMaps = Flag();
			for ( var f = 0; f < 7; f++ ) Flag();
			_r.Int(); // physicalMaterial
			var prefabId = _r.Int();
			var shareCollision = Flag();
			_r.Vector3(); _r.Vector3();
			dyn.AabbPivot = _r.Vector3();

			bool readGeo, readColl;
			if ( prefabId == -1 ) { readGeo = true; readColl = true; }
			else if ( !prefabMeshes.ContainsKey( prefabId ) ) { readGeo = true; readColl = true; }
			else if ( useLightMaps != 0 ) { readGeo = true; readColl = shareCollision == 0; }
			else { readGeo = false; readColl = false; }

			if ( readGeo )
			{
				var meshCount = _r.Int();
				dyn.Meshes = new List<SubMesh>( meshCount );
				for ( var m = 0; m < meshCount; m++ ) dyn.Meshes.Add( ReadSubMesh() );
				if ( prefabId != -1 && !prefabMeshes.ContainsKey( prefabId ) ) prefabMeshes[prefabId] = dyn.Meshes;
			}
			else
			{
				dyn.Meshes = prefabMeshes[prefabId];
			}

			if ( readColl ) SkipCollisions();

			var anims = _r.Int();
			for ( var a = 0; a < anims; a++ )
			{
				SkipName(); _r.SkipTyped(); _r.Matrix4x3(); _r.Matrix4x3();
				for ( var ch = 0; ch < 2; ch++ )
				{
					_r.SkipTyped(); _r.SkipTyped(); _r.SkipTyped();
					_r.SkipTyped(); // sampleRate
					var points = _r.Int();
					_r.Skip( points * 4 );
					_r.Skip( points * 4 );
				}
				_r.SkipTyped(); _r.SkipTyped(); _r.SkipTyped();
			}

			DynamicMeshes.Add( dyn );
		}
		// mirrors: last section, not consumed
	}

	int Flag() => _r.Peek == 0x0E ? (_r.Bool() ? 1 : 0) : _r.Int();

	void SkipName()
	{
		if ( _r.Peek == 0x0D ) _r.String();
		else _r.Int();
	}

	void ReadTextureGroup( List<TextureData> into )
	{
		var count = _r.Int();
		for ( var i = 0; i < count; i++ )
		{
			var fileType = _r.Int();
			var size = _r.Int();
			_r.Int(); // filePathOffset
			var data = _r.RawBytes( size );
			into?.Add( new TextureData { FileType = fileType, Data = data } );
		}
	}

	void ReadLightmaps()
	{
		_r.Bool(); // isDds
		var count = _r.Int();
		for ( var i = 0; i < count; i++ )
		{
			var size = _r.Int();
			var data = _r.RawBytes( size );
			Lightmaps.Add( new TextureData { FileType = 0, Data = data } );
		}
	}

	void ReadMaterials()
	{
		var count = _r.Int();
		for ( var i = 0; i < count; i++ )
		{
			var blendMode = _r.Int();
			var frameStart = _r.Int();
			_r.Int(); // frameEnd
			var lightmapId = _r.Int();
			_r.Int(); // detailTexId
			_r.Int(); // reflectionTexId
			_r.Int(); // glossTexId
			_r.Int(); // alphaCompareReferenceValue
			var sortPriority = _r.Int();
			_r.Int(); // detailOffset
			var dualSided = _r.Bool();
			var writesZBuffer = _r.Bool();
			_r.Int(); // framerate
			var visibleFrame = _r.Int();

			Materials.Add( new MaterialDef
			{
				FrameStart = frameStart,
				VisibleFrame = visibleFrame,
				LightmapId = lightmapId,
				BlendMode = blendMode,
				SortPriority = sortPriority,
				DualSided = dualSided,
				WritesZBuffer = writesZBuffer,
			} );
		}
	}

	void ReadRooms()
	{
		var count = _r.Int();
		for ( var i = 0; i < count; i++ )
		{
			var room = new Room { Name = _r.String() };
			room.Transform = _r.Matrix4x3();
			_r.SkipTyped(); // unknown
			_r.Vector3(); _r.Vector3(); _r.Vector3(); // aabb min/max/pivot

			var meshCount = _r.Int();
			for ( var m = 0; m < meshCount; m++ )
				room.Meshes.Add( ReadSubMesh() );

			SkipCollisions();
			ReadVolumeLights( room );

			Rooms.Add( room );
		}
	}

	SubMesh ReadSubMesh()
	{
		var mesh = new SubMesh { MaterialId = _r.Int() };
		var hasLightmapUvs = _r.Bool();
		var hasDetailUvs = _r.Bool();
		var polygonCount = _r.Int();
		var vertexCount = _r.Int();

		var pos = _r.RawFloats( vertexCount * 3 );
		var norm = _r.RawFloats( vertexCount * 3 );
		var uv = _r.RawFloats( vertexCount * 2 );

		mesh.Positions = new Vector3[vertexCount];
		mesh.Normals = new Vector3[vertexCount];
		mesh.Uvs = new Vector2[vertexCount];
		for ( var i = 0; i < vertexCount; i++ )
		{
			mesh.Positions[i] = new Vector3( pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2] );
			mesh.Normals[i] = new Vector3( norm[i * 3], norm[i * 3 + 1], norm[i * 3 + 2] );
			mesh.Uvs[i] = new Vector2( uv[i * 2], uv[i * 2 + 1] );
		}

		if ( hasLightmapUvs )
		{
			var lm = _r.RawFloats( vertexCount * 2 );
			mesh.LightmapUvs = new Vector2[vertexCount];
			for ( var i = 0; i < vertexCount; i++ )
				mesh.LightmapUvs[i] = new Vector2( lm[i * 2], lm[i * 2 + 1] );
		}

		if ( hasDetailUvs )
			_r.Skip( vertexCount * 2 * 4 );

		mesh.Indices = _r.RawUInt16s( polygonCount * 3 );
		return mesh;
	}

	void SkipCollisions()
	{
		var count = _r.Int();
		for ( var i = 0; i < count; i++ )
		{
			var vertexCount = _r.Int();
			var polygonCount = _r.Int();
			_r.Skip( vertexCount * 3 * 4 ); // positions
			_r.Skip( polygonCount * 3 * 2 ); // indices
			_r.Skip( polygonCount ); // material indices
			_r.Bool(); // isConvex
			_r.Int(); // collisionMask
			_r.Skip( 3 * 4 ); // origin
			_r.Skip( 4 ); // reserved
			var moppSize = (int)_r.RawUInt32();
			_r.Skip( moppSize );
		}
	}

	// 3D ambient grid per room, reduced to one average color per volume.
	// Cell = 12 bytes: RGB8 ambient color, 5 unknown bytes, float32 intensity (0..~0.86).
	// NOT 3 floats as the python spec claims - that decodes as garbage/NaN.
	void ReadVolumeLights( Room room )
	{
		var count = _r.Int();
		for ( var i = 0; i < count; i++ )
		{
			var w = _r.Int();
			var h = _r.Int();
			var d = _r.Int();
			var min = _r.Vector3();
			var max = _r.Vector3();

			var cells = w * h * d;
			var raw = _r.RawBytes( cells * 12 );

			// average only the lit cells - cells buried in geometry are near-black and
			// drag the mean down (characters stand in the open, not inside walls)
			float meanIntensity = 0;
			for ( var c = 0; c < cells; c++ )
				meanIntensity += BitConverter.ToSingle( raw, c * 12 + 8 );
			if ( cells > 0 ) meanIntensity /= cells;

			float r = 0, g = 0, b = 0;
			var lit = 0;
			for ( var c = 0; c < cells; c++ )
			{
				var intensity = BitConverter.ToSingle( raw, c * 12 + 8 );
				if ( intensity < meanIntensity )
					continue;

				r += raw[c * 12] / 255f * intensity;
				g += raw[c * 12 + 1] / 255f * intensity;
				b += raw[c * 12 + 2] / 255f * intensity;
				lit++;
			}

			if ( lit > 0 ) { r /= lit; g /= lit; b /= lit; }

			room.VolumeLights.Add( new VolumeLight { Min = min, Max = max, R = r, G = g, B = b } );
		}
	}
}
