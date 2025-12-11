using Sandbox.Engine;
using Sandbox.UI;
using System;

namespace UITest;

[TestClass]
public class StyleSheetImport
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
}
