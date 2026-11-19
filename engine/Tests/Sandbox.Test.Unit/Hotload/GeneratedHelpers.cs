using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace HotloadTests;

/// <summary>
/// Generated helper methods with non-C# names still use ordinary member identity.
/// </summary>
[TestClass, DoNotParallelize]
public class GeneratedHelpers
{
	private sealed class UnchangedCallbacks<T>
	{
		internal static Func<int> Create() => () => 42;
	}

	private sealed class References
	{
		internal MethodInfo Method;
		internal Func<int> Callback;
	}

	private static Type Emit( int version )
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( "GeneratedHelpers" ) { Version = new Version( version, 0 ) }, AssemblyBuilderAccess.RunAndCollect );
		var type = assembly.DefineDynamicModule( "Helpers" ).DefineType( "Helpers", TypeAttributes.Public );
		var method = type.DefineMethod( "<initialize>Value", MethodAttributes.Public | MethodAttributes.Static, typeof( int ), Type.EmptyTypes );
		var il = method.GetILGenerator();
		il.Emit( OpCodes.Ldc_I4, version );
		il.Emit( OpCodes.Ret );

		return type.CreateType();
	}

	/// <summary>
	/// A leading angle bracket does not make a method a C# lambda or permit losing its reflection handle.
	/// </summary>
	[TestMethod]
	public void MigratesNonLambdaMethodAndDelegate()
	{
		var before = Emit( 1 );
		var after = Emit( 2 );
		var references = new References { Method = before.GetMethod( "<initialize>Value" ) };
		references.Callback = references.Method.CreateDelegate<Func<int>>();

		var hotload = new Hotload();
		hotload.WatchInstance( references );
		hotload.ReplacingAssembly( before.Assembly, after.Assembly );

		var result = hotload.UpdateReferences();

		Assert.IsTrue( result.Success );
		Assert.AreEqual( after, references.Method.DeclaringType );
		Assert.AreEqual( 2, references.Method.Invoke( null, null ) );
		Assert.AreEqual( 2, references.Callback() );
	}

	/// <summary>
	/// Unrelated replacements must preserve reflected lambdas on unchanged generic types without a resolver.
	/// </summary>
	[TestMethod]
	public void PreservesLambdaFromUnchangedAssembly()
	{
		var callback = UnchangedCallbacks<int>.Create();
		var references = new References { Method = callback.Method, Callback = callback };
		var before = Emit( 1 );
		var after = Emit( 2 );
		var hotload = new Hotload();
		hotload.WatchInstance( references );
		hotload.ReplacingAssembly( before.Assembly, after.Assembly );

		var result = hotload.UpdateReferences();

		Assert.IsTrue( result.Success );
		Assert.IsFalse( result.Warnings.Any(), string.Join( "\n", result.Warnings ) );
		Assert.AreSame( callback.Method, references.Method );
		Assert.AreEqual( 42, references.Callback() );
	}
}
