using Sandbox.Internal;

namespace ReflectionTests;

[TestClass]
public class EnumDescriptionTest
{
	// Scanning the test assembly is slow, so build it once. Tests only read through it.
	static readonly System.Lazy<TypeLibrary> SharedLibrary = new( () =>
	{
		var tl = new TypeLibrary();
		tl.AddAssembly( typeof( EnumDescriptionTest ).Assembly, true );
		return tl;
	} );

	private EnumDescription GetExampleEnumDescription()
	{
		return SharedLibrary.Value.GetEnumDescription( typeof( ExampleEnum ) );
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
