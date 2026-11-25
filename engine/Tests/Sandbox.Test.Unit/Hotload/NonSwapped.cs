using CompilingTests.CodeAccess;
using Mono.Cecil;
using Sandbox.Upgraders;
using Sandbox.Utility;
using System;
using System.Collections.Generic;
using System.Threading;

namespace HotloadTests;

/// <summary>
/// Swapped instances referenced from an assembly that isn't swapped, like this one. Each test keeps its roots in
/// its own static class and only watches that, so the tests can still run in parallel. Tests clear their roots
/// when they finish, so they don't keep old fixture assemblies alive for the rest of the run.
/// </summary>
[TestClass]
public class NonSwappedTest : HotloadFixtureTestBase
{
	[DisableAutoCreation]
	private sealed class StartHook( Action start ) : SkipUpgrader
	{
		/// <summary>
		/// Only the start hook participates in this test.
		/// </summary>
		public override bool ShouldProcessType( Type type ) => false;

		protected override void OnHotloadStart() => start();
	}

	/// <summary>
	/// A start hook that fails once leaves the outgoing roots in place for the next attempt.
	/// </summary>
	[TestMethod]
	public async Task StartFailureCanRetryQueuedSwap()
	{
		using var fixture = await CreateFixtureAsync( "public class Marker : IHotloadTestFixture;" );
		await fixture.BuildNewAsync();
		var hotload = fixture.CreateHotload();
		hotload.WatchAssembly( fixture.OldAssembly );
		var starts = 0;
		hotload.AddUpgrader( new StartHook( () =>
		{
			if ( ++starts == 1 ) throw new InvalidOperationException( "First start failed" );
		} ) );

		Assert.ThrowsException<InvalidOperationException>( () => hotload.UpdateReferences() );
		Assert.IsTrue( hotload.WatchedAssemblies.Contains( fixture.OldAssembly ) );
		Assert.AreSame( fixture.NewAssembly, hotload.GetQueuedAssemblyReplacements()[fixture.OldAssembly] );

		var result = hotload.UpdateReferences();

		Assert.IsTrue( result.Success );
		Assert.AreSame( fixture.NewAssembly, fixture.Instance.GetType().Assembly );
		Assert.IsTrue( hotload.WatchedAssemblies.Contains( fixture.NewAssembly ) );
		Assert.IsFalse( hotload.WatchedAssemblies.Contains( fixture.OldAssembly ) );
	}

	private static class ObjectFieldRoots
	{
		public static object ObjectField;
	}

	[TestMethod]
	public async Task ObjectFieldInNonSwappedType()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			public class ObjectFieldInNonSwappedType : IHotloadTestFixture;
			""" );

		try
		{
			ObjectFieldRoots.ObjectField = fixture.OldInstance;

			await fixture.HotloadAsync( configure: x => WatchStatics( x, typeof( ObjectFieldRoots ) ) );

			Assert.AreSame( fixture.Instance, ObjectFieldRoots.ObjectField );
		}
		finally
		{
			ObjectFieldRoots.ObjectField = null;
		}
	}

	private static class StaticEventRoots
	{
		public static event Action ExampleEvent;

		public static Action Handlers => ExampleEvent;

		public static void Add( Action handler ) => ExampleEvent += handler;

		public static void Clear() => ExampleEvent = null;
	}

	/// <summary>
	/// Static delegate fields / events in non-swapped types must have their values replaced.
	/// </summary>
	[TestMethod]
	public async Task StaticEventInNonSwappedType()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			public class StaticEventInNonSwappedType : IHotloadTestFixture;

			public static class StaticHandlerClass
			{
				public static void Handler()
				{
					System.Console.WriteLine( "Hello, world!" );
				}
			}
			""" );

		try
		{
			StaticEventRoots.Add( fixture.GetType( "StaticHandlerClass" ).GetMethod( "Handler" )!.CreateDelegate<Action>() );

			Assert.AreEqual( fixture.OldAssembly, StaticEventRoots.Handlers?.Method.DeclaringType?.Assembly );

			await fixture.HotloadAsync( configure: x => WatchStatics( x, typeof( StaticEventRoots ) ) );

			Assert.AreEqual( fixture.GetType( "StaticHandlerClass" ), StaticEventRoots.Handlers?.Method.DeclaringType );
		}
		finally
		{
			StaticEventRoots.Clear();
		}
	}

	private static class ReadonlyHashSetRoots
	{
		public static readonly HashSet<object> InitOnlyHashSet = new();
	}

	[TestMethod]
	public async Task ReadonlyHashSetField()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			public class ReadonlyHashSetField : IHotloadTestFixture;
			""" );

		try
		{
			ReadonlyHashSetRoots.InitOnlyHashSet.Add( "Hello" );
			ReadonlyHashSetRoots.InitOnlyHashSet.Add( "World" );

			await fixture.HotloadAsync( configure: x => WatchStatics( x, typeof( ReadonlyHashSetRoots ) ) );

			Assert.AreEqual( 2, ReadonlyHashSetRoots.InitOnlyHashSet.Count );
		}
		finally
		{
			ReadonlyHashSetRoots.InitOnlyHashSet.Clear();
		}
	}

	private static class HashSetExRoots
	{
		public static HashSetEx<object> HashSetEx;
	}

	/// <summary>
	/// HashSetEx must gracefully handle items being removed during hotload
	/// because their types don't exist in the new assembly.
	/// </summary>
	[TestMethod]
	public async Task HashSetExRemovedItem()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			public class HashSetExRemovedItem : IHotloadTestFixture;

			-public class RemovedClass;
			+public class AddedClass;
			""" );

		try
		{
			HashSetExRoots.HashSetEx = new HashSetEx<object>();
			HashSetExRoots.HashSetEx.Add( Activator.CreateInstance( fixture.GetType( "RemovedClass" ) ) );
			HashSetExRoots.HashSetEx.Add( new object() );

			Assert.AreEqual( 2, HashSetExRoots.HashSetEx.Count );
			Assert.AreEqual( 2, HashSetExRoots.HashSetEx.List.Count );

			await fixture.HotloadAsync( configure: x => WatchStatics( x, typeof( HashSetExRoots ) ) );

			Assert.AreEqual( 1, HashSetExRoots.HashSetEx.Count );
			Assert.AreEqual( 1, HashSetExRoots.HashSetEx.List.Count );
		}
		finally
		{
			HashSetExRoots.HashSetEx = null;
		}
	}

	/// <summary>
	/// Test lambdas declared in a non-swapped assembly. These should be preserved.
	/// </summary>
	[TestMethod]
	public async Task NonSwappedLambda()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			public class NonSwappedLambda : IHotloadTestFixture
			{
				public System.Action ActionSimple;
			}
			""" );

		var testString = "";
		Action action = () => testString = "Action Simple!";

		SetField( fixture.OldInstance, "ActionSimple", action );

		await fixture.HotloadAsync();

		// Delegate was created in a non-hotloaded assembly, so should be the same instance after

		Assert.AreSame( action, GetField( fixture.Instance, "ActionSimple" ) );
		Assert.AreEqual( "", testString );

		((Action)GetField( fixture.Instance, "ActionSimple" ))();

		Assert.AreEqual( "Action Simple!", testString );
	}

	/// <summary>
	/// Tests a lambda defined in a non-swapped assembly that captures a delegate to a swapped method.
	/// </summary>
	[TestMethod]
	public async Task NonSwappedLambdaCapturingMethodGroup()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			using System;

			public class NonSwappedLambdaCapturingMethodGroup : IHotloadTestFixture
			{
				public Action Action;
				public int IntValue;

				public void TestMethod()
				{
			-		IntValue = 1;
			+		IntValue = 2;
				}

				void IHotloadTestFixture.Test()
				{
					Assert.AreEqual( 0, IntValue );
			+
			+		Action();
			+
			+		Assert.AreEqual( 2, IntValue );
				}
			}
			""" );

		var action = (Action)Delegate.CreateDelegate( typeof( Action ), fixture.OldInstance, "TestMethod" );

		SetField( fixture.OldInstance, "Action", (Action)(() => action()) );

		await fixture.HotloadAsync();

		Assert.AreEqual( 0, GetField( fixture.OldInstance, "IntValue" ) );
	}

	private static class PackageResolverRoots
	{
		/// <summary>
		/// Stands in for <c>PackageManager.AssemblyResolver</c>: a static the hotload walks while it resolves through it.
		/// </summary>
		public static PackageAssemblyResolver Resolver;
	}

	/// <summary>
	/// A package assembly added before a hotload is only parsed when the hotload first asks for it. That has to
	/// work mid-swap, while the hotload is also walking the resolver's own fields.
	/// </summary>
	[TestMethod]
	public async Task QueuedPackageAssembly_ResolvesDuringHotload()
	{
		const string gameName = "package.hotload.game";

		// A lambda from the swapped assembly makes the hotload read its Cecil definitions

		using var fixture = await CreateFixtureAsync(
			"""
			using System;

			public class QueuedPackageAssembly : IHotloadTestFixture
			{
				public int IntField;
				public Action ActionSimple;

				public Action CreateAction( int value )
				{
					return () => IntField = value;
				}

				void IHotloadTestFixture.Test()
				{
			-		ActionSimple = CreateAction( 50 );
			+		Assert.IsNotNull( ActionSimple );
				}
			}
			""" );

		using var resolver = PackageResolverRoots.Resolver = new PackageAssemblyResolver();

		try
		{
			resolver.AddAssembly( PackageAssemblyResolverTest.Compile( gameName, "public sealed class Game { }" ), gameName );

			ResolveAlongside resolveAlongside = null;

			await fixture.HotloadAsync( configure: hotload =>
			{
				WatchStatics( hotload, typeof( PackageResolverRoots ) );

				resolveAlongside = new ResolveAlongside( hotload.AssemblyResolver,
					() => resolver.Resolve( new AssemblyNameReference( gameName, new Version( 0, 0, 0, 0 ) ) ) );

				hotload.AssemblyResolver = resolveAlongside;
			} );

			Assert.IsTrue( resolveAlongside.Calls > 0, "Hotload never resolved anything, so this tests nothing" );
		}
		finally
		{
			PackageResolverRoots.Resolver = null;
		}
	}

	/// <summary>
	/// Resolves the package assembly every time the hotload resolves anything, like a game's lambdas do.
	/// </summary>
	private sealed class ResolveAlongside( IAssemblyResolver inner, Action alongside ) : IAssemblyResolver
	{
		public int Calls { get; private set; }

		public AssemblyDefinition Resolve( AssemblyNameReference name )
		{
			Calls++;
			alongside();
			return inner.Resolve( name );
		}

		public AssemblyDefinition Resolve( AssemblyNameReference name, ReaderParameters parameters ) => Resolve( name );

		public void Dispose() => inner.Dispose();
	}
}

/// <summary>
/// Races a thread against a hotload, which needs the hotload to be slow enough to interfere with, and the thread
/// to get a core while it runs. Other tests running in parallel can break both, so this runs on its own.
/// </summary>
[TestClass]
[DoNotParallelize]
public class HashSetInterferenceTest : HotloadFixtureTestBase
{
	private static class InterferenceRoots
	{
		public static readonly HashSet<object> InitOnlyHashSet = new();
	}

	/// <summary>
	/// Test erroring when a hash set is modified by other thread during a hotload.
	/// </summary>
	[TestMethod]
	public async Task HashSetInterference()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			public class HashSetInterference : IHotloadTestFixture;
			""" );

		var set = InterferenceRoots.InitOnlyHashSet;

		try
		{
			// Lots of items so hotload gives us enough time to interfere

			const int itemCount = 1_000_000;

			for ( var i = 0; i < itemCount; i++ )
			{
				set.Add( i );
			}

			await fixture.BuildNewAsync();

			using var cts = new CancellationTokenSource();
			using var spinning = new ManualResetEventSlim();

			// A dedicated thread that is already spinning: a pool task can start after the hotload has finished
			var interferer = new Thread( () =>
			{
				spinning.Set();

				while ( !cts.IsCancellationRequested )
				{
					// Wait for hotload to clear the set and start repopulating

					if ( set.Count == itemCount )
					{
						continue;
					}

					// Interfere!

					set.Add( itemCount + 1 );
					break;
				}
			} );

			interferer.Start();
			spinning.Wait();

			HotloadResult result;

			try
			{
				result = await fixture.HotloadAsync( allowErrors: true, configure: x => WatchStatics( x, typeof( InterferenceRoots ) ) );
			}
			finally
			{
				cts.Cancel();
				interferer.Join();
			}

			Assert.IsTrue( result.HasErrors );

			// Look for an error involving the interfered hash set

			Assert.IsNotNull( result.Errors.FirstOrDefault( x => x.Member?.Name == nameof( InterferenceRoots.InitOnlyHashSet ) ) );
		}
		finally
		{
			set.Clear();
			set.TrimExcess();
		}
	}
}
