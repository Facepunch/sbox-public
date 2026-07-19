namespace Editor.ProjectSettingPages;

/// <summary>
/// The ordered list of sprite sort layers, back to front.
/// </summary>
internal sealed class SortLayerListWidget : Widget
{
	public SortingSettings Settings { get; }

	/// <summary>
	/// Raised whenever a layer is added, removed, renamed or moved.
	/// </summary>
	public Action ValueChanged { get; set; }

	private Layout _rows;

	public SortLayerListWidget( SortingSettings settings ) : base( null )
	{
		Settings = settings;

		Layout = Layout.Column();
		Layout.Spacing = 2;

		_rows = Layout.AddColumn();
		_rows.Spacing = 1;

		var addButton = new Button( "Add Layer", "add" );
		addButton.Clicked = AddLayer;
		Layout.AddSpacingCell( 4 );
		Layout.Add( addButton );

		// Which end of the list is the front is the single most common point of confusion with
		// layer lists anywhere, so say it outright rather than leaving it to be discovered.
		Layout.AddSpacingCell( 4 );
		Layout.Add( new Label( "Drawn first, at the back  →  drawn last, in front" )
		{
			Color = Theme.Text.WithAlpha( 0.5f )
		} );

		Rebuild();
	}

	public void Rebuild()
	{
		_rows.Clear( true );

		foreach ( var layer in Settings.Layers )
		{
			_rows.Add( new SortLayerRow( this, layer ) );
		}
	}

	public void NotifyChanged()
	{
		ValueChanged?.Invoke();
	}

	private void AddLayer()
	{
		// Through the settings rather than the list, so the id lookup is rebuilt with it.
		Settings.AddLayer( UniqueName( "New Layer" ) );

		NotifyChanged();
		Rebuild();
	}

	/// <summary>
	/// Removes a layer, warning first if any sprite in the open scene still points at it.
	/// </summary>
	public void RemoveLayer( SortingSettings.SortLayer layer )
	{
		var users = CountUsers( layer );

		if ( users > 0 )
		{
			var confirm = new PopupWindow(
				$"Delete \"{layer.Name}\"?",
				$"{users} sprite{(users == 1 ? "" : "s")} in the open scene {(users == 1 ? "is" : "are")} using this layer. " +
				$"They will fall back to \"{Settings.DefaultLayer.Name}\".",
				"Cancel",
				new Dictionary<string, Action> { { "Delete", () => DoRemoveLayer( layer ) } } );

			confirm.Show();
			return;
		}

		DoRemoveLayer( layer );
	}

	private void DoRemoveLayer( SortingSettings.SortLayer layer )
	{
		// Sprites referencing this id resolve back to the default layer on their own, so there is
		// nothing to fix up - GetLayerIndex already treats an unknown id as the default.
		Settings.RemoveLayer( layer );

		NotifyChanged();
		Rebuild();
	}

	private static int CountUsers( SortingSettings.SortLayer layer )
	{
		var scene = SceneEditorSession.Active?.Scene;
		if ( scene is null ) return 0;

		var count = 0;

		foreach ( var sprite in scene.GetAllComponents<SpriteRenderer>() )
		{
			if ( sprite.SortLayer.Id == layer.Id ) count++;
		}

		return count;
	}

	private string UniqueName( string baseName )
	{
		if ( Settings.GetLayer( baseName ) is null ) return baseName;

		for ( var i = 2; ; i++ )
		{
			var candidate = $"{baseName} {i}";
			if ( Settings.GetLayer( candidate ) is null ) return candidate;
		}
	}
}
