using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace UITests;

/// <summary>Managed docking tree and persistence tests.</summary>
[TestClass]
public class DockLayoutTest
{
	/// <summary>Empty layouts support docking, closing and reopening.</summary>
	[TestMethod]
	public void EmptyCloseAndReopen()
	{
		var layout = new DockLayout();
		Assert.IsNull( layout.Root );
		Assert.IsNull( layout.FindGroup( null ) );
		Assert.IsFalse( layout.Close( "a" ) );
		Assert.IsFalse( layout.Activate( "a" ) );
		layout.Dock( "a" );
		Assert.AreEqual( "a", layout.FindGroup( "a" ).ActiveId );
		Assert.IsTrue( layout.Close( "a" ) );
		Assert.IsNull( layout.Root );
		layout.Dock( "a", position: DockPosition.Bottom );
		Assert.IsInstanceOfType<DockGroup>( layout.Root );
		Assert.AreEqual( "a", layout.FindGroup( "a" ).ActiveId );
	}

	/// <summary>Nested moves collapse empty branches without losing siblings.</summary>
	[TestMethod]
	public void NestedMoveAndCollapse()
	{
		var layout = new DockLayout();
		layout.Dock( "a" );
		layout.Dock( "b", "a", DockPosition.Right );
		layout.Dock( "c", "b", DockPosition.Bottom );
		layout.Dock( "d", "c", DockPosition.Left );
		var root = (DockSplit)layout.Root;
		var a = layout.FindGroup( "a" );
		var d = layout.FindGroup( "d" );
		layout.Dock( "c", "a" );
		Assert.AreSame( root, layout.Root );
		Assert.AreSame( d, ((DockSplit)root.Second).Second );
		CollectionAssert.AreEqual( new[] { "a", "c" }, a.Tabs.ToArray() );
		layout.Dock( "b", "d" );
		Assert.AreSame( d, root.Second );
		layout.Dock( "d", "a" );
		Assert.AreEqual( "b", layout.FindGroup( "b" ).ActiveId );
		layout.Dock( "b", "a" );
		Assert.AreSame( a, layout.Root );
		CollectionAssert.AreEqual( new[] { "a", "c", "d", "b" }, a.Tabs.ToArray() );
		AssertTree( layout.Root );
	}

	/// <summary>Null targets select the first group or split the whole remaining tree.</summary>
	[TestMethod]
	public void NullTargetsAndMovingOutOfCollapsedRoot()
	{
		var layout = new DockLayout();
		layout.Dock( "a" );
		layout.Dock( "b", "a", DockPosition.Right );
		layout.Dock( "c" );
		Assert.AreSame( layout.FindGroup( "a" ), layout.FindGroup( "c" ) );
		var original = layout.Root;
		layout.Dock( "d", position: DockPosition.Top, fraction: 0.3f );
		Assert.AreSame( original, ((DockSplit)layout.Root).Second );
		layout.Dock( "d", position: DockPosition.Bottom, fraction: 0.2f );
		var root = (DockSplit)layout.Root;
		Assert.AreSame( original, root.First );
		Assert.AreSame( layout.FindGroup( "d" ), root.Second );
		Assert.AreEqual( 0.8f, root.Fraction );
		AssertTree( layout.Root );
	}

	/// <summary>Indices are measured after removal, and implicit self-docking only activates.</summary>
	[TestMethod]
	public void ReorderAndSelection()
	{
		var layout = new DockLayout();
		foreach ( var id in new[] { "a", "b", "c", "d" } ) layout.Dock( id );
		var group = layout.FindGroup( "a" );
		layout.Dock( "a", "c", tabIndex: 2 );
		CollectionAssert.AreEqual( new[] { "b", "c", "a", "d" }, group.Tabs.ToArray() );
		layout.Dock( "a", "a" );
		CollectionAssert.AreEqual( new[] { "b", "c", "a", "d" }, group.Tabs.ToArray() );
		layout.Dock( "d", tabIndex: 0 );
		layout.Dock( "d", tabIndex: 3 );
		CollectionAssert.AreEqual( new[] { "b", "c", "a", "d" }, group.Tabs.ToArray() );
		layout.Dock( "b" );
		Assert.AreEqual( "b", group.ActiveId );
		layout.Dock( "e", "a", DockPosition.Right );
		layout.Dock( "e", "a", tabIndex: 1 );
		CollectionAssert.AreEqual( new[] { "b", "e", "c", "a", "d" }, group.Tabs.ToArray() );
		Assert.AreSame( group, layout.Root );
	}

	/// <summary>Edge self-docking needs another tab to split away from.</summary>
	[TestMethod]
	public void SelfEdgeMove()
	{
		var layout = new DockLayout();
		layout.Dock( "a" );
		var group = layout.Root;
		var changes = 0;
		layout.Changed += () => changes++;
		layout.Dock( "a", "a", DockPosition.Left );
		layout.Dock( "a", position: DockPosition.Right );
		Assert.AreSame( group, layout.Root );
		Assert.AreEqual( 0, changes );
		layout.Dock( "b" );
		layout.Dock( "a", "a", DockPosition.Top );
		var split = (DockSplit)layout.Root;
		Assert.AreSame( group, split.Second );
		Assert.AreEqual( "b", ((DockGroup)split.Second).ActiveId );
		Assert.AreEqual( "a", ((DockGroup)split.First).ActiveId );
		var before = layout.Save();
		layout.Dock( "a", "a", DockPosition.Bottom );
		Assert.AreEqual( before, layout.Save() );
		Assert.AreEqual( 2, changes );
	}

	/// <summary>Rejected moves preserve identity, order, selection and notifications.</summary>
	[TestMethod]
	public void InvalidMovesAreAtomic()
	{
		var layout = new DockLayout();
		layout.Dock( "a" );
		layout.Dock( "b", "a", DockPosition.Right );
		var before = layout.Save();
		var root = layout.Root;
		var a = layout.FindGroup( "a" );
		var changes = 0;
		layout.Changed += () => changes++;
		Action[] invalid =
		[
			() => layout.Dock( null ),
			() => layout.Dock( "" ),
			() => layout.Dock( " " ),
			() => layout.Dock( "\uD800" ),
			() => layout.Dock( "\uDC00" ),
			() => layout.Dock( "a", "missing" ),
			() => layout.Dock( "a", "" ),
			() => layout.Dock( "a", "missing", DockPosition.Left ),
			() => layout.Dock( "a", "b", (DockPosition)99 ),
			() => layout.Dock( "a", "b", tabIndex: -2 ),
			() => layout.Dock( "a", "b", tabIndex: 2 ),
			() => layout.Dock( "a", "a", tabIndex: 1 ),
			() => layout.Dock( "a", "b", DockPosition.Left, tabIndex: 1 )
		];
		foreach ( var action in invalid )
		{
			try
			{
				action();
				Assert.Fail( "Invalid docking arguments must throw." );
			}
			catch ( ArgumentException ) { }
			Assert.AreEqual( before, layout.Save() );
			Assert.AreSame( root, layout.Root );
			Assert.AreSame( a, layout.FindGroup( "a" ) );
		}
		Assert.AreEqual( 0, changes );
	}

	/// <summary>Closing active tabs chooses the next tab, then the previous final tab.</summary>
	[TestMethod]
	public void ActiveFallbackAndNotifications()
	{
		var layout = new DockLayout();
		var changes = 0;
		layout.Changed += () =>
		{
			AssertTree( layout.Root );
			changes++;
		};
		layout.Dock( "a" );
		layout.Dock( "b" );
		layout.Dock( "c" );
		Assert.IsTrue( layout.Activate( "b" ) );
		Assert.IsFalse( layout.Activate( "b" ) );
		Assert.IsFalse( layout.Activate( "missing" ) );
		layout.Close( "a" );
		Assert.AreEqual( "b", layout.FindGroup( "b" ).ActiveId );
		layout.Close( "b" );
		Assert.AreEqual( "c", layout.FindGroup( "c" ).ActiveId );
		layout.Dock( "d" );
		layout.Close( "d" );
		Assert.AreEqual( "c", layout.FindGroup( "c" ).ActiveId );
		layout.Close( "c" );
		Assert.AreEqual( 9, changes );
	}

	/// <summary>Incoming shares convert to first-child shares for each edge.</summary>
	[DataTestMethod]
	[DataRow( DockPosition.Left, false, true )]
	[DataRow( DockPosition.Right, false, false )]
	[DataRow( DockPosition.Top, true, true )]
	[DataRow( DockPosition.Bottom, true, false )]
	public void EdgeFractions( DockPosition position, bool vertical, bool before )
	{
		foreach ( var fraction in new[] { 0.05f, 0.2f, 0.5f, 0.95f } )
		{
			var layout = new DockLayout();
			layout.Dock( "a" );
			layout.Dock( "b", "a", position, fraction );
			var split = (DockSplit)layout.Root;
			Assert.AreEqual( vertical, split.Vertical );
			Assert.AreEqual( before ? fraction : 1 - fraction, split.Fraction );
			Assert.AreEqual( before ? "b" : "a", ((DockGroup)split.First).ActiveId );
			var restored = new DockLayout();
			Assert.IsTrue( restored.Restore( layout.Save(), new[] { "a", "b" } ) );
			Assert.AreEqual( layout.Save(), restored.Save() );
		}
	}

	/// <summary>Fractions and stale split references are validated before edits.</summary>
	[TestMethod]
	public void SetFractionAndInvalidFractions()
	{
		var layout = new DockLayout();
		layout.Dock( "a" );
		layout.Dock( "b", "a", DockPosition.Right );
		var split = (DockSplit)layout.Root;
		var changes = 0;
		layout.Changed += () => changes++;
		foreach ( var fraction in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0, 0.049f, 0.951f, 1 } )
		{
			var saved = layout.Save();
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => layout.Dock( "a", "b", DockPosition.Left, fraction ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => layout.Dock( "a", "b", fraction: fraction ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => layout.SetFraction( split, fraction ) );
			Assert.AreEqual( saved, layout.Save() );
		}
		Assert.AreEqual( 0, changes );
		layout.SetFraction( split, 0.05f );
		layout.SetFraction( split, 0.95f );
		layout.SetFraction( split, 0.95f );
		Assert.AreEqual( 2, changes );
		Assert.AreEqual( 0.95f, split.Fraction );
		Assert.ThrowsException<ArgumentException>( () => layout.SetFraction( null, 0.5f ) );
		Assert.ThrowsException<ArgumentException>( () => new DockLayout().SetFraction( split, 0.5f ) );
		layout.Close( "b" );
		Assert.ThrowsException<ArgumentException>( () => layout.SetFraction( split, 0.5f ) );
		Assert.AreEqual( 3, changes );
	}

	/// <summary>Public views cannot mutate layout state.</summary>
	[TestMethod]
	public void PublicViewsAreReadOnly()
	{
		var layout = new DockLayout();
		layout.Dock( "a" );
		var tabs = (IList<string>)layout.FindGroup( "a" ).Tabs;
		Assert.IsTrue( tabs.IsReadOnly );
		Assert.ThrowsException<NotSupportedException>( () => tabs.Add( "b" ) );
		foreach ( var type in new[] { typeof( DockLayout ), typeof( DockGroup ), typeof( DockSplit ) } )
		{
			foreach ( var property in type.GetProperties() ) Assert.IsNull( property.SetMethod );
		}
	}

	/// <summary>Round trips preserve every split, tab, selection and closed panel.</summary>
	[TestMethod]
	public void RoundTripAndEmptyRestore()
	{
		const string special = "a\"\\\n\u00E9\U0001D11E";
		var layout = new DockLayout();
		layout.Dock( special );
		layout.Dock( "b" );
		layout.Activate( special );
		layout.Dock( "c", "b", DockPosition.Left, 0.25f );
		layout.Dock( "d", "c", DockPosition.Bottom, 0.7f );
		var saved = layout.Save();
		var restored = new DockLayout();
		restored.Dock( "old" );
		var changes = 0;
		restored.Changed += () => changes++;
		Assert.IsTrue( restored.Restore( saved, new[] { special, "b", "c", "d", "closed" } ) );
		Assert.AreEqual( saved, restored.Save() );
		Assert.IsNull( restored.FindGroup( "old" ) );
		Assert.IsNull( restored.FindGroup( "closed" ) );
		Assert.IsTrue( restored.Restore( new DockLayout().Save(), Array.Empty<string>() ) );
		Assert.IsNull( restored.Root );
		Assert.AreEqual( 2, changes );
	}

	/// <summary>Invalid JSON, versions, shapes and IDs leave the original tree untouched.</summary>
	[TestMethod]
	public void InvalidRestoreIsAtomic()
	{
		var layout = new DockLayout();
		layout.Dock( "a" );
		layout.Dock( "b", "a", DockPosition.Right );
		var saved = layout.Save();
		var root = layout.Root;
		var a = layout.FindGroup( "a" );
		var changes = 0;
		layout.Changed += () => changes++;
		string[] invalid =
		[
			null, "", "{", "null", "[]", "{}", "{\"root\":null}",
			"{\"version\":1}", "{\"version\":2,\"root\":null}",
			"{\"version\":\"1\",\"root\":null}", "{\"version\":1.5,\"root\":null}",
			"{\"version\":1,\"version\":1,\"root\":null}",
			"{\"version\":1,\"root\":null,\"extra\":0}",
			saved + "{}",
			saved.Replace( "\"b\"", "\"a\"" ),
			saved.Replace( "\"b\"", "\"unknown\"" ),
			saved.Replace( "\"b\"", "\"A\"" ),
			saved.Replace( "\"b\"", "\" \"" ),
			saved.Replace( "\"b\"", "null" ),
			saved.Replace( "\"b\"", "123" ),
			saved.Replace( "\"b\"", "\"\\uD800\"" ),
			saved.Replace( "\"b\"", "\"\\uDC00\"" ),
			saved.Replace( "\"b\"", "\"\uD800\"" ),
			saved.Replace( "\"b\"", "\"\uDC00\"" ),
			saved.Replace( "\"activeId\"", "\"\\uD800\"" ),
			saved.Replace( "[\"a\"]", "[]" ),
			saved.Replace( "[\"a\"]", "[\"a\",\"a\"]" ),
			saved.Replace( "[\"a\"]", "{}" ),
			saved.Replace( "\"activeId\":\"a\"", "\"activeId\":\"b\"" ),
			saved.Replace( "\"activeId\":\"a\"", "\"activeId\":null" ),
			saved.Replace( "\"type\":\"group\"", "\"type\":\"other\"" ),
			saved.Replace( "\"type\":\"group\"", "\"type\":null" ),
			saved.Replace( "\"type\":\"group\"", "\"type\":\"group\",\"type\":\"group\"" ),
			saved.Replace( "\"vertical\":false", "\"vertical\":0" ),
			saved.Replace( "\"vertical\":false,", "" ),
			saved.Replace( "\"fraction\":0.5", "\"fraction\":0.01" ),
			saved.Replace( "\"fraction\":0.5", "\"fraction\":0.99" ),
			saved.Replace( "\"fraction\":0.5", "\"fraction\":1e100" ),
			saved.Replace( "\"fraction\":0.5", "\"fraction\":\"NaN\"" )
		];
		foreach ( var json in invalid )
		{
			Assert.IsFalse( layout.Restore( json, new[] { "a", "b" } ), json );
			Assert.AreEqual( saved, layout.Save() );
			Assert.AreSame( root, layout.Root );
			Assert.AreSame( a, layout.FindGroup( "a" ) );
		}
		var missingChild = JsonNode.Parse( saved );
		missingChild["root"]["first"] = null;
		Assert.IsFalse( layout.Restore( missingChild.ToJsonString(), new[] { "a", "b" } ) );
		Assert.IsFalse( layout.Restore( saved, null ) );
		Assert.IsFalse( layout.Restore( saved, new[] { "a" } ) );
		Assert.AreEqual( saved, layout.Save() );
		Assert.AreEqual( 0, changes );
	}

	/// <summary>Depth limits apply atomically to restores and moves, including moves which collapse branches.</summary>
	[TestMethod]
	public void BoundedDepth()
	{
		var layout = new DockLayout();
		var ids = Enumerable.Range( 0, 65 ).Select( x => x.ToString() ).ToArray();
		layout.Dock( ids[0] );
		for ( var i = 1; i < 64; i++ ) layout.Dock( ids[i], ids[i - 1], DockPosition.Right );
		var saved = layout.Save();
		var root = layout.Root;
		var restored = new DockLayout();
		Assert.IsTrue( restored.Restore( saved, ids ) );
		Assert.AreEqual( saved, restored.Save() );
		var changes = 0;
		layout.Changed += () => changes++;
		Assert.ThrowsException<InvalidOperationException>( () => layout.Dock( "64", "63", DockPosition.Right ) );
		Assert.ThrowsException<InvalidOperationException>( () => layout.Dock( "64", position: DockPosition.Left ) );
		Assert.AreEqual( saved, layout.Save() );
		Assert.AreSame( root, layout.Root );
		Assert.AreEqual( 0, changes );
		var tooDeep = "{\"version\":1,\"root\":{\"type\":\"split\",\"vertical\":false,\"fraction\":0.5,\"first\":"
			+ saved["{\"version\":1,\"root\":".Length..^1] + ",\"second\":{\"type\":\"group\",\"tabs\":[\"64\"],\"activeId\":\"64\"}}}";
		using var document = System.Text.Json.JsonDocument.Parse( tooDeep, new System.Text.Json.JsonDocumentOptions { MaxDepth = 128 } );
		Assert.IsFalse( layout.Restore( tooDeep, ids ) );
		Assert.AreEqual( saved, layout.Save() );
		layout.Dock( "0", "63", DockPosition.Bottom );
		AssertTree( layout.Root );
		Assert.IsTrue( restored.Restore( layout.Save(), ids ) );
		layout.Dock( "extra", "0" );
		var before = layout.Save();
		Assert.ThrowsException<InvalidOperationException>( () => layout.Dock( "extra", "0", DockPosition.Left ) );
		Assert.AreEqual( before, layout.Save() );
	}

	/// <summary>Mixed edits maintain unique IDs, valid selection, full splits and lossless persistence.</summary>
	[TestMethod]
	public void MixedEditsPreserveInvariants()
	{
		var layout = new DockLayout();
		var random = new Random( 481 );
		var ids = Enumerable.Range( 0, 20 ).Select( x => $"panel{x}" ).ToArray();
		var present = new HashSet<string>();
		for ( var i = 0; i < 1000; i++ )
		{
			var id = ids[random.Next( ids.Length )];
			if ( random.Next( 4 ) == 0 )
			{
				Assert.AreEqual( present.Remove( id ), layout.Close( id ) );
			}
			else
			{
				var target = ids[random.Next( ids.Length )];
				layout.Dock( id, present.Contains( target ) ? target : null, (DockPosition)random.Next( 5 ), 0.25f );
				present.Add( id );
			}
			CollectionAssert.AreEquivalent( present.ToArray(), AssertTree( layout.Root ).ToArray() );
			var restored = new DockLayout();
			Assert.IsTrue( restored.Restore( layout.Save(), ids ) );
			Assert.AreEqual( layout.Save(), restored.Save() );
		}
	}

	static HashSet<string> AssertTree( DockNode root )
	{
		var ids = new HashSet<string>();
		if ( root is not null ) Visit( root, 1 );
		return ids;

		void Visit( DockNode node, int depth )
		{
			Assert.IsTrue( depth <= 64 );
			Assert.IsNotNull( node );
			if ( node is DockGroup group )
			{
				Assert.IsTrue( group.Tabs.Count > 0 );
				Assert.IsTrue( group.Tabs.Contains( group.ActiveId ) );
				foreach ( var id in group.Tabs ) Assert.IsTrue( ids.Add( id ) );
			}
			else
			{
				var split = (DockSplit)node;
				Assert.IsTrue( float.IsFinite( split.Fraction ) && split.Fraction >= 0.05f && split.Fraction <= 0.95f );
				Visit( split.First, depth + 1 );
				Visit( split.Second, depth + 1 );
			}
		}
	}
}
