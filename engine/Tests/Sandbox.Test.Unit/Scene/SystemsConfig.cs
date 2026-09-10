namespace SceneTests;

[TestClass]
public class SystemsConfigTest
{
	[TestMethod]
	public void GetPropertyValue_UsesDefaultValueAttributeWhenUnset()
	{
		var typeLibrary = new Sandbox.Internal.TypeLibrary();
		typeLibrary.AddAssembly( typeof( DefaultValueSystem ).Assembly, false );

		var systemType = typeLibrary.GetType<DefaultValueSystem>();
		var property = systemType.GetProperty( nameof( DefaultValueSystem.MyInt ) );
		var config = new Sandbox.SystemsConfig();

		Assert.AreEqual( 123, config.GetPropertyValue( systemType, property ) );
	}

}

[Expose]
public sealed class DefaultValueSystem : GameObjectSystem
{
	public DefaultValueSystem( Sandbox.Scene scene ) : base( scene )
	{
	}

	[Property, DefaultValue( 123 )]
	public int MyInt { get; set; } = 123;
}
