namespace Editor.SpriteEditor;

public class Preview : Widget
{
	public Window SpriteEditor { get; private set; }
	public SpriteRenderer Renderer { get; private set; }

	private SceneRenderingWidget RenderingWidget;
	private Scene Scene;
	private CameraComponent Camera;

	public Preview( Window window ) : base( null )
	{
		SpriteEditor = window;

		Name = "Preview";
		WindowTitle = Name;
		SetWindowIcon( "emoji_emotions" );

		MinimumSize = new Vector2( 256, 256 );
		Layout = Layout.Column();

		CreateScene();

		SetSizeMode( SizeMode.Default, SizeMode.CanShrink );

		SpriteEditor.OnAssetLoaded += UpdateRenderer;
		SpriteEditor.OnAnimationSelected += UpdateRenderer;
		SpriteEditor.OnPlayPause += UpdateRenderer;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		SpriteEditor.OnAssetLoaded -= UpdateRenderer;
		SpriteEditor.OnAnimationSelected -= UpdateRenderer;
		SpriteEditor.OnPlayPause -= UpdateRenderer;
		Scene?.Destroy();
	}

	private void CreateScene()
	{
		Scene = Scene.CreateEditorScene();
		using ( Scene.Push() )
		{
			var cameraObject = new GameObject( true, "camera" );
			Camera = cameraObject.Components.Create<CameraComponent>();
			Camera.Orthographic = true;
			Camera.OrthographicHeight = 100;
			Camera.BackgroundColor = Theme.SurfaceBackground;

			var rendererObject = new GameObject( true, "sprite" );
			rendererObject.Flags = rendererObject.Flags.WithFlag( GameObjectFlags.EditorOnly, true );
			Renderer = rendererObject.Components.Create<SpriteRenderer>();
			Renderer.WorldPosition = Camera.WorldTransform.Forward * 100;
			Renderer.Size = 100;
			Renderer.IsSorted = true;

			var backgroundObject = new GameObject( true, "background" );
			var background = backgroundObject.Components.Create<SpriteRenderer>();
			background.WorldPosition = Camera.WorldTransform.Forward * 200;
			background.Size = 100;
			background.IsSorted = true;
			background.Color = Theme.ControlBackground.Darken( 0.9f );
			background.Sprite = new Sprite()
			{
				Animations = new()
				{
					new Sprite.Animation()
					{
						Name = "background",
						Frames = new()
						{
							new Sprite.Frame()
							{
								Texture = Texture.White
							}
						}
					}
				}
			};
		}

		RenderingWidget = new SceneRenderingWidget( this );
		RenderingWidget.Layout = Layout.Row();
		RenderingWidget.Scene = Scene;
		RenderingWidget.OnPreFrame += ScenePreFrame;

		Layout.Add( RenderingWidget );

		UpdateRenderer();
	}

	private void ScenePreFrame()
	{
		var texture = Renderer.Texture;
		if ( texture.Width == 0 || texture.Height == 0 )
			return;
		var ratio = (float)texture.Width / texture.Height;
		var pivotOffset = new Vector2( 0.5f, 0.5f ) - (SpriteEditor?.SelectedAnimation?.Origin ?? new Vector2( 0.5f, 0.5f ));
		if ( ratio > 1 )
		{
			Renderer.WorldPosition = Renderer.WorldPosition
			.WithY( pivotOffset.x * 100 )
			.WithZ( pivotOffset.y * 100 / ratio );
		}
		else
		{
			Renderer.WorldPosition = Renderer.WorldPosition
			.WithY( pivotOffset.x * 100 * ratio )
			.WithZ( pivotOffset.y * 100 );
		}

		switch ( SpriteEditor?.Antialiasing )
		{
			case 0:
				Renderer.TextureFilter = Sandbox.Rendering.FilterMode.Point;
				break;
			case 1:
				Renderer.TextureFilter = Sandbox.Rendering.FilterMode.Bilinear;
				break;
			case 2:
				Renderer.TextureFilter = Sandbox.Rendering.FilterMode.Trilinear;
				break;
			case 3:
				Renderer.TextureFilter = Sandbox.Rendering.FilterMode.Anisotropic;
				break;
		}

		Scene.EditorTick( RealTime.Now, RealTime.Delta );

		var overlay = Scene.GetSystem<DebugOverlaySystem>();
		overlay.Text( Renderer.WorldPosition.WithX( 50 ), new TextRendering.Scope( "add", Color.White, 28, "Material Icons" )
		{
			FilterMode = Sandbox.Rendering.FilterMode.Point,
		} );
	}

	private void UpdateRenderer()
	{
		if ( SpriteEditor.Sprite == null )
		{
			Renderer.Sprite = null;
			return;
		}
		Renderer.Sprite = SpriteEditor.Sprite;
		if ( SpriteEditor.SelectedAnimation != null )
		{
			Renderer.PlayAnimation( SpriteEditor.SelectedAnimation.Name );
		}
		Renderer.PlaybackSpeed = SpriteEditor.IsPlaying ? 1 : 0;
	}
}
