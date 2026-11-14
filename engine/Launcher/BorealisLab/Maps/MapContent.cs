using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Materials;
using Borealis.Source2.Models;
using Borealis.Source2.Resources;
using Borealis.Source2.World;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GpuDevice = Borealis.Gpu.Device;
using Matrix4x4 = System.Numerics.Matrix4x4;
using V2 = System.Numerics.Vector2;
using V3 = System.Numerics.Vector3;
using V4 = System.Numerics.Vector4;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// One vertex as the map draws it: position, normal (signed bytes) and the first uv. 24 bytes.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct MapVertex
{
	public V3 Position;
	public uint Normal;
	public V2 Uv;
}

/// <summary>A draw call's range of the shared index and vertex buffers, and its material.</summary>
internal readonly record struct MapDraw( int FirstIndex, int IndexCount, int VertexOffset, int Material, V4 Tint );

/// <summary>How a material draws.</summary>
internal enum SurfaceKind
{
	Opaque,
	AlphaTest,
	Translucent,
}

/// <summary>A material reduced to what the simple shader uses: a colour texture or a flat colour, and how it blends.</summary>
internal sealed class MapMaterial
{
	public string Path;
	public int Index;
	public SurfaceKind Kind;
	public bool TwoSided;
	public V4 Color = V4.One;
	public float AlphaReference = 0.5f;
	public string ColorTexture;
	public TextureHandle Texture;
	public bool Textured;
	public string Shader;
	public bool Unreadable;

	/// <summary>The material's persistent set (variant ii), made by the renderer.</summary>
	public BindingSetHandle Set;
}

/// <summary>A model's draws per LOD, in the shared buffers.</summary>
internal sealed class MapModel
{
	public string Path;
	public ModelFile File;
	public V3 Mins, Maxs;
	public int LodCount;
	public float[] SwitchDistances;

	/// <summary>Every draw call, numbered across meshes in order (what aggregates index).</summary>
	public MapDraw[] Draws;

	/// <summary>Each draw's mesh, LOD mask and mesh groups.</summary>
	public (int Mesh, byte LodMask, ulong MeshGroups)[] DrawInfo;

	public readonly Dictionary<(ulong Mask, int Skin), MapDraw[][]> ByLod = new();

	/// <summary>The draws at each LOD for a mesh group mask and material group.</summary>
	public MapDraw[][] Lods( ulong mask, int skin, Func<string, int> material )
	{
		lock ( ByLod )
		{
			if ( ByLod.TryGetValue( (mask, skin), out var found ) ) return found;

			var lods = new MapDraw[Math.Max( LodCount, 1 )][];
			for ( int lod = 0; lod < lods.Length; lod++ )
			{
				var list = new List<MapDraw>();
				for ( int d = 0; d < Draws.Length; d++ )
				{
					var (mesh, lodMask, groups) = DrawInfo[d];
					if ( (lodMask & (1 << lod)) == 0 || (groups & mask) == 0 ) continue;
					var draw = Draws[d];
					if ( skin > 0 ) draw = draw with { Material = material( File.MaterialFor( File.Meshes[mesh].DrawCalls[d - FirstDraw( mesh )].Material, skin ) ) };
					list.Add( draw );
				}

				lods[lod] = list.ToArray();
			}

			ByLod[(mask, skin)] = lods;
			return lods;
		}
	}

	int FirstDraw( int mesh )
	{
		var first = 0;
		for ( int m = 0; m < mesh; m++ ) first += File.Meshes[m].DrawCalls.Count;
		return first;
	}
}

/// <summary>
/// Something culled on its own: a placed model (whose LOD is chosen per view), or one draw of an aggregate.
/// </summary>
internal sealed class MapObject
{
	public MapModel Model;
	public Matrix4x4 World;

	/// <summary>Object to world as three rows for column vectors, what the shader reads.</summary>
	public V4 Row0, Row1, Row2;
	public V3 Mins, Maxs, Center, Extents;
	public float Scale = 1;
	public V4 Tint = V4.One;
	public float FadeEnd;
	public int LodOverride = -1;

	/// <summary>Its draws by LOD; an aggregate draw has one LOD of one draw.</summary>
	public MapDraw[][] Lods;
	public bool Aggregate;
}

/// <summary>
/// A compiled map read with Borealis.Source2 and uploaded to a Borealis.Gpu device: its world node's scene objects and
/// aggregates, their models' geometry in one vertex and one index buffer, and their materials' colour textures.
/// Entities (props placed as entities, lights), the 3D skybox, lightmaps and probes aren't read.
/// </summary>
internal sealed class MapContent : IDisposable
{
	public GpuDevice Gpu { get; }
	public ContentFiles Content { get; }
	public WorldFile World { get; private set; }
	public List<MapObject> Objects { get; } = new();
	public List<MapMaterial> Materials { get; } = new();
	public Dictionary<string, MapModel> Models { get; } = new( StringComparer.OrdinalIgnoreCase );
	public BufferHandle Vertices { get; private set; }
	public BufferHandle Indices { get; private set; }
	public TextureHandle White { get; private set; }
	public V3 Mins { get; private set; }
	public V3 Maxs { get; private set; }
	public long VertexCount { get; private set; }
	public long IndexCount { get; private set; }
	public int TextureCount { get; private set; }
	public long TextureBytes { get; private set; }
	public List<string> Problems { get; } = new();
	public List<(string Name, double Ms)> Phases { get; } = new();

	readonly Dictionary<string, int> materialIndex = new( StringComparer.OrdinalIgnoreCase );
	readonly List<TextureHandle> textures = new();
	readonly VpkFile package;

	/// <param name="folder">The map package's folder (a workshop item): loose content and <c>maps/*.vpk</c>.</param>
	/// <param name="core">Core content, for materials and models the package doesn't have.</param>
	/// <param name="maxTextureSize">Mips above this size are left out, to load faster.</param>
	public MapContent( GpuDevice gpu, string folder, ContentFiles core, int maxTextureSize )
	{
		Gpu = gpu;
		var timer = Stopwatch.StartNew();
		void Phase( string name )
		{
			Phases.Add( (name, timer.Elapsed.TotalMilliseconds) );
			timer.Restart();
		}

		var vpk = Directory.GetFiles( Path.Combine( folder, "maps" ), "*.vpk" )
			.Where( x => !Path.GetFileNameWithoutExtension( x ).EndsWith( "_sky", StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( x => x.Length )
			.FirstOrDefault() ?? throw new FileNotFoundException( $"No map VPK under {folder}/maps" );

		package = VpkFile.Open( vpk );
		Content = new ContentFiles( folder );
		foreach ( var root in core.Roots ) Content.Add( root );
		Content.AddPackage( package );

		var name = Path.GetFileNameWithoutExtension( vpk );
		World = WorldFile.Load( Content, $"maps/{name}/world.vwrld" );
		Phase( "World" );

		// Models, read in parallel
		var paths = World.Nodes.SelectMany( n => n.SceneObjects.Select( o => o.Model ).Concat( n.Aggregates.Select( a => a.Model ) ) )
			.Where( x => !string.IsNullOrEmpty( x ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToArray();

		var files = new ConcurrentDictionary<string, ModelFile>( StringComparer.OrdinalIgnoreCase );
		Parallel.ForEach( paths, path =>
		{
			try
			{
				files[path] = ModelFile.Load( Content, path );
			}
			catch ( Exception e )
			{
				lock ( Problems ) Problems.Add( $"model {path}: {e.Message}" );
			}
		} );
		Phase( $"Models ({files.Count})" );

		// Materials, then their colour textures, read in parallel and uploaded in order
		var materialPaths = files.Values.SelectMany( f => f.Meshes.SelectMany( m => m.DrawCalls.Select( d => d.Material ) )
			.Concat( f.MaterialGroups.SelectMany( g => g.Materials ) ) )
			.Where( x => !string.IsNullOrEmpty( x ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.OrderBy( x => x, StringComparer.OrdinalIgnoreCase )
			.ToArray();

		foreach ( var path in materialPaths ) MaterialIndex( path );
		Parallel.ForEach( Materials, ReadMaterial );
		Phase( $"Materials ({Materials.Count})" );

		White = gpu.CreateTexture( TextureInfo.Texture2D( 1, 1, Format.RGBA8Unorm, 1, "White" ) );
		gpu.WriteTexture( White, [255, 255, 255, 255] );
		textures.Add( White );
		LoadTextures( maxTextureSize );
		Phase( $"Textures ({TextureCount}, {TextureBytes / (1024.0 * 1024.0):0} MiB)" );

		// Geometry: every model's meshes into one vertex and one index buffer
		var vertices = new List<MapVertex[]>();
		var indices = new List<uint[]>();
		long vertexBase = 0, indexBase = 0;
		var converted = new ConcurrentDictionary<string, (MapVertex[][] Vertices, uint[][] Indices)>( StringComparer.OrdinalIgnoreCase );
		Parallel.ForEach( files, pair => converted[pair.Key] = Convert( pair.Value ) );

		foreach ( var path in paths )
		{
			if ( !files.TryGetValue( path, out var file ) ) continue;
			var (meshVertices, meshIndices) = converted[path];

			// Where each mesh's buffers land
			var vertexStarts = new long[meshVertices.Length];
			var indexStarts = new long[meshIndices.Length];
			for ( int i = 0; i < meshVertices.Length; i++ )
			{
				vertexStarts[i] = vertexBase;
				vertexBase += meshVertices[i].Length;
				vertices.Add( meshVertices[i] );
			}

			for ( int i = 0; i < meshIndices.Length; i++ )
			{
				indexStarts[i] = indexBase;
				indexBase += meshIndices[i].Length;
				indices.Add( meshIndices[i] );
			}

			Models[path] = MakeModel( path, file, vertexStarts, indexStarts );
		}

		VertexCount = vertexBase;
		IndexCount = indexBase;
		Vertices = Upload( vertices, BufferUsage.Vertex, "Map vertices" );
		Indices = Upload( indices, BufferUsage.Index, "Map indices" );
		Phase( $"Geometry ({VertexCount / 1e6:0.0}M vertices, {IndexCount / 3e6:0.0}M triangles)" );

		PlaceObjects();
		Phase( $"Objects ({Objects.Count})" );
	}

	int MaterialIndex( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) path = "materials/default.vmat";
		lock ( materialIndex )
		{
			if ( materialIndex.TryGetValue( path, out var index ) ) return index;
			index = Materials.Count;
			Materials.Add( new MapMaterial { Path = path, Index = index } );
			materialIndex[path] = index;
			return index;
		}
	}

	void ReadMaterial( MapMaterial material )
	{
		try
		{
			var data = Content.TryRead( material.Path );
			if ( data is null )
			{
				material.Unreadable = true;
				return;
			}

			var file = MaterialFile.Read( data );
			material.Shader = file.Shader;
			bool Feature( string name ) => file.Ints.TryGetValue( name, out var v ) && v != 0;

			material.Kind = Feature( "F_TRANSLUCENT" ) || Feature( "F_ADDITIVE_BLEND" ) ? SurfaceKind.Translucent
				: Feature( "F_ALPHA_TEST" ) ? SurfaceKind.AlphaTest
				: SurfaceKind.Opaque;
			material.TwoSided = Feature( "F_RENDER_BACKFACES" );
			if ( file.Shader?.Contains( "glass", StringComparison.OrdinalIgnoreCase ) == true ) material.Kind = SurfaceKind.Translucent;

			if ( file.Vectors.TryGetValue( "g_vColorTint", out var tint ) ) material.Color = new V4( tint[0], tint[1], tint[2], 1 );
			if ( file.Floats.TryGetValue( "g_flAlphaTestReference", out var reference ) ) material.AlphaReference = reference;
			if ( file.Floats.TryGetValue( "g_flOpacityScale", out var opacity ) && material.Kind == SurfaceKind.Translucent ) material.Color.W = opacity;

			// Water's colour comes from its shader, not a texture: a flat translucent blue-green instead
			if ( file.Shader?.Contains( "water", StringComparison.OrdinalIgnoreCase ) == true )
			{
				material.Kind = SurfaceKind.Translucent;
				material.Color = new V4( 0.08f, 0.14f, 0.15f, 0.85f );
				return;
			}

			material.ColorTexture = file.Textures.TryGetValue( "g_tColor", out var color ) ? color
				: file.Textures.TryGetValue( "g_tColor1", out var layer ) ? layer
				: file.Textures.FirstOrDefault( x => x.Key.StartsWith( "g_tColor", StringComparison.OrdinalIgnoreCase ) ).Value;
		}
		catch ( Exception )
		{
			material.Unreadable = true;
		}
	}

	void LoadTextures( int maxSize )
	{
		var wanted = Materials.Where( x => !string.IsNullOrEmpty( x.ColorTexture ) ).GroupBy( x => x.ColorTexture, StringComparer.OrdinalIgnoreCase ).ToArray();
		var read = new ConcurrentDictionary<string, TextureFile>( StringComparer.OrdinalIgnoreCase );
		Parallel.ForEach( wanted, group =>
		{
			try
			{
				if ( Content.TryRead( group.Key ) is { } data ) read[group.Key] = TextureFile.Read( data );
			}
			catch ( Exception e )
			{
				lock ( Problems ) Problems.Add( $"texture {group.Key}: {e.Message}" );
			}
		} );

		foreach ( var group in wanted )
		{
			if ( !read.TryGetValue( group.Key, out var file ) || file.Format == Format.Unknown || file.Dimension != TextureDimension.Texture2D ) continue;

			var skip = 0;
			while ( skip < file.Mips - 1 && Math.Max( file.Width >> skip, file.Height >> skip ) > maxSize ) skip++;
			var mips = file.Mips - skip;
			var texture = Gpu.CreateTexture( new TextureInfo
			{
				Width = Math.Max( file.Width >> skip, 1 ),
				Height = Math.Max( file.Height >> skip, 1 ),
				Depth = 1,
				Layers = 1,
				Mips = mips,
				Samples = 1,
				Dimension = TextureDimension.Texture2D,
				Format = file.Format,
				Usage = TextureUsage.Sampled | TextureUsage.CopyDestination,
				Name = group.Key,
			} );

			for ( int mip = 0; mip < mips; mip++ )
			{
				Gpu.WriteTexture( texture, file.MipData[mip + skip], mip );
				TextureBytes += file.MipData[mip + skip].Length;
			}

			textures.Add( texture );
			TextureCount++;
			foreach ( var material in group )
			{
				material.Texture = texture;
				material.Textured = true;
			}
		}

		foreach ( var material in Materials )
		{
			if ( !material.Textured ) material.Texture = White;
		}
	}

	/// <summary>Every mesh's vertex buffers in the map's format, and its index buffers as 32-bit.</summary>
	static (MapVertex[][] Vertices, uint[][] Indices) Convert( ModelFile file )
	{
		var vertices = new List<MapVertex[]>();
		var indices = new List<uint[]>();
		foreach ( var mesh in file.Meshes )
		{
			foreach ( var buffer in mesh.VertexBuffers ) vertices.Add( Convert( buffer ) );
			foreach ( var buffer in mesh.IndexBuffers )
			{
				var converted = new uint[buffer.Count];
				for ( int i = 0; i < converted.Length; i++ ) converted[i] = buffer[i];
				indices.Add( converted );
			}
		}

		return (vertices.ToArray(), indices.ToArray());
	}

	static MapVertex[] Convert( MeshVertexBuffer buffer )
	{
		var result = new MapVertex[buffer.Count];
		var position = buffer.Find( "POSITION" );
		var normal = buffer.Find( "NORMAL" );
		var uv = buffer.Find( "TEXCOORD" );
		if ( position is null ) return result;

		var packed = normal?.Format is ColorFormat.R10G10B10A2_UNORM;
		var legacy = normal?.Format is ColorFormat.R8G8B8A8_UNORM;
		for ( int v = 0; v < result.Length; v++ )
		{
			var p = buffer.Read( position.Value, v );
			V3 n = V3.UnitZ;
			if ( normal is { } nf )
			{
				if ( packed || legacy )
				{
					var bits = BitConverter.ToUInt32( buffer.Data, v * buffer.Stride + nf.Offset );
					n = packed ? TangentFrame.UnpackOctDiamond( bits ).Normal : TangentFrame.UnpackLegacy( bits ).Normal;
				}
				else
				{
					var r = buffer.Read( nf, v );
					n = new V3( r.X, r.Y, r.Z );
				}
			}

			var t = uv is { } uf ? buffer.Read( uf, v ) : default;
			result[v] = new MapVertex { Position = new V3( p.X, p.Y, p.Z ), Normal = PackNormal( n ), Uv = new V2( t.X, t.Y ) };
		}

		return result;
	}

	static uint PackNormal( V3 n )
	{
		n = n.LengthSquared() > 0 ? V3.Normalize( n ) : V3.UnitZ;
		static uint B( float x ) => (uint)(byte)(sbyte)Math.Clamp( (int)MathF.Round( x * 127 ), -127, 127 );
		return B( n.X ) | (B( n.Y ) << 8) | (B( n.Z ) << 16);
	}

	MapModel MakeModel( string path, ModelFile file, long[] vertexStarts, long[] indexStarts )
	{
		var draws = new List<MapDraw>();
		var info = new List<(int, byte, ulong)>();
		int vertexBuffer = 0, indexBuffer = 0;
		for ( int m = 0; m < file.Meshes.Count; m++ )
		{
			var mesh = file.Meshes[m];
			foreach ( var call in mesh.DrawCalls )
			{
				var vb = call.VertexBuffers.Count > 0 ? call.VertexBuffers[0] : (Buffer: 0, Offset: 0);
				var stride = mesh.VertexBuffers.Count > vb.Buffer ? mesh.VertexBuffers[vb.Buffer].Stride : 1;
				var vertexOffset = vertexStarts[vertexBuffer + vb.Buffer] + call.BaseVertex + vb.Offset / Math.Max( stride, 1 );
				var firstIndex = indexStarts[indexBuffer + call.IndexBuffer.Buffer] + call.StartIndex;
				var supported = call.Primitive == PrimitiveKind.Triangles;
				draws.Add( new MapDraw( (int)firstIndex, supported ? call.IndexCount : 0, (int)vertexOffset, MaterialIndex( call.Material ), new V4( call.Tint, 1 ) ) );
				info.Add( (m, mesh.LodMask, mesh.MeshGroupMask) );
			}

			vertexBuffer += mesh.VertexBuffers.Count;
			indexBuffer += mesh.IndexBuffers.Count;
		}

		return new MapModel
		{
			Path = path,
			File = file,
			Mins = file.Mins,
			Maxs = file.Maxs,
			LodCount = file.LodCount,
			SwitchDistances = file.LodSwitchDistances.ToArray(),
			Draws = draws.ToArray(),
			DrawInfo = info.ToArray(),
		};
	}

	/// <summary>The compiler leaves this pattern in world geometry's mesh group mask: it means none was set.</summary>
	const ulong UnsetMask = 0xD0D0D0D0D0D0D0D0;

	void PlaceObjects()
	{
		var mins = new V3( float.MaxValue );
		var maxs = new V3( float.MinValue );

		foreach ( var node in World.Nodes )
		{
			foreach ( var o in node.SceneObjects )
			{
				if ( !Models.TryGetValue( o.Model, out var model ) ) continue;

				var skin = 0;
				if ( !string.IsNullOrEmpty( o.Skin ) )
				{
					for ( int g = 0; g < model.File.MaterialGroups.Count; g++ )
					{
						if ( string.Equals( model.File.MaterialGroups[g].Name, o.Skin, StringComparison.OrdinalIgnoreCase ) ) skin = g;
					}
				}

				var mask = o.MeshGroupMask == UnsetMask ? model.File.DefaultMeshGroupMask : o.MeshGroupMask;
				var obj = new MapObject
				{
					Model = model,
					World = o.Transform,
					Tint = o.Tint,
					FadeEnd = o.FadeEnd,
					LodOverride = o.LodOverride,
					Scale = new V3( o.Transform.M11, o.Transform.M12, o.Transform.M13 ).Length(),
					Lods = model.Lods( mask, skin, MaterialIndex ),
				};

				Place( obj, TransformBounds( model.Mins, model.Maxs, o.Transform ) );
			}

			foreach ( var a in node.Aggregates )
			{
				if ( !Models.TryGetValue( a.Model, out var model ) ) continue;

				var transform = 0;
				foreach ( var mesh in a.Meshes )
				{
					if ( mesh.DrawCall < 0 || mesh.DrawCall >= model.Draws.Length ) continue;
					var world = mesh.HasTransform && transform < a.FragmentTransforms.Count ? a.FragmentTransforms[transform++] : Matrix4x4.Identity;
					var obj = new MapObject
					{
						Model = model,
						World = world,
						Tint = mesh.Tint,
						Lods = [[model.Draws[mesh.DrawCall]]],
						Aggregate = true,
					};

					Place( obj, (mesh.Mins, mesh.Maxs) );
				}
			}
		}

		void Place( MapObject obj, (V3 Mins, V3 Maxs) bounds )
		{
			var m = obj.World;
			obj.Row0 = new V4( m.M11, m.M21, m.M31, m.M41 );
			obj.Row1 = new V4( m.M12, m.M22, m.M32, m.M42 );
			obj.Row2 = new V4( m.M13, m.M23, m.M33, m.M43 );
			obj.Mins = bounds.Mins;
			obj.Maxs = bounds.Maxs;
			obj.Center = (bounds.Mins + bounds.Maxs) * 0.5f;
			obj.Extents = (bounds.Maxs - bounds.Mins) * 0.5f;
			Objects.Add( obj );
			mins = V3.Min( mins, bounds.Mins );
			maxs = V3.Max( maxs, bounds.Maxs );
		}

		Mins = mins;
		Maxs = maxs;
	}

	static (V3, V3) TransformBounds( V3 mins, V3 maxs, Matrix4x4 m )
	{
		var center = V3.Transform( (mins + maxs) * 0.5f, m );
		var e = (maxs - mins) * 0.5f;
		var extents = new V3(
			MathF.Abs( m.M11 ) * e.X + MathF.Abs( m.M21 ) * e.Y + MathF.Abs( m.M31 ) * e.Z,
			MathF.Abs( m.M12 ) * e.X + MathF.Abs( m.M22 ) * e.Y + MathF.Abs( m.M32 ) * e.Z,
			MathF.Abs( m.M13 ) * e.X + MathF.Abs( m.M23 ) * e.Y + MathF.Abs( m.M33 ) * e.Z );
		return (center - extents, center + extents);
	}

	BufferHandle Upload<T>( List<T[]> parts, BufferUsage usage, string name ) where T : unmanaged
	{
		long count = parts.Sum( x => (long)x.Length );
		var size = Math.Max( count * Marshal.SizeOf<T>(), 16 );
		var buffer = Gpu.CreateBuffer( new BufferInfo { Size = size, Usage = usage | BufferUsage.Storage | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = name } );

		// In chunks of up to 64 MiB, so staging stays small
		const int Chunk = 64 << 20;
		var staging = new byte[Chunk];
		long written = 0;
		var used = 0;
		foreach ( var part in parts )
		{
			var bytes = MemoryMarshal.AsBytes( part.AsSpan() );
			while ( bytes.Length > 0 )
			{
				var take = Math.Min( bytes.Length, Chunk - used );
				bytes[..take].CopyTo( staging.AsSpan( used ) );
				used += take;
				bytes = bytes[take..];
				if ( used == Chunk )
				{
					Gpu.WriteBuffer( buffer, staging.AsSpan( 0, used ), written );
					written += used;
					used = 0;
				}
			}
		}

		if ( used > 0 ) Gpu.WriteBuffer( buffer, staging.AsSpan( 0, used ), written );
		return buffer;
	}

	public void Dispose()
	{
		Gpu.WaitIdle();
		foreach ( var material in Materials )
		{
			if ( material.Set.IsValid ) Gpu.Destroy( material.Set );
		}

		foreach ( var texture in textures ) Gpu.Destroy( texture );
		if ( Vertices.IsValid ) Gpu.Destroy( Vertices );
		if ( Indices.IsValid ) Gpu.Destroy( Indices );
		package.Dispose();
	}
}
