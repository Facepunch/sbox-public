using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Sandbox.Diagnostics;

namespace EngineTests;

/// <summary>
/// An error says which package it came from - the first "package.*" assembly on its stack - so the code error notice
/// can name it.
/// </summary>
[TestClass]
public class LogSourceAssemblyTest
{
	/// <summary>
	/// A package assembly with Thrower.Throw(), which throws, and Thrower.Run( Action ), which calls what it's given
	/// </summary>
	static Type BuildPackage( string name )
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( name ), AssemblyBuilderAccess.Run );
		var type = assembly.DefineDynamicModule( name ).DefineType( "Thrower", TypeAttributes.Public | TypeAttributes.Class );

		var thrower = type.DefineMethod( "Throw", MethodAttributes.Public | MethodAttributes.Static, typeof( void ), Type.EmptyTypes ).GetILGenerator();
		thrower.Emit( OpCodes.Newobj, typeof( InvalidOperationException ).GetConstructor( Type.EmptyTypes ) );
		thrower.Emit( OpCodes.Throw );

		var runner = type.DefineMethod( "Run", MethodAttributes.Public | MethodAttributes.Static, typeof( void ), [typeof( Action )] ).GetILGenerator();
		runner.Emit( OpCodes.Ldarg_0 );
		runner.Emit( OpCodes.Callvirt, typeof( Action ).GetMethod( "Invoke" ) );
		runner.Emit( OpCodes.Ret );

		return type.CreateType();
	}

	static Exception Catch( Action action )
	{
		try
		{
			action();
		}
		catch ( Exception e )
		{
			return e;
		}

		throw new AssertFailedException( "Nothing was thrown" );
	}

	[TestMethod]
	public void ExceptionThrownInPackageNamesThePackage()
	{
		// Arrange
		var package = BuildPackage( "package.test.thrower" );
		var exception = Catch( () => package.GetMethod( "Throw" ).CreateDelegate<Action>()() );

		// Act
		var source = Logger.FindPackageAssembly( exception, null );

		// Assert
		Assert.AreEqual( "package.test.thrower", source );
	}

	[TestMethod]
	public void ExceptionWrappedByReflectionNamesThePackageInside()
	{
		// Arrange
		var package = BuildPackage( "package.test.invoked" );
		var exception = Catch( () => package.GetMethod( "Throw" ).Invoke( null, null ) );
		Assert.IsInstanceOfType<TargetInvocationException>( exception );

		// Act
		var source = Logger.FindPackageAssembly( exception, null );

		// Assert
		Assert.AreEqual( "package.test.invoked", source );
	}

	[TestMethod]
	public void ErrorLoggedFromPackageCodeNamesThePackage()
	{
		// Arrange
		var package = BuildPackage( "package.test.logger" );
		StackTrace logged = null;
		package.GetMethod( "Run" ).CreateDelegate<Action<Action>>()( () => logged = new StackTrace( 0, false ) );

		// Act
		var source = Logger.FindPackageAssembly( null, logged );

		// Assert
		Assert.AreEqual( "package.test.logger", source );
	}

	[TestMethod]
	public void ErrorOutsideAnyPackageNamesNothing()
	{
		// Arrange
		var exception = Catch( () => throw new InvalidOperationException() );

		// Act
		var source = Logger.FindPackageAssembly( exception, new StackTrace( 0, false ) );

		// Assert
		Assert.IsNull( source );
	}
}
