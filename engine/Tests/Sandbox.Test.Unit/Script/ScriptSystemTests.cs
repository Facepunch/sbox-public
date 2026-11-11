using System;
using System.Reflection;
using Sandbox.Engine;
using Sandbox.Internal;

namespace Sandbox.Tests;

/// <summary>
/// Verifies script execution, host API restrictions, and context lifetimes.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ScriptSystemTests
{
	private GlobalContext previous;
	private GlobalContext context;

	// Scanning the assemblies is slow, so tests share one library unless they mutate it
	static readonly Lazy<TypeLibrary> SharedLibrary = new( CreateLibrary );

	static TypeLibrary CreateLibrary()
	{
		var library = new TypeLibrary();
		library.AddIntrinsicTypes();
		library.AddAssembly( typeof( Vector3 ).Assembly, false );
		library.AddAssembly( typeof( Script ).Assembly, false );
		library.AddAssembly( typeof( ScriptSystemTests ).Assembly, false );
		return library;
	}

	/// <summary>
	/// Swap in a private library for tests that add or remove assemblies.
	/// </summary>
	private void UseOwnLibrary() => GlobalContext.Current = context = new GlobalContext { TypeLibrary = CreateLibrary() };

	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		GlobalContext.Current = context = new GlobalContext { TypeLibrary = SharedLibrary.Value };
	}

	/// <summary>
	/// Menu code cannot create a scripting runtime, even through direct context access.
	/// </summary>
	[TestMethod]
	public void MenuCannotLoadScripts()
	{
		var previousMenu = GlobalContext.Menu;
		try
		{
			GlobalContext.Menu = context;
			var error = Assert.ThrowsException<InvalidOperationException>( () => context.Scripting.Load( "return 1;" ) );
			StringAssert.Contains( error.Message, "Scripting is unavailable in the menu" );
		}
		finally
		{
			GlobalContext.Menu = previousMenu;
		}
	}

	/// <summary>
	/// Releases the async test host and restores the previous game context.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		AsyncHost.Current = null;
		GlobalContext.Current = previous;
	}

	/// <summary>
	/// Familiar builder names construct the same safe wrapper used by compiled addon C#.
	/// </summary>
	[TestMethod]
	[DataRow( "var sb = new System.Text.StringBuilder();" )]
	[DataRow( "var sb = new StringBuilder();" )]
	[DataRow( "StringBuilder sb = new StringBuilder();" )]
	[DataRow( "System.Text.StringBuilder sb = new();" )]
	public void StringBuilderNamesUseSafeWrapper( string declaration )
	{
		var script = context.Scripting.CreateScript( declaration + " return sb.Append(\"value=\").Append(42).AppendLine(\"!\").AppendLine();" );
		var result = script.RunAndReturn<object>();

		Assert.IsNull( script.LastError, script.LastError?.Message );
		Assert.IsInstanceOfType<SafeStringBuilder>( result );
		Assert.AreEqual( "value=42!" + Environment.NewLine + Environment.NewLine, result.ToString() );
	}

	/// <summary>
	/// Every wrapper constructor and both string conversion overloads remain usable from scripts.
	/// </summary>
	[TestMethod]
	[DataRow( "", "" )]
	[DataRow( "16", "" )]
	[DataRow( "16, 64", "" )]
	[DataRow( "\"seed\"", "seed" )]
	[DataRow( "\"seed\", 16", "seed" )]
	[DataRow( "\"!seed!\", 1, 4, 16", "seed" )]
	public void StringBuilderConstructorsAndFormatting( string arguments, string initial )
	{
		var script = context.Scripting.CreateScript( $$"""
			var sb = new System.Text.StringBuilder({{arguments}});
			sb.Append("!");
			return sb.ToString() + sb.ToString(0, {{initial.Length}});
			""" );

		Assert.AreEqual( initial + "!" + initial, script.RunAndReturn<string>(), script.LastError?.Message );
		Assert.IsNull( script.LastError );
	}

	/// <summary>
	/// Builder support does not expose its implementation or reflection.
	/// </summary>
	[TestMethod]
	[DataRow( "return new StringBuilder().GetType();" )]
	[DataRow( "return new StringBuilder()._inner;" )]
	[DataRow( "return raw.Append(\"unsafe\");" )]
	public void StringBuilderImplementationRemainsHidden( string source )
	{
		var script = context.Scripting.CreateScript( source );
		if ( source.Contains( "raw" ) )
		{
			script.SetArgument( "raw", new System.Text.StringBuilder() );
		}

		Assert.IsFalse( script.Run(), source );
		Assert.IsNotNull( script.LastError );
	}

	/// <summary>
	/// Only the registered wrapper is available, through the ordinary catalog member filters.
	/// </summary>
	[TestMethod]
	public void StringBuilderCatalogPreservesRestrictions()
	{
		var resolver = new TypeLibraryScriptResolver( SharedLibrary.Value );
		Assert.AreSame( typeof( SafeStringBuilder ), resolver.ResolveType( "System.Text.StringBuilder" ) );
		Assert.AreSame( typeof( SafeStringBuilder ), resolver.ResolveType( "StringBuilder" ) );
		Assert.IsNull( SharedLibrary.Value.GetType( typeof( System.Text.StringBuilder ) ) );
		Assert.IsNull( resolver.ResolveType( "System.Text.Encoding" ) );

		var members = new System.Collections.Generic.List<MemberInfo>();
		resolver.GetMembers( typeof( SafeStringBuilder ), members );
		Assert.AreEqual( 6, members.OfType<ConstructorInfo>().Count() );
		Assert.AreEqual( 2, members.OfType<MethodInfo>().Count( method => method.Name == "ToString" ) );
		Assert.IsFalse( members.OfType<MethodBase>().Any( method => method.GetParameters().Any( parameter => parameter.ParameterType.IsByRefLike ) ) );
		Assert.IsFalse( members.Any( member => member.Name is "GetType" or "_inner" or "_syncLock" ) );
	}

	/// <summary>
	/// Builder aliases follow catalog registration and do not hide a user's own StringBuilder type.
	/// </summary>
	[TestMethod]
	public void StringBuilderAliasesFollowCatalogAndNamePrecedence()
	{
		var library = new TypeLibrary();
		try
		{
			var resolver = new TypeLibraryScriptResolver( library );
			Assert.IsNull( resolver.ResolveType( "StringBuilder" ) );
			Assert.IsNull( resolver.ResolveType( "System.Text.StringBuilder" ) );

			library.AddAssembly( typeof( SafeStringBuilder ).Assembly, false );
			Assert.AreSame( typeof( SafeStringBuilder ), resolver.ResolveType( "StringBuilder" ) );
			Assert.AreSame( typeof( SafeStringBuilder ), resolver.ResolveType( "System.Text.StringBuilder" ) );

			var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
				new AssemblyName( "ScriptBuilderPrecedence" ), System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect );
			var userType = assembly.DefineDynamicModule( "Types" ).DefineType( "User.StringBuilder", TypeAttributes.Public ).CreateType();
			resolver.IncomingAssemblies = [assembly];
			Assert.AreSame( userType, resolver.ResolveType( "StringBuilder" ) );
			Assert.AreSame( typeof( SafeStringBuilder ), resolver.ResolveType( "System.Text.StringBuilder" ) );

			resolver.IncomingAssemblies = [];
			library.RemoveAssembly( typeof( SafeStringBuilder ).Assembly );
			Assert.IsNull( resolver.ResolveType( "StringBuilder" ) );
			Assert.IsNull( resolver.ResolveType( "System.Text.StringBuilder" ) );
		}
		finally
		{
			library.Dispose();
		}
	}

	/// <summary>
	/// Compatibility names must not shadow existing aliases, including aliases changed after lookup.
	/// </summary>
	[TestMethod]
	public void StringBuilderCompatibilityNamesRespectLiveAliases()
	{
		var library = new TypeLibrary();
		try
		{
			library.AddAssembly( typeof( SafeStringBuilder ).Assembly, false );
			var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
				new AssemblyName( "ScriptBuilderAliases" ), System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect );
			var builder = assembly.DefineDynamicModule( "Types" ).DefineType( "User.CustomBuilder", TypeAttributes.Public );
			builder.SetCustomAttribute( new System.Reflection.Emit.CustomAttributeBuilder(
				typeof( AliasAttribute ).GetConstructor( [typeof( string[] )] ), [new[] { "StringBuilder", "System.Text.StringBuilder" }] ) );
			var userType = builder.CreateType();
			var resolver = new TypeLibraryScriptResolver( library );
			Assert.AreSame( typeof( SafeStringBuilder ), resolver.ResolveType( "StringBuilder" ) );

			library.AddAssembly( assembly, true );
			Assert.AreSame( userType, resolver.ResolveType( "StringBuilder" ) );
			Assert.AreSame( userType, resolver.ResolveType( "System.Text.StringBuilder" ) );

			library.GetType( userType ).Aliases[0] = "RenamedBuilder";
			Assert.AreSame( typeof( SafeStringBuilder ), resolver.ResolveType( "StringBuilder" ) );
			Assert.AreSame( userType, resolver.ResolveType( "RenamedBuilder" ) );
			Assert.AreSame( userType, resolver.ResolveType( "System.Text.StringBuilder" ) );
		}
		finally
		{
			library.Dispose();
		}
	}

	/// <summary>
	/// The shared catalog also retains ActionGraph's existing ref-struct parameter restrictions.
	/// </summary>
	[TestMethod]
	public void StringBuilderActionGraphMembersExcludeRefStructs()
	{
		var loader = new Sandbox.ActionGraphs.TypeLoader( () => SharedLibrary.Value );
		foreach ( var name in new[] { "Append", "AppendLine", "Insert", "CopyTo" } )
		{
			var methods = loader.GetMethods( typeof( SafeStringBuilder ), name );
			Assert.IsTrue( methods.Count > 0, name );
			Assert.IsFalse( methods.Any( method => method.GetParameters().Any( parameter => parameter.ParameterType.IsByRefLike ) ), name );
		}
	}

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
			var next = GlobalContext.Current.Scripting.CreateScript( "Host.Cascade();" );
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
		var script = GlobalContext.Current.Scripting.CreateScript( "return Number * 2;" );
		script.SetArgument( "Number", 3 );
		Assert.AreEqual( 6, script.RunAndReturn<int>() );
		Assert.IsNull( script.LastError );
		Assert.AreEqual( 0, script.RunAndReturn<int>() );
		Assert.IsNotNull( script.LastError );
		script.SetArgument( "Number", 4L );
		Assert.AreEqual( 8L, script.RunAndReturn<long>() );

		var failure = GlobalContext.Current.Scripting.CreateScript( "Host.Value++; Host.Fail();" );
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
		var script = GlobalContext.Current.Scripting.CreateScript( "if (Skip) return 7; return Player.Read(1);" );
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

		var writes = GlobalContext.Current.Scripting.CreateScript( "if (Write) X = 42; return X;" );
		writes.SetArgument( "Write", true );
		writes.SetArgument( "X", 0 );
		Assert.AreEqual( 42, writes.RunAndReturn<int>(), writes.LastError?.Message );
		writes.SetArgument( "Write", false );
		Assert.IsFalse( writes.Run() );
	}

	[TestMethod]
	public void ErrorsHaveSourceRangesAndReturnConversionFailures()
	{
		var script = GlobalContext.Current.Scripting.CreateScript( "return Missing;" );
		Assert.IsFalse( script.Run() );
		Assert.AreEqual( 7, script.LastError.Start );
		Assert.AreEqual( 7, script.LastError.Length );
		Assert.AreEqual( script.LastError.Message, script.LastError.ToString() );
		script.SetArgument( "Missing", new Host() );
		Assert.AreEqual( 0, script.RunAndReturn<int>() );
		Assert.AreEqual( "SCR_RETURN_TYPE", script.LastError.Code );
		Assert.AreEqual( 0, GlobalContext.Current.Scripting.CreateScript( "var local = 1;" ).RunAndReturn<int>() );
		Assert.ThrowsException<Script.ScriptException>( () => GlobalContext.Current.Scripting.Run<int>( "return (;" ) );
		Assert.ThrowsException<Script.ScriptException>( () => GlobalContext.Current.Scripting.Run<int>( "var local = 1;" ) );
		Assert.ThrowsException<InvalidCastException>( () => GlobalContext.Current.Scripting.Run<int>( "return 1.0;" ) );
	}

	[TestMethod]
	public void LocalsLoopsOperatorsAndHostMetadataWork()
	{
		var script = GlobalContext.Current.Scripting.CreateScript( "long total = 0; for (var i = 0; i < 10; i++) { if (i < 5) total += i; else total += 2; } return total;" );
		Assert.AreEqual( 20L, script.RunAndReturn<long>() );
		Assert.AreEqual( 20L, script.RunAndReturn<long>() );
		Assert.IsTrue( GlobalContext.Current.Scripting.CreateScript( "return true || Missing.Read();" ).RunAndReturn<bool>() );
		Assert.IsFalse( GlobalContext.Current.Scripting.CreateScript( "return false && Missing.Read();" ).RunAndReturn<bool>() );
		var vector = GlobalContext.Current.Scripting.CreateScript( "return new Vector3(4, 5, 6) * 100;" );
		var result = vector.RunAndReturn<Vector3>();
		Assert.IsNull( vector.LastError, vector.LastError?.ToString() );
		Assert.AreEqual( new Vector3( 400, 500, 600 ), result );
		Assert.AreEqual( 10.0, GlobalContext.Current.Scripting.Run<double>( "return Math.Cos(0) * 10;" ) );
		Assert.IsTrue( GlobalContext.Current.Scripting.CreateScript( "return Time.Now + RealTime.Now;" ).Run() );
		var member = GlobalContext.Current.Scripting.CreateScript( "return Player.Read(1);" );
		member.SetArgument( "Player", new Host { Value = 2 } );
		Assert.AreEqual( 3, member.RunAndReturn<int>(), member.LastError?.Message );
	}

	[TestMethod]
	[DataRow( "return new Vector3(4, 5, 6) * 100;" )]
	[DataRow( "return 100 * new Vector3(4, 5, 6);" )]
	[DataRow( "return new Vector3(4, 5, 6) * 100.0f;" )]
	public void VectorMultiplicationSelectsTheScalarOverload( string source )
	{
		var script = GlobalContext.Current.Scripting.CreateScript( source );
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
			var script = GlobalContext.Current.Scripting.CreateScript( source );
			script.SetArgument( "Host", new Host() );
			Assert.IsFalse( script.Run(), source );
		}
		var denied = GlobalContext.Current.Scripting.CreateScript( "return Target.Value;" );
		denied.SetArgument( "Target", new UnexposedHost() );
		Assert.IsFalse( denied.Run() );
		Assert.IsFalse( GlobalContext.Current.Scripting.CreateScript( "return System.IO.File.ReadAllText(\"secret\");" ).Run() );
		Assert.IsFalse( GlobalContext.Current.Scripting.CreateScript( "return new UnexposedHost();" ).Run() );
	}

	[TestMethod]
	public void ConstructedGenericInputsUseApprovedMembers()
	{
		var items = new System.Collections.Generic.List<int> { 2, 3 };
		var script = GlobalContext.Current.Scripting.CreateScript( "Items.Add(4); return Items[0] + Items.Count;" );
		script.SetArgument( "Items", items );
		Assert.AreEqual( 5, script.RunAndReturn<int>(), script.LastError?.Message );
		Assert.AreEqual( 3, items.Count );
	}

	[TestMethod]
	public void InstancesAreIndependentAndHotloadInvalidatesPermissions()
	{
		UseOwnLibrary();
		var first = GlobalContext.Current.Scripting.CreateScript( "return Host.Read(1);" );
		var second = GlobalContext.Current.Scripting.CreateScript( "return Host.Read(1);" );
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
		var script = GlobalContext.Current.Scripting.CreateScript( "return Player.Read();" );
		script.SetArgument( "Player", new Host() );
		Assert.AreEqual( 1, script.RunAndReturn<int>() );
		Assert.IsNull( script.LastError );
	}

	[TestMethod]
	public void ContextsAreIsolatedAndOldScriptsDispose()
	{
		var script = GlobalContext.Current.Scripting.CreateScript( "return 42;" );
		var other = new GlobalContext { TypeLibrary = context.TypeLibrary };
		GlobalContext.Current = other;
		Assert.IsFalse( script.Run() );
		Assert.AreNotSame( context.Scripting, other.Scripting );
		GlobalContext.Current = context;
		Assert.AreEqual( 42, script.RunAndReturn<int>() );
		context.TypeLibrary = new TypeLibrary();
		Assert.IsFalse( script.Run() );
		StringAssert.Contains( script.LastError.Message, "ended" );
		var fresh = GlobalContext.Current.Scripting.CreateScript( "return 1;" );
		context.Shutdown();
		Assert.IsFalse( fresh.Run() );
	}

	[TestMethod]
	public void BudgetsAndReentryAreBounded()
	{
		var budget = Assert.ThrowsException<Script.ScriptException>( () => GlobalContext.Current.Scripting.Run<int>( "while (true) { } return 0;" ) );
		Assert.AreEqual( "SCR_BUDGET", budget.Code );
		var host = new Host();
		host.Recursive = GlobalContext.Current.Scripting.CreateScript( "return Host.Reenter();" );
		host.Recursive.SetArgument( "Host", host );
		Assert.IsFalse( host.Recursive.RunAndReturn<bool>() );
		var cascade = GlobalContext.Current.Scripting.CreateScript( "Host.Cascade();" );
		cascade.SetArgument( "Host", host );
		Assert.IsTrue( cascade.Run() );
		StringAssert.Contains( host.NestedError, "depth limit" );
	}

	[TestMethod]
	public void DirectRunsPreserveContextAndReleaseDepthAfterFailure()
	{
		var system = GlobalContext.Current.Scripting;
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

	/// <summary>
	/// Supplies a controlled asynchronous operation and records where its script resumes.
	/// </summary>
	[Expose]
	public sealed class AsyncHost
	{
		/// <summary>
		/// Durable host input for named script functions, independent of temporary invocation arguments.
		/// </summary>
		public static AsyncHost Current { get; set; }

		internal readonly TaskCompletionSource<int> Completion = new();
		internal GlobalContext ResumedContext;
		internal int ResumedThread;

		/// <summary>
		/// Suspends until the test completes the host operation.
		/// </summary>
		public Task<int> Wait() => Completion.Task;

		/// <summary>
		/// Records the script's continuation context before returning its result.
		/// </summary>
		public int Finish( int value )
		{
			ResumedContext = GlobalContext.Current;
			ResumedThread = Environment.CurrentManagedThreadId;
			return value + 1;
		}
	}

	private Task<int> StartAsyncScript( AsyncHost host )
	{
		AsyncHost.Current = host;
		var task = context.Scripting.Run<Task<int>>( "async Task<int> Read() { var host = AsyncHost.Current; var value = await host.Wait(); return host.Finish(value); } return Read();" );
		Assert.IsNotNull( task );
		return task;
	}

	/// <summary>
	/// A worker completing a host task only queues work; the owning context resumes it on its tick.
	/// </summary>
	[TestMethod]
	public void AsyncScriptsResumeOnTheirOwningContextTick()
	{
		var host = new AsyncHost();
		var task = StartAsyncScript( host );
		var worker = new System.Threading.Thread( () => host.Completion.SetResult( 41 ) );
		worker.Start();
		worker.Join();
		Assert.IsFalse( task.IsCompleted );

		var other = new GlobalContext { TypeLibrary = context.TypeLibrary };
		using ( new GlobalContext.GlobalContextScope( other ) )
		{
			other.TickScripting();
			Assert.IsFalse( task.IsCompleted );
		}

		context.TickScripting();
		Assert.IsTrue( task.IsCompletedSuccessfully );
		Assert.AreEqual( 42, task.Result );
		Assert.AreSame( context, host.ResumedContext );
		Assert.AreEqual( Environment.CurrentManagedThreadId, host.ResumedThread );
	}

	/// <summary>
	/// Failed and cancelled host operations complete the script task without running the rest of its body.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void AsyncScriptsPropagateHostFailure( bool cancelled )
	{
		var host = new AsyncHost();
		var task = StartAsyncScript( host );
		if ( cancelled )
		{
			host.Completion.SetCanceled();
		}
		else
		{
			host.Completion.SetException( new InvalidOperationException( "Host failed" ) );
		}

		context.TickScripting();
		Assert.IsTrue( task.IsCompleted );
		Assert.AreEqual( cancelled, task.IsCanceled );
		Assert.AreEqual( !cancelled, task.IsFaulted );
		if ( !cancelled ) StringAssert.Contains( task.Exception.ToString(), "Host failed" );
		Assert.IsNull( host.ResumedContext );
	}

	/// <summary>
	/// Hotload and context teardown prevent suspended scripts from executing stale code.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void AsyncScriptsStopWhenTheirOwnerChanges( bool shutdown )
	{
		var host = new AsyncHost();
		var task = StartAsyncScript( host );
		if ( shutdown )
		{
			context.Shutdown();
		}
		else
		{
			context.Scripting.InvalidateBindings();
		}

		host.Completion.SetResult( 41 );
		context.TickScripting();
		Assert.IsTrue( task.IsCompleted );
		Assert.IsFalse( task.IsCompletedSuccessfully );
		_ = task.Exception;
		Assert.IsNull( host.ResumedContext );
	}

	/// <summary>
	/// Task-returning functions without a result also resume, and each frame processes only a bounded batch.
	/// </summary>
	[TestMethod]
	public void AsyncScriptsWithoutResultsAreBatchedAcrossTicks()
	{
		var hosts = Enumerable.Range( 0, 101 ).Select( _ => new AsyncHost() ).ToArray();
		var tasks = hosts.Select( host =>
		{
			AsyncHost.Current = host;
			return context.Scripting.Run<Task>( "async System.Threading.Tasks.Task Read() { await AsyncHost.Current.Wait(); } return Read();" );
		} ).ToArray();

		// A shared task can dispatch its extra awaiters through the ThreadPool. Each completion here
		// has one awaiter, so all script continuations are queued before the first tick.
		foreach ( var host in hosts )
		{
			host.Completion.SetResult( 1 );
		}

		context.TickScripting();
		Assert.AreEqual( 100, tasks.Count( task => task.IsCompletedSuccessfully ) );
		context.TickScripting();
		Assert.IsTrue( tasks.All( task => task.IsCompletedSuccessfully ) );
	}

	/// <summary>
	/// Task declaration names do not expose scheduling methods or constructors outside the approved catalog.
	/// </summary>
	[TestMethod]
	public void AsyncTaskNamesPreserveMemberRestrictions()
	{
		var resolver = new TypeLibraryScriptResolver( context.TypeLibrary );
		Assert.AreEqual( typeof( Task ), resolver.ResolveType( "Task" ) );
		Assert.AreEqual( typeof( Task ), resolver.ResolveType( "System.Threading.Tasks.Task" ) );
		Assert.AreEqual( typeof( Task<> ), resolver.ResolveType( "Task`1" ) );
		Assert.AreEqual( typeof( Task<> ), resolver.ResolveType( "System.Threading.Tasks.Task`1" ) );
		var members = new System.Collections.Generic.List<MemberInfo>();
		resolver.GetMembers( typeof( Task ), members );
		Assert.IsFalse( members.Any( member => member is ConstructorInfo || member.Name is "Run" or "Start" or "Wait" ) );
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
		var script = GlobalContext.Current.Scripting.CreateScript( "return Number;" );
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

	[TestMethod, TestCategory( Allocations.Category )]
	public void WarmedCallsIncludingArgumentExpiryAllocateNothing()
	{
		var script = GlobalContext.Current.Scripting.CreateScript( "return Math.Sin(Number) * 10.0;" );
		Allocations.AssertSettles( "10,000 calls", () =>
		{
			for ( int i = 0; i < 10_000; i++ )
			{
				script.SetArgument( "Number", (double)i );
				script.RunAndReturn<double>();
			}
		} );
		Assert.IsNull( script.LastError );
	}
}
