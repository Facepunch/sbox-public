namespace Editor;

internal class SceneRendering : SceneRenderingWidget
{
	private readonly AmbientLight ambientLight;
	private readonly CameraComponent camera;
	private readonly SkinnedModelRenderer model;

	private float hoverTime;

	public SceneRendering( string modelName ) : base( null )
	{
		MinimumSize = 300;

		Scene = Scene.CreateEditorScene();

		using ( Scene.Push() )
		{
			{
				camera = new GameObject( true, "camera" ).GetOrAddComponent<CameraComponent>( false );
				camera.BackgroundColor = Color.Black;
				camera.Enabled = true;
			}
			{
				var light = new GameObject( true, "light" ).GetOrAddComponent<PointLight>( false );
				light.WorldPosition = 100;
				light.Radius = 500;
				light.LightColor = Color.Orange * 3;
				light.Enabled = true;
			}
			{
				var light = new GameObject( true, "light" ).GetOrAddComponent<PointLight>( false );
				light.WorldPosition = -100;
				light.Radius = 500;
				light.LightColor = Color.Cyan * 3;
				light.Enabled = true;
			}
			{
				ambientLight = new GameObject( true, "light" ).GetOrAddComponent<AmbientLight>( false );
				ambientLight.Color = Color.Black;
				ambientLight.Enabled = true;
			}
			{
				model = new GameObject( true, "model" ).GetOrAddComponent<SkinnedModelRenderer>( false );
				model.Model = Model.Load( modelName );
				model.WorldPosition = Vector3.Down * model.Model.Bounds.Mins.z;
				model.Enabled = true;
			}

			UpdateCamera();
		}

		MouseTracking = true;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		Scene?.Destroy();
		Scene = null;
	}

	Vector2 lastPos;
	float spinVelocity;

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );

		Update();

		var delta = e.LocalPosition - lastPos;
		lastPos = e.LocalPosition;

		if ( (e.ButtonState & MouseButtons.Left) != 0 )
		{
			spinVelocity += delta.x * 0.1f;
		}
	}

	protected override void PreFrame()
	{
		Scene.EditorTick( RealTime.Now, RealTime.Delta );

		spinVelocity = spinVelocity.Approach( 0.0f, RealTime.Delta * 10.0f );
		hoverTime += spinVelocity * RealTime.Delta;

		if ( ambientLight.IsValid() )
		{
			ambientLight.Color = IsUnderMouse ? Color.Gray : Color.Black;
		}

		UpdateCamera();

		Gizmo.Draw.Grid( 0, Gizmo.GridAxis.XY );
	}

	private void UpdateCamera()
	{
		if ( !camera.IsValid() )
			return;

		if ( !model.IsValid() )
			return;

		var dir = new Vector3( MathF.Sin( hoverTime ), MathF.Cos( hoverTime ), 0.5f ).Normal;
		var distance = MathX.SphereCameraDistance( model.Bounds.Size.Length * 0.5f, camera.FieldOfView );
		var aspect = Size.x / Size.y;
		if ( aspect > 1 ) distance *= aspect;

		camera.WorldPosition = model.Bounds.Center + dir * distance * 0.9f;
		camera.WorldRotation = Rotation.LookAt( dir * -1.0f, Vector3.Up );
		camera.ZFar = 10000;
		camera.FieldOfView = 50;
	}

	/// <summary>
	/// SceneCamera.RenderScene allows you to make any widget render a SceneWorld. You can render the world like a regular widget in OnPaint
	/// or you could call it in an Event.Frame update to redraw it every frame. Or whatever you want.
	/// </summary>
	[WidgetGallery]
	[Title( "RenderToWidget" )]
	[Icon( "portrait" )]
	internal static Widget WidgetGallery()
	{
		var canvas = new Widget( null );
		canvas.Layout = Layout.Grid();
		canvas.Layout.Spacing = 4;

		if ( canvas.Layout is GridLayout gridLayout )
		{
			gridLayout.AddCell( 0, 0, new SceneRendering( "models/citizen_human/human.vmdl" ), 1 );
			gridLayout.AddCell( 1, 0, new SceneRendering( "models/citizen_human/human.vmdl" ), 1 );
			gridLayout.AddCell( 2, 0, new SceneRendering( "models/citizen_human/human.vmdl" ), 1 );

			gridLayout.AddCell( 0, 1, new SceneRendering( "models/citizen_human/human.vmdl" ), 1 );
			gridLayout.AddCell( 1, 1, new SceneRendering( "models/citizen_human/human.vmdl" ), 1 );
			gridLayout.AddCell( 2, 1, new SceneRendering( "models/citizen_human/human.vmdl" ), 1 );
		}

		return canvas;
	}
}
