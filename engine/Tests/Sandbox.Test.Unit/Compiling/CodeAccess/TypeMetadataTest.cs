namespace CompilingTests.CodeAccess;

[TestClass]
public class TypeMetadata
{
	[TestMethod]
	public void IsPrimitiveDoesNotGrantReflectionAccess()
	{
		var rules = AccessRules.Instance;
		Assert.IsTrue( rules.IsAllowed( "System.Private.CoreLib/System.Type.get_IsPrimitive()" ) );
		Assert.IsFalse( rules.IsAllowed( "System.Private.CoreLib/System.Type.get_Assembly()" ) );
		Assert.IsFalse( rules.IsAllowed( "System.Private.CoreLib/System.Type.get_TypeHandle()" ) );
		Assert.IsFalse( rules.IsAllowed( "System.Private.CoreLib/System.Type.GetMethods()" ) );
		Assert.IsFalse( rules.IsAllowed( "System.Private.CoreLib/System.Type.GetConstructors()" ) );
		Assert.IsFalse( rules.IsAllowed( "System.Private.CoreLib/System.Type.GetType( System.String )" ) );
	}
}
