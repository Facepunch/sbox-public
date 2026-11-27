using Borealis.Gpu;
using Borealis.Source2.Shaders;
using Sandbox.ShaderCompiler;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Borealis.Backend;

/// <summary>
/// Drawing new-tier materials (<see cref="SurfaceMaterial"/>): the pass's pipeline, sets 0 and 1 bound by name from this
/// context's attributes as classic draws bind them, the material's record at an offset pushed per draw. Forward and
/// alpha-tested forward in colour passes; depth (vertex shader only) or alpha-tested depth where there's no colour.
/// See docs/reference/borealis-slang-materials.md.
/// </summary>
internal sealed partial class BackendContext
{
	const string ForwardAlphaTestPass = ShaderPasses.Forward + "AlphaTest";
	const string ForwardTranslucentPass = ShaderPasses.Forward + "Translucent";

	/// <summary>The depth and shadow pass of a surface with a shadow of its own (<c>IShadowSurface</c>): depth dithered by its opacity.</summary>
	internal const string DepthTranslucentPass = ShaderPasses.Depth + "Translucent";

	static readonly StringToken LightmapCombo = new( "D_BAKED_LIGHTING_FROM_LIGHTMAP" );

	/// <summary><c>kMaterialOffsetPerPixel</c> in sbox/material.slang.</summary>
	internal const uint MaterialOffsetPerPixelId = 2;

	/// <summary><c>kSurfaceOpaqueFade</c> in sbox/surface_passes.slangh: the draw is of a fading object (<c>D_OPAQUE_FADE</c>).</summary>
	internal const uint OpaqueFadeId = 3;

	static readonly StringToken OpaqueFadeCombo = new( "D_OPAQUE_FADE" );

	/// <summary><c>kSurfaceSkybox</c> in sbox/surface_passes.slangh: the draw is in the 3D skybox's view (<c>D_SKYBOX</c>).</summary>
	internal const uint SkyboxId = 4;

	static readonly StringToken SkyboxCombo = new( "D_SKYBOX" );

	// A pass's skinned or lightmapped variant's name, without joining the strings every draw
	static string Named( string pass, string suffix ) => (pass, suffix) switch
	{
		(ShaderPasses.Depth, ShaderPasses.Skinned ) => ShaderPasses.Depth + ShaderPasses.Skinned,
		(ShaderPasses.Depth, ShaderPasses.Lightmapped ) => ShaderPasses.Depth + ShaderPasses.Lightmapped,
		(ShaderPasses.DepthAlphaTest, ShaderPasses.Skinned ) => ShaderPasses.DepthAlphaTest + ShaderPasses.Skinned,
		(ShaderPasses.DepthAlphaTest, ShaderPasses.Lightmapped ) => ShaderPasses.DepthAlphaTest + ShaderPasses.Lightmapped,
		(DepthTranslucentPass, ShaderPasses.Skinned ) => DepthTranslucentPass + ShaderPasses.Skinned,
		(DepthTranslucentPass, ShaderPasses.Lightmapped ) => DepthTranslucentPass + ShaderPasses.Lightmapped,
		(ShaderPasses.Forward, ShaderPasses.Skinned ) => ShaderPasses.Forward + ShaderPasses.Skinned,
		(ShaderPasses.Forward, ShaderPasses.Lightmapped ) => ShaderPasses.Forward + ShaderPasses.Lightmapped,
		(ForwardAlphaTestPass, ShaderPasses.Skinned ) => ForwardAlphaTestPass + ShaderPasses.Skinned,
		(ForwardAlphaTestPass, ShaderPasses.Lightmapped ) => ForwardAlphaTestPass + ShaderPasses.Lightmapped,
		(ForwardTranslucentPass, ShaderPasses.Skinned ) => ForwardTranslucentPass + ShaderPasses.Skinned,
		(ForwardTranslucentPass, ShaderPasses.Lightmapped ) => ForwardTranslucentPass + ShaderPasses.Lightmapped,
		_ => pass + suffix,
	};

	bool DrawSurface( SurfaceMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias, float slopeScaledDepthBias, bool stripPixelShader, in IndirectDraws indirect )
	{
		if ( mesh.Geometry is not IBackendGeometry source || !source.Use() ) return false;
		if ( (indirect.InstanceIds ?? InstanceIds?.Resource as BackendBuffer) is not { } instanceIds ) return false;
		if ( device.Surfaces.For( material ) is not { } program ) return false;
		if ( !EnsureScope() ) return false;

		// The targets are this scope's only once it's open
		var r = recording;
		var noColor = stripPixelShader || r.Color is null;
		var state = DrawState( material, noColor );
		string name;
		if ( noColor )
		{
			// A surface that writes no depth, or draws in the translucent layer, has depth and shadow passes only where it
			// casts a shadow of its own (IShadowSurface: glass's, dithered by its opacity); a converted one's are otherwise its
			// .shader's
			if ( !state.DepthWrite || state.Translucent )
			{
				if ( program.Pass( DepthTranslucentPass ) is null ) return false;
				name = DepthTranslucentPass;
			}
			else
			{
				name = state.AlphaTest ? ShaderPasses.DepthAlphaTest : ShaderPasses.Depth;
			}
		}
		else if ( shaderMode.Value != ForwardMode.Value )
		{
			return false;
		}
		else if ( state.DrawsTranslucent )
		{
			// Blended over the colour behind, or in the translucent layer: compiled translucent (opacity kept in alpha), as a
			// .shader's S_TRANSLUCENT is
			name = ForwardTranslucentPass;
		}
		else
		{
			name = state.AlphaTest && program.Pass( ForwardAlphaTestPass ) is not null ? ForwardAlphaTestPass : ShaderPasses.Forward;
		}

		// A skinned draw from the vertex cache reads its vertices there (today's D_CS_VERTEX_ANIMATION combo); a lightmapped
		// object's draws (D_BAKED_LIGHTING_FROM_LIGHTMAP) pass the mesh's lightmap uv on. One-weight skinned draws need neither:
		// the engine's vertex stage skins them from the transform buffer's bones
		var vertexCache = cacheDraw is not null;
		var pass = vertexCache ? program.Pass( Named( name, ShaderPasses.Skinned ) )
			: (LookupCombo( LightmapCombo ) is > 0 ? program.Pass( Named( name, ShaderPasses.Lightmapped ) ) : null) ?? program.Pass( name );

		if ( pass is null ) return false;

		var color = noColor ? Format.Unknown : FormatOfTarget( r.Color, r.Srgb );
		var depth = r.Depth is null ? Format.Unknown : BackendDevice.FormatOf( r.Depth );
		var geometry = GeometryFor( source, draw );

		// Opaque depth reads the positions-only vertex copy, when its vertex stage reads nothing else of the mesh's
		if ( !vertexCache && pass.Pass.Pixel is null && device.PositionStreams && geometry.Positions.IsValid && ReadsOnlyPositions( pass.Pass.Vertex, geometry.Layout ) ) geometry = geometry.PositionsOnly;
		var vertexConstants = SurfaceConstants( pass.Pass.Vertex, out var vertexKey );
		ulong pixelKey = 0;
		var pixelConstants = pass.Pass.Pixel is { } pixel ? SurfaceConstants( pixel, out pixelKey ) : null;
		// Opaque colour whose depth the layer before drew (DepthDrawnFirst): tested, not written again, keeping early depth testing
		var depthEqual = DepthDrawnFirst && BackendDevice.ColourAfterDepthDrawn && !noColor && state.DepthWrite && !state.DrawsTranslucent;
		var pipeline = program.Pipeline( pass, geometry.Layout, color, depth, (r.Color ?? r.Depth).Samples, vertexConstants, pixelConstants, vertexKey * 31 + pixelKey, state, opaqueFade: LookupCombo( OpaqueFadeCombo ) is > 0, depthEqual: depthEqual, specialized: program.Features( material ), normals: r.Normals is not null, tools: noColor ? ToolsPass.None : ToolsPass, stencil: OverlayStencil );
		if ( r.Pipeline != pipeline )
		{
			r.List.SetPipeline( pipeline );
			r.Pipeline = pipeline;
		}

		uncompressedTangentFrame = geometry.UncompressedTangentFrame;
		var record = device.Surfaces.Records.Offset( material, out drawRecords );

		// Both stages share the same view and attribute epoch.
		NextPassState( device.MaterialCache );
		boundRecords = default;
		BindSurface( pass, pass.Pass.Vertex, ShaderAbi.VertexSet );
		if ( pass.Pass.Pixel is not null ) BindSurface( pass, pass.Pass.Pixel, ShaderAbi.FragmentSet );
		if ( boundRecords.IsValid && boundRecords != drawRecords ) System.Threading.Interlocked.Increment( ref device.StaleRecordBindings );
		drawRecords = default;
		r.List.PushConstants( record );
		r.Binder = null;

		if ( noColor ) r.List.SetDepthBias( depthBias, slopeScaledDepthBias );
		else SurfaceProgram.ColorDepthBias( r.List, state );
		BackendDevice.SetStencilReference( r.List, depth, OverlayStencil );
		RecordDraw( r.List, geometry, instanceIds, draw, firstInstance, instances, indirect, material );
		return true;
	}

	/// <summary>
	/// A surface's render state for this draw: two-sided for a depth-only draw while the context draws two-sided
	/// (<see cref="TwoSided"/>: a shadow view's draws of an object casting from both faces).
	/// </summary>
	SurfaceRenderState DrawState( SurfaceMaterial material, bool noColor )
	{
		var state = material.RenderState;
		return noColor && TwoSided && !state.TwoSided ? state with { TwoSided = true } : state;
	}

	/// <summary>Whether a surface's vertex stage reads nothing of these vertices but their positions.</summary>
	static bool ReadsOnlyPositions( CompiledStage stage, VertexLayout layout )
	{
		foreach ( var input in stage.Inputs )
		{
			if ( input.Semantic != "POSITION" && layout.Find( SurfaceProgram.Semantic( input.Semantic ), input.SemanticIndex ) is not null ) return false;
		}

		return true;
	}

	/// <summary>
	/// The GPU-driven bucket a surface batch draws in (BackendContext.GpuDriven.cs), made when none fits: its GPU-driven
	/// pass for the mode, alpha test, blending and lightmap combo, under the batch's object attributes. Null, so it draws
	/// the classic way, when the surface has no GPU-driven passes or the mode isn't one a surface draws. Blended and
	/// overlay surfaces come here only where the scene draws such materials GPU-driven (a render order's static overlays,
	/// glass), as .shader materials do; a surface that writes no depth, or draws in the translucent layer, has no depth
	/// passes of its own. A skinned batch (<paramref name="skinned"/>) draws in buckets of its own, under its own object
	/// attributes (<paramref name="own"/>), with the pass that reads the vertex cache when it does (<paramref name="vertexCache"/>).
	/// </summary>
	GpuDrivenBucket SurfaceBucket( SurfaceMaterial material, StringToken shaderMode, bool noColor, ShaderParameters attributes, long key, OverlayStencil stencil, bool skinned = false, bool vertexCache = false, ShaderParameters own = null )
	{
		var state = DrawState( material, noColor );
		if ( noColor && (!state.DepthWrite || state.Translucent) ) return null;
		if ( !noColor && shaderMode.Value != ForwardMode.Value ) return null;
		if ( device.Surfaces.For( material ) is not { } program ) return null;
		if ( program.GpuDrivenPass( noColor, state.AlphaTest, LookupCombo( LightmapCombo ) is > 0, state.DrawsTranslucent, vertexCache ) is not { } pass ) return null;

		// Buckets split by the materials' features where a pixel stage reads them, each drawn with a pipeline specialised for them
		var depthOnly = pass.Pass.Pixel is null;
		var features = depthOnly ? null : program.Features( material );
		for ( int i = openBucket; i < gpuDrivenBucketCount; i++ )
		{
			var bucket = gpuDrivenBuckets[i];
			if ( ReferenceEquals( bucket.Surface, pass ) && bucket.AttributesKey == key && bucket.NoColor == noColor && bucket.SurfaceState == state && ReferenceEquals( bucket.SurfaceFeatures, features )
				&& bucket.Stencil == stencil && bucket.Skinned == skinned && ReferenceEquals( bucket.Own, own ) ) return bucket;
		}

		if ( gpuDrivenBucketCount == gpuDrivenBuckets.Count ) gpuDrivenBuckets.Add( new GpuDrivenBucket() );
		var made = gpuDrivenBuckets[gpuDrivenBucketCount++];
		made.Variant = null;
		made.Material = null;
		made.Classic = null;
		made.Surface = pass;
		made.SurfaceProgram = program;
		made.SurfacePositionsOnly = !program.Displaces;
		made.Attributes = attributes;
		made.AttributesKey = key;
		made.DepthOnly = depthOnly;
		made.NoColor = noColor;
		made.Stencil = stencil;
		made.SurfaceState = state;
		made.SurfaceFeatures = features;
		made.PixelMaterial = 0;
		made.Batches.Clear();
		made.Commands = 0;
		made.Ids = false;
		made.Skinned = skinned;
		made.Own = own;
		made.Fragments = 0;
		made.MeshletChunks32 = made.MeshletChunks16 = 0;
		return made;
	}

	/// <summary>
	/// A draw's vertex and pixel material records: a surface's one record (its <c>[Material]</c> struct) for both stages,
	/// or a variant's per stage.
	/// </summary>
	(uint Vertex, uint Pixel) Records( GpuDrivenTables tables, GpuDrivenBucket bucket, GpuMaterial material, bool uncompressedTangentFrame ) =>
		bucket.Surface is not null ? Records( tables, bucket, material, uncompressedTangentFrame, 0, 0 )
		: Records( tables, bucket, material, uncompressedTangentFrame, bucket.Variant.Vertex is { } vertex ? AttributeHash( vertex ) : 0, (!bucket.DepthOnly || bucket.Ids && !bucket.IdsShader) && bucket.Variant.Pixel is { } pixel ? AttributeHash( pixel ) : 0 );

	/// <summary>The same with the bucket's stages' attribute hashes (<see cref="AttributeHash"/>) already worked out.</summary>
	(uint Vertex, uint Pixel) Records( GpuDrivenTables tables, GpuDrivenBucket bucket, GpuMaterial material, bool uncompressedTangentFrame, int vertexAttributes, int pixelAttributes )
	{
		if ( bucket.Surface is not null )
		{
			var surface = (SurfaceMaterial)material;
			var key = new GpuDrivenTables.MaterialRecordKey( surface, surface.Version, null, false, 0 );
			var size = (Math.Max( surface.RecordSize, 4 ) + 15) & ~15;
			var record = tables.Material( key, size, surface, static ( s, bytes ) =>
			{
				bytes.Clear();
				s.WriteRecord( bytes, static ( texture, srgb ) => texture.ViewIndex( srgb, TextureViewDimension.Default ) );
			} );
			return (record, record);
		}

		var classic = (BackendMaterial)material;
		return (MaterialRecord( tables, classic, bucket.Variant.Vertex, uncompressedTangentFrame, vertexAttributes ), bucket.DepthOnly && !(bucket.Ids && !bucket.IdsShader) ? 0 : MaterialRecord( tables, classic, bucket.Variant.Pixel, uncompressedTangentFrame, pixelAttributes ));
	}

	/// <summary>
	/// The GPU-driven run being drawn is the visibility-driven forward pass's lists: only triangles the view's ids prepass
	/// drew, each with its depth already written.
	/// </summary>
	bool surfaceDepthFromIds;

	/// <summary>
	/// A surface bucket's pipeline (vertices pulled, so no vertex layout) and sets 0 and 1, bound by name from this
	/// context's attributes as for its classic draws; with <paramref name="ids"/>, for the visibility prepass's targets.
	/// </summary>
	void BindSurfaceGpuDriven( GpuDrivenBucket bucket, Format color, Format depth, int samples, bool ids )
	{
		var r = recording;
		var pass = bucket.Surface;

		// Opaque colour in the visibility-driven forward pass, or in a layer whose depth the layer before drew (DepthDrawnFirst):
		// the prepass wrote these triangles' depth, so colour doesn't write it again; alpha-tested buckets, whose ids the prepass drew with their own vertex stage, test it equal, keeping early
		// depth testing
		var depthEqual = (surfaceDepthFromIds || (DepthDrawnFirst && BackendDevice.ColourAfterDepthDrawn)) && !ids && color != Format.Unknown && !bucket.NoColor && !bucket.IdsShader
			&& bucket.SurfaceState.DepthWrite && !bucket.SurfaceState.DrawsTranslucent;
		var vertexConstants = SurfaceConstants( pass.Pass.Vertex, out var vertexKey, gpuDriven: true );
		ulong pixelKey = 0;
		var pixelConstants = pass.Pass.Pixel is { } pixel ? SurfaceConstants( pixel, out pixelKey, gpuDriven: true ) : null;
		var pipeline = bucket.SurfaceProgram.Pipeline( pass, device.PulledLayout, color, depth, samples, vertexConstants, pixelConstants, vertexKey * 31 + pixelKey, bucket.SurfaceState, ids, LookupCombo( OpaqueFadeCombo ) is > 0, depthEqual, bucket.SurfaceFeatures, r.Normals is not null, bucket.NoColor || color == Format.Unknown ? ToolsPass.None : ToolsPass, bucket.Stencil );
		if ( r.Pipeline != pipeline )
		{
			r.List.SetPipeline( pipeline );
			r.Pipeline = pipeline;
		}

		NextPassState( device.MaterialCache );
		BindSurface( pass, pass.Pass.Vertex, ShaderAbi.VertexSet );
		if ( pass.Pass.Pixel is not null ) BindSurface( pass, pass.Pass.Pixel, ShaderAbi.FragmentSet );
		r.Binder = null;

		// Colour with the surface's own depth bias (a static overlay's); depth and ids passes set the pass's after binding
		if ( !ids && color != Format.Unknown && !bucket.NoColor ) SurfaceProgram.ColorDepthBias( r.List, bucket.SurfaceState );
		BackendDevice.SetStencilReference( r.List, depth, bucket.Stencil );
	}

	/// <summary>
	/// The specialization constants a stage's draws most likely have, as <see cref="SurfaceConstants"/> makes them: each slot's
	/// value as draws last looked it up (<see cref="SurfaceShaders.SpecializationValues"/>), else its default; not in the 3D
	/// skybox, not fading. What a program's parts are made ahead with (<see cref="SurfaceProgram.PrepareParts"/>).
	/// </summary>
	internal static SpecializationConstant[] LikelySurfaceConstants( CompiledStage stage, bool perPixel, IReadOnlyDictionary<string, int> seen )
	{
		var constants = new SpecializationConstant[stage.Specializations.Count + 3];
		for ( int i = 0; i < stage.Specializations.Count; i++ )
		{
			var slot = stage.Specializations[i];
			var value = seen.TryGetValue( slot.Attribute, out var looked ) ? looked : (int)(slot.Annotations?.Default is { Length: > 0 } d ? d[0] : 0);
			constants[i] = new SpecializationConstant( (uint)slot.Id, (uint)value );
		}

		constants[^3] = new SpecializationConstant( SkyboxId, 0 );
		constants[^2] = new SpecializationConstant( MaterialOffsetPerPixelId, perPixel ? 1u : 0u );
		constants[^1] = new SpecializationConstant( OpaqueFadeId, 0 );
		return constants;
	}

	/// <summary>
	/// A stage's specialization constants (<c>SpecializedConstant()</c> globals: <c>ShadowFilterQuality</c>,
	/// <c>DDGI_Enabled</c>, <c>ToolsVisMode</c>), each from its attribute, else the dynamic combo of that name, else its
	/// default, as classic variants take them;
	/// <paramref name="key"/> tells their values apart for the pipeline cache.
	/// </summary>
	SpecializationConstant[] SurfaceConstants( CompiledStage stage, out ulong key, bool gpuDriven = false )
	{
		// Where the pixel stage reads the material record's offset (sbox/material.slang): passed on per pixel, or the draw's.
		// A GPU-driven draw has no push constant of its own, so its pixels always take it from the vertex stage
		var perPixel = gpuDriven || device.Surfaces.MaterialOffsetPerPixel;
		var info = StageBindings( stage );

		// What the same attributes, object combos and (where an object can set a slot's attribute) object attributes gave
		// before: a bucket binds its stages per run, each looking up every slot
		var version = attributes.Version;
		var parent = attributes.Parent;
		var layer = info.SpecializationsLayered( stage, device ) ? layerEpoch : 0;
		var memo = info.Memo;
		for ( int m = 0; m < memo.Length; m++ )
		{
			ref var seen = ref memo[m];
			if ( seen.Constants is not null && seen.Version == version && ReferenceEquals( seen.Parent, parent ) && ReferenceEquals( seen.Combos, layerCombos ) && seen.Layer == layer && seen.PerPixel == perPixel )
			{
				key = seen.Key;
				return seen.Constants;
			}
		}

		var made = SurfaceConstantsLookedUp( stage, info, perPixel, out key );
		memo[info.NextMemo] = new SurfaceStageBindings.Remembered( version, parent, layerCombos, layer, perPixel, made, key );
		info.NextMemo = (info.NextMemo + 1) % memo.Length;
		return made;
	}

	SpecializationConstant[] SurfaceConstantsLookedUp( CompiledStage stage, SurfaceStageBindings info, bool perPixel, out ulong key )
	{
		var fade = LookupCombo( OpaqueFadeCombo ) is > 0;
		var skybox = LookupCombo( SkyboxCombo ) is > 0;
		key = (perPixel ? 1u : 2u) + (fade ? 4u : 0u) + (skybox ? 8u : 0u);
		Span<int> values = stackalloc int[stage.Specializations.Count];
		for ( int i = 0; i < values.Length; i++ )
		{
			var slot = stage.Specializations[i];
			// An attribute's value, else a dynamic combo of that name (a converted shader's D_GRID), else the default
			values[i] = Integer( Lookup( info.SpecializationTokens[i] ) ) ?? LookupCombo( info.SpecializationTokens[i] ) ?? (int)(slot.Annotations?.Default is { Length: > 0 } d ? d[0] : 0);
			key = key * 1000003 + (uint)values[i] + 1;
		}

		// The same values as the last bind (nearly always): the same array, so a bind doesn't allocate; values this stage
		// had before (draws that alternate fading) the array made for them then
		if ( info.LastConstants is not null && info.LastConstantsKey == key ) return info.LastConstants;
		if ( info.Made.TryGetValue( key, out var made ) )
		{
			(info.LastConstants, info.LastConstantsKey) = (made, key);
			return made;
		}

		var constants = new SpecializationConstant[values.Length + 3];
		constants[^3] = new SpecializationConstant( SkyboxId, skybox ? 1u : 0u );
		constants[^2] = new SpecializationConstant( MaterialOffsetPerPixelId, perPixel ? 1u : 0u );
		constants[^1] = new SpecializationConstant( OpaqueFadeId, fade ? 1u : 0u );
		for ( int i = 0; i < values.Length; i++ ) constants[i] = new SpecializationConstant( (uint)stage.Specializations[i].Id, (uint)values[i] );
		(info.LastConstants, info.LastConstantsKey) = (constants, key);
		if ( info.Made.Count < 16 ) info.Made[key] = constants;

		// What programs compiled later make their parts ahead with
		for ( int i = 0; i < values.Length; i++ ) device.Surfaces.SpecializationLookedUp( stage.Specializations[i].Attribute, values[i] );
		return constants;
	}

	/// <summary>
	/// What a stage's binds look up, made once per stage for this context: each binding's attribute token and sampler,
	/// each specialization slot's token, and the last specialization constants. A surface pass binds per bucket or draw.
	/// </summary>
	sealed class SurfaceStageBindings
	{
		public readonly StringToken[] Tokens;
		public readonly SamplerHandle[] Samplers;
		public readonly StringToken[] SpecializationTokens;
		public SpecializationConstant[] LastConstants;
		public ulong LastConstantsKey;

		/// <summary>The constants made for each set of values, a few of them.</summary>
		public readonly Dictionary<ulong, SpecializationConstant[]> Made = new();

		/// <summary>What the specialization slots were looked up as, by what they were looked up from: the last few.</summary>
		public readonly Remembered[] Memo = new Remembered[4];
		public int NextMemo;

		public readonly record struct Remembered( int Version, BackendParameters Parent, string Combos, long Layer, bool PerPixel, SpecializationConstant[] Constants, ulong Key );

		bool specializationsLayered;
		int specializationsLayeredVersion = int.MinValue;

		/// <summary>
		/// Whether an object's attributes can set a specialization slot's attribute (without material objects, which keep the
		/// names and combos objects set, assumed so).
		/// </summary>
		public bool SpecializationsLayered( CompiledStage stage, BackendDevice device )
		{
			if ( !device.MaterialObjects ) return true;
			if ( SpecializationTokens.Length == 0 ) return false;
			var cache = device.MaterialCache;
			var version = cache.LayerNames;
			if ( specializationsLayeredVersion == version ) return specializationsLayered;

			var layered = false;
			foreach ( var token in SpecializationTokens ) layered |= cache.IsLayerName( token.Value );
			(specializationsLayered, specializationsLayeredVersion) = (layered, version);
			return layered;
		}

		/// <summary>The pass state (the context's pass epoch) and list <see cref="Bound"/> was resolved in.</summary>
		public long Epoch = -1;
		public CommandList List;

		/// <summary>By binding, what it bound in that pass state, where <see cref="Resolved"/>.</summary>
		public readonly Binding[] Bound;
		public readonly bool[] Resolved;

		/// <summary>The <c>UncompressedTangentFrame</c> the bound <c>$Globals</c> were written with.</summary>
		public bool Tangent;

		/// <summary>By binding, whether an object's attributes can set what it reads, as of the cache's layer names version.</summary>
		bool[] objectNames;
		int objectNamesVersion = -1;

		public SurfaceStageBindings( CompiledStage stage, ShaderDefaults defaults )
		{
			Tokens = new StringToken[stage.Bindings.Count];
			Samplers = new SamplerHandle[stage.Bindings.Count];
			for ( int i = 0; i < Tokens.Length; i++ )
			{
				var b = stage.Bindings[i];
				Tokens[i] = Token( b.Name );
				if ( b.Kind == BindingKind.Sampler ) Samplers[i] = defaults.Sampler( SurfaceSampler( b.Annotations ) );
			}

			SpecializationTokens = new StringToken[stage.Specializations.Count];
			for ( int i = 0; i < SpecializationTokens.Length; i++ ) SpecializationTokens[i] = Token( stage.Specializations[i].Attribute );

			Bound = new Binding[stage.Bindings.Count];
			Resolved = new bool[stage.Bindings.Count];
		}

		/// <summary>By binding, whether an object's attributes (<see cref="MaterialObjectCache.IsLayerName"/>) can set what it reads.</summary>
		public bool[] ObjectNames( CompiledStage stage, MaterialObjectCache cache )
		{
			var version = cache.LayerNames;
			if ( objectNames is not null && objectNamesVersion == version ) return objectNames;

			var names = new bool[Tokens.Length];
			for ( int i = 0; i < names.Length; i++ )
			{
				names[i] = stage.Bindings[i].Kind switch
				{
					BindingKind.Sampler or BindingKind.MaterialRecords or BindingKind.StorageTexture => false,
					BindingKind.Globals => stage.Globals is { } globals && AnyLayerName( GlobalLayouts.GetValue( globals, static g => new SurfaceGlobalsLayout( g ) ).Tokens, cache ),
					_ => cache.IsLayerName( Tokens[i].Value ),
				};
			}

			(objectNames, objectNamesVersion) = (names, version);
			return names;
		}

		static bool AnyLayerName( StringToken[] tokens, MaterialObjectCache cache )
		{
			foreach ( var token in tokens )
			{
				if ( cache.IsLayerName( token.Value ) ) return true;
			}

			return false;
		}
	}

	readonly System.Runtime.CompilerServices.ConditionalWeakTable<CompiledStage, SurfaceStageBindings> surfaceStageBindings = new();

	SurfaceStageBindings StageBindings( CompiledStage stage )
	{
		if ( surfaceStageBindings.TryGetValue( stage, out var info ) ) return info;
		info = new SurfaceStageBindings( stage, device.Defaults );
		surfaceStageBindings.AddOrUpdate( stage, info );
		return info;
	}

	/// <summary>
	/// One stage's set: every binding its SPIR-V uses, by the name the engine binds it by. What the pass state decides is
	/// resolved once per pass state (<see cref="NextPassState"/>: the attributes changed or a scope opened), and only what an
	/// object's attributes can set is looked up again for each draw, so draws of one stage write the same set.
	/// </summary>
	// The buffer holding the record of the surface draw being bound (Offset), or none outside one; and the records buffer
	// BindSurface last bound, which must be it
	BufferHandle drawRecords, boundRecords;

	// Up to 64 bindings on the stack, each written before it's read: not cleared first, a bind at a time
	[System.Runtime.CompilerServices.SkipLocalsInit]
	void BindSurface( SurfaceProgram.PassObjects pass, CompiledStage stage, int set )
	{
		if ( !pass.Sets[set].IsValid ) return;
		var info = StageBindings( stage );
		var list = recording.List;
		if ( !device.SurfacePassBindings ) info.Epoch = -1;
		if ( info.Epoch != passEpoch || !ReferenceEquals( info.List, list ) )
		{
			(info.Epoch, info.List) = (passEpoch, list);
			info.Resolved.AsSpan().Clear();
		}

		var perObject = layerEpoch != 0 ? info.ObjectNames( stage, device.MaterialCache ) : null;
		Span<Binding> bindings = stackalloc Binding[64];
		var count = 0;
		for ( int i = 0; i < stage.Bindings.Count; i++ )
		{
			var b = stage.Bindings[i];
			if ( b.Set != set ) continue;
			if ( count == bindings.Length ) throw new InvalidOperationException( $"{stage.Entry} binds more than {bindings.Length} resources in set {set}" );

			// The records buffer is bound as the draw's record was written in it, never as a pass state resolved it: if writing
			// it grew the records into a new buffer, its offset is past the end of the one bound before, which the draw then read
			// beyond (a GPU page fault where that buffer ended its memory block)
			if ( b.Kind == BindingKind.MaterialRecords )
			{
				boundRecords = drawRecords.IsValid ? drawRecords : device.Surfaces.Records.Buffer;
				bindings[count++] = Binding.Storage( b.Binding, boundRecords );
				continue;
			}

			if ( perObject is not null && perObject[i] )
			{
				bindings[count++] = SurfaceBinding( stage, b, info.Tokens[i], info.Samplers[i] );
				continue;
			}

			var globals = b.Kind == BindingKind.Globals;
			if ( !info.Resolved[i] || (globals && info.Tangent != uncompressedTangentFrame) )
			{
				info.Bound[i] = SurfaceBinding( stage, b, info.Tokens[i], info.Samplers[i] );
				info.Resolved[i] = true;
				if ( globals ) info.Tangent = uncompressedTangentFrame;
			}

			bindings[count++] = info.Bound[i];
		}

		list.SetBindings( set, pass.Sets[set], bindings[..count] );
	}

	Binding SurfaceBinding( CompiledStage stage, ShaderBinding b, StringToken token, SamplerHandle sampler )
	{
		var defaults = device.Defaults;
		switch ( b.Kind )
		{
			case BindingKind.Globals:
				return SurfaceGlobals( stage.Globals, b.Binding );

			case BindingKind.MaterialRecords:
				return Binding.Storage( b.Binding, device.Surfaces.Records.Buffer );

			case BindingKind.ConstantBuffer:
				{
					var data = Lookup( token ) as byte[];
					var size = (Math.Max( data?.Length ?? 0, b.Size ) + 15) & ~15;
					if ( size == 0 ) return Binding.Uniform( b.Binding, defaults.Uniform, 0, ShaderDefaults.UniformSize );
					return SurfaceConstantBuffer( token, data, size, b.Binding );
				}

			case BindingKind.Buffer:
				return Lookup( token ) is GpuBuffer { Resource: BackendBuffer { Live: true } buffer } ? Binding.Storage( b.Binding, buffer.Handle ) : Binding.Storage( b.Binding, defaults.Storage );

			case BindingKind.Sampler:
				return Binding.SamplerState( b.Binding, sampler );

			case BindingKind.StorageTexture:
				return Binding.StorageTexture( b.Binding, defaults.StorageTexture( Dimension( b.Shape ) ) );

			default:
				{
					var expected = b.Shape switch
					{
						TextureShape.Cube => TextureDimension.TextureCube,
						TextureShape.CubeArray => TextureDimension.TextureCubeArray,
						TextureShape.Texture3D => TextureDimension.Texture3D,
						TextureShape.Texture2DArray => TextureDimension.Texture2DArray,
						_ => TextureDimension.Texture2D,
					};

					if ( Lookup( token ) is not BackendTexture { IsValid: true } texture || texture.Dimension != expected || texture.Samples > 1 || SampledLayoutOf( texture ) != ImageLayout.ShaderRead )
						return Binding.Sampled( b.Binding, defaults.Texture( Dimension( b.Shape ) ) );

					var srgb = (b.Annotations?.SrgbRead ?? false) && Gpu.InfoOf( texture.Handle ).Format.HasSrgb();
					return Binding.Sampled( b.Binding, texture.Handle, srgb );
				}
		}
	}

	/// <summary>
	/// A constant buffer an attribute holds, in upload memory: the same bytes as its last upload in this list bind that
	/// again, so stages reading it in one list bind the same set.
	/// </summary>
	Binding SurfaceConstantBuffer( StringToken token, byte[] data, int size, int binding )
	{
		var session = recording.Started;
		var length = data?.Length ?? 0;
		if ( !constantBuffersWritten.TryGetValue( (token.Value, size), out var written ) ) constantBuffersWritten[(token.Value, size)] = written = new ConstantBufferWritten( size );
		if ( written.Session == session && written.Length == length && (data is null || data.AsSpan().SequenceEqual( written.Bytes.AsSpan( 0, length ) )) ) return Binding.Uniform( binding, written.Buffer, written.Offset, size );

		var allocation = List.AllocateUpload( size, Gpu.Capabilities.UniformAlignment );
		allocation.Data[..size].Clear();
		data?.CopyTo( allocation.Data );
		data?.CopyTo( written.Bytes, 0 );
		(written.Session, written.Length, written.Buffer, written.Offset) = (session, length, allocation.Buffer, allocation.Offset);
		return Binding.Uniform( binding, allocation.Buffer, allocation.Offset, size );
	}

	/// <summary>What <see cref="SurfaceConstantBuffer"/> last uploaded for an attribute at a size: in which list, where, and its bytes.</summary>
	sealed class ConstantBufferWritten( int size )
	{
		public readonly byte[] Bytes = new byte[size];
		public long Session = -1;
		public int Length;
		public BufferHandle Buffer;
		public long Offset;
	}

	readonly Dictionary<(uint Token, int Size), ConstantBufferWritten> constantBuffersWritten = new();

	internal static SpirvImageDimension Dimension( TextureShape shape ) => shape switch
	{
		TextureShape.Cube => SpirvImageDimension.Cube,
		TextureShape.CubeArray => SpirvImageDimension.CubeArray,
		TextureShape.Texture3D => SpirvImageDimension.Texture3D,
		TextureShape.Texture2DArray => SpirvImageDimension.Texture2DArray,
		_ => SpirvImageDimension.Texture2D,
	};

	/// <summary>
	/// The stage's <c>$Globals</c>: each member with an <c>Attribute</c> annotation from that attribute, else its annotated
	/// default, in upload memory. <c>UncompressedTangentFrame</c> comes from the draw's vertex layout, as for classic draws.
	/// The members the pass state sets are looked up once per pass state, and only those an object's attributes can set
	/// again for each bind; a block the same as the last one this layout wrote in the list binds that upload again, so draws
	/// of one stage under different objects bind the same set.
	/// </summary>
	Binding SurfaceGlobals( GlobalConstants globals, int binding )
	{
		var size = Math.Max( globals?.Size ?? 16, 16 );
		if ( globals is null )
		{
			var zeros = List.AllocateUpload( size, Gpu.Capabilities.UniformAlignment );
			zeros.Data[..size].Clear();
			return Binding.Uniform( binding, zeros.Buffer, zeros.Offset, size );
		}

		var layout = GlobalLayouts.GetValue( globals, static g => new SurfaceGlobalsLayout( g ) );
		if ( !globalsWritten.TryGetValue( layout, out var written ) ) globalsWritten[layout] = written = new GlobalsWritten( size );
		var session = recording.Started;
		var cache = device.MaterialCache;
		var layerNames = device.MaterialObjects ? cache.LayerNames : -2;
		if ( written.Layered is null || written.LayerNames != layerNames )
		{
			// Which members an object's attributes can set, as of the cache's layer names (without material objects, which
			// keep them, any)
			written.Layered ??= new bool[layout.Bound.Length];
			written.AnyLayered = false;
			for ( int a = 0; a < layout.Bound.Length; a++ )
			{
				written.Layered[a] = globals.Members[layout.Bound[a]].Attribute != "UncompressedTangentFrame" && (layerNames == -2 || cache.IsLayerName( layout.Tokens[a].Value ));
				written.AnyLayered |= written.Layered[a];
			}

			(written.LayerNames, written.PassEpoch) = (layerNames, -1);
		}

		Span<float> value = stackalloc float[16];
		if ( written.PassEpoch != passEpoch || written.PassSession != session || written.Tangent != uncompressedTangentFrame )
		{
			// What the pass state sets: the defaults, then the members bound by an attribute only it can set. The core includes
			// declare hundreds of members, so the defaults are laid out once per stage and only the bound members looked up
			var block = written.PassBlock.AsSpan( 0, size );
			layout.Defaults.AsSpan( 0, Math.Min( layout.Defaults.Length, size ) ).CopyTo( block );
			if ( layout.Defaults.Length < size ) block[layout.Defaults.Length..].Clear();
			for ( int a = 0; a < layout.Bound.Length; a++ )
			{
				if ( written.Layered[a] ) continue;
				var member = globals.Members[layout.Bound[a]];
				int count;
				if ( member.Attribute == "UncompressedTangentFrame" )
				{
					value[0] = uncompressedTangentFrame ? 1 : 0;
					count = 1;
				}
				else
				{
					count = Components( attributes.Get( layout.Tokens[a] ), value );
				}

				if ( count > 0 ) SurfaceGlobalsLayout.Write( block, member, value[..count] );
			}

			(written.PassEpoch, written.PassSession, written.Tangent) = (passEpoch, session, uncompressedTangentFrame);
		}

		// The members an object's attributes can set, over the pass state's block
		var data = written.PassBlock.AsSpan( 0, size );
		if ( written.AnyLayered )
		{
			data = written.Scratch.AsSpan( 0, size );
			written.PassBlock.AsSpan( 0, size ).CopyTo( data );
			for ( int a = 0; a < layout.Bound.Length; a++ )
			{
				if ( !written.Layered[a] ) continue;
				var count = Components( Lookup( layout.Tokens[a] ), value );
				if ( count > 0 ) SurfaceGlobalsLayout.Write( data, globals.Members[layout.Bound[a]], value[..count] );
			}
		}

		// The same block as one of this layout's last few uploads in this list (objects lit by lightmap and by probe take
		// turns): bound again
		for ( int k = 0; k < GlobalsWritten.Kept; k++ )
		{
			if ( written.Sessions[k] == session && data.SequenceEqual( written.Uploaded[k].AsSpan( 0, size ) ) ) return Binding.Uniform( binding, written.Buffers[k], written.Offsets[k], size );
		}

		var allocation = List.AllocateUpload( size, Gpu.Capabilities.UniformAlignment );
		data.CopyTo( allocation.Data );
		var next = written.Next;
		written.Next = (next + 1) % GlobalsWritten.Kept;
		data.CopyTo( written.Uploaded[next] );
		(written.Sessions[next], written.Buffers[next], written.Offsets[next]) = (session, allocation.Buffer, allocation.Offset);
		return Binding.Uniform( binding, allocation.Buffer, allocation.Offset, size );
	}

	/// <summary>
	/// What <see cref="SurfaceGlobals"/> last wrote for a <c>$Globals</c> layout: the block as the pass state sets it, and
	/// its last few uploads in this context's lists and what they hold.
	/// </summary>
	sealed class GlobalsWritten( int size )
	{
		public const int Kept = 4;
		public readonly byte[] PassBlock = new byte[size], Scratch = new byte[size];
		public bool[] Layered;
		public int LayerNames = -1;
		public bool AnyLayered;
		public long PassEpoch = -1, PassSession = -1;
		public bool Tangent;

		/// <summary>By upload kept: the list's session it's in, where, and what it holds; the next to replace.</summary>
		public readonly long[] Sessions = [-1, -1, -1, -1];
		public readonly BufferHandle[] Buffers = new BufferHandle[Kept];
		public readonly long[] Offsets = new long[Kept];
		public readonly byte[][] Uploaded = [new byte[size], new byte[size], new byte[size], new byte[size]];
		public int Next;
	}

	readonly Dictionary<SurfaceGlobalsLayout, GlobalsWritten> globalsWritten = new( ReferenceEqualityComparer.Instance );

	internal static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GlobalConstants, SurfaceGlobalsLayout> GlobalLayouts = new();

	/// <summary>
	/// A stage's <c>$Globals</c> laid out once: every member's annotated default, and the members an attribute sets (an
	/// <c>Attribute</c> annotation, as vfx binds them; the rest are a material's values, which a surface's record holds).
	/// </summary>
	internal sealed class SurfaceGlobalsLayout
	{
		public readonly byte[] Defaults;
		public readonly int[] Bound;
		public readonly StringToken[] Tokens;

		/// <summary>Per bound member, the <see cref="AttributeIds"/> id of its attribute.</summary>
		public readonly int[] Ids;

		public SurfaceGlobalsLayout( GlobalConstants globals )
		{
			Defaults = new byte[Math.Max( globals.Size, 16 )];
			var bound = new List<int>();
			for ( int m = 0; m < globals.Members.Count; m++ )
			{
				var member = globals.Members[m];
				if ( member.Annotations?.Default is { } defaults ) Write( Defaults, member, defaults.AsSpan( 0, Math.Min( defaults.Length, 4 ) ) );
				if ( member.Annotations?.Attribute is not null || member.Attribute == "UncompressedTangentFrame" ) bound.Add( m );
			}

			Bound = [.. bound];
			Tokens = new StringToken[Bound.Length];
			Ids = new int[Bound.Length];
			for ( int i = 0; i < Bound.Length; i++ )
			{
				Tokens[i] = Token( globals.Members[Bound[i]].Attribute );
				Ids[i] = AttributeIds.Of( globals.Members[Bound[i]].Attribute );
			}
		}

		public static void Write( Span<byte> data, GlobalMember member, ReadOnlySpan<float> value )
		{
			for ( int i = 0; i < member.Components && i < value.Length && member.Offset + i * 4 + 4 <= data.Length; i++ )
			{
				var target = data[(member.Offset + i * 4)..];
				switch ( member.Kind )
				{
					case ScalarKind.Float:
						BinaryPrimitives.WriteSingleLittleEndian( target, value[i] );
						break;
					case ScalarKind.Bool:
						BinaryPrimitives.WriteInt32LittleEndian( target, value[i] != 0 ? 1 : 0 );
						break;
					default:
						BinaryPrimitives.WriteInt32LittleEndian( target, (int)MathF.Round( value[i] ) );
						break;
				}
			}
		}
	}

	/// <summary>An attribute's value as up to sixteen floats (a matrix), as <see cref="ScratchFloats"/> reads it, without allocating; 0 for none.</summary>
	static int Components( object attribute, Span<float> into )
	{
		switch ( attribute )
		{
			case int i: into[0] = i; return 1;
			case uint u: into[0] = u; return 1;
			case float f: into[0] = f; return 1;
			case bool b: into[0] = b ? 1 : 0; return 1;
			case Vector2 v: into[0] = v.x; into[1] = v.y; return 2;
			case Vector3 v: into[0] = v.x; into[1] = v.y; into[2] = v.z; return 3;
			case Vector4 v: into[0] = v.x; into[1] = v.y; into[2] = v.z; into[3] = v.w; return 4;
			case Color c: into[0] = c.r; into[1] = c.g; into[2] = c.b; into[3] = c.a; return 4;
			case Vector2Int v: into[0] = v.x; into[1] = v.y; return 2;
			case Matrix m:
				if ( into.Length < 16 ) return 0;
				into[0] = m.M11; into[1] = m.M12; into[2] = m.M13; into[3] = m.M14;
				into[4] = m.M21; into[5] = m.M22; into[6] = m.M23; into[7] = m.M24;
				into[8] = m.M31; into[9] = m.M32; into[10] = m.M33; into[11] = m.M34;
				into[12] = m.M41; into[13] = m.M42; into[14] = m.M43; into[15] = m.M44;
				return 16;
			default: return 0;
		}
	}

	/// <summary>
	/// A sampler from vfx's sampler-state annotations, as classic shaders' sampler states make them: <c>Filter</c>
	/// (ANISOTROPIC, BILINEAR, TRILINEAR, POINT or a D3D filter name, COMPARISON_ for comparison), <c>AddressU/V/W</c>,
	/// <c>MaxAniso</c>, <c>ComparisonFunc</c>. Without a <c>Filter</c> it's native's default sampler state
	/// (<c>DefaultSamplerStateDesc</c>): anisotropic, wrapped. A filter chosen by a feature
	/// (<c>F_TEXTURE_FILTERING == 0 ? ANISOTROPIC : ...</c>) takes the feature's value from <paramref name="values"/>, 0
	/// where it doesn't have it (a feature's default), as vfx evaluates annotations with the material's features.
	/// </summary>
	internal static SamplerInfo SurfaceSampler( Annotations annotations, Func<string, int?> values = null )
	{
		var sampler = SamplerInfo.Linear( AddressMode.Wrap, DefaultAnisotropy );
		if ( annotations is null ) return sampler;
		if ( annotations.MaxAniso > 0 ) sampler.MaxAnisotropy = annotations.MaxAniso;

		if ( annotations.Filter is { } filter )
		{
			var name = AnnotationExpression.Choose( filter, values ).ToUpperInvariant();
			var comparison = name.StartsWith( "COMPARISON_", StringComparison.Ordinal );
			if ( comparison ) name = name["COMPARISON_".Length..];

			// MaxAniso only counts with an anisotropic filter
			if ( name is not ("ANISOTROPIC" or "ANISO") ) sampler.MaxAnisotropy = 1;

			switch ( name )
			{
				case "ANISOTROPIC" or "ANISO":
					break;
				case "BILINEAR":
					sampler.MipFilter = Filter.Nearest;
					break;
				case "TRILINEAR":
					break;
				case "POINT" or "NEAREST":
					sampler.MinFilter = sampler.MagFilter = sampler.MipFilter = Filter.Nearest;
					break;
				default:
					// MIN_<f>_MAG_<f>_MIP_<f>, MIN_MAG_<f>_MIP_<f>, MIN_MAG_MIP_<f>
					var min = Part( name, "MIN_" );
					var mag = Part( name, "MAG_" );
					var mip = Part( name, "MIP_" );
					if ( name.StartsWith( "MIN_MAG_MIP_", StringComparison.Ordinal ) ) min = mag = mip;
					else if ( name.StartsWith( "MIN_MAG_", StringComparison.Ordinal ) ) min = mag;
					sampler.MinFilter = min;
					sampler.MagFilter = mag;
					sampler.MipFilter = mip;
					break;
			}

			if ( comparison ) sampler.Compare = CompareFunction.LessEqual;
		}

		if ( annotations.AddressU is { } u ) sampler.AddressU = Address( AnnotationExpression.Choose( u, values ) );
		if ( annotations.AddressV is { } v ) sampler.AddressV = Address( AnnotationExpression.Choose( v, values ) );
		if ( annotations.AddressW is { } w ) sampler.AddressW = Address( AnnotationExpression.Choose( w, values ) );
		if ( annotations.Comparison is { } compare ) sampler.Compare = Compare( compare );
		return sampler;
	}

	/// <summary>An anisotropic sampler's anisotropy when it doesn't give one (<c>MaxAniso</c>).</summary>
	internal const float DefaultAnisotropy = 16;

	static Filter Part( string name, string prefix )
	{
		var at = name.IndexOf( prefix, StringComparison.Ordinal );
		if ( at < 0 ) return Filter.Linear;
		var rest = name[(at + prefix.Length)..];
		while ( rest.StartsWith( "MAG_", StringComparison.Ordinal ) || rest.StartsWith( "MIP_", StringComparison.Ordinal ) ) rest = rest[4..];
		return rest.StartsWith( "POINT", StringComparison.Ordinal ) ? Filter.Nearest : Filter.Linear;
	}

	static AddressMode Address( string mode ) => mode.Trim().ToUpperInvariant() switch
	{
		"CLAMP" => AddressMode.Clamp,
		"BORDER" => AddressMode.Border,
		"MIRROR" or "MIRROR_ONCE" => AddressMode.Mirror,
		_ => AddressMode.Wrap,
	};

	static CompareFunction Compare( string function ) => function.Trim().ToUpperInvariant() switch
	{
		"NEVER" => CompareFunction.Never,
		"LESS" => CompareFunction.Less,
		"EQUAL" => CompareFunction.Equal,
		"LESS_EQUAL" => CompareFunction.LessEqual,
		"GREATER" => CompareFunction.Greater,
		"NOT_EQUAL" => CompareFunction.NotEqual,
		"GREATER_EQUAL" => CompareFunction.GreaterEqual,
		_ => CompareFunction.Always,
	};
}
