using Basalt.Gpu;
using System.Runtime.InteropServices;

namespace Basalt.Backend;

/// <summary>
/// A Basalt texture that's a <see cref="Basalt.Gpu"/> texture. It remembers the layout its last recorded use left it
/// in, so Basalt's barriers, which name only where a texture is going, become full transitions.
/// </summary>
internal sealed class BackendTexture : GpuTexture
{
	readonly BackendDevice device;
	readonly TextureDesc desc;
	bool disposed;

	public BackendTexture( BackendDevice device, TextureHandle handle, in TextureDesc desc, string name )
	{
		this.device = device;
		this.desc = desc;
		Handle = handle;
		Name = name;
	}

	public TextureHandle Handle { get; }
	public string Name { get; }

	/// <summary>The layout the last recorded use left it in.</summary>
	public ImageLayout Layout { get; set; }

	public override int Width => desc.Width;
	public override int Height => desc.Height;
	public override int Mips => Math.Max( desc.Mips, 1 );
	public override TextureFormat Format => desc.Format;
	public override int Samples => Math.Max( desc.Samples, 1 );
	public override bool IsCube => desc.Cube;
	public override bool IsArray => false;
	public override bool IsValid => !disposed;
	public override int BindlessIndex => disposed ? device.MissingTextureViewIndex( false ) : Math.Max( device.Gpu.BindlessIndex( Handle ), 0 );
	public override Vector4 SheetData => default;

	public override int ViewIndex( bool srgb, TextureViewDimension dimension ) => BindlessIndex + (srgb ? 1 : 0);

	public override void KeepResident() { }

	public override void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		device.Gpu.Destroy( Handle );
	}

	public override string ToString() => Name;
}

/// <summary>
/// A <see cref="GpuBuffer"/>'s device side: a <see cref="Basalt.Gpu"/> buffer in video memory.
/// </summary>
internal sealed class BackendBuffer( BackendDevice device, BufferHandle handle, long size, string name ) : IDisposable
{
	public BufferHandle Handle { get; } = handle;
	public long Size { get; } = size;
	public string Name { get; } = name;
	bool disposed;

	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		device.Gpu.Destroy( Handle );
	}

	public override string ToString() => Name;
}

/// <summary>
/// Shader parameters as a dictionary of what was set, falling back to a parent's: a segment's to its frame's. Constant
/// structs are kept as bytes. <see cref="Version"/> changes whenever anything here or above changes, so contexts know when
/// to rebind.
/// </summary>
internal sealed class BackendParameters : ShaderParameters
{
	readonly Dictionary<uint, object> values = new();
	readonly Dictionary<uint, int> combos = new();
	int version;

	public BackendParameters Parent { get; set; }

	/// <summary>Changes when this or a parent changes.</summary>
	public int Version => version + (Parent?.Version ?? 0) * 31;

	public object Get( StringToken name )
	{
		lock ( values )
		{
			if ( values.TryGetValue( name.Value, out var value ) ) return value;
		}

		return Parent?.Get( name );
	}

	public int? GetCombo( StringToken name )
	{
		lock ( values )
		{
			if ( combos.TryGetValue( name.Value, out var value ) ) return value;
		}

		return Parent?.GetCombo( name );
	}

	void Store( StringToken name, object value )
	{
		lock ( values )
		{
			values[name.Value] = value;
			version++;
		}
	}

	public override void Set( StringToken name, int value ) => Store( name, value );
	public override void Set( StringToken name, uint value ) => Store( name, value );
	public override void Set( StringToken name, float value ) => Store( name, value );
	public override void Set( StringToken name, bool value ) => Store( name, value );
	public override void Set( StringToken name, Vector2 value ) => Store( name, value );
	public override void Set( StringToken name, Vector3 value ) => Store( name, value );
	public override void Set( StringToken name, Vector4 value ) => Store( name, value );
	public override void Set( StringToken name, Vector2Int value ) => Store( name, value );
	public override void Set( StringToken name, Matrix value ) => Store( name, value );
	public override void Set( StringToken name, GpuTexture value, int mip = -1 ) => Store( name, value );
	public override void Set( StringToken name, GpuBuffer value ) => Store( name, value );

	public override void SetData<T>( StringToken name, in T value )
	{
		var bytes = new byte[Marshal.SizeOf<T>()];
		MemoryMarshal.Write( bytes, in value );
		Store( name, bytes );
	}

	public override void SetCombo( StringToken name, int value )
	{
		lock ( values )
		{
			combos[name.Value] = value;
			version++;
		}
	}

	public override bool GetBool( StringToken name, bool fallback = false ) => Get( name ) is bool value ? value : fallback;

	public override void MergeTo( ShaderParameters target )
	{
		var into = (BackendParameters)target;
		if ( ReferenceEquals( into, this ) ) return;

		KeyValuePair<uint, object>[] copiedValues;
		KeyValuePair<uint, int>[] copiedCombos;
		lock ( values )
		{
			copiedValues = [.. values];
			copiedCombos = [.. combos];
		}

		lock ( into.values )
		{
			foreach ( var (key, value) in copiedValues ) into.values[key] = value;
			foreach ( var (key, value) in copiedCombos ) into.combos[key] = value;
			into.version++;
		}
	}

	public override void Clear()
	{
		lock ( values )
		{
			values.Clear();
			combos.Clear();
			version++;
		}
	}
}

/// <summary>
/// The programs a <see cref="BackendMaterial"/> can draw with.
/// </summary>
internal enum BackendProgram
{
	/// <summary>Nothing: an engine shader this backend doesn't have. Draws and dispatches with it are skipped.</summary>
	None,

	/// <summary>The built-in lit mesh program (<c>Shaders/forward.hlsl</c>): Depth and Forward modes.</summary>
	Mesh,
}

/// <summary>
/// A material: a program and its values (an albedo for now).
/// </summary>
internal sealed class BackendMaterial( string name, BackendProgram program, Color albedo ) : GpuMaterial
{
	public BackendProgram Program { get; } = program;
	public Color Albedo { get; set; } = albedo;

	public override string Name { get; } = name;
	public override bool IsTranslucent => false;
	public override bool IsAlphaTest => false;
	public override bool ReadsFrameBuffer => false;
	public override bool ShadowFastPath => false;
	public override bool NeedsLightProbe => false;
	public override ulong SimilarityKey => (ulong)Program;

	public override void Dispose() { }
}

/// <summary>
/// One vertex of a <see cref="BackendMesh"/>: position, normal, uv.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct MeshVertex( Vector3 position, Vector3 normal, Vector2 uv )
{
	/// <summary>Position in the mesh's space.</summary>
	public Vector3 Position = position;

	/// <summary>Unit normal.</summary>
	public Vector3 Normal = normal;

	/// <summary>Texture coordinates.</summary>
	public Vector2 Uv = uv;
}

/// <summary>
/// Geometry from raw vertex and index data, in a vertex and an index buffer on the GPU.
/// </summary>
public sealed class BackendMesh : GpuMesh
{
	internal BufferHandle Vertices { get; }
	internal BufferHandle Indices { get; }
	internal IndexFormat IndexFormat { get; }

	readonly string name;

	BackendMesh( BufferHandle vertices, BufferHandle indices, IndexFormat format, string name )
	{
		Vertices = vertices;
		Indices = indices;
		IndexFormat = format;
		this.name = name;
	}

	/// <inheritdoc/>
	public override string Name => name;

	/// <summary>
	/// A one-draw mesh of triangles from vertices and 32-bit indices, drawn with <paramref name="material"/>.
	/// </summary>
	public static RenderMesh Create( BackendDevice device, ReadOnlySpan<MeshVertex> vertices, ReadOnlySpan<uint> indices, GpuMaterial material, string name )
	{
		ArgumentOutOfRangeException.ThrowIfZero( vertices.Length );
		ArgumentOutOfRangeException.ThrowIfZero( indices.Length );

		var gpu = device.Gpu;
		var vertexBuffer = gpu.CreateBuffer( new BufferInfo { Size = vertices.Length * Marshal.SizeOf<MeshVertex>(), Usage = BufferUsage.Vertex, Name = $"{name} vertices" } );
		var indexBuffer = gpu.CreateBuffer( new BufferInfo { Size = indices.Length * 4L, Usage = BufferUsage.Index, Name = $"{name} indices" } );
		gpu.WriteBuffer( vertexBuffer, vertices );
		gpu.WriteBuffer( indexBuffer, indices );

		var mins = vertices[0].Position;
		var maxs = vertices[0].Position;
		foreach ( var v in vertices )
		{
			mins = Vector3.Min( mins, v.Position );
			maxs = Vector3.Max( maxs, v.Position );
		}

		var geometry = new BackendMesh( vertexBuffer, indexBuffer, IndexFormat.UInt32, name );
		var draw = new RenderMesh.Draw( 0, 0, 0, indices.Length, vertices.Length, 0, material );
		return new RenderMesh( geometry, new BBox( mins, maxs ), [[draw]] );
	}
}
