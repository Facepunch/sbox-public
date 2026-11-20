using CompilingTests;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HotloadTests;

/// <summary>
/// Base for tests that compile their own fixture with <see cref="HotloadFixtureAsync"/>. Each test gets its own
/// assemblies, and hotload only walks those, so these tests can run in parallel.
/// </summary>
public abstract class HotloadFixtureTestBase
{
	/// <summary>
	/// <para>
	/// Compiles <paramref name="diff"/> twice through <see cref="Compiler"/>, first without its <c>+</c> lines
	/// and then without its <c>-</c> lines (see <see cref="DiffSource"/>). The first version must contain exactly
	/// one type implementing <see cref="IHotloadTestFixture"/>, which is instantiated and tested, then hotloaded
	/// to the second version and tested again.
	/// </para>
	/// <para>
	/// Fixture sources can use <c>Assert</c> without a using directive. See the remarks on
	/// <see cref="HotloadFixture"/> about compiler-generated code that only one version has.
	/// </para>
	/// </summary>
	/// <param name="diff">Source of both versions, see <see cref="DiffSource"/>.</param>
	/// <param name="allowErrors">If false, fail the test if hotload logs any errors.</param>
	/// <param name="warningsAsErrors">Fail to compile if there are any warnings.</param>
	/// <param name="testName">Used to name the fixture's assembly.</param>
	protected static async Task<HotloadResult> HotloadFixtureAsync( string diff, bool allowErrors = false,
		bool warningsAsErrors = false, [CallerMemberName] string testName = "" )
	{
		using var fixture = await HotloadFixture.CreateAsync( diff, testName, warningsAsErrors );

		return await fixture.HotloadAsync( allowErrors );
	}

	/// <summary>
	/// Like <see cref="HotloadFixtureAsync"/>, but only compiles and tests the first version, so the test can
	/// inspect it or set up more state before calling <see cref="HotloadFixture.HotloadAsync"/>.
	/// </summary>
	protected static Task<HotloadFixture> CreateFixtureAsync( string diff, bool warningsAsErrors = false,
		[CallerMemberName] string testName = "" )
	{
		return HotloadFixture.CreateAsync( diff, testName, warningsAsErrors );
	}

	/// <summary>
	/// Reads a field of a fixture instance, for tests that check it from outside the fixture.
	/// </summary>
	protected static object GetField( object instance, string name )
	{
		var field = instance.GetType().GetField( name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );

		Assert.IsNotNull( field, $"Couldn't find field {name} in {instance.GetType()}" );

		return field.GetValue( instance );
	}

	/// <summary>
	/// Writes a field of a fixture instance, for tests that set it from outside the fixture.
	/// </summary>
	protected static void SetField( object instance, string name, object value )
	{
		var field = instance.GetType().GetField( name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );

		Assert.IsNotNull( field, $"Couldn't find field {name} in {instance.GetType()}" );

		field.SetValue( instance, value );
	}

	/// <summary>
	/// Watches the static fields of <paramref name="types"/>, which are in this non-swapped assembly. Each test
	/// should only watch its own types, so tests running in parallel don't share roots.
	/// </summary>
	protected static void WatchStatics( Sandbox.Hotload hotload, params Type[] types )
	{
		var set = new HashSet<Type>( types );

		hotload.WatchAssembly( typeof( HotloadFixtureTestBase ).Assembly, set.Contains );
	}
}

/// <summary>
/// Two versions of a fixture compiled from a diff (see <see cref="DiffSource"/>), with an instance of its
/// <see cref="IHotloadTestFixture"/> type kept in a static root that hotload walks.
/// </summary>
/// <remarks>
/// Code that only exists in <c>-</c> lines is removed in the second version, along with anything the compiler
/// generated for it, like lambda methods, closures, anonymous types, or delegate types for method groups with
/// <c>in</c> parameters. Hotload then treats those as removed. To test them being upgraded instead, put the
/// code in a method that's in both versions, and only call it from <c>-</c> lines.
/// <para>
/// TODO: Both versions are loaded into the default load context, so they stay loaded for the rest of the test run.
/// Loading them into a collectible <see cref="System.Runtime.Loader.AssemblyLoadContext"/> would let
/// <see cref="Dispose"/> unload them.
/// </para>
/// </remarks>
public sealed class HotloadFixture : IDisposable
{
	private const string FixturePath = "Fixture.cs";

	/// <summary>
	/// Compiled into each fixture assembly, holding the fixture instance so hotload can reach it.
	/// Keeping it there rather than in a static of the test means tests running in parallel don't share roots.
	/// </summary>
	private const string RootPath = "__HotloadFixtureRoot.cs";
	private const string RootTypeName = "__HotloadFixtureRoot";
	private const string RootFieldName = "Instance";

	private readonly InMemoryCompiler _compiler;
	private readonly string _before;
	private readonly string _after;

	/// <summary>
	/// The first version of the fixture.
	/// </summary>
	public Assembly OldAssembly { get; private set; }

	/// <summary>
	/// The second version of the fixture, once <see cref="BuildNewAsync"/> or <see cref="HotloadAsync"/> has been called.
	/// </summary>
	public Assembly NewAssembly { get; private set; }

	private byte[] _oldAssemblyData;
	private byte[] _newAssemblyData;

	/// <summary>
	/// The fixture instance created from <see cref="OldAssembly"/>. Hotload doesn't change it.
	/// </summary>
	public IHotloadTestFixture OldInstance => (IHotloadTestFixture)GetRootField( OldAssembly ).GetValue( null );

	/// <summary>
	/// The fixture instance from the most recently built assembly, which after a hotload is the upgraded one.
	/// </summary>
	public IHotloadTestFixture Instance => (IHotloadTestFixture)GetRootField( NewAssembly ?? OldAssembly ).GetValue( null );

	private HotloadFixture( string diff, string testName, bool warningsAsErrors )
	{
		(_before, _after) = DiffSource.Parse( diff );

		// Named after the test, so it's easy to tell apart from fixtures being compiled in parallel

		_compiler = new InMemoryCompiler( $"HotloadFixture.{testName}" );
		_compiler.Config = _compiler.Config with { Unsafe = true, TreatWarningsAsErrors = warningsAsErrors };

		_compiler.Compiler.AddReference( typeof( Assert ).Assembly.GetName().Name );
		_compiler.Compiler.GeneratedCode.AppendLine( $"global using {typeof( Assert ).Namespace};" );
		_compiler.Compiler.GeneratedCode.AppendLine( $"global using {typeof( IHotloadTestFixture ).Namespace};" );
	}

	/// <summary>
	/// Compiles the first version of <paramref name="diff"/>, which must have exactly one type implementing
	/// <see cref="IHotloadTestFixture"/>. Creates an instance of it and calls <see cref="IHotloadTestFixture.Test"/>.
	/// </summary>
	public static async Task<HotloadFixture> CreateAsync( string diff, string testName, bool warningsAsErrors = false )
	{
		var fixture = new HotloadFixture( diff, testName, warningsAsErrors );

		try
		{
			await fixture.BuildOldAsync();
			return fixture;
		}
		catch
		{
			fixture.Dispose();
			throw;
		}
	}

	private async Task BuildOldAsync()
	{
		await RunAsync( async () =>
		{
			_compiler.WriteSources( new Dictionary<string, string>
			{
				[RootPath] = $"public static class {RootTypeName} {{ public static global::{typeof( IHotloadTestFixture ).FullName} {RootFieldName}; }}",
				[FixturePath] = _before
			} );

			var build = await _compiler.BuildAsync();

			OldAssembly = build.Assembly;
			_oldAssemblyData = build.AssemblyData;

			var instance = CreateFixtureInstance( OldAssembly );

			GetRootField( OldAssembly ).SetValue( null, instance );
			instance.Test();
		} );
	}

	/// <summary>
	/// Compiles the second version, if it hasn't been already.
	/// </summary>
	public async Task BuildNewAsync()
	{
		if ( NewAssembly is not null ) return;

		await RunAsync( async () =>
		{
			_compiler.WriteSources( new Dictionary<string, string> { [FixturePath] = _after } );

			var build = await _compiler.BuildAsync();

			NewAssembly = build.Assembly;
			_newAssemblyData = build.AssemblyData;
		} );
	}

	/// <summary>
	/// A hotload replacing <see cref="OldAssembly"/> with <see cref="NewAssembly"/>, which must have been built.
	/// It only watches the fixture's own assemblies.
	/// </summary>
	public Sandbox.Hotload CreateHotload()
	{
		Assert.IsNotNull( NewAssembly, $"Call {nameof( BuildNewAsync )} first" );

		var resolver = new InMemoryAssemblyResolver();

		resolver.Add( _oldAssemblyData );
		resolver.Add( _newAssemblyData );

		var hotload = HotloadRunner.Create( resolver );
		hotload.ReplacingAssembly( OldAssembly, NewAssembly );

		return hotload;
	}

	/// <summary>
	/// Builds the second version, hotloads to it, then checks the fixture instance was upgraded and
	/// calls <see cref="IHotloadTestFixture.Test"/> on it.
	/// </summary>
	/// <param name="allowErrors">If false, fail the test if hotload logs any errors.</param>
	/// <param name="configure">Called on the hotload before it runs, for example to watch more roots.</param>
	public async Task<HotloadResult> HotloadAsync( bool allowErrors = false, Action<Sandbox.Hotload> configure = null )
	{
		await BuildNewAsync();

		HotloadResult result = null;

		await RunAsync( () =>
		{
			var hotload = CreateHotload();

			configure?.Invoke( hotload );

			result = HotloadRunner.Run( hotload, allowErrors );

			var instance = Instance;

			Assert.IsNotNull( instance, "Fixture instance wasn't hotloaded" );
			Assert.AreSame( NewAssembly, instance.GetType().Assembly, "Fixture instance wasn't hotloaded" );

			instance.Test();

			return Task.CompletedTask;
		} );

		return result;
	}

	/// <summary>
	/// Gets a type from <see cref="NewAssembly"/> if it's been built, otherwise <see cref="OldAssembly"/>.
	/// </summary>
	public Type GetType( string name )
	{
		var type = (NewAssembly ?? OldAssembly).GetType( name );

		Assert.IsNotNull( type, $"Couldn't find type {name}" );

		return type;
	}

	/// <summary>
	/// Runs <paramref name="action"/>, writing both versions of the fixture's source if it fails,
	/// so line numbers in the failure can be matched up.
	/// </summary>
	private async Task RunAsync( Func<Task> action )
	{
		try
		{
			await action();
		}
		catch
		{
			InMemoryCompiler.PrintSource( $"Before ({FixturePath})", _before );
			InMemoryCompiler.PrintSource( $"After ({FixturePath})", _after );
			throw;
		}
	}

	private static FieldInfo GetRootField( Assembly assembly )
	{
		var field = assembly.GetType( RootTypeName )?.GetField( RootFieldName );

		Assert.IsNotNull( field, $"Couldn't find {RootTypeName}.{RootFieldName} in {assembly}" );

		return field;
	}

	private static IHotloadTestFixture CreateFixtureInstance( Assembly assembly )
	{
		var types = assembly.GetTypes()
			.Where( x => !x.IsAbstract && !x.ContainsGenericParameters && typeof( IHotloadTestFixture ).IsAssignableFrom( x ) )
			.ToArray();

		// Exactly one, so a fixture that forgets to implement the interface fails rather than testing nothing

		Assert.AreEqual( 1, types.Length,
			$"Expected exactly one {nameof( IHotloadTestFixture )} type, found: {string.Join( ", ", types.Select( x => x.FullName ) )}" );

		return (IHotloadTestFixture)Activator.CreateInstance( types[0] );
	}

	public void Dispose()
	{
		_compiler.Dispose();
	}

	/// <summary>
	/// Resolves assemblies that were compiled in memory, so have no file to read.
	/// </summary>
	private sealed class InMemoryAssemblyResolver : DefaultAssemblyResolver
	{
		public void Add( byte[] assemblyData )
		{
			RegisterAssembly( AssemblyDefinition.ReadAssembly( new MemoryStream( assemblyData ) ) );
		}
	}
}
