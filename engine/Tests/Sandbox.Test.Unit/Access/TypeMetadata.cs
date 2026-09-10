namespace Sandbox.Test;

[TestClass]
public class TypeMetadata
{
	[TestMethod]
	public void IsPrimitiveDoesNotGrantReflectionAccess()
	{
		var rules = new AccessRules();
		Assert.IsTrue( rules.IsInWhitelist( "System.Private.CoreLib/System.Type.get_IsPrimitive()" ) );
		Assert.IsFalse( rules.IsInWhitelist( "System.Private.CoreLib/System.Type.get_Assembly()" ) );
		Assert.IsFalse( rules.IsInWhitelist( "System.Private.CoreLib/System.Type.get_TypeHandle()" ) );
		Assert.IsFalse( rules.IsInWhitelist( "System.Private.CoreLib/System.Type.GetMethods()" ) );
		Assert.IsFalse( rules.IsInWhitelist( "System.Private.CoreLib/System.Type.GetConstructors()" ) );
		Assert.IsFalse( rules.IsInWhitelist( "System.Private.CoreLib/System.Type.GetType( System.String )" ) );
	}
}
