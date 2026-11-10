namespace EngineTests;

[TestClass]
public class KeyBindingCommandTests
{
	[TestMethod]
	public void BindingCommandsAreManagedAfterStartup()
	{
		string[] commands = ["bind", "unbind", "unbindall", "binddefaults", "writekeybindings", "key_findbinding", "key_listboundkeys"];
		foreach ( var name in commands )
			Assert.IsInstanceOfType<ManagedCommand>( ConVarSystem.Find( name ), name );
		Assert.IsFalse( ConVarSystem.Find( "key_findbinding" ).IsProtected );
		Assert.IsFalse( ConVarSystem.Find( "key_listboundkeys" ).IsProtected );
	}
}
