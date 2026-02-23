namespace Sandbox.Diagnostics;

/// <summary>
/// Stats returned from the engine each frame describing what was rendered, and how much of it.
/// </summary>
public struct FrameStats
{
	public static FrameStats Current => _current;
	internal static FrameStats _current = new();

	internal FrameStats( SceneSystemPerFrameStats_t stats )
	{
		ObjectsRendered = stats.m_nNumObjectsPassingCullCheck;
		ObjectsPreCull = stats.m_nNumObjectsPreCullCheck;
		ObjectsTested = stats.m_nNumObjectsTested;
		BaseObjectDraws = stats.m_nBaseSceneObjectPrimDraws;
		AnimatableObjectDraws = stats.m_nAnimatableObjectPrimDraws;
		RenderBatchDraws = stats.m_nRenderBatchDraws;
		TrianglesRendered = stats.m_nTrianglesRendered;
		DrawCalls = stats.m_nDrawCalls;
		MaterialChanges = stats.m_nMaterialChangesNonShadow;
		ShadowMaterialChanges = stats.m_nMaterialChangesShadow;
		InitialMaterialChanges = stats.m_nMaterialChangesNonShadowInitial + stats.m_nMaterialChangesShadowInitial;
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

	/// <summary>Number of initial material changes (first bind of a material this frame).</summary>
	public double InitialMaterialChanges { get; set; }

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
}
