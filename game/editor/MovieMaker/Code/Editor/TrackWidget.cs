using Editor.NodeEditor;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Properties;
using Sandbox.UI;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>
/// An item in the <see cref="TrackListWidget"/>, showing the name of a track with buttons to configure it.
/// </summary>
public partial class TrackWidget : Widget
{
	public TrackListWidget TrackList { get; }
	public new TrackWidget? Parent { get; }

	public new IEnumerable<TrackWidget> Children => _children;

	public TrackView View { get; }

	RealTimeSince _timeSinceInteraction = 1000;

	private readonly Label? _label;
	private readonly Button _collapseButton;
	private readonly Button _lockButton;
	private readonly Layout _childLayout;
	private readonly SynchronizedSet<TrackView, TrackWidget> _children;

	private ControlWidget? _controlWidget;

	public TrackWidget( TrackListWidget trackList, TrackWidget? parent, TrackView view )
		: base( (Widget?)parent ?? trackList )
	{
		TrackList = trackList;
		Parent = parent;

		View = view;
		FocusMode = FocusMode.TabOrClickOrWheel;
		VerticalSizeMode = SizeMode.CanGrow;

		_children = new SynchronizedSet<TrackView, TrackWidget>(
			AddChildTrack, RemoveChildTrack, UpdateChildTrack );

		ToolTip = View.Description;

		View.Changed += View_Changed;
		View.ValueChanged += View_ValueChanged;

		Layout = Layout.Column();

		var row = Layout.AddRow();

		row.Spacing = 4f;
		row.Margin = 4f;

		_childLayout = Layout.Add( Layout.Column() );
		_childLayout.Margin = new Margin( 8f, 0f, 0f, 0f );

		_collapseButton = new CollapseButton( this );
		row.Add( _collapseButton );

		if ( !AddReferenceControl( row ) )
		{
			row.AddSpacingCell( 8f );

			_label = row.Add( new Label( view.Target.Name ) { Color = Color.White } );
		}

		row.AddStretchCell();
		row.AddSpacingCell( 24f );

		_lockButton = row.Add( new LockButton( this ) );

		View_Changed( view );
	}

	private TrackWidget AddChildTrack( TrackView source ) => new( TrackList, this, source );
	private void RemoveChildTrack( TrackWidget item ) => item.Destroy();
	private bool UpdateChildTrack( TrackView source, TrackWidget item ) => item.UpdateLayout();

	public bool UpdateLayout()
	{
		_children.Update( View.IsExpanded ? View.Children : [] );
		_childLayout.Clear( false );

		foreach ( var child in _children )
		{
			_childLayout.Add( child );
		}

		return true;
	}

	private bool AddReferenceControl( Layout layout )
	{
		if ( View.Target is not ITrackReference reference ) return false;
		if ( View.IsBoneObject ) return false;

		// Add control to retarget a scene reference (Component / GameObject)

		_controlWidget = null;

		if ( View.Track is ProjectSequenceTrack )
		{
			//
		}
		else if ( reference is ITrackReference<GameObject> goReference )
		{
			_controlWidget = ControlWidget.Create( EditorTypeLibrary.CreateProperty( reference.Name,
				() => goReference.Value, goReference.Bind ) );
		}
		else
		{
			var helperType = typeof( ReflectionHelper<> ).MakeGenericType( reference.TargetType );
			var createControlMethod = helperType.GetMethod( nameof( ReflectionHelper<IValid>.CreateControlWidget ),
				BindingFlags.Static | BindingFlags.Public )!;

			_controlWidget = (ControlWidget)createControlMethod.Invoke( null, [View.Track, reference] )!;
		}

		if ( !_controlWidget.IsValid() ) return false;

		_controlWidget.MaximumWidth = 300;

		layout.Add( _controlWidget );
		return true;
	}

	private void View_Changed( TrackView view )
	{
		_collapseButton.Visible = view.Children.Count > 0;

		_lockButton.Update();
		_collapseButton.Update();

		_label?.Color = !View.IsLocked ? IsSelected ? Color.White : Color.White.Darken( 0.4f ) : Color.White.Darken( 0.6f );
		_controlWidget?.Enabled = !View.IsLocked;

		Update();
	}

	private void View_ValueChanged( TrackView view )
	{
		_timeSinceInteraction = 0f;

		Update();
	}

	public override void OnDestroyed()
	{
		View.Changed -= View_Changed;
		View.ValueChanged -= View_ValueChanged;

		base.OnDestroyed();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( !e.MiddleMouseButton )
		{
			e.Accepted = true;
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( !e.LeftMouseButton ) return;

		if ( e.HasCtrl )
		{
			View.ToggleSelect();
		}
		else if ( e.HasShift )
		{
			View.RangeSelect();
		}
		else
		{
			View.Select();
		}

		e.Accepted = true;
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		View.InspectProperty();

		e.Accepted = true;
	}

	protected override void OnMouseEnter()
	{
		View.IsHovered = true;

		base.OnMouseEnter();
	}

	protected override void OnMouseLeave()
	{
		View.IsHovered = false;

		base.OnMouseLeave();
	}

	protected override Vector2 SizeHint()
	{
		return 32;
	}

	public bool IsSelected => View.IsSelected;

	public Color BackgroundColor
	{
		get
		{
			var canModify = !View.IsLocked;

			var defaultColor = Theme.SurfaceBackground.LerpTo( Theme.ControlBackground, canModify ? 0f : 0.5f );
			var hoveredColor = defaultColor.Lighten( 0.25f );
			var selectedColor = Color.Lerp( defaultColor, Theme.Primary, canModify ? 0.5f : 0.2f );

			var isHovered = canModify && View.IsHovered;

			return IsSelected ? selectedColor
				: isHovered ? hoveredColor
					: defaultColor;
		}
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = false;
		Paint.SetBrushAndPen( BackgroundColor );
		Paint.DrawRect( new Rect( LocalRect.Left + 1f, LocalRect.Top + 1f, LocalRect.Width - 2f, Timeline.TrackHeight - 2f ), 4 );

		if ( _timeSinceInteraction < 2.0f )
		{
			var delta = _timeSinceInteraction.Relative.Remap( 2.0f, 0, 0, 1 );
			Paint.SetBrush( Theme.Yellow.WithAlpha( delta ) );
			Paint.DrawRect( new Rect( LocalRect.Right - 4, LocalRect.Top, 32, Timeline.TrackHeight ) );
			Update();
		}
	}

	private Menu? _menu;

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		e.Accepted = true;

		if ( !View.IsSelected )
		{
			if ( (Application.KeyboardModifiers & KeyboardModifiers.Ctrl) != 0 )
			{
				View.ToggleSelect();
			}
			else if ( (Application.KeyboardModifiers & KeyboardModifiers.Shift) != 0 )
			{
				View.RangeSelect();
			}
			else
			{
				View.Select();
			}
		}

		ShowContextMenu();
	}

	public void ShowContextMenu()
	{
		_menu = new Menu( this );

		var trackViews = View.TrackList.SelectedTracks.ToArray();

		if ( trackViews.Length == 1 )
		{
			_menu.AddHeading( $"{View.Title} Track" );

			if ( View.Track is ProjectSequenceTrack sequenceTrack )
			{
				var rename = _menu.AddMenu( "Rename", "edit" );

				rename.AddLineEdit( "Name", sequenceTrack.Name, autoFocus: true, onSubmit: OnRename );
			}

			AddCommonContextMenuOptions( _menu, trackViews );

			if ( CanMoveToRoot )
			{
				_menu.AddOption( "Move to Root", "subdirectory_arrow_left", MoveToRoot );
			}

			if ( CanMoveToParent( out var parentTrack ) )
			{
				_menu.AddOption( "Move to Parent", "subdirectory_arrow_right", () => MoveToParent( parentTrack ) );
			}

			if ( CanHaveSubTracks )
			{
				CreateSubTrackMenu( _menu );
			}
		}
		else
		{
			_menu.AddHeading( "Selected Tracks" );

			AddCommonContextMenuOptions( _menu, trackViews );
		}

		_menu.OpenAtCursor();
	}

	private void AddCommonContextMenuOptions( Menu menu, IReadOnlyList<TrackView> trackViews )
	{
		var anyLocked = trackViews.Any( x => x.IsLockedSelf );
		var anyUnlocked = trackViews.Any( x => !x.IsLockedSelf );

		if ( anyUnlocked )
		{
			menu.AddOption( "Lock", "lock", () =>
			{
				foreach ( var track in trackViews )
				{
					track.IsLockedSelf = true;
				}
			} );
		}

		if ( anyLocked )
		{
			menu.AddOption( "Unlock", "lock_open", () =>
			{
				foreach ( var track in trackViews )
				{
					track.IsLockedSelf = false;
				}
			} );
		}

		menu.AddOption( "Remove", "delete", () =>
		{
			foreach ( var track in trackViews )
			{
				track.Remove();
			}
		} );

		menu.AddOption( "Create Missing References", "person_add", () =>
		{
			var touched = new HashSet<TrackView>();

			foreach ( var trackView in trackViews )
			{
				CreateTargets( trackView, touched );
			}
		} );
	}

	private void CreateTargets( TrackView view, HashSet<TrackView> touched )
	{
		if ( !touched.Add( view ) ) return;
		if ( view.IsLocked ) return;
		if ( view.Parent?.Target is { IsBound: false } ) return;

		var binder = TrackList.Session.Binder;

		using var sceneScope = binder.Scene.Push();

		if ( view.Track is ProjectSequenceTrack sequenceTrack )
		{
			foreach ( var refTrack in sequenceTrack.ReferenceTracks )
			{
				CreateTarget( refTrack, binder.Get( refTrack ) );
			}
		}
		else if ( view.Track is IReferenceTrack refTrack && view.Target is ITrackReference trackRef )
		{
			CreateTarget( refTrack, trackRef );
		}

		foreach ( var childView in view.Children )
		{
			CreateTargets( childView, touched );
		}
	}

	private void CreateTarget( IReferenceTrack track, ITrackReference target )
	{
		var parentGo = target.Parent?.Value;

		if ( target is ITrackReference<GameObject> { IsBound: false } goRef )
		{
			var go = new GameObject( parentGo, name: track.Name );

			goRef.Bind( go );
		}
		else if ( parentGo is not null && target is { IsBound: false } cmpRef )
		{
			var typeDesc = TypeLibrary.GetType( target.TargetType );
			if ( typeDesc is null ) return;

			var cmp = parentGo.Components.Create( typeDesc );

			cmpRef.Bind( cmp );
		}
	}

	private bool? GetAggregateLockState( IEnumerable<TrackView> trackViews )
	{
		var anyLocked = false;
		var anyUnlocked = false;

		foreach ( var view in trackViews )
		{
			if ( view.IsLockedSelf ) anyLocked = true;
			else anyUnlocked = true;
		}

		return anyLocked == anyUnlocked ? null : anyLocked;
	}

	/// <summary>
	/// True if this is a child GameObject track, and not representing a bone object.
	/// </summary>
	private bool CanMoveToRoot
	{
		get
		{
			if ( View is not { Target: ITrackReference<GameObject> reference, Parent: not null } )
			{
				return false;
			}

			// Don't allow moving bone tracks to root

			return !reference.IsBound || (reference.Value?.Flags & GameObjectFlags.Bone) == 0;
		}
	}

	/// <summary>
	/// Should we show sub-track menu options?
	/// </summary>
	private bool CanHaveSubTracks => View.Track is not ProjectSequenceTrack && (View.Children.Count > 0 || TrackProperty.GetAll( View.Target ).Any());

	/// <summary>
	/// True if this is a GameObject track, and the object's parent also has a track that
	/// this track isn't parented to.
	/// </summary>
	private bool CanMoveToParent( [NotNullWhen( true )] out IProjectReferenceTrack? parentTrack )
	{
		parentTrack = null;

		if ( View is not { Target: ITrackReference<GameObject> reference } )
		{
			return false;
		}

		if ( !reference.IsBound || reference.Value is not { Parent: { } parent } )
		{
			return false;
		}

		parentTrack = TrackList.Session.GetTrack( parent ) as IProjectReferenceTrack;

		if ( parentTrack is null )
		{
			return false;
		}

		// Already parented

		return View.Parent?.Track != parentTrack;
	}

	private record AvailableTrackProperty( string Name, string Category, Type Type, Action Create );

	private void CreateSubTrackMenu( Menu parent )
	{
		parent.AddHeading( "Sub-Tracks" );

		var menu = parent.AddMenu( "Add / Remove", "playlist_add_check" );

		var session = TrackList.Session;
		var availableTracks = new List<AvailableTrackProperty>();

		if ( View.Target is ITrackReference<GameObject> { IsBound: true, Value: { Components.Count: > 0 } go } )
		{
			foreach ( var component in go.Components.GetAll() )
			{
				var type = component.GetType();

				availableTracks.Add( new AvailableTrackProperty( type.Name, "Components", type,
					() => session.GetOrCreateTrack( component ) ) );
			}
		}

		foreach ( var property in TrackProperty.GetAll( View.Target ) )
		{
			availableTracks.Add( new AvailableTrackProperty( property.Name, property.Category, property.Type,
				() => session.GetOrCreateTrack( View.Track, property.Name ) ) );
		}

		var categories = availableTracks.GroupBy( x => x.Category ).ToArray();

		Action? updateActive = null;

		foreach ( var category in categories.OrderBy( x => x.Key ) )
		{
			var subMenu = categories.Length == 1 ? menu : menu.AddMenu( category.Key );

			foreach ( var type in category.GroupBy( x => x.Type.ToSimpleString( false ) ).OrderBy( x => x.Key ) )
			{
				if ( category.Key != "Components" )
				{
					subMenu.AddHeading( type.Key ).Color = Theme.TextDisabled;
				}

				foreach ( var item in type.OrderBy( x => x.Name ) )
				{
					var option = new ToggleOption( item.Name, false, create =>
					{
						using var scope = session.History.Push( $"{(create ? "Create" : "Remove")} Track ({item.Name})" );

						if ( create )
						{
							item.Create();
						}
						else
						{
							View.Children
								.FirstOrDefault( x => x.Track.Name == item.Name )?
								.Remove();
						}

						session.TrackList.Update();
						session.ClipModified();
					} );

					updateActive += () => option.IsActive = View.Children.Any( x => x.Track.Name == item.Name );

					subMenu.AddWidget( option );
				}
			}
		}

		menu.AboutToShow += () => updateActive?.Invoke();

		if ( availableTracks.Any( x => session.GetTrack( View.Track, x.Name ) is null ) )
		{
			parent.AddOption( "Add All", "playlist_add", () =>
			{
				foreach ( var available in availableTracks )
				{
					session.GetOrCreateTrack( View.Track, available.Name );
				}

				session.TrackList.Update();
				session.ClipModified();
			} );
		}

		if ( View.Children.Count > 0 )
		{
			parent.AddOption( "Remove All", "playlist_remove", () =>
			{
				foreach ( var child in View.Children.ToArray() )
				{
					child.Track.Remove();
				}

				session.TrackList.Update();
				session.ClipModified();
			} );

			parent.AddOption( "Remove Empty", "cleaning_services", RemoveEmptyChildren );
		}

		CreatePresetMenu( parent );

		if ( View.Children.Count <= 0 ) return;

		parent.AddSeparator();

		var lockState = GetAggregateLockState( View.Children );

		if ( lockState != true )
		{
			parent.AddOption( "Lock All", "lock", LockChildren );
		}

		if ( lockState != false )
		{
			parent.AddOption( "Unlock All", "lock_open", UnlockChildren );
		}
	}

	private void CreatePresetMenu( Menu parent )
	{
		var session = TrackList.Session;

		var matching = TrackPreset.BuiltInPresets
			.Concat( session.Config.TrackPresets )
			.Where( x => x.Root.Matches( View.Target ) )
			.ToArray();

		var canLoad = matching.Length > 0;
		var canSave = View.Children.Any();

		if ( !canLoad && !canSave ) return;

		if ( canLoad )
		{
			var menu = parent.AddMenu( "Load Preset", "menu_open" );

			PopulatePresetMenu( menu, matching, [View] );
		}

		if ( canSave )
		{
			var menu = parent.AddMenu( "Save Preset", "save" );

			menu.AddLineEdit( "Title", autoFocus: true, onSubmit: title =>
			{
				if ( string.IsNullOrWhiteSpace( title ) ) return;

				var preset = new TrackPreset( new TrackPresetMetadata( title ), TrackPresetNode.FromTrackView( View ) );

				session.Config.TrackPresets.Add( preset );
				session.SaveConfig();
			} );
		}
	}

	public static void PopulatePresetMenu( Menu menu, IReadOnlyList<TrackPreset> presets, IReadOnlyList<TrackView> rootViews )
	{
		if ( rootViews.Count == 0 ) return;

		var session = rootViews[0].TrackList.Session;

		Action? updateActive = null;

		foreach ( var preset in presets )
		{
			var option = new ToggleOption( preset.Meta.Title, false, create =>
			{
				using var scope = session.History.Push( $"{(create ? "Create" : "Remove")} Preset Tracks ({preset.Meta.Title})" );

				foreach ( var rootView in rootViews )
				{
					if ( create )
					{
						session.LoadPreset( rootView.Track, rootView.Target, preset.Root );
					}
					else
					{
						session.RemovePreset( rootView.Track, rootView.Target, preset.Root );
					}
				}

				session.TrackList.Update();
				session.ClipModified();

				updateActive?.Invoke();
			}, TrackPreset.BuiltInPresets.Contains( preset ) ? null : () =>
			{
				session.Config.TrackPresets.Remove( preset );
				session.SaveConfig();
			} );

			option.ToolTip = preset.Meta.Description;

			updateActive += () =>
			{
				var totalCount = rootViews.Max( x => preset.AvailableTrackCount( x.Track, session.Binder ) );
				var matchingCount = rootViews.Min( x => preset.MatchingTrackCount( x.Track ) );

				option.Title = $"{preset.Meta.Title} ({matchingCount} / {totalCount} Tracks)";
				option.IsActive = rootViews.All( x => preset.AllTracksExist( x.Track, session.Binder ) );
				option.Update();
			};

			menu.AddWidget( option );
		}

		menu.AboutToShow += () => updateActive?.Invoke();
	}

	private void OnRename( string name )
	{
		if ( View.Track is not ProjectSequenceTrack sequenceTrack ) return;

		sequenceTrack.Name = name;

		if ( _label is { } label ) label.Text = name;

		// Track order might have changed

		TrackList.Session.TrackList.Update();
	}

	private void Remove()
	{
		using var scope = TrackList.Session.History.Push( "Remove Track(s)" );
		View.Remove();
	}

	private void MoveToRoot()
	{
		if ( View.Track is not IProjectTrackInternal track ) return;
		if ( View.Parent?.Track is not IProjectTrackInternal parentTrack ) return;

		parentTrack.RemoveChild( track );

		TrackList.Session.TrackList.Update();
	}

	private void MoveToParent( IProjectReferenceTrack parentTrack )
	{
		if ( View.Track is not IProjectTrackInternal track ) return;
		if ( parentTrack is not IProjectTrackInternal newParentTrack ) return;

		if ( View.Parent?.Track is IProjectTrackInternal currentParentTrack )
		{
			if ( currentParentTrack == parentTrack ) return;

			currentParentTrack.RemoveChild( track );
		}

		newParentTrack.AddChild( track );

		TrackList.Session.TrackList.Update();
	}

	private void RemoveEmptyChildren()
	{
		foreach ( var child in View.Children.ToArray() )
		{
			RemoveEmptyCore( child );
		}

		TrackList.Session.TrackList.Update();
	}

	private static bool RemoveEmptyCore( TrackView view )
	{
		var allChildrenRemoved = true;

		foreach ( var child in view.Children.ToArray() )
		{
			allChildrenRemoved &= RemoveEmptyCore( child );
		}

		if ( allChildrenRemoved && view.IsEmpty )
		{
			view.Remove();
			return true;
		}

		return false;
	}

	private void LockChildren()
	{
		foreach ( var child in View.Children )
		{
			child.IsLockedSelf = true;
		}
	}

	private void UnlockChildren()
	{
		foreach ( var child in View.Children )
		{
			child.IsLockedSelf = false;
		}
	}
}

// TODO: surely there's an easier way to stop Menus from closing
file sealed class ToggleOption : Widget
{
	private readonly Label _label;
	private readonly Action<bool> _toggled;

	public new bool Enabled
	{
		get => base.Enabled;
		set
		{
			_label.Color = value ? Theme.TextControl : Theme.TextControl.Darken( 0.5f );
			_label.Update();

			base.Enabled = value;
		}
	}

	private bool _isActive;

	public bool IsActive
	{
		get => _isActive;
		set
		{
			_isActive = value;

			_label.SetStyles( value ? "font-weight: bold;" : "font-weight: regular;" );
			Update();
		}
	}

	public string Title
	{
		get => _label.Text;
		set => _label.Text = value;
	}

	protected override Vector2 SizeHint()
	{
		// So there's enough space for the label to become bold

		return base.SizeHint() * new Vector2( 1.1f, 1f );
	}

	public ToggleOption( string title, bool active, Action<bool> toggled, Action? deleted = null )
	{
		Layout = Layout.Row();
		Layout.Margin = new Margin( 40f, 5f, 16f, 5f );

		_label = new Label( title, this );
		_toggled = toggled;

		MinimumWidth = 120f;

		Layout.Add( _label );

		IsActive = active;

		if ( deleted is not null )
		{
			Layout.Margin = Layout.Margin with { Right = 8f };
			Layout.Add( new IconButton( "delete", () =>
			{
				Dialog.AskConfirm( deleted, $"Are you sure you want to delete {title}?", "Delete Confirmation" );
			} )
			{
				Foreground = Theme.Red,
				ForegroundActive = Theme.Red
			} );
		}
	}

	protected override void OnPaint()
	{
		if ( Paint.HasMouseOver )
		{
			Paint.SetBrushAndPen( Theme.SurfaceBackground );
			Paint.DrawRect( LocalRect.Shrink( IsActive ? 0f : 4f, 0f, 4f, 0f ), 3f );
		}

		if ( IsActive )
		{
			Paint.SetBrushAndPen( Theme.Primary );
			Paint.DrawRect( LocalRect.Contain( new Vector2( 3f, LocalRect.Height ), TextFlag.LeftCenter ) );

			Paint.SetPen( Theme.Text );
			Paint.DrawIcon( LocalRect with { Left = LocalRect.Left + 16f }, "done", 13f, TextFlag.LeftCenter );
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		IsActive = !IsActive;

		e.Accepted = true;

		_toggled.Invoke( IsActive );
		Update();
	}
}

file sealed class LockButton : Button
{
	public TrackWidget TrackWidget { get; }

	public LockButton( TrackWidget trackWidget )
	{
		TrackWidget = trackWidget;

		FixedSize = 24f;

		ToolTip = "Toggle lock";
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( PaintExtensions.PaintSelectColor( Theme.ControlBackground,
			Theme.ControlBackground.Darken( 0.5f ), Theme.Primary ) );
		Paint.DrawRect( LocalRect, 4f );

		Paint.SetPen( Theme.TextControl );
		Paint.DrawIcon( LocalRect, TrackWidget.View.IsLockedSelf ? "lock" : "lock_open", 12f );
	}

	protected override void OnClicked()
	{
		using var scope = TrackWidget.TrackList.Session.History.Push( $"{(TrackWidget.View.IsLockedSelf ? "Unlocked" : "Locked")} Track" );
		TrackWidget.View.IsLockedSelf = !TrackWidget.View.IsLockedSelf;
	}
}

file sealed class CollapseButton : Button
{
	public TrackWidget Track { get; }

	public CollapseButton( TrackWidget track )
	{
		Track = track;
		FixedSize = 24f;

		ToolTip = "Toggle expanded";
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( PaintExtensions.PaintSelectColor( Theme.ControlBackground,
			Theme.ControlBackground.Darken( 0.5f ), Theme.Primary ) );
		Paint.DrawRect( LocalRect, 4f );

		Paint.SetPen( Theme.TextControl );
		Paint.DrawIcon( LocalRect, Track.View.IsExpanded ? "remove" : "add", 12f );
	}

	protected override void OnClicked()
	{
		Track.View.IsExpanded = !Track.View.IsExpanded;
	}
}

file sealed class ReflectionHelper<T>
	where T : class, IValid
{
	public static ControlWidget CreateControlWidget( IProjectReferenceTrack track, ITrackReference<T> target )
	{
		return ControlWidget.Create( EditorTypeLibrary.CreateProperty( target.Name,
			() => target.Value, value =>
			{
				track.ReferenceId = value switch
				{
					Component cmp => cmp.Id,
					GameObject go => go.Id,
					_ => null
				};

				target.Bind( value );
			} ) );
	}
}
