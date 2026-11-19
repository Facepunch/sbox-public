using Borealis.Gpu;
using Borealis.Source2.Materials;
using Borealis.Source2.Shaders;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Borealis.Backend;

/// <summary>
/// A Borealis texture that's a <see cref="Borealis.Gpu"/> texture. It remembers the layout its last recorded use left it
/// in, so Borealis's barriers, which name only where a texture is going, become full transitions.
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

	/// <summary>Its image: swapped between frames for one of other mips while it streams (<see cref="Stream"/>).</summary>
	public TextureHandle Handle { get; private set; }

	public string Name { get; }

	/// <summary>Its streaming state while only some of its mips may be resident, or null when it holds them all.</summary>
	public StreamedTexture Stream { get; set; }

	/// <summary>
	/// Take a new image (a streamed texture's other mips), ready to sample once work submitted after now runs; the old one
	/// is destroyed once the GPU is done with it. Between frames only.
	/// </summary>
	public void Swap( TextureHandle image )
	{
		if ( disposed )
		{
			device.Gpu.Destroy( image );
			return;
		}

		var old = Handle;
		Handle = image;
		Layout = ImageLayout.ShaderRead;
		BackendContext.ForgetMipViews( this, device.Gpu );
		device.Gpu.Destroy( old );
	}

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
	// What its image is, which a bindless read has to sample it as: a cube loaded from an array file is a cube array
	public override bool IsArray => !disposed && device.Gpu.InfoOf( Handle ).Dimension is TextureDimension.TextureCubeArray or TextureDimension.Texture2DArray;
	public override bool IsValid => !disposed;
	public override int BindlessIndex => disposed ? device.MissingTextureViewIndex( false ) : Math.Max( device.Gpu.BindlessIndex( Handle ), 0 );
	public override Vector4 SheetData => default;

	public override int ViewIndex( bool srgb, TextureViewDimension dimension ) => BindlessIndex + (srgb ? 1 : 0);

	public override void KeepResident() { }

	public override void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		BackendContext.ForgetMipViews( this, device.Gpu );
		device.Gpu.Destroy( Handle );
	}

	public override string ToString() => Name;
}

/// <summary>
/// A <see cref="GpuBuffer"/>'s device side: a <see cref="Borealis.Gpu"/> buffer in video memory. A borrowed one
/// (<paramref name="owned"/> false) is someone else's buffer, which disposing leaves alone.
/// </summary>
internal sealed class BackendBuffer( BackendDevice device, BufferHandle handle, long size, string name, bool owned = true ) : IDisposable
{
	public BufferHandle Handle { get; } = handle;
	public long Size { get; } = size;
	public string Name { get; } = name;
	bool disposed;

	/// <summary>
	/// Whether it can still be bound: not disposed, and for a borrowed one, its owner hasn't destroyed it (an engine buffer a
	/// game disposed while an object's attributes still name it). Draws bind the default buffer otherwise.
	/// </summary>
	public bool Live => !disposed && device.Gpu.IsAlive( Handle );

	public void Dispose()
	{
		if ( disposed || !owned ) return;
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
	// Read without a lock by every recording thread (a frame's parameters are every segment's parent); written under gate
	readonly object gate = new();
	readonly AttributeMap values;
	readonly AttributeMap combos;
	int version;

	public BackendParameters()
	{
		values = new AttributeMap( gate );
		combos = new AttributeMap( gate );
	}

	public BackendParameters Parent { get; set; }

	/// <summary>Changes when this or a parent changes.</summary>
	public int Version => Volatile.Read( ref version ) + (Parent?.Version ?? 0) * 31;

	public object Get( StringToken name )
	{
		for ( var p = this; p is not null; p = p.Parent )
		{
			if ( p.values.TryGet( name.Value, out var value ) ) return value;
		}

		return null;
	}

	public int? GetCombo( StringToken name )
	{
		for ( var p = this; p is not null; p = p.Parent )
		{
			if ( p.combos.TryGet( name.Value, out var value ) ) return (int)value;
		}

		return null;
	}

	void Store( StringToken name, object value )
	{
		lock ( gate )
		{
			Replace( name.Value, value );
			version++;
		}
	}

	// Each constant buffer's previous bytes by name, written the next time it's set, so setting one allocates nothing: a
	// reader on another thread that took the current bytes still reads them whole. Every byte array set here is this
	// set's own (SetData's, or MergeTo's copy). Under gate
	// A name's two spare arrays: a clear keeps the current one too, so a set cleared every frame (a segment's) still has both
	readonly Dictionary<uint, (byte[] First, byte[] Second)> spareBytes = new();

	byte[] TakeBytes( uint name, int size )
	{
		if ( !spareBytes.TryGetValue( name, out var spare ) ) return new byte[size];
		if ( spare.First?.Length == size )
		{
			if ( spare.Second is null ) spareBytes.Remove( name );
			else spareBytes[name] = (spare.Second, null);
			return spare.First;
		}

		if ( spare.Second?.Length == size )
		{
			spareBytes[name] = (spare.First, null);
			return spare.Second;
		}

		return new byte[size];
	}

	// Keep a name's replaced or cleared bytes to write next time, up to two
	void KeepBytes( uint name, byte[] bytes )
	{
		ref var spare = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault( spareBytes, name, out _ );
		if ( ReferenceEquals( spare.First, bytes ) || ReferenceEquals( spare.Second, bytes ) ) return;
		spare = spare.First is null ? (bytes, spare.Second) : (bytes, spare.First);
	}

	// Set a value, keeping the bytes it replaces to write next time. Hold gate
	void Replace( uint name, object value )
	{
		if ( values.TryGet( name, out var old ) && old is byte[] previous && !ReferenceEquals( previous, value ) ) KeepBytes( name, previous );
		values.Set( name, value );
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
	public override void Set( StringToken name, GpuTexture value, int mip = -1 ) => Store( name, mip >= 0 && value is not null ? new TextureMip( value, mip ) : value );
	public override void Set( StringToken name, GpuBuffer value ) => Store( name, value );

	public override void SetData<T>( StringToken name, in T value )
	{
		var size = Marshal.SizeOf<T>();
		lock ( gate )
		{
			var bytes = TakeBytes( name.Value, size );
			bytes.AsSpan( Math.Min( System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), size ) ).Clear();
			MemoryMarshal.Write( bytes, in value );
			Replace( name.Value, bytes );
			version++;
		}
	}

	/// <summary>Combo values boxed once: combos are small numbers.</summary>
	static readonly object[] BoxedCombos = [.. Enumerable.Range( 0, 64 ).Select( v => (object)v )];

	public override void SetCombo( StringToken name, int value )
	{
		var boxed = value >= 0 && value < BoxedCombos.Length ? BoxedCombos[value] : value;
		lock ( gate )
		{
			combos.Set( name.Value, boxed );
			version++;
		}
	}

	public override bool GetBool( StringToken name, bool fallback = false ) => Get( name ) is bool value ? value : fallback;

	static readonly uint BakedLightingName = new StringToken( "BakedLightingConstantBuffer_t" ).Value;
	static readonly uint ProbeVolumeName = new StringToken( "PerBatchLightProbeVolumeConstantBuffer_t" ).Value;
	int lightingFreeVersion = -1;
	long lightingFreeKey;

	// Odd while the two above are written: read without the lock when even and the same before and after (every recording
	// thread asks for it per run of draws, and the lock kept them waiting on each other)
	int lightingFreeSequence;

	/// <summary>
	/// The same for parameters that hold the same values but for their lightmap and light probe volume constants, which GPU-driven variants
	/// read from a record per draw: draws whose attributes differ only in those share a
	/// bucket. A hash of the rest, its count in the top half.
	/// </summary>
	internal long LightingFreeKey
	{
		get
		{
			var current = Version;
			var before = Volatile.Read( ref lightingFreeSequence );
			if ( (before & 1) == 0 )
			{
				var (version, key) = (Volatile.Read( ref lightingFreeVersion ), Volatile.Read( ref lightingFreeKey ));
				if ( version == current && Volatile.Read( ref lightingFreeSequence ) == before ) return key;
			}

			// The parent's first, so this lock is never held while taking another
			var parentKey = Parent?.LightingFreeKey;
			lock ( gate )
			{
				if ( lightingFreeVersion == current ) return lightingFreeKey;

				var hash = new HashCode();
				var count = 0;
				foreach ( var (name, value) in Sorted( values ) )
				{
					if ( name == ProbeVolumeName || name == BakedLightingName ) continue;
					hash.Add( name );
					if ( value is byte[] bytes ) hash.AddBytes( bytes );
					else hash.Add( value );
					count++;
				}

				foreach ( var (name, value) in Sorted( combos ) )
				{
					hash.Add( name ^ 0x9E3779B9u );
					hash.Add( (int)value );
					count++;
				}

				if ( parentKey is { } inherited ) hash.Add( inherited );
				var made = (long)count << 32 | (uint)hash.ToHashCode();
				Interlocked.Increment( ref lightingFreeSequence );
				lightingFreeKey = made;
				lightingFreeVersion = current;
				Interlocked.Increment( ref lightingFreeSequence );
				return made;
			}
		}
	}

	/// <summary>A map's entries by key, so the same values hash the same whatever order they were set in. Hold the gate.</summary>
	static List<(uint Key, object Value)> Sorted( AttributeMap map )
	{
		var entries = new List<(uint, object)>( map.Count );
		for ( int i = 0; i < map.Slots; i++ )
		{
			if ( map.At( i, out var key, out var value ) ) entries.Add( (key, value) );
		}

		entries.Sort( static ( a, b ) => a.Item1.CompareTo( b.Item1 ) );
		return entries;
	}

	/// <summary>What material objects keep of these as an object's attributes (<see cref="ObjectLayer"/>), made when first set.</summary>
	internal ObjectLayer Layer;

	/// <summary>The names of the values set here and above, and the combos with their values (the nearest wins).</summary>
	internal void Names( HashSet<uint> values, Dictionary<uint, int> combosSet )
	{
		lock ( gate )
		{
			for ( int i = 0; i < this.values.Slots; i++ )
			{
				if ( this.values.At( i, out var key, out _ ) ) values.Add( key );
			}

			for ( int i = 0; i < combos.Slots; i++ )
			{
				if ( combos.At( i, out var key, out var value ) ) combosSet.TryAdd( key, (int)value );
			}
		}

		Parent?.Names( values, combosSet );
	}

	/// <summary>The textures set here (not a parent's), into <paramref name="into"/>.</summary>
	internal void TexturesInto( List<BackendTexture> into )
	{
		lock ( gate )
		{
			for ( int i = 0; i < values.Slots; i++ )
			{
				if ( values.At( i, out _, out var value ) && value is BackendTexture texture ) into.Add( texture );
			}
		}
	}

	public override void MergeTo( ShaderParameters target )
	{
		var into = (BackendParameters)target;
		if ( ReferenceEquals( into, this ) ) return;

		// Taken under this set's lock, then set under the other's, so the two are never held at once
		var copiedValues = mergeValues ??= new();
		var copiedCombos = mergeCombos ??= new();
		lock ( gate )
		{
			Entries( values, copiedValues );
			Entries( combos, copiedCombos );
		}

		lock ( into.gate )
		{
			foreach ( var (key, value) in copiedValues )
			{
				// Bytes are copied into the other set's own, as it writes its bytes in place
				if ( value is byte[] bytes )
				{
					var copy = into.TakeBytes( key, bytes.Length );
					bytes.CopyTo( copy, 0 );
					into.Replace( key, copy );
				}
				else into.Replace( key, value );
			}

			foreach ( var (key, value) in copiedCombos ) into.combos.Set( key, value );
			into.version++;
		}

		copiedValues.Clear();
		copiedCombos.Clear();
	}

	[ThreadStatic] static List<(uint Key, object Value)> mergeValues, mergeCombos;

	/// <summary>A map's entries into <paramref name="into"/>, cleared first. Hold the gate.</summary>
	static void Entries( AttributeMap map, List<(uint Key, object Value)> into )
	{
		into.Clear();
		for ( int i = 0; i < map.Slots; i++ )
		{
			if ( map.At( i, out var key, out var value ) ) into.Add( (key, value) );
		}
	}

	public override void Clear()
	{
		lock ( gate )
		{
			// Bytes stay for the next time their names are set
			for ( int i = 0; i < values.Slots; i++ )
			{
				if ( values.At( i, out var key, out var value ) && value is byte[] bytes ) KeepBytes( key, bytes );
			}

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
internal sealed class BackendMaterial : GpuMaterial, IStreamDependent
{
	// Variants by key, read without a lock by every recording thread: replaced whole under variantsGate when one is added
	Dictionary<string, ShaderVariant> variants = new();
	readonly object variantsGate = new();
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
		Streamed();

		Features = FeatureValues( shader, file );

		SpecializationAttributes = shader is null ? [] : new[] { ShaderProgramType.Vertex, ShaderProgramType.Pixel, ShaderProgramType.Compute }
			.SelectMany( t => shader[t]?.Variables ?? [] )
			.Where( v => v.Register == RegisterType.Specialization && v.Source == VariableSource.Attribute && !string.IsNullOrEmpty( v.Attribute ) )
			.Select( v => v.Attribute ).Distinct( StringComparer.Ordinal ).ToArray();
	}

	/// <summary>
	/// The attributes the shader's specialization constants come from (<c>SpecializedConstant()</c>, such as
	/// <c>ToolsVisMode</c>): like dynamic combos, their values pick the variant.
	/// </summary>
	public string[] SpecializationAttributes { get; private set; } = [];

	/// <summary>The shader, or null when there's no compiled one: draws with it are skipped.</summary>
	public ShaderFile Shader { get; private set; }

	/// <summary>The compiled material, or null for a material made from a shader.</summary>
	public MaterialFile File { get; private set; }

	/// <summary>The shader version the material was compiled for (<c>Source( ShaderVersion )</c>); 0 without a file.</summary>
	public int ShaderVersion => File?.ShaderVersion ?? 0;

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
		Streamed();
		Interlocked.Increment( ref version );
	}

	/// <summary>The texture sizes its draws ask for, for its streamed textures.</summary>
	internal MaterialAsks Asks { get; } = new();

	// Its streamed textures, which ask for mips through it and re-key it when their images change
	void Streamed()
	{
		List<StreamedTexture> streamed = null;
		foreach ( var texture in Textures.Values )
		{
			if ( texture?.Stream is not { } stream ) continue;
			stream.AddDependent( this );
			(streamed ??= new()).Add( stream );
		}

		Asks.Textures = streamed?.ToArray();
	}

	void IStreamDependent.TexturesChanged() => Interlocked.Increment( ref version );

	/// <summary>
	/// Take a material's new compiled form, after its file (or its shader) changed on disk: values set in code stay.
	/// Call between frames.
	/// </summary>
	public void Reload( ShaderFile shader, MaterialFile file, Dictionary<string, BackendTexture> textures )
	{
		lock ( variantsGate )
		{
			Volatile.Write( ref variants, new() );
			Volatile.Write( ref gpuDrivenVariants, new() );
			Load( shader, file, textures );
			needsLightProbe = 0;
			Volatile.Write( ref needsStencil, -1 );
			loads++;
		}

		Interlocked.Increment( ref version );
	}

	/// <summary>Moves on with every <see cref="Reload"/>, so a variant made from what was loaded before isn't kept.</summary>
	int loads;

	/// <summary>The library its device's draws take its variants from, which <see cref="NeedsStencil"/> reads.</summary>
	internal ShaderLibrary Library { get; set; }

	int needsStencil = -1;

	/// <summary>A material's feature values (<c>F_*</c>) by the shader's feature index, from its file.</summary>
	internal static int[] FeatureValues( ShaderFile shader, MaterialFile file )
	{
		var features = new int[shader?.Features?.StaticCombos.Length ?? 0];
		foreach ( var (parameter, value) in file?.Ints ?? new Dictionary<string, int>() )
		{
			var index = shader?.FeatureIndex( parameter ) ?? -1;
			if ( index >= 0 ) features[index] = value;
		}

		// The shader's feature rules, as native applies them to a material's values: Allow1( F_CLOTH_SHADING, F_METALNESS_TEXTURE )
		// keeps cloth when a material sets both (Needlestack's gloves)
		if ( shader?.Features is { } program ) ShaderRule.Apply( program.StaticRules, ShaderRuleType.Feature, features );
		return features;
	}

	/// <summary>The modes <see cref="NeedsStencil"/> reads.</summary>
	internal static readonly string[] StencilModes = ["Forward", "Depth"];

	/// <summary>
	/// Read what <see cref="NeedsStencil"/> will for a material of <paramref name="shader"/> and <paramref name="file"/>, on a
	/// worker while it loads, so the first read on the main thread finds a pass module's lines preprocessed.
	/// </summary>
	internal static void ReadStencilAhead( ShaderLibrary library, ShaderFile shader, MaterialFile file )
	{
		if ( shader is null || StencilFromVariants ) return;
		var features = FeatureValues( shader, file );
		foreach ( var mode in StencilModes )
		{
			try
			{
				library.NeedsStencil( shader, mode, features );
			}
			catch ( Exception e ) when ( e is System.IO.InvalidDataException or KeyNotFoundException )
			{
				// Read again on the main thread, which skips it the same way
			}
		}
	}

	/// <summary>
	/// Whether <see cref="NeedsStencil"/> makes the forward and depth variants to read their state, as it did, rather than
	/// reading the state alone (<see cref="ShaderLibrary.PixelState(ShaderFile, string, ReadOnlySpan{int})"/>). For comparisons.
	/// </summary>
	public static bool StencilFromVariants { get; set; }

	/// <summary>
	/// Whether its forward or depth variant (no dynamic combos set, as the scene's meshes mostly draw it) needs a stencil:
	/// <see cref="ShaderPipelineState.NeedsStencil"/>, read from the shader without making the variants
	/// (<see cref="ShaderLibrary.NeedsStencil(ShaderFile, string, ReadOnlySpan{int}, out bool)"/>). Read once, then kept until
	/// it's loaded again; not kept while its shader recompiles in the background.
	/// </summary>
	public override bool NeedsStencil
	{
		get
		{
			var known = Volatile.Read( ref needsStencil );
			if ( known >= 0 ) return known != 0;
			if ( Library is not { } library || Shader is null ) return false;

			ShaderFile shader;
			int[] features;
			lock ( variantsGate ) (shader, features) = (Shader, Features);

			var needs = false;
			var pending = false;
			using var zone = Zones.StencilCheck.Start();
			foreach ( var mode in StencilModes )
			{
				try
				{
					if ( StencilFromVariants )
					{
						needs |= Variant( library, mode, mode, static _ => null, static _ => null )?.State.NeedsStencil == true;
						continue;
					}

					needs |= library.NeedsStencil( shader, mode, features, out var upgrading );
					pending |= upgrading;
				}
				catch ( System.IO.InvalidDataException )
				{
					// A mode whose combo wasn't compiled doesn't draw
				}
			}

			if ( !pending ) Volatile.Write( ref needsStencil, needs ? 1 : 0 );
			return needs;
		}
	}

	/// <summary>A variant for a mode and dynamic combo values, made once.</summary>
	public ShaderVariant Variant( ShaderLibrary library, string mode, string key, Func<string, int?> combos, Func<string, int?> attributes )
	{
		if ( Volatile.Read( ref variants ).TryGetValue( key, out var known ) ) return known;

		ShaderFile shader;
		int[] features;
		int loaded;
		lock ( variantsGate ) (shader, features, loaded) = (Shader, Features, loads);

		// Made outside the lock: making one loads the shader's combos and reads the attributes, which take locks of their own.
		// Not kept while its module compiles: its programs draw until then
		ShaderVariant variant;
		bool pending;
		using ( Zones.VariantLoad.Start() ) variant = library.Variant( shader, mode, features, combos, attributes, false, out pending );
		return pending ? variant : Keep( ref variants, key, variant, loaded );
	}

	Dictionary<string, ShaderVariant> gpuDrivenVariants = new();

	/// <summary>Add a variant made from load <paramref name="loaded"/> to a map, unless another got there first: the one kept.</summary>
	ShaderVariant Keep( ref Dictionary<string, ShaderVariant> map, string key, ShaderVariant variant, int loaded )
	{
		lock ( variantsGate )
		{
			if ( loaded != loads ) return variant;
			if ( map.TryGetValue( key, out var known ) ) return known;
			Volatile.Write( ref map, new Dictionary<string, ShaderVariant>( map ) { [key] = variant } );
			return variant;
		}
	}

	/// <summary>
	/// The same variant from the shader's GPU-driven file (<see cref="ShaderLibrary.GpuDriven"/>), made once; null when
	/// there's none or it lacks the combo.
	/// </summary>
	public ShaderVariant GpuDrivenVariant( ShaderLibrary library, string mode, string key, Func<string, int?> combos, Func<string, int?> attributes )
	{
		if ( Volatile.Read( ref gpuDrivenVariants ).TryGetValue( key, out var known ) ) return known;

		ShaderFile shader;
		int[] features;
		int loaded;
		lock ( variantsGate ) (shader, features, loaded) = (Shader, Features, loads);

		// Made outside the lock, as Variant's are
		ShaderVariant variant;
		try
		{
			variant = library.Variant( shader, mode, features, combos, attributes, gpuDriven: true );
		}
		catch ( System.IO.InvalidDataException )
		{
			variant = null;
		}

		// Not while its variant is compiling: the batch draws classic until then
		if ( variant is null && library.GpuDrivenPending( shader ) ) return null;
		return Keep( ref gpuDrivenVariants, key, variant, loaded );
	}

	bool Feature( string name ) => Shader?.FeatureIndex( name ) is int index && index >= 0 && Features[index] != 0;

	public override string Name { get; }
	/// <summary>
	/// Whether it blends: its translucency features, or the <c>translucent</c> attribute its shader exports, as native reads a
	/// material's (a shader that defines <c>S_TRANSLUCENT</c> itself, as Rubble's glass does, exports it from sbox_pixel.fxc
	/// with no feature). Worked out once per load: draws ask for every draw.
	/// </summary>
	public override bool IsTranslucent
	{
		get
		{
			// The load it was worked out for, and the answer in the low bits (0 not yet, 1 no, 2 yes)
			var known = Volatile.Read( ref translucent );
			if ( (known & 3) == 0 || (int)(known >> 2) != loads )
			{
				known = ((long)loads << 2) | (Feature( "F_TRANSLUCENT" ) || Feature( "F_ADDITIVE_BLEND" ) || Attribute( "translucent", false ) ? 2L : 1L);
				Volatile.Write( ref translucent, known );
			}

			return (known & 3) == 2;
		}
	}

	long translucent;
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
	internal bool Attribute( string name, bool fallback )
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

	BackendMesh( BufferHandle vertices, BufferHandle indices, IndexFormat format, string name, PooledGeometry? pooled, VertexLayout layout = null )
	{
		Vertices = vertices;
		Indices = indices;
		IndexFormat = format;
		this.name = name;
		geometry = new DrawGeometry( vertices, 0, indices, 0, format, 0, layout ?? Layout, UncompressedTangentFrame: true, pooled );
	}

	DrawGeometry IBackendGeometry.For( int mesh, int drawCall ) => geometry;

	/// <inheritdoc/>
	public override string Name => name;

	/// <summary>
	/// A one-draw mesh of triangles from vertices and 32-bit indices, drawn with <paramref name="material"/>.
	/// Tangents left zero are computed from the uvs. Without <paramref name="pooled"/> it stays out of the GPU-driven pages:
	/// the device's own meshes (the sky box, the full-screen quad) last as long as it and would hold a page each.
	/// </summary>
	public static RenderMesh Create( BackendDevice device, ReadOnlySpan<MeshVertex> vertices, ReadOnlySpan<uint> indices, GpuMaterial material, string name, bool pooled = true )
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

		// In the GPU-driven pages too, when the device keeps them
		PooledGeometry? inPages = null;
		if ( pooled && device.GpuDrivenTables is { } tables )
		{
			var (slot, offset) = tables.AddVertices( MemoryMarshal.AsBytes( data.AsSpan() ) );
			var (page, pageSlot, first) = tables.AddIndices( MemoryMarshal.AsBytes( indices ), IndexFormat.UInt32 );
			// And its positions alone, for depth-only draws
			var positions = new float[data.Length * 3];
			for ( int i = 0; i < data.Length; i++ )
			{
				positions[i * 3] = data[i].Position.x;
				positions[i * 3 + 1] = data[i].Position.y;
				positions[i * 3 + 2] = data[i].Position.z;
			}

			var (_, positionOffset, positionSlot) = tables.AddVertexStream( MemoryMarshal.AsBytes( positions.AsSpan() ) );
			var positionsRecord = tables.Geometry( positionSlot, positionOffset, device.Layout( BackendModel.PositionLayout ), data.Length );
			inPages = new PooledGeometry( tables.Geometry( slot, offset, device.Layout( Layout ), data.Length ), page, pageSlot, first, IndexFormat.UInt32, positionsRecord, data.Length );
		}

		var geometry = new BackendMesh( vertexBuffer, indexBuffer, IndexFormat.UInt32, name, inPages );
		var draw = new RenderMesh.Draw( 0, 0, 0, indices.Length, data.Length, 0, material );
		return new RenderMesh( geometry, new BBox( mins, maxs ), [[draw]] );
	}

	/// <summary>
	/// A one-draw mesh of triangles from vertices of a layout of their own (<paramref name="layout"/>: uncompressed normals
	/// and tangents, streams a shader reads by semantic) and 32-bit indices, drawn with <paramref name="material"/> within
	/// <paramref name="bounds"/>: a mesh made in code that <see cref="MeshVertex"/> can't describe, such as a water body's
	/// clipmap. It isn't kept in the GPU-driven pages.
	/// </summary>
	internal static RenderMesh Create<T>( BackendDevice device, ReadOnlySpan<T> vertices, VertexLayout layout, ReadOnlySpan<uint> indices, BBox bounds, GpuMaterial material, string name ) where T : unmanaged
	{
		ArgumentOutOfRangeException.ThrowIfZero( vertices.Length );
		ArgumentOutOfRangeException.ThrowIfZero( indices.Length );

		var gpu = device.Gpu;
		var vertexBuffer = gpu.CreateBuffer( new BufferInfo { Size = vertices.Length * (long)Marshal.SizeOf<T>(), Usage = BufferUsage.Vertex, Name = $"{name} vertices" } );
		var indexBuffer = gpu.CreateBuffer( new BufferInfo { Size = indices.Length * 4L, Usage = BufferUsage.Index, Name = $"{name} indices" } );
		gpu.WriteBuffer( vertexBuffer, vertices );
		gpu.WriteBuffer( indexBuffer, indices );

		var geometry = new BackendMesh( vertexBuffer, indexBuffer, IndexFormat.UInt32, name, null, layout );
		var draw = new RenderMesh.Draw( 0, 0, 0, indices.Length, vertices.Length, 0, material );
		return new RenderMesh( geometry, bounds, [[draw]] );
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
