using Editor.AssetPickers;

namespace Editor;

[CustomEditor( typeof( GameObject ) )]
public class GameObjectControlWidget : ControlWidget
{
	public override bool SupportsMultiEdit => true;
	bool IsInList => Parent?.Parent is ListControlWidget;
	IconButton PickerButton;

	public GameObjectControlWidget( SerializedProperty property ) : base( property )
	{
		if ( !property.IsEditable )
			ReadOnly = true;

		IsDraggable = true;

		SetSizeMode( SizeMode.Default, SizeMode.Default );

		Layout = Layout.Column();
		Layout.Spacing = 2;

		var inner = Layout.Add( Layout.Row() );
		inner.Margin = 2;

		inner.AddStretchCell( 1 );
		PickerButton = inner.Add( new IconButton( "colorize", () =>
		{
			PropertyStartEdit();
			EyeDropperTool.SetTargetProperty( property );
			EyeDropperTool.OnBackToLastTool = () => PropertyFinishEdit();
		}, this ) );
		PickerButton.FixedWidth = Theme.RowHeight - 4;
		PickerButton.FixedHeight = Theme.RowHeight - 4;
		PickerButton.Visible = false;

		Cursor = CursorShape.Finger;
		AcceptDrops = true;
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var m = new ContextMenu( this );

		var go = SerializedProperty.GetValue<GameObject>();
		if ( go is PrefabScene prefab )
		{
			var asset = AssetSystem.FindByPath( prefab.Source.ResourcePath );

			m.AddOption( "Open in Editor", "edit", () => asset?.OpenInEditor() ).Enabled = asset != null && !asset.IsProcedural;
			m.AddOption( "Find in Asset Browser", "search", () => LocalAssetBrowser.OpenTo( asset, true ) ).Enabled = asset is not null;
			m.AddSeparator();
		}
		else if ( go is not null )
		{
			m.AddOption( "Find in Scene", "search", () => EditorUtility.FindInScene( go ) );
			m.AddSeparator();
		}

		//m.AddOption( "Copy", "file_copy" );
		//m.AddOption( "Paste", "content_paste" );
		//m.AddSeparator();

		if ( !ReadOnly )
		{
			m.AddOption( "Clear", "backspace", action: Clear );
		}

		if ( m.OptionCount > 0 )
		{
			m.OpenAtCursor( false );
		}

		e.Accepted = true;
	}

	protected override void PaintControl()
	{
		var rect = LocalRect.Shrink( 6, 0 );
		var go = SerializedProperty.GetValue<GameObject>();

		Paint.SetDefaultFont();

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			Paint.SetPen( Theme.MultipleValues );
			Paint.DrawIcon( rect, "panorama_wide_angle_select", 14, TextFlag.LeftCenter );
			rect.Left += 22;
			Paint.DrawText( rect, "Multiple Values", TextFlag.LeftCenter );
		}
		else if ( !go.IsValid() )
		{
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.3f ) );
			Paint.DrawIcon( rect, "radio_button_unchecked", 14, TextFlag.LeftCenter );
			rect.Left += 22;
			Paint.DrawText( rect, "None (GameObject)", TextFlag.LeftCenter );
		}
		else if ( go is PrefabScene )
		{
			Paint.SetPen( Theme.Blue );
			Paint.DrawIcon( rect, "panorama_wide_angle_select", 14, TextFlag.LeftCenter );
			rect.Left += 22;
			Paint.DrawText( rect, go.Name, TextFlag.LeftCenter );
		}
		else
		{
			Paint.SetPen( Theme.Green );
			Paint.DrawIcon( rect, "panorama_wide_angle_select", 14, TextFlag.LeftCenter );
			rect.Left += 22;
			Paint.DrawText( rect, BuildName( go ), TextFlag.LeftCenter );
		}

		if ( EyeDropperTool.TargetProperty == SerializedProperty )
		{
			Paint.SetPen( ControlHighlightSecondary.WithAlpha( 0.8f ), 2, PenStyle.Dot );
			Paint.SetBrush( ControlHighlightSecondary.WithAlpha( 0.2f ) );
			Paint.DrawRect( LocalRect.Shrink( 2 ), Theme.ControlRadius );
		}

		PickerButton.Visible = IsControlHovered;
	}

	private string BuildName( GameObject go )
	{
		string str = "";

		if ( go.Parent.IsValid() && go.Parent is not Scene )
		{
			str += $"{BuildName( go.Parent )} > ";
		}

		str += $"{go.Name}";

		return str;
	}

	void UpdateFromAsset( Asset asset )
	{
		if ( asset.TryLoadResource( out PrefabFile prefabFile ) )
		{
			PropertyStartEdit();
			SerializedProperty.SetValue( SceneUtility.GetPrefabScene( prefabFile ) );
			PropertyFinishEdit();
		}
	}

	void UpdateFromGameObject( GameObject go )
	{
		PropertyStartEdit();
		SerializedProperty.SetValue( go );
		PropertyFinishEdit();
	}

	void UpdateFromAssets( Asset[] assets )
	{
		if ( assets.Length == 0 ) return;

		if ( assets[0].TryLoadResource( out PrefabFile prefabFile ) )
		{
			PropertyStartEdit();
			SerializedProperty.SetValue( SceneUtility.GetPrefabScene( prefabFile ) );
			PropertyFinishEdit();
		}

		if ( IsInList )
		{
			var list = Parent.Parent as ListControlWidget;
			for ( int i = 1; i < assets.Length; i++ )
			{
				if ( assets[i].TryLoadResource( out PrefabFile newPrefabFile ) )
				{
					list.Collection.Add( SceneUtility.GetPrefabScene( newPrefabFile ) );
				}
			}
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			e.Accepted = true;
			var go = SerializedProperty.GetValue<GameObject>();

			if ( ReadOnly ) return;

			var resource = (go as PrefabScene)?.Source ?? null;
			var asset = resource != null ? AssetSystem.FindByPath( resource.ResourcePath ) : null;

			// Open Prefab Asset Picker
			var prefabAssetType = AssetType.Find( "prefab", false );
			var picker = AssetPicker.Create( null, prefabAssetType, new AssetPicker.PickerOptions()
			{
				EnableMultiselect = IsInList
			} );
			picker.Window.Title = $"Select GameObject/Prefab";
			picker.OnAssetHighlighted = ( o ) => UpdateFromAsset( o.FirstOrDefault() );
			picker.OnAssetPicked = ( o ) => UpdateFromAssets( o );
			picker.Show();

			var sceneBrowser = new GameObjectSceneBrowser( null );
			sceneBrowser.OnGameObjectSelect = ( o ) => UpdateFromGameObject( o );

			if ( picker is GenericPicker genericPicker )
			{
				genericPicker.DockManager.AddDock( null, sceneBrowser, DockArea.Inside, DockManager.DockProperty.HideCloseButton
					| DockManager.DockProperty.DisallowUserDocking | DockManager.DockProperty.DisableDraggableTab );
				sceneBrowser.ConfirmButton = genericPicker.ConfirmButton;
				sceneBrowser.OnConfirm = () => picker.Close();
				if ( go.IsValid() && go is not PrefabScene )
				{
					sceneBrowser.Focus();
				}
			}

			picker.SetSelection( asset );
		}
	}

	void Clear()
	{
		PropertyStartEdit();

		SerializedProperty.SetValue<GameObject>( null );
		SignalValuesChanged();

		PropertyFinishEdit();
	}

	protected override void OnDragStart()
	{
		var drag = new Drag( this );
		drag.Data.Object = SerializedProperty.GetValue<GameObject>();
		drag.Execute();
	}

	public override void OnDragHover( DragEvent ev )
	{
		ev.Action = DropAction.Ignore;

		if ( ev.Data.OfType<GameObject>().Any() )
		{
			ev.Action = DropAction.Link;
			return;
		}

		if ( ev.Data.HasFileOrFolder )
		{
			var asset = AssetSystem.FindByPath( ev.Data.Files.First() );
			if ( asset is null ) return;
			if ( asset.AssetType.FileExtension != "prefab" ) return;

			if ( asset.TryLoadResource( out PrefabFile prefabFile ) )
			{
				ev.Action = DropAction.Link;
				return;
			}
		}

	}

	public override void OnDragDrop( DragEvent ev )
	{
		if ( ev.Data.OfType<GameObject>().FirstOrDefault() is { } go )
		{
			PropertyStartEdit();

			SerializedProperty.SetValue( go );

			PropertyFinishEdit();
			return;
		}

		if ( ev.Data.HasFileOrFolder )
		{
			var asset = AssetSystem.FindByPath( ev.Data.Files.First() );
			if ( asset is null ) return;
			if ( asset.AssetType.FileExtension != "prefab" ) return;

			if ( asset.TryLoadResource( out PrefabFile prefabFile ) )
			{
				PropertyStartEdit();

				SerializedProperty.SetValue( SceneUtility.GetPrefabScene( prefabFile ) );

				PropertyFinishEdit();

				return;
			}
		}
	}
}
