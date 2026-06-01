namespace Sandbox.Diagnostics;

/// <summary>
/// Stats returned from the engine each frame describing what was rendered, and how much of it.
/// </summary>
public struct FrameStats
{
	public static FrameStats Current => _current;
	internal static FrameStats _current = new();

	internal FrameStats( SceneSystemPerFrameStats_t stats, uint unbatchableMaterials, string gpuStatsSummary, int pendingStreamingRequests, ulong texturePoolUsedBytes, ulong texturePoolLimitBytes, ulong texturePoolNonEvictableBytes )
	{
		ObjectsRendered = stats.m_nNumObjectsPassingCullCheck;
		ObjectsPreCull = stats.m_nNumObjectsPreCullCheck;
		ObjectsTested = stats.m_nNumObjectsTested;
		BaseObjectDraws = stats.m_nBaseSceneObjectPrimDraws;
		AnimatableObjectDraws = stats.m_nAnimatableObjectPrimDraws;
		AggregateObjectDraws = stats.m_nAggregateSceneObjectPrimDraws;
		AggregateObjectsFullyCulled = stats.m_nAggregateSceneObjectsFullyCulled;
		AggregateObjectDrawCalls = stats.m_nAggregateSceneObjectDrawCalls;
		RenderBatchDraws = stats.m_nRenderBatchDraws;
		TrianglesRendered = stats.m_nTrianglesRendered;
		DrawCalls = stats.m_nDrawCalls;
		MaterialChanges = stats.m_nMaterialChangesNonShadow;
		ShadowMaterialChanges = stats.m_nMaterialChangesShadow;
		ShadowMaterialChangesAlphaTested = stats.m_nMaterialChangesShadowAlphaTested;
		InitialMaterialChanges = stats.m_nMaterialChangesNonShadowInitial + stats.m_nMaterialChangesShadowInitial;
		InitialShadowMaterialChanges = stats.m_nMaterialChangesShadowInitial;
		CopyMaterialChanges = stats.m_nCopyMaterialChangesNonShadow;
		MaterialComputes = stats.m_nNumMaterialCompute;
		// Native displays full material sets as (total - similar); preserve that decomposition here.
		FullMaterialSets = stats.m_nNumMaterialSet >= stats.m_nNumSimilarMaterialSet
			? stats.m_nNumMaterialSet - stats.m_nNumSimilarMaterialSet
			: 0;
		SimilarMaterialSets = stats.m_nNumSimilarMaterialSet;
		TextureOnlyMaterialSets = stats.m_nNumTextureOnlyMaterialSet;
		VfxEvals = stats.m_nNumVfxEval;
		VfxRuleChecks = stats.m_nNumVfxRule;
		ConstantBufferUpdates = stats.m_nNumConstantBufferUpdates;
		ConstantBufferBytes = stats.m_nNumConstantBufferBytes;
		UnbatchableMaterialDraws = unbatchableMaterials;
		UniqueMaterials = stats.m_nNumUniqueMaterialsSeen;
		DisplayLists = stats.m_nNumDisplayListsSubmitted;
		SceneViewsRendered = stats.m_nNumViewsRendered;
		RenderTargetResolves = stats.m_nNumResolves;
		PrimaryContexts = stats.m_nNumPrimaryContexts;
		SecondaryContexts = stats.m_nNumSecondaryContexts;
		ObjectsCulledByVis = stats.m_nNumObjectsRejectedByVis;
		ObjectsCulledByScreenSize = stats.m_nNumObjectsRejectedByScreenSizeCulling;
		ObjectsCulledByFade = stats.m_nNumObjectsRejectedByFading;
		ObjectsFading = stats.m_nNumFadingObjects;
		ShadowedLightsInView = stats.m_nNumShadowedLightsInView;
		UnshadowedLightsInView = stats.m_nNumUnshadowedLightsInView;
		ShadowMaps = stats.m_nNumShadowMaps;
		GpuStatsSummary = gpuStatsSummary;
		PendingStreamingRequests = pendingStreamingRequests;
		TexturePoolUsedBytes = texturePoolUsedBytes;
		TexturePoolLimitBytes = texturePoolLimitBytes;
		TexturePoolNonEvictableBytes = texturePoolNonEvictableBytes;
	}

	/// <summary>Number of objects that passed all cull checks and were rendered.</summary>
	public double ObjectsRendered { get; set; }

	/// <summary>Number of objects considered before culling.</summary>
	public double ObjectsPreCull { get; set; }

	/// <summary>Number of objects that were tested against cull checks.</summary>
	public double ObjectsTested { get; set; }

	/// <summary>Primitive draws for base (static) scene objects.</summary>
	public double BaseObjectDraws { get; set; }

	/// <summary>Primitive draws for animatable scene objects.</summary>
	public double AnimatableObjectDraws { get; set; }

	/// <summary>Primitive draws for aggregate scene objects (batched static prop / world geometry containers).</summary>
	public double AggregateObjectDraws { get; set; }

	/// <summary>Aggregate scene objects rejected entirely by their bounding box this frame.</summary>
	public double AggregateObjectsFullyCulled { get; set; }

	/// <summary>Native draw calls issued from aggregate scene objects.</summary>
	public double AggregateObjectDrawCalls { get; set; }

	/// <summary>Number of render batch draw lists submitted.</summary>
	public double RenderBatchDraws { get; set; }

	/// <summary>Total number of triangles rendered.</summary>
	public double TrianglesRendered { get; set; }

	/// <summary>Number of draw calls.</summary>
	public double DrawCalls { get; set; }

	/// <summary>Number of non-shadow (colour pass) material changes.</summary>
	public double MaterialChanges { get; set; }

	/// <summary>Number of depth-only (shadow pass) material changes.</summary>
	public double ShadowMaterialChanges { get; set; }

	/// <summary>Number of depth-only alpha-tested material changes.</summary>
	public double ShadowMaterialChangesAlphaTested { get; set; }

	/// <summary>Number of initial material changes across both colour and shadow passes (first bind of a material this frame).</summary>
	public double InitialMaterialChanges { get; set; }

	/// <summary>Initial material changes that occurred in the shadow / depth-only pass specifically.</summary>
	public double InitialShadowMaterialChanges { get; set; }

	/// <summary>Number of "copy" material changes (rebound copy of a previously seen material).</summary>
	public double CopyMaterialChanges { get; set; }

	/// <summary>Number of material compute invocations.</summary>
	public double MaterialComputes { get; set; }

	/// <summary>Number of full material sets (m_nNumMaterialSet minus similar sets, matching stats_display output).</summary>
	public double FullMaterialSets { get; set; }

	/// <summary>Number of "similar" material sets (cheap reset against a near-identical material).</summary>
	public double SimilarMaterialSets { get; set; }

	/// <summary>Number of texture-only material sets (only texture bindings changed).</summary>
	public double TextureOnlyMaterialSets { get; set; }

	/// <summary>Number of material Vfx evaluations this frame.</summary>
	public double VfxEvals { get; set; }

	/// <summary>Number of material Vfx rule checks this frame.</summary>
	public double VfxRuleChecks { get; set; }

	/// <summary>Number of constant-buffer updates issued by the material system.</summary>
	public double ConstantBufferUpdates { get; set; }

	/// <summary>Total bytes uploaded into constant buffers by the material system.</summary>
	public double ConstantBufferBytes { get; set; }

	/// <summary>Number of unbatchable material draws (materials that opted out of batching for the frame).</summary>
	public double UnbatchableMaterialDraws { get; set; }

	/// <summary>Number of unique materials seen this frame.</summary>
	public double UniqueMaterials { get; set; }

	/// <summary>Number of display lists submitted to the GPU.</summary>
	public double DisplayLists { get; set; }

	/// <summary>Number of scene views rendered.</summary>
	public double SceneViewsRendered { get; set; }

	/// <summary>Number of render target resolves.</summary>
	public double RenderTargetResolves { get; set; }

	/// <summary>Number of primary render contexts created.</summary>
	public double PrimaryContexts { get; set; }

	/// <summary>Number of secondary render contexts created.</summary>
	public double SecondaryContexts { get; set; }

	/// <summary>Number of objects culled by static visibility.</summary>
	public double ObjectsCulledByVis { get; set; }

	/// <summary>Number of objects culled by screen size.</summary>
	public double ObjectsCulledByScreenSize { get; set; }

	/// <summary>Number of objects culled by distance fading.</summary>
	public double ObjectsCulledByFade { get; set; }

	/// <summary>Number of objects currently being distance-faded.</summary>
	public double ObjectsFading { get; set; }

	/// <summary>Number of lights in view that cast shadows.</summary>
	public double ShadowedLightsInView { get; set; }

	/// <summary>Number of lights in view that don't cast shadows.</summary>
	public double UnshadowedLightsInView { get; set; }

	/// <summary>Number of shadow maps rendered this frame.</summary>
	public double ShadowMaps { get; set; }

	/// <summary>Multi-line GPU resource summary from the render device (memory mgr, framebuffers, pipeline cache).</summary>
	public string GpuStatsSummary { get; set; }

	/// <summary>In-flight resource streaming-data requests. Sustained &gt; 0 indicates streaming pressure (mips/data still loading).</summary>
	public int PendingStreamingRequests { get; set; }

	/// <summary>Current texture pool memory usage as tracked by the texture streamer, in bytes.</summary>
	public ulong TexturePoolUsedBytes { get; set; }

	/// <summary>Texture pool limit (dynamically tuned against GPU heap budget by the texture streamer), in bytes.</summary>
	public ulong TexturePoolLimitBytes { get; set; }

	/// <summary>Portion of the texture pool occupied by non-evictable textures, in bytes.</summary>
	public ulong TexturePoolNonEvictableBytes { get; set; }
}
