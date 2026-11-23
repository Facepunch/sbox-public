namespace Sandbox;

/// <summary>
/// Put this in a prefab to frame its thumbnail - it's rendered from this object's position and
/// rotation instead of auto-framing the whole prefab. Point it at what should be in shot, or turn on
/// <see cref="FitToBounds"/> to just set the angle and have the prefab fill the frame.
/// </summary>
[Alias( "ThumbnailCamera" )]
[Title( "Thumbnail Camera" ), Category( "Rendering" ), Icon( "photo_camera" )]
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

	internal Transform PrefabView( PrefabFile prefab ) => prefab.GetScene().WorldTransform.ToLocal( WorldTransform );

	internal void Render( PrefabFile prefab, Bitmap bitmap, Model previewModel )
	{
		Render( prefab, bitmap, PrefabView( prefab ), FieldOfView, FitToBounds, Padding, Zoom, Focus, previewModel );
	}

	internal static void Render( PrefabFile prefab, Bitmap bitmap, Transform view, float fieldOfView, bool fit, float padding, float zoom, Vector2 focus, Model previewModel )
	{
		// An editor scene, so nothing in the prefab thinks it's live gameplay (random loadouts etc.)
		var scene = Scene.CreateEditorScene();

		try
		{
			using var scope = scene.Push();
			var root = GameObject.Clone( prefab, new CloneConfig { Transform = global::Transform.Zero, StartEnabled = true } );
			if ( previewModel is not null )
			{
				var renderers = root.GetComponentsInChildren<ModelRenderer>( true ).ToArray();
				var target = renderers.FirstOrDefault( x => x.Model is null );
				if ( target is null && renderers.Length == 0 )
					target = root.AddComponent<ModelRenderer>();

				if ( target.IsValid() )
				{
					target.Model = previewModel;
					target.Enabled = true;
					for ( var go = target.GameObject; go != root; go = go.Parent )
						go.Enabled = true;
				}
			}

			var bounds = root.GetBounds();

			var camera = new GameObject( true, "camera" ).Components.Create<CameraComponent>();
			camera.WorldTransform = root.WorldTransform.ToWorld( view );
			camera.FieldOfView = fieldOfView;

			if ( fit )
				camera.WorldPosition = FitPosition( bounds, camera.WorldRotation, fieldOfView, padding, zoom, focus, bitmap.Width / (float)bitmap.Height );

			SceneUtility.AddThumbnailLights( camera.WorldRotation, bounds );
			camera.BackgroundColor = Color.Transparent;

			camera.RenderToBitmap( bitmap );

			// A tilted, cropped prefab rarely sits in the middle of the frame its box does - centre what
			// actually rendered, top to bottom, and render again
			if ( fit && VerticalOffset( bitmap ) is { } offset )
			{
				var depth = Vector3.Dot( bounds.Center - camera.WorldPosition, camera.WorldRotation.Forward );
				var unitsPerPixel = 2f * depth * MathF.Tan( fieldOfView * 0.5f * MathF.PI / 180f ) / bitmap.Width;

				camera.WorldPosition -= camera.WorldRotation.Up * offset * unitsPerPixel;
				camera.RenderToBitmap( bitmap );
			}
		}
		finally
		{
			scene.Destroy();
		}
	}

	// A nested prefab can bring its own camera. Prefer the one nearest the root.
	internal static ThumbnailCamera Find( GameObject source ) => source?.GetComponentsInChildren<ThumbnailCamera>( true )
		.OrderBy( x => Depth( x.GameObject ) )
		.FirstOrDefault();

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
	/// Where to put a camera looking along <paramref name="rotation"/> so <paramref name="bounds"/>
	/// fills the frame - measured from the box's outline as seen from that angle, so long thin
	/// things fill the frame as well as chunky ones. Then pushed in by <paramref name="zoom"/> and slid
	/// across by <paramref name="focus"/>.
	/// </summary>
	private static Vector3 FitPosition( BBox bounds, Rotation rotation, float fieldOfView, float padding, float zoom, Vector2 focus, float aspect )
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

		var extent = MathF.Max( halfWidth, halfHeight * aspect ) * (1f + padding);
		var distance = extent / MathF.Max( zoom, 0.01f ) / MathF.Tan( fieldOfView * 0.5f * MathF.PI / 180f ) + halfDepth;

		// Slide along the camera's axes without its roll - tilting the camera tilts the subject, and
		// it should slide along the subject, not across it
		var angles = rotation.Angles();
		var level = Rotation.From( angles.pitch, angles.yaw, 0f );
		centre += level.Right * focus.x * extent + level.Up * focus.y * extent;

		return centre - rotation.Forward * distance;
	}
}
