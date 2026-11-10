using Sandbox;

public partial class MyClass
{
	public static Model Debug = Cloud.Model( "facepunch.cone" );

	public void SomeMethod()
	{
		var mdl = Cloud.Model( "facepunch.box" );
	}
}
