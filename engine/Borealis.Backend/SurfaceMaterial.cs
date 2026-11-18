using Borealis.Gpu;
using Sandbox.ShaderCompiler;
using System.Buffers.Binary;
using System.Threading;

namespace Borealis.Backend;

/// <summary>
/// A material of a new-tier shader (a Slang surface or raw module, see docs/managed/shader-format-replacement.md): its
/// record, laid out as the shader's <c>[Material]</c> struct, with textures as bindless slots. The shader compiler
/// generates a class per material deriving from this (<c>new Rock { Color = texture, Tint = ... }</c>); its properties
/// write the record at their fields' offsets. The device finds and compiles the shader by file name the first time the
/// material draws, and checks the layout the class was generated for against it.
/// </summary>
public abstract class SurfaceMaterial : GpuMaterial, IStreamDependent
{
	readonly byte[] record;
	readonly Dictionary<int, (GpuTexture Texture, bool Srgb)> textures = new();
	int version;

	/// <summary>
	/// A material of the shader in <paramref name="shader"/> (a file name the device's surface folders hold), with a
	/// record of <paramref name="recordSize"/> bytes laid out as <paramref name="layoutHash"/> says, drawn with the
	/// render state <paramref name="states"/> describes (opaque for none).
	/// </summary>
	protected SurfaceMaterial( string shader, int recordSize, uint layoutHash, SurfaceStateLayout states = null )
	{
		Shader = shader;
		record = new byte[recordSize];
		LayoutHash = layoutHash;
		States = states ?? SurfaceStateLayout.Opaque;
	}

	/// <summary>The shader's file name (<c>rock.slang</c>).</summary>
	public string Shader { get; }

	/// <summary>The layout fingerprint the class was generated for; the compiled shader's must match.</summary>
	public uint LayoutHash { get; }

	/// <summary>
	/// Clip pixels whose opacity is under the cutoff, in colour and depth passes, whatever the render state says
	/// (<see cref="RenderState"/>).
	/// </summary>
	public bool AlphaTest
	{
		get => alphaTest;
		set
		{
			alphaTest = value;
			Interlocked.Increment( ref version );
		}
	}

	bool alphaTest;

	/// <summary>Draw back faces too, their normal turned to the viewer, whatever the render state says (<see cref="RenderState"/>).</summary>
	public bool TwoSided
	{
		get => twoSided;
		set
		{
			twoSided = value;
			Interlocked.Increment( ref version );
		}
	}

	bool twoSided;

	/// <summary>Its render state's defaults and the fields that change it: the shader's.</summary>
	public SurfaceStateLayout States { get; }

	/// <summary>
	/// The render state it draws with: the shader's defaults, changed by its <c>[RenderState]</c> fields, with
	/// <see cref="AlphaTest"/> and <see cref="TwoSided"/> on top. Resolved again when a field changes. A blended one draws
	/// in the translucent layer.
	/// </summary>
	public SurfaceRenderState RenderState
	{
		get
		{
			var v = Version;
			var known = resolved;
			if ( known is not null && known.Version == v ) return known.State;
			SurfaceRenderState state;
			lock ( record ) state = States.Resolve( record );
			state = state with { AlphaTest = state.AlphaTest || alphaTest, TwoSided = state.TwoSided || twoSided };
			resolved = new Resolved( v, state );
			return state;
		}
	}

	sealed record Resolved( int Version, SurfaceRenderState State );

	Resolved resolved;

	/// <summary>
	/// What draws where the surface can't (a skinned mesh, a mode surfaces have no pass for): the <c>.shader</c> material a
	/// converted <c>.vmat</c> stands in for, made the first time it's needed; null for none.
	/// </summary>
	internal GpuMaterial Fallback => fallback?.Value;

	Lazy<GpuMaterial> fallback;

	/// <summary>Set what draws where the surface can't (<see cref="Fallback"/>).</summary>
	internal void SetFallback( Func<GpuMaterial> make ) => fallback = new Lazy<GpuMaterial>( make, LazyThreadSafetyMode.ExecutionAndPublication );

	/// <summary>
	/// A bool attribute of the material as the scene reads it for its objects' flags (<c>overlay</c>, <c>NoZPrepass</c>,
	/// <c>DoNotCastShadows</c>, <c>bWantsFBCopyTexture</c>): a converted <c>.vmat</c>'s are its <c>.shader</c> material's;
	/// <paramref name="fallback"/> for a material with none.
	/// </summary>
	internal bool Attribute( string name, bool fallback ) => attributes?.Value is { } source ? source.Attribute( name, fallback ) : fallback;

	Lazy<BackendMaterial> attributes;

	/// <summary>Set where <see cref="Attribute"/> reads from: the <c>.shader</c> material, without its textures.</summary>
	internal void SetAttributes( Func<BackendMaterial> make ) => attributes = new Lazy<BackendMaterial>( make, LazyThreadSafetyMode.ExecutionAndPublication );

	/// <summary>
	/// Its values of <paramref name="features"/> (its shader's <see cref="Sandbox.ShaderCompiler.MaterialLayout.Features"/>), as
	/// pipelines are specialised for them: a key that tells value sets apart (0 for none), and the values. Read again when
	/// a field changes.
	/// </summary>
	internal (ulong Key, int[] Values) Features( IReadOnlyList<Sandbox.ShaderCompiler.MaterialField> features )
	{
		if ( features.Count == 0 ) return (0, null);
		var v = Version;
		var known = featureValues;
		if ( known is not null && known.Version == v ) return (known.Key, known.Values);

		var values = new int[features.Count];
		var key = 14695981039346656037ul;
		lock ( record )
		{
			for ( int i = 0; i < values.Length; i++ )
			{
				values[i] = BinaryPrimitives.ReadInt32LittleEndian( record.AsSpan( features[i].Offset ) );
				key = (key ^ (uint)values[i]) * 1099511628211ul;
			}
		}

		key |= 1;
		featureValues = new FeatureValues( v, key, values );
		return (key, values);
	}

	sealed record FeatureValues( int Version, ulong Key, int[] Values );

	FeatureValues featureValues;

	/// <summary>What its program's pipelines are specialised for, at a version of it (<see cref="SurfaceProgram.Features"/>).</summary>
	internal sealed record SpecializedFor( int Version, SurfaceProgram Program, SurfaceFeatures Features );

	/// <summary>The last <see cref="SpecializedFor"/> worked out.</summary>
	internal SpecializedFor Specialization { get; set; }

	/// <summary>Its compiled program, once a device found it (<see cref="SurfaceShaders"/>).</summary>
	internal SurfaceProgram Program { get; set; }

	/// <summary>Whether a model's draw calls noted the passes it draws with (<see cref="SurfaceShaders.NoteMaterial"/>).</summary>
	internal bool PassesNoted { get; set; }

	/// <summary>Its name, for diagnostics; the shader's by default.</summary>
	public string DisplayName { get; set; }

	/// <summary>Moves on whenever a field changes.</summary>
	public int Version => Volatile.Read( ref version );

	/// <summary>The record as the shader reads it: texture slots resolved now, sRGB views for sRGB fields.</summary>
	internal void WriteRecord( Span<byte> into, Func<GpuTexture, bool, int> slot )
	{
		lock ( record )
		{
			record.CopyTo( into );
			foreach ( var (offset, (texture, srgb)) in textures )
				BinaryPrimitives.WriteInt32LittleEndian( into[offset..], texture is null ? 0 : slot( texture, srgb ) );
		}
	}

	/// <summary>Bytes in the record.</summary>
	internal int RecordSize => record.Length;

	void Changed() => Interlocked.Increment( ref version );

	/// <summary>A texture field.</summary>
	protected GpuTexture GetTexture( int offset )
	{
		lock ( record ) return textures.TryGetValue( offset, out var t ) ? t.Texture : null;
	}

	/// <summary>Set a texture field; <paramref name="srgb"/> reads it through its sRGB view.</summary>
	protected void SetTexture( int offset, GpuTexture texture, bool srgb )
	{
		List<StreamedTexture> streamed = null;
		lock ( record )
		{
			textures[offset] = (texture, srgb);
			foreach ( var (t, _) in textures.Values )
			{
				if ( t is BackendTexture { Stream: { } stream } ) (streamed ??= new()).Add( stream );
			}
		}

		if ( texture is BackendTexture { Stream: { } added } ) added.AddDependent( this );
		Asks.Textures = streamed?.ToArray();
		Changed();
	}

	/// <summary>The texture sizes its draws ask for, for its streamed textures.</summary>
	internal MaterialAsks Asks { get; } = new();

	void IStreamDependent.TexturesChanged() => Changed();

	/// <summary>A float field.</summary>
	protected float GetFloat( int offset )
	{
		lock ( record ) return BinaryPrimitives.ReadSingleLittleEndian( record.AsSpan( offset ) );
	}

	/// <summary>Set a float field.</summary>
	protected void SetFloat( int offset, float value ) => Write( offset, value );

	/// <summary>A float2 field.</summary>
	protected Vector2 GetVector2( int offset ) => new( GetFloat( offset ), GetFloat( offset + 4 ) );

	/// <summary>Set a float2 field.</summary>
	protected void SetVector2( int offset, Vector2 value ) => Write( offset, value.x, value.y );

	/// <summary>A float3 field.</summary>
	protected Vector3 GetVector3( int offset ) => new( GetFloat( offset ), GetFloat( offset + 4 ), GetFloat( offset + 8 ) );

	/// <summary>Set a float3 field.</summary>
	protected void SetVector3( int offset, Vector3 value ) => Write( offset, value.x, value.y, value.z );

	/// <summary>A float4 field.</summary>
	protected Vector4 GetVector4( int offset ) => new( GetFloat( offset ), GetFloat( offset + 4 ), GetFloat( offset + 8 ), GetFloat( offset + 12 ) );

	/// <summary>Set a float4 field.</summary>
	protected void SetVector4( int offset, Vector4 value ) => Write( offset, value.x, value.y, value.z, value.w );

	/// <summary>An int, uint or bool field.</summary>
	protected int GetInt( int offset )
	{
		lock ( record ) return BinaryPrimitives.ReadInt32LittleEndian( record.AsSpan( offset ) );
	}

	/// <summary>Set an int, uint or bool field.</summary>
	protected void SetInt( int offset, int value )
	{
		lock ( record ) BinaryPrimitives.WriteInt32LittleEndian( record.AsSpan( offset ), value );
		Changed();
	}

	void Write( int offset, params ReadOnlySpan<float> values )
	{
		lock ( record )
		{
			for ( int i = 0; i < values.Length; i++ ) BinaryPrimitives.WriteSingleLittleEndian( record.AsSpan( offset + i * 4 ), values[i] );
		}

		Changed();
	}

	/// <inheritdoc/>
	public override string Name => DisplayName ?? Shader;

	/// <inheritdoc/>
	public override bool IsTranslucent => RenderState.DrawsTranslucent;

	/// <inheritdoc/>
	public override bool IsAlphaTest => RenderState.AlphaTest;

	/// <summary>
	/// Never by itself, as a <c>.shader</c> material: the scene asks for the frame-buffer copy from the material's
	/// <c>bWantsFBCopyTexture</c> attribute (<see cref="Attribute"/>; native's object flags in the game).
	/// </summary>
	public override bool ReadsFrameBuffer => false;

	/// <summary>Never: a surface draws its depth with its own pass (vertices it moves, opacity it clips).</summary>
	public override bool ShadowFastPath => false;

	/// <summary>
	/// Once its program has compiled and doesn't move vertices (<see cref="SurfaceProgram.Displaces"/>), when it draws opaque,
	/// one-sided, depth tested and written, without alpha test or depth bias: its depth is the shared depth-only material's,
	/// as native's fast path draws a <c>ShadowFastPath</c> material's.
	/// </summary>
	public override bool DepthOnlyNow => Program is { Displaces: false, Stale: false } && RenderState is { Blend: SurfaceBlend.Opaque, DepthWrite: true, DepthTest: true, DepthBias: 0, SlopeDepthBias: 0, TwoSided: false, AlphaTest: false, Translucent: false };

	/// <summary>Until its program is found, or while a hotload has dropped it.</summary>
	public override bool DepthOnlyPending => Program is null or { Stale: true };

	/// <summary>
	/// Always: a surface lights through the engine's lighting, which reads a probe volume's baked light and occlusion
	/// when its object isn't lightmapped, as today's lit shaders declare (<c>NeedsLightProbe</c>,
	/// <c>UsesBakedLightingFromProbe</c>).
	/// </summary>
	public override bool NeedsLightProbe => true;

	/// <inheritdoc/>
	public override ulong SimilarityKey => (ulong)StringComparer.OrdinalIgnoreCase.GetHashCode( Shader );

	/// <inheritdoc/>
	public override void Dispose() { }
}
