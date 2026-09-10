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

	ResourcePublishContext BuildPublishContext() => Asset.Publishing.BuildPublishContext();

	void StartBuild()
	{
		var context = BuildPublishContext();

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
		upload.OnClick = () => OpenProjectWindow();

		var settings = Layout.Add( new IconButton( "settings" ) );
		settings.ToolTip = "Settings";
		settings.OnClick = () => ProjectSettingsWindow.OpenForProject( addon );

		var package = await Package.FetchAsync( addon.Config.FullIdent, false );
		if ( !Layout.IsValid() ) return;

		if ( package is not null )
		{
			var view = Layout.Add( new IconButton( "launch" ) );
			view.ToolTip = "Open Web";
			view.OnClick = () => EditorUtility.OpenFolder( addon.ViewUrl );
		}

		Update();
	}

	void OpenProjectWindow()
	{
		var context = BuildPublishContext();

		//
		// The wizard's project is rooted where its code comes from, and that's decided here. We
		// don't have a better way right now - in the future we'll allow them to define which code
		// to include and whatever. Without code we root it nowhere, so we don't drag any in from
		// whatever project happens to be open.
		//
		var project = Asset.Publishing.CreateTemporaryProject( context.IncludeCode ? Project.Current?.RootDirectory : null );

		PublishWizard.Open( project, context );
	}
}

