using Sandbox.UI.Construct;

namespace Editor;

/// <summary>
/// Panel control for strings marked with an <see cref="AssetPathAttribute"/>, like
/// [ImageAssetPath]. The panel version of <see cref="ResourceStringControlWidget"/>: click to pick
/// an asset, right-click for the usual actions, drop an asset file on it.
/// </summary>
[CustomEditor( typeof( string ), WithAllAttributes = new[] { typeof( AssetPathAttribute ) } )]
[StyleSheet.Inline( "assetpathcontrol", Styles )]
public class AssetPathControl : BaseControl
{
	const string Styles = """
		AssetPathControl
		{
			flex-direction: row;
			align-items: center;
			flex-grow: 1;
			min-width: 0px;
			gap: 8px;
			height: 32px;
			flex-shrink: 0;
			padding: 0px 8px;
			border-radius: 8px;
			background-color: #000a;
			cursor: pointer;
			pointer-events: all;

			&:hover { background-color: #000c; }
			&.drop-target { outline: 1px solid #3273EB; }
		}

		AssetPathControl > .icon { flex-shrink: 0; font-size: 16px; color: #8a8f98; }
		AssetPathControl > .thumb { flex-shrink: 0; width: 24px; height: 24px; border-radius: 3px; background-size: cover; background-position: center; background-color: #ffffff0d; }
		AssetPathControl > .name { flex-shrink: 0; font-weight: 600; white-space: nowrap; }
		AssetPathControl > .path { flex-shrink: 1; min-width: 0px; opacity: 0.5; font-size: 11px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
		AssetPathControl > .placeholder { flex-shrink: 1; min-width: 0px; opacity: 0.3; font-style: italic; white-space: nowrap; }
		AssetPathControl.missing > .icon { color: #ff7b7b; }
		""";

	AssetType _assetType;
	string _shown;
	Sandbox.UI.Menu _menu;

	public override void Rebuild()
	{
		if ( Property is null ) return;

		Property.TryGetAttribute<AssetPathAttribute>( out var attribute );
		var extension = attribute?.AssetTypeExtension.Replace( "resource:", "" ).Trim().ToLowerInvariant();

		_assetType = AssetType.All.FirstOrDefault( x => x.FileExtension == extension )
			?? AssetType.All.FirstOrDefault( x => x.FileExtensions.Contains( extension ) );

		Tooltip = $"Select {Property.DisplayName}";
		_shown = null;
		Refresh();
	}

	public override void Tick()
	{
		base.Tick();
		Refresh();
	}

	string Resource => Property?.GetValue<string>( null );

	Asset CurrentAsset => Resource is { } path ? AssetSystem.FindByPath( path ) : null;

	void Refresh()
	{
		var resource = Resource;
		if ( _shown == resource && ChildrenCount > 0 ) return;
		_shown = resource;

		DeleteChildren( true );

		var asset = CurrentAsset;
		var typeName = _assetType?.FriendlyName ?? "Asset";

		var missing = asset is null && !string.IsNullOrWhiteSpace( resource );
		SetClass( "missing", missing );

		if ( asset is not null && !asset.IsDeleted )
		{
			// The asset browser's thumbnail, through the engine's thumb: loader
			Add.Panel( "thumb" ).Style.BackgroundImage = Texture.Load( $"thumb:{asset.RelativePath}" );
			Add.Label( asset.Name, "name" );
			Add.Label( asset.RelativePath, "path" );
		}
		else if ( missing )
		{
			Add.Icon( "error", "icon" );
			Add.Label( $"Missing {typeName}", "name" );
			Add.Label( resource, "path" );
		}
		else
		{
			Add.Icon( IconFor( _assetType ), "icon" );
			Add.Label( typeName, "placeholder" );
		}
	}

	static string IconFor( AssetType type )
	{
		if ( type == AssetType.ImageFile || type == AssetType.Texture ) return "image";
		if ( type == AssetType.Material ) return "texture";
		if ( type == AssetType.Model ) return "view_in_ar";
		if ( type == AssetType.SoundFile || type == AssetType.SoundEvent ) return "volume_up";
		return "description";
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );
		if ( !Property.IsEditable ) return;

		var picker = AssetPicker.Create( null, _assetType );
		picker.Title = $"Select {Property.DisplayName}";
		picker.SetSelection( Resource );
		picker.OnAssetHighlighted = assets => SetAsset( assets.FirstOrDefault() );
		picker.OnAssetPicked = assets => SetAsset( assets.FirstOrDefault() );
		picker.Show();
		picker.SetSelection( CurrentAsset );
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		e.StopPropagation();
		_menu?.Delete( true );

		var resource = Resource;
		var asset = CurrentAsset;
		var menu = new Sandbox.UI.Menu();
		_menu = menu;

		menu.AddOption( "Open in Editor", "edit", () => asset?.OpenInEditor() ).Enabled = asset is not null && !asset.IsProcedural;
		menu.AddOption( "Find in Asset Browser", "search", () => LocalAssetBrowser.OpenTo( asset, true ) ).Enabled = asset is not null;
		menu.AddSeparator();
		menu.AddOption( "Copy", "file_copy", () => EditorUtility.Clipboard.Copy( asset?.RelativePath ?? resource ) ).Enabled = !string.IsNullOrWhiteSpace( resource );
		menu.AddOption( "Paste", "content_paste", () => SetAsset( AssetSystem.FindByPath( EditorUtility.Clipboard.Paste() ) ) );
		menu.AddSeparator();
		menu.AddOption( "Clear", "backspace", () => SetPath( null ) ).Enabled = resource is not null;

		menu.Closed += _ => { if ( _menu == menu ) _menu = null; menu.Delete( true ); };
		menu.Open( this, Popup.PositionMode.UnderMouse );
	}

	// A file dragged in from the OS or the asset browser
	protected override void OnDrop( PanelEvent e )
	{
		if ( e is not DropEvent drop ) return;

		var asset = drop.Files.Select( AssetSystem.FindByPath ).FirstOrDefault( x => x is not null && (_assetType is null || x.AssetType == _assetType) );
		SetClass( "drop-target", asset is not null && !drop.IsDrop );

		if ( asset is null ) return;

		drop.Action = Sandbox.UI.DropAction.Copy;
		drop.StopPropagation();

		if ( drop.IsDrop ) SetAsset( asset );
	}

	protected override void OnDragLeave( PanelEvent e )
	{
		SetClass( "drop-target", false );
	}

	void SetAsset( Asset asset )
	{
		if ( asset is null ) return;
		SetPath( asset.RelativePath );
	}

	void SetPath( string path )
	{
		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue( path );
		Property.Parent?.NoteFinishEdit( Property );
	}

	public override void OnDeleted()
	{
		_menu?.Delete( true );
		base.OnDeleted();
	}
}
