namespace Reflection;

[TestClass]
[DoNotParallelize]
public class MemberMethods
{
	System.Reflection.Assembly ThisAssembly => this.GetType().Assembly;

	[TestMethod]
	public void FindStaticMethodsByName()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, true );

		RegularClass.DebugValue = "";
		StaticClass.DebugValue = "";

		var methods = tl.FindStaticMethods( "RegularStaticMethod" );
		Assert.AreEqual( 2, methods.Count() );

		Assert.AreEqual( "", RegularClass.DebugValue );
		Assert.AreEqual( "", StaticClass.DebugValue );

		foreach ( var method in methods )
		{
			method.Invoke( null );
		}

		Assert.AreEqual( "Regular Static Method", RegularClass.DebugValue );
		Assert.AreEqual( "Regular Static Method", StaticClass.DebugValue );

		tl.RemoveAssembly( ThisAssembly );
		Assert.AreEqual( 0, tl.Types.Count() );
	}

	[TestMethod]
	public void FindStaticMethodsByAttributeAndName()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, true );

		RegularClass.DebugValue = "";
		StaticClass.DebugValue = "";

		var methods = tl.FindStaticMethods<MyMethodAttribute>( "RegularStaticMethod" );
		Assert.AreEqual( 1, methods.Count() );

		Assert.AreEqual( "", RegularClass.DebugValue );
		Assert.AreEqual( "", StaticClass.DebugValue );

		foreach ( var method in methods )
		{
			method.Invoke( null );
		}

		Assert.AreEqual( "", RegularClass.DebugValue );
		Assert.AreEqual( "Regular Static Method", StaticClass.DebugValue );

		tl.RemoveAssembly( ThisAssembly );
		Assert.AreEqual( 0, tl.Types.Count() );
	}

	[TestMethod]
	public void FindStaticMethodsByAttribute()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, true );

		RegularClass.DebugValue = "";
		StaticClass.DebugValue = "";

		var methods = tl.GetMethodsWithAttribute<MyMethodAttribute>().Select( x => x.Method ).ToArray();
		Assert.AreEqual( 1, methods.Count() );

		Assert.AreEqual( "", RegularClass.DebugValue );
		Assert.AreEqual( "", StaticClass.DebugValue );

		foreach ( var method in methods )
		{
			method.Invoke( null );
		}

		Assert.AreEqual( "", RegularClass.DebugValue );
		Assert.AreEqual( "Regular Static Method", StaticClass.DebugValue );

		Assert.IsTrue( methods[0].IsMethod );
		Assert.IsTrue( methods[0].IsStatic );
		Assert.IsFalse( methods[0].IsProperty );
		Assert.AreEqual( "My Static Method", methods[0].Title );
		Assert.AreEqual( "RegularStaticMethod", methods[0].Name );
		Assert.AreEqual( "A load of shit", methods[0].Description );
		Assert.IsTrue( methods[0].Tags.Contains( "broken" ) );
		Assert.IsTrue( methods[0].HasTag( "broken" ) );
		Assert.IsFalse( methods[0].HasTag( "fffff" ) );
		Assert.IsTrue( methods[0].IsNamed( "RegularStaticMethod" ) );
		Assert.IsFalse( methods[0].IsNamed( "PoopMonster" ) );

		tl.RemoveAssembly( ThisAssembly );
		Assert.AreEqual( 0, tl.Types.Count() );
	}
}


public class RegularClass
{
	public static string DebugValue = "";

	public static void RegularStaticMethod()
	{
		DebugValue = "Regular Static Method";
	}
}

public static class StaticClass
{
	public static string DebugValue = "";

	[MyMethod]
	[Title( "My Static Method" )]
	[Description( "A load of shit" )]
	[Tag( "broken" )]
	public static void RegularStaticMethod()
	{
		DebugValue = "Regular Static Method";
	}
}

public class MyMethodAttribute : System.Attribute, Sandbox.IMemberAttribute
{
	public Sandbox.MemberDescription MemberDescription { get; set; }
}
