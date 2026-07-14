using System.Collections.Generic;
using System.Linq;

namespace CompilingTests.CodeAccess;

/// <summary>
/// Regression corpus for the unified code-access analyzer (<see cref="CodeAccessGate"/> -> AccessWalker).
///
/// Each row pins the analyzer's verdict for one snippet - whether it's denied, and optionally a
/// substring the denial must contain. The two rule categories the single walker enforces are recorded
/// separately (<see cref="Case.ExpectBlacklisted"/> = memory-unsafe blacklist rule,
/// <see cref="Case.ExpectAccessDenied"/> = member-level allow-list denial) only to document WHY a case
/// is denied; the assertion is on their union (<see cref="Case.ExpectDenied"/>).
/// </summary>
[TestClass]
[DoNotParallelize]
public class CodeAccessCorpusTest
{
	// Wrap statements in a method body.
	static string M( string body ) =>
		$$"""
		using Sandbox;
		public static class Probe
		{
			public static void Run()
			{
				{{body}}
			}
		}
		""";

	// Raw declaration (types/attributes that can't live inside a method body).
	static string D( string decl ) => "using Sandbox;\n" + decl;

	public sealed record Case(
		string Name,
		string Source,
		bool ExpectBlacklisted,
		bool ExpectAccessDenied,
		string DeniedContains = null )
	{
		/// <summary>
		/// Denied if either rule category rejects the snippet: the memory-unsafe blacklist
		/// (<see cref="ExpectBlacklisted"/>) or the member-level allow-list (<see cref="ExpectAccessDenied"/>).
		/// A blacklist hit the allow-list would pass (compiler-emitted infra like <c>Unsafe.As</c>) is why
		/// this is a union.
		/// </summary>
		public bool ExpectDenied => ExpectBlacklisted || ExpectAccessDenied;
	}

	static IEnumerable<Case> Corpus =>
	[
		// ---- Member granularity + ! deny-exceptions (Access) ----
		new( "Path.Combine allowed", M( "var _ = System.IO.Path.Combine( \"a\", \"b\" );" ), false, false ),
		new( "Path.GetFullPath denied", M( "var _ = System.IO.Path.GetFullPath( \"a\" );" ), false, true, "Path.GetFullPath" ),
		new( "Path.Exists denied", M( "var _ = System.IO.Path.Exists( \"a\" );" ), false, true, "Path.Exists" ),
		new( "Path.GetTempPath denied", M( "var _ = System.IO.Path.GetTempPath();" ), false, true, "Path.GetTempPath" ),
		// The generator rewrites ArrayPool<T>.Shared -> Sandbox PublicArrayPool<T>.Shared (a safe wrapper)
		// before Access runs, so the production path allows it. See ArrayPoolSharedRedirect and
		// ArrayPoolSharedEscapeTest.
		new( "ArrayPool<T>.Shared rewritten and allowed", M( "var _ = System.Buffers.ArrayPool<byte>.Shared;" ), false, false ),
		new( "StreamReader(Stream) allowed",
			M( "var s = new System.IO.MemoryStream(); var r = new System.IO.StreamReader( s );" ), false, false ),
		new( "StreamReader(string) denied",
			M( "var r = new System.IO.StreamReader( \"f.txt\" );" ), false, true, "StreamReader" ),
		new( "File.ReadAllText denied", M( "var _ = System.IO.File.ReadAllText( \"x\" );" ), false, true, "System.IO.File" ),
		new( "Environment.CurrentManagedThreadId allowed",
			M( "var _ = System.Environment.CurrentManagedThreadId;" ), false, false ),
		new( "Environment.GetEnvironmentVariable denied",
			M( "var _ = System.Environment.GetEnvironmentVariable( \"x\" );" ), false, true, "Environment" ),
		new( "Array.Clone denied", M( "var a = new int[1]; var _ = a.Clone();" ), false, true, "Array.Clone" ),

		// ---- Reflection allow-list boundary ----
		new( "Type.FullName allowed", M( "var _ = typeof( int ).FullName;" ), false, false ),
		new( "Type.IsAssignableTo allowed", M( "var _ = typeof( int ).IsAssignableTo( typeof( object ) );" ), false, false ),
		new( "Type.GetMethod denied", M( "var _ = typeof( int ).GetMethod( \"x\" );" ), false, true, "Type.GetMethod" ),
		new( "Type.Assembly denied", M( "var _ = typeof( int ).Assembly;" ), false, true, "Type.get_Assembly" ),
		new( "Activator.CreateInstance<T> allowed",
			M( "var _ = System.Activator.CreateInstance<System.Version>();" ), false, false ),
		new( "Activator.CreateInstance(Type) denied",
			M( "var _ = System.Activator.CreateInstance( typeof( int ) );" ), false, true, "Activator" ),
		new( "dynamic invocation denied",
			M( "dynamic t = typeof( int ); var _ = t.GetMethod( \"X\" );" ), false, true, "dynamic" ),
		new( "PropertyInfo equality allowed",
			M( "System.Reflection.PropertyInfo property = null; var same = property == null;" ), false, false ),
		new( "Task continuation options allowed",
			M( "var next = System.Threading.Tasks.Task.CompletedTask.ContinueWith( _ => { }, System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously );" ), false, false ),

		// ---- Implicit language-construct calls (regression guards: must stay allowed) ----
		new( "foreach over List allowed",
			M( "var total = 0; foreach ( var x in new System.Collections.Generic.List<int> { 1, 2 } ) total += x;" ),
			false, false ),
		new( "using/IDisposable allowed",
			M( "using ( var m = new System.IO.MemoryStream() ) { m.WriteByte( 1 ); }" ), false, false ),
		new( "await Task.Delay allowed",
			D( "public static class Probe { public static async System.Threading.Tasks.Task Run() { await System.Threading.Tasks.Task.Delay( 1 ); } }" ),
			false, false ),
		new( "operator + on BigInteger allowed",
			M( "var b = new System.Numerics.BigInteger( 5 ) + new System.Numerics.BigInteger( 3 );" ), false, false ),

		// ---- Assembly attribution (type-forwarded impl assemblies; must stay allowed) ----
		new( "System.Uri allowed (System.Private.Uri)", M( "var u = new System.Uri( \"http://example.com\" );" ), false, false ),
		new( "Complex allowed (System.Runtime.Numerics)", M( "var c = new System.Numerics.Complex( 1, 2 );" ), false, false ),
		new( "Regex allowed (System.Text.RegularExpressions)",
			M( "var ok = System.Text.RegularExpressions.Regex.IsMatch( \"a\", \"b\" );" ), false, false ),

		// ---- IL-only-derived checks now expressible at source ----
		new( "StructLayout Sequential allowed",
			D( "[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )] public struct Seq { public int A; }" ),
			false, false ),
		new( "StructLayout Explicit denied",
			D( "[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Explicit )] public struct Exp { [System.Runtime.InteropServices.FieldOffset( 0 )] public int A; }" ),
			false, true, "StructLayout" ),
		new( "DllImport denied",
			D( "public static class Native { [System.Runtime.InteropServices.DllImport( \"user32\" )] public static extern int Msg( int h ); }" ),
			false, true, "DllImport" ),
		new( "TypeForwardedTo denied",
			D( "[assembly: System.Runtime.CompilerServices.TypeForwardedTo( typeof( System.Diagnostics.Process ) )] public class Marker { }" ),
			false, true, "TypeForwardedTo" ),
		new( "Object.Finalize method group denied",
			D( "public class FinalizerProbe { public System.Action Capture() => base.Finalize; }" ),
			false, true, "Object.Finalize" ),

		// ---- Union cases: source-blacklist vs Access divergence ----
		new( "Unsafe.As blacklisted, Access allows",
			M( "var v = System.Runtime.CompilerServices.Unsafe.As<System.Version>( new System.Version() );" ),
			true, false, "Unsafe.As" ),
		new( "Unsafe.SizeOf allowed by both", M( "var _ = System.Runtime.CompilerServices.Unsafe.SizeOf<int>();" ), false, false ),
		new( "stackalloc to Span allowed by both", M( "System.Span<int> s = stackalloc int[4]; s[0] = 1;" ), false, false ),

		// ---- Escape-surface denials (security-critical: the refactor MUST keep denying these) ----
		new( "Process.Start denied", M( "var _ = System.Diagnostics.Process.Start( \"calc\" );" ), false, true, "Process" ),
		new( "Thread ctor denied", M( "var t = new System.Threading.Thread( () => { } );" ), false, true, "System.Threading.Thread" ),
		new( "Task.Run denied", M( "var _ = System.Threading.Tasks.Task.Run( () => { } );" ), false, true, "Task.Run" ),
		new( "Assembly.Load denied", M( "var _ = System.Reflection.Assembly.Load( \"x\" );" ), false, true, "Assembly" ),
		new( "AppDomain denied", M( "var _ = System.AppDomain.CurrentDomain;" ), false, true, "AppDomain" ),
		new( "Environment.Exit denied", M( "System.Environment.Exit( 0 );" ), false, true, "Environment" ),
		new( "HttpClient ctor denied", M( "var _ = new System.Net.Http.HttpClient();" ), false, true, "HttpClient" ),

		// ---- Type-touch paths: a disallowed type reached via field / generic arg, not a direct call ----
		new( "disallowed type as generic arg denied",
			M( "var _ = new System.Collections.Generic.List<System.Diagnostics.Process>();" ), false, true, "Process" ),
		new( "disallowed type as field denied",
			D( "public class Holder { public System.Diagnostics.Process P; }" ), false, true, "Process" ),
		new( "MarshalAs field denied",
			D( "public class Marshalled { [System.Runtime.InteropServices.MarshalAs( System.Runtime.InteropServices.UnmanagedType.Bool )] public bool B; }" ),
			false, true, "MarshalAs" ),

		// ---- Inheritance path: deriving from a disallowed base pulls in its non-whitelisted ctor ----
		new( "non-whitelisted base class denied",
			D( "public class Evil : System.Net.Http.HttpClient { }" ), false, true, "HttpClient" ),

		new( "is-type of disallowed type denied",
			M( "var ok = ((object)1) is System.Diagnostics.Process;" ), false, true, "Process" ),
		new( "default of disallowed value type denied",
			D( "public static class X { public static object Run() => default( System.Runtime.InteropServices.GCHandle ); }" ), false, true, "GCHandle" ),
		new( "array of disallowed element type denied",
			D( "public static class X { public static object Run() => new System.Diagnostics.Process[1]; }" ), false, true, "Process" ),
		new( "Vector2.Create(ReadOnlySpan) allowed",
			M( "var v = System.Numerics.Vector2.Create( new System.ReadOnlySpan<float>( new float[2] ) );" ), false, false ),
		new( "external event subscription denied",
			M( "System.Console.CancelKeyPress += ( s, e ) => { };" ), false, true, "add_CancelKeyPress" ),
		new( "non-whitelisted static field owner denied",
			M( "var size = System.Runtime.InteropServices.Marshal.SystemDefaultCharSize;" ), false, true, "Marshal" ),
		new( "folded enum constant of blocked type allowed",
			M( "var x = (int)System.Diagnostics.ProcessWindowStyle.Hidden;" ), false, false ),
		new( "catch type denied",
			D( "public static class X { public static void Run() { try { } catch ( System.Reflection.TargetInvocationException ) { } } }" ), false, true, "TargetInvocationException" ),
		new( "foreach type denied",
			D( "public static class X { public static void Run() { foreach ( System.Diagnostics.Process process in new object[0] ) { } } }" ), false, true, "Process" ),
		new( "generic constraint type denied",
			D( "public sealed class Constrained<T> where T : System.Net.Http.HttpClient { }" ), false, true, "HttpClient" ),
		new( "stackalloc element type denied",
			M( "System.Span<System.Runtime.InteropServices.GCHandle> handles = stackalloc System.Runtime.InteropServices.GCHandle[1];" ), false, true, "GCHandle" ),
	];

	public static IEnumerable<object[]> Cases => Corpus.Select( c => new object[] { c } );

	[TestMethod]
	[DynamicData( nameof( Cases ) )]
	public void AnalyzerVerdict( Case c )
	{
		var r = CodeAccessGate.Verify( c.Source );

		Assert.IsFalse( r.EmitFailed, $"[{c.Name}] should compile but didn't: {r}" );
		Assert.AreEqual( c.ExpectDenied, r.Denied.Count > 0, $"[{c.Name}] analyzer verdict mismatch: {r}" );

		if ( c.DeniedContains is not null )
			Assert.IsTrue( r.Denied.Any( x => x.Contains( c.DeniedContains ) ),
				$"[{c.Name}] expected a denial containing '{c.DeniedContains}': {r}" );
	}

	[TestMethod]
	public void ArrayPoolShared_RedirectOrDeny()
	{
		var source = M( "var pool = System.Buffers.ArrayPool<byte>.Shared;" );
		var generated = CodeAccessGate.Verify( source );
		var raw = CodeAccessGate.Verify( source, runGenerator: false );
		var aliased = CodeAccessGate.Verify( D( "using Pool = System.Buffers.ArrayPool<byte>; public static class Probe { public static object Run() => Pool.Shared; }" ) );

		Assert.AreEqual( 0, generated.Denied.Count, $"generated access should use PublicArrayPool: {generated}" );
		Assert.IsTrue( raw.Denied.Any( x => x.Contains( "get_Shared" ) ), $"raw access must be denied: {raw}" );
		Assert.IsTrue( aliased.Denied.Any( x => x.Contains( "get_Shared" ) ), $"unrewritten alias must be denied: {aliased}" );
	}
}
