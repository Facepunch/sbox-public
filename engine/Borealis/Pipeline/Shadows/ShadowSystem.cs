using System.Runtime.InteropServices;

namespace Borealis.Shadows;

/// <summary>
/// Renders the views <see cref="ShadowMapper"/> asks for, with its policy: resolution, cascades, caching and time slicing.
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
	readonly Dictionary<(ShadowMap Map, int Slice, int Region), ViewPass> passPool = new();

	readonly ShadowMap[] cascadeTargets = new ShadowMap[MaxCascades];
	readonly ShadowMap[] extraSunTargets = new ShadowMap[SceneLighting.MaxExtraSuns];
	readonly List<GpuTexture> targets = new();

	// Group shared queries after all views are known, then cull chunks in parallel.
	readonly List<int> groupStarts = new();
	readonly List<int> chunkStarts = new();
	List<int>[] chunkCandidates = [];

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

	readonly UploadRing<ShadowMapper.GPUProjectedShadow> projectedRing = new( "Borealis projected shadows" );
	readonly UploadRing<ShadowMapper.GPUProjectedCubeShadow> cubeRing = new( "Borealis cube shadows" );

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
	/// The <see cref="Passes"/> the frame draws, after <see cref="PreparePasses"/>: all but the cascade views that would leave
	/// their maps as they are (<see cref="KeepUnchangedCascades"/>).
	/// </summary>
	public ReadOnlySpan<ViewPass> Drawn => CollectionsMarshal.AsSpan( drawn );

	readonly List<ViewPass> drawn = new();

	/// <summary>
	/// What a cascade's map holds after the view that last drew it, and the map's version its tiles were made from.
	/// </summary>
	struct CascadeContents
	{
		public ShadowMap Map;

		/// <summary>
		/// The view drew no casters: the map holds only what it started from, a clear or <see cref="Source"/> copied in.
		/// </summary>
		public bool Plain;
		public ShadowMap Source;
		public ulong SourceVersion;
		public Vector2Int Offset;

		public ulong TilesVersion;
	}

	readonly CascadeContents[] cascadeContents = new CascadeContents[MaxCascades];

	// The cascades whose views were left out this frame, a bit each
	uint keptCascades;

	/// <summary>
	/// The sun's constants, cascades and all, for <c>DirectionalLightCB</c>.
	/// </summary>
	public ref readonly GPUDirectionalLight Directional => ref mapper.DirectionalLightData;

	/// <summary>
	/// Choose this frame's shadow views after main-view light packing, on the main thread, as the shadow mapper makes
	/// shadow maps. Then <see cref="BeginCull"/> and <see cref="PreparePasses"/>. Skybox suns have no cascades.
	/// </summary>
	public void Select( RenderWorld world, ViewPass main, LightBinnerFeature lightBinner, int featureCount, bool enabled, bool skybox = false )
	{
		this.world = world;
		this.main = main;
		this.featureCount = featureCount;
		passes.Clear();
		drawn.Clear();
		keptCascades = 0;
		mapper.InFlight = true;

		var view = main.View;
		mapper.View = InfoOf( view, skybox );

		// A view drawn before its frame's lead draws the sun's cascades for the lead, which then takes them
		var lead = skybox ? null : view.CascadesFor;
		mapper.HasLead = LeadCascades && lead is not null && lead != view && !lead.Reflection && !lead.Orthographic;
		mapper.Lead = mapper.HasLead ? InfoOf( lead, false ) : default;
		if ( mapper.HasLead ) mapper.Lead.Root = lead;
		mapper.InitForView();

		PrepareSun( world.Lighting, enabled || skybox );
		if ( enabled ) PrepareLocalLights( lightBinner );
		if ( enabled ) PrepareExtraSuns( world.Lighting, lightBinner );
	}

	/// <summary>
	/// Whether a view drawn before its frame's lead (<see cref="RenderView.CascadesFor"/>) draws the sun's cascades for it (on
	/// unless <c>-no-lead-cascades</c>). Off, each fits and draws its own.
	/// </summary>
	internal static bool LeadCascades { get; set; } = !Array.Exists( Environment.GetCommandLineArgs(), a => a.Equals( "-no-lead-cascades", StringComparison.OrdinalIgnoreCase ) );

	/// <summary>
	/// The shadow mapper's picture of <paramref name="view"/>: what its cascades are fit to and depend on.
	/// </summary>
	static ShadowViewInfo InfoOf( RenderView view, bool skybox ) => new()
	{
		// Cascades fit the view from its near plane, which an oblique projection leans along its clip plane
		ProjectionToView = view.SquareViewToProjection.Inverted,
		ViewToWorldRotation = view.WorldToViewRotation.Transpose(),
		CameraPosition = view.Position,
		Camera = view,
		ViewportSize = new Vector2( view.Viewport.Width, view.Viewport.Height ),
		RenderTags = view.RenderTags,
		ExcludeTags = view.ExcludeTags,
		SharesCascades = view.ClipPlane is not null,
		SubWorlds = view.SubWorlds,
		IsSkybox = skybox,
		IsReflection = view.Reflection,
		NoShadows = view.Reflection && !view.ReflectionShadows,
		RootState = (view.FieldOfView, view.Viewport.Width, view.TextureLodScale, view.FallbackTextureLodScale, view.Capture, view.HostView),
	};

	/// <summary>
	/// The frame was never submitted: forget that its shadow maps were drawn, so they're drawn again.
	/// </summary>
	public void Abandon()
	{
		mapper.Abandon();

		// Nor were the cache's tiles made
		cacheTilesOf = null;

		// Nor did its cascade views or tiles
		Array.Clear( cascadeContents );
	}

	/// <summary>
	/// The frame was submitted: its shadow maps are drawn, for the views after it.
	/// </summary>
	public void Submitted() => mapper.InFlight = false;

	/// <summary>
	/// Start culling the selected views on the job dispatcher's workers, so the caller can prepare the main view
	/// meanwhile. Culling reads the world and writes only the shadow views. Join before <see cref="PreparePasses"/>.
	/// </summary>
	public JobHandle BeginCull()
	{
		if ( passes.Count == 0 ) return default;

		if ( !system.ParallelRecording )
		{
			CullPasses();
			return default;
		}

		// Lookups that miss write the shared cache, which the main view's features mustn't race
		system.WarmFeatureTypes( world );
		return JobDispatcher.Begin( 1, this, static ( shadows, _ ) => shadows.CullPasses() );
	}

	/// <summary>
	/// Prepare the culled views, serially because transforms share one buffer.
	/// </summary>
	public void PreparePasses( TransformBuffer transforms )
	{
		foreach ( var pass in Passes )
			system.Prepare( world, pass, transforms );

		KeepUnchangedCascades();

		this.world = null;
		this.main = null;
	}

	/// <summary>
	/// Leave out the cascade views that would leave their maps as they are: ones that draw no casters, into a map that holds
	/// just the clear or the copy of an unchanged cache they'd start from. Their tiles stay too (<see cref="MarkCascades"/>).
	/// Every other view is drawn, and its map's version moves on.
	/// </summary>
	void KeepUnchangedCascades()
	{
		drawn.Clear();
		keptCascades = 0;
		var cascades = mapper.Cascades;
		foreach ( var pass in passes )
		{
			var cascade = -1;
			for ( int i = 0; i < cascades.Length; i++ )
			{
				if ( cascades[i] == pass.TargetMap ) { cascade = i; break; }
			}

			if ( cascade >= 0 && Keeps( ref cascadeContents[cascade], pass ) )
			{
				keptCascades |= 1u << cascade;
				continue;
			}

			pass.TargetMap.Version++;
			drawn.Add( pass );
		}
	}

	/// <summary>
	/// Whether the cascade's map already holds what <paramref name="pass"/> would leave in it; if not, it's what the view will.
	/// </summary>
	bool Keeps( ref CascadeContents held, ViewPass pass )
	{
		var map = pass.TargetMap;
		var source = pass.CachedStatic;

		// A whole clear or copy with nothing drawn on top. Resident lists draw what the visible lists don't show
		var plain = pass.Blank is null && pass.TargetSlice == 0 && !HasCasters( pass ) && !system.DrawsResident( pass );
		if ( plain && held.Map == map && held.Plain && held.Source == source
			&& (source is null || (held.SourceVersion == source.Version && held.Offset == pass.CacheOffset)) )
			return true;

		if ( held.Map != map ) held = new CascadeContents { Map = map };
		held.Plain = plain;
		held.Source = source;
		held.SourceVersion = source?.Version ?? 0;
		held.Offset = pass.CacheOffset;
		return false;
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

			// Baked suns leave static casters to the lightmaps
			Baked = lighting.SunShadowsBaked,

			// What the cached far cascade holds changed
			StaticChanges = world.StaticChanges,
			SettledChanges = world.SettledChanges,
		};

		mapper.DoDirectionalLight( sun );

		// FindOrCreateDirectionalShadowMaps skips colour/direction without cascades; fill them explicitly.
		ref var data = ref mapper.DirectionalLightData;
		data.Color = new Vector4( sun.Color, sun.FogStrength );
		data.Direction = new Vector4( sun.Direction, 0 );
	}

	/// <summary>
	/// Give the extra suns that cast shadows their maps, after the local lights' (<see cref="LightBinnerFeature.ExtraSunCount"/>).
	/// </summary>
	void PrepareExtraSuns( SceneLighting lighting, LightBinnerFeature lightBinner )
	{
		var suns = CollectionsMarshal.AsSpan( lighting.ExtraSuns );
		for ( int i = 0; i < lightBinner.ExtraSunCount; i++ )
		{
			ref readonly var sun = ref suns[lightBinner.ExtraSunSource( i )];
			if ( !sun.Shadows ) continue;

			var light = new ShadowLight
			{
				Key = lighting,
				Type = ShadowLightType.Directional,
				Direction = sun.Direction,
				Hardness = sun.ShadowHardness,
				ShadowsEnabled = true,
				Baked = sun.ShadowsBaked,
			};

			lightBinner.SetExtraSunShadow( i, mapper.FindOrCreateExtraSunShadow( i, light ) );
		}
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

				// A mixed light's static shadows are in its lightmaps, so its maps leave static casters out
				Baked = light.Baked,
				StaticChanges = world.StaticChanges,
				SettledChanges = world.SettledChanges,
			};

			lightBinner.SetShadowMapIndex( i, mapper.FindOrCreateShadowMaps( shadowLight, lightBinner.ScreenSize( i ) ) );
		}
	}

	/// <summary>
	/// Queue a requested shadow pass for later culling.
	/// </summary>
	public System.Numerics.Matrix4x4 RenderShadowView( in ShadowViewDesc desc )
	{
		if ( !passPool.TryGetValue( (desc.Target, desc.Slice, desc.Region), out var pass ) )
			passPool[(desc.Target, desc.Slice, desc.Region)] = pass = new ViewPass { View = new RenderView() };

		var viewport = desc.Viewport.Width > 0 ? desc.Viewport : new Rect( 0, 0, desc.Resolution, desc.Resolution );

		passes.Add( pass );
		pass.Reset( featureCount );
		pass.IsShadow = true;
		pass.Root = desc.Root as RenderView ?? main.View;
		pass.TargetMap = desc.Target;
		pass.TargetSlice = desc.Slice;
		pass.CachedStatic = desc.CachedStatic;
		pass.DepthBias = desc.DepthBias;
		pass.SlopeScaledDepthBias = desc.SlopeScaledDepthBias;
		pass.StaticFilter = desc.StaticFilter;
		pass.TexelSized = desc.Orthographic && desc.TexelSized;
		pass.LodTexelsPerUnit = desc.Orthographic && desc.TexelLods ? viewport.Width / MathF.Max( desc.Width, 1 ) : 0;
		pass.SettledStamp = desc.SettledStamp;
		pass.RestlessStamp = desc.RestlessStamp;
		pass.NewlySettled = desc.NewlySettled;
		pass.CacheOffset = desc.CacheOffset;
		pass.Blank = desc.Blank;

		var view = pass.View;
		view.Name = desc.Name;
		view.Position = desc.Position;
		view.Rotation = desc.Rotation;
		view.Orthographic = desc.Orthographic;
		view.OrthoSize = new Vector2( desc.Width, desc.Height );
		view.FieldOfView = desc.FieldOfView;
		view.ZNear = desc.ZNear;
		view.ZFar = desc.ZFar;
		view.Viewport = viewport;
		view.Update();

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
		using var rendererAllocations = Borealis.RendererAllocations.Measure();
		var count = passes.Count;
		if ( count == 0 ) return;

		MarkUnsettled();

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
		JobDispatcher.For( chunkCount, this, static ( shadows, chunk ) => shadows.CullChunk( chunk ), itemsPerBatch: 1 );
	}

	readonly List<UnsettledMarks> unsettledMarks = new();

	/// <summary>
	/// Whether <see cref="StaticFilter.Unsettled"/> views query only casters their cache doesn't hold (<see cref="MarkUnsettled"/>).
	/// </summary>
	internal static bool MarksUnsettled { get; set; } = true;

	/// <summary>
	/// Give each <see cref="StaticFilter.Unsettled"/> view tree marks that leave out the casters its far cascade's cache holds:
	/// its filter takes none of them, and its query would otherwise visit every caster that isn't static.
	/// </summary>
	void MarkUnsettled()
	{
		if ( !MarksUnsettled ) return;
		var n = 0;
		foreach ( var pass in passes )
		{
			// A local light's sphere query visits only the casters in its reach anyway
			if ( pass.StaticFilter != StaticFilter.Unsettled || pass.LightSphere is not null || pass.TreeKept is not null || pass.CullReasons is not null ) continue;
			if ( n == unsettledMarks.Count ) unsettledMarks.Add( new UnsettledMarks() );
			pass.TreeKept = unsettledMarks[n++].For( world, pass );
		}
	}

	/// <summary>
	/// An <see cref="StaticFilter.Unsettled"/> view's tree marks, made again only when the tree or which casters count as
	/// cached have changed since.
	/// </summary>
	sealed class UnsettledMarks
	{
		Culling.SpatialTree tree;
		int treeVersion;
		byte[] cached = [];
		byte[] scratch = [];
		int count = -1;
		bool[] kept = [];

		public bool[] For( RenderWorld world, ViewPass pass )
		{
			var stamps = world.MovedStamps;
			if ( scratch.Length < stamps.Length ) scratch = new byte[Math.Max( stamps.Length, scratch.Length * 2 )];

			// What the cache holds, by world index: the same test the view's filter rejects them by. A static caster that
			// animates vertices is never in it (it's drawn live over it), however long ago it last moved
			var now = scratch.AsSpan( 0, stamps.Length );
			var objects = world.Objects;
			for ( int i = 0; i < stamps.Length; i++ )
				now[i] = !objects[i].AnimatesVertices && SettledCasters.SettledBy( stamps[i], pass.SettledStamp, pass.RestlessStamp ) ? (byte)1 : (byte)0;

			var dynamic = world.DynamicTree;
			if ( ReferenceEquals( tree, dynamic ) && treeVersion == dynamic.Version && count == stamps.Length && now.SequenceEqual( cached.AsSpan( 0, count ) ) )
				return kept;

			(cached, scratch) = (scratch, cached);
			dynamic.Mark( cached.AsSpan( 0, stamps.Length ), ref kept );
			tree = dynamic;
			treeVersion = dynamic.Version;
			count = stamps.Length;
			return kept;
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
			target = new ShadowMap( resolution, false, TextureFormat.D32 );
		}

		return target;
	}

	/// <summary>
	/// Reuse one persistent target per extra sun.
	/// </summary>
	public ShadowMap GetExtraSunTarget( int sun, int resolution )
	{
		ref var target = ref extraSunTargets[sun];
		if ( target is null || target.Resolution != resolution )
		{
			target?.Dispose();
			target = new ShadowMap( resolution, false, TextureFormat.D32 );
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
	public void Setup( GpuContext context, GpuTexture contactShadowMask = null )
	{
		mapper.ResolveTextureIndices();

		// Assign the contact mask after CPU-only Prepare fills sun constants.
		if ( contactShadowMask is not null && mapper.DirectionalLightData.Enabled )
			mapper.DirectionalLightData.ShadowMaskTextureIndex = (uint)contactShadowMask.BindlessIndex;

		targets.Clear();
		foreach ( var pass in Drawn )
		{
			pass.Target = pass.TargetMap.Texture;
			if ( targets.Count == 0 || targets[^1] != pass.Target )
				targets.Add( pass.Target );
		}

		MarkCascades();
		PlanCacheTiles();

		var attributes = context.Attributes;
		attributes.Set( ProjectedShadowsName, projectedRing.Upload( context, mapper.ProjectedShadowData ) );
		attributes.Set( ProjectedCubeShadowsName, cubeRing.Upload( context, mapper.ProjectedCubeShadowData ) );
		attributes.Set( ShadowFilterQualityName, ShadowMapper.ShadowFilter );
	}

	//
	// Cheaper sun shadow sampling: a lit shader skips the filter where a cascade drew nothing, and, with ShadowMapper.CascadeTiles
	// on, where the receiver is nearer the sun than every caster the filter could reach, or behind all of them
	//

	/// <summary>
	/// Texels along each side of a tile.
	/// </summary>
	const int TileTexels = 32;

	static readonly StringToken ShadowTilesName = new( "ShadowTiles" );
	static readonly StringToken ShadowTileDepthName = new( "ShadowTileDepth" );
	static readonly StringToken ShadowTilesInName = new( "ShadowTilesIn" );
	static readonly StringToken ShadowTilesOutName = new( "ShadowTilesOut" );
	static readonly StringToken WidenCombo = new( "D_WIDEN" );

	static GpuMaterial tilesCompute;
	static GpuMaterial TilesCompute => tilesCompute ??= RenderHost.Current.Device.MaterialFromShader( "shaders/borealis_shadow_tiles_cs.shader" );

	/// <summary>
	/// Make the tiles' shader on the main thread: made where it's first dispatched, on a recording thread, it isn't there
	/// for that frame, whose tiles would then say every receiver is lit.
	/// </summary>
	public static void WarmUp()
	{
		_ = TilesCompute;
		_ = CacheCopy;
	}

	// Each cascade's tiles side by side: their own min and max depth, then widened over their neighbours
	GpuTexture tilesRaw, tiles;

	// The cascades whose tiles are made this frame, a bit each
	uint tileCascades;

	/// <summary>
	/// Flag the cascades that drew nothing, and, with <see cref="ShadowMapper.CascadeTiles"/> on and any drawn, have their tiles
	/// made after the shadow passes (<see cref="BuildTiles"/>). After culling: a cascade with no caster left is empty, unless it starts from a cache.
	/// </summary>
	void MarkCascades()
	{
		tileCascades = 0;
		ref var sun = ref mapper.DirectionalLightData;
		var cascades = mapper.Cascades;
		if ( cascades.Length == 0 ) return;

		uint drawn = 0, empty = 0;
		for ( int i = 0; i < cascades.Length; i++ )
		{
			// A kept cascade is as its view last left it: empty when that only cleared it
			if ( (keptCascades & (1u << i)) != 0 )
			{
				drawn |= 1u << i;
				if ( cascadeContents[i].Source is null ) empty |= 1u << i;
				continue;
			}

			foreach ( var pass in Drawn )
			{
				if ( pass.TargetMap != cascades[i] ) continue;
				drawn |= 1u << i;
				if ( pass.CachedStatic is null && !HasCasters( pass ) ) empty |= 1u << i;
			}
		}

		// Cascades another view drew this frame keep the flags and tiles it made
		if ( drawn == 0 ) return;

		if ( !ShadowMapper.CascadeTiles )
		{
			sun.SetCascadeFlags( empty, 0 );
			return;
		}

		var resolution = cascades[0].Resolution;
		var side = Math.Max( resolution / TileTexels, 1 );
		if ( tiles is null || tiles.Height != side )
		{
			tilesRaw?.Dispose();
			tiles?.Dispose();
			var desc = TextureDesc.Target( side * MaxCascades, side, TextureFormat.RG3232F ) with { Storage = true };
			tilesRaw = RenderHost.Current.Device.CreateTexture( desc, "Borealis cascade tiles (raw)" );
			tiles = RenderHost.Current.Device.CreateTexture( desc, "Borealis cascade tiles" );
			for ( int i = 0; i < cascadeContents.Length; i++ ) cascadeContents[i].TilesVersion = 0;
		}

		// Tiles are made again only for a map that changed since they were
		var lit = drawn & ~empty;
		for ( int i = 0; i < cascades.Length; i++ )
		{
			ref var held = ref cascadeContents[i];
			if ( (lit & (1u << i)) == 0 || (held.Map == cascades[i] && held.TilesVersion == cascades[i].Version) ) continue;
			tileCascades |= 1u << i;
			if ( held.Map == cascades[i] ) held.TilesVersion = cascades[i].Version;
		}

		if ( tileCascades != 0 ) WarmUp();
		sun.SetCascadeFlags( empty, lit != 0 ? tiles.BindlessIndex : 0 );
	}

	static bool HasCasters( ViewPass pass )
	{
		// Casters its resident list draws, which its culling left out
		if ( pass.ResidentInView ) return true;

		foreach ( var list in pass.VisibleByFeature )
		{
			if ( list.Count > 0 ) return true;
		}

		return false;
	}

	/// <summary>
	/// Make the tiles of the cascades drawn this frame, once their shadow passes are done: the min and max depth of each 32x32
	/// texel tile, then each over its neighbours too, so one read covers the widest filter.
	/// </summary>
	public void BuildTiles( GpuContext context )
	{
		if ( tileCascades == 0 ) return;
		context.Invalidate();

		var cascades = mapper.Cascades;
		var side = tiles.Height;
		context.TextureBarrier( tilesRaw, GpuStage.ComputeShader, GpuAccess.ShaderRead, GpuStage.ComputeShader, GpuAccess.ShaderWrite, TextureLayout.General );

		// Every map readable before the first dispatch, so no barrier sits between the dispatches
		for ( int i = 0; i < cascades.Length; i++ )
		{
			if ( (tileCascades & (1u << i)) == 0 ) continue;
			context.TextureBarrier( cascades[i].Texture, GpuStage.LateFragmentTests, GpuAccess.DepthStencilAttachmentWrite, GpuStage.ComputeShader | GpuStage.FragmentShader, GpuAccess.ShaderRead, TextureLayout.ShaderRead );
		}

		var parameters = ParameterPool.Get();
		parameters.Set( ShadowTilesOutName, tilesRaw );
		parameters.SetCombo( WidenCombo, false );
		for ( int i = 0; i < cascades.Length; i++ )
		{
			if ( (tileCascades & (1u << i)) == 0 ) continue;
			var depth = cascades[i].Texture;
			parameters.Set( ShadowTileDepthName, depth );
			parameters.Set( ShadowTilesName, new Vector4( i * side, side, 0, 0 ) );
			context.Dispatch( TilesCompute, parameters, side * 8, side * 8, 1 );
		}

		context.TextureBarrier( tilesRaw, GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.ComputeShader, GpuAccess.ShaderRead, TextureLayout.ShaderRead );
		context.TextureBarrier( tiles, GpuStage.FragmentShader, GpuAccess.ShaderRead, GpuStage.ComputeShader, GpuAccess.ShaderWrite, TextureLayout.General );

		parameters.Recycle();
		parameters.Set( ShadowTilesInName, tilesRaw );
		parameters.Set( ShadowTilesOutName, tiles );
		parameters.SetCombo( WidenCombo, true );
		for ( int i = 0; i < cascades.Length; i++ )
		{
			if ( (tileCascades & (1u << i)) == 0 ) continue;
			parameters.Set( ShadowTilesName, new Vector4( i * side, side, 0, 0 ) );
			context.Dispatch( TilesCompute, parameters, side, side, 1 );
		}

		ParameterPool.Return( parameters );
		context.TextureBarrier( tiles, GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.FragmentShader | GpuStage.ComputeShader, GpuAccess.ShaderRead, TextureLayout.ShaderRead );
	}

	/// <summary>
	/// List this view's shadow maps for the shadow map debug view, laid out as thumbnails along the bottom of
	/// <paramref name="viewport"/>, a row of labels' room above each, rows stacking upwards while there's room. After
	/// <see cref="Setup"/>, which makes their textures.
	/// </summary>
	public void Describe( DebugViewReport report, Rect viewport )
	{
		var frame = RenderHost.Current.FrameCount;
		var size = MathF.Round( Math.Clamp( viewport.Height / 6, 96, 224 ) );
		var layout = new ThumbnailLayout( viewport, size );

		var cascades = mapper.Cascades;
		for ( int i = 0; i < cascades.Length; i++ )
			Add( report, ref layout, cascades[i], ShadowMapKind.Cascade, i, false, frame );

		// The far cascade's cache after them, framed as drawn this frame (green: a strip, changed cells or all of it) or kept
		// (grey); it's held wrapped around, so its picture is the cascade's cut into four and swapped
		if ( mapper.FarCacheMap is { } farCache ) Add( report, ref layout, farCache, ShadowMapKind.Cascade, cascades.Length, false, frame );

		var spots = mapper.SpotShadows;
		for ( int i = 0; i < spots.Count; i++ )
		{
			Add( report, ref layout, spots[i].ShadowMap, ShadowMapKind.Spot, i, false, spots[i].RenderedFrame );
			if ( spots[i].StaticCache is { } cache ) Add( report, ref layout, cache, ShadowMapKind.Spot, i, true, spots[i].StaticRenderedFrame );
		}

		var points = mapper.PointShadows;
		for ( int i = 0; i < points.Count; i++ )
		{
			Add( report, ref layout, points[i].ShadowMap, ShadowMapKind.Point, i, false, points[i].RenderedFrame );
			if ( points[i].StaticCache is { } cache ) Add( report, ref layout, cache, ShadowMapKind.Point, i, true, points[i].StaticRenderedFrame );
		}
	}

	void Add( DebugViewReport report, ref ThumbnailLayout layout, ShadowMap map, ShadowMapKind kind, int index, bool staticCache, ulong renderedFrame )
	{
		if ( map is not { HasTexture: true } || !layout.Place( kind == ShadowMapKind.Point ? 1.5f : 1, out var rect ) )
		{
			report.ShadowMapsNotShown++;
			return;
		}

		var rendered = false;
		foreach ( var pass in Drawn )
			rendered |= pass.TargetMap == map;

		report.ShadowMaps.Add( new ShadowMapInfo
		{
			Kind = kind,
			Index = index,
			StaticCache = staticCache,
			Resolution = map.Resolution,
			RenderedFrame = renderedFrame,
			Rendered = rendered,
			Thumbnail = rect,
			Texture = map.Texture,
		} );
	}

	/// <summary>
	/// Rows of thumbnails from the bottom left of a viewport, each thumbnail with room above it for its labels.
	/// </summary>
	struct ThumbnailLayout( Rect viewport, float size )
	{
		const float Margin = 8;
		const float LabelHeight = 46;

		float x = viewport.Left + Margin;
		float y = viewport.Bottom - Margin - size;

		/// <summary>
		/// Where the next thumbnail goes, <paramref name="aspect"/> times as wide as it's tall, or false when it's out of room.
		/// </summary>
		public bool Place( float aspect, out Rect rect )
		{
			var width = MathF.Round( size * aspect );
			if ( x + width > viewport.Right - Margin && x > viewport.Left + Margin )
			{
				x = viewport.Left + Margin;
				y -= size + LabelHeight + Margin;
			}

			rect = new Rect( x, y, width, size );
			if ( y - LabelHeight < viewport.Top || width > viewport.Width - Margin * 2 ) return false;

			x += width + Margin;
			return true;
		}
	}

	/// <summary>
	/// Make the shadow maps writable as depth, before any shadow view renders.
	/// </summary>
	public void BarrierToWrite( GpuContext context )
	{
		foreach ( var target in targets )
			context.BarrierToDepthWrite( target );
	}

	/// <summary>
	/// Leave the shadow maps readable by the main view's pixel shaders, after every shadow view has rendered.
	/// </summary>
	public void BarrierToRead( GpuContext context )
	{
		foreach ( var target in targets )
			context.BarrierToPixelShaderRead( target );
	}

	/// <summary>
	/// Bind shadow targets/constants on the recording thread. The first share clears or copies the static cache.
	/// </summary>
	public void BeginPass( GpuContext context, ViewPass pass, bool clear )
	{
		var view = pass.View;
		var size = new Vector2( pass.Target.Width, pass.Target.Height );

		// Copy static depth once before drawing dynamic casters. A cache held wrapped around is unwrapped. A 2D cache is drawn
		// in (DrawCache), which costs less than a transfer copy into the depth target
		var cached = pass.CachedStatic;
		if ( clear && cached is not null && pass.TargetSlice == 0 )
		{
			var scope = context.BeginGpuScope( "Shadow cache copy" );
			var whole = !cached.IsCube && cached.Texture.Width == pass.Target.Width && cached.Texture.Height == pass.Target.Height;
			var tiles = whole && cacheTiles is not null && cacheTilesOf == cached ? cacheTiles : null;
			if ( tiles is not null && buildCacheTiles ) BuildCacheTiles( context, cached.Texture, tiles );
			var drawn = whole && DrawCache( context, cached.Texture, pass.Target, pass.CacheOffset, tiles );
			if ( !drawn && pass.CacheOffset == default ) context.CopyTexture( cached.Texture, pass.Target, cached.IsCube ? 6 : 1 );
			else if ( !drawn ) CopyWrapped( context, cached.Texture, pass.Target, pass.CacheOffset );
			context.EndGpuScope( scope );
		}

		// A view of part of a map clears only that part, from a cleared tile
		if ( clear && cached is null && pass.Blank is { } blank )
		{
			var tile = blank.Resolution;
			var rect = view.Viewport;
			for ( int y = (int)rect.Top; y < (int)rect.Bottom; y += tile )
			{
				for ( int x = (int)rect.Left; x < (int)rect.Right; x += tile )
					context.CopyTextureRegion( blank.Texture, pass.Target, 0, 0, x, y, Math.Min( tile, (int)rect.Right - x ), Math.Min( tile, (int)rect.Bottom - y ) );
			}
		}

		context.Bind( new StageTarget
		{
			Context = context,
			Attributes = context.Attributes,
			Depth = pass.Target,
			DepthSlice = pass.TargetSlice,
			Size = new Vector2( pass.Target.Width, pass.Target.Height ),
			Viewport = view.Viewport,
			MaxZ = 1,
			ColorFormat = TextureFormat.None,
			Samples = 1,
		} );
		if ( clear && cached is null && pass.Blank is null ) context.Clear( Color.Black, clearColor: false );

		// The shadow view's own, which no key describes
		var constants = new ViewConstants();
		view.FillConstants( ref constants, size, system.Frame.Time );
		context.SetViewConstants( constants, null );
	}


	// The device's own: a material is made per device
	static GpuMaterial cacheCopy;
	static GpuDevice cacheCopyDevice;
	static GpuMaterial CacheCopy
	{
		get
		{
			var device = RenderHost.Current.Device;
			if ( cacheCopy is null || cacheCopyDevice != device ) (cacheCopy, cacheCopyDevice) = (device.MaterialFromShader( "shaders/borealis_shadow_cache_copy.shader" ), device);
			return cacheCopy;
		}
	}

	static readonly StringToken CacheDepthName = new( "ShadowCacheDepth" );
	static readonly StringToken CacheWrapName = new( "ShadowCacheWrap" );
	static readonly StringToken CacheTilesName = new( "ShadowCacheTiles" );
	static readonly StringToken CacheSparseName = new( "ShadowCacheSparse" );

	/// <summary>
	/// The far cascade's cache is copied into its cascade only where it holds a caster (<see cref="DrawCache"/>): the map is
	/// cleared, and each 32x32 tile of the cache whose depth is all clear, which <see cref="cacheTiles"/> says, is left so.
	/// Off: every texel is copied.
	/// </summary>
	internal static bool SparseCacheCopy { get; set; } = true;

	// Each 32x32 tile of the far cascade's cache: its min and max depth, made again when the cache is drawn
	GpuTexture cacheTiles;
	ShadowMap cacheTilesOf;
	ulong cacheTilesVersion;
	bool buildCacheTiles;

	/// <summary>
	/// Whether this frame's far cascade copies its cache sparsely, and whether the cache's tiles are made again first: when it's
	/// a different cache or drawn since (its version moved on). After <see cref="KeepUnchangedCascades"/> has moved versions on.
	/// </summary>
	void PlanCacheTiles()
	{
		buildCacheTiles = false;
		ShadowMap cache = null;
		foreach ( var pass in Drawn )
		{
			if ( pass.CachedStatic is { } c && c == mapper.FarCacheMap ) cache = c;
		}

		if ( !SparseCacheCopy || cache is null )
		{
			cacheTilesOf = null;
			return;
		}

		var side = Math.Max( cache.Resolution / TileTexels, 1 );
		if ( cacheTiles is null || cacheTiles.Width != side )
		{
			cacheTiles?.Dispose();
			cacheTiles = RenderHost.Current.Device.CreateTexture( TextureDesc.Target( side, side, TextureFormat.RG3232F ) with { Storage = true }, "Borealis far cascade cache tiles" );
			cacheTilesOf = null;
		}

		if ( cacheTilesOf == cache && cacheTilesVersion == cache.Version ) return;
		cacheTilesOf = cache;
		cacheTilesVersion = cache.Version;
		buildCacheTiles = true;
		WarmUp();
	}

	/// <summary>
	/// The min and max depth of each 32x32 tile of the cache, as the cascades' raw tiles are made (<see cref="BuildTiles"/>).
	/// </summary>
	internal static void BuildCacheTiles( GpuContext context, GpuTexture cache, GpuTexture tiles )
	{
		context.Invalidate();
		context.TextureBarrier( cache, GpuStage.LateFragmentTests, GpuAccess.DepthStencilAttachmentWrite, GpuStage.ComputeShader | GpuStage.FragmentShader, GpuAccess.ShaderRead, TextureLayout.ShaderRead );
		context.TextureBarrier( tiles, GpuStage.FragmentShader, GpuAccess.ShaderRead, GpuStage.ComputeShader, GpuAccess.ShaderWrite, TextureLayout.General );
		var parameters = ParameterPool.Get();
		parameters.Set( ShadowTilesOutName, tiles );
		parameters.SetCombo( WidenCombo, false );
		parameters.Set( ShadowTileDepthName, cache );
		parameters.Set( ShadowTilesName, new Vector4( 0, tiles.Width, 0, 0 ) );
		context.Dispatch( TilesCompute, parameters, tiles.Width * 8, tiles.Height * 8, 1 );
		ParameterPool.Return( parameters );
		context.TextureBarrier( tiles, GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.FragmentShader, GpuAccess.ShaderRead, TextureLayout.ShaderRead );
	}
	static readonly StringToken DefaultMode = new( "Default" );

	/// <summary>
	/// Write a 2D cache's depth into a map the same size, unwrapped by <paramref name="offset"/> texels as <see cref="CopyWrapped"/>
	/// does: a full-screen draw that writes every texel's depth as the cache holds it, so the map stays a depth target.
	/// False when it couldn't draw yet (its pipeline is still being made): the caller copies instead.
	/// </summary>
	internal static bool DrawCache( GpuContext context, GpuTexture source, GpuTexture destination, Vector2Int offset, GpuTexture tiles = null )
	{
		int w = source.Width, h = source.Height;
		int ox = ((offset.x % w) + w) % w, oy = ((offset.y % h) + h) % h;
		context.BarrierToPixelShaderRead( source );
		context.Bind( new StageTarget
		{
			Context = context,
			Attributes = context.Attributes,
			Depth = destination,
			Size = new Vector2( w, h ),
			Viewport = new Rect( 0, 0, w, h ),
			MaxZ = 1,
			ColorFormat = TextureFormat.None,
			Samples = 1,
		} );

		// With the cache's tiles: the map cleared, and only the tiles that hold a caster written
		if ( tiles is not null ) context.Clear( Color.Black, clearColor: false );

		var parameters = ParameterPool.Get();
		parameters.Set( CacheDepthName, source );
		parameters.Set( CacheWrapName, new Vector4( ox, oy, w, h ) );
		if ( tiles is not null ) parameters.Set( CacheTilesName, tiles );

		// A quad a tile, those without a caster collapsed, where the wrap is a whole number of tiles; else every texel drawn,
		// those of tiles without a caster discarded
		var drew = false;
		if ( tiles is not null && ox % TileTexels == 0 && oy % TileTexels == 0 && w % TileTexels == 0 && h % TileTexels == 0 )
		{
			parameters.Set( CacheSparseName, 2.0f );
			drew = context.DrawFullscreenInstances( CacheCopy, parameters, DefaultMode, (w / TileTexels) * (h / TileTexels) );
		}

		if ( !drew )
		{
			parameters.Set( CacheSparseName, tiles is not null ? 1.0f : 0.0f );
			drew = context.DrawFullscreenNow( CacheCopy, parameters, DefaultMode );
		}

		ParameterPool.Return( parameters );
		return drew;
	}

	/// <summary>
	/// Copy a texture held wrapped around by <paramref name="offset"/> texels into one that isn't: four rectangles.
	/// </summary>
	static void CopyWrapped( GpuContext context, GpuTexture source, GpuTexture destination, Vector2Int offset )
	{
		int w = source.Width, h = source.Height;
		int ox = ((offset.x % w) + w) % w, oy = ((offset.y % h) + h) % h;
		context.CopyTextureRegion( source, destination, ox, oy, 0, 0, w - ox, h - oy );
		context.CopyTextureRegion( source, destination, 0, oy, w - ox, 0, ox, h - oy );
		context.CopyTextureRegion( source, destination, ox, 0, 0, h - oy, w - ox, oy );
		context.CopyTextureRegion( source, destination, 0, 0, w - ox, h - oy, ox, oy );
	}

	public void Dispose()
	{
		foreach ( var pass in passPool.Values ) RenderHost.Current?.Device?.ReleaseView( pass );
		passPool.Clear();
		tilesRaw?.Dispose();
		tiles?.Dispose();
		tilesRaw = tiles = null;
		cacheTiles?.Dispose();
		cacheTiles = null;
		cacheTilesOf = null;
		projectedRing.Dispose();
		cubeRing.Dispose();

		for ( int i = 0; i < cascadeTargets.Length; i++ )
		{
			cascadeTargets[i]?.Dispose();
			cascadeTargets[i] = null;
		}

		for ( int i = 0; i < extraSunTargets.Length; i++ )
		{
			extraSunTargets[i]?.Dispose();
			extraSunTargets[i] = null;
		}
	}
}
