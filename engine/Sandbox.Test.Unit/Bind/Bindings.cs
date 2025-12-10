using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Bind;

namespace TestBind;

[TestClass]
public class Bindings
{
	public string One { get; set; }
	public string Two { get; set; }

	[TestMethod]
	public void MethodBinding()
	{
		One = "one";

		var source = new MethodProxy<string>( () => One, x => One = x );

		Assert.AreEqual( "one", One );
		Assert.AreEqual( "one", source.Value );

		One = "two";

		Assert.AreEqual( "two", One );
		Assert.AreEqual( "two", source.Value );

		source.Value = "three";

		Assert.AreEqual( "three", One );
		Assert.AreEqual( "three", source.Value );

		One = "four";

		Assert.AreEqual( "four", One );
		Assert.AreEqual( "four", source.Value );
	}

	[TestMethod]
	public void MethodBindingReadOnly()
	{
		One = "one";

		var source = new MethodProxy<string>( () => One, null );

		Assert.IsTrue( source.IsValid );
		Assert.IsTrue( source.CanRead );
		Assert.IsFalse( source.CanWrite );

		Assert.AreEqual( "one", One );
		Assert.AreEqual( "one", source.Value );

		One = "two";

		Assert.AreEqual( "two", One );
		Assert.AreEqual( "two", source.Value );

		One = "four";

		Assert.AreEqual( "four", One );
		Assert.AreEqual( "four", source.Value );
	}

	[TestMethod]
	public void PropertyBinding()
	{
		One = "one";

		var source = PropertyProxy.Create( this, "One" );

		Assert.AreEqual( "one", One );
		Assert.AreEqual( "one", source.Value );

		One = "two";

		Assert.AreEqual( "two", One );
		Assert.AreEqual( "two", source.Value );

		source.Value = "three";

		Assert.AreEqual( "three", One );
		Assert.AreEqual( "three", source.Value );

		One = "four";

		Assert.AreEqual( "four", One );
		Assert.AreEqual( "four", source.Value );
	}
}

