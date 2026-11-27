using Borealis.Gpu;
using Sandbox.ShaderCompiler;
using System.Buffers.Binary;
using System.Threading;

namespace Borealis.Backend;

/// <summary>
/// A material of a new-tier shader (a Slang surface or raw module, see docs/reference/borealis-slang-materials.md): its
/// record, laid out as the shader's <c>[Material]</c> struct, with textures as bindless slots. The shader compiler
/// generates a class per material deriving from this (<c>new Rock { Color = texture, Tint = ... }</c>); its properties
/// write the record at their fields' offsets. The device finds and compiles the shader by file name the first time the
/// material draws, and checks the layout the class was generated for against it.
/// </summary>
public abstract class SurfaceMaterial : GpuMaterial, IStreamDependent
{
	readonly byte[] record;
	readonly Dictionary<int, (GpuTexture Texture, bool Srgb)> textures = new();
	int version, stateVersion;

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
			Changed();
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
			Changed();
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
	public SurfaceRenderState RenderState => ResolveState().State;

	Resolved ResolveState()
	{
		var v = StateVersion;
		var known = resolved;
		if ( known is not null && known.Version == v ) return known;
		SurfaceRenderState state;
		lock ( record ) state = States.Resolve( record );
		state = state with { AlphaTest = state.AlphaTest || alphaTest, TwoSided = state.TwoSided || twoSided };
		var depthOnly = state is { Blend: SurfaceBlend.Opaque, DepthWrite: true, DepthTest: true, DepthBias: 0, SlopeDepthBias: 0, AlphaTest: false, Translucent: false };
		return resolved = new Resolved( v, state, depthOnly );
	}

	sealed record Resolved( int Version, SurfaceRenderState State, bool DepthOnly );

	/// <summary>Whether its render state draws both faces (<see cref="TwoSided"/>, or a field of its shader's).</summary>
	public override bool DrawsTwoSided => RenderState.TwoSided;

	Resolved resolved;

	/// <summary>
	/// What draws where the surface can't (a skinned mesh, a mode surfaces have no pass for): the <c>.shader</c> material a
	/// converted <c>.vmat</c> stands in for, made with the surface; null for none.
	/// </summary>
	internal BackendMaterial Fallback => fallback;

	BackendMaterial fallback;

	/// <summary>Set what draws where the surface can't (<see cref="Fallback"/>).</summary>
	internal void SetFallback( BackendMaterial material ) => fallback = material;

	/// <summary>
	/// A bool attribute of the material as the scene reads it for its objects' flags (<c>overlay</c>, <c>NoZPrepass</c>,
	/// <c>DoNotCastShadows</c>, <c>bWantsFBCopyTexture</c>): a converted <c>.vmat</c>'s are its <c>.shader</c> material's;
	/// <paramref name="fallback"/> for a material with none. A surface standing in for a deleted <c>.shader</c> has no
	/// static combos to read them from: after the material's own, it answers what its module declares for its features
	/// (<see cref="ModuleAttribute"/>), so a map's <c>static_overlay</c> decals stay overlays.
	/// </summary>
	internal bool Attribute( string name, bool fallback )
	{
		var source = Fallback ?? attributes;
		if ( source?.Shader is not null ) return source.Attribute( name, fallback );
		var declared = ModuleAttribute( name ) ?? fallback;
		return source is null ? declared : source.Attribute( name, declared );
	}

	/// <summary>
	/// A bool attribute its surface's module declares for the material's feature values (<c>BoolAttribute( overlay, true )</c>),
	/// as a <c>.shader</c>'s static combos export it; null when it declares none.
	/// </summary>
	private protected virtual bool? ModuleAttribute( string name ) => null;

	BackendMaterial attributes;

	/// <summary>Set where <see cref="Attribute"/> reads from: the <c>.shader</c> material, without its textures.</summary>
	internal void SetAttributes( BackendMaterial material ) => attributes = material;

	/// <summary>
	/// Its values of <paramref name="features"/> (its shader's <see cref="Sandbox.ShaderCompiler.MaterialLayout.Features"/>), as
	/// pipelines are specialised for them: a key that tells value sets apart (0 for none), and the values. Read again when
	/// a field changes.
	/// </summary>
	internal (ulong Key, int[] Values) Features( IReadOnlyList<Sandbox.ShaderCompiler.MaterialField> features )
	{
		if ( features.Count == 0 ) return (0, null);
		var v = StateVersion;
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
	internal SurfaceProgram Program
	{
		get => program;
		set
		{
			program = value;
			DepthOnlyMayHaveChanged();
		}
	}

	SurfaceProgram program;

	/// <summary>Whether it noted the passes it draws with (<see cref="SurfaceShaders.NoteMaterial"/>): a model's draw calls, or its first draw.</summary>
	internal bool PassesNoted { get; set; }

	/// <summary>Its name, for diagnostics; the shader's by default.</summary>
	public string DisplayName { get; set; }

	/// <summary>Moves on whenever a field changes.</summary>
	public int Version => Volatile.Read( ref version );

	/// <summary>
	/// Moves on when a field its render state or feature values read changes (<see cref="AffectsState"/>), a texture, or
	/// <see cref="AlphaTest"/> and <see cref="TwoSided"/>; not when other values change. What's worked out from those
	/// (<see cref="RenderState"/>, <see cref="Features"/>, <see cref="Specialization"/>) is kept by it, so code that sets a
	/// value every frame (a weapon's glow) doesn't work them out again for every draw of every frame.
	/// </summary>
	public int StateVersion => Volatile.Read( ref stateVersion );

	/// <summary>
	/// Whether the field at <paramref name="offset"/> can change its render state or feature values: every field, unless the
	/// material knows its layout.
	/// </summary>
	private protected virtual bool AffectsState( int offset ) => true;

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

	void Changed()
	{
		Interlocked.Increment( ref stateVersion );
		DepthOnlyMayHaveChanged();
		Interlocked.Increment( ref version );
	}

	void Changed( int offset, int words = 1 )
	{
		for ( int i = 0; i < words; i++ )
		{
			if ( !AffectsState( offset + i * 4 ) ) continue;
			Interlocked.Increment( ref stateVersion );
			DepthOnlyMayHaveChanged();
			break;
		}

		Interlocked.Increment( ref version );
	}

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

		Asks.Textures = streamed?.ToArray();
		Changed();
	}

	/// <summary>The texture sizes its draws ask for, for its streamed textures.</summary>
	internal MaterialAsks Asks { get; } = new();

	void IStreamDependent.TexturesChanged()
	{
		Changed();
		((IStreamDependent)Fallback)?.TexturesChanged();
	}

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
		Changed( offset );
	}

	void Write( int offset, params ReadOnlySpan<float> values )
	{
		lock ( record )
		{
			for ( int i = 0; i < values.Length; i++ ) BinaryPrimitives.WriteSingleLittleEndian( record.AsSpan( offset + i * 4 ), values[i] );
		}

		Changed( offset, values.Length );
	}

	/// <inheritdoc/>
	public override string Name => DisplayName ?? Shader;

	/// <inheritdoc/>
	public override bool IsTranslucent => RenderState.DrawsTranslucent;

	/// <inheritdoc/>
	public override bool IsAlphaTest => RenderState.AlphaTest;

	/// <summary>An opaque alpha-tested surface: its GPU-driven depth and forward pixel entries clip the fade's dither too.</summary>
	public override bool DithersLodFade => RenderState is { AlphaTest: true, Translucent: false };

	/// <summary>
	/// Never by itself, as a <c>.shader</c> material: the scene asks for the frame-buffer copy from the material's
	/// <c>bWantsFBCopyTexture</c> attribute (<see cref="Attribute"/>; native's object flags in the game).
	/// </summary>
	public override bool ReadsFrameBuffer => false;

	/// <summary>Never: a surface draws its depth with its own pass (vertices it moves, opacity it clips).</summary>
	public override bool ShadowFastPath => false;

	/// <summary>
	/// Once its program has compiled and doesn't move vertices (<see cref="SurfaceProgram.Displaces"/>), when it draws opaque,
	/// depth tested and written, without alpha test or depth bias: its depth is the shared depth-only material's (its copy
	/// that draws both faces for a two-sided one), as native's fast path draws a <c>ShadowFastPath</c> material's.
	/// </summary>
	public override bool DepthOnlyNow => Program is { Displaces: false, Stale: false } && ResolveState().DepthOnly;

	/// <summary>Until its program is found, or while a hotload has dropped it.</summary>
	public override bool DepthOnlyPending => Program is null or { Stale: true };

	/// <summary>Once its program has compiled: whether its vertex stage is its own (<see cref="SurfaceProgram.PlacesVertices"/>).</summary>
	public override bool? PlacesVertices => Program is { Stale: false } program ? program.PlacesVertices : null;

	/// <summary>
	/// Once its program has compiled: drawing translucent (or without depth writes) with no translucent depth pass in its
	/// program (the surface draw has nothing to draw its depth with) and no <c>.shader</c> to fall
	/// back to.
	/// </summary>
	public override bool DrawsNoDepth => fallback is null && Program is { Stale: false } program && (RenderState.Translucent || !RenderState.DepthWrite)
		&& program.Pass( BackendContext.DepthTranslucentPass ) is null;

	/// <summary>
	/// Once its program has compiled: whether its module declares <c>BoolAttribute( AnimatesVertices, true )</c> for its feature
	/// values (<see cref="Attribute"/>).
	/// </summary>
	public override bool? AnimatesVertices => Program is { Stale: false } ? Attribute( "AnimatesVertices", false ) : null;

	/// <summary>
	/// Always: a surface lights through the engine's lighting, which reads a probe volume's baked light and occlusion
	/// when its object isn't lightmapped, as today's lit shaders declare (<c>NeedsLightProbe</c>,
	/// <c>UsesBakedLightingFromProbe</c>).
	/// </summary>
	public override bool NeedsLightProbe => true;

	/// <inheritdoc/>
	public override ulong SimilarityKey => (ulong)StringComparer.OrdinalIgnoreCase.GetHashCode( Shader );

	/// <inheritdoc/>
	public override void Dispose() => Interlocked.Exchange( ref fallback, null )?.Dispose();
}
