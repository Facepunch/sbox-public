using Editor.Wizards;

namespace Editor.Inspectors;

sealed class AssetPublishWidget : Widget, AssetSystem.IEventListener
{
	private Asset Asset;
	SerializedObject SerializedObject;

	Label label;

	public AssetPublishWidget( Widget parent, Asset asset ) : base( parent )
	{
		Asset = asset;
		SerializedObject = asset.Publishing.GetSerialized();
		SerializedObject.OnPropertyChanged += ( p ) => asset.Publishing.Save();
		Layout = Layout.Row();
		Layout.Margin = 8;

		SerializedObject.OnPropertyChanged += x => StartBuild();

		StartBuild();
	}

	void AssetSystem.IEventListener.OnAssetChanged( Asset asset )
	{
		if ( asset != Asset ) return;

		StartBuild();
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( Theme.Primary.WithAlpha( 0.2f ) );
		Paint.DrawRect( LocalRect );
	}

	ResourcePublishContext BuildPubishContext()
	{
		var context = new ResourcePublishContext();

		// pre fill?

		var resource = Asset.LoadResource();
		if ( resource == null )
		{
			return context;
		}

		resource.ConfigurePublishing( context );
		return context;
	}

	void StartBuild()
	{
		var context = BuildPubishContext();

		Layout.Clear( true );

		Layout.Spacing = 8;
		var enableDisable = Layout.Add( ControlWidget.Create( SerializedObject.GetProperty( "Enabled" ) ) );

		label = Layout.Add( new Label( "" ) );
		label.Color = Color.White;

		if ( !context.PublishingEnabled )
		{
			label.Text = $"Can't publish - {context.ReasonForDisabling}";
			enableDisable.Hide();
			return;
		}

		if ( !Asset.Publishing.Enabled )
		{
			label.Text = "Enable Publishing";
		}
		else
		{
			label.Text = "Published";
		}

		Layout.AddStretchCell();

		Update();

		_ = Build();
	}

	async Task Build()
	{
		// fake addon for upload

		if ( AssetSystem.IsCloudInstalled( Asset.Package ) && string.IsNullOrWhiteSpace( Asset.GetSourceFile( true ) ) )
		{
			Enabled = false;
			Visible = false;
			//	Title = "Cannot re-publish a downloaded asset";

			return;
		}
		else
		{
			//	Title = "Publish";
			Visible = true;
		}

		var addon = Asset.Publishing.CreateTemporaryProject();

		if ( !Asset.Publishing.Enabled )
			return;

		var upload = Layout.Add( new IconButton( "upload" ) );
		upload.ToolTip = "Publish";
		upload.Background = Theme.Green;
		upload.OnClick = () => OpenProjectWindow( addon );

		var settings = Layout.Add( new IconButton( "settings" ) );
		settings.ToolTip = "Settings";
		settings.OnClick = () => ProjectSettingsWindow.OpenForProject( addon );

		var package = await Package.FetchAsync( addon.Config.FullIdent, false );
		if ( package is not null )
		{
			var view = Layout.Add( new IconButton( "launch" ) );
			view.ToolTip = "Open Web";
			view.OnClick = () => EditorUtility.OpenFolder( addon.ViewUrl );
		}

		Update();
	}

	void OpenProjectWindow( Project project )
	{
		var w = PublishWizard.Open( project, BuildPubishContext() );
	}
}

