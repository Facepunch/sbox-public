namespace Reflection;

[TestClass]
public class Properties
{
	System.Reflection.Assembly ThisAssembly => this.GetType().Assembly;

	[TestMethod]
	public void SetPropertiesInDynamicAssembly()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, false );

		var instance = new ClassWithProps();
		var type = tl.GetType<ClassWithProps>();

		// We should only see the public properties, not the private or internal
		Assert.AreEqual( 3, type.Properties.Count() );

		// We should be able to set public properties
		var publicNameProp = type.GetProperty( nameof( ClassWithProps.PublicName ) );
		publicNameProp.SetValue( instance, "Homer" );
		Assert.AreEqual( publicNameProp.GetValue( instance ), "Homer" );

		// We should not be able to set private setter properties
		var privateSetNameProp = type.GetProperty( nameof( ClassWithProps.PrivateSetName ) );
		privateSetNameProp.SetValue( instance, "Simpson" );
		Assert.AreNotEqual( privateSetNameProp.GetValue( instance ), "Simpson" );

		// We should not be able to set init properties
		var initNameProp = type.GetProperty( nameof( ClassWithProps.InitName ) );
		initNameProp.SetValue( instance, "Simpsons" );
		Assert.AreNotEqual( initNameProp.GetValue( instance ), "Simpsons" );

		// This other route shouldn't work either
		tl.SetProperty( instance, nameof( ClassWithProps.InitName ), "Flintstones" );
		Assert.AreNotEqual( tl.GetPropertyValue( instance, nameof( ClassWithProps.InitName ) ), "Flintstones" );

		tl.RemoveAssembly( ThisAssembly );
	}
}

[Expose]
public class ClassWithProps
{
	public string PublicName { get; set; } = "Peter";
	public string PrivateSetName { get; private set; } = "Griffin";
	public string InitName { get; init; } = "Family Guy";
	private int PrivateAge { get; set; } = 42;
	internal int InternalSize { get; set; } = 3;
}
