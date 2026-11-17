namespace Sandbox;

/// <summary>
/// Removes water inside a box: the surface isn't drawn there, and swimming, underwater effects, splashes and
/// buoyancy treat the space as dry. Use it for boat hulls, docks or rooms below sea level, and make the box
/// reach above the highest wave crest. Cuts every water body it overlaps, including oceans.
/// A rigidbody that owns the hole, like a boat with a dry hull, still floats.
/// </summary>
[Expose, Title( "Water Hole" ), Category( "Rendering" ), Icon( "crop_free" )]
public sealed class WaterHole : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// The dry region, in local space.
	/// </summary>
	[Property]
	public BBox Bounds
	{
		get;
		set
		{
			field = value;
			MarkDirty();
		}
	} = BBox.FromPositionAndSize( 0, 256 );

	IDisposable _boundsUndoScope;

	/// <summary>
	/// Three rows mapping a world position into the box, which spans -1 to 1 on each axis.
	/// Matches WaterHoleData in the water shader.
	/// </summary>
	internal struct Data
	{
		internal Vector4 X;
		internal Vector4 Y;
		internal Vector4 Z;

		internal readonly bool Contains( Vector3 p ) => MathF.Abs( Vector3.Dot( new Vector3( X.x, X.y, X.z ), p ) + X.w ) <= 1
			&& MathF.Abs( Vector3.Dot( new Vector3( Y.x, Y.y, Y.z ), p ) + Y.w ) <= 1
			&& MathF.Abs( Vector3.Dot( new Vector3( Z.x, Z.y, Z.z ), p ) + Z.w ) <= 1;
	}

	/// <summary>
	/// Whether the box encloses any space. A collapsed one cuts nothing.
	/// </summary>
	internal bool HasVolume => Bounds.Size.x > 0 && Bounds.Size.y > 0 && Bounds.Size.z > 0
		&& WorldScale.x != 0 && WorldScale.y != 0 && WorldScale.z != 0;

	/// <summary>
	/// Whether a world position lies inside the box.
	/// </summary>
	public bool Contains( Vector3 worldPosition ) => HasVolume && Bounds.Contains( WorldTransform.PointToLocal( worldPosition ), 0 );

	/// <summary>
	/// Whether an active hole removes water at a world position. Holes on <paramref name="owner"/> or its
	/// descendants are ignored, so a body keeps the buoyancy of its own dry hull.
	/// </summary>
	internal static bool Cuts( Scene scene, Vector3 position, GameObject owner = null )
		=> scene.GetSystem<WaterHoleSystem>()?.Cuts( position, owner ) ?? false;

	internal Data GetData()
	{
		var transform = WorldTransform;
		var center = Bounds.Center;
		var half = Bounds.Extents;
		return new Data
		{
			X = Row( transform.Rotation.Forward, transform.Scale.x, center.x, half.x ),
			Y = Row( transform.Rotation.Left, transform.Scale.y, center.y, half.y ),
			Z = Row( transform.Rotation.Up, transform.Scale.z, center.z, half.z ),
		};

		// (Dot( axis, p - position ) / scale - center) / half, as a dot product and an offset.
		Vector4 Row( Vector3 axis, float scale, float center, float half )
		{
			var row = axis / (scale * half);
			return new Vector4( row, -Vector3.Dot( row, transform.Position ) - center / half );
		}
	}

	protected override void OnEnabled()
	{
		MarkDirty();
		// Raised for parent moves and interpolation too.
		Transform.OnTransformChanged += MarkDirty;
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= MarkDirty;
		MarkDirty();
		_boundsUndoScope?.Dispose();
		_boundsUndoScope = null;
	}

	void MarkDirty() => Scene?.GetSystem<WaterHoleSystem>()?.MarkDirty();

	protected override void DrawGizmos()
	{
		if ( !Gizmo.Pressed.Any )
		{
			_boundsUndoScope?.Dispose();
			_boundsUndoScope = null;
		}

		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		Gizmo.Draw.Color = new Color( 1, 0.55f, 0.1f, Gizmo.IsSelected ? 0.9f : 0.4f );
		if ( Gizmo.IsSelected && Gizmo.Control.UpdateBoundingBox( "Bounds", Bounds, out var newBounds ) )
		{
			_boundsUndoScope ??= Scene.Editor?.UndoScope( "Resize Water Hole" ).WithComponentChanges( this ).Push();
			Bounds = newBounds;
		}
		Gizmo.Draw.LineBBox( Bounds );
		if ( Gizmo.IsSelected )
			Gizmo.Draw.BoundingBox( "Bounds", Bounds );
	}
}
