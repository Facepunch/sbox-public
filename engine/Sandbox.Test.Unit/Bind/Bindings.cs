using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Bind;

namespace TestBind;

[TestClass]
public class Bindings
{
	[TestMethod]
	public void MethodBinding()
	{
		var target = new BindingTarget { One = "one" };
		var source = new MethodProxy<string>( () => target.One, x => target.One = x );

		Assert.AreEqual( "one", target.One );
		Assert.AreEqual( "one", source.Value );

		target.One = "two";

		Assert.AreEqual( "two", target.One );
		Assert.AreEqual( "two", source.Value );

		source.Value = "three";

		Assert.AreEqual( "three", target.One );
		Assert.AreEqual( "three", source.Value );

		target.One = "four";

		Assert.AreEqual( "four", target.One );
		Assert.AreEqual( "four", source.Value );
	}

	[TestMethod]
	public void MethodBindingReadOnly()
	{
		var target = new BindingTarget { One = "one" };
		var source = new MethodProxy<string>( () => target.One, null );

		Assert.IsTrue( source.IsValid );
		Assert.IsTrue( source.CanRead );
		Assert.IsFalse( source.CanWrite );

		Assert.AreEqual( "one", target.One );
		Assert.AreEqual( "one", source.Value );

		target.One = "two";

		Assert.AreEqual( "two", target.One );
		Assert.AreEqual( "two", source.Value );

		target.One = "four";

		Assert.AreEqual( "four", target.One );
		Assert.AreEqual( "four", source.Value );
	}

	[TestMethod]
	public void PropertyBinding()
	{
		var target = new BindingTarget { One = "one" };
		var source = PropertyProxy.Create( target, nameof( BindingTarget.One ) );

		Assert.AreEqual( "one", target.One );
		Assert.AreEqual( "one", source.Value );

		target.One = "two";

		Assert.AreEqual( "two", target.One );
		Assert.AreEqual( "two", source.Value );

		source.Value = "three";

		Assert.AreEqual( "three", target.One );
		Assert.AreEqual( "three", source.Value );

		target.One = "four";

		Assert.AreEqual( "four", target.One );
		Assert.AreEqual( "four", source.Value );
	}

	private sealed class BindingTarget
	{
		public string One { get; set; }
	}
}

