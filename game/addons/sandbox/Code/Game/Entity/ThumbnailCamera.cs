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

		var view = source.WorldTransform.ToLocal( framing.WorldTransform );
		Render( prefab, bitmap, view, framing.FieldOfView, framing.FitToBounds, framing.Padding, framing.Zoom, framing.Focus );
		return true;
	}

	/// <summary>
	/// Frame a tool's model in a still scene so physics can't move it out of shot.
	/// </summary>
	internal static Bitmap RenderToolModel( PrefabFile prefab, int width, int height )
	{
		if ( prefab is null ) return null;

		var bitmap = new Bitmap( width, height );
		bitmap.Clear( Color.Transparent );
		var view = new Transform( Vector3.Zero, Rotation.From( 20, 225, 0 ) );
		Render( prefab, bitmap, view, 25f, true, 0.12f, 1f, Vector2.Zero );
		return bitmap;
	}

	private static void Render( PrefabFile prefab, Bitmap bitmap, Transform view, float fieldOfView, bool fit, float padding, float zoom, Vector2 focus )
	{
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
	}

	/// <summary>
	/// The angle <paramref name="prefab"/>'s thumbnail camera looks from, relative to the prefab. Null without one.
	/// </summary>
	public static Rotation? ViewOf( PrefabFile prefab )
	{
		var source = prefab?.GetScene();
		var framing = source?.GetComponentsInChildren<ThumbnailCamera>( true ).OrderBy( x => Depth( x.GameObject ) ).FirstOrDefault();
		return framing.IsValid() ? source.WorldTransform.ToLocal( framing.WorldTransform ).Rotation : null;
	}

	/// <summary>
	/// Render <paramref name="model"/> to fill <paramref name="bitmap"/> under the same studio lighting, posed in
	/// <paramref name="sequence"/>, or its idle if it doesn't have that. Framed by what actually rendered rather than
	/// its bounds, which for mounted models can be far from the posed shape. Weapons are seen from
	/// <paramref name="weaponView"/>, relative to the way they point; everything else from the front three-quarters.
	/// </summary>
	public static void RenderModel( Model model, string sequence, Rotation? weaponView, Bitmap bitmap )
	{
		const float fieldOfView = 20f;
		const float fill = 0.88f;

		var tan = MathF.Tan( fieldOfView * 0.5f * MathF.PI / 180f );
		var scene = Scene.CreateEditorScene();
		Sandbox.Animation.Graph graph = null;

		try
		{
			using var _ = scene.Push();
			var renderer = new GameObject( true, "model" ).Components.Create<SkinnedModelRenderer>();
			renderer.Model = model;
			// Editor scenes otherwise leave a graph unevaluated and show the bind pose.
			renderer.PlayAnimationsInEditorScene = true;

			var skeleton = model.AnimationSkeleton();
			if ( skeleton.IsValid() && PoseClip( model, sequence ) is { } clip )
			{
				var builder = Sandbox.Animation.Graph.Builder;
				builder.Skeleton = skeleton.Name;
				var pose = builder.Pose( clip, new Sandbox.Animation.GraphBuilder.PoseSettings() );
				// A masked sequence, such as a Counter-Strike player's Idle_Upper_AK, is a layer: lay it over the idle.
				var mask = (model.GetData<ModelSequenceData[]>() ?? []).FirstOrDefault( x => x.Name.Equals( sequence, StringComparison.OrdinalIgnoreCase ) )?.BoneMask;
				if ( !string.IsNullOrEmpty( mask ) && PoseClip( model, null ) is { } idle )
				{
					var layers = builder.Layers( builder.Pose( idle, new Sandbox.Animation.GraphBuilder.PoseSettings() ) );
					layers.AddLayer( pose, new() { BlendMode = Sandbox.Animation.GraphBuilder.LayerBlendMode.Overlay, BoneMask = builder.BoneMask( mask ) } );
					pose = layers;
				}
				graph = builder.Create( pose, "thumbnail" );
				renderer.Animation.Graph = graph;
				renderer.UseAnimGraph = true;
			}

			scene.GameTick( 0.1f );
			scene.GameTick( 0.1f );

			var bounds = renderer.Bounds;

			var rotation = weaponView is { } view ? WeaponFrame( model, bounds ) * view : Rotation.From( 12, 215, 0 );

			var camera = new GameObject( true, "camera" ).Components.Create<CameraComponent>();
			camera.WorldRotation = rotation;
			camera.FieldOfView = fieldOfView;
			camera.ZNear = 1f;
			camera.ZFar = 100000f;
			camera.BackgroundColor = Color.Transparent;

			// Start far enough back that nothing is cropped, then frame what rendered
			camera.WorldPosition = bounds.Center - rotation.Forward * bounds.Size.Length * 0.65f / tan;

			AddLights( rotation, bounds );

			for ( var pass = 0; pass < 3; pass++ )
			{
				bitmap.Clear( Color.Transparent );
				camera.RenderToBitmap( bitmap );

				if ( pass == 2 || DrawnRect( bitmap ) is not { } drawn )
					break;

				var depth = Vector3.Dot( bounds.Center - camera.WorldPosition, rotation.Forward );
				var unitsPerPixel = 2f * depth * tan / bitmap.Height;
				var offset = drawn.Center - new Vector2( bitmap.Width, bitmap.Height ) * 0.5f;
				var scale = MathF.Max( drawn.Width / bitmap.Width, drawn.Height / bitmap.Height ) / fill;

				camera.WorldPosition += rotation.Right * offset.x * unitsPerPixel - rotation.Up * offset.y * unitsPerPixel;
				camera.WorldPosition += rotation.Forward * depth * (1f - scale);
			}
		}
		finally
		{
			// The graph outlives the scene otherwise: one per mounted icon.
			scene.Destroy();
			graph?.Dispose();
		}
	}

	/// <summary>
	/// Which way a mounted weapon model points. They lie in whatever orientation the hand bone wants. The barrel points
	/// the way its muzzle does, or failing that along its longest side away from the grip; the thinner of the other two
	/// sides is its width, and the grip hangs below the barrel.
	/// </summary>
	private static Rotation WeaponFrame( Model model, BBox bounds )
	{
		var size = bounds.Size;
		var centre = bounds.Center;
		var muzzles = model.Attachments.All
			.Where( x => x.Name.Contains( "muzzle", StringComparison.OrdinalIgnoreCase ) )
			.Select( x => x.WorldTransform )
			.ToArray();

		// Some muzzle attachments sit off to the side of the model, or off it entirely: only trust one out toward the end it points at
		bool PointsOut( Transform muzzle )
		{
			var axis = MainAxis( muzzle.Rotation.Forward );
			var reach = (muzzle.Position - centre)[axis] * MathF.Sign( muzzle.Rotation.Forward[axis] );
			return bounds.Grow( 1f ).Contains( muzzle.Position ) && reach >= size[axis] * 0.25f;
		}

		int forward;
		float ahead;
		var barrel = muzzles.Where( PointsOut ).Select( x => x.Rotation.Forward ).FirstOrDefault();

		if ( barrel != Vector3.Zero )
		{
			forward = MainAxis( barrel );
			ahead = barrel[forward];
		}
		else
		{
			forward = MainAxis( size );

			// It's held by its hand bone, or its origin without one
			var hand = model.Bones.AllBones.FirstOrDefault( x => x.Name.EndsWith( "R_Hand", StringComparison.OrdinalIgnoreCase ) );
			var grip = hand is null ? Vector3.Zero : model.GetBoneTransform( hand.Index ).Position;
			ahead = centre[forward] - grip[forward];

			// Held near its middle, like a grenade: no telling
			if ( MathF.Abs( ahead ) < 1f )
				ahead = 0f;
		}

		// Two guns side by side are wider than they are long: the width is the thinnest side once the gap between their muzzles is taken out
		var up = Enumerable.Range( 0, 3 )
			.Where( axis => axis != forward )
			.OrderBy( axis => size[axis] - (muzzles.Length == 0 ? 0f : muzzles.Max( x => x.Position[axis] ) - muzzles.Min( x => x.Position[axis] )) )
			.Last();

		// Standing on end, like a grenade or a knife, there's no grip under a barrel to go by
		var front = Vector3.Zero;
		front[forward] = ahead < 0f ? -1 : 1;
		var top = Vector3.Zero;
		top[up] = forward == 2 ? 1 : UpSign( bounds, up );

		return Rotation.LookAt( front, top );
	}

	/// <summary>
	/// Which way is up along <paramref name="axis"/> of a gun's bounds: +Z for one standing upright, else away from the side
	/// reaching further from the origin, its barrel line, where a gun lying on its side hangs its grip.
	/// </summary>
	private static float UpSign( BBox bounds, int axis ) => axis == 2 || bounds.Center[axis] < bounds.Size[axis] * 0.05f ? 1 : -1;

	/// <summary>
	/// The model axis closest to <paramref name="direction"/>.
	/// </summary>
	private static int MainAxis( Vector3 direction )
	{
		var axis = MathF.Abs( direction.y ).CompareTo( MathF.Abs( direction.x ) ) > 0 ? 1 : 0;
		return MathF.Abs( direction.z ).CompareTo( MathF.Abs( direction[axis] ) ) > 0 ? 2 : axis;
	}

	/// <summary>
	/// The clip called <paramref name="sequence"/>, or the model's idle activity if there isn't one.
	/// </summary>
	private static Sandbox.Animation.Clip PoseClip( Model model, string sequence )
	{
		var clips = model.AnimationClips();
		if ( clips.TryGetValue( sequence, out var clip ) )
			return clip;

		var idle = (model.GetData<ModelSequenceData[]>() ?? [])
			.Where( x => x.Activity.Equals( "ACT_IDLE", StringComparison.OrdinalIgnoreCase ) )
			.OrderByDescending( x => x.ActivityWeight );

		foreach ( var candidate in idle )
			if ( clips.TryGetValue( candidate.Name, out clip ) )
				return clip;

		return null;
	}

	/// <summary>
	/// The part of the image something was drawn in, null if nothing was.
	/// </summary>
	private static Rect? DrawnRect( Bitmap bitmap )
	{
		var pixels = bitmap.GetPixels();
		int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;

		for ( var y = 0; y < bitmap.Height; y++ )
		{
			for ( var x = 0; x < bitmap.Width; x++ )
			{
				if ( pixels[y * bitmap.Width + x].a < 0.25f )
					continue;

				left = Math.Min( left, x );
				right = Math.Max( right, x );
				top = Math.Min( top, y );
				bottom = Math.Max( bottom, y );
			}
		}

		return right < 0 ? null : new Rect( left, top, right - left + 1, bottom - top + 1 );
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
