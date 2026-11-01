using System;
using Sandbox.Internal;

namespace Sandbox.Tests;

[TestClass]
public class MethodDelegateRefKindTests
{
	delegate ref int OutEcho( out int value );
	delegate ref int RefEcho( ref int value );
	delegate ref readonly int ReadOnlyEcho( ref int value );
	delegate int OutRead( out int value );
	delegate int InRead( in int value );
	delegate ref int InstanceRefEcho( RefKindTarget target, ref int value );
	delegate ref int InstanceOutEcho( RefKindTarget target, out int value );

	[Expose]
	public static class RefKindStatics
	{
		public static ref int Echo( ref int value ) => ref value;
		public static int Read( ref int value ) => value;
		public static int AddOne( int value ) => value + 1;
	}

	[Expose]
	public sealed class RefKindTarget
	{
		public ref int Echo( ref int value ) => ref value;
	}

	static readonly Lazy<TypeLibrary> Library = new( () =>
	{
		var library = new TypeLibrary();
		library.AddAssembly( typeof( RefKindStatics ).Assembly, false );
		return library;
	} );

	static MethodDescription Static( string name ) => Library.Value.GetType( typeof( RefKindStatics ) ).GetStaticMethod( name );
	static MethodDescription Instance( string name ) => Library.Value.GetType( typeof( RefKindTarget ) ).GetMethod( name );

	[TestMethod]
	public void OutDelegateCannotBindRefTarget()
	{
		var echo = Static( nameof( RefKindStatics.Echo ) );

		Assert.ThrowsException<ArgumentException>( () => echo.CreateDelegate<OutEcho>() );
		Assert.ThrowsException<ArgumentException>( () => echo.CreateDelegate( typeof( OutEcho ) ) );
	}

	[TestMethod]
	public void OutDelegateCannotBindRefTargetWithTarget()
	{
		var echo = Instance( nameof( RefKindTarget.Echo ) );
		var target = new RefKindTarget();

		Assert.ThrowsException<ArgumentException>( () => echo.CreateDelegate<OutEcho>( target ) );
		Assert.ThrowsException<ArgumentException>( () => echo.CreateDelegate( typeof( OutEcho ), target ) );
		Assert.ThrowsException<ArgumentException>( () => echo.CreateDelegate<InstanceOutEcho>() );
	}

	[TestMethod]
	public void ReadOnlyReturnCannotBindMutableReturn()
	{
		var echo = Static( nameof( RefKindStatics.Echo ) );

		Assert.ThrowsException<ArgumentException>( () => echo.CreateDelegate<ReadOnlyEcho>() );
	}

	[TestMethod]
	public void ParameterRefKindMustMatch()
	{
		var read = Static( nameof( RefKindStatics.Read ) );

		Assert.ThrowsException<ArgumentException>( () => read.CreateDelegate<OutRead>() );
		Assert.ThrowsException<ArgumentException>( () => read.CreateDelegate<InRead>() );
	}

	[TestMethod]
	public void MatchingRefKindsStillBind()
	{
		var value = 5;
		ref var echoed = ref Static( nameof( RefKindStatics.Echo ) ).CreateDelegate<RefEcho>()( ref value );
		echoed = 7;
		Assert.AreEqual( 7, value );

		var target = new RefKindTarget();
		ref var bound = ref Instance( nameof( RefKindTarget.Echo ) ).CreateDelegate<RefEcho>( target )( ref value );
		bound = 9;
		Assert.AreEqual( 9, value );

		ref var open = ref Instance( nameof( RefKindTarget.Echo ) ).CreateDelegate<InstanceRefEcho>()( target, ref value );
		open = 11;
		Assert.AreEqual( 11, value );

		Assert.AreEqual( 3, Static( nameof( RefKindStatics.AddOne ) ).CreateDelegate<Func<int, int>>()( 2 ) );
	}
}
