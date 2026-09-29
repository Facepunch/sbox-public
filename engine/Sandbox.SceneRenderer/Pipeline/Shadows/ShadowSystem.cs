using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer.Shadows;

/// <summary>
/// Managed renderer for the shared <see cref="ShadowMapper"/> policy: resolution, cascades, caching and time slicing.
/// Each requested shadow view becomes a culled depth <see cref="ViewPass"/>.
/// </summary>
internal sealed class ShadowSystem : IShadowRenderer, IDisposable
{
	public const int MaxCascades = 4;

	static readonly StringToken ProjectedShadowsName = new( "ProjectedShadows" );
	static readonly StringToken ProjectedCubeShadowsName = new( "ProjectedCubeShadows" );
	static readonly StringToken ShadowFilterQualityName = new( "ShadowFilterQuality" );
	static readonly StringToken ViewConstantsName = new( "PerViewConstantBuffer_t" );

	readonly RenderSystem system;
	readonly ShadowMapper mapper = new();
	readonly List<ViewPass> passes = new();

	/// <summary>
	/// Stable map/face pooling preserves list capacity across time-sliced updates.
	/// </summary>
	readonly Dictionary<(ShadowMap Map, int Slice), ViewPass> passPool = new();

	readonly ShadowMap[] cascadeTargets = new ShadowMap[MaxCascades];
	readonly List<Texture> targets = new();
	readonly RenderView exclusionView = new();

	// Group shared queries after all views are known, then cull chunks in parallel.
	readonly List<int> groupStarts = new();
	readonly List<int> chunkStarts = new();
	List<int>[] chunkCandidates = [];
	JobBatch cullJobs;

	/// <summary>
	/// The most jobs the shadow views are culled in.
	/// </summary>
	static readonly int MaxCullJobs = Math.Clamp( Environment.ProcessorCount - 1, 1, 8 );

	/// <summary>
	/// Minimum view count that amortizes worker overhead.
	/// </summary>
	const int MinParallelCullViews = 16;

	/// <summary>
	/// Large worlds justify parallel culling even with few views.
	/// </summary>
	const int MinParallelCullObjects = 2000;

	readonly UploadRing<ShadowMapper.GPUProjectedShadow> projectedRing = new( "SceneRenderer projected shadows" );
	readonly UploadRing<ShadowMapper.GPUProjectedCubeShadow> cubeRing = new( "SceneRenderer cube shadows" );

	// The view being prepared - the mapper calls back into RenderShadowView in the middle of Prepare
	RenderWorld world;
	ViewPass main;
	int featureCount;

	public ShadowSystem( RenderSystem system )
	{
		this.system = system;
		mapper.Renderer = this;
	}

	/// <summary>
	/// This frame's shadow views, in the order they're drawn. A map that's cached this frame has none.
	/// </summary>
	public ReadOnlySpan<ViewPass> Passes => CollectionsMarshal.AsSpan( passes );

	/// <summary>
	/// The sun's constants, cascades and all, for <c>DirectionalLightCB</c>.
	/// </summary>
	public ref readonly GPUDirectionalLight Directional => ref mapper.DirectionalLightData;

	/// <summary>
	/// Select, cull and prepare shadow views after main-view light packing. Skybox suns have no cascades.
	/// </summary>
	public void Prepare( RenderWorld world, ViewPass main, LightBinnerFeature lightBinner, TransformBuffer transforms, int featureCount, bool enabled, bool skybox = false )
	{
		this.world = world;
		this.main = main;
		this.featureCount = featureCount;
		passes.Clear();

		var view = main.View;
		mapper.View = new ShadowViewInfo
		{
			InverseViewProjection = view.WorldToProjection.Inverted,
			CameraPosition = view.Position,
			ViewportSize = new Vector2( view.Viewport.Width, view.Viewport.Height ),
			IsSkybox = skybox,
		};
		mapper.InitForView();

		PrepareSun( world.Lighting, enabled || skybox );
		if ( enabled ) PrepareLocalLights( lightBinner );

		// Cull in parallel, then prepare serially because transforms share one buffer.
		CullPasses();
		foreach ( var pass in Passes )
			system.Prepare( world, pass, transforms );

		this.world = null;
		this.main = null;
	}

	/// <summary>
	/// Prepare sun lighting; skybox foliage still needs <c>g_DirectionalLightEnabled</c> without cascades.
	/// </summary>
	void PrepareSun( SceneLighting lighting, bool enabled )
	{
		if ( !lighting.SunEnabled ) return;

		var color = lighting.SunColor;
		var sun = new ShadowLight
		{
			Key = lighting,
			Type = ShadowLightType.Directional,
			Direction = lighting.SunDirection,
			Color = new Vector3( color.r, color.g, color.b ),
			FogStrength = lighting.SunFogStrength,
			Hardness = lighting.SunShadowHardness,
			Bias = lighting.SunShadowBias,
			ShadowsEnabled = enabled && lighting.SunShadows,
			Cascades = lighting.SunShadowCascades,
			CascadeSplitRatio = lighting.SunShadowSplitRatio,

			// Baked suns exclude static casters (ShadowMapper.FromNative).
			Baked = lighting.SunShadowsBaked,
		};

		mapper.DoDirectionalLight( sun );

		// FindOrCreateDirectionalShadowMaps skips colour/direction without cascades; fill them explicitly.
		ref var data = ref mapper.DirectionalLightData;
		data.Color = new Vector4( sun.Color, sun.FogStrength );
		data.Direction = new Vector4( sun.Direction, 0 );
	}

	/// <summary>
	/// Allocate local shadows in priority order until the budget runs out.
	/// </summary>
	void PrepareLocalLights( LightBinnerFeature lightBinner )
	{
		var lights = lightBinner.PackedLights;
		for ( int i = 0; i < lights.Length; i++ )
		{
			var light = lights[i];
			if ( !light.RendersShadowMap ) continue;

			var transform = light.Transform;
			var shadowLight = new ShadowLight
			{
				Key = light,
				Type = light.Kind == LightObject.LightKind.Spot ? ShadowLightType.Spot : ShadowLightType.Point,
				Position = transform.Position,
				Rotation = transform.Rotation,
				TransformVersion = light.TransformVersion,
				Radius = light.Radius,
				ConeOuter = light.ConeOuter,
				Hardness = light.ShadowHardness,
				Static = light.IsStatic,
			};

			lightBinner.SetShadowMapIndex( i, mapper.FindOrCreateShadowMaps( shadowLight, lightBinner.ScreenSize( i ) ) );
		}
	}

	/// <summary>
	/// Queue a requested shadow pass for later culling.
	/// </summary>
	public System.Numerics.Matrix4x4 RenderShadowView( in ShadowViewDesc desc )
	{
		if ( !passPool.TryGetValue( (desc.Target, desc.Slice), out var pass ) )
			passPool[(desc.Target, desc.Slice)] = pass = new ViewPass { View = new RenderView() };

		passes.Add( pass );
		pass.Reset( featureCount );
		pass.IsShadow = true;
		pass.Root = main.View;
		pass.TargetMap = desc.Target;
		pass.TargetSlice = desc.Slice;
		pass.CachedStatic = desc.CachedStatic;
		pass.DepthBias = desc.DepthBias;
		pass.SlopeScaledDepthBias = desc.SlopeScaledDepthBias;
		pass.StaticFilter = (desc.RequiredFlags & SceneObjectFlags.StaticObject) != 0 ? StaticFilter.OnlyStatic
			: (desc.ExcludedFlags & SceneObjectFlags.StaticObject) != 0 ? StaticFilter.NoStatic
			: StaticFilter.Any;

		var view = pass.View;
		view.Name = desc.Name;
		view.Position = desc.Position;
		view.Rotation = desc.Rotation;
		view.Orthographic = desc.Orthographic;
		view.OrthoSize = new Vector2( desc.Width, desc.Height );
		view.FieldOfView = desc.FieldOfView;
		view.ZNear = desc.ZNear;
		view.ZFar = desc.ZFar;
		view.Viewport = new Rect( 0, 0, desc.Resolution, desc.Resolution );
		view.Update();

		if ( desc.HasExclusion )
		{
			var size = desc.ExclusionSize;
			exclusionView.Position = desc.ExclusionCenter;
			exclusionView.Rotation = desc.Rotation;
			exclusionView.Orthographic = true;
			exclusionView.OrthoSize = new Vector2( size );
			exclusionView.ZNear = -size * 0.5f;
			exclusionView.ZFar = size * 0.5f;
			exclusionView.Viewport = new Rect( 0, 0, 1, 1 );
			exclusionView.Update();
			pass.Exclusion = exclusionView.Frustum;
		}

		// Local-light views share a sphere query during CullPasses.
		pass.LightSphere = desc.Orthographic ? null : (desc.Position, desc.ZFar);
		return view.WorldToProjection;
	}

	/// <summary>
	/// Group views sharing a light-sphere query, then cull balanced chunks.
	/// Workers read the world and write only per-view lists; cascades use separate groups.
	/// </summary>
	void CullPasses()
	{
		var count = passes.Count;
		if ( count == 0 ) return;

		groupStarts.Clear();
		for ( int i = 0; i < count; i++ )
		{
			if ( i == 0 || !SharesQuery( passes[i - 1], passes[i] ) ) groupStarts.Add( i );
		}
		groupStarts.Add( count );

		// Balance view counts without splitting query groups.
		var parallel = system.ParallelRecording && (count >= MinParallelCullViews || world.Count >= MinParallelCullObjects);
		var chunks = parallel ? Math.Min( groupStarts.Count - 1, MaxCullJobs ) : 1;
		var share = (count + chunks - 1) / chunks;
		chunkStarts.Clear();
		chunkStarts.Add( 0 );
		for ( int g = 1; g < groupStarts.Count - 1; g++ )
		{
			if ( groupStarts[g] - groupStarts[chunkStarts[^1]] >= share ) chunkStarts.Add( g );
		}
		chunkStarts.Add( groupStarts.Count - 1 );

		var chunkCount = chunkStarts.Count - 1;
		if ( chunkCandidates.Length < chunkCount )
		{
			var old = chunkCandidates.Length;
			Array.Resize( ref chunkCandidates, chunkCount );
			for ( int i = old; i < chunkCount; i++ ) chunkCandidates[i] = new List<int>();
		}

		if ( chunkCount == 1 )
		{
			CullChunk( 0 );
			return;
		}

		// Warm feature lookups before concurrent reads.
		system.WarmFeatureTypes( world );

		// Workers and the caller share chunk claiming.
		cullJobs ??= new JobBatch( CullChunk );
		cullJobs.Start( chunkCount, chunkCount - 1 );

		try
		{
			cullJobs.Help();
		}
		finally
		{
			cullJobs.Wait();
		}
	}

	/// <summary>
	/// Whether views share a map and light sphere. Frustum queries are never shared.
	/// </summary>
	static bool SharesQuery( ViewPass a, ViewPass b )
		=> a.LightSphere is { } sphere && sphere == b.LightSphere && a.TargetMap == b.TargetMap;

	/// <summary>
	/// Cull the groups in chunk <paramref name="chunk"/>, with that chunk's own candidate list.
	/// </summary>
	void CullChunk( int chunk )
	{
		using var zone = Zones.ShadowCull.Start();
		var candidates = chunkCandidates[chunk];
		for ( int g = chunkStarts[chunk]; g < chunkStarts[chunk + 1]; g++ )
		{
			var first = groupStarts[g];
			var inFrustum = RenderSystem.FindCandidates( world, passes[first], candidates, out var inTree, out _ );

			// Culled the same way as the main view, into counts nobody reads
			var stats = new RenderStats();
			for ( int i = first; i < groupStarts[g + 1]; i++ )
				system.Cull( world, passes[i], candidates, inTree, inFrustum, ref stats );
		}
	}

	/// <summary>
	/// Reuse one persistent target per cascade.
	/// </summary>
	public ShadowMap GetCascadeTarget( int cascade, int resolution )
	{
		ref var target = ref cascadeTargets[cascade];
		if ( target is null || target.Resolution != resolution )
		{
			target?.Dispose();
			target = new ShadowMap( resolution, false, ImageFormat.D32 );
		}

		return target;
	}

	/// <summary>
	/// Contact-shadow indices are assigned later in Setup.
	/// </summary>
	public uint GetShadowMaskIndex( object light ) => 0;

	/// <summary>
	/// Resolve shadow textures/indices and upload lighting buffers before render passes.
	/// </summary>
	public void Setup( RenderContext context, Texture contactShadowMask = null )
	{
		mapper.ResolveTextureIndices();

		// Assign the contact mask after CPU-only Prepare fills sun constants.
		if ( contactShadowMask is not null && mapper.DirectionalLightData.Enabled )
			mapper.DirectionalLightData.ShadowMaskTextureIndex = (uint)contactShadowMask.Index;

		targets.Clear();
		foreach ( var pass in Passes )
		{
			pass.Target = pass.TargetMap.Texture;
			if ( targets.Count == 0 || targets[^1] != pass.Target )
				targets.Add( pass.Target );
		}

		var attributes = context.Attributes;
		attributes.Set( ProjectedShadowsName, projectedRing.Upload( context, mapper.ProjectedShadowData ) );
		attributes.Set( ProjectedCubeShadowsName, cubeRing.Upload( context, mapper.ProjectedCubeShadowData ) );
		attributes.Set( ShadowFilterQualityName, ShadowMapper.ShadowFilter );
	}

	/// <summary>
	/// Make the shadow maps writable as depth, before any shadow view renders.
	/// </summary>
	public void BarrierToWrite( RenderContext context )
	{
		foreach ( var target in targets )
			context.BarrierToDepthWrite( target );
	}

	/// <summary>
	/// Leave the shadow maps readable by the main view's pixel shaders, after every shadow view has rendered.
	/// </summary>
	public void BarrierToRead( RenderContext context )
	{
		foreach ( var target in targets )
			context.BarrierToPixelShaderRead( target );
	}

	/// <summary>
	/// Bind shadow targets/constants on the recording thread. The first share clears or copies the static cache.
	/// </summary>
	public void BeginPass( RenderContext context, ViewPass pass, bool clear )
	{
		var view = pass.View;
		var size = new Vector2( view.Viewport.Width, view.Viewport.Height );

		// Copy static depth once before drawing dynamic casters.
		var cached = pass.CachedStatic;
		if ( clear && cached is not null && pass.TargetSlice == 0 )
			context.CopyTexture( cached.Texture, pass.Target, cached.IsCube ? 6 : 1 );

		context.Bind( new StageTarget
		{
			Context = context.Native,
			Attributes = context.Attributes,
			Depth = pass.Target.native,
			DepthSlice = pass.TargetSlice,
			Size = new Vector2( pass.Target.Width, pass.Target.Height ),
			Viewport = view.Viewport,
			MaxZ = 1,
			ColorFormat = ImageFormat.None,
			Samples = 1,
		} );
		if ( clear && cached is null ) context.Clear( Color.Black, clearColor: false );

		// The shadow view's own, which no key describes
		var constants = new ViewConstants();
		view.FillConstants( ref constants, size, system.Frame.Time );
		context.SetViewConstants( constants, null );
	}


	public void Dispose()
	{
		passPool.Clear();
		projectedRing.Dispose();
		cubeRing.Dispose();

		for ( int i = 0; i < cascadeTargets.Length; i++ )
		{
			cascadeTargets[i]?.Dispose();
			cascadeTargets[i] = null;
		}
	}
}
