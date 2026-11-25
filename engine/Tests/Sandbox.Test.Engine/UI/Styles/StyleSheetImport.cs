using Sandbox.Engine;
using Sandbox.UI;
using System;

namespace UITests;

[TestClass]
[DoNotParallelize]
public class StyleSheetImportTest
{
	[TestMethod]
	public void Parsing()
	{
		GlobalContext.Current.FileMount = new AggregateFileSystem();
		FileSystem.Mounted.Mount( new LocalFileSystem( Environment.CurrentDirectory ) );

		var sheet = StyleSheet.FromFile( "unittest/styles/import-test.scss" );

		Assert.IsNotNull( sheet );

		// Test variables
		Assert.AreEqual( sheet.GetVariable( "$primary" ), "red" );
		Assert.AreEqual( sheet.GetVariable( "$white" ), "black" );

		// A dropped import would silently skip its switch case below - assert each selector is present.
		Assert.IsTrue( sheet.Nodes.Any( n => n.SelectorStrings.Contains( ".one" ) ) );
		Assert.IsTrue( sheet.Nodes.Any( n => n.SelectorStrings.Contains( ".include" ) ) );

		// Test styles
		foreach ( var node in sheet.Nodes )
		{
			switch ( node.SelectorStrings.First() )
			{
				case ".one":
					{
						Assert.AreEqual( node.Styles.MarginTop.ToString(), "50px" );
						Assert.AreEqual( node.Styles.BackgroundColor.Value.Hex, "#FF0000" );
						break;
					}
				case ".include":
					{
						Assert.AreEqual( node.Styles.BackgroundColor.Value.Hex, "#FF0000" );
						break;
					}
			}
		}
	}

	[TestMethod]
	public void NestedImports()
	{
		GlobalContext.Current.FileMount = new AggregateFileSystem();
		FileSystem.Mounted.Mount( new LocalFileSystem( Environment.CurrentDirectory ) );

		var sheet = StyleSheet.FromFile( "unittest/styles/nested/nested-import-test.scss" );

		Assert.IsNotNull( sheet );

		// Test variables
		Assert.AreEqual( sheet.GetVariable( "$var1" ), "red" );
		Assert.AreEqual( sheet.GetVariable( "$var2" ), "blue" );
		Assert.AreEqual( sheet.GetVariable( "$var3" ), "pink" ); // Was green but overridden by the nested import

		// A dropped import would silently skip its switch case below - assert each selector is present.
		Assert.IsTrue( sheet.Nodes.Any( n => n.SelectorStrings.Contains( ".base" ) ) );
		Assert.IsTrue( sheet.Nodes.Any( n => n.SelectorStrings.Contains( ".nested" ) ) );

		// Test styles
		foreach ( var node in sheet.Nodes )
		{
			switch ( node.SelectorStrings.First() )
			{
				case ".base":
					{
						Assert.AreEqual( node.Styles.FontSize.ToString(), "32px" );
						Assert.AreEqual( node.Styles.FontColor?.Hex, "#0000FF" );
						break;
					}
				case ".nested":
					{
						Assert.AreEqual( node.Styles.FontSize.ToString(), "64px" );
						Assert.AreEqual( node.Styles.FontColor?.Hex, "#FF0000" );
						break;
					}
			}
		}
	}

	[TestMethod]
	public void ReloadingStyles()
	{
		GlobalContext.Current.FileMount = new AggregateFileSystem();
		FileSystem.Mounted.Mount( new LocalFileSystem( Environment.CurrentDirectory ) );

		var sheet = StyleSheet.FromString( ".box { background-color: red; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( sheet.Nodes.First().Styles.BackgroundColor, Color.Red );

		sheet.UpdateFromString( ".box { width: 100%; }" );

		Assert.AreEqual( sheet.Nodes.First().Styles.BackgroundColor, null );
	}

	/// <summary>
	/// Reset should only clear the current context's sheets.
	/// </summary>
	[TestMethod]
	public void ResetStyleSheetsIsContextScoped()
	{
		StyleSheet menuSheet;
		using ( GlobalContext.MenuScope() )
		{
			menuSheet = StyleSheet.FromFile( "unittest/a10-menu-only.scss", null, true );
		}

		var gameSheet = StyleSheet.FromFile( "unittest/a10-game-only.scss", null, true );
		StyleSheet.ResetStyleSheets();

		Assert.IsTrue( StyleSheet.AllLoaded.Contains( menuSheet ) );
		Assert.IsFalse( StyleSheet.AllLoaded.Contains( gameSheet ) );
	}

	[TestMethod]
	public void LoadedOnlyListsGameSheets()
	{
		StyleSheet menuSheet;
		using ( GlobalContext.MenuScope() )
		{
			menuSheet = StyleSheet.FromInline( ".menu-only { color: red; }", "inline:loaded-menu-only" );
		}

		Assert.IsFalse( StyleSheet.Loaded.Contains( menuSheet ) );

		using ( GlobalContext.MenuScope() )
		{
			Assert.IsFalse( StyleSheet.Loaded.Contains( menuSheet ) );
			StyleSheet.Loaded.Clear();
		}

		Assert.IsTrue( StyleSheet.AllLoaded.Contains( menuSheet ) );
	}

	/// <summary>
	/// Panels are built on several threads at once (this assembly runs tests in parallel). Loading sheets concurrently
	/// must neither corrupt the loaded list (a null slot made every later FromFile throw) nor load one key twice.
	/// </summary>
	[TestMethod]
	public void ConcurrentLoadsKeepTheLoadedListIntact()
	{
		const int keys = 64;
		var results = new StyleSheet[keys, 16];

		System.Threading.Tasks.Parallel.For( 0, keys * 16, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
		{
			// The ambient context flows into the parallel tasks, so every load is in this test's context
			results[i % keys, i / keys] = StyleSheet.FromInline( ".a { width: 1px; }", $"inline:concurrent-{i % keys}" );
		} );

		lock ( StyleSheet.AllLoaded )
		{
			Assert.IsFalse( StyleSheet.AllLoaded.Contains( null ) );
			for ( int k = 0; k < keys; k++ )
			{
				Assert.AreEqual( 1, StyleSheet.AllLoaded.Count( x => x.FileName == $"inline:concurrent-{k}" ) );
				for ( int n = 0; n < 16; n++ )
					Assert.AreSame( results[k, 0], results[k, n] );
			}

			StyleSheet.AllLoaded.RemoveAll( x => x?.FileName?.StartsWith( "inline:concurrent-" ) == true );
		}
	}
}
