using Microsoft.AspNetCore.Components;
using Sandbox.Internal;
using System.Threading;

namespace Sandbox.UI;

/// <summary>
/// A simple User Interface panel. Can be styled with <a href="https://en.wikipedia.org/wiki/CSS">CSS</a>.
/// </summary>
[Library( "panel" ), Alias( "div", "span" ), Expose]
[Title( "Panel" ), Icon( "view_quilt" )]
public partial class Panel : IPanel, IValid, IComponent
{
	/// <summary>
	/// The element name. If you've created this Panel via a template this will be whatever the element
	/// name is on there. If not then it'll be the name of the class (ie Panel, Button)
	/// </summary>
	[Property]
	public string ElementName { get; set; }

	/// <summary>
	/// Works the same as the html id="" attribute. If you set Id to "poop", it'll match any styles
	/// that define #poop in their selector.
	/// </summary>
	public string Id
	{
		get;
		set;
	}

	/// <summary>
	/// If this was created by razor, this is the file in which it was created
	/// </summary>
	[Hide]
	public string SourceFile { get; set; }

	/// <summary>
	/// If this was created by razor, this is the line number in the file
	/// </summary>
	[Hide]
	public int SourceLine { get; set; }

	/// <summary>
	/// Quick access to timing events, for async/await.
	/// </summary>
	public TaskSource Task = new( 1 );

	/// <summary>
	/// A collection of stylesheets applied to this panel directly.
	/// </summary>
	public StyleSheetCollection StyleSheet;


	//
	// Start with the intro flag on
	//
	PseudoClass _pseudoClass = PseudoClass.Intro;

	/// <summary>
	/// Special flags used by the styling system for hover, active etc..
	/// </summary>
	[Property]
	public PseudoClass PseudoClass
	{
		get => _pseudoClass;
		set
		{
			if ( _pseudoClass == value )
				return;

			_pseudoClass = value;

			StyleSelectorsChanged( true, true );
		}
	}

	/// <summary>
	/// Whether this panel has the <c>:focus</c> pseudo class active.
	/// </summary>
	[Hide]
	public bool HasFocus => (PseudoClass & PseudoClass.Focus) != 0;

	/// <summary>
	/// Whether this panel has the <c>:active</c> pseudo class active.
	/// </summary>
	[Hide]
	public bool HasActive => (PseudoClass & PseudoClass.Active) != 0;

	/// <summary>
	/// Whether this panel has the <c>:hover</c> pseudo class active.
	/// </summary>
	[Hide]
	public bool HasHovered => (PseudoClass & PseudoClass.Hover) != 0;

	/// <summary>
	/// Whether this panel has the <c>:intro</c> pseudo class active.
	/// </summary>
	[Hide]
	public bool HasIntro => (PseudoClass & PseudoClass.Intro) != 0;

	/// <summary>
	/// Whether this panel has the <c>:outro</c> pseudo class active.
	/// </summary>
	[Hide]
	public bool HasOutro => (PseudoClass & PseudoClass.Outro) != 0;


	public Panel()
	{
		InitializeEvents();

		YogaNode = new YogaWrapper( this );
		Style = new PanelStyle( this );
		StyleSheet = new StyleSheetCollection( this );
		Transitions = new Transitions( this );

		ElementName = GetType().Name.ToLower();
		Switch( PseudoClass.Empty, true );

		LoadStyleSheet();
	}

	public Panel( Panel parent ) : this()
	{
		if ( parent != null )
			Parent = parent;
	}

	public Panel( Panel parent, string classnames ) : this( parent )
	{
		if ( classnames != null )
			AddClass( classnames );
	}

	internal virtual void AddToLists()
	{
		Sandbox.Event.Register( this );
	}

	internal virtual void RemoveFromLists()
	{
		Sandbox.Event.Unregister( this );

		PanelLayer?.Dispose();
		PanelLayer = null;
	}

	/// <summary>
	/// Called when a hotload happened. (Not necessarily on this panel)
	/// </summary>
	public virtual void OnHotloaded()
	{
		LoadStyleSheet();
		InitializeEvents();

		// If the checksum changed on our render tree, then we have to assume that everthing
		// about it changed. Lets destroy it and start from scratch.
		if ( razorLastTreeChecksum != GetRenderTreeChecksum() )
		{
			razorLastTreeChecksum = GetRenderTreeChecksum();
			if ( renderTree != null )
			{
				renderTree?.Clear();
				razorTreeDirty = true;
			}
		}

		//
		// Some of our children may have stopped existing. Like if they deleted the 
		// type - then we'll have nulls here. Lets prune them out.
		//
		_children?.RemoveAll( x => x is null );
		_renderChildren?.RemoveAll( x => x is null );

		if ( _childrenHash is not null && _childrenHash.Any( x => x == null ) )
			_childrenHash = _childrenHash.Where( x => x != null ).ToHashSet();

		foreach ( var child in Children )
		{
			try
			{
				child.OnHotloaded();
			}
			catch ( System.Exception e )
			{
				Log.Error( e );
			}
		}
	}

	/// <summary>
	/// List of all <see cref="UI.StyleSheet"/>s applied to this panel and all its <see cref="AncestorsAndSelf">ancestors</see>.
	/// </summary>
	[Hide]
	public IEnumerable<StyleSheet> AllStyleSheets
	{
		get
		{
			foreach ( var p in AncestorsAndSelf )
			{
				if ( p.StyleSheet.List == null ) continue;

				foreach ( var sheet in p.StyleSheet.List )
					yield return sheet;
			}
		}
	}

	/// <summary>
	/// Switch a pseudo class on or off.
	/// </summary>
	public bool Switch( PseudoClass c, bool state )
	{
		if ( state == ((PseudoClass & c) != 0) ) return false;

		if ( state )
		{
			PseudoClass |= c;
		}
		else
		{
			PseudoClass &= ~c;
		}

		return true;
	}

	internal static void Switch( PseudoClass c, bool state, Panel panel, Panel unlessAncestorOf = null )
	{
		if ( panel == null )
			return;

		foreach ( var target in panel.AncestorsAndSelf )
		{
			if ( unlessAncestorOf != null && unlessAncestorOf.IsAncestor( target ) )
				continue;

			target.Switch( c, state );
		}
	}

	/// <summary>
	/// Return true if this panel isn't hidden by opacity or displaymode.
	/// </summary>
	[Hide]
	public bool IsVisible { get; internal set; } = true;

	/// <summary>
	/// Return true if this panel isn't hidden by opacity or displaymode.
	/// </summary>
	[Hide]
	public bool IsVisibleSelf { get; internal set; } = true;

	/// <summary>
	/// Called every frame. This is your "Think" function.
	/// </summary>
	public virtual void Tick()
	{

	}

	/// <summary>
	/// Called after the parent of this panel has changed.
	/// </summary>
	public virtual void OnParentChanged()
	{

	}

	/// <summary>
	/// Returns true if this panel would like the mouse cursor to be visible.
	/// </summary>
	public virtual bool WantsMouseInput()
	{
		if ( ComputedStyle == null )
			return false;

		if ( !IsVisibleSelf )
			return false;

		if ( ComputedStyle.PointerEvents == PointerEvents.All )
			return true;

		if ( _children is null )
			return false;

		foreach ( var child in _children )
		{
			if ( child?.WantsMouseInput() ?? false )
				return true;
		}

		return false;
	}

	internal void TickInternal()
	{
		if ( Application.IsHeadless )
			return;

		if ( IsDeleting )
		{
			// we're probably transitioning out
			// so make sure we keep updating the layout
			SetNeedsPreLayout();
			return;
		}

		try
		{
			if ( ParentHasChanged )
			{
				ParentHasChanged = false;
				OnParentChanged();
				StyleSelectorsChanged( true, true );
			}

			var didBuildRenderTree = false;
			var isFirstRender = renderTree == null;

			if ( HasRenderTree || templateBindsChanged )
			{
				InternalTreeBinds();

				if ( templateBindsChanged )
				{
					templateBindsChanged = false;
					razorTreeDirty = true;
					ParametersChanged( true );
				}

				if ( razorTreeDirty )
				{
					InternalRenderTree();
					didBuildRenderTree = true;
				}
			}

			// keep before and after updated
			UpdateBeforeAfterElements();

			//
			// If our style is dirty, or we're animating/transitioning/scrolling then make sure we get layed out
			//
			if ( Style is not null && (Style.IsDirty || HasActiveTransitions || (ComputedStyle?.HasAnimation ?? false) || ScrollVelocity != 0 || isScrolling || IsDragScrolling) )
			{
				SetNeedsPreLayout();
			}

			//
			// Don't tick our children if we're not visible
			//
			if ( IsVisible && _children is not null && _children.Count > 0 )
			{
				for ( int i = _children.Count - 1; _children != null && i >= 0; i-- )
				{
					_children[i]?.TickInternal();
				}
			}

			//
			// Defer OnAfterTreeRender so that children are all processed too
			//
			if ( didBuildRenderTree )
			{
				OnAfterTreeRender( isFirstRender );
			}

			RunPendingEvents();
			Tick();
			RunPendingEvents();

			AddScrollVelocity();
			RunClassBinds();


		}
		catch ( System.Exception e )
		{
			Log.Error( e );
		}

	}

	internal int GetRenderOrderIndex()
	{
		return (SiblingIndex) + (ComputedStyle?.ZIndex ?? 0);
	}

	/// <summary>
	/// Convert a point from the screen to a point representing a delta on this panel where
	/// the top left is [0,0] and the bottom right is [1,1]
	/// </summary>
	public Vector2 ScreenPositionToPanelDelta( Vector2 pos )
	{
		pos = ScreenPositionToPanelPosition( pos );

		var x = pos.x.LerpInverse( 0, Box.Rect.Width, false );
		var y = pos.y.LerpInverse( 0, Box.Rect.Height, false );

		return new Vector2( x, y );
	}

	/// <summary>
	/// Convert a point from the screen to a position relative to the top left of this panel
	/// </summary>
	public Vector2 ScreenPositionToPanelPosition( Vector2 pos )
	{
		if ( GlobalMatrix.HasValue )
		{
			pos = GlobalMatrix.Value.Transform( pos );
		}

		var x = pos.x - Box.Rect.Left;
		var y = pos.y - Box.Rect.Top;

		return new Vector2( x, y );
	}

	/// <summary>
	/// Convert a point from local space to screen space
	/// </summary>
	public Vector2 PanelPositionToScreenPosition( Vector2 pos )
	{
		var screenPos = new Vector2( pos.x + Box.Rect.Left, pos.y + Box.Rect.Top );

		if ( GlobalMatrix.HasValue )
		{
			screenPos = GlobalMatrix.Value.Inverted.Transform( screenPos );
		}

		return screenPos;
	}

	/// <summary>
	/// Find and return any children of this panel (including self) within the given rect.
	/// </summary>
	/// <param name="box">The area to look for panels in, in screen-space coordinates.</param>
	/// <param name="fullyInside">Whether we want only the panels that are completely within the given bounds.</param>
	public IEnumerable<Panel> FindInRect( Rect box, bool fullyInside )
	{
		if ( !IsVisible )
			yield break;

		if ( !IsInside( box, fullyInside ) )
			yield break;

		yield return this;

		if ( !HasChildren )
			yield break;

		foreach ( var child in Children )
		{
			foreach ( var found in child.FindInRect( box, fullyInside ) )
			{
				yield return found;
			}
		}
	}

	/// <summary>
	/// Allow selecting child text
	/// </summary>
	[Category( "Selection" )]
	public bool AllowChildSelection { get; set; }

	[Hide]
	public bool IsValid => YogaNode is not null;

	string CollectSelectedChildrenText( Panel p )
	{
		if ( !p.IsVisible )
			return null;

		if ( p is Sandbox.UI.Label label )
		{
			return label.GetSelectedText();
		}

		string selection = null;

		var lines = p.ComputedStyle.FlexDirection == FlexDirection.Column;

		foreach ( var child in p.Children )
		{
			var sel = CollectSelectedChildrenText( child );
			if ( string.IsNullOrEmpty( sel ) ) continue;

			if ( selection == null ) selection = sel;
			else selection = $"{selection}{(lines ? "\n" : " ")}{sel}";
		}

		return selection;
	}

	/// <summary>
	/// Called when the player moves the mouse after "press and holding" (or dragging) the panel.
	/// </summary>
	protected virtual void OnDragSelect( SelectionEvent e )
	{
		if ( AllowChildSelection )
		{
			e.StopPropagation();

			foreach ( var child in Children )
			{
				UpdateSelection( child, e );
			}
		}
	}

	/// <summary>
	/// If AllowChildSelection is enabled, we'll try to select all children text
	/// </summary>
	public void SelectAllInChildren()
	{
		if ( this is Sandbox.UI.Label label )
		{
			label.ShouldDrawSelection = true;
			label.SelectionStart = 0;
			label.SelectionEnd = int.MaxValue;
			return;
		}

		foreach ( var child in Children )
		{
			child.SelectAllInChildren();
		}
	}

	/// <summary>
	/// Clear any selection in children
	/// </summary>
	public void UnselectAllInChildren()
	{
		if ( this is Sandbox.UI.Label label )
		{
			label.ShouldDrawSelection = false;
			return;
		}

		foreach ( var child in Children )
		{
			child.UnselectAllInChildren();
		}
	}

	void UpdateSelection( Panel p, SelectionEvent e )
	{
		var rect = e.SelectionRect;

		// child is outside of selection vertically
		if ( p.Box.Rect.Bottom < rect.Top || p.Box.Rect.Top > rect.Bottom )
		{
			p.UnselectAllInChildren();
			return;
		}

		// Selectable
		if ( p is Sandbox.UI.Label label )
		{
			label.ShouldDrawSelection = true;

			if ( e.StartPoint.y > e.EndPoint.y )
			{
				(e.EndPoint, e.StartPoint) = (e.StartPoint, e.EndPoint);
			}

			var start = p.Box.Rect.Top < rect.Top;
			var end = p.Box.Rect.Bottom > e.EndPoint.y;
			var negwidth = (e.EndPoint - e.StartPoint).x < 0;

			// selectAll
			if ( start && end )
			{
				label.SelectionStart = label.GetLetterAtScreenPosition( new Vector2( rect.Left, rect.Top ) );
				label.SelectionEnd = label.GetLetterAtScreenPosition( new Vector2( rect.Right, rect.Bottom ) );
			}
			else if ( start )
			{
				var from = negwidth ? rect.Right : rect.Left;
				label.SelectionStart = label.GetLetterAtScreenPosition( new Vector2( from, rect.Top ) );
				label.SelectionEnd = int.MaxValue;
			}
			else if ( end )
			{
				var to = negwidth ? rect.Left : rect.Right;
				label.SelectionStart = 0;
				label.SelectionEnd = label.GetLetterAtScreenPosition( new Vector2( to, rect.Bottom ) );
			}
			else
			{
				label.SelectionStart = 0;
				label.SelectionEnd = int.MaxValue;
			}

			return;
		}

		foreach ( var child in p.Children )
		{
			UpdateSelection( child, e );
		}
	}

	/// <summary>
	/// Called when the current language has changed. This allows you to rebuild
	/// anything that might need rebuilding. Tokenized text labels should automatically update.
	/// </summary>
	public virtual void LanguageChanged()
	{
		foreach ( var child in Children )
		{
			child.LanguageChanged();
		}
	}

	/// <summary>
	/// Invoke a method after a delay. If the panel is deleted before this delay the method will not be called.
	/// </summary>
	public async void Invoke( float seconds, Action action )
	{
		await Task.DelaySeconds( seconds );
		if ( !this.IsValid() ) return;

		action.InvokeWithWarning();
	}

	Dictionary<string, CancellationTokenSource> invokes = new();

	/// <summary>
	/// Invoke a method after a delay. If the panel is deleted before this delay the method will not be called. If the invoke is called
	/// while the old one is waiting, the old one will be cancelled.
	/// </summary>
	public async void InvokeOnce( string name, float seconds, Action action )
	{
		CancelInvoke( name );

		var tokenSource = new CancellationTokenSource();
		invokes[name] = tokenSource;

		await Task.DelaySeconds( seconds );
		if ( !this.IsValid() ) return;

		if ( tokenSource.IsCancellationRequested )
			return;

		invokes.Remove( name );
		action.InvokeWithWarning();
	}

	/// <summary>
	/// Cancel a named invocation
	/// </summary>
	public void CancelInvoke( string name )
	{
		if ( invokes.Remove( name, out var cts ) )
		{
			cts.Cancel();
			cts.Dispose();
		}
	}

}
