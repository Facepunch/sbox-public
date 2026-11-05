using Sandbox.Engine;
using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class ControlPseudoClassesTest
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	[TestMethod]
	[DataRow( "disabled" )]
	[DataRow( "enabled" )]
	[DataRow( "checked" )]
	public void ParseAndScore( string pseudo )
	{
		var sheet = StyleParser.ParseSheet( $"button {{ &:{pseudo.ToUpperInvariant()}:hover {{ width: 100px; }} }}" );

		Assert.AreEqual( 1, sheet.Nodes.Count );
		var selector = sheet.Nodes[0].Selectors.Single();
		Assert.AreEqual( 21, selector.SelfScore ); // Element and two pseudo-classes
		Assert.AreEqual( "button:" + pseudo.ToUpperInvariant() + ":hover", selector.AsString );
	}

	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void StateSelectorsDoNotAllocate()
	{
		var root = new Panel();
		var panel = new Panel( root ) { Classes = "disabled checked" };
		panel.Switch( PseudoClass.Focus, true );
		var disabled = StyleParser.ParseSelector( ":disabled" );
		var enabled = StyleParser.ParseSelector( ":enabled" );
		var checkedSelector = StyleParser.ParseSelector( ":checked" );
		var rootSelector = StyleParser.ParseSelector( ":root" );
		var focusWithin = StyleParser.ParseSelector( ":focus-within" );
		var nthChild = StyleParser.ParseSelector( ":nth-child(3n+1)" );
		bool matched = false;
		Allocations.AssertSettles( "Matching control state selectors", () =>
		{
			matched = disabled.Test( panel ) && !enabled.Test( panel ) && checkedSelector.Test( panel )
				&& rootSelector.Test( root ) && !rootSelector.Test( panel )
				&& focusWithin.Test( panel ) && nthChild.Test( panel );
		} );
		Assert.IsTrue( matched );
	}

	[TestMethod]
	public void MatchClassChangesAndNegation()
	{
		var panel = new Panel();
		var enabled = StyleParser.ParseSelector( ":enabled" );
		var disabled = StyleParser.ParseSelector( ":disabled" );
		var checkedSelector = StyleParser.ParseSelector( ":checked" );
		var notDisabled = StyleParser.ParseSelector( ":not(:disabled)" );
		var both = StyleParser.ParseSelector( ":enabled:disabled" );
		var combined = StyleParser.ParseSelector( ":disabled:checked:hover:not(.skip)" );

		// These states come from classes, even if callers set the public flags directly.
		panel.Switch( PseudoClass.Disabled | PseudoClass.Enabled | PseudoClass.Checked, true );
		Assert.IsTrue( enabled.Test( panel ) );
		Assert.IsTrue( notDisabled.Test( panel ) );
		Assert.IsFalse( disabled.Test( panel ) );
		Assert.IsFalse( checkedSelector.Test( panel ) );
		Assert.IsFalse( both.Test( panel ) );

		panel.Classes = "disabled checked";
		Assert.IsTrue( disabled.Test( panel ) );
		Assert.IsTrue( checkedSelector.Test( panel ) );
		Assert.IsFalse( enabled.Test( panel ) );
		Assert.IsFalse( notDisabled.Test( panel ) );
		Assert.IsFalse( both.Test( panel ) );
		Assert.IsFalse( combined.Test( panel ) );

		panel.Switch( PseudoClass.Hover, true );
		Assert.IsTrue( combined.Test( panel ) );
		panel.AddClass( "skip" );
		Assert.IsFalse( combined.Test( panel ) );

		panel.Classes = "";
		Assert.IsTrue( enabled.Test( panel ) );
		Assert.IsFalse( disabled.Test( panel ) );
		Assert.IsFalse( checkedSelector.Test( panel ) );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DisabledPropertyUpdatesStyles( bool textEntry )
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		root.StyleSheet.Parse( ".control { height: 50px; } .control:disabled { width: 200px; } .control:enabled { width: 100px; }" );
		Panel control = textEntry ? new TextEntry() : new Button();
		control.Parent = root;
		control.AddClass( "control" );
		root.Layout();
		Assert.AreEqual( 100, control.Box.Rect.Width );

		if ( control is TextEntry entry ) entry.Disabled = true;
		else ((Button)control).Disabled = true;
		root.Layout();
		Assert.AreEqual( 200, control.Box.Rect.Width );

		if ( control is TextEntry entry2 ) entry2.Disabled = false;
		else ((Button)control).Disabled = false;
		root.Layout();
		Assert.AreEqual( 100, control.Box.Rect.Width );
	}

	[TestMethod]
	public void CheckedPropertyUpdatesStyles()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		root.StyleSheet.Parse( "checkbox { width: 100px; height: 50px; } checkbox:checked { width: 200px; }" );
		var checkbox = new Checkbox { Parent = root };
		root.Layout();
		Assert.AreEqual( 100, checkbox.Box.Rect.Width );

		checkbox.Checked = true;
		root.Layout();
		Assert.AreEqual( 200, checkbox.Box.Rect.Width );

		checkbox.Checked = false;
		root.Layout();
		Assert.AreEqual( 100, checkbox.Box.Rect.Width );
	}

	[TestMethod]
	public void StateChangesInvalidateAncestorAndDescendantSelectors()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		root.StyleSheet.Parse( """
			.holder { width: 100px; height: 50px; }
			.holder:has(.control:disabled) { width: 200px; }
			.leaf { width: 10px; height: 10px; }
			.control:disabled > .leaf { width: 20px; }
			""" );
		var holder = new Panel( root, "holder" );
		var control = new Panel( holder, "control" );
		var leaf = new Panel( control, "leaf" );

		root.Layout();
		Assert.AreEqual( 100, holder.Box.Rect.Width );
		Assert.AreEqual( 10, leaf.Box.Rect.Width );

		control.AddClass( "disabled" );
		root.Layout();
		Assert.AreEqual( 200, holder.Box.Rect.Width );
		Assert.AreEqual( 20, leaf.Box.Rect.Width );

		control.RemoveClass( "disabled" );
		root.Layout();
		Assert.AreEqual( 100, holder.Box.Rect.Width );
		Assert.AreEqual( 10, leaf.Box.Rect.Width );
	}
}
