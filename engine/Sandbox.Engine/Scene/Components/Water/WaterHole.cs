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
	public BBox Bounds { get; set; } = BBox.FromPositionAndSize( 0, 256 );

	IDisposable _boundsUndoScope;
	static readonly List<WaterHole> _queries = [];

	/// <summary>
	/// Three rows mapping a world position into the box, which spans -1 to 1 on each axis.
	/// Matches WaterHoleData in the water shader.
	/// </summary>
	internal struct Data
	{
		internal Vector4 X;
		internal Vector4 Y;
		internal Vector4 Z;
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
	{
		bool cut = false;
		_queries.Clear();
		scene.GetAll( _queries );
		foreach ( var hole in _queries )
		{
			if ( !hole.Active || (owner.IsValid() && hole.GameObject.IsAncestor( owner )) || !hole.Contains( position ) )
				continue;

			cut = true;
			break;
		}
		_queries.Clear();
		return cut;
	}

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

	protected override void OnDisabled()
	{
		_boundsUndoScope?.Dispose();
		_boundsUndoScope = null;
	}

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
