using Basalt.Gpu;
using Basalt.Source2.Materials;
using Basalt.Source2.Shaders;
using System.Runtime.InteropServices;
using System.Threading;

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
		Dimension = desc.Cube ? TextureDimension.TextureCube : desc.Depth > 1 ? TextureDimension.Texture3D : TextureDimension.Texture2D;
	}

	public TextureHandle Handle { get; }
	public string Name { get; }

	/// <summary>The layout the last recorded use left it in.</summary>
	public ImageLayout Layout { get; set; }

	/// <summary>Its shape, which a shader binding it must expect.</summary>
	public TextureDimension Dimension { get; init; }

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

	/// <summary>What material objects keep of these as an object's attributes (<see cref="ObjectLayer"/>), made when first set.</summary>
	internal ObjectLayer Layer;

	/// <summary>The names of the values set here and above, and the combos with their values (the nearest wins).</summary>
	internal void Names( HashSet<uint> values, Dictionary<uint, int> combosSet )
	{
		lock ( this.values )
		{
			foreach ( var key in this.values.Keys ) values.Add( key );
			foreach ( var (key, value) in combos ) combosSet.TryAdd( key, value );
		}

		Parent?.Names( values, combosSet );
	}

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
/// A material: an s&amp;box shader (<c>.shader_c</c>) with a compiled material's parameters and textures, or the
/// shader's defaults, plus values set in code. Variants are picked per mode and dynamic combos and kept. Changing it
/// (<see cref="Set(string, float[])"/>, <see cref="SetTexture"/>, <see cref="Reload"/>) moves <see cref="Version"/> on,
/// which makes its material objects again the next time it draws.
/// </summary>
internal sealed class BackendMaterial : GpuMaterial
{
	readonly Dictionary<string, ShaderVariant> variants = new();
	readonly Dictionary<string, float[]> values;
	int version;

	public BackendMaterial( string name, ShaderFile shader, MaterialFile file, Dictionary<string, BackendTexture> textures, Dictionary<string, float[]> values = null )
	{
		Name = name;
		this.values = values ?? new( StringComparer.OrdinalIgnoreCase );
		Load( shader, file, textures );
	}

	void Load( ShaderFile shader, MaterialFile file, Dictionary<string, BackendTexture> textures )
	{
		Shader = shader;
		File = file;
		Textures = textures ?? new( StringComparer.OrdinalIgnoreCase );

		Features = new int[shader?.Features?.StaticCombos.Length ?? 0];
		foreach ( var (parameter, value) in file?.Ints ?? new Dictionary<string, int>() )
		{
			var index = shader?.FeatureIndex( parameter ) ?? -1;
			if ( index >= 0 ) Features[index] = value;
		}
	}

	/// <summary>The shader, or null when there's no compiled one: draws with it are skipped.</summary>
	public ShaderFile Shader { get; private set; }

	/// <summary>The compiled material, or null for a material made from a shader.</summary>
	public MaterialFile File { get; private set; }

	/// <summary>Feature values (<c>F_*</c>), by the shader's feature index.</summary>
	public int[] Features { get; private set; }

	/// <summary>Textures by shader variable (<c>g_tColor</c>).</summary>
	public Dictionary<string, BackendTexture> Textures { get; private set; }

	/// <summary>Moves on whenever a value, texture or the material itself changes.</summary>
	public int Version => Volatile.Read( ref version );

	/// <summary>The material's value of a variable: set in code, else the compiled material's; null for the shader's default.</summary>
	public float[] Value( string variable )
	{
		lock ( values ) return values.TryGetValue( variable, out var value ) ? value : File?.Value( variable );
	}

	/// <summary>Set a variable's value in code, over the compiled material's, as <c>Material.Set</c> does.</summary>
	public void Set( string variable, float[] value )
	{
		lock ( values ) values[variable] = value;
		Interlocked.Increment( ref version );
	}

	/// <summary>Set a texture variable (<c>g_tColor</c>), as <c>Material.Set</c> does with a texture.</summary>
	public void SetTexture( string variable, BackendTexture texture )
	{
		var textures = new Dictionary<string, BackendTexture>( Textures, StringComparer.OrdinalIgnoreCase ) { [variable] = texture };
		Textures = textures;
		Interlocked.Increment( ref version );
	}

	/// <summary>
	/// Take a material's new compiled form, after its file (or its shader) changed on disk: values set in code stay.
	/// Call between frames.
	/// </summary>
	public void Reload( ShaderFile shader, MaterialFile file, Dictionary<string, BackendTexture> textures )
	{
		lock ( variants )
		{
			variants.Clear();
			Load( shader, file, textures );
			needsLightProbe = 0;
		}

		Interlocked.Increment( ref version );
	}

	/// <summary>A variant for a mode and dynamic combo values, made once.</summary>
	public ShaderVariant Variant( ShaderLibrary library, string mode, string key, Func<string, int?> combos, Func<string, int?> attributes )
	{
		lock ( variants )
		{
			if ( variants.TryGetValue( key, out var variant ) ) return variant;
			variant = library.Variant( Shader, mode, Features, combos, attributes );
			variants[key] = variant;
			return variant;
		}
	}

	bool Feature( string name ) => Shader?.FeatureIndex( name ) is int index && index >= 0 && Features[index] != 0;

	public override string Name { get; }
	public override bool IsTranslucent => Feature( "F_TRANSLUCENT" ) || Feature( "F_ADDITIVE_BLEND" );
	public override bool IsAlphaTest => Feature( "F_ALPHA_TEST" );
	public override bool ReadsFrameBuffer => false;
	public override bool ShadowFastPath => Shader is not null && !IsTranslucent && !IsAlphaTest;
	public override bool NeedsLightProbe
	{
		get
		{
			// Worked out once (0 until then, 1 no, 2 yes), as the draw loop asks for every draw
			var known = Volatile.Read( ref needsLightProbe );
			if ( known == 0 ) Volatile.Write( ref needsLightProbe, known = Attribute( "NeedsLightProbe", true ) && Attribute( "UsesBakedLightingFromProbe", true ) ? 2 : 1 );
			return known == 2;
		}
	}

	int needsLightProbe;

	/// <summary>
	/// A bool attribute of the material as native's <c>GetBoolAttributeOrDefault</c> reads it: the compiled material's,
	/// else what its shader's forward static combos export (<c>BoolAttribute( NeedsLightProbe, ... )</c>).
	/// </summary>
	bool Attribute( string name, bool fallback )
	{
		if ( File?.IntAttributes is { } own && own.TryGetValue( name, out var set ) ) return set != 0;
		if ( Shader is not { } shader || (shader.Mode( "Forward" ) ?? shader.Mode( "VrForward" )) is not { } mode ) return fallback;

		foreach ( var type in (ReadOnlySpan<ShaderProgramType>)[ShaderProgramType.Features, ShaderProgramType.Vertex, ShaderProgramType.Pixel] )
		{
			if ( shader[type] is not { } program ) continue;
			var id = shader.StaticComboId( program, mode, Features );
			if ( !program.HasCombo( id ) ) continue;

			StaticCombo combo;
			try
			{
				combo = program.LoadCombo( id );
			}
			catch ( System.IO.InvalidDataException )
			{
				continue;
			}

			foreach ( var attribute in combo.Attributes )
			{
				if ( attribute.Value is bool value && string.Equals( attribute.Name, name, StringComparison.OrdinalIgnoreCase ) ) return value;
			}
		}

		return fallback;
	}
	public override ulong SimilarityKey => (ulong)(Shader?.Name.GetHashCode() ?? 0);

	/// <summary>Drop its material objects and their sets. It still draws if it's used again (shared materials are).</summary>
	public override void Dispose() => Disposing?.Invoke( this );

	/// <summary>Called when it's disposed, so the device can drop what it made for it.</summary>
	internal Action<BackendMaterial> Disposing;
}

/// <summary>
/// One vertex of a <see cref="BackendMesh"/>: position, normal, uv and tangent (u direction and v sign); the tangent is
/// worked out from the uvs when it's left zero.
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

	/// <summary>Tangent along u, and the sign of v's direction relative to normal x tangent.</summary>
	public Vector4 Tangent;
}

/// <summary>
/// Geometry from raw vertex and index data, in a vertex and an index buffer on the GPU.
/// </summary>
public sealed class BackendMesh : GpuMesh, IBackendGeometry
{
	internal BufferHandle Vertices { get; }
	internal BufferHandle Indices { get; }
	internal IndexFormat IndexFormat { get; }

	readonly string name;
	readonly DrawGeometry geometry;

	/// <summary>Position, normal, uv and tangent, as <see cref="MeshVertex"/> lays them out.</summary>
	internal static readonly VertexLayout Layout = new( Marshal.SizeOf<MeshVertex>(),
	[
		new( VertexSemantic.Position, 0, VertexFormat.Float3, 0 ),
		new( VertexSemantic.Normal, 0, VertexFormat.Float3, 12 ),
		new( VertexSemantic.TexCoord, 0, VertexFormat.Float2, 24 ),
		new( VertexSemantic.TexCoord, 1, VertexFormat.Float2, 24 ),
		new( VertexSemantic.Tangent, 0, VertexFormat.Float4, 32 ),
	] );

	BackendMesh( BufferHandle vertices, BufferHandle indices, IndexFormat format, string name )
	{
		Vertices = vertices;
		Indices = indices;
		IndexFormat = format;
		this.name = name;
		geometry = new DrawGeometry( vertices, 0, indices, 0, format, 0, Layout, UncompressedTangentFrame: true );
	}

	DrawGeometry IBackendGeometry.For( int mesh, int drawCall ) => geometry;

	/// <inheritdoc/>
	public override string Name => name;

	/// <summary>
	/// A one-draw mesh of triangles from vertices and 32-bit indices, drawn with <paramref name="material"/>.
	/// Tangents left zero are computed from the uvs.
	/// </summary>
	public static RenderMesh Create( BackendDevice device, ReadOnlySpan<MeshVertex> vertices, ReadOnlySpan<uint> indices, GpuMaterial material, string name )
	{
		ArgumentOutOfRangeException.ThrowIfZero( vertices.Length );
		ArgumentOutOfRangeException.ThrowIfZero( indices.Length );

		var data = vertices.ToArray();
		ComputeTangents( data, indices );

		var gpu = device.Gpu;
		var vertexBuffer = gpu.CreateBuffer( new BufferInfo { Size = data.Length * Marshal.SizeOf<MeshVertex>(), Usage = BufferUsage.Vertex, Name = $"{name} vertices" } );
		var indexBuffer = gpu.CreateBuffer( new BufferInfo { Size = indices.Length * 4L, Usage = BufferUsage.Index, Name = $"{name} indices" } );
		gpu.WriteBuffer<MeshVertex>( vertexBuffer, data );
		gpu.WriteBuffer( indexBuffer, indices );

		var mins = data[0].Position;
		var maxs = data[0].Position;
		foreach ( var v in data )
		{
			mins = Vector3.Min( mins, v.Position );
			maxs = Vector3.Max( maxs, v.Position );
		}

		var geometry = new BackendMesh( vertexBuffer, indexBuffer, IndexFormat.UInt32, name );
		var draw = new RenderMesh.Draw( 0, 0, 0, indices.Length, data.Length, 0, material );
		return new RenderMesh( geometry, new BBox( mins, maxs ), [[draw]] );
	}

	/// <summary>
	/// Per-vertex tangents from each triangle's uv derivatives, for vertices whose tangent is zero.
	/// </summary>
	static void ComputeTangents( MeshVertex[] vertices, ReadOnlySpan<uint> indices )
	{
		var tangents = new Vector3[vertices.Length];
		var bitangents = new Vector3[vertices.Length];
		for ( int i = 0; i + 2 < indices.Length; i += 3 )
		{
			var (a, b, c) = ((int)indices[i], (int)indices[i + 1], (int)indices[i + 2]);
			var e1 = vertices[b].Position - vertices[a].Position;
			var e2 = vertices[c].Position - vertices[a].Position;
			var d1 = vertices[b].Uv - vertices[a].Uv;
			var d2 = vertices[c].Uv - vertices[a].Uv;
			var det = d1.x * d2.y - d2.x * d1.y;
			if ( MathF.Abs( det ) < 1e-12f ) continue;
			var r = 1 / det;
			var t = (e1 * d2.y - e2 * d1.y) * r;
			var bt = (e2 * d1.x - e1 * d2.x) * r;
			foreach ( var v in (ReadOnlySpan<int>)[a, b, c] )
			{
				tangents[v] += t;
				bitangents[v] += bt;
			}
		}

		for ( int i = 0; i < vertices.Length; i++ )
		{
			ref var v = ref vertices[i];
			if ( v.Tangent != default ) continue;

			var n = v.Normal;
			var t = tangents[i] - n * Vector3.Dot( n, tangents[i] );
			if ( t.LengthSquared < 1e-12f ) t = MathF.Abs( n.z ) < 0.9f ? Vector3.Cross( Vector3.Up, n ) : Vector3.Cross( Vector3.Forward, n );
			t = t.Normal;
			var sign = Vector3.Dot( Vector3.Cross( n, t ), bitangents[i] ) < 0 ? -1f : 1f;
			v.Tangent = new Vector4( t.x, t.y, t.z, sign );
		}
	}
}
