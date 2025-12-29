using Sandbox.Internal;

namespace Reflection;

[TestClass]
public class EnumDescriptionTest
{
	private EnumDescription GetExampleEnumDescription()
	{
		var tl = new TypeLibrary();
		tl.AddAssembly( GetType().Assembly, true );

		return tl.GetEnumDescription( typeof( ExampleEnum ) );
	}

	[TestMethod]
	public void GetEnumDescription()
	{
		var enumDesc = GetExampleEnumDescription();

		Assert.AreEqual( 4, enumDesc.Count() );
	}

	[TestMethod]
	public void GetEntryByObjectValue()
	{
		var enumDesc = GetExampleEnumDescription();
		var enumEntry = enumDesc.GetEntry( ExampleEnum.C );

		Assert.AreEqual( nameof( ExampleEnum.C ), enumEntry.Name );
	}

	[TestMethod]
	public void GetEntryByIntegerValue()
	{
		var enumDesc = GetExampleEnumDescription();
		var enumEntry = enumDesc.GetEntry( 2 );

		Assert.AreEqual( nameof( ExampleEnum.C ), enumEntry.Name );
	}
}

[Expose]
public enum ExampleEnum
{
	A = 0,
	B = 1,
	C = 2,
	D = 3
}
