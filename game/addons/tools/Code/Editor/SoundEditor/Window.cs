
namespace Editor.SoundEditor;

[EditorForAssetType( "vsnd" )]
public class Window : DockWindow, IAssetEditor
{
	public bool CanOpenMultipleAssets => true;

	private string DefaultDockState;
	private Preview Preview;
	private Timeline Timeline;
	private Properties Properties;
	private Asset Asset;
	private SoundFile SoundFile;
	private short[] Samples;
	private float Duration;
	private string Sound;

	public Window()
	{
		DeleteOnClose = true;

		Title = "Sound Editor";
		Size = new Vector2( 1000, 800 );

		CreateToolBar();
		CreateUI();
		Show();
	}

	public void AssetOpen( Asset asset )
	{
		if ( Asset != null )
			return;

		if ( asset == null )
			return;

		Asset = asset;
		SoundFile = SoundFile.Load( asset.Path );
		Title = $"Sound Editor - {asset.Name}";
		Timeline.SetAsset( Asset );
		Properties.SetAsset( Asset );

		OnAssetChanged();
	}

	private async void OnAssetChanged()
	{
		if ( !IsValid )
			return;

		if ( !SoundFile.IsValid() )
			return;

		SoundFile.OnSoundReloaded = OnAssetChanged;

		if ( !await SoundFile.LoadAsync() )
			return;

		Samples = await SoundFile.GetSamplesAsync();
		Duration = SoundFile.Duration;
		Sound = SoundFile.ResourcePath;
		Timeline.SetSamples( Samples, Duration, Sound );
	}

	[EditorEvent.Frame]
	protected void OnFrame()
	{
		if ( Timeline.Frames == null )
			return;

		Preview.AddVisemes( Timeline.Frames, Timeline.Time, 0.08f );
	}

	public void CreateUI()
	{
		if ( Asset != null )
			Title = $"Sound Editor - {Asset.Name}";

		BuildMenuBar();

		DockManager.RegisterDockType( "Preview", "photo", null, false );
		Preview = new Preview( this );
		DockManager.AddDock( null, Preview, DockArea.Left, DockManager.DockProperty.HideOnClose );

		DockManager.RegisterDockType( "Properties", "edit", null, false );
		Properties = new Properties( this );
		Properties.SetAsset( Asset );
		DockManager.AddDock( null, Properties, DockArea.Left, DockManager.DockProperty.HideOnClose, 0.0f );

		DockManager.RegisterDockType( "Timeline", "timeline", null, false );
		Timeline = new Timeline( this );
		Timeline.SetSamples( Samples, Duration, Sound );
		Timeline.SetAsset( Asset );
		DockManager.AddDock( null, Timeline, DockArea.BottomOuter, DockManager.DockProperty.HideOnClose, 0.3f );

		DockManager.Update();

		DefaultDockState = DockManager.State;

		if ( StateCookie != "SoundEditor" )
		{
			StateCookie = "SoundEditor";
		}
		else
		{
			RestoreFromStateCookie();
		}
	}

	protected override void RestoreDefaultDockLayout()
	{
		DockManager.State = DefaultDockState;

		SaveToStateCookie();
	}

	[EditorEvent.Hotload]
	public void OnHotload()
	{
		SaveToStateCookie();

		DockManager.Clear();
		MenuBar.Clear();

		CreateUI();
	}

	private void Save()
	{
		if ( Asset == null )
			return;

		if ( Timeline.Frames != null && Timeline.Frames.Count > 0 )
		{
			Asset.MetaData.Set( "visemes", Timeline.Frames );
		}
	}

	// Analyze the loaded audio offline and replace the timeline's visemes.
	private void GenerateLipSync()
	{
		if ( SoundFile == null || Samples == null )
			return;

		Timeline.SetVisemes( LipSyncGenerator.Generate( Samples, Duration ) );
	}

	private void CreateToolBar()
	{
		var toolBar = new ToolBar( this, "SoundEditorToolbar" );
		AddToolBar( toolBar, ToolbarPosition.Top );

		toolBar.AddOption( "Save", "common/save.png", Save ).StatusTip = "Save";
		toolBar.AddOption( "Generate Lip Sync", "record_voice_over", GenerateLipSync ).StatusTip = "Generate visemes from the audio";
		toolBar.AddOption( "Full Recompile", "refresh", () => Asset.Compile( true ) ).StatusTip = "Full Recompile";
	}

	public void BuildMenuBar()
	{
		var file = MenuBar.AddMenu( "File" );
		file.AddOption( "Save", "common/save.png", Save, "Ctrl+S" ).StatusTip = "Save";
		file.AddOption( "Full Recompile", "refresh", () => Asset.Compile( true ) ).StatusTip = "Full Recompile";
		file.AddSeparator();
		file.AddOption( "Open Asset Location", "folder", () => EditorUtility.OpenFileFolder( Asset.AbsolutePath ) ).StatusTip = "Open Asset Location";
		file.AddSeparator();
		file.AddOption( "Quit", null, Close, "Ctrl+Q" ).StatusTip = "Quit";

		var view = MenuBar.AddMenu( "View" );
		view.AboutToShow += () => OnViewMenu( view );
	}

	private void OnViewMenu( Menu view )
	{
		view.Clear();
		view.AddOption( "Restore To Default", "settings_backup_restore", RestoreDefaultDockLayout );
		view.AddSeparator();

		foreach ( var dock in DockManager.DockTypes )
		{
			var o = view.AddOption( dock.Title, dock.Icon );
			o.Checkable = true;
			o.Checked = DockManager.IsDockOpen( dock.Title );
			o.Toggled += ( b ) => DockManager.SetDockState( dock.Title, b );
		}
	}

	protected override void OnClosed()
	{
		base.OnClosed();

		SoundFile = null;
		Save();
	}
	void IAssetEditor.SelectMember( string memberName )
	{
		throw new System.NotImplementedException();
	}
}
