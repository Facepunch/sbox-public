namespace Editor;

/// <summary>
/// What "Upgrade to Slang" made (shader spec 3.6): its message and notes, and for a surface the <c>.shader</c> and the
/// surface side by side, each drawing a material made from it with its defaults on a sphere, lit the same way, so the
/// two pictures can be compared before the <c>.shader</c> is moved away.
/// </summary>
internal sealed class ShaderUpgradeDialog : Dialog
{
	public ShaderUpgradeDialog( string text, bool succeeded, Material before, string beforeName, Material after, string afterName )
	{
		Window.Title = "Upgrade to Slang";
		Window.SetWindowIcon( succeeded ? "upgrade" : "warning" );
		var compare = before is not null && after is not null;
		Window.Size = compare ? new Vector2( 860, 620 ) : new Vector2( 520, 280 );

		Layout = Layout.Column();
		Layout.Margin = 16;
		Layout.Spacing = 8;

		var label = Layout.Add( new Label( text, this ) );
		label.WordWrap = true;

		if ( compare )
		{
			var row = Layout.AddRow( 1 );
			row.Spacing = 8;
			row.Add( Side( beforeName, before ), 1 );
			row.Add( Side( afterName, after ), 1 );
		}
		else
		{
			Layout.AddStretchCell();
		}

		var footer = Layout.AddRow();
		footer.AddStretchCell();
		var close = footer.Add( new Button.Primary( "Close", this ) );
		close.Clicked = Close;
	}

	Widget Side( string title, Material material )
	{
		var side = new Widget( this ) { Layout = Layout.Column() };
		side.Layout.Spacing = 4;
		side.Layout.Add( new Label( title, side ) );
		side.Layout.Add( new MaterialSphere( material, side ), 1 );
		return side;
	}

	/// <summary>A material on a sphere, turning slowly, under a sun, an ambient light and an environment probe.</summary>
	sealed class MaterialSphere : SceneRenderingWidget
	{
		readonly CameraComponent camera;
		readonly GameObject sphere;

		public MaterialSphere( Material material, Widget parent ) : base( parent )
		{
			MinimumSize = 300;
			Scene = Scene.CreateEditorScene();

			using ( Scene.Push() )
			{
				camera = new GameObject( true, "camera" ).GetOrAddComponent<CameraComponent>();
				camera.BackgroundColor = new Color( 0.1f, 0.1f, 0.12f );
				camera.FieldOfView = 40;

				var sun = new GameObject( true, "sun" ).GetOrAddComponent<DirectionalLight>();
				sun.WorldRotation = Rotation.From( 50, 30, 0 );
				sun.LightColor = Color.White * 2.5f;

				var ambient = new GameObject( true, "ambient" ).GetOrAddComponent<AmbientLight>();
				ambient.Color = Color.White * 0.25f;

				var probe = new GameObject( true, "envmap" ).GetOrAddComponent<EnvmapProbe>();
				probe.TintColor = Color.White * 0.1f;

				sphere = new GameObject( true, "sphere" );
				var renderer = sphere.GetOrAddComponent<ModelRenderer>();
				renderer.Model = Model.Load( "models/dev/sphere.vmdl" );
				renderer.MaterialOverride = material;

				var distance = MathX.SphereCameraDistance( renderer.Model.Bounds.Size.Length * 0.5f, camera.FieldOfView );
				camera.WorldPosition = renderer.Model.Bounds.Center + new Vector3( -1, 0, 0.3f ).Normal * distance * 0.75f;
				camera.WorldRotation = Rotation.LookAt( renderer.Model.Bounds.Center - camera.WorldPosition, Vector3.Up );
			}
		}

		protected override void PreFrame()
		{
			Scene.EditorTick( RealTime.Now, RealTime.Delta );
			if ( sphere.IsValid() ) sphere.WorldRotation = Rotation.FromYaw( RealTime.Now * 20.0f );
		}

		public override void OnDestroyed()
		{
			base.OnDestroyed();
			Scene?.Destroy();
			Scene = null;
		}
	}
}
