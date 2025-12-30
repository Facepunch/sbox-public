using Sandbox.UI;
namespace UITest.Panels;

[TestClass]
[DoNotParallelize] // Modfiies UI System Global
public partial class PanelTests
{
	[TestMethod]
	public void SetClass()
	{
		var panel = new Panel();
		panel.SetClass( "one", true );

		Assert.IsTrue( panel.HasClass( "one" ) );
		Assert.IsFalse( panel.HasClass( "two" ) );

		panel.SetClass( "one", false );

		Assert.IsFalse( panel.HasClass( "one" ) );
		Assert.IsFalse( panel.HasClass( "two" ) );

		panel.AddClass( "one" );

		Assert.IsTrue( panel.HasClass( "one" ) );
		Assert.IsFalse( panel.HasClass( "two" ) );

		panel.AddClass( "two" );

		Assert.IsTrue( panel.HasClass( "one" ) );
		Assert.IsTrue( panel.HasClass( "two" ) );

		panel.RemoveClass( "two" );

		Assert.IsTrue( panel.HasClass( "one" ) );
		Assert.IsFalse( panel.HasClass( "two" ) );
	}
}
