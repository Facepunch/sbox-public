using System.Linq;

namespace CompilingTests.CodeAccess;

/// <summary>
/// The analyzer only sees source, so members a language construct invokes without any call syntax -
/// foreach's enumerator, using's Dispose, conversion operators - have to be resolved deliberately.
/// Each test blocks a member only reachable through the construct.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CodeAccessLoweringTest
{
	static AccessRules With( params string[] extra ) => new( [.. Rules.All, .. extra] );

	static void AssertDenied( string source, string expected, AccessRules rules )
	{
		var result = CodeAccessGate.Verify( source, rules: rules );

		Assert.IsFalse( result.EmitFailed, $"fixture should compile: {result}" );
		Assert.IsTrue( result.Denied.Any( x => x.Contains( expected ) ), $"expected '{expected}' to be denied: {result}" );
	}

	// A ref-returning indexer has no setter, so assigning through it calls get_Item
	[DataTestMethod]
	[DataRow( "s[0] = 1;" )]
	[DataRow( "s[0] += 1;" )]
	[DataRow( "(s[0], s[1]) = (1, 2);" )]
	public void RefReturningIndexer_Assignment_ChecksGetter( string statement )
	{
		AssertDenied( $$"""
			public static class Probe
			{
				public static void Run( System.Span<int> s ) { {{statement}} }
			}
			""", "Span`1.get_Item", With( "-System.Span`1.get_Item*" ) );
	}

	[TestMethod]
	public void Foreach_ChecksEnumeratorMembers()
	{
		var rules = With( "-System.Collections.Generic.List`1/Enumerator.MoveNext()" );

		AssertDenied( """
			public static class Probe
			{
				public static int Run( System.Collections.Generic.List<int> list )
				{
					var total = 0;
					foreach ( var i in list ) total += i;
					return total;
				}
			}
			""", "Enumerator.MoveNext", rules );

		// control - the same member, called explicitly
		AssertDenied( """
			public static class Probe
			{
				public static bool Run( System.Collections.Generic.List<int> list )
				{
					var e = list.GetEnumerator();
					return e.MoveNext();
				}
			}
			""", "Enumerator.MoveNext", rules );
	}

	[TestMethod]
	public void Using_ChecksDispose()
	{
		var rules = With( "-System.IO.Stream.Dispose*" );

		AssertDenied( """
			public static class Probe
			{
				public static void Run()
				{
					using var stream = new System.IO.MemoryStream();
				}
			}
			""", "Dispose", rules );

		AssertDenied( """
			public static class Probe
			{
				public static void Run()
				{
					using ( var stream = new System.IO.MemoryStream() ) { }
				}
			}
			""", "Dispose", rules );
	}

	[TestMethod]
	public void CollectionInitializer_ChecksAdd()
	{
		AssertDenied( """
			public static class Probe
			{
				public static object Run() => new System.Collections.Generic.List<int> { 1, 2, 3 };
			}
			""", "List`1.Add", With( "-System.Collections.Generic.List`1.Add*" ) );
	}

	[TestMethod]
	public void Deconstruction_ChecksDeconstruct()
	{
		var rules = With( "-System.Collections.Generic.KeyValuePair`2.Deconstruct*" );

		AssertDenied( """
			public static class Probe
			{
				public static int Run( System.Collections.Generic.KeyValuePair<int, int> kv )
				{
					var (a, b) = kv;
					return a + b;
				}
			}
			""", "Deconstruct", rules );

		AssertDenied( """
			public static class Probe
			{
				public static int Run( System.Collections.Generic.Dictionary<int, int> map )
				{
					var total = 0;
					foreach ( var (key, value) in map ) total += key + value;
					return total;
				}
			}
			""", "Deconstruct", rules );
	}

	[TestMethod]
	public void UserDefinedConversion_IsChecked()
	{
		AssertDenied( """
			public static class Probe
			{
				public static System.Half Run( float f ) => (System.Half)f;
			}
			""", "Half.op_Explicit", With( "-System.Half.op_Explicit*" ) );

		// Span/Memory declare their own widening operators, so the signature names the source type.
		AssertDenied( """
			public static class Probe
			{
				public static System.ReadOnlyMemory<int> Run( System.Memory<int> m ) => m;
			}
			""", "Memory`1.op_Implicit", With( "-System.Memory`1.op_Implicit*" ) );
	}

	[TestMethod]
	public void Lock_ChecksMonitor()
	{
		AssertDenied( """
			public static class Probe
			{
				static readonly object Gate = new object();
				public static void Run() { lock ( Gate ) { } }
			}
			""", "Monitor", With( "-System.Threading.Monitor.*" ) );
	}

	[TestMethod]
	public void AttributeNamedArgument_ChecksPropertySetter()
	{
		AssertDenied( """
			public static class Probe
			{
				[System.Obsolete( "x", DiagnosticId = "y" )]
				public static void Run() { }
			}
			""", "set_DiagnosticId", With( "-System.ObsoleteAttribute.set_DiagnosticId*" ) );
	}

	/// <summary>
	/// Known gaps, asserted so they stay visible. Both lower to members that are allow-listed on
	/// purpose, so there's nothing to enforce - but a hard block on either would silently not apply.
	/// </summary>
	[TestMethod]
	public void KnownGaps_LoweringHelpersAreNotVisibleInSource()
	{
		var slice = CodeAccessGate.Verify( """
			public static class Probe
			{
				public static int[] Run( int[] a ) => a[1..];
			}
			""", rules: With( "-System.Runtime.CompilerServices.RuntimeHelpers.GetSubArray*" ) );

		Assert.IsFalse( slice.EmitFailed, $"fixture should compile: {slice}" );
		Assert.AreEqual( 0, slice.Denied.Count,
			"array range slicing calls GetSubArray with no source symbol - if this now reports, update the docs" );

		var interpolation = CodeAccessGate.Verify( """
			public static class Probe
			{
				public static string Run( int x ) => $"v{x}";
			}
			""", rules: With( "-System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.*" ) );

		Assert.IsFalse( interpolation.EmitFailed, $"fixture should compile: {interpolation}" );
		Assert.AreEqual( 0, interpolation.Denied.Count,
			"interpolation handler calls are synthesised - if this now reports, update the docs" );

		var tuple = CodeAccessGate.Verify( """
			public static class Probe
			{
				public static bool Run( System.DateTime a, System.DateTime b ) => (a, 1) == (b, 1);
			}
			""", rules: With( "-System.DateTime.op_Equality*" ) );

		Assert.IsFalse( tuple.EmitFailed, $"fixture should compile: {tuple}" );
		Assert.AreEqual( 0, tuple.Denied.Count,
			"tuple == calls element operators with no source symbol - if this now reports, update the docs" );

		var builder = CodeAccessGate.Verify( """
			public static class Probe
			{
				public static System.Collections.Immutable.ImmutableArray<int> Run() => [1, 2];
			}
			""", rules: With( "-System.Collections.Immutable.ImmutableArray.Create*" ) );

		Assert.IsFalse( builder.EmitFailed, $"fixture should compile: {builder}" );
		Assert.AreEqual( 0, builder.Denied.Count,
			"collection expressions call [CollectionBuilder] methods with no source symbol - if this now reports, update the docs" );

		// The same rules do apply when source names the member
		Assert.AreNotEqual( 0, CodeAccessGate.Verify(
			"public static class Probe { public static bool Run( System.DateTime a, System.DateTime b ) => a == b; }",
			rules: With( "-System.DateTime.op_Equality*" ) ).Denied.Count );
		Assert.AreNotEqual( 0, CodeAccessGate.Verify(
			"public static class Probe { public static System.Collections.Immutable.ImmutableArray<int> Run() => System.Collections.Immutable.ImmutableArray.Create( 1, 2 ); }",
			rules: With( "-System.Collections.Immutable.ImmutableArray.Create*" ) ).Denied.Count );
	}
}
