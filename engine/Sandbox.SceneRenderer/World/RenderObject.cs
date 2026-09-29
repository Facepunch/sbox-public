namespace Sandbox.SceneRenderer;

/// <summary>
/// World object drawn by its type's render feature, equivalent to native's <c>CSceneObject</c>.
/// </summary>
public abstract class RenderObject
{
	/// <summary>
	/// The world this is in, or null.
	/// </summary>
	public RenderWorld World { get; internal set; }

	/// <summary>
	/// Slot in the world's dense arrays. Changes when another object is removed.
	/// </summary>
	internal int Index { get; set; } = -1;

	Transform transform = Transform.Zero;
	BBox localBounds;

	/// <summary>
	/// World transform. Changes update bounds and invalidate cached shadow transforms.
	/// </summary>
	public Transform Transform
	{
		get => transform;
		set
		{
			transform = value;
			TransformVersion++;
			World?.Sync( this );
		}
	}

	/// <summary>
	/// Transform revision used by shadow caching.
	/// </summary>
	internal int TransformVersion { get; private set; }

	/// <summary>
	/// Transform slot and generation shared by all views this frame.
	/// </summary>
	internal int Slot;
	internal int SlotGeneration = -1;

	/// <summary>
	/// Whether to use the spatial tree and screen-size culling. Lights opt out because their influence exceeds their bounds.
	/// </summary>
	internal virtual bool SizeCulled => true;

	/// <summary>
	/// Static object flag. Static lights cache static casters and redraw dynamic casters over them.
	/// </summary>
	public bool IsStatic
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			World?.SyncDynamic( this );
		}
	}

	/// <summary>
	/// String-token tags used by view include/exclude filters.
	/// </summary>
	public uint[] Tags { get; set; } = [];

	/// <summary>
	/// Treat as tagged <c>world</c> for view filtering (<c>CSceneSystem</c> world flags).
	/// </summary>
	public bool IsWorld { get; set; }

	/// <summary>
	/// Include in game layers (inverse of <c>SCENEOBJECTFLAG_EXCLUDE_GAME_LAYER</c>). Does not affect shadow casting.
	/// </summary>
	public bool GameLayers { get; set; } = true;

	/// <summary>
	/// Redraw in front as a viewmodel, in addition to world rendering (<c>SCENEOBJECTFLAG_GAME_OVERLAY_LAYER</c>).
	/// </summary>
	public bool Overlay { get; set; }

	/// <summary>
	/// Include in quarter-resolution bloom (<c>SCENEOBJECTFLAG_EFFECTS_BLOOM_LAYER</c>).
	/// Disable <see cref="GameLayers"/> for bloom without a visible surface.
	/// </summary>
	public bool Bloom { get; set; }

	/// <summary>
	/// Redraw above screen UI without depth (<c>SCENEOBJECTFLAG_UI_OVERLAY_LAYER</c>).
	/// </summary>
	public bool AfterUI { get; set; }

	/// <summary>
	/// Exclusive layer match (<c>SceneObject.RenderLayer</c>). Overrides all layer flags, including shadows,
	/// until cleared; existing flags are retained.
	/// </summary>
	public LayerMatch LayerMatch { get; set; }

	/// <summary>
	/// Bounds in object space, before <see cref="Transform"/>.
	/// </summary>
	public BBox LocalBounds
	{
		get => localBounds;
		set
		{
			localBounds = value;
			World?.Sync( this );
		}
	}
}

/// <summary>
/// Exclusive layer IDs, matching native's <c>SceneRenderLayer</c> and <c>StaticOverlayLayer</c>.
/// </summary>
public enum LayerMatch : byte
{
	/// <summary>
	/// Use the object's layer flags.
	/// </summary>
	None,

	/// <summary>
	/// Mesh-only static overlays after opaque geometry, sorted by <see cref="MeshObject.RenderOrder"/>.
	/// </summary>
	StaticOverlay,

	/// <summary>
	/// Depth-tested overlays after post processing.
	/// </summary>
	OverlayWithDepth,

	/// <summary>
	/// After post processing, over everything - native's <c>OverlayWithoutDepth</c>: gizmos.
	/// </summary>
	OverlayWithoutDepth,

	/// <summary>
	/// Unsupported match: draws nowhere, including shadows.
	/// </summary>
	Unsupported,
}
