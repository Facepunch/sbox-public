using Sandbox.Clutter;
using Sandbox.UI;

namespace Editor;

/// <summary>
/// Custom control widget for editing ClutterEntry list in a grid layout.
/// Similar to TerrainMaterialList.
/// </summary>
[CustomEditor( typeof( List<ClutterEntry> ), NamedEditor = "ClutterEntriesGrid" )]
public class ClutterEntriesGridWidget : ControlWidget
{
	private SerializedProperty _listProperty;
	private readonly ClutterEntriesListView _listView;

	public override bool SupportsMultiEdit => false;

	public ClutterEntriesGridWidget( SerializedProperty property ) : base( property )
	{
		_listProperty = property;

		Layout = Layout.Column();
		Layout.Spacing = 0;
		VerticalSizeMode = SizeMode.CanGrow;

		_listView = new ClutterEntriesListView( this, _listProperty );
		_listView.VerticalSizeMode = SizeMode.CanGrow;
		Layout.Add( _listView, 1 );

		var buttonRow = Layout.AddRow();
		buttonRow.Spacing = 4;
		buttonRow.Margin = new Margin( 8, 0, 8, 8 );
		buttonRow.AddStretchCell();

		var addButton = new Button( "Add Entry", "add" );
		addButton.Clicked = () => AddNewEntry();
		buttonRow.Add( addButton );
	}

	private void AddNewEntry()
	{
		var picker = AssetPicker.Create( this, null );

		picker.OnAssetPicked = ( assets ) =>
		{
			var entries = _listProperty.GetValue<List<ClutterEntry>>() ?? new List<ClutterEntry>();
			foreach ( var asset in assets )
			{
				ClutterEntry newEntry = new();

				if ( asset.AssetType.FileExtension == "vmdl" && asset.TryLoadResource<Model>( out var model ) )
				{
					newEntry.Model = model;
				}
				else if ( asset.AssetType.FileExtension == "prefab" && asset.TryLoadResource<PrefabFile>( out var prefabFile ) )
				{
					var prefab = SceneUtility.GetPrefabScene( prefabFile );
					newEntry.Prefab = prefab;
				}

				if ( newEntry.HasAsset )
				{
					entries.Add( newEntry );
				}
			}

			_listProperty.SetValue( entries );
			_listProperty.Parent?.NoteChanged( _listProperty );
			_listView.BuildItems();
		};

		picker.Show();
	}

	/// <summary>
	/// ListView for displaying clutter entries in a grid
	/// </summary>
	private class ClutterEntriesListView : ListView
	{
		private SerializedProperty _listProperty;
		private int _dragOverIndex = -1;

		public ClutterEntriesListView( Widget parent, SerializedProperty listProperty ) : base( parent )
		{
			_listProperty = listProperty;

			ItemSpacing = 4;
			Margin = 8;
			MinimumHeight = 200;
			VerticalSizeMode = SizeMode.CanGrow;
			ItemSize = new Vector2( 86, 86 + 32 );
			ItemAlign = Align.FlexStart;
			ItemContextMenu = ShowItemContext;
			AcceptDrops = true;

			BuildItems();
		}

		protected override bool OnDragItem( VirtualWidget item )
		{
			if ( item.Object is not ClutterEntry entry ) return false;

			var entries = _listProperty.GetValue<List<ClutterEntry>>();
			if ( entries is null ) return false;

			var index = entries.IndexOf( entry );
			if ( index < 0 ) return false;

			var drag = new Drag( this );
			drag.Data.Object = index;
			drag.Execute();
			return true;
		}

		protected override DropAction OnItemDrag( ItemDragEvent e )
		{
			_dragOverIndex = -1;

			// Handle reordering visual feedback
			if ( e.Data.Object is int && e.Item.Object is ClutterEntry )
			{
				// Find target index for highlight
				var idx = 0;
				foreach ( var item in Items )
				{
					if ( item == e.Item.Object )
					{
						_dragOverIndex = idx;
						break;
					}
					idx++;
				}
				Update();
				return DropAction.Move;
			}

			// Handle external asset drops
			if ( e.Data.Assets != null )
			{
				foreach ( var dragAsset in e.Data.Assets )
				{
					var path = dragAsset.AssetPath;
					if ( string.IsNullOrEmpty( path ) ) continue;

					if ( path.EndsWith( ".vmdl" ) || path.EndsWith( ".vmdl_c" ) || path.EndsWith( ".prefab" ) )
						return DropAction.Copy;
				}
			}

			return base.OnItemDrag( e );
		}

		public override void OnDragLeave()
		{
			base.OnDragLeave();
			_dragOverIndex = -1;
			Update();
		}

		public void BuildItems()
		{
			var entries = _listProperty.GetValue<IList<ClutterEntry>>();
			if ( entries != null && entries.Count > 0 )
			{
				SetItems( entries.Cast<object>() );
			}
			else
			{
				SetItems( [] );
			}
		}

		private void ShowItemContext( object obj )
		{
			if ( obj is not ClutterEntry entry )
				return;

			var entries = _listProperty.GetValue<IList<ClutterEntry>>();
			if ( entries is null ) return;

			var index = entries.IndexOf( entry );
			if ( index < 0 ) return;

			var menu = new Menu( this );

			if ( entry.Model is not null || entry.Prefab is not null )
			{
				menu.AddOption( "Find in Asset Browser", "search", () =>
				{
					Asset asset = null;

					if ( entry.Prefab is PrefabScene prefabScene && prefabScene.Source is not null )
					{
						asset = AssetSystem.FindByPath( prefabScene.Source.ResourcePath );
					}
					else if ( entry.Model is not null )
					{
						asset = AssetSystem.FindByPath( entry.Model.ResourcePath );
					}

					if ( asset is not null )
					{
						LocalAssetBrowser.OpenTo( asset, true );
					}
				} );

				menu.AddSeparator();
			}

			menu.AddOption( "Set Weight...", "balance", () =>
			{
				var popup = new PopupWidget( this );
				popup.Layout = Layout.Row();
				popup.Layout.Margin = 8;
				popup.Layout.Spacing = 8;
				popup.MinimumWidth = 200;

				var slider = new FloatSlider( popup );
				slider.Minimum = 0.01f;
				slider.Maximum = 1f;
				slider.Value = entry.Weight;
				slider.MinimumWidth = 150;
				slider.OnValueEdited += () =>
				{
					entry.Weight = slider.Value;
					_listProperty.SetValue( entries );
					_listProperty.Parent?.NoteChanged( _listProperty );
					Update();
				};
				popup.Layout.Add( slider );

				popup.OpenAtCursor();
			} );

			menu.AddSeparator();

			menu.AddOption( "Remove Entry", "close", () =>
			{
				entries.RemoveAt( index );
				_listProperty.SetValue( entries );
				_listProperty.Parent?.NoteChanged( _listProperty );
				BuildItems();
			} );

			menu.OpenAtCursor();
		}

		public override void OnDragHover( DragEvent e )
		{
			base.OnDragHover( e );

			if ( e.Data.Object is int )
			{
				e.Action = DropAction.Move;
				return;
			}

			if ( e.Data.Assets is null ) return;

			foreach ( var dragAsset in e.Data.Assets )
			{
				var path = dragAsset.AssetPath;
				if ( string.IsNullOrEmpty( path ) ) continue;

				var isModel = path.EndsWith( ".vmdl" ) || path.EndsWith( ".vmdl_c" );
				var isPrefab = path.EndsWith( ".prefab" );

				if ( isModel || isPrefab )
				{
					e.Action = DropAction.Copy;
					return;
				}
			}
		}

		public override void OnDragDrop( DragEvent e )
		{
			base.OnDragDrop( e );

			_dragOverIndex = -1;

			// Handle internal reordering
			if ( e.Data.Object is int oldIndex )
			{
				var hoveredItem = GetItemAt( e.LocalPosition );
				if ( hoveredItem?.Object is ClutterEntry )
				{
					// Find the target index
					var newIndex = -1;
					var idx = 0;
					foreach ( var item in Items )
					{
						if ( item == hoveredItem.Object )
						{
							newIndex = idx;
							break;
						}
						idx++;
					}

					var entries = _listProperty.GetValue<List<ClutterEntry>>();
					if ( entries is not null && oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex && oldIndex < entries.Count && newIndex < entries.Count )
					{
						// Move entry from oldIndex to newIndex
						var entry = entries[oldIndex];
						entries.RemoveAt( oldIndex );
						entries.Insert( newIndex, entry );

						_listProperty.SetValue( entries );
						_listProperty.Parent?.NoteChanged( _listProperty );
						BuildItems();
					}
				}
				Update();
				return;
			}

			if ( e.Data.Assets != null )
				AddAssetsFromDrop( e.Data.Assets );
		}

		private async void AddAssetsFromDrop( IEnumerable<DragAssetData> draggedAssets )
		{
			var entries = _listProperty.GetValue<List<ClutterEntry>>() ?? new List<ClutterEntry>();

			foreach ( var dragAsset in draggedAssets )
			{
				var path = dragAsset.AssetPath;
				if ( string.IsNullOrEmpty( path ) ) continue;

				var isModel = path.EndsWith( ".vmdl" ) || path.EndsWith( ".vmdl_c" );
				var isPrefab = path.EndsWith( ".prefab" );

				if ( !isModel && !isPrefab ) continue;

				var asset = await dragAsset.GetAssetAsync();
				if ( asset is null ) continue;

				var newEntry = new ClutterEntry();

				if ( isModel && asset.TryLoadResource<Model>( out var model ) )
				{
					newEntry.Model = model;
				}
				else if ( isPrefab && asset.TryLoadResource<PrefabFile>( out var prefabFile ) )
				{
					var prefab = SceneUtility.GetPrefabScene( prefabFile );
					newEntry.Prefab = prefab;
				}

				if ( newEntry.HasAsset )
				{
					entries.Add( newEntry );
				}
			}

			_listProperty.SetValue( entries );
			_listProperty.Parent?.NoteChanged( _listProperty );
			BuildItems();
		}

		protected override void PaintItem( VirtualWidget item )
		{
			if ( item.Object is not ClutterEntry entry )
				return;

			var rect = item.Rect.Shrink( 0, 0, 0, 32 );

			// Get asset for thumbnail
			Asset asset = null;
			if ( entry.Prefab is PrefabScene prefabScene && prefabScene.Source is not null )
			{
				asset = AssetSystem.FindByPath( prefabScene.Source.ResourcePath );
			}
			else if ( entry.Model is not null )
			{
				asset = AssetSystem.FindByPath( entry.Model.ResourcePath );
			}

			// Hover highlight
			if ( Paint.HasMouseOver )
			{
				Paint.SetBrush( Theme.Blue.WithAlpha( 0.2f ) );
				Paint.ClearPen();
				Paint.DrawRect( item.Rect, 4 );
			}

			// Drag-over highlight
			var entries = _listProperty.GetValue<IList<ClutterEntry>>();
			if ( entries is not null && _dragOverIndex >= 0 && _dragOverIndex < entries.Count )
			{
				if ( entries[_dragOverIndex] == entry )
				{
					Paint.ClearBrush();
					Paint.SetPen( Theme.Primary, 2f );
					Paint.DrawRect( item.Rect.Shrink( 1 ), 4 );
				}
			}

			Paint.SetBrush( Theme.ControlBackground );
			Paint.ClearPen();
			Paint.DrawRect( rect.Shrink( 2 ), 4 );

			// Draw thumbnail
			if ( asset is not null )
			{
				var pixmap = asset.GetAssetThumb( true );
				if ( pixmap is not null )
				{
					Paint.Draw( rect.Shrink( 2 ), pixmap );
				}
				else
				{
					Paint.SetPen( Theme.Text.WithAlpha( 0.3f ) );
					Paint.DrawIcon( rect.Shrink( 24 ), "category", 32 );
				}
			}
			else
			{
				// Empty entry
				Paint.SetPen( Theme.Text.WithAlpha( 0.3f ) );
				Paint.DrawIcon( rect.Shrink( 24 ), "add_photo_alternate", 32 );
			}

			// Border
			Paint.ClearBrush();
			Paint.SetPen( Theme.ControlBackground.Lighten( 0.1f ) );
			Paint.DrawRect( rect.Shrink( 2 ), 4 );

			// Weight label at bottom
			var weightRect = new Rect( item.Rect.Left, rect.Bottom + 2, item.Rect.Width, 28 );
			Paint.SetDefaultFont( 9 );
			Paint.SetPen( Theme.Text.WithAlpha( 0.8f ) );

			var weightText = $"Weight: {entry.Weight:F2}";
			if ( asset is not null )
			{
				weightText = $"{asset.Name}\n{weightText}";
			}

			Paint.DrawText( weightRect, weightText, TextFlag.CenterTop );
		}

		protected override void OnPaint()
		{
			// Background
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( LocalRect, 4 );

			var entries = _listProperty.GetValue<IList<ClutterEntry>>();
			if ( entries is null or { Count: 0 } )
			{
				Paint.SetDefaultFont( 11 );
				Paint.SetPen( Theme.Text.WithAlpha( 0.4f ) );
				Paint.DrawText( LocalRect, "Drag & drop Prefabs or Models here\nor click Add Entry", TextFlag.Center );
			}

			base.OnPaint();
		}
	}
}
