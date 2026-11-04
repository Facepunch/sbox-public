namespace Editor.AnimGraph;

/// <summary>
/// Base for 3D model preview widgets - a lit scene with a treadmill ground and
/// camera controls (LMB orbit, RMB drag zoom, MMB pan, wheel zoom).
/// </summary>
public class PreviewSceneWidget : Widget
{
	protected Scene Scene { get; private set; }
	protected CameraComponent Camera { get; private set; }
	private protected TreadmillGround Ground { get; private set; }
	protected GameObject ModelObject { get; private set; }
	protected SkinnedModelRenderer Renderer { get; private set; }
	protected Model Model { get; private set; }
	public Skeleton PreviewSkeleton { get; private set; }
	private readonly PreviewThrobber _throbber;

	/// <summary>Scroll the ground against root motion. Turn off when the model shouldn't treadmill, e.g. ragdolled.</summary>
	protected bool TreadmillEnabled { get; set; } = true;

	/// <summary>When set, the orbit camera sticks to this bone instead of the framed center.</summary>
	public string FocusBone { get; protected set; }

	/// <summary>The model actually in the preview, which isn't always the one that was asked for.</summary>
	public Model PreviewModel => Renderer.IsValid() ? Renderer.Model : null;

	/// <summary>The visualization options on this preview's toolbar, if it offers any.</summary>
	internal PreviewOptions Options { get; set; }

	/// <summary>Draw the model's skeleton over it, with a label on whatever the cursor is near.</summary>
	public bool ShowBones { get; set; }

	/// <summary>Draw the root motion the model is reporting as an arrow on the ground.</summary>
	public bool ShowRootMotion { get; set; }

	private bool _showGround = true;

	/// <summary>Show the ground plane under the model.</summary>
	public bool ShowGround
	{
		get => _showGround;
		set
		{
			_showGround = value;
			Ground.Visible = value && _groundAllowed;
		}
	}

	private bool _groundAllowed = true;

	/// <summary>
	/// Set false where a ground makes no sense at all, like a viewmodel floating in front of a
	/// camera. The option is left alone, so it comes back with a model that can use it.
	/// </summary>
	protected bool GroundAllowed
	{
		get => _groundAllowed;
		set
		{
			_groundAllowed = value;
			Ground.Visible = _showGround && value;
		}
	}

	/// <summary>The orbit camera's field of view.</summary>
	public float FieldOfView { get; set; } = 60f;

	private readonly List<string> _hoveredBones = [];
	private Vector3 _rootVelocity;
	private int _stepFrames;

	/// <summary>Advance the scene a single frame, ignoring time scale.</summary>
	protected void StepFrame() => _stepFrames++;

	private readonly RenderView _renderView;

	private Vector2 _lastCursorPos;
	private Vector2 _angles = new( 135f, 20f );
	private Vector3 _origin;
	private Vector3 _center;
	private float _distance = 200f;
	private bool _orbit;
	private bool _zoom;
	private bool _pan;
	private float _dragDistance;

	/// <summary>True while a mouse button is dragging the camera (orbit, zoom or pan).</summary>
	protected bool CameraDragging => _orbit || _zoom || _pan;

	/// <summary>True while the cursor is over the viewport.</summary>
	protected bool ViewIsHovered => _renderView.IsUnderMouse;

	private PhysicsBody _mouseBody;
	private Sandbox.Physics.ControlJoint _mouseJoint;
	private float _grabDistance;

	/// <summary>Toolbar area above the viewport for preview controls.</summary>
	protected Layout Toolbar { get; }

	public PreviewSceneWidget( Widget parent, string sceneName ) : base( parent )
	{
		Layout = Layout.Column();

		Scene = EditorUtility.CreatePreviewScene();
		Scene.Name = sceneName;

		using ( Scene.Push() )
		{
			var camGo = new GameObject( true, "camera" );
			Camera = camGo.AddComponent<CameraComponent>();
			Camera.BackgroundColor = Theme.ControlBackground;
			Camera.FieldOfView = 60f;
			Camera.ZFar = 15000f;
			Camera.ZNear = 0.1f;

			var ambGo = new GameObject( true, "ambient" );
			ambGo.AddComponent<AmbientLight>().Color = Color.Cyan * 0.05f;

			var sunGo = new GameObject( true, "sun" );
			var sun = sunGo.AddComponent<DirectionalLight>();
			sun.Shadows = true;
			sun.WorldRotation = new Angles( 50, 45, 0 );
			sun.LightColor = Color.White * 0.6f;

			var envGo = new GameObject( true, "envmap" );
			var envmap = envGo.AddComponent<EnvmapProbe>();
			envmap.Mode = EnvmapProbe.EnvmapProbeMode.CustomTexture;
			envmap.Texture = Texture.Load( "textures/cubemaps/default2.vtex" );
			envmap.Bounds = BBox.FromPositionAndSize( Vector3.Zero, 100000 );

			var skyGo = new GameObject( true, "skybox" );
			skyGo.AddComponent<SkyBox2D>().SkyMaterial = Material.Load( "materials/skybox/skybox_workshop.vmat" );

			// Ground plane - treadmills against root motion so the model stays put
			Ground = new TreadmillGround();
		}

		var renderWidget = _renderView = new RenderView( this ) { Scene = Scene };
		renderWidget.OnPreFrame += PreFrame;
		_throbber = new PreviewThrobber( Scene.SceneWorld, renderWidget );
		Toolbar = Layout.AddRow();

		var frame = new Widget( this );
		frame.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( frame.LocalRect );
			return true;
		};
		frame.Layout = Layout.Column();
		frame.Layout.Margin = 2;
		frame.Layout.Add( renderWidget, 1 );

		Layout.Add( frame, 1 );
	}

	/// <summary>
	/// Put something under the viewport, full width - where the graph editor keeps its model picker.
	/// </summary>
	public void AddFooterWidget( Widget widget ) => Layout.Add( widget );

	protected void SetCompiling( bool compiling ) => _throbber.Enabled = compiling;

	/// <summary>
	/// The viewport owns camera input so clicks on surrounding UI never start an orbit.
	/// </summary>
	private sealed class RenderView : SceneRenderingWidget
	{
		private readonly PreviewSceneWidget _owner;

		public RenderView( PreviewSceneWidget owner ) : base( owner )
		{
			_owner = owner;
		}

		protected override void OnMousePress( MouseEvent e ) => _owner.ViewMousePress( e );
		protected override void OnMouseReleased( MouseEvent e ) => _owner.ViewMouseReleased( e );
		protected override void OnMouseWheel( WheelEvent e ) => _owner.ViewMouseWheel( e );
	}

	/// <summary>
	/// Swap the preview model and frame the camera around it. Returns false if
	/// the model is unchanged and the renderer is still live.
	/// </summary>
	protected bool SetModel( Model model )
	{
		if ( model is not null && !model.IsValid() )
			model = Model.Load( model.Name );

		if ( !model.IsValid() )
			model = null;

		var rendererValid = Renderer.IsValid() && Renderer.SceneModel.IsValid();
		if ( model == Model && rendererValid == (model is not null) )
			return false;

		Model = model;

		using ( Scene.Push() )
		{
			if ( ModelObject.IsValid() )
			{
				ModelObject.Destroy();
				ModelObject = null;
				Renderer = null;
			}

			if ( model is null )
				return true;

			ModelObject = new GameObject( true, "preview model" );
			Renderer = ModelObject.AddComponent<SkinnedModelRenderer>();
			Renderer.Model = model;
			Renderer.PlayAnimationsInEditorScene = true;

			// Nothing here plays the model's legacy animgraph - until an AG2 graph is bound
			// the model stays in its reference pose
			Renderer.UseAnimGraph = false;

			FrameCamera();
		}

		return true;
	}

	/// <summary>
	/// Convert a world position to pixel coordinates in the preview viewport.
	/// </summary>
	protected Vector2 ToViewportPixels( Vector3 worldPosition, out bool isBehind )
	{
		return Camera.PointToScreenNormal( worldPosition, out isBehind ) * _renderView.Size * _renderView.DpiScale;
	}

	protected void DrawOverlayText( string text, Vector2 position, Color color, TextFlag alignment = TextFlag.LeftTop )
	{
		using var overlay = Camera.BeginOverlay();
		overlay.TextStyle = new TextStyle( "Roboto Mono", 12, color.WithAlphaMultiplied( 0.85f ) )
		{
			Alignment = alignment | TextFlag.DontClip,
			Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 3 },
		};
		overlay.Text( text, new Rect( position, Vector2.One ) );
	}

	/// <summary>
	/// Frame the camera around the model's bounds.
	/// </summary>
	public void FrameCamera()
	{
		if ( Model is null && PreviewSkeleton is null )
			return;

		var bounds = Model?.Bounds ?? new BBox();
		if ( Model is null )
			foreach ( var bone in PreviewSkeleton.Bones )
				bounds = bounds.AddPoint( bone.Transform.Position );

		_center = bounds.Center;
		_distance = MathX.SphereCameraDistance( bounds.Size.Length * 0.5f, FieldOfView ) * 1.2f;
		_origin = Vector3.Zero;

		UpdateCamera();
	}

	protected void SetSkeleton( Skeleton skeleton )
	{
		if ( PreviewSkeleton == skeleton )
			return;

		PreviewSkeleton = skeleton;

		if ( Model is null )
			FrameCamera();
	}

	protected virtual Transform GetSkeletonBoneTransform( int index ) => PreviewSkeleton.Bones[index].Transform;

	private bool TryGetPreviewBoneTransform( string name, out Transform transform )
	{
		if ( Renderer.IsValid() )
			return Renderer.TryGetBoneTransform( name, out transform );

		var bone = PreviewSkeleton?.Bones.FirstOrDefault( bone => bone.Name == name );
		transform = bone is null ? Transform.Zero : GetSkeletonBoneTransform( bone.Index );
		return bone is not null;
	}

	private IEnumerable<(string Name, Transform Transform, Transform Parent)> GetPreviewBones()
	{
		if ( Renderer.IsValid() && Renderer.Model is { } model )
		{
			foreach ( var bone in model.Bones.AllBones )
			{
				if ( !Renderer.TryGetBoneTransform( bone, out var transform ) )
					continue;
				if ( bone.Parent is null || !Renderer.TryGetBoneTransform( bone.Parent, out var parent ) )
					parent = Renderer.WorldTransform;
				yield return (bone.Name, transform, parent);
			}
		}
		else if ( PreviewSkeleton is { } skeleton )
		{
			foreach ( var bone in skeleton.Bones )
				yield return (bone.Name, GetSkeletonBoneTransform( bone.Index ),
					bone.ParentIndex >= 0 ? GetSkeletonBoneTransform( bone.ParentIndex ) : Transform.Zero);
		}
	}

	private void PreFrame()
	{
		if ( !Scene.IsValid() || Camera is null )
			return;

		Camera.CustomSize = _renderView.Size * _renderView.DpiScale;

		var hasMouse = ViewIsHovered && !CameraDragging;
		_renderView.GizmoInstance.Input.IsHovered = hasMouse;
		_renderView.UpdateGizmoInputs( hasMouse );

		using ( Scene.Push() )
		{
			if ( _stepFrames > 0 )
			{
				var timeScale = Scene.TimeScale;
				Scene.TimeScale = 1f;
				Scene.GameTick( 1f / 60f );
				Scene.TimeScale = timeScale;
				_stepFrames--;
			}
			else
			{
				Scene.GameTick( RealTime.Delta );
			}

			if ( TreadmillEnabled )
				UpdateTreadmill();

			UpdateGrab();
			UpdateCamera();
			OnScenePreFrame();

			Options?.Toolbar.UpdateButtons();

			if ( ShowBones )
				DrawBones();
			else
				_hoveredBones.Clear();

			if ( ShowRootMotion )
				DrawRootMotion();
		}
	}

	/// <summary>
	/// What colour to draw a bone. Overridden where the skeleton means something, like a bone mask.
	/// </summary>
	protected virtual Color BoneColor( string bone, bool hovered )
	{
		return Color.White.WithAlpha( hovered ? 1f : 0.25f );
	}

	/// <summary>
	/// The skeleton drawn over the model, with the bone under the cursor labelled. Drawn through
	/// the mesh - half a skeleton hidden inside a model says nothing about the rig.
	/// </summary>
	private void DrawBones()
	{
		var ray = Gizmo.CurrentRay;
		var picked = _hoveredBones.FirstOrDefault();
		var hits = new List<(string Name, float Score)>();

		foreach ( var bone in GetPreviewBones() )
		{
			var tx = bone.Transform;
			var parentTx = bone.Parent;
			using var scope = Gizmo.Scope( $"BoneHandle{bone.Name}", tx );

			var radius = Math.Min( 1f, tx.Position.Distance( Gizmo.CameraTransform.Position ) / 64f );
			var hovered = bone.Name == picked;

			// Cursor distance in units of the handle's drawn radius - a screen-space hit
			// test where overlapping bones all register instead of the front one winning
			var toBone = tx.Position - ray.Position;
			var score = toBone.Cross( ray.Forward ).Length / radius;

			if ( ViewIsHovered && score < 1.5f && toBone.Dot( ray.Forward ) > 0f )
				hits.Add( (bone.Name, score) );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = BoneColor( bone.Name, hovered );
			Gizmo.Draw.LineThickness = hovered ? 2f : 1f;
			Gizmo.Draw.Line( 0f, tx.PointToLocal( parentTx.Position ) );
			Gizmo.Draw.LineCircle( 0f, radius );

			if ( !hovered )
				continue;

			// Best match's label first, any other bones under the cursor stacked dimly
			// below - drawn on the camera overlay, above everything in the scene
			var pixel = ToViewportPixels( tx.Position + Gizmo.CameraTransform.Right * radius * 2f, out var isBehind );
			if ( isBehind )
				continue;

			for ( var i = 0; i < _hoveredBones.Count; i++ )
			{
				DrawOverlayText( _hoveredBones[i], pixel + new Vector2( 0f, i * 16f ),
					Color.White.WithAlpha( i == 0 ? 1f : 0.7f ), TextFlag.LeftCenter );
			}
		}

		// Hover trails the draw by a frame, imperceptibly. Frozen while dragging the
		// camera so a right click still opens the menu for what was hovered
		if ( CameraDragging )
			return;

		_hoveredBones.Clear();

		if ( hits.Count == 0 )
			return;

		// The animating model jiggles the scores every frame - keep the current pick
		// on top unless another bone is clearly closer, and stack the rest in
		// skeleton order, so the labels never flicker or reshuffle
		var best = hits.MinBy( h => h.Score );
		var current = hits.FirstOrDefault( h => h.Name == picked );
		var top = current.Name is not null && current.Score < best.Score + 0.5f ? current.Name : best.Name;

		_hoveredBones.Add( top );
		_hoveredBones.AddRange( hits.Where( h => h.Name != top ).Select( h => h.Name ) );
	}

	/// <summary>
	/// The velocity the model is travelling at, as a flat arrow on the ground with the speed
	/// written next to it.
	/// </summary>
	private void DrawRootMotion()
	{
		if ( !Renderer.IsValid() )
			return;

		// Root motion scales with the ticked delta - divide it back out so the display is timescale-independent
		var dt = RealTime.Delta * Scene.TimeScale;
		if ( dt > 0f )
			SetRootVelocity( Renderer.WorldRotation, dt );

		var speed = _rootVelocity.Length;
		var origin = Renderer.WorldPosition + Vector3.Up * 0.5f;

		Scene.DebugOverlay.Line( Circle( origin, 6f ), Theme.Green, overlay: true );

		if ( speed < 5f )
			return;

		var forward = _rootVelocity.Normal;
		var left = Vector3.Cross( Vector3.Up, forward ).Normal;
		var length = 12f + (speed * 0.2f).Clamp( 6f, 60f );
		var start = origin + forward * 9f;

		// Flat arrow silhouette on the ground plane
		Vector3 P( float along, float side ) => start + forward * along + left * side;

		Scene.DebugOverlay.Line( new[]
		{
			P( 0f, 1.8f ),
			P( length - 8f, 1.8f ),
			P( length - 8f, 4.5f ),
			P( length, 0f ),
			P( length - 8f, -4.5f ),
			P( length - 8f, -1.8f ),
			P( 0f, -1.8f ),
			P( 0f, 1.8f ),
		}, Theme.Green, overlay: true );

		var pixel = ToViewportPixels( P( length, 0f ) + Vector3.Up * 2f, out var isBehind );
		if ( isBehind )
			return;

		Scene.DebugOverlay.ScreenText( pixel - new Vector2( 0f, 16f ), $"{speed:0} u/s", size: 13, color: Theme.Green );
	}

	/// <summary>
	/// The velocity the root motion arrow is drawn from. Playback that isn't the scene's own -
	/// a recording being reviewed - sets this itself.
	/// </summary>
	protected void SetRootVelocity( Rotation rotation, float deltaTime )
	{
		_rootVelocity = rotation * Renderer.RootMotion.Position / deltaTime;
	}

	/// <summary>Clear the velocity the root motion arrow is drawn from.</summary>
	protected void ClearRootVelocity() => _rootVelocity = default;

	private static IEnumerable<Vector3> Circle( Vector3 center, float radius )
	{
		for ( var i = 0; i <= 32; i++ )
		{
			var rad = i / 32f * MathF.Tau;
			yield return center + new Vector3( MathF.Cos( rad ) * radius, MathF.Sin( rad ) * radius, 0f );
		}
	}

	/// <summary>
	/// Per-frame hook, called with the preview scene pushed.
	/// </summary>
	protected virtual void OnScenePreFrame() { }

	/// <summary>
	/// Move the ground under the model. Integrates the root motion the model reports each
	/// frame, which previews that scrub through a clip replace with an absolute placement.
	/// </summary>
	protected virtual void UpdateTreadmill() => Ground.Update( Renderer );

	protected virtual void UpdateCamera()
	{
		Camera.FieldOfView = FieldOfView;

		var cursorPos = Application.UnscaledCursorPosition;
		var delta = cursorPos - _lastCursorPos;

		if ( _orbit )
		{
			_angles.x = (_angles.x + delta.x * 0.2f).NormalizeDegrees();
			_angles.y = (_angles.y + delta.y * 0.2f).Clamp( -90, 90 );
		}
		else if ( _zoom )
		{
			Zoom( delta.y );
		}
		else if ( _pan )
		{
			_origin += Camera.WorldRotation.Right * delta.x * 0.2f;
			_origin += Camera.WorldRotation.Down * delta.y * 0.2f;
		}

		if ( CameraDragging )
		{
			_dragDistance += delta.Length;
			Application.UnscaledCursorPosition = _lastCursorPos;
			_renderView.Cursor = CursorShape.Blank;
		}
		else
		{
			_lastCursorPos = cursorPos;
			_renderView.Cursor = _mouseJoint.IsValid() ? CursorShape.ClosedHand
				: IsHoveringDynamicBody() ? CursorShape.OpenHand : CursorShape.None;
		}

		Camera.WorldRotation = new Angles( _angles.y, -_angles.x, 0 );

		var center = _center;
		if ( FocusBone is not null && TryGetPreviewBoneTransform( FocusBone, out var boneTx ) )
			center = boneTx.Position;

		Camera.WorldPosition = center + _origin + Camera.WorldRotation.Backward * _distance;
	}

	private void ViewMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			// Grabbing a dynamic body takes priority over orbiting
			if ( !TryStartGrab() )
				_orbit = true;
		}
		else if ( e.RightMouseButton ) _zoom = true;
		else if ( e.MiddleMouseButton ) _pan = true;
		else return;

		_dragDistance = 0f;
		_lastCursorPos = Application.UnscaledCursorPosition;
		e.Accepted = true;
	}

	private void ViewMouseReleased( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			EndGrab();
			_orbit = false;
		}
		else if ( e.RightMouseButton )
		{
			_zoom = false;

			// A right click without dragging is a context click, not a zoom
			if ( _dragDistance < 5f )
				OnViewRightClick();
		}
		else if ( e.MiddleMouseButton ) _pan = false;
	}

	/// <summary>
	/// Called when the viewport is right clicked without dragging. Offers the camera whatever
	/// bone is under the cursor.
	/// </summary>
	protected virtual void OnViewRightClick()
	{
		var menu = new Menu( this );

		foreach ( var bone in _hoveredBones.Where( b => b != FocusBone ) )
			menu.AddOption( $"Focus Camera on {bone}", "center_focus_strong", () => SetFocusBone( bone ) );

		if ( FocusBone is not null )
			menu.AddOption( "Clear Camera Focus", "center_focus_weak", () => SetFocusBone( null ) );

		menu.AddOption( "Frame Camera", "fit_screen", FrameCamera );

		menu.OpenAtCursor();
	}

	/// <summary>
	/// Point the camera at a bone, or at the whole model with null. Overridden where the choice
	/// is remembered somewhere.
	/// </summary>
	public virtual void SetFocusBone( string bone ) => FocusBone = bone;

	private void ViewMouseWheel( WheelEvent e )
	{
		Zoom( e.Delta * -0.1f );
		e.Accept();
	}

	private void Zoom( float delta ) => _distance = (_distance + delta).Clamp( 10, 2000 );

	private Ray CursorRay => _renderView.CursorRay;

	private bool IsHoveringDynamicBody()
	{
		if ( !_renderView.IsUnderMouse )
			return false;

		var tr = Scene.Trace.Ray( CursorRay, 10000 ).Run();
		return tr.Hit && tr.Body.IsValid() && tr.Body.BodyType == PhysicsBodyType.Dynamic;
	}

	private bool TryStartGrab()
	{
		var tr = Scene.Trace.Ray( CursorRay, 10000 ).Run();
		if ( !tr.Hit || !tr.Body.IsValid() || tr.Body.BodyType != PhysicsBodyType.Dynamic )
			return false;

		_grabDistance = tr.HitPosition.Dot( Camera.WorldRotation.Forward );

		_mouseBody = new PhysicsBody( Scene.PhysicsWorld ) { BodyType = PhysicsBodyType.Keyframed };
		_mouseBody.Position = tr.HitPosition;

		// Motor joint pulling the grab point to the mouse body - same as the hammer/modeldoc mouse joint
		_mouseJoint = Sandbox.Physics.PhysicsJoint.CreateControl(
			new Sandbox.Physics.PhysicsPoint( _mouseBody ),
			Sandbox.Physics.PhysicsPoint.World( tr.Body, tr.HitPosition ) );

		var maxForce = 100.0f * tr.Body.Mass * Scene.PhysicsWorld.Gravity.Length;
		_mouseJoint.LinearSpring = new Sandbox.Physics.PhysicsSpring( 15, 1, maxForce );
		_mouseJoint.AngularSpring = new Sandbox.Physics.PhysicsSpring( 0, 0, 0 );

		return true;
	}

	private void UpdateGrab()
	{
		if ( _mouseBody is null )
			return;

		// Grabbed body got destroyed under us - ragdoll toggled off, model swapped
		if ( !_mouseJoint.IsValid() )
		{
			EndGrab();
			return;
		}

		// Drag along the camera-facing plane at the grab distance
		var plane = new Plane( Camera.WorldRotation.Forward, _grabDistance );
		if ( plane.TryTrace( CursorRay, out var hitPoint, true ) )
			_mouseBody.Position = hitPoint;
	}

	private void EndGrab()
	{
		if ( _mouseJoint.IsValid() )
			_mouseJoint.Remove();

		if ( _mouseBody.IsValid() )
			_mouseBody.Remove();

		_mouseJoint = null;
		_mouseBody = null;
	}

	private int _attireHash;

	/// <summary>
	/// Apply the graph's saved preview settings - body groups, skin and bone merged models.
	/// </summary>
	protected void ApplyPreviewSettings( Data graph )
	{
		if ( graph is null || !Renderer.IsValid() )
			return;

		Renderer.BodyGroups = graph.PreviewBodyGroups;
		Renderer.MaterialGroup = graph.PreviewMaterialGroup;

		var hash = new HashCode();
		hash.Add( Renderer );

		foreach ( var a in graph.PreviewAttachments )
		{
			hash.Add( a.Model );
			hash.Add( a.BodyGroups );
			hash.Add( a.MaterialGroup );
		}

		var attireHash = hash.ToHashCode();
		if ( attireHash == _attireHash )
			return;

		_attireHash = attireHash;

		using ( Scene.Push() )
		{
			foreach ( var child in ModelObject.Children.Where( c => c.Name == "bonemerge" ).ToArray() )
				child.Destroy();

			foreach ( var a in graph.PreviewAttachments )
			{
				if ( a.Model is null )
					continue;

				var go = new GameObject( true, "bonemerge" ) { Parent = ModelObject };
				var renderer = go.AddComponent<SkinnedModelRenderer>();
				renderer.Model = a.Model;
				renderer.BodyGroups = a.BodyGroups;
				renderer.MaterialGroup = a.MaterialGroup;
				renderer.BoneMergeTarget = Renderer;
				renderer.UseAnimGraph = false;
			}
		}
	}

	protected void DestroyScene()
	{
		Scene?.Destroy();
		Scene = null;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		DestroyScene();
	}
}
