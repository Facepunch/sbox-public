using SelectionSetEntry = Sandbox.SelectionSetsSystem.SelectionSetEntry;

namespace Editor;

[Dock( "Editor", "Selection Sets", "select_all" )]
public sealed class SelectionSetsDock : Widget
{
	private enum SetEnabledState
	{
		Enabled,
		Disabled,
		Mixed
	}

	private readonly List<SelectionSetEntry> _sets = new();
	private SceneEditorSession _session;
	private Layout _header;
	private Layout _subHeader;

	private SelectionSetListView _setList;
	private AddButton _createButton;

	private SelectionSetEntry _selectedSet;
	private readonly Dictionary<SelectionSetEntry, SetRowStats> _rowStats = new();
	private bool _dataDirty = true;
	private bool _viewDirty = true;

	private sealed class SetRowStats
	{
		public List<GameObject> Objects { get; set; } = [];
		public int EnabledCount { get; set; }
		public SetEnabledState State { get; set; }
	}

	public SelectionSetsDock( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		BuildUi();
		RefreshDataForActiveSession( forceReload: true );
		ApplyPendingDataChanges();
		RefreshView();
	}

	private void BuildUi()
	{
		Layout.Clear( true );
		_header = Layout.AddColumn();
		_subHeader = _header.AddRow();
		_subHeader.Spacing = 2;
		_subHeader.Margin = new Sandbox.UI.Margin( 0, 2 );
		_subHeader.Alignment = TextFlag.LeftCenter;

		_createButton = _subHeader.Add( new AddButton( "add" ) );
		_createButton.ToolTip = "New Selection Set";
		_createButton.MouseLeftPress = OpenCreateSetDialog;
		_subHeader.Add( new Widget( this ) { FixedWidth = 28 } );
		_subHeader.Add( new Label( "Set", this ), 1 );
		_subHeader.Add( new Label( "Enabled", this ) { FixedWidth = 58, Alignment = TextFlag.RightCenter } );
		_subHeader.Add( new Label( "Objects", this ) { FixedWidth = 58, Alignment = TextFlag.RightCenter } );
		_subHeader.Add( new Widget( this ) { FixedWidth = 14 } );

		_setList = Layout.Add( new SelectionSetListView( this, this ), 1 );
		_setList.ItemSize = new Vector2( 0, Theme.RowHeight );
		_setList.ItemSpacing = 0;
		_setList.Margin = 0;
		_setList.MultiSelect = false;
		_setList.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( _setList.LocalRect, Theme.ControlRadius );
			return false;
		};
		_setList.ItemSelected = item =>
		{
			_selectedSet = item as SelectionSetEntry;
		};
		_setList.ItemContextMenu = OpenSetContextMenu;
		_setList.BodyContextMenu = OpenBodyContextMenu;
		_setList.ItemPaint = PaintSetItem;
		_setList.ItemDrag = TryStartSetDrag;
	}

	[EditorEvent.Frame]
	private void Frame()
	{
		if ( !Visible )
			return;

		RefreshDataForActiveSession();

		if ( _session is not null && !_dataDirty && SetContentHash( SceneStateHash, 0.2f ) )
		{
			_dataDirty = true;
		}

		ApplyPendingDataChanges();

		if ( _viewDirty )
		{
			RefreshView();
		}
	}

	private void RefreshDataForActiveSession( bool forceReload = false )
	{
		var active = SceneEditorSession.Active;
		if ( !forceReload && ReferenceEquals( _session, active ) )
			return;

		_session = active;
		_sets.Clear();
		_rowStats.Clear();
		_selectedSet = null;

		if ( _session is null )
		{
			UpdateButtonStates();
			_viewDirty = true;
			return;
		}

		var loaded = LoadSceneSets();

		foreach ( var loadedSet in loaded )
		{
			if ( loadedSet is null || string.IsNullOrWhiteSpace( loadedSet.Name ) )
				continue;

			var set = CloneSet( loadedSet );
			set.ObjectIds = set.ObjectIds.Distinct().ToList();
			_sets.Add( set );
		}

		UpdateButtonStates();
		_dataDirty = true;
	}

	private void ApplyPendingDataChanges()
	{
		if ( !_dataDirty )
			return;

		_dataDirty = false;
		var changed = PruneInvalidObjects();
		changed |= SyncSetEnabledStates();
		RebuildRowStats();

		if ( changed )
		{
			SaveSets();
		}

		_viewDirty = true;
	}

	private void RefreshAfterChange()
	{
		_dataDirty = true;
		ApplyPendingDataChanges();
		if ( _viewDirty )
		{
			RefreshView();
		}
	}

	private void RebuildRowStats()
	{
		_rowStats.Clear();
		foreach ( var set in _sets )
		{
			var objects = GetValidObjects( set );
			_rowStats[set] = new SetRowStats
			{
				Objects = objects,
				EnabledCount = objects.Count( x => x.Enabled ),
				State = GetSetEnabledState( set, objects )
			};
		}
	}

	private SetRowStats GetRowStats( SelectionSetEntry set )
	{
		if ( set is null )
			return new SetRowStats();

		if ( _rowStats.TryGetValue( set, out var stats ) )
			return stats;

		var objects = GetValidObjects( set );
		stats = new SetRowStats
		{
			Objects = objects,
			EnabledCount = objects.Count( x => x.Enabled ),
			State = GetSetEnabledState( set, objects )
		};
		_rowStats[set] = stats;
		return stats;
	}

	private int SceneStateHash()
	{
		var scene = _session?.Scene;
		var directory = scene?.Directory;
		var hash = HashCode.Combine( _session, _sets.Count, _selectedSet );

		if ( directory is null )
			return hash;

		foreach ( var set in _sets )
		{
			hash = HashCode.Combine( hash, set.Name, set.ObjectIds.Count );

			foreach ( var id in set.ObjectIds )
			{
				var gameObject = directory.FindByGuid( id ) as GameObject;
				var isValid = gameObject.IsValid();
				hash = HashCode.Combine( hash, id, isValid, isValid ? gameObject.Enabled : false );
			}
		}

		return hash;
	}

	private void SaveSets()
	{
		if ( _session is null )
			return;

		var data = GetSelectionSetsData();
		if ( data is null )
			return;

		data.SelectionSets = _sets.Select( CloneSet ).ToList();
		_session.HasUnsavedChanges = true;
	}

	private List<SelectionSetEntry> LoadSceneSets()
	{
		return GetSelectionSetsData( createIfMissing: false )?.SelectionSets?.Select( CloneSet ).ToList() ?? [];
	}

	private SelectionSetsSystem.SelectionSetsData GetSelectionSetsData( bool createIfMissing = true )
	{
		var system = _session?.Scene?.GetSystem<SelectionSetsSystem>();
		if ( system is null )
			return null;

		if ( system.Data is null && createIfMissing )
		{
			system.Data = new SelectionSetsSystem.SelectionSetsData();
		}

		return system.Data;
	}

	private static SelectionSetEntry CloneSet( SelectionSetEntry set )
	{
		return new SelectionSetEntry
		{
			Name = set.Name ?? string.Empty,
			ObjectIds = set.ObjectIds?.Distinct().ToList() ?? [],
			Enabled = set.Enabled
		};
	}

	private void OpenCreateSetDialog()
	{
		if ( _session is null )
			return;

		Dialog.AskString( CreateSetFromSelection, "Name:", okay: "Create", title: "Create Selection Set" );
	}

	private void OpenBodyContextMenu()
	{
		var menu = new ContextMenu( this );
		menu.AddOption( "New Selection Set", "add", OpenCreateSetDialog );
		menu.OpenAtCursor();
	}

	private void OpenSetContextMenu( object item )
	{
		if ( item is not SelectionSetEntry set )
			return;

		_selectedSet = set;
		var enabledState = GetRowStats( set ).State;
		var (toggleLabel, toggleIcon) = GetToggleActionPresentation( enabledState );

		var menu = new ContextMenu( this );
		menu.AddOption( "Rename", "edit", () => BeginRenameSet( set ), "editor.rename" );
		menu.AddOption( "Select Set", "ads_click", () => SelectSetObjects( set ) );
		menu.AddOption( toggleLabel, toggleIcon, () => ToggleSetEnabled( set ) );
		menu.AddSeparator();
		menu.AddOption( "Add Selected Objects", "add_link", () => AddSelectedObjects( set ) );
		menu.AddOption( "Remove Selected Objects", "link_off", () => RemoveSelectedObjects( set ) );
		menu.AddSeparator();
		menu.AddOption( "Delete Set", "delete", () => DeleteSet( set ) );
		menu.OpenAtCursor();
	}

	[Shortcut( "editor.rename", "F2" )]
	public void RenameSelectionSet()
	{
		if ( _selectedSet is null )
			return;

		BeginRenameSet( _selectedSet );
	}

	private void BeginRenameSet( SelectionSetEntry set )
	{
		if ( set is null )
			return;

		_setList.BeginRename( set, name => RenameSetTo( set, name ) );
	}

	private void RenameSetTo( SelectionSetEntry set, string requestedName )
	{
		if ( set is null )
			return;

		var name = requestedName?.Trim();
		if ( string.IsNullOrWhiteSpace( name ) )
			return;

		if ( _sets.Any( x => !ReferenceEquals( x, set ) && x.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) )
		{
			name = GetUniqueName( name, set );
		}

		if ( set.Name.Equals( name, StringComparison.Ordinal ) )
			return;

		set.Name = name;
		SaveSets();
		RefreshAfterChange();
	}

	private void CreateSetFromSelection( string requestedName )
	{
		if ( _session is null )
			return;

		var name = requestedName?.Trim();
		if ( string.IsNullOrWhiteSpace( name ) )
		{
			name = GetNextDefaultName();
		}
		else if ( _sets.Any( x => x.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) )
		{
			name = GetUniqueName( name );
		}

		var set = new SelectionSetEntry { Name = name };
		foreach ( var id in GetCurrentSelectionIds() )
		{
			set.ObjectIds.Add( id );
		}

		_sets.Add( set );
		_selectedSet = set;

		SaveSets();
		RefreshAfterChange();
	}

	private void DeleteSet( SelectionSetEntry set = null )
	{
		set ??= _selectedSet;
		if ( set is null )
			return;

		_sets.Remove( set );
		if ( ReferenceEquals( _selectedSet, set ) )
		{
			_selectedSet = null;
		}

		SaveSets();
		RefreshAfterChange();
	}

	private void AddSelectedObjects( SelectionSetEntry set = null )
	{
		set ??= _selectedSet;
		if ( set is null )
			return;

		var changed = false;
		foreach ( var id in GetCurrentSelectionIds() )
		{
			if ( set.ObjectIds.Contains( id ) )
				continue;

			set.ObjectIds.Add( id );
			changed = true;
		}

		if ( !changed )
			return;

		SaveSets();
		RefreshAfterChange();
	}

	private void RemoveSelectedObjects( SelectionSetEntry set = null )
	{
		set ??= _selectedSet;
		if ( set is null )
			return;

		var selectedIds = GetCurrentSelectionIds();
		if ( selectedIds.Count == 0 )
			return;

		var before = set.ObjectIds.Count;
		set.ObjectIds.RemoveAll( selectedIds.Contains );
		if ( set.ObjectIds.Count == before )
			return;

		SaveSets();
		RefreshAfterChange();
	}

	private void SelectSetObjects( SelectionSetEntry set )
	{
		if ( set is null || _session is null )
			return;

		_selectedSet = set;
		var objects = GetRowStats( set ).Objects;
		using ( _session.UndoScope( $"Select Set: {set.Name}" ).Push() )
		{
			EditorScene.Selection.Clear();

			foreach ( var gameObject in objects )
			{
				EditorScene.Selection.Add( gameObject );
			}
		}
	}

	private void ToggleSetEnabled( SelectionSetEntry set )
	{
		if ( set is null )
			return;

		var enabledState = GetRowStats( set ).State;
		var enabled = enabledState != SetEnabledState.Enabled;
		set.Enabled = enabled;
		ApplySetEnabledStateToObjects( set, enabled );
		SaveSets();
		RefreshAfterChange();
	}

	private bool TryStartSetDrag( object item )
	{
		if ( item is not SelectionSetEntry set )
			return false;

		var drag = new Drag( _setList );
		drag.Data.Object = new SelectionSetDragData( set );
		drag.Execute();
		return true;
	}

	private void MoveSet( SelectionSetEntry draggedSet, SelectionSetEntry targetSet, bool insertAfter )
	{
		if ( draggedSet is null || targetSet is null || ReferenceEquals( draggedSet, targetSet ) )
			return;

		var fromIndex = _sets.IndexOf( draggedSet );
		var targetIndex = _sets.IndexOf( targetSet );
		if ( fromIndex < 0 || targetIndex < 0 )
			return;

		var insertIndex = targetIndex + (insertAfter ? 1 : 0);
		if ( fromIndex < insertIndex )
			insertIndex--;

		insertIndex = Math.Clamp( insertIndex, 0, _sets.Count - 1 );
		if ( insertIndex == fromIndex )
			return;

		_sets.RemoveAt( fromIndex );
		_sets.Insert( insertIndex, draggedSet );
		_selectedSet = draggedSet;
		SaveSets();
		RefreshAfterChange();
	}

	private void ApplySetEnabledStateToObjects( SelectionSetEntry set, bool enabled )
	{
		if ( _session is null )
			return;

		var objects = GetRowStats( set ).Objects;
		if ( objects.Count == 0 )
			return;

		var actionName = enabled ? "Enable Selection Set Objects" : "Disable Selection Set Objects";
		using ( _session.UndoScope( actionName ).WithGameObjectChanges( objects, GameObjectUndoFlags.Properties ).Push() )
		{
			foreach ( var gameObject in objects )
			{
				gameObject.Enabled = enabled;
			}
		}
	}

	private HashSet<Guid> GetCurrentSelectionIds()
	{
		var ids = new HashSet<Guid>();
		var selection = EditorScene.Selection;
		if ( selection is null )
			return ids;

		foreach ( var gameObject in selection.OfType<GameObject>() )
		{
			if ( !gameObject.IsValid() )
				continue;

			ids.Add( gameObject.Id );
		}

		return ids;
	}

	private List<GameObject> GetValidObjects( SelectionSetEntry set )
	{
		var objects = new List<GameObject>();
		if ( _session?.Scene?.Directory is null )
			return objects;

		foreach ( var id in set.ObjectIds )
		{
			var found = _session.Scene.Directory.FindByGuid( id ) as GameObject;
			if ( !found.IsValid() )
				continue;

			objects.Add( found );
		}

		return objects;
	}

	private bool PruneInvalidObjects()
	{
		if ( _session?.Scene?.Directory is null || _sets.Count == 0 )
			return false;

		var changed = false;
		foreach ( var set in _sets )
		{
			var validIds = set.ObjectIds
				.Where( id => (_session.Scene.Directory.FindByGuid( id ) as GameObject).IsValid() )
				.Distinct()
				.ToList();

			if ( validIds.Count == set.ObjectIds.Count )
				continue;

			set.ObjectIds = validIds;
			changed = true;
		}

		return changed;
	}

	private bool SyncSetEnabledStates()
	{
		if ( _sets.Count == 0 )
			return false;

		var changed = false;
		foreach ( var set in _sets )
		{
			var objects = GetValidObjects( set );
			var actualEnabled = GetSetEnabledState( set, objects ) == SetEnabledState.Enabled;
			if ( set.Enabled == actualEnabled )
				continue;

			set.Enabled = actualEnabled;
			changed = true;
		}

		return changed;
	}

	private void RefreshView()
	{
		_viewDirty = false;
		_setList.SetItems( _sets );

		if ( _selectedSet is not null && _sets.Contains( _selectedSet ) )
		{
			_setList.SelectItem( _selectedSet, skipEvents: true );
		}

		_setList.Update();
	}

	private void UpdateButtonStates()
	{
		_createButton.Enabled = _session is not null;
	}

	private void PaintSetItem( VirtualWidget item )
	{
		if ( item.Object is not SelectionSetEntry set )
			return;

		var isEven = item.Row % 2 == 0;
		var isHovered = item.Hovered;
		var selected = item.Selected || item.Pressed || item.Dragging;
		var stats = GetRowStats( set );
		var enabledState = stats.State;
		var enabled = enabledState == SetEnabledState.Enabled;
		var mixed = enabledState == SetEnabledState.Mixed;
		var opacity = enabledState == SetEnabledState.Disabled ? 0.6f : 1.0f;

		var fullSpanRect = item.Rect;
		fullSpanRect.Left = 0;
		fullSpanRect.Right = _setList.Width;

		Paint.ClearPen();
		if ( selected )
		{
			Paint.SetBrush( Theme.SelectedBackground );
			Paint.DrawRect( fullSpanRect );
		}
		else if ( isHovered )
		{
			Paint.SetBrush( Theme.SelectedBackground.WithAlpha( 0.25f ) );
			Paint.DrawRect( fullSpanRect );
		}
		else if ( isEven )
		{
			Paint.SetBrush( Theme.SurfaceLightBackground.WithAlpha( 0.1f ) );
			Paint.DrawRect( fullSpanRect );
		}

		var nameRect = GetNameColumnRect( item.Rect );
		var enabledCountRect = GetEnabledCountColumnRect( item.Rect );
		var objectRect = GetObjectCountColumnRect( item.Rect );
		var toggleRect = GetToggleButtonRect( item );
		var selectRect = GetSelectButtonRect( item );

		if ( _setList.TryGetDropPreview( set, out var insertAfter ) )
		{
			var dropRect = item.Rect;
			dropRect.Left = 0;
			dropRect.Right = _setList.Width;
			dropRect.Top = insertAfter ? dropRect.Bottom - 1 : dropRect.Top - 1;
			dropRect.Height = 2;

			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue );
			Paint.DrawRect( dropRect, 2 );
		}

		var textAlpha = enabled
			? (selected ? 1.0f : 0.85f)
			: mixed
				? (selected ? 0.9f : 0.75f)
				: (selected ? 0.7f : 0.5f);
		Paint.SetPen( Theme.TextControl.WithAlpha( textAlpha * opacity ) );
		Paint.DrawText( nameRect, set.Name, TextFlag.LeftCenter | TextFlag.SingleLine );

		Paint.SetPen( Theme.TextControl.WithAlpha( (selected ? 0.95f : 0.65f) * opacity ) );
		Paint.DrawText( enabledCountRect, stats.EnabledCount.ToString(), TextFlag.RightCenter | TextFlag.SingleLine );
		Paint.SetPen( Theme.TextControl.WithAlpha( (selected ? 0.95f : 0.65f) * opacity ) );
		Paint.DrawText( objectRect, stats.Objects.Count.ToString(), TextFlag.RightCenter | TextFlag.SingleLine );

		var iconAlpha = selected ? 0.95f : 0.75f;
		var (toggleIcon, toggleColor) = GetToggleIconPresentation( enabledState );
		Paint.SetPen( toggleColor.WithAlpha( iconAlpha * opacity ) );
		Paint.DrawIcon( toggleRect, toggleIcon, 14, TextFlag.Center );
		Paint.SetPen( Theme.TextControl.WithAlpha( iconAlpha * opacity ) );
		Paint.DrawIcon( selectRect, "ads_click", 14, TextFlag.Center );
	}

	private static Rect GetActionsColumnRect( Rect rowRect )
	{
		var rect = rowRect.Shrink( 6, 0, 10, 0 );
		rect.Right = rect.Left + 48;
		return rect;
	}

	private static Rect GetToggleButtonRect( VirtualWidget item )
	{
		var actionsRect = GetActionsColumnRect( item.Rect );
		var y = actionsRect.Top + (actionsRect.Height - 16) * 0.5f;
		return new Rect( actionsRect.Left, y, 16, 16 );
	}

	private static Rect GetSelectButtonRect( VirtualWidget item )
	{
		var toggleRect = GetToggleButtonRect( item );
		return new Rect( toggleRect.Right + 4, toggleRect.Top, 16, 16 );
	}

	private static Rect GetEnabledCountColumnRect( Rect rowRect )
	{
		var rect = rowRect.Shrink( 6, 0, 14, 0 );
		rect.Left = rect.Right - 58 - 58 - 6;
		rect.Right = rect.Left + 58;
		return rect;
	}

	private static Rect GetObjectCountColumnRect( Rect rowRect )
	{
		var rect = rowRect.Shrink( 6, 0, 14, 0 );
		rect.Left = rect.Right - 58;
		return rect;
	}

	private static Rect GetNameColumnRect( Rect rowRect )
	{
		var rect = rowRect.Shrink( 6, 0, 14, 0 );
		rect.Left += 48 + 8;
		rect.Right -= 58 + 6 + 58 + 8;
		return rect;
	}

	private static (string Label, string Icon) GetToggleActionPresentation( SetEnabledState state )
	{
		return state switch
		{
			SetEnabledState.Enabled => ("Disable Set", "visibility_off"),
			SetEnabledState.Mixed => ("Enable All In Set", "select_all"),
			_ => ("Enable Set", "visibility")
		};
	}

	private static (string Icon, Color Color) GetToggleIconPresentation( SetEnabledState state )
	{
		return state switch
		{
			SetEnabledState.Enabled => ("visibility", Theme.TextControl),
			SetEnabledState.Mixed => ("indeterminate_check_box", Theme.TextControl),
			_ => ("visibility_off", Theme.TextControl)
		};
	}

	private static SetEnabledState GetSetEnabledState( SelectionSetEntry set, List<GameObject> objects )
	{
		if ( objects.Count == 0 )
			return set.Enabled ? SetEnabledState.Enabled : SetEnabledState.Disabled;

		var enabledCount = objects.Count( x => x.Enabled );
		if ( enabledCount == 0 )
			return SetEnabledState.Disabled;

		if ( enabledCount == objects.Count )
			return SetEnabledState.Enabled;

		return SetEnabledState.Mixed;
	}

	private string GetNextDefaultName()
	{
		var index = 1;
		while ( true )
		{
			var name = $"Selection Set {index}";
			if ( !_sets.Any( x => x.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) )
				return name;

			index++;
		}
	}

	private string GetUniqueName( string baseName, SelectionSetEntry except = null )
	{
		var index = 2;
		while ( true )
		{
			var name = $"{baseName} ({index})";
			if ( !_sets.Any( x => !ReferenceEquals( x, except ) && x.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) )
				return name;

			index++;
		}
	}

	private sealed class SelectionSetDragData
	{
		public SelectionSetEntry Set { get; }

		public SelectionSetDragData( SelectionSetEntry set )
		{
			Set = set;
		}
	}

	private sealed class AddButton : Widget
	{
		public string Icon;

		public AddButton( string icon ) : base( null )
		{
			Icon = icon;
			Cursor = CursorShape.Finger;
			FixedHeight = Theme.RowHeight;
		}

		protected override Vector2 SizeHint()
		{
			return new Vector2( Theme.RowHeight );
		}

		protected override void OnPaint()
		{
			Paint.ClearBrush();
			Paint.ClearPen();

			var color = Enabled ? Theme.ControlBackground : Theme.SurfaceBackground;
			if ( Enabled && Paint.HasMouseOver )
			{
				color = color.Lighten( 0.1f );
			}

			Paint.SetBrush( color );
			Paint.DrawRect( LocalRect, Theme.ControlRadius );

			Paint.ClearBrush();
			Paint.SetPen( Theme.Primary );
			Paint.DrawIcon( LocalRect, Icon, 14, TextFlag.Center );
		}
	}

	private sealed class SelectionSetListView : ListView
	{
		private readonly SelectionSetsDock _dock;
		private PopupWidget _renamePopup;
		private SelectionSetEntry _dropPreviewSet;
		private bool _dropPreviewAfter;

		public SelectionSetListView( SelectionSetsDock dock, Widget parent ) : base( parent )
		{
			_dock = dock;
		}

		public void BeginRename( SelectionSetEntry set, Action<string> onRename )
		{
			if ( set is null || onRename is null )
				return;

			Rebuild();

			var item = ItemLayouts.FirstOrDefault( x => ReferenceEquals( x.Object, set ) );
			if ( item is null )
				return;

			_renamePopup?.Close();
			_renamePopup = new PopupWidget( this );
			_renamePopup.Layout = Layout.Column();

			var rect = GetNameColumnRect( item.Rect );
			_renamePopup.Position = ToScreen( rect.TopLeft );
			_renamePopup.Width = rect.Width;
			_renamePopup.Height = rect.Height;

			var lineEdit = _renamePopup.Layout.Add( new LineEdit() );
			lineEdit.Text = set.Name ?? string.Empty;

			void CompleteRename()
			{
				if ( _renamePopup?.Visible ?? false )
				{
					onRename( lineEdit.Text );
					_renamePopup.Close();
				}
			}

			_renamePopup.OnLostFocus += CompleteRename;
			lineEdit.ReturnPressed += CompleteRename;
			lineEdit.SelectAll();
			lineEdit.Focus();

			_renamePopup.Show();
		}

		protected override bool OnItemPressed( VirtualWidget pressedItem, MouseEvent e )
		{
			if ( pressedItem.Object is not SelectionSetEntry set )
				return true;

			if ( GetToggleButtonRect( pressedItem ).IsInside( e.LocalPosition ) )
			{
				_dock.ToggleSetEnabled( set );
				return false;
			}

			if ( GetSelectButtonRect( pressedItem ).IsInside( e.LocalPosition ) )
			{
				_dock.SelectSetObjects( set );
				return false;
			}

			return true;
		}

		public bool TryGetDropPreview( SelectionSetEntry set, out bool insertAfter )
		{
			insertAfter = _dropPreviewAfter;
			return ReferenceEquals( _dropPreviewSet, set );
		}

		private void SetDropPreview( SelectionSetEntry set, bool insertAfter )
		{
			if ( ReferenceEquals( _dropPreviewSet, set ) && _dropPreviewAfter == insertAfter )
				return;

			_dropPreviewSet = set;
			_dropPreviewAfter = insertAfter;
			Update();
		}

		private void ClearDropPreview()
		{
			if ( _dropPreviewSet is null )
				return;

			_dropPreviewSet = null;
			_dropPreviewAfter = false;
			Update();
		}

		protected override void OnDragHoverItem( DragEvent ev, VirtualWidget item )
		{
			base.OnDragHoverItem( ev, item );

			var dragData = ev.Data.OfType<SelectionSetDragData>().FirstOrDefault();
			if ( dragData?.Set is not SelectionSetEntry draggedSet )
			{
				ClearDropPreview();
				return;
			}

			if ( item.Object is not SelectionSetEntry targetSet || ReferenceEquals( draggedSet, targetSet ) )
			{
				ClearDropPreview();
				return;
			}

			SetDropPreview( targetSet, ev.LocalPosition.y > item.Rect.Center.y );
			ev.Action = DropAction.Move;
		}

		protected override void OnDropOnItem( DragEvent ev, VirtualWidget item )
		{
			var dragData = ev.Data.OfType<SelectionSetDragData>().FirstOrDefault();
			if ( dragData?.Set is not SelectionSetEntry draggedSet )
				return;

			if ( item.Object is not SelectionSetEntry targetSet )
				return;

			var insertAfter = ev.LocalPosition.y > item.Rect.Center.y;
			_dock.MoveSet( draggedSet, targetSet, insertAfter );
			ClearDropPreview();
			ev.Action = DropAction.Move;
		}

		public override void OnDragLeave()
		{
			ClearDropPreview();
			base.OnDragLeave();
		}
	}
}

