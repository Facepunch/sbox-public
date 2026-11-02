using System;

namespace SceneTests;

[TestClass]
public class PrimitiveTypeValidationTest
{
	[TestMethod]
	public void UndefinedPrimitiveTypeIsRejected()
	{
		foreach ( var type in Enum.GetValues<Graphics.PrimitiveType>() )
			SceneDynamicObject.ThrowIfUndefined( type );

		Assert.ThrowsException<ArgumentOutOfRangeException>( () => SceneDynamicObject.ThrowIfUndefined( (Graphics.PrimitiveType)9999 ) );
	}
}
