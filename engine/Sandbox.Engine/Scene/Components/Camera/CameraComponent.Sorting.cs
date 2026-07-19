namespace Sandbox;

/// <summary>
/// How sorted sprites are ordered against each other by a camera, once their sort layer and
/// order in layer have been compared and come out equal.
/// </summary>
[Expose]
public enum TransparencySortMode
{
	/// <summary>
	/// Follow the camera's projection - distance from the camera when perspective, depth along
	/// the view direction when orthographic.
	/// </summary>
	[Icon( "auto_awesome" )]
	Default,

	/// <summary>
	/// Always sort by distance from the camera position, even in an orthographic view.
	/// </summary>
	[Icon( "videocam" )]
	Perspective,

	/// <summary>
	/// Always sort by depth along the camera's view direction, even in a perspective view.
	/// </summary>
	[Icon( "crop_free" )]
	Orthographic,

	/// <summary>
	/// Sort by position along <see cref="CameraComponent.TransparencySortAxis"/>, ignoring where
	/// the camera is. This is how top-down and isometric games make characters occlude each other
	/// by their feet.
	/// </summary>
	[Icon( "straighten" )]
	CustomAxis,
}

public sealed partial class CameraComponent
{
	/// <summary>
	/// How this camera breaks ties between sprites that share a sort layer and order in layer.
	/// Only affects sprites with <see cref="SpriteRenderer.IsSorted"/> enabled.
	/// </summary>
	[Property, Category( "Sorting" ), Order( 300 )]
	public TransparencySortMode TransparencySort { get; set; } = TransparencySortMode.Default;

	/// <summary>
	/// The axis sprites are sorted along when <see cref="TransparencySort"/> is
	/// <see cref="TransparencySortMode.CustomAxis"/>. A sprite further along this axis draws
	/// behind one that is less far along it - so for a top-down game the default (0,1,0) makes a
	/// character lower on the screen walk in front of one higher up.
	/// </summary>
	[Property, Category( "Sorting" ), Order( 301 )]
	[ShowIf( nameof( TransparencySort ), TransparencySortMode.CustomAxis )]
	public Vector3 TransparencySortAxis { get; set; } = new( 0, 1, 0 );

	/// <summary>
	/// The axis handed to the sprite compute shader for a given mode. Only
	/// <see cref="TransparencySortMode.CustomAxis"/> sorts along an axis at all, so every other
	/// mode resolves to zero - which the shader reads as "sort by camera depth instead".
	///
	/// A zero axis in CustomAxis mode stays zero rather than becoming a NaN, so a half-configured
	/// camera degrades to the old behaviour instead of corrupting the sort.
	/// </summary>
	internal static Vector3 ResolveSortAxis( TransparencySortMode mode, Vector3 axis )
	{
		if ( mode != TransparencySortMode.CustomAxis ) return Vector3.Zero;

		// Normal returns the vector untouched when it is near-zero, so this cannot produce NaN.
		return axis.Normal;
	}

	/// <summary>
	/// Pushes the sorting settings onto the camera so the sprite compute shader can read them.
	/// A camera that never touches these renders exactly as it did before they existed.
	/// </summary>
	internal void UpdateSortingAttributes( SceneCamera camera )
	{
		camera.Attributes.Set( "SpriteSortMode", (int)TransparencySort );
		camera.Attributes.Set( "SpriteSortAxis", ResolveSortAxis( TransparencySort, TransparencySortAxis ) );
	}

	/// <summary>
	/// Draws the sort axis in front of the camera while it is selected. "Which way is behind?" is
	/// the question a custom axis immediately raises, and a number in the inspector answers it far
	/// less well than an arrow does.
	/// </summary>
	private void DrawSortAxisGizmo()
	{
		var axis = ResolveSortAxis( TransparencySort, TransparencySortAxis );
		if ( axis.IsNearZeroLength ) return;

		// Out in front of the camera, so the arrow lands somewhere the camera can actually see
		// rather than on top of the frustum apex.
		var origin = WorldPosition + WorldRotation.Forward * 128f;
		var length = 48f;

		Gizmo.Draw.Color = Color.Cyan.WithAlpha( 0.9f );
		Gizmo.Draw.Arrow( origin - axis * length, origin + axis * length, 12f, 4f );

		// The arrow alone does not say which end wins, and guessing wrong inverts the whole scene.
		Gizmo.Draw.Color = Color.Cyan.WithAlpha( 0.6f );
		Gizmo.Draw.ScreenText( "behind", Gizmo.Camera.ToScreen( origin + axis * length ), size: 12 );
		Gizmo.Draw.ScreenText( "in front", Gizmo.Camera.ToScreen( origin - axis * length ), size: 12 );
	}
}
