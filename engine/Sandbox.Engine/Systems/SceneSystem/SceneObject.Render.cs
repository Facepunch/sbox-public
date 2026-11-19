namespace Sandbox;

public partial class SceneObject
{
	/// <summary>
	/// Whether it makes and writes its own render object (<see cref="MakeRenderObject"/>, <see cref="WriteRender"/>): its world's
	/// renderer adds it and removes it, and a change made on the main thread outside a render of a world holding it goes straight
	/// into it. Other kinds are made and written by their world's renderer.
	/// </summary>
	internal virtual bool OwnsRenderObject => ResolvesDraws && GetType() == typeof( SceneObject );

	/// <summary>
	/// Make the render object it draws as (<see cref="OwnsRenderObject"/>). Its world's renderer adds it to its render world and
	/// writes everything into it.
	/// </summary>
	internal virtual Borealis.RenderObject MakeRenderObject() => new Borealis.MeshObject( null, Transform );

	/// <summary>
	/// Write what changed into its render object (<see cref="OwnsRenderObject"/>), with <paramref name="render"/> for the
	/// device resources it draws with.
	/// </summary>
	internal virtual void WriteRender( Borealis.RenderObject obj, Rendering.SceneObjectChange change, Rendering.ISceneWorldRender render )
	{
		if ( (change & Rendering.SceneObjectChange.Tags) != 0 ) obj.Tags = RenderTags();

		// A model managed code made (ResolvesDraws): its draws are worked out from the model's mesh the world holds
		if ( obj is Borealis.MeshObject mesh && GetType() == typeof( SceneObject ) ) render.WriteMesh( this, mesh, change );
	}

	/// <summary>
	/// Its tags as string tokens, as a render object filters by them (<see cref="TagTokens"/>).
	/// </summary>
	internal uint[] RenderTags()
	{
		var tokens = TagTokens;
		return tokens.Count == 0 ? [] : [.. tokens];
	}

	static readonly uint StaticOverlayMatch = new StringToken( "StaticOverlayLayer" ).Value;
	static readonly uint OverlayWithDepthMatch = new StringToken( "OverlayWithDepth" ).Value;
	static readonly uint OverlayWithoutDepthMatch = new StringToken( "OverlayWithoutDepth" ).Value;

	/// <summary>
	/// A layer match (<c>CSceneObject::m_nLayerMatchID</c>) as Borealis knows it; its features decide membership.
	/// </summary>
	internal static Borealis.LayerMatch LayerMatchFor( uint match )
	{
		if ( match == 0 ) return Borealis.LayerMatch.None;
		if ( match == StaticOverlayMatch ) return Borealis.LayerMatch.StaticOverlay;
		if ( match == OverlayWithDepthMatch ) return Borealis.LayerMatch.OverlayWithDepth;
		if ( match == OverlayWithoutDepthMatch ) return Borealis.LayerMatch.OverlayWithoutDepth;
		return Borealis.LayerMatch.Unsupported;
	}

	/// <summary>
	/// Game, overlay and effect layers from native's <c>RenderOptions</c> flags, and whether cube captures leave it out.
	/// </summary>
	internal static void WriteLayerFlags( Borealis.RenderObject obj, SceneObjectFlagAccessor flags )
	{
		obj.GameLayers = !flags.HasFlag( Rendering.SceneObjectFlags.ExcludeGameLayer );
		obj.Overlay = flags.HasFlag( Rendering.SceneObjectFlags.GameOverlayLayer );
		obj.Bloom = flags.HasFlag( Rendering.SceneObjectFlags.EffectsBloomLayer );
		obj.AfterUI = flags.HasFlag( Rendering.SceneObjectFlags.UIOverlayLayer );
		obj.HiddenInCaptures = flags.HasFlag( Rendering.SceneObjectFlags.HideInCubemaps );
	}

	/// <summary>
	/// A custom or dynamic object's placement, culling bounds and flags.
	/// </summary>
	private protected void WriteCustom( Borealis.CustomObject custom, Rendering.SceneObjectChange change )
	{
		const Rendering.SceneObjectChange Placement = Rendering.SceneObjectChange.Transform | Rendering.SceneObjectChange.Bounds;
		if ( (change & Placement) != 0 )
		{
			// Unplaced custom objects may report zero scale; native uses identity.
			var transform = Transform;
			custom.Transform = transform == default ? Transform.Zero : transform;

			// Bounds as kept (WorldBounds): Bounds collapses infinity to a point. Large finite bounds keep culling math valid
			var bounds = WorldBounds;
			if ( !float.IsFinite( bounds.Mins.x + bounds.Mins.y + bounds.Mins.z + bounds.Maxs.x + bounds.Maxs.y + bounds.Maxs.z ) || bounds.Size.x > 1e9f )
				bounds = new BBox( new Vector3( -1e9f ), new Vector3( 1e9f ) );

			custom.LocalBounds = ToLocal( bounds, custom.Transform );
		}

		const Rendering.SceneObjectChange State = Rendering.SceneObjectChange.Tint | Rendering.SceneObjectChange.Flags | Rendering.SceneObjectChange.Visibility;
		if ( (change & State) != 0 )
		{
			var flags = Flags;
			custom.Tint = ColorTint;
			custom.IsOpaque = flags.IsOpaque && !flags.IsTranslucent;
			custom.WantsFrameBufferCopy = flags.WantsFrameBufferCopy;
			custom.DepthPrepass = !flags.HasFlag( Rendering.SceneObjectFlags.NoZPrepass );
			custom.CastShadows = flags.CastShadows;
			custom.Visible = RenderingEnabled;
			WriteLayerFlags( custom, flags );
			custom.LayerMatch = LayerMatchFor( LayerMatchToken );
		}
	}

	/// <summary>
	/// A world box in an object's space: the box around its corners taken back through the transform.
	/// </summary>
	internal static BBox ToLocal( BBox world, Transform transform )
	{
		var mins = new Vector3( float.MaxValue );
		var maxs = new Vector3( float.MinValue );

		for ( int i = 0; i < 8; i++ )
		{
			var corner = new Vector3( (i & 1) == 0 ? world.Mins.x : world.Maxs.x, (i & 2) == 0 ? world.Mins.y : world.Maxs.y, (i & 4) == 0 ? world.Mins.z : world.Maxs.z );
			var local = transform.PointToLocal( corner );
			mins = Vector3.Min( mins, local );
			maxs = Vector3.Max( maxs, local );
		}

		return new BBox( mins, maxs );
	}

	/// <summary>
	/// What waits for its world's renderer even for an object that writes its own render object: coming and going, and its
	/// attributes being made. A model's draws are worked out once per sync for however many model and material changes came.
	/// </summary>
	const Rendering.SceneObjectChange RendererChanges = Rendering.SceneObjectChange.Added | Rendering.SceneObjectChange.Removed
		| Rendering.SceneObjectChange.Deleted | Rendering.SceneObjectChange.Attributes | Rendering.SceneObjectChange.Model
		| Rendering.SceneObjectChange.Material;

	/// <summary>
	/// Write a change straight into its own render object if it can: one that owns it, made already, on the main thread, outside
	/// a render of a world holding it. Otherwise its world's renderer takes the change, and writes it before the next render.
	/// </summary>
	bool WroteRender( Rendering.SceneObjectChange change )
	{
		if ( (change & RendererChanges) != 0 || !OwnsRenderObject || RenderObject is not { World: not null } obj ) return false;
		if ( World?.Render is not { } render || obj.InRender || !ThreadSafe.IsMainThread ) return false;

		using ( Borealis.Zones.ObjectWrite.Start() ) WriteRender( obj, change, render );
		return true;
	}
}
