using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize]
public class RuntimeStyleSheetTest
{
	[TestMethod]
	public void MutationsRebuildOwnerAndDescendantsWithoutHover()
	{
		var root = new RootPanel();
		var owner = new Panel { Parent = root };
		var child = new Panel { Parent = owner };
		var grandchild = new Panel { Parent = child };
		var sheet = StyleSheet.FromString( "* { background-color: red; }", "runtime-test", null );
		sheet.FileName = "runtime-test";

		void Check( bool applied )
		{
			root.BuildStyleRules();
			foreach ( var panel in new[] { owner, child, grandchild } )
			{
				var cascade = new LayoutCascade { Scale = 1 };
				panel.Style.BuildCached( ref cascade );
				Assert.AreEqual( applied, panel.Style.ContainsStyle( sheet.Nodes[0].Styles ) );
			}
		}

		try
		{
			Check( false );
			owner.StyleSheet.Add( sheet );
			Check( true );
			owner.StyleSheet.Remove( sheet );
			Check( false );
			owner.StyleSheet.Add( sheet );
			Check( true );
			owner.StyleSheet.Remove( "runtime-*" );
			Check( false );
		}
		finally
		{
			root.Delete( true );
		}
	}
}
