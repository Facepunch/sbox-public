namespace Borealis;

/// <summary>
/// World object drawn by its type's render feature, equivalent to native's <c>CSceneObject</c>.
/// </summary>
public abstract class RenderObject
{
	/// <summary>
	/// The world this is in, or null. Worlds that draw this world as a sub-world (<see cref="RenderWorld.Include"/>) hold it too.
	/// </summary>
	public RenderWorld World { get; internal set; }

	/// <summary>
	/// Slot in <see cref="World"/>'s dense arrays. Changes when another object is removed. Its slot in a world that holds it as a
	/// sub-world's is <see cref="IndexIn"/>.
	/// </summary>
	internal int Index { get; set; } = -1;

	// The worlds that hold this as one of a sub-world's objects, and its slot in each
	(RenderWorld World, int Index)[] hosts = [];
	int hostCount;

	/// <summary>
	/// Its slot in <paramref name="world"/>'s dense arrays: its own world's, or one holding its world as a sub-world. -1 when
	/// that world doesn't hold it.
	/// </summary>
	internal int IndexIn( RenderWorld world )
	{
		if ( ReferenceEquals( World, world ) ) return Index;
		for ( int i = 0; i < hostCount; i++ )
		{
			if ( ReferenceEquals( hosts[i].World, world ) ) return hosts[i].Index;
		}

		return -1;
	}

	/// <summary>
	/// Note its slot in <paramref name="world"/> (its own or a host), when the world moves it.
	/// </summary>
	internal void SetIndexIn( RenderWorld world, int index )
	{
		if ( ReferenceEquals( World, world ) )
		{
			Index = index;
			return;
		}

		for ( int i = 0; i < hostCount; i++ )
		{
			if ( ReferenceEquals( hosts[i].World, world ) ) hosts[i].Index = index;
		}
	}

	/// <summary>
	/// Note that a world holding its world as a sub-world holds it at <paramref name="index"/>.
	/// </summary>
	internal void AddHost( RenderWorld world, int index )
	{
		if ( hostCount == hosts.Length ) Array.Resize( ref hosts, Math.Max( 2, hosts.Length * 2 ) );
		hosts[hostCount++] = (world, index);
	}

	/// <summary>
	/// Note that a world holding its world as a sub-world no longer does.
	/// </summary>
	internal void RemoveHost( RenderWorld world )
	{
		for ( int i = 0; i < hostCount; i++ )
		{
			if ( !ReferenceEquals( hosts[i].World, world ) ) continue;
			hosts[i] = hosts[--hostCount];
			hosts[hostCount] = default;
			return;
		}
	}

	/// <summary>
	/// How many worlds hold it as a sub-world's (<see cref="HostAt"/>).
	/// </summary>
	internal int HostCount => hostCount;

	internal RenderWorld HostAt( int i ) => hosts[i].World;

	/// <summary>
	/// Whether a world that holds it is being rendered: its own, or one holding it as a sub-world's. Its changes wait meanwhile.
	/// </summary>
	public bool InRender
	{
		get
		{
			if ( World is { Rendering: > 0 } ) return true;
			for ( int i = 0; i < hostCount; i++ )
			{
				if ( hosts[i].World.Rendering > 0 ) return true;
			}

			return false;
		}
	}

	/// <summary>What changed, for every world holding the object (<see cref="Changed"/>).</summary>
	private protected enum WorldNote : byte
	{
		Sync,
		SyncDynamic,
		StaticCaster,
		Caster,
		Draw,
		Entry,
		Object,
	}

	/// <summary>
	/// Tell its own world and every world holding it as a sub-world's that something changed.
	/// </summary>
	private protected void Changed( WorldNote note )
	{
		if ( World is { } world ) Note( world, note );
		for ( int i = 0; i < hostCount; i++ ) Note( hosts[i].World, note );
	}

	void Note( RenderWorld world, WorldNote note )
	{
		switch ( note )
		{
			case WorldNote.Sync: world.Sync( this ); break;
			case WorldNote.SyncDynamic: world.SyncDynamic( this ); break;
			case WorldNote.StaticCaster: world.StaticCasterChanged( this ); break;
			case WorldNote.Caster: world.CasterChanged( this ); break;
			case WorldNote.Draw: world.DrawChanged( this ); break;
			case WorldNote.Entry: world.EntryChanged( this ); break;
			case WorldNote.Object: world.ObjectChanged( this ); break;
		}
	}

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
			Changed( WorldNote.Sync );
		}
	}

	/// <summary>
	/// Transform revision used by shadow caching.
	/// </summary>
	internal int TransformVersion { get; private set; }

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
			Changed( WorldNote.SyncDynamic );
			Changed( WorldNote.StaticCaster );
			DrawChanged();
		}
	}

	/// <summary>
	/// Tell the world this changed how it casts shadows, so lights it reaches redraw their static shadow caches, and the sun's
	/// cached far cascade redraws if it holds it.
	/// </summary>
	private protected void ShadowChanged() => Changed( WorldNote.Caster );

	/// <summary>
	/// Tell the world something that decides how this draws changed, so what renderers kept of it is made again
	/// (<see cref="RenderWorld.DrawVersions"/>).
	/// </summary>
	private protected void DrawChanged() => Changed( WorldNote.Draw );

	/// <summary>
	/// Count a mesh whose attributes are read as it draws in or out of every world holding it (<see cref="RenderWorld.AttributeSources"/>).
	/// </summary>
	private protected void AttributeSourceChanged( int by )
	{
		if ( World is { } world ) world.AttributeSources += by;
		for ( int i = 0; i < hostCount; i++ ) hosts[i].World.AttributeSources += by;
	}

	/// <summary>
	/// String-token tags used by view include/exclude filters.
	/// </summary>
	public uint[] Tags
	{
		get;
		set
		{
			if ( field.AsSpan().SequenceEqual( value ) ) return;
			field = value;
			ShadowChanged();
			DrawChanged();
		}
	} = [];

	/// <summary>
	/// Treat as tagged <c>world</c> for view filtering (<c>CSceneSystem</c> world flags).
	/// </summary>
	public bool IsWorld
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ShadowChanged();
			DrawChanged();
		}
	}

	/// <summary>
	/// The sub-world this object belongs to in <see cref="World"/>: 0 for the world's own objects, or 1 to
	/// <see cref="RenderWorld.MaxSubWorlds"/> for one only views with its bit in <see cref="RenderView.SubWorlds"/> show. A world
	/// drawn as another's sub-world (<see cref="RenderWorld.Include"/>) is held there under the number it was given. Native
	/// draws a camera's extra worlds in its view this way, in the same layers and depth as its main world
	/// (<c>CSceneView::AddWorldToRenderList</c>).
	/// </summary>
	public int SubWorld
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative( value );
			ArgumentOutOfRangeException.ThrowIfGreaterThan( value, RenderWorld.MaxSubWorlds );
			if ( field == value ) return;
			field = value;
			World?.SetSubWorld( this, value );
			DrawChanged();
		}
	}

	/// <summary>
	/// Include in game layers (inverse of <c>SCENEOBJECTFLAG_EXCLUDE_GAME_LAYER</c>). Does not affect shadow casting.
	/// </summary>
	public bool GameLayers
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	} = true;

	/// <summary>
	/// Redraw in front as a viewmodel, in addition to world rendering (<c>SCENEOBJECTFLAG_GAME_OVERLAY_LAYER</c>).
	/// </summary>
	public bool Overlay
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	}

	/// <summary>
	/// Include in quarter-resolution bloom (<c>SCENEOBJECTFLAG_EFFECTS_BLOOM_LAYER</c>).
	/// Disable <see cref="GameLayers"/> for bloom without a visible surface.
	/// </summary>
	public bool Bloom
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	}

	/// <summary>
	/// Redraw above screen UI without depth (<c>SCENEOBJECTFLAG_UI_OVERLAY_LAYER</c>).
	/// </summary>
	public bool AfterUI
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	}

	/// <summary>
	/// Left out of cube captures (<c>SCENEOBJECTFLAG_HIDE_IN_CUBEMAPS</c>, <see cref="RenderView.Capture"/>).
	/// </summary>
	public bool HiddenInCaptures
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	}

	/// <summary>
	/// Exclusive layer match (<c>SceneObject.RenderLayer</c>). Overrides all layer flags, including shadows,
	/// until cleared; existing flags are retained.
	/// </summary>
	public LayerMatch LayerMatch
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ShadowChanged();
			DrawChanged();
		}
	}

	/// <summary>
	/// Bounds in object space, before <see cref="Transform"/>.
	/// </summary>
	public BBox LocalBounds
	{
		get => localBounds;
		set
		{
			localBounds = value;
			Changed( WorldNote.Sync );
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
