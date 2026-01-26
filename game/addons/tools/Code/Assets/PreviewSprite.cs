namespace Editor.Assets;

[AssetPreview( "sprite" )]
class PreviewSprite : AssetPreview
{
	public override bool IsAnimatedPreview => false;
	public Sandbox.Rendering.FilterMode FilterMode { get; set; } = Sandbox.Rendering.FilterMode.Bilinear;

	private Sprite spriteResource;
	private SpriteRenderer spriteRenderer;
	private int currentAnimationIndex = 0;

	Sprite.Animation CurrentAnimation => spriteResource?.GetAnimation( currentAnimationIndex ) ?? null;


	public PreviewSprite( Asset asset ) : base( asset )
	{
		if ( asset.TryLoadResource<Sprite>( out var resource ) )
		{
			spriteResource = resource;
		}
	}

	public override Task InitializeAsset()
	{
		using ( Scene.Push() )
		{
			PrimaryObject = new GameObject();
			PrimaryObject.WorldTransform = Transform.Zero;

			spriteRenderer = PrimaryObject.AddComponent<SpriteRenderer>();
			spriteRenderer.Sprite = spriteResource;
			spriteRenderer.Size = new Vector2( 16, 16 );
			spriteRenderer.IsSorted = true;

			var originObj = new GameObject();
			originObj.WorldPosition = Vector3.Backward * 10;

			Camera.Orthographic = true;
			Camera.OrthographicHeight = 16;
		}

		return Task.CompletedTask;
	}

	public override void UpdateScene( float cycle, float timeStep )
	{
		var texture = spriteRenderer.Texture;
		if ( texture.Width == 0 || texture.Height == 0 )
			return;
		var ratio = (float)texture.Width / texture.Height;
		var pivotOffset = new Vector2( 0.5f, 0.5f ) - (CurrentAnimation?.Origin ?? new Vector2( 0.5f, 0.5f ));
		if ( ratio > 1 )
		{
			PrimaryObject.WorldPosition = new Vector3( 0, pivotOffset.x, pivotOffset.y / ratio ) * 16;
		}
		else
		{
			PrimaryObject.WorldPosition = new Vector3( 0, pivotOffset.x * ratio, pivotOffset.y ) * 16;
		}

		spriteRenderer.TextureFilter = FilterMode;

		Camera.Orthographic = true;
		Camera.OrthographicHeight = 16;
		Camera.WorldPosition = Vector3.Forward * -200;
		Camera.WorldRotation = Rotation.LookAt( Vector3.Forward );

		TickScene( timeStep );

		if ( !IsRenderingThumbnail )
		{
			var originPosition = new Vector3( -1, pivotOffset.x, pivotOffset.y ) * 16;
			Scene.DebugOverlay.Text( originPosition, new TextRendering.Scope( "add", Color.White, 7, "Material Icons" )
			{
				FilterMode = Sandbox.Rendering.FilterMode.Point,
			} );
		}
	}

	public void SetAnimation( Sprite.Animation animation )
	{
		if ( animation is null || spriteResource is null )
			return;
		var index = spriteResource.Animations.IndexOf( animation );
		if ( index >= 0 )
		{
			currentAnimationIndex = index;
			spriteRenderer.PlayAnimation( currentAnimationIndex );
		}
	}

	public override Widget CreateToolbar()
	{
		var info = new IconButton( "settings" );
		info.Layout = Layout.Row();
		info.MinimumSize = 16;
		info.MouseLeftPress = () => OpenSettings( info );

		return info;
	}

	public void OpenSettings( Widget parent )
	{
		var popup = new PopupWidget( parent );
		popup.IsPopup = true;

		popup.Layout = Layout.Column();
		popup.Layout.Margin = 16;

		var ps = new ControlSheet();

		ps.AddProperty( this, x => x.FilterMode );

		popup.Layout.Add( ps );
		popup.MaximumWidth = 300;
		popup.Show();
		popup.Position = parent.ScreenRect.TopRight - popup.Size;
		popup.ConstrainToScreen();

	}
}
