using Sandbox;

namespace Editor.TerrainEditor;

/// <summary>
/// Pending terrain bounds published by the rescale / move popups so they can be drawn in the
/// viewport while values are being edited. Purely visual - nothing here touches storage.
/// The owner is whichever popup is driving the preview; it draws from
/// <see cref="EditorEvent.ISceneView.DrawGizmos"/> so the preview shows under every editor tool,
/// not just the object tool.
/// </summary>
internal static class TerrainBoundsPreview
{
	static object owner;

	static Terrain Target;
	static Vector3 Position;
	static float Size;
	static float Height;

	/// <summary>Bounds the pending operation drops.</summary>
	static BBox[] Lost = Array.Empty<BBox>();

	/// <summary>Bounds the pending operation gains, filled from the border average.</summary>
	static BBox[] Added = Array.Empty<BBox>();

	public static void Show( object source, Terrain terrain, Vector3 position, float size, float height,
		BBox[] lost = null, BBox[] added = null )
	{
		if ( !terrain.IsValid() || size <= 0.0f || height <= 0.0f
			|| !float.IsFinite( size ) || !float.IsFinite( height )
			|| !float.IsFinite( position.x ) || !float.IsFinite( position.y ) || !float.IsFinite( position.z ) )
		{
			Clear( source );
			return;
		}

		owner = source;
		Target = terrain;
		Position = position;
		Size = size;
		Height = height;
		Lost = lost ?? Array.Empty<BBox>();
		Added = added ?? Array.Empty<BBox>();
	}

	/// <summary>
	/// Only the popup that owns the preview may clear it, so a closing dialog can't wipe another's.
	/// </summary>
	public static void Clear( object source )
	{
		if ( owner is not null && !ReferenceEquals( owner, source ) )
			return;

		owner = null;
		Target = null;
		Lost = Array.Empty<BBox>();
		Added = Array.Empty<BBox>();
	}

	public static void Draw( object source )
	{
		if ( !ReferenceEquals( owner, source ) ) return;

		var terrain = Target;
		if ( !terrain.IsValid() || terrain.Storage is null ) return;

		var rotation = terrain.WorldRotation;
		var storage = terrain.Storage;

		using ( Gizmo.Scope( "TerrainBoundsCurrent", new Transform( terrain.WorldPosition, rotation ) ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.LineThickness = 1;
			Gizmo.Draw.Color = Color.White.WithAlpha( 0.35f );
			Gizmo.Draw.LineBBox( new BBox( Vector3.Zero, new Vector3( storage.TerrainSize, storage.TerrainSize, storage.TerrainHeight ) ) );
		}

		using ( Gizmo.Scope( "TerrainBoundsPending", new Transform( Position, rotation ) ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.Color = Color.Orange;
			Gizmo.Draw.LineBBox( new BBox( Vector3.Zero, new Vector3( Size, Size, Height ) ) );

			Gizmo.Draw.ScreenText( $"{Size:0.#} x {Size:0.#} x {Height:0.#}",
				Gizmo.Camera.ToScreen( Position + rotation * new Vector3( Size * 0.5f, Size * 0.5f, Height ) ),
				size: 14 );
		}

		// Regions are old-footprint-local. Solid only - outlines turned every slab into a thicket.
		using ( Gizmo.Scope( "TerrainRegions", new Transform( terrain.WorldPosition, rotation ) ) )
		{
			Gizmo.Draw.IgnoreDepth = true;

			Gizmo.Draw.Color = Color.Red.WithAlpha( 0.2f );
			foreach ( var box in Lost )
				Gizmo.Draw.SolidBox( box );

			Gizmo.Draw.Color = Color.Green.WithAlpha( 0.2f );
			foreach ( var box in Added )
				Gizmo.Draw.SolidBox( box );
		}
	}
}
