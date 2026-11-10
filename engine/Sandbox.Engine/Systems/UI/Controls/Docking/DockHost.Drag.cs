using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>A placement selected by a docking guide or tab strip.</summary>
internal readonly record struct DockDropTarget( string RelativeTo, DockPosition Position, int TabIndex = -1, float Fraction = 0.5f );

public partial class DockHost
{
	string _dragId;
	DockDropTarget? _drop;
	Panel _targets;
	Panel _emptyTarget;
	readonly Dictionary<DockGroup, Panel> _sections = new();
	readonly Dictionary<DockPosition, Panel> _groupGuides = new();
	readonly Dictionary<DockPosition, Panel> _rootGuides = new();

	internal Rect GetDockBounds( string id ) => _layout.FindGroup( id ) is { } group && _views.TryGetValue( group, out var view ) ? view.Box.Rect : default;

	void BeginDrag( string id )
	{
		CancelDrag();
		if ( !IsOpen( id ) ) return;
		_dragId = id;
		_tabs[id].AddClass( "dragging" );
		UpdateDockTargets( null, id );
	}

	/// <summary>Cancels a pending in-surface tab drag without changing the layout.</summary>
	internal void CancelDrag()
	{
		if ( _dragId is not null && _tabs.TryGetValue( _dragId, out var tab ) && tab.IsValid ) tab.RemoveClass( "dragging" );
		_dragId = null;
		_drop = null;
		HideDockTargets();
	}

	internal void HideDockTargets()
	{
		if ( _targets is { IsValid: true } ) _targets.Style.Display = DisplayMode.None;
		if ( _preview.IsValid ) _preview.Style.Display = DisplayMode.None;
	}

	void CreateTargets()
	{
		if ( _targets is not null ) return;
		_targets = Add.Panel( "dock-targets" );
		_emptyTarget = _targets.Add.Panel( "dock-section" );
		_preview.Parent = _targets;
		foreach ( var position in Enum.GetValues<DockPosition>() )
		{
			_groupGuides.Add( position, CreateGuide( position, false ) );
			if ( position != DockPosition.Center ) _rootGuides.Add( position, CreateGuide( position, true ) );
		}
	}

	Panel CreateGuide( DockPosition position, bool root )
	{
		var guide = _targets.Add.Panel( $"dock-guide {(root ? "dock-root-guide" : "dock-group-guide")}-{position.ToString().ToLowerInvariant()}" );
		var frame = guide.Add.Panel( "dock-guide-frame" );
		frame.Add.Panel( $"dock-guide-fill {position.ToString().ToLowerInvariant()}" );
		if ( position == DockPosition.Center ) frame.Add.Icon( "tab", "dock-guide-icon" );
		return guide;
	}

	internal DockDropTarget? UpdateDockTargets( Vector2? point, string draggedId )
	{
		if ( !IsValid || IsDeleting || !IsVisible )
		{
			HideDockTargets();
			return null;
		}
		CreateTargets();
		_targets.Style.Display = DisplayMode.Flex;
		_preview.Style.Display = DisplayMode.None;
		foreach ( var guide in _groupGuides.Values.Concat( _rootGuides.Values ) )
		{
			guide.Style.Display = DisplayMode.None;
			guide.RemoveClass( "hovered" );
		}

		var bounds = _workspace.Box.Rect;
		if ( point.HasValue && (!Contains( bounds, point.Value ) || !IsInsideVisibleContent( point.Value )) ) point = null;
		DockGroup hovered = null;
		foreach ( var (node, view) in _views )
		{
			if ( node is not DockGroup group ) continue;
			if ( !_sections.TryGetValue( group, out var section ) )
				_sections.Add( group, section = _targets.Add.Panel( "dock-section" ) );
			var eligible = group.Tabs.Count > 1 || group.ActiveId != draggedId;
			PositionOverlay( section, view.Box.Rect );
			section.Style.Display = eligible ? DisplayMode.Flex : DisplayMode.None;
			var inside = point.HasValue && Contains( view.Box.Rect, point.Value );
			section.SetClass( "hovered", inside );
			if ( inside ) hovered = group;
		}
		foreach ( var group in _sections.Keys.Where( x => !_views.ContainsKey( x ) ).ToArray() )
		{
			_sections[group].Delete( true );
			_sections.Remove( group );
		}
		_emptyTarget.Style.Display = _layout.Root is null ? DisplayMode.Flex : DisplayMode.None;
		PositionOverlay( _emptyTarget, bounds );

		DockDropTarget? result = null;
		Rect preview = default;
		var size = 30 / ScaleFromScreen;
		var pitch = size + 5 / ScaleFromScreen;
		if ( hovered is not null || (_layout.Root is null && point.HasValue) )
		{
			var region = hovered is null ? bounds : _views[hovered].Box.Rect;
			var guideSize = MathF.Min( size, MathF.Min( region.Width, region.Height ) / 3.5f );
			var guidePitch = guideSize + 4 / ScaleFromScreen;
			foreach ( var (position, guide) in _groupGuides )
			{
				if ( hovered is null && position != DockPosition.Center ) continue;
				if ( !CanDrop( hovered, draggedId ) ) continue;
				var center = region.Center + Direction( position ) * guidePitch;
				var rect = new Rect( center - guideSize * 0.5f, new Vector2( guideSize ) );
				PositionOverlay( guide, rect );
				guide.Style.Display = DisplayMode.Flex;
				if ( !point.HasValue || !Contains( rect, point.Value ) ) continue;
				guide.AddClass( "hovered" );
				result = new DockDropTarget( hovered?.ActiveId, position );
				preview = PreviewBounds( region, position );
			}
		}

		// Whole-workspace targets are distinct from the hovered group's targets.
		if ( _layout.Root is not null && CanDrop( _layout.Root as DockGroup, draggedId ) )
		{
			foreach ( var (position, guide) in _rootGuides )
			{
				var direction = Direction( position );
				var center = bounds.Center + direction * (bounds.Size * 0.5f - new Vector2( pitch ));
				var rect = new Rect( center - size * 0.5f, new Vector2( size ) );
				PositionOverlay( guide, rect );
				guide.Style.Display = DisplayMode.Flex;
				if ( !point.HasValue || !Contains( rect, point.Value ) ) continue;
				foreach ( var groupGuide in _groupGuides.Values ) groupGuide.RemoveClass( "hovered" );
				guide.AddClass( "hovered" );
				result = new DockDropTarget( null, position );
				preview = PreviewBounds( bounds, position );
			}
		}

		// Tab strips remain direct insertion targets so tab ordering does not require a guide.
		if ( result is null && hovered is not null && point.HasValue && CanDrop( hovered, draggedId ) )
		{
			var tabs = ((GroupView)_views[hovered]).Tabs.Box.Rect;
			if ( Contains( tabs, point.Value ) )
			{
				int index = 0;
				var insertion = tabs.Left;
				foreach ( var id in hovered.Tabs )
				{
					if ( id == draggedId ) continue;
					var tab = _tabs[id].Box.Rect;
					if ( point.Value.x < tab.Center.x ) break;
					insertion = tab.Right;
					index++;
				}
				result = new DockDropTarget( hovered.ActiveId, DockPosition.Center, index );
				var markerWidth = MathF.Min( 3 / ScaleFromScreen, tabs.Width );
				preview = new Rect( Math.Clamp( insertion, tabs.Left, tabs.Right - markerWidth ), tabs.Top, markerWidth, tabs.Height );
			}
		}

		if ( result.HasValue )
		{
			PositionOverlay( _preview, preview );
			_preview.Style.Display = DisplayMode.Flex;
		}
		return result;
	}

	static bool CanDrop( DockGroup group, string draggedId )
	{
		return group is null || group.Tabs.Count > 1 || group.ActiveId != draggedId;
	}

	bool IsInsideVisibleContent( Vector2 point )
	{
		foreach ( var panel in AncestorsAndSelf )
		{
			var clip = panel.ContentClipRect;
			if ( panel is RootPanel && !Contains( panel.Box.Rect, point ) ) return false;
			if ( panel.ComputedStyle is not { } style ) return false;
			if ( (style.OverflowX ?? OverflowMode.Visible) != OverflowMode.Visible && (point.x < clip.Left || point.x >= clip.Right) ) return false;
			if ( (style.OverflowY ?? OverflowMode.Visible) != OverflowMode.Visible && (point.y < clip.Top || point.y >= clip.Bottom) ) return false;
		}
		return true;
	}

	static Vector2 Direction( DockPosition position ) => position switch
	{
		DockPosition.Left => new Vector2( -1, 0 ),
		DockPosition.Right => new Vector2( 1, 0 ),
		DockPosition.Top => new Vector2( 0, -1 ),
		DockPosition.Bottom => new Vector2( 0, 1 ),
		_ => Vector2.Zero
	};

	static bool Contains( Rect rect, Vector2 point ) => point.x >= rect.Left && point.y >= rect.Top && point.x < rect.Right && point.y < rect.Bottom;

	static Rect PreviewBounds( Rect bounds, DockPosition position )
	{
		var size = bounds.Size;
		var origin = bounds.Position;
		if ( position is DockPosition.Left or DockPosition.Right ) size.x *= 0.5f;
		if ( position is DockPosition.Top or DockPosition.Bottom ) size.y *= 0.5f;
		if ( position == DockPosition.Right ) origin.x += size.x;
		if ( position == DockPosition.Bottom ) origin.y += size.y;
		return new Rect( origin, size );
	}

	void PositionOverlay( Panel panel, Rect rect )
	{
		var local = (rect.Position - Box.Rect.Position) * ScaleFromScreen;
		panel.Style.Left = local.x;
		panel.Style.Top = local.y;
		panel.Style.Width = rect.Width * ScaleFromScreen;
		panel.Style.Height = rect.Height * ScaleFromScreen;
	}

	void UpdateDrag( Vector2 point )
	{
		if ( _dragId is not null ) _drop = UpdateDockTargets( point, _dragId );
	}

	void EndDrag( Vector2 point )
	{
		if ( _dragId is null ) return;
		UpdateDrag( point );
		var id = _dragId;
		var drop = _drop;
		CancelDrag();
		if ( drop is { } target ) Dock( id, target.RelativeTo, target.Position, target.Fraction, target.TabIndex );
	}
}
