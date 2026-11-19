using Sandbox.Engine;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ScriptingTests;

/// <summary>
/// Scripts supply common object methods independently of TypeLibrary's discovery filtering.
/// </summary>
[TestClass, DoNotParallelize]
public class ScriptCommonObjectMethodTests
{
	GlobalContext previous;
	GlobalContext previousGame;

	/// <summary>
	/// Uses the production catalog without registering the test fixtures.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		previousGame = GlobalContext.Game;
		GlobalContext.Current = GlobalContext.Game = new GlobalContext { TypeLibrary = SharedTypeLibrary.ScriptingWithSystem };
	}

	/// <summary>
	/// Releases the test context and restores the game context.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.Shutdown();
		GlobalContext.Current = previous;
		GlobalContext.Game = previousGame;
	}

	/// <summary>
	/// Registered values, plain objects, and unregistered generic receivers use their real implementations.
	/// </summary>
	[TestMethod]
	public void CommonMethodsWorkAcrossTypes()
	{
		AssertCommonMethods( new object() );
		AssertCommonMethods( new Vector2( 1, 2 ) );
		AssertCommonMethods( new Vector3( 1, 2, 3 ) );
		AssertCommonMethods( Guid.NewGuid() );
		AssertCommonMethods( new DateTime( 2026, 11, 7 ) );
		AssertCommonMethods( new CommonValue<int>( 7 ) );
	}

	private static void AssertCommonMethods<T>( T value )
	{
		foreach ( var (source, expected) in new (string, object)[]
		{
			("return value.ToString();", value.ToString()),
			("return value.Equals(value);", value.Equals( value )),
			("return value.GetHashCode();", value.GetHashCode())
		} )
		{
			var script = Game.Scripting.CreateScript( source );
			script.SetArgument( "value", value );
			Assert.IsTrue( script.Compile(), script.LastError?.ToString() );
			Assert.AreEqual( expected, script.RunAndReturn<object>(), script.LastError?.ToString() );
			Assert.IsNull( script.LastError );
		}
	}

	/// <summary>
	/// Public overloads are available before and during incoming assembly registration.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CommonOverloadsWorkWithoutCatalogRegistration( bool incoming )
	{
		var resolver = new TypeLibraryScriptResolver( Game.TypeLibrary );
		if ( incoming )
		{
			resolver.IncomingAssemblies = [typeof( CommonValue<> ).Assembly];
		}

		var runtime = new Breen.ScriptSystem( new() { Resolver = resolver } );
		var script = runtime.CreateScript( "return value.ToString(\"item\");" ).Set( "value", new CommonValue<int>( 7 ) );
		Assert.IsTrue( script.Run().Success );
		Assert.AreEqual( "item:7", script.ReturnValue.Get<string>() );
		Assert.IsNull( Game.TypeLibrary.GetType( typeof( CommonValue<int> ) ) );
	}

	/// <summary>
	/// Common methods remain hidden from TypeLibrary discovery, and script metadata contains no duplicates.
	/// </summary>
	[TestMethod]
	public void DiscoveryFilteringIsUnchanged()
	{
		var resolver = new TypeLibraryScriptResolver( Game.TypeLibrary );
		foreach ( var type in new[] { typeof( Guid ), typeof( Vector3 ), typeof( int ) } )
		{
			var members = new List<MemberInfo>();
			resolver.GetMembers( type, members );
			Assert.AreEqual( members.Count, members.Distinct().Count() );
			Assert.IsFalse( Game.TypeLibrary.GetType( type ).Members.Any( member => member.Name is "Equals" or "GetHashCode" ) );
		}

		Assert.IsFalse( Game.TypeLibrary.GetType( typeof( Vector3 ) ).Members.Any( member => member.Name == "ToString" ) );
	}

	/// <summary>
	/// Common-method lookup does not expose other public methods, private overloads, or reflection.
	/// </summary>
	[TestMethod]
	[DataRow( "return value.Hidden();" )]
	[DataRow( "return value.ToString(7);" )]
	[DataRow( "return value.GetType();" )]
	public void UnregisteredMembersRemainUnavailable( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		script.SetArgument( "value", new CommonValue<int>( 7 ) );
		Assert.IsFalse( script.Run(), source );
		Assert.IsNotNull( script.LastError );
	}

	private sealed class CommonValue<T>( T value )
	{
		/// <summary>
		/// Formats the fixture's value.
		/// </summary>
		public override string ToString() => $"Value={value}";

		/// <summary>
		/// Adds a caller-supplied label to the value.
		/// </summary>
		public string ToString( string label ) => $"{label}:{value}";

		/// <summary>
		/// Compares the stored values.
		/// </summary>
		public override bool Equals( object other ) => other is CommonValue<T> instance && EqualityComparer<T>.Default.Equals( value, instance.Value );

		/// <summary>
		/// Uses the stored value's hash.
		/// </summary>
		public override int GetHashCode() => EqualityComparer<T>.Default.GetHashCode( value );

		/// <summary>
		/// Represents an ordinary unregistered public method.
		/// </summary>
		public string Hidden() => "hidden";

		private T Value => value;
		private string ToString( int ignored ) => "private";
	}
}
