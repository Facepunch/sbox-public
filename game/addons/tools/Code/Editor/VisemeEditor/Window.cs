
using Editor.ModelEditor;

namespace Editor.VisemeEditor;

public class Window : DockWindow
{
	public bool CanOpenMultipleAssets => true;

	private Preview Preview;
	private Visemes Visemes;
	private Morphs Morphs;
	private Asset Asset;
	private Model Model;
	private string VisemeSelected;
	private Dictionary<string, Dictionary<string, float>> VisemeData = new();

	public Window()
	{
		DeleteOnClose = true;

		Title = "Viseme Editor";
		Size = new Vector2( 1000, 600 );

		CreateUI();
		Show();
		StateCookie = "VisemeEditor";
	}

	public void AssetOpen( Asset asset )
	{
		if ( Asset != null )
			return;

		var model = Model.Load( asset.Path );
		if ( model == null || model.IsError )
			return;

		if ( model.MorphCount == 0 )
			return;

		Model = model;
		Asset = asset;
		VisemeData = Asset.MetaData.Get( "visemes", VisemeData );
		VisemeSelected = "B";
		Visemes.VisemeSelected = VisemeSelected;

		Morphs.Model = Model;
		Preview.Model = Model;
		Visemes.Model = Model;

		var morphs = VisemeData.GetValueOrDefault( VisemeSelected );
		Preview.SetMorphs( morphs );
		Morphs.SetMorphs( morphs );

		foreach ( var viseme in VisemeData )
		{
			Visemes.SetMorphs( viseme.Key, viseme.Value );
		}

		Visemes.SetMorphs( VisemeSelected, morphs );
	}

	[Event( "modeldoc.menu.tools" )]
	public static void OnModelDocToolsMenu( Menu menu )
	{
		menu.AddOption( "Viseme Editor", "abc", () =>
		{
			var asset = ModelDoc.ModelAsset;
			if ( asset == null )
				return;

			var editor = new Window();
			editor.AssetOpen( asset );
		} );
	}

	public void CreateUI()
	{
		Morphs = new Morphs( this );
		Morphs.Model = Model;
		Morphs.OnValueEdited += OnMorphEdited;
		Morphs.OnReset += OnResetMorphs;
		DockManager.AddDock( "Morphs", "tune", Morphs, DockArea.Right );

		Visemes = new Visemes( this );
		Visemes.Model = Model;
		Visemes.OnSelectionChanged += OnVisemeSelected;
		DockManager.AddDock( "Visemes", "abc", Visemes, DockArea.Right );

		Preview = new Preview( this );
		Preview.Model = Model;
		DockManager.AddDock( "Preview", "photo", Preview, DockArea.Left );

		if ( VisemeSelected != null )
		{
			Visemes.VisemeSelected = VisemeSelected;

			foreach ( var viseme in VisemeData )
			{
				Visemes.SetMorphs( viseme.Key, viseme.Value );
			}

			var morphs = VisemeData.GetValueOrDefault( VisemeSelected );
			Morphs.SetMorphs( morphs );
			Preview.SetMorphs( morphs );
			Visemes.SetMorphs( VisemeSelected, morphs );
		}

	}

	protected override void BuildDefaultLayout()
	{
		var preview = DockManager.OpenDock( "Preview", DockArea.Left );
		DockManager.OpenDock( "Morphs", DockArea.Right );
		DockManager.OpenDock( "Visemes", DockArea.Right );
		DockManager.SetSplitterProportions( preview, 0.55f, 0.25f, 0.20f );
	}

	private void OnVisemeSelected( string name )
	{
		VisemeSelected = name;
		var morphs = VisemeData.GetValueOrDefault( VisemeSelected );
		Preview.SetMorphs( morphs );
		Morphs.SetMorphs( morphs );
		Visemes.SetMorphs( VisemeSelected, morphs );
	}

	private void OnResetMorphs()
	{
		if ( VisemeData.TryGetValue( VisemeSelected, out var morphs ) )
			morphs.Clear();

		OnVisemeSelected( VisemeSelected );
	}

	private void OnMorphEdited( string name, float value )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return;

		if ( string.IsNullOrWhiteSpace( VisemeSelected ) )
			return;

		if ( !VisemeData.TryGetValue( VisemeSelected, out var morphs ) )
		{
			morphs = new Dictionary<string, float>();
			VisemeData.Add( VisemeSelected, morphs );
		}

		if ( value == 0 )
		{
			morphs.Remove( name );
		}
		else
		{
			morphs[name] = value;
		}

		Preview.SetMorph( name, value );
		Visemes.SetMorph( VisemeSelected, name, value );

		var keysToRemove = VisemeData
			.Where( kvp => !kvp.Value.Any() || kvp.Value.All( pair => pair.Value == 0 ) )
			.Select( kvp => kvp.Key )
			.ToList();

		foreach ( var key in keysToRemove )
		{
			VisemeData.Remove( key );
		}
	}

	[EditorEvent.Hotload]
	public void OnHotload()
	{
		SaveLayout();

		DockManager.Clear();
		MenuBar.Clear();

		CreateUI();
		RestoreLayout();
	}

	protected override void OnClosed()
	{
		base.OnClosed();

		Asset.MetaData.Set( "visemes", VisemeData );
	}
}
