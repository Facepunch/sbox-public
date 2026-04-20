namespace Editor.Assets;

[AssetPreview( "vmat" )]
class PreviewMaterial : AssetPreview
{
	//public override bool IsAnimatedPreview => false;

	public override float PreviewWidgetCycleSpeed => 0.2f;

	SkyBox2D skyboxObject;

	static readonly Model Plane = Model.Load( "models/dev/plane_blend.vmdl" );

	public PreviewMaterial( Asset asset ) : base( asset )
	{

	}

	/// <summary>
	/// Create the model or whatever needs to be viewed
	/// </summary>
	public override async Task InitializeAsset()
	{
		var material = await Material.LoadAsync( Asset.Path );
		if ( material is null ) return;

		using ( Scene.Push() )
		{
			PrimaryObject = new GameObject();
			PrimaryObject.WorldTransform = Transform.Zero;

			if ( material.Flags.IsSky )
			{
				skyboxObject = PrimaryObject.AddComponent<SkyBox2D>();
				skyboxObject.SkyMaterial = material;
			}
			else
			{
				var go = Scene.Directory.FindByName( "envmap" )?.FirstOrDefault() ?? new GameObject( true, "envmap" );
				var c = go.GetOrAddComponent<EnvmapProbe>();
				c.WorldPosition = new Vector3( 0, 0, 0 );
				c.TintColor = Color.White * 0.1f;
				var sprite = PrimaryObject.AddComponent<ModelRenderer>();
				sprite.Model = Plane;
				sprite.MaterialOverride = material;
			}

		}
	}

	public override void UpdateScene( float cycle, float timeStep )
	{
		// Big fov for skybox preview so you can see a few sides
		if ( skyboxObject.IsValid() )
		{
			Camera.WorldPosition = Vector3.Zero;
			Camera.WorldRotation = new Angles( 0, 180 * cycle, 0 );
			Camera.FieldOfView = 120;
			return;
		}

		float spin = 180 * cycle;
		float pitch = 90;

		Camera.WorldPosition = Vector3.Up * 300;
		Camera.WorldRotation = new Angles( pitch, 180 + spin, 0 );

		PrimaryObject.WorldRotation = new Angles( 0, spin, 0 );

		SceneCenter = 0;
		SceneSize = 55;
		FrameScene();
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

		ps.AddProperty( Camera, x => x.BackgroundColor );
		ps.AddProperty( PrimaryObject.GetComponent<ModelRenderer>(), x => x.Tint );
		//ps.AddProperty( Camera, x => x.EnablePostProcessing );

		popup.Layout.Add( ps );
		popup.MaximumWidth = 300;
		popup.Show();
		popup.Position = parent.ScreenRect.TopRight - popup.Size;
		popup.ConstrainToScreen();
	}
}
