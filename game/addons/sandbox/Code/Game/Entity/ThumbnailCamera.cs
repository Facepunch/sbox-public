/// <summary>
/// Put this in a prefab to frame its thumbnail - it's rendered from this object's position and
/// rotation instead of auto-framing the whole prefab. Point it at what should be in shot, or turn on
/// <see cref="FitToBounds"/> to just set the angle and have the prefab fill the frame.
/// </summary>
[Title( "Thumbnail Camera" ), Icon( "photo_camera" )]
public sealed class ThumbnailCamera : Component
{
	/// <summary>
	/// Keep it narrow and pull the camera back - a long lens flattens the perspective and looks
	/// more cinematic than a wide one up close.
	/// </summary>
	[Property, Range( 5, 90 )]
	public float FieldOfView { get; set; } = 15f;

	/// <summary>
	/// How far ahead to draw the framing in the editor - just a guide, it doesn't affect the render.
	/// </summary>
	[Property]
	public float GizmoDistance { get; set; } = 120f;

	/// <summary>
	/// Ignore where the camera is placed - keep its angle, but move it so the prefab's models fill the
	/// frame. Good for things that should all sit the same in a grid, like weapons.
	/// </summary>
	[Property]
	public bool FitToBounds { get; set; }

	/// <summary>
	/// Space left around the prefab when fitting, as a fraction of its size.
	/// </summary>
	[Property, Range( 0, 1 ), ShowIf( nameof( FitToBounds ), true )]
	public float Padding { get; set; } = 0.1f;

	/// <summary>
	/// Push in past the fit - above 1 crops into the prefab, so long things can run off the edge.
	/// </summary>
	[Property, Range( 1, 3 ), ShowIf( nameof( FitToBounds ), true )]
	public float Zoom { get; set; } = 1f;

	/// <summary>
	/// Slide the framing toward the interesting end when zoomed in, as a fraction of the fitted frame -
	/// x toward the right, y up. Measured before any roll on the camera, so a tilted gun slides along
	/// its own length.
	/// </summary>
	[Property, ShowIf( nameof( FitToBounds ), true )]
	public Vector2 Focus { get; set; }

	protected override void DrawGizmos()
	{
		var half = GizmoDistance * MathF.Tan( FieldOfView * 0.5f * MathF.PI / 180f );
		var corners = new[]
		{
			new Vector3( GizmoDistance, half, half ),
			new Vector3( GizmoDistance, -half, half ),
			new Vector3( GizmoDistance, -half, -half ),
			new Vector3( GizmoDistance, half, -half ),
		};

		Gizmo.Draw.Color = Color.Cyan;

		for ( var i = 0; i < corners.Length; i++ )
		{
			Gizmo.Draw.Line( Vector3.Zero, corners[i] );
			Gizmo.Draw.Line( corners[i], corners[(i + 1) % corners.Length] );
		}
	}

	/// <summary>
	/// Render <paramref name="prefab"/> to <paramref name="bitmap"/> through the thumbnail camera it
	/// contains. Returns false if it doesn't have one.
	/// </summary>
	public static bool TryRender( PrefabFile prefab, Bitmap bitmap )
	{
		var source = prefab?.GetScene();

		// The one nearest the root - prefabs nested inside this one (an NPC's gun) bring their own
		var framing = source?.GetComponentsInChildren<ThumbnailCamera>( true )
			.OrderBy( x => Depth( x.GameObject ) )
			.FirstOrDefault();

		if ( !framing.IsValid() )
			return false;

		// Where the camera sits relative to the prefab root
		var view = source.WorldTransform.ToLocal( framing.WorldTransform );
		var fieldOfView = framing.FieldOfView;
		var fit = framing.FitToBounds;
		var padding = framing.Padding;
		var zoom = framing.Zoom;
		var focus = framing.Focus;

		// An editor scene, so nothing in the prefab thinks it's live gameplay (random loadouts etc.)
		var scene = Scene.CreateEditorScene();

		using ( scene.Push() )
		{
			var root = GameObject.Clone( prefab, new CloneConfig { Transform = global::Transform.Zero, StartEnabled = true } );
			var bounds = root.GetBounds();

			var camera = new GameObject( true, "camera" ).Components.Create<CameraComponent>();
			camera.WorldTransform = root.WorldTransform.ToWorld( view );
			camera.FieldOfView = fieldOfView;

			if ( fit )
				camera.WorldPosition = FitPosition( bounds, camera.WorldRotation, fieldOfView, padding, zoom, focus );

			AddLights( camera.WorldRotation, bounds );
			camera.BackgroundColor = Color.Transparent;

			camera.RenderToBitmap( bitmap );

			// A tilted, cropped prefab rarely sits in the middle of the frame its box does - centre what
			// actually rendered, top to bottom, and render again
			if ( fit && VerticalOffset( bitmap ) is { } offset )
			{
				var depth = Vector3.Dot( bounds.Center - camera.WorldPosition, camera.WorldRotation.Forward );
				var unitsPerPixel = 2f * depth * MathF.Tan( fieldOfView * 0.5f * MathF.PI / 180f ) / bitmap.Height;

				camera.WorldPosition -= camera.WorldRotation.Up * offset * unitsPerPixel;
				camera.RenderToBitmap( bitmap );
			}
		}

		scene.Destroy();
		return true;
	}

	/// <summary>
	/// How far below the middle of the image the middle of what's drawn is, in pixels - null if
	/// nothing was drawn. Halfway between the middle of its outline and its centre of mass: a rifle's
	/// thin barrel along the top shouldn't count as much as the body hanging under it, but its
	/// magazine shouldn't drag it off the top either.
	/// </summary>
	private static float? VerticalOffset( Bitmap bitmap )
	{
		var pixels = bitmap.GetPixels();
		int top = -1, bottom = -1;
		float mass = 0, moment = 0;

		for ( var y = 0; y < bitmap.Height; y++ )
		{
			var row = 0f;
			for ( var x = 0; x < bitmap.Width; x++ )
				row += pixels[y * bitmap.Width + x].a;

			if ( row < 0.5f )
				continue;

			if ( top < 0 ) top = y;
			bottom = y;
			mass += row;
			moment += row * (y + 0.5f);
		}

		if ( top < 0 )
			return null;

		var outline = (top + bottom + 1) * 0.5f;
		var centre = moment / mass;
		return (outline + centre) * 0.5f - bitmap.Height * 0.5f;
	}

	private static int Depth( GameObject go )
	{
		var depth = 0;
		for ( var parent = go.Parent; parent.IsValid(); parent = parent.Parent )
			depth++;

		return depth;
	}

	/// <summary>
	/// Studio lighting that follows the camera, so every thumbnail is lit the same way: a warm key
	/// from above-left, a soft fill from beside the camera, and a cool rim from behind to pick out
	/// the edges.
	/// The rim is a point light - a scene only takes one directional light, a second replaces the first.
	/// </summary>
	private static void AddLights( Rotation view, BBox bounds )
	{
		var key = new GameObject( true, "key" ).Components.Create<DirectionalLight>();
		key.WorldRotation = Rotation.LookAt( (view.Forward + view.Right * 0.7f - view.Up * 0.9f).Normal );
		key.LightColor = new Color( 1f, 0.95f, 0.88f ) * 0.8f;
		key.SkyColor = new Color( 0.55f, 0.6f, 0.7f ) * 0.55f;
		key.Shadows = false;

		var size = bounds.Size.Length;

		// A soft fill from beside the camera, so dark metal facing us doesn't go black
		var fill = new GameObject( true, "fill" ).Components.Create<PointLight>();
		fill.WorldPosition = bounds.Center - view.Forward * size * 1.5f + view.Right * size * 0.6f;
		fill.Radius = size * 4f;
		fill.Attenuation = 0f;
		fill.LightColor = new Color( 1f, 0.97f, 0.92f ) * 0.3f;
		fill.Shadows = false;

		var rim = new GameObject( true, "rim" ).Components.Create<PointLight>();
		rim.WorldPosition = bounds.Center + view.Forward * size * 0.6f + view.Left * size * 0.5f + view.Up * size * 0.5f;
		rim.Radius = size * 4f;
		rim.Attenuation = 0f;
		rim.LightColor = new Color( 0.7f, 0.85f, 1f ) * 6f;
		rim.Shadows = false;
	}

	/// <summary>
	/// Where to put a camera looking along <paramref name="rotation"/> so <paramref name="bounds"/>
	/// fills a square frame - measured from the box's outline as seen from that angle, so long thin
	/// things fill the frame as well as chunky ones. Then pushed in by <paramref name="zoom"/> and slid
	/// across by <paramref name="focus"/>.
	/// </summary>
	private static Vector3 FitPosition( BBox bounds, Rotation rotation, float fieldOfView, float padding, float zoom, Vector2 focus )
	{
		var centre = bounds.Center;
		float halfWidth = 0, halfHeight = 0, halfDepth = 0;

		foreach ( var corner in bounds.Corners )
		{
			// Camera space: x forward, y left, z up
			var local = rotation.Inverse * (corner - centre);
			halfDepth = MathF.Max( halfDepth, MathF.Abs( local.x ) );
			halfWidth = MathF.Max( halfWidth, MathF.Abs( local.y ) );
			halfHeight = MathF.Max( halfHeight, MathF.Abs( local.z ) );
		}

		var extent = MathF.Max( halfWidth, halfHeight ) * (1f + padding);
		var distance = extent / MathF.Max( zoom, 0.01f ) / MathF.Tan( fieldOfView * 0.5f * MathF.PI / 180f ) + halfDepth;

		// Slide along the camera's axes without its roll - tilting the camera tilts the subject, and
		// it should slide along the subject, not across it
		var angles = rotation.Angles();
		var level = Rotation.From( angles.pitch, angles.yaw, 0f );
		centre += level.Right * focus.x * extent + level.Up * focus.y * extent;

		return centre - rotation.Forward * distance;
	}

}
