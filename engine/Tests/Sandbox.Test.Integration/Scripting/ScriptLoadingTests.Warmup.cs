using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	private const BindingFlags WarmupFields = BindingFlags.Instance | BindingFlags.NonPublic;
	private const string WarmupSource = "int calls = 0; int ReadCalls() { return calls; } int Next() { return ++calls; } class WarmupProbe { public static int Read() { return 1; } }";

	private static (Breen.ScriptModule Module, object Cache) CurrentWarmupCache()
	{
		var module = (Breen.ScriptModule)typeof( ScriptSystem ).GetProperty( "Module", WarmupFields ).GetValue( Game.Scripting );
		return (module, typeof( Breen.ScriptModule ).GetField( "compilationCache", WarmupFields ).GetValue( module ));
	}

	/// <summary>
	/// Both loading paths schedule background compilation without running component methods.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void LoadingWarmsMethodsWithoutExecutingThem( bool named )
	{
		if ( named )
		{
			var result = Game.Scripting.LoadFiles( new Dictionary<string, string> { ["warm.csx"] = WarmupSource } );
			Assert.IsTrue( result.Success, string.Join( "; ", result.Diagnostics ) );
		}
		else
		{
			Assert.IsTrue( Game.Scripting.Load( WarmupSource ), Game.Scripting.LastError?.ToString() );
		}
		var (_, cache) = CurrentWarmupCache();
		var warming = (Task)cache.GetType().GetField( "warmup", WarmupFields ).GetValue( cache );
		Assert.IsNotNull( warming, "Loading should schedule warmup without an extra host request." );
		Assert.IsTrue( warming.Wait( TimeSpan.FromSeconds( 10 ) ) );
		Assert.AreEqual( 0, Game.Scripting.Run<int>( "return ReadCalls();" ) );
		Assert.AreEqual( 1, Game.Scripting.Run<int>( "return Next();" ) );
		Assert.AreEqual( 1, Game.Scripting.Run<int>( "return WarmupProbe.Read();" ) );
	}

	/// <summary>
	/// A structural update drains the active compiler before entering native reference migration.
	/// </summary>
	[TestMethod]
	public void ScriptMigrationDrainsBackgroundCompilation()
	{
		var sources = new Dictionary<string, string> { ["warm.csx"] = WarmupSource };
		var loaded = Game.Scripting.LoadFiles( sources );
		Assert.IsTrue( loaded.Success, string.Join( "; ", loaded.Diagnostics ) );
		var (module, cache) = CurrentWarmupCache();
		module.StopWarmup();
		var bodies = (IDictionary)cache.GetType().GetField( "bodies", WarmupFields ).GetValue( cache );
		var body = bodies.Values.Cast<object>().First( value => (bool)value.GetType().GetProperty( "Warm" ).GetValue( value ) );
		var factoryProperty = body.GetType().GetProperty( "Factory" );
		var original = (Lazy<Func<object[], Delegate>>)factoryProperty.GetValue( body );
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		int completed = 0;
		factoryProperty.SetValue( body, new Lazy<Func<object[], Delegate>>( () =>
		{
			entered.Set();
			Assert.IsTrue( release.Wait( TimeSpan.FromSeconds( 10 ) ) );
			var factory = original.Value;
			Interlocked.Increment( ref completed );
			return factory;
		}, LazyThreadSafetyMode.ExecutionAndPublication ) );
		var warming = module.WarmupAsync();
		var cancellation = (CancellationTokenSource)cache.GetType().GetField( "warmupCancellation", WarmupFields ).GetValue( cache );
		var replace = context.ReplaceScriptAssembly;
		bool migrated = false;
		context.ReplaceScriptAssembly = ( oldAssembly, newAssembly, registered ) =>
		{
			Assert.AreEqual( 1, completed );
			Assert.IsTrue( warming.IsCompleted );
			migrated = true;
			replace( oldAssembly, newAssembly, registered );
		};
		try
		{
			Assert.IsTrue( entered.Wait( TimeSpan.FromSeconds( 10 ) ) );
			var releasing = Task.Run( () =>
			{
				try
				{
					Assert.IsTrue( cancellation.Token.WaitHandle.WaitOne( TimeSpan.FromSeconds( 10 ) ) );
				}
				finally
				{
					release.Set();
				}
			} );
			sources["warm.csx"] = WarmupSource.Replace( "class WarmupProbe {", "class WarmupProbe { public int Added;" );
			var result = Game.Scripting.LoadFiles( sources );
			releasing.GetAwaiter().GetResult();
			Assert.IsTrue( result.Success, string.Join( "; ", result.Diagnostics ) );
			Assert.IsTrue( migrated );
			Assert.AreEqual( 1, Game.Scripting.Run<int>( "return WarmupProbe.Read();" ) );
		}
		finally
		{
			release.Set();
			context.ReplaceScriptAssembly = replace;
			module.StopWarmup();
		}
	}
}
