using System;
using System.Reflection;
using Sandbox.Engine;
using Sandbox.Internal;

namespace Sandbox.Tests;

[TestClass]
[DoNotParallelize]
public class ScriptSystemTests
{
	private GlobalContext previous;
	private GlobalContext context;

	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		var library = new TypeLibrary();
		library.AddIntrinsicTypes();
		library.AddAssembly( typeof( Vector3 ).Assembly, false );
		library.AddAssembly( typeof( Script ).Assembly, false );
		library.AddAssembly( typeof( ScriptSystemTests ).Assembly, false );
		GlobalContext.Current = context = new GlobalContext { TypeLibrary = library };
	}

	[TestCleanup]
	public void Cleanup() => GlobalContext.Current = previous;

	[Expose]
	public sealed class Host
	{
		public int Value { get; set; }
		public int ReadOnly { get; private set; } = 4;
		public int WriteOnly { private get; set; }
		public readonly int Field = 7;
		public int Read( int offset = 1 ) => Value + offset;
		public int Fail() => throw new InvalidOperationException( "Host failed" );
		private int Hidden() => 42;
		public Script Recursive { get; set; }
		public bool Reenter() => Recursive.Run();
		public string NestedError { get; private set; }
		public void Cascade()
		{
			var next = Game.Scripting.CreateScript( "Host.Cascade();" );
			next.SetArgument( "Host", this );
			if ( !next.Run() ) NestedError = next.LastError.Message;
		}
	}

	public sealed class UnexposedHost
	{
		public int Value => 99;
	}

	[TestMethod]
	public void InputsExpireOnSuccessAndFailure()
	{
		var script = Game.Scripting.CreateScript( "return Number * 2;" );
		script.SetArgument( "Number", 3 );
		Assert.AreEqual( 6, script.RunAndReturn<int>() );
		Assert.IsNull( script.LastError );
		Assert.AreEqual( 0, script.RunAndReturn<int>() );
		Assert.IsNotNull( script.LastError );
		script.SetArgument( "Number", 4L );
		Assert.AreEqual( 8L, script.RunAndReturn<long>() );

		var failure = Game.Scripting.CreateScript( "Host.Value++; Host.Fail();" );
		var host = new Host();
		failure.SetArgument( "Host", host );
		Assert.IsFalse( failure.Run() );
		Assert.AreEqual( "Host failed", failure.LastError.Message );
		Assert.AreEqual( 1, host.Value );
		Assert.IsFalse( failure.Run() );
		Assert.AreEqual( 1, host.Value );
	}

	[TestMethod]
	public void MissingReceiversReportBindingErrorsAndScriptWritesExpire()
	{
		var script = Game.Scripting.CreateScript( "if (Skip) return 7; return Player.Read(1);" );
		script.SetArgument( "Skip", true );
		// rc.1 needs receiver types before compiling even an untaken branch.
		Assert.IsFalse( script.Run() );
		StringAssert.Contains( script.LastError.Message, "Player" );
		script.SetArgument( "Skip", true );
		script.SetArgument<Host>( "Player", null );
		Assert.AreEqual( 7, script.RunAndReturn<int>(), script.LastError?.Message );
		script.SetArgument( "Skip", false );
		Assert.IsFalse( script.Run() );
		StringAssert.Contains( script.LastError.Message, "Player" );
		script.SetArgument( "Skip", false );
		script.SetArgument<Host>( "Player", null );
		Assert.IsFalse( script.Run() );

		var writes = Game.Scripting.CreateScript( "if (Write) X = 42; return X;" );
		writes.SetArgument( "Write", true );
		writes.SetArgument( "X", 0 );
		Assert.AreEqual( 42, writes.RunAndReturn<int>(), writes.LastError?.Message );
		writes.SetArgument( "Write", false );
		Assert.IsFalse( writes.Run() );
	}

	[TestMethod]
	public void ErrorsHaveSourceRangesAndReturnConversionFailures()
	{
		var script = Game.Scripting.CreateScript( "return Missing;" );
		Assert.IsFalse( script.Run() );
		Assert.AreEqual( 7, script.LastError.Start );
		Assert.AreEqual( 7, script.LastError.Length );
		Assert.AreEqual( script.LastError.Message, script.LastError.ToString() );
		script.SetArgument( "Missing", new Host() );
		Assert.AreEqual( 0, script.RunAndReturn<int>() );
		Assert.AreEqual( "SCR_RETURN_TYPE", script.LastError.Code );
		Assert.AreEqual( 0, Game.Scripting.CreateScript( "var local = 1;" ).RunAndReturn<int>() );
		Assert.ThrowsException<Script.ScriptException>( () => Game.Scripting.Run<int>( "return (;" ) );
		Assert.ThrowsException<Script.ScriptException>( () => Game.Scripting.Run<int>( "var local = 1;" ) );
		Assert.ThrowsException<InvalidCastException>( () => Game.Scripting.Run<int>( "return 1.0;" ) );
	}

	[TestMethod]
	public void LocalsLoopsOperatorsAndHostMetadataWork()
	{
		var script = Game.Scripting.CreateScript( "long total = 0; for (var i = 0; i < 10; i++) { if (i < 5) total += i; else total += 2; } return total;" );
		Assert.AreEqual( 20L, script.RunAndReturn<long>() );
		Assert.AreEqual( 20L, script.RunAndReturn<long>() );
		Assert.IsTrue( Game.Scripting.CreateScript( "return true || Missing.Read();" ).RunAndReturn<bool>() );
		Assert.IsFalse( Game.Scripting.CreateScript( "return false && Missing.Read();" ).RunAndReturn<bool>() );
		var vector = Game.Scripting.CreateScript( "return new Vector3(4, 5, 6) * 100;" );
		var result = vector.RunAndReturn<Vector3>();
		Assert.IsNull( vector.LastError, vector.LastError?.ToString() );
		Assert.AreEqual( new Vector3( 400, 500, 600 ), result );
		Assert.AreEqual( 10.0, Game.Scripting.Run<double>( "return Math.Cos(0) * 10;" ) );
		Assert.IsTrue( Game.Scripting.CreateScript( "return Time.Now + RealTime.Now;" ).Run() );
		var member = Game.Scripting.CreateScript( "return Player.Read(1);" );
		member.SetArgument( "Player", new Host { Value = 2 } );
		Assert.AreEqual( 3, member.RunAndReturn<int>(), member.LastError?.Message );
	}

	[TestMethod]
	[DataRow( "return new Vector3(4, 5, 6) * 100;" )]
	[DataRow( "return 100 * new Vector3(4, 5, 6);" )]
	[DataRow( "return new Vector3(4, 5, 6) * 100.0f;" )]
	public void VectorMultiplicationSelectsTheScalarOverload( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		Assert.IsTrue( script.Compile(), script.LastError?.Message );
		var result = script.RunAndReturn<Vector3>();
		Assert.IsNull( script.LastError, script.LastError?.Message );
		Assert.AreEqual( new Vector3( 400, 500, 600 ), result );
	}

	[TestMethod]
	public void PrivateMembersAccessorsAndUnregisteredTypesAreDenied()
	{
		foreach ( var source in new[] { "Host.Hidden();", "Host.ReadOnly = 10;", "return Host.WriteOnly;", "Host.Field = 8;" } )
		{
			var script = Game.Scripting.CreateScript( source );
			script.SetArgument( "Host", new Host() );
			Assert.IsFalse( script.Run(), source );
		}
		var denied = Game.Scripting.CreateScript( "return Target.Value;" );
		denied.SetArgument( "Target", new UnexposedHost() );
		Assert.IsFalse( denied.Run() );
		Assert.IsFalse( Game.Scripting.CreateScript( "return System.IO.File.ReadAllText(\"secret\");" ).Run() );
		Assert.IsFalse( Game.Scripting.CreateScript( "return new UnexposedHost();" ).Run() );
	}

	[TestMethod]
	public void ConstructedGenericInputsUseApprovedMembers()
	{
		var items = new System.Collections.Generic.List<int> { 2, 3 };
		var script = Game.Scripting.CreateScript( "Items.Add(4); return Items[0] + Items.Count;" );
		script.SetArgument( "Items", items );
		Assert.AreEqual( 5, script.RunAndReturn<int>(), script.LastError?.Message );
		Assert.AreEqual( 3, items.Count );
	}

	[TestMethod]
	public void InstancesAreIndependentAndHotloadInvalidatesPermissions()
	{
		var first = Game.Scripting.CreateScript( "return Host.Read(1);" );
		var second = Game.Scripting.CreateScript( "return Host.Read(1);" );
		first.SetArgument( "Host", new Host { Value = 10 } );
		second.SetArgument( "Host", new Host { Value = 20 } );
		Assert.AreEqual( 11, first.RunAndReturn<int>(), first.LastError?.Message );
		Assert.AreEqual( 21, second.RunAndReturn<int>() );
		context.OnHotload();
		first.SetArgument( "Host", new Host { Value = 30 } );
		Assert.AreEqual( 31, first.RunAndReturn<int>() );
		Assert.IsFalse( second.Run() );
		context.TypeLibrary.RemoveAssembly( typeof( Host ).Assembly );
		context.OnHotload();
		first.SetArgument( "Host", new Host() );
		Assert.IsFalse( first.Run() );
	}

	[TestMethod]
	public void OptionalArgumentsUseTheirDeclaredDefaults()
	{
		var script = Game.Scripting.CreateScript( "return Player.Read();" );
		script.SetArgument( "Player", new Host() );
		Assert.AreEqual( 1, script.RunAndReturn<int>() );
		Assert.IsNull( script.LastError );
	}

	[TestMethod]
	public void ContextsAreIsolatedAndOldScriptsDispose()
	{
		var script = Game.Scripting.CreateScript( "return 42;" );
		var other = new GlobalContext { TypeLibrary = context.TypeLibrary };
		GlobalContext.Current = other;
		Assert.IsFalse( script.Run() );
		Assert.AreNotSame( context.Scripting, other.Scripting );
		GlobalContext.Current = context;
		Assert.AreEqual( 42, script.RunAndReturn<int>() );
		context.TypeLibrary = new TypeLibrary();
		Assert.IsFalse( script.Run() );
		StringAssert.Contains( script.LastError.Message, "ended" );
		var fresh = Game.Scripting.CreateScript( "return 1;" );
		context.Shutdown();
		Assert.IsFalse( fresh.Run() );
	}

	[TestMethod]
	public void BudgetsAndReentryAreBounded()
	{
		var budget = Assert.ThrowsException<Script.ScriptException>( () => Game.Scripting.Run<int>( "while (true) { } return 0;" ) );
		Assert.AreEqual( "SCR_BUDGET", budget.Code );
		var host = new Host();
		host.Recursive = Game.Scripting.CreateScript( "return Host.Reenter();" );
		host.Recursive.SetArgument( "Host", host );
		Assert.IsFalse( host.Recursive.RunAndReturn<bool>() );
		var cascade = Game.Scripting.CreateScript( "Host.Cascade();" );
		cascade.SetArgument( "Host", host );
		Assert.IsTrue( cascade.Run() );
		StringAssert.Contains( host.NestedError, "depth limit" );
	}

	[TestMethod]
	public void DirectRunsPreserveContextAndReleaseDepthAfterFailure()
	{
		var system = Game.Scripting;
		for ( int i = 0; i < 40; i++ )
			Assert.ThrowsException<Script.ScriptException>( () => system.Run<int>( "return Missing;" ) );
		Assert.AreEqual( 100000, system.Run<int>( "var total = 0; for (var i = 0; i < 100000; i++) total++; return total;" ) );

		GlobalContext.Current = new GlobalContext { TypeLibrary = context.TypeLibrary };
		Assert.ThrowsException<InvalidOperationException>( () => system.Run<int>( "return 1;" ) );
		GlobalContext.Current = context;
		context.Shutdown();
		Assert.ThrowsException<InvalidOperationException>( () => system.Run<int>( "return 1;" ) );
	}

	[TestMethod]
	public void PublicApiDoesNotExposeBreen()
	{
		foreach ( var type in new[] { typeof( Script ), typeof( ScriptSystem ), typeof( Script.ScriptError ), typeof( Script.ScriptException ), typeof( ScriptInvocation<> ), typeof( ScriptInvocation<,> ), typeof( IScriptInvocation ) } )
		{
			AssertNoBreen( type );
			foreach ( var implemented in type.GetInterfaces() ) AssertNoBreen( implemented );
			foreach ( var method in type.GetMethods( BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly ) )
			{
				AssertNoBreen( method.ReturnType );
				foreach ( var argument in method.GetGenericArguments() ) AssertNoBreen( argument );
				foreach ( var parameter in method.GetParameters() )
					AssertNoBreen( parameter.ParameterType );
			}
		}
	}

	static void AssertNoBreen( Type type )
	{
		Assert.AreNotEqual( "Breen", type.Assembly.GetName().Name, type.ToString() );
		if ( type.HasElementType ) AssertNoBreen( type.GetElementType() );
		if ( type.IsGenericParameter )
		{
			foreach ( var constraint in type.GetGenericParameterConstraints() ) AssertNoBreen( constraint );
		}
		else
		{
			foreach ( var argument in type.GetGenericArguments() ) AssertNoBreen( argument );
		}
	}

	[TestMethod]
	public void ForeignThreadsCannotConsumeArguments()
	{
		var script = Game.Scripting.CreateScript( "return Number;" );
		script.SetArgument( "Number", 42 );
		Exception failure = null;
		var thread = new System.Threading.Thread( () =>
		{
			try { script.Run(); }
			catch ( Exception exception ) { failure = exception; }
		} );
		thread.Start();
		thread.Join();
		Assert.IsInstanceOfType<InvalidOperationException>( failure );
		Assert.AreEqual( 42, script.RunAndReturn<int>() );
	}

	[TestMethod]
	public void WarmedCallsIncludingArgumentExpiryAllocateNothing()
	{
		var script = Game.Scripting.CreateScript( "return Math.Sin(Number) * 10.0;" );
		for ( int i = 0; i < 10_000; i++ )
		{
			script.SetArgument( "Number", (double)i );
			script.RunAndReturn<double>();
		}
		long before = GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 10_000; i++ )
		{
			script.SetArgument( "Number", (double)i );
			script.RunAndReturn<double>();
		}
		Assert.AreEqual( 0L, GC.GetAllocatedBytesForCurrentThread() - before );
		Assert.IsNull( script.LastError );
	}
}
