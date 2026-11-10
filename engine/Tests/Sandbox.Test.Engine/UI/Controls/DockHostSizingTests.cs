using Sandbox.UI;
using System.Linq;

namespace UITests.Controls;

/// <summary>
/// Content sizing constraints in docked layouts.
/// </summary>
public partial class DockHostTests
{
	/// <summary>
	/// Splitter drags pass through a fixed pane regardless of split nesting or orientation.
	/// </summary>
	[TestMethod]
	[DataRow( false, false, false )]
	[DataRow( false, true, false )]
	[DataRow( true, false, false )]
	[DataRow( true, true, false )]
	[DataRow( false, false, true )]
	[DataRow( false, true, true )]
	[DataRow( true, false, true )]
	[DataRow( true, true, true )]
	public void DragPassesThroughFixedPane( bool vertical, bool nestRight, bool lastDivider )
	{
		surface.Size = new Vector2( 1200, 1200 );
		surface.Root.PanelBounds = new Rect( 0, 0, 1200, 1200 );
		var main = new Panel();
		var fixedPane = new Panel();
		var chat = new Panel();
		if ( vertical )
		{
			main.Style.MinHeight = 200;
			fixedPane.Style.MinHeight = fixedPane.Style.MaxHeight = 200;
			chat.Style.MinHeight = 500;
		}
		else
		{
			main.Style.MinWidth = 200;
			fixedPane.Style.MinWidth = fixedPane.Style.MaxWidth = 200;
			chat.Style.MinWidth = 500;
		}
		host.Register( "main", "Main", main );
		host.Register( "fixed", "Fixed", fixedPane );
		host.Register( "chat", "Chat", chat );
		host.Dock( "main" );
		var position = vertical ? DockPosition.Bottom : DockPosition.Right;
		if ( nestRight )
		{
			host.Dock( "fixed", "main", position );
			host.Dock( "chat", "fixed", position );
		}
		else
		{
			host.Dock( "chat", "main", position );
			host.Dock( "fixed", "main", position );
		}
		var dividers = host.Descendants.Where( x => x.HasClass( "dock-splitter" ) ).ToArray();
		foreach ( var divider in dividers )
		{
			if ( vertical )
			{
				divider.Style.Height = 16;
			}
			else
			{
				divider.Style.Width = 16;
			}
		}

		Frame();
		Frame();
		float Axis( Vector2 value ) => vertical ? value.y : value.x;
		var orderedDividers = dividers.OrderBy( x => Axis( x.Box.Rect.Position ) ).ToArray();
		var handle = lastDivider ? orderedDividers.Last() : orderedDividers.First();
		var start = handle.Box.Rect.Center;
		var mainSize = Axis( main.Parent.Box.Rect.Size );
		var chatSize = Axis( chat.Parent.Box.Rect.Size );
		var fixedSize = Axis( fixedPane.Parent.Box.Rect.Size );
		var state = host.State;
		MoveTo( start );
		MouseButton( true );
		MoveTo( start - (vertical ? Vector2.Down : Vector2.Right) * 100 );
		Frame();
		Assert.AreEqual( mainSize - 100, Axis( main.Parent.Box.Rect.Size ), 1.0f );
		Assert.AreEqual( chatSize + 100, Axis( chat.Parent.Box.Rect.Size ), 1.0f );
		Assert.AreEqual( fixedSize, Axis( fixedPane.Parent.Box.Rect.Size ), 1.0f );
		MoveTo( start + (vertical ? Vector2.Down : Vector2.Right) * 150 );
		Frame();
		Assert.IsTrue( Axis( chat.Parent.Box.Rect.Size ) >= 499 );
		Assert.AreEqual( fixedSize, Axis( fixedPane.Parent.Box.Rect.Size ), 1.0f );
		surface.SetKey( "escape", true );
		Frame();
		surface.SetKey( "escape", false );
		Frame();
		Assert.AreEqual( state, host.State );
		Assert.AreEqual( mainSize, Axis( main.Parent.Box.Rect.Size ), 1.0f );
		MouseButton( false );
	}

	/// <summary>
	/// Fixed-width content keeps its allocation as the workspace grows and shrinks.
	/// </summary>
	[TestMethod]
	[DataRow( 1.0f )]
	[DataRow( 0.8f )]
	[DataRow( 1.5f )]
	public void ContentWidthLimitsSurviveWorkspaceResize( float scale )
	{
		surface.DpiScale = scale;
		var main = new Panel();
		main.Style.MinWidth = 500;
		var sidebar = new Panel();
		sidebar.Style.MinWidth = 200;
		sidebar.Style.MaxWidth = 200;
		host.Register( "main", "Main", main );
		host.Register( "sidebar", "Sidebar", sidebar );
		host.Dock( "main" );
		host.Dock( "sidebar", "main", DockPosition.Right );

		foreach ( var width in new[] { 800, 1200, 720 } )
		{
			surface.Size = new Vector2( width, 600 ) * scale;
			surface.Root.PreLayout( new Rect( 0, 0, width * scale, 600 * scale ) );
			Frame();
			Frame();
			Assert.AreEqual( 200, sidebar.Parent.Box.Rect.Width / scale, 1.0f );
			Assert.IsTrue( main.Parent.Box.Rect.Width / scale >= 499 );
		}
	}

	/// <summary>
	/// Nested splits reserve space for constrained descendants and styled dividers.
	/// </summary>
	[TestMethod]
	public void NestedContentLimitsIncludeStyledDividers()
	{
		surface.Size = new Vector2( 1200, 600 );
		surface.Root.PanelBounds = new Rect( 0, 0, 1200, 600 );
		var main = new Panel();
		var sidebar = new Panel();
		sidebar.Style.MinWidth = 200;
		sidebar.Style.MaxWidth = 200;
		var chat = new Panel();
		chat.Style.MinWidth = 500;
		host.Register( "main", "Main", main );
		host.Register( "sidebar", "Sidebar", sidebar );
		host.Register( "chat", "Chat", chat );
		host.Dock( "main" );
		host.Dock( "chat", position: DockPosition.Right, fraction: 0.1f );
		host.Dock( "sidebar", "main", DockPosition.Right );
		foreach ( var divider in host.Descendants.Where( x => x.HasClass( "dock-splitter" ) ) ) divider.Style.Width = 16;
		Frame();
		Frame();
		Assert.AreEqual( 200, sidebar.Parent.Box.Rect.Width, 1.0f );
		Assert.AreEqual( 500, chat.Parent.Box.Rect.Width, 1.0f );
		Assert.AreEqual( 468, main.Parent.Box.Rect.Width, 1.0f );
	}

	/// <summary>
	/// An undersized workspace shares its space without overflowing the dock branches.
	/// </summary>
	[TestMethod]
	public void ConflictingMinimumsShareAvailableSpace()
	{
		surface.Root.PanelBounds = new Rect( 0, 0, 800, 600 );
		foreach ( var id in new[] { "a", "b" } )
		{
			var panel = new Panel();
			panel.Style.MinWidth = 500;
			host.Register( id, id, panel );
		}
		host.Dock( "a" );
		host.Dock( "b", "a", DockPosition.Right );
		Frame();
		Frame();
		foreach ( var branch in host.Descendants.Where( x => x.HasClass( "dock-branch" ) ) )
		{
			Assert.AreEqual( 397.5f, branch.Box.Rect.Width, 1.0f );
		}
	}

	/// <summary>
	/// Grouped tabs reserve their largest minimum without inheriting a sibling's narrow maximum.
	/// </summary>
	[TestMethod]
	public void GroupedTabConstraintsDoNotJumpOnSelection()
	{
		surface.Root.PanelBounds = new Rect( 0, 0, 800, 600 );
		var wide = new Panel();
		wide.Style.MinWidth = 500;
		var narrow = new Panel();
		narrow.Style.MinWidth = 200;
		narrow.Style.MaxWidth = 200;
		host.Register( "wide", "Wide", wide );
		host.Register( "narrow", "Narrow", narrow );
		host.Register( "other", "Other", new Panel() );
		host.Dock( "wide" );
		host.Dock( "narrow", "wide" );
		host.Dock( "other", "wide", DockPosition.Right, 0.8f );
		host.Activate( "wide" );
		Frame();
		Frame();
		var width = wide.Parent.Box.Rect.Width;
		Assert.IsTrue( width >= 499 );
		host.Activate( "narrow" );
		Frame();
		Assert.AreEqual( width, narrow.Parent.Box.Rect.Width, 1.0f );
	}
}
