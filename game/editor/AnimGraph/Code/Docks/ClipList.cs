namespace Editor.AnimGraph;

/// <summary>
/// Dock panel listing the animation clips compiled against the graph's target skeleton.
/// Items can be dragged onto the graph to create AnimationClip nodes.
/// </summary>
public class ClipList : Widget, AssetSystem.IEventListener
{
	private Data _graph;
	private readonly ListView _listView;
	private readonly LineEdit _filter;
	private readonly Label _empty;
	private readonly IconButton _import;
	private readonly IconButton _sort;
	private readonly IconButton _used;
	private readonly ComboBox _typeFilter;
	private readonly bool _editing;
	private readonly Dictionary<string, ClipMetadata> _metadata = new( StringComparer.OrdinalIgnoreCase );
	private string _lastSkeleton;
	private string _pendingSelection;
	private bool _sortAscending = true;
	private HashSet<string> _referenced = new( StringComparer.OrdinalIgnoreCase );

	public Action<string> OnClipSelected { get; set; }

	public ClipList( Data graph, bool editing = true ) : base( null )
	{
		_graph = graph;
		_editing = editing;

		Name = "AnimationClips";
		WindowTitle = "Animation Clips";
		SetWindowIcon( "movie" );

		Layout = Layout.Column();
		Layout.Spacing = 2;

		var bar = new Widget( this );
		bar.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceBackground );
			Paint.DrawRect( bar.LocalRect );
			return true;
		};

		var header = bar.Layout = Layout.Row();
		header.Spacing = 2;
		header.Alignment = TextFlag.LeftCenter;
		Layout.Add( bar );

		_filter = header.Add( new LineEdit() { PlaceholderText = "Search clips...", ClearButtonEnabled = true, MinimumHeight = Theme.RowHeight }, 1 );
		_filter.TextEdited += _ => Rebuild();

		_typeFilter = header.Add( new ComboBox( this ) { ToolTip = "Filter clips by type" } );
		_typeFilter.SetStyles( "QComboBox { padding-top: 0px; padding-bottom: 0px; }" );
		_typeFilter.AddItem( "All types", selected: editing );
		_typeFilter.AddItem( "Animations", description: "Non-additive clips with more than one frame", selected: !editing );
		_typeFilter.AddItem( "Poses", description: "Single-frame poses, including additive poses" );
		_typeFilter.AddItem( "Additive", description: "Motion or poses that must be layered over a base pose" );
		_typeFilter.ItemChanged += Rebuild;

		_sort = header.Add( new IconButton( "swap_vert" ) );
		_sort.ToolTip = "Sort Z to A";
		_sort.IconSize = 14;
		_sort.Background = Theme.ControlBackground;
		_sort.Foreground = Theme.Primary;
		_sort.OnClick = () =>
		{
			_sortAscending = !_sortAscending;
			_sort.ToolTip = _sortAscending ? "Sort Z to A" : "Sort A to Z";
			Rebuild();
		};

		_used = header.Add( new IconButton( "link" ) );
		_used.Visible = editing;
		_used.ToolTip = "Only show animations used by this graph";
		_used.IconSize = 14;
		_used.Background = Theme.ControlBackground;
		_used.Foreground = Theme.Primary;
		_used.IsToggle = true;
		_used.OnToggled = _ => Rebuild();

		_import = header.Add( new IconButton( "file_download" )
		{
			IconSize = 14,
			Background = Theme.ControlBackground,
			Foreground = Theme.Primary,
			OnClick = ImportAnimation
		} );

		_listView = new AnimClipListView( this );
		_listView.MultiSelect = editing;
		_listView.ItemSize = new Vector2( 0, Theme.RowHeight );
		_listView.ItemSpacing = 0;
		if ( editing )
			_listView.ItemDrag = OnItemDragged;
		_listView.ItemClicked = OnItemClicked;
		_listView.ItemSelected = OnItemClicked;
		Layout.Add( _listView, 1 );
		_empty = Layout.Add( new Label( this ) { WordWrap = true, Alignment = TextFlag.Center }, 1 );
		_empty.ContentMargins = 12;
		_empty.Color = Theme.TextLight;
	}

	internal void SetGraph( Data graph )
	{
		_pendingSelection = null;
		_listView.UnselectAll( skipEvents: true );
		_graph = graph;
		Rebuild();
	}

	internal void ClearSelection()
	{
		_pendingSelection = null;
		_listView.UnselectAll();
		OnClipSelected?.Invoke( "" );
	}

	public void Rebuild()
	{
		if ( !string.Equals( _lastSkeleton, _graph?.TargetSkeleton, StringComparison.OrdinalIgnoreCase ) )
		{
			_pendingSelection = null;
			_listView.UnselectAll( skipEvents: true );
			_metadata.Clear();
		}
		_lastSkeleton = _graph?.TargetSkeleton;
		_import.Enabled = !string.IsNullOrWhiteSpace( _lastSkeleton )
			&& Skeleton.Load( _lastSkeleton ) is { IsValid: true, IsError: false, BoneCount: > 0 };
		_import.ToolTip = _import.Enabled ? "Import an animation for this skeleton" : "Choose a valid target skeleton before importing animations";

		_referenced = GetReferencedAnimations();

		var filter = _filter?.Text?.Trim() ?? "";
		var typeFilter = (ClipFilter)_typeFilter.CurrentIndex;
		var filtered = (_graph?.SelectedVariation.CompatibleClips ?? [])
			.Where( n => string.IsNullOrEmpty( filter ) || n.Contains( filter, StringComparison.OrdinalIgnoreCase ) )
			.Where( n => !_used.IsActive || _referenced.Contains( n ) )
			.Where( n => typeFilter == ClipFilter.All || MatchesType( GetMetadata( n ), typeFilter ) );

		var sorted = (_sortAscending
			? filtered.OrderBy( System.IO.Path.GetFileNameWithoutExtension ).ThenBy( n => n )
			: filtered.OrderByDescending( System.IO.Path.GetFileNameWithoutExtension ).ThenByDescending( n => n )).ToArray();

		_listView.SetItems( sorted.Cast<object>() );
		foreach ( var selected in _listView.SelectedItems.OfType<string>().Except( sorted, StringComparer.OrdinalIgnoreCase ).ToArray() )
			_listView.UnselectItem( selected, skipEvents: true );
		_listView.Visible = sorted.Length > 0;
		_empty.Visible = sorted.Length == 0;
		_empty.Text = string.IsNullOrWhiteSpace( _lastSkeleton )
			? "Choose a target skeleton to see its animation clips."
			: !string.IsNullOrEmpty( filter ) || typeFilter != ClipFilter.All ? "No clips match these filters."
			: _used.IsActive ? "No clips are used by this graph."
			: "No animation clips use this skeleton. Use Import Animation to add one.";

		if ( _pendingSelection is { } selection && sorted.Contains( selection, StringComparer.OrdinalIgnoreCase ) )
		{
			_pendingSelection = null;
			_listView.SelectItem( selection );
			_listView.ScrollTo( selection );
		}
	}

	private void ImportAnimation()
	{
		var dialog = new ImportAnimationDialog( Project.Current.GetAssetsPath(), _graph.TargetSkeleton, this );
		dialog.Imported = asset =>
		{
			if ( !this.IsValid() )
				return;

			_filter.Text = "";
			_typeFilter.CurrentIndex = (int)ClipFilter.All;
			_used.IsActive = false;
			_pendingSelection = asset.Path;
			Rebuild();
		};
		dialog.Show();
	}

	void AssetSystem.IEventListener.OnAssetSystemChanges() => Rebuild();

	void AssetSystem.IEventListener.OnAssetChanged( Asset asset )
	{
		if ( asset.AssetType.FileExtension == "anim" )
			_metadata.Remove( asset.Path );

		if ( asset.AssetType.FileExtension is "anim" or "skel" )
			Rebuild();
	}

	private enum ClipFilter
	{
		All,
		Animations,
		Poses,
		Additive
	}

	private static bool MatchesType( ClipMetadata clip, ClipFilter filter ) => filter switch
	{
		ClipFilter.Animations => clip.FrameCount > 1 && !clip.IsAdditive,
		ClipFilter.Poses => clip.FrameCount == 1,
		ClipFilter.Additive => clip.IsAdditive,
		_ => true
	};

	internal ClipMetadata GetMetadata( string path )
	{
		if ( _metadata.TryGetValue( path, out var metadata ) )
			return metadata;

		var clip = Clip.Load( path );
		metadata = clip is { IsValid: true, IsError: false }
			? new ClipMetadata( clip.FrameCount, clip.Duration, clip.IsAdditive )
			: new ClipMetadata( 0, 0f, false );
		_metadata[path] = metadata;
		return metadata;
	}

	internal string GetClipWarning( ClipMetadata clip ) => clip.FrameCount == 0
		? "This clip could not be loaded. Check its compilation errors."
		: !_editing && clip.IsAdditive ? "Needs a base pose" : "";

	internal readonly record struct ClipMetadata( int FrameCount, float Duration, bool IsAdditive )
	{
		public string Type { get; } = FrameCount switch
		{
			0 => "Unavailable",
			1 => IsAdditive ? "Additive pose" : "Pose",
			_ => IsAdditive ? "Additive" : "Animation"
		};
		public string Timing { get; } = FrameCount switch
		{
			> 1 => $"{Duration:0.##}s",
			1 => "1 frame",
			_ => ""
		};
		public string Icon => FrameCount == 0 ? "error_outline" : IsAdditive ? "layers" : FrameCount == 1 ? "image" : "movie";
	}

	private HashSet<string> GetReferencedAnimations()
	{
		var set = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var node in _graph?.AllNodes ?? [] )
		{
			var name = node switch
			{
				Nodes.AnimationClipNode clip => clip.AnimationName,
				Nodes.AnimationPoseNode pose => pose.AnimationName,
				_ => null
			};

			if ( !string.IsNullOrEmpty( name ) )
				set.Add( name );
		}

		return set;
	}

	public bool IsReferenced( string animName ) => _referenced.Contains( animName );

	private bool OnItemDragged( object item )
	{
		if ( item is not string animName ) return false;

		// Dragging an item that's part of the selection carries the whole selection
		var names = _listView.SelectedItems.OfType<string>().ToList();
		if ( !names.Contains( animName ) )
			names = [animName];

		var drag = new Drag( this );
		drag.Data.Text = string.Join( "\n", names );
		drag.Data.Object = new ClipDragData( names );
		drag.Execute();
		return true;
	}

	private void OnItemClicked( object item )
	{
		if ( item is string animName )
			OnClipSelected?.Invoke( animName );
	}

	internal void OnGraphEdited()
	{
		if ( _graph?.TargetSkeleton != _lastSkeleton )
		{
			Rebuild();
			return;
		}

		// keep referenced markers fresh
		var referenced = GetReferencedAnimations();
		if ( !referenced.SetEquals( _referenced ) )
			Rebuild();
	}
}

file class AnimClipListView : ListView
{
	private readonly ClipList _owner;

	public AnimClipListView( ClipList parent ) : base( parent )
	{
		_owner = parent;
		Margin = new Sandbox.UI.Margin( 8, 0, 16, 8 );

		OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( LocalRect, Theme.ControlRadius );
			return false;
		};
	}

	protected override string GetTooltip( object obj )
	{
		if ( obj is not string path )
			return base.GetTooltip( obj );

		var clip = _owner.GetMetadata( path );
		var warning = _owner.GetClipWarning( clip );
		var details = clip.FrameCount > 1 ? $"{clip.Type} - {clip.Timing}, {clip.FrameCount} frames"
			: clip.FrameCount == 1 ? $"{clip.Type} - 1 frame" : clip.Type;
		return $"{path}\n{details}"
			+ (string.IsNullOrEmpty( warning ) ? "" : $"\n{warning}");
	}

	protected override void PaintItem( VirtualWidget item )
	{
		if ( item.Object is not string animName )
			return;

		var fullSpanRect = item.Rect;
		fullSpanRect.Left = 0;
		fullSpanRect.Right = Width;

		var isEven = item.Row % 2 == 0;
		var selected = item.Selected || item.Pressed;

		Paint.ClearPen();

		if ( selected )
		{
			Paint.SetBrush( Theme.SelectedBackground );
			Paint.DrawRect( fullSpanRect );
		}
		else if ( item.Hovered )
		{
			Paint.SetBrush( Theme.SelectedBackground.WithAlpha( 0.25f ) );
			Paint.DrawRect( fullSpanRect );
		}
		else if ( isEven )
		{
			Paint.SetBrush( Theme.SurfaceLightBackground.WithAlpha( 0.1f ) );
			Paint.DrawRect( fullSpanRect );
		}

		var r = item.Rect;
		r.Left += 4;

		var referenced = _owner.IsReferenced( animName );
		var clip = _owner.GetMetadata( animName );
		var hasWarning = !string.IsNullOrEmpty( _owner.GetClipWarning( clip ) );
		var warningColor = clip.FrameCount == 0 ? Theme.Red : Theme.Yellow;

		Paint.SetPen( hasWarning ? warningColor : referenced ? Theme.Green.WithAlpha( 0.9f ) : Theme.Primary );
		Paint.DrawIcon( r, hasWarning && clip.FrameCount > 0 ? "warning_amber" : clip.Icon, 16, TextFlag.LeftCenter );
		r.Left += 22;

		var timing = r;
		timing.Left = Math.Max( r.Left, r.Right - 52 );
		var type = r;
		type.Right = Math.Max( r.Left, timing.Left - 8 );
		type.Left = Math.Max( r.Left, type.Right - 80 );
		var nameRect = r;
		nameRect.Right = Math.Max( r.Left, type.Left - 8 );
		float opacity = item.Hovered || selected ? 0.9f : 0.8f;
		Paint.SetPen( Theme.TextControl.WithAlpha( opacity ) );
		Paint.SetDefaultFont();

		var name = System.IO.Path.GetFileNameWithoutExtension( animName );
		Paint.DrawText( nameRect, Paint.GetElidedText( name, Math.Max( 0, nameRect.Width ) ), TextFlag.LeftCenter | TextFlag.SingleLine );

		Paint.SetPen( Theme.TextControl.WithAlpha( 0.7f * opacity ) );
		Paint.DrawText( timing, clip.Timing, TextFlag.RightCenter | TextFlag.SingleLine );
		if ( hasWarning )
			Paint.SetPen( warningColor );
		Paint.DrawText( type, Paint.GetElidedText( clip.Type, Math.Max( 0, type.Width ) ), TextFlag.LeftCenter | TextFlag.SingleLine );
	}
}

/// <summary>
/// Marker object carried in drag data to identify animation clip drags.
/// </summary>
public record ClipDragData( IReadOnlyList<string> AnimationNames );

/// <summary>
/// Readonly info about an animation clip, displayed in the properties panel
/// when a clip is selected in the animation list.
/// </summary>
public class AnimationClipInfo
{
	private readonly Clip _clip;

	[Property, Sandbox.ReadOnly, Title( "Name" )]
	public string AnimationName { get; }

	[Property, Sandbox.ReadOnly]
	public float Duration => _clip?.Duration ?? 0f;

	[Property, Sandbox.ReadOnly, Title( "Frames" )]
	public int FrameCount => _clip?.FrameCount ?? 0;

	[Property, Sandbox.ReadOnly, Title( "FPS" )]
	public float FrameRate => _clip?.FrameRate ?? 0f;

	[Property, Sandbox.ReadOnly, Title( "Additive" )]
	public bool IsAdditive => _clip?.IsAdditive ?? false;

	[Property, Sandbox.ReadOnly, Title( "Skeleton" )]
	public string SkeletonName => _clip?.SkeletonName ?? "";

	public AnimationClipInfo( string name )
	{
		AnimationName = name;
		_clip = string.IsNullOrWhiteSpace( name ) ? null : Clip.Load( name );
	}
}
