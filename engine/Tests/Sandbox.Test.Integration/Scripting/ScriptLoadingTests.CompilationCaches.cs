using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	private static Assembly EmitCacheAssembly( int version )
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( "CacheSentinel" )
		{
			Version = new Version( version, 0 )
		}, AssemblyBuilderAccess.RunAndCollect );
		assembly.DefineDynamicModule( "CacheSentinel" ).DefineType( "CacheSentinel", TypeAttributes.Public ).CreateType();
		return assembly;
	}

	private Script cachedCommand;

	/// <summary>
	/// Stopping JSON warmup waits for metadata already being created, so it cannot refill a cleared hotload cache.
	/// </summary>
	[TestMethod]
	public void StoppingJsonWarmUpWaitsForInFlightMetadata()
	{
		Json.StopWarmUp();
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		using var finished = new ManualResetEventSlim();
		var resolver = new DefaultJsonTypeInfoResolver();
		resolver.Modifiers.Add( typeInfo =>
		{
			// PopulateReflectionCache resolves string on the calling thread before starting its worker.
			if ( typeInfo.Type == typeof( string ) ) return;

			entered.Set();
			if ( release.Wait( TimeSpan.FromSeconds( 10 ) ) )
			{
				finished.Set();
			}
		} );
		context.JsonSerializerOptions = new JsonSerializerOptions { TypeInfoResolver = resolver };

		try
		{
			Json.PopulateReflectionCache( Game.TypeLibrary );
			Assert.IsTrue( entered.Wait( TimeSpan.FromSeconds( 10 ) ), "JSON warmup did not start creating metadata." );
			using var cancellation = Json.WarmUpCts.Token.Register( release.Set );

			Json.StopWarmUp();

			Assert.IsTrue( finished.IsSet, "JSON warmup returned before its in-flight metadata creation finished." );
		}
		finally
		{
			release.Set();
			Json.StopWarmUp();
		}
	}

	/// <summary>
	/// An unrelated assembly reload must not rewrite generated compiler delegates into throwing stubs.
	/// File captures and commands remain usable, then rebind normally after host invalidation and source edits.
	/// </summary>
	[TestMethod]
	public void AssemblyHotloadPreservesCompilerCachesAndRuntimeCaptures()
	{
		LoadSource( "cache.csx", """
			int count = 10;
			int NextCount() { return ++count; }
			class CacheComponent : Component
			{
				[Property] public int Value { get; set; } = 7;
				public int Read() { return Value + NextCount(); }
			}
			""" );
		LoadSource( "captures.csx", """
			Component cacheHeld = null;
			void RememberCache(Component value) { cacheHeld = value; }
			Component RecallCache() { return cacheHeld; }
			""" );
		scene = new Scene();
		using var scope = scene.Push();
		var type = Game.TypeLibrary.GetType( "CacheComponent" );
		reloadRoots = [scene.CreateObject().Components.Create( type )];
		Game.Scripting.Globals.Set<Component>( "Retained", (Component)reloadRoots[0] );
		var remember = Game.Scripting.CreateScript( "RememberCache(item);" );
		Assert.IsTrue( remember.With( "item", (Component)reloadRoots[0] ).Run(), remember.LastError?.ToString() );
		cachedCommand = Game.Scripting.CreateScript( "[Global] Component Retained = null; return Retained.Read();" );
		Assert.AreEqual( 18, cachedCommand.RunAndReturn<int>(), cachedCommand.LastError?.ToString() );

		var warnings = new List<string>();
		void OnMessage( LogEvent entry )
		{
			if ( entry.Logger == "hotload/ScriptTest" && entry.Level >= LogLevel.Warn )
			{
				warnings.Add( entry.Message );
			}
		}

		using var hotload = new HotloadManager( "ScriptTest" );
		hotload.Watch( GetType().Assembly );
		var completed = false;
		hotload.OnSuccess += () =>
		{
			// Unchanged compiled plans must remain callable even before the normal invalidation hook.
			Assert.AreEqual( 19, cachedCommand.RunAndReturn<int>(), cachedCommand.LastError?.ToString() );
			context.OnHotload();
			completed = true;
		};

		Logging.PushQueuedMessages();
		Logging.OnMessage += OnMessage;
		try
		{
			hotload.Replace( EmitCacheAssembly( 1 ), EmitCacheAssembly( 2 ) );
			hotload.DoSwap();
			Logging.PushQueuedMessages();
		}
		finally
		{
			Logging.OnMessage -= OnMessage;
		}

		Assert.IsTrue( completed );
		Assert.AreEqual( 0, warnings.Count, string.Join( "\n", warnings ) );
		Assert.AreEqual( 20, cachedCommand.RunAndReturn<int>(), cachedCommand.LastError?.ToString() );

		var original = reloadRoots[0];
		type.SetValue( original, "Value", 21 );
		LoadSource( "cache.csx", """
			class CacheComponent : Component
			{
				[Property] public int Value { get; set; } = 7;
				public int Read() { return Value * 2; }
				[Property] public int Added { get; set; } = 8;
			}
			""" );
		Assert.AreNotSame( original, reloadRoots[0] );
		Assert.AreSame( reloadRoots[0], Game.Scripting.Globals.Get<Component>( "Retained" ) );
		Assert.AreSame( reloadRoots[0], Game.Scripting.Run<Component>( "return RecallCache();" ) );
		Assert.AreEqual( 8, type.GetValue( reloadRoots[0], "Added" ) );
		Assert.AreEqual( 42, cachedCommand.RunAndReturn<int>(), cachedCommand.LastError?.ToString() );
	}
}
