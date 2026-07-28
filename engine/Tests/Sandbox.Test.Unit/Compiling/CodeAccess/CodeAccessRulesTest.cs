using System;
using System.Collections.Generic;
using System.IO.Enumeration;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;

namespace CompilingTests.CodeAccess;

[TestClass]
[DoNotParallelize]
public class CodeAccessRulesTest
{
	/// <summary>
	/// <see cref="AccessRules.IsBlocked"/> is consulted before the allow-list, so an allow entry for a
	/// hard-blocked signature is dead and reads as permission. These signatures deliberately have no
	/// allow entry; if a hard block is ever narrowed, restore one rather than leaving a silent hole.
	/// </summary>
	[TestMethod]
	public void HardBlocks_MakeAllowEntriesRedundant()
	{
		string[] signatures =
		[
			"System.Runtime.CompilerServices.Unsafe.As<T>( System.Object )",
			"System.Runtime.CompilerServices.Unsafe.As<TFrom,TTo>( TFrom& )",
			"System.Runtime.CompilerServices.Unsafe.AsRef<T>( T& )",
			"System.Runtime.CompilerServices.Unsafe.Add<T>( T&, System.Int32 )",
			"System.Runtime.CompilerServices.Unsafe.AddByteOffset<T>( T&, System.IntPtr )",
			"System.Runtime.CompilerServices.InlineArray4`1",
			"System.Runtime.CompilerServices.InlineArrayAttribute",
			"System.Runtime.CompilerServices.InlineArrayAttribute..ctor( System.Int32 )",
			"System.Runtime.CompilerServices.ExtensionMarkerAttribute",
			"System.Runtime.CompilerServices.ParamCollectionAttribute",
			"System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan<T>( T&, System.Int32 )",
			"System.Span`1..ctor( System.Void*, System.Int32 )",
			"System.ReadOnlySpan`1..ctor( System.Void*, System.Int32 )",
		];

		foreach ( var signature in signatures )
			Assert.IsTrue( AccessRules.Instance.IsBlocked( signature ), $"'{signature}' must be hard blocked" );

		// The one Unsafe member with a `+` carve-out, so its allow entry is live.
		Assert.IsFalse( AccessRules.Instance.IsBlocked( "System.Runtime.CompilerServices.Unsafe.SizeOf<T>()" ) );
		Assert.IsTrue( AccessRules.Instance.IsAllowed( "System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.SizeOf<T>()" ) );
	}

	[TestMethod]
	public void RuleEffects_AreUnambiguous()
	{
		var rules = new AccessRules(
		[
			"",
			"   ",
			"Trusted/*",
			"Runtime/Allowed.*",
			"!Runtime/Allowed.Denied()",
			"-Blocked.*",
			"+Blocked.Safe()",
		] );

		Assert.IsFalse( rules.WholesaleAssembly( null ) );

		Assert.IsTrue( rules.WholesaleAssembly( "Trusted" ) );
		Assert.IsTrue( rules.IsAllowed( "Runtime/Allowed.Safe()" ) );
		Assert.IsFalse( rules.IsAllowed( "Runtime/Allowed.Denied()" ) );
		Assert.IsTrue( rules.IsBlocked( "Blocked.Dangerous()" ) );
		Assert.IsFalse( rules.IsBlocked( "Blocked.Safe()" ) );
	}

	[DataTestMethod]
	[DataRow( "target.Value += 1;", "Target.set_Value( System.Int32 )", "get_Value" )]
	[DataRow( "target.Value++;", "Target.get_Value()", "set_Value" )]
	[DataRow( "++target.Value;", "Target.get_Value()", "set_Value" )]
	[DataRow( "target.Value = 1;", "Target.set_Value( System.Int32 )", null )]
	[DataRow( "var value = target.Value;", "Target.get_Value()", null )]
	public void PropertyAccess_ChecksExactlyTheExecutedAccessors( string statement, string allowedAccessor, string deniedAccessor )
	{
		var rules = new AccessRules(
		[
			"System.Private.CoreLib/System.Int32*",
			"System.Private.CoreLib/System.Void*",
			"System.Private.CoreLib/System.Object..ctor()",
			"external/Target",
			$"external/{allowedAccessor}",
		] );

		var source = $$"""
			public sealed class Target
			{
				public int Value { get; set; }
			}

			public static class Probe
			{
				public static void Run( Target target )
				{
					{{statement}}
				}
			}
			""";

		var result = CodeAccessGate.Verify( source, runGenerator: false, assemblyName: "external", rules: rules );

		Assert.IsFalse( result.EmitFailed, $"fixture must compile: {result}" );
		if ( deniedAccessor is null )
		{
			Assert.AreEqual( 0, result.Denied.Count, $"only the executed accessor should be required: {result}" );
			return;
		}

		Assert.IsTrue( result.Denied.Any( x => x.Contains( deniedAccessor ) ),
			$"read-modify-write must validate '{deniedAccessor}': {result}" );
	}

	[DataTestMethod]
	[DataRow( "target.Inner.Field = 1;", "get_Inner" )]
	[DataRow( "(target.Inner).Field = 1;", "get_Inner" )]
	[DataRow( "target.Inner.Field++;", "get_Inner" )]
	[DataRow( "_ = new Target { Inner = { Field = 1 } };", "get_Inner" )]
	[DataRow( "(target.Value, _) = (1, 2);", "set_Value" )]
	[DataRow( "((target.Value), _) = (1, 2);", "set_Value" )]
	public void PropertyAccess_ReceiversAreReadsAndDeconstructionTargetsAreWrites( string statement, string executedAccessor )
	{
		var source = $$"""
			public sealed class Inner
			{
				public int Field;
			}

			public sealed class Target
			{
				public Inner Inner { get; } = new();
				public int Value { get; set; }
			}

			public static class Probe
			{
				public static void Run( Target target )
				{
					{{statement}}
				}
			}
			""";

		var allowedRules = new AccessRules( [.. Rules.All, "external/Target*", "external/Inner*"] );
		var allowed = CodeAccessGate.Verify( source, runGenerator: false, assemblyName: "external", rules: allowedRules );
		Assert.IsFalse( allowed.EmitFailed, $"fixture must compile: {allowed}" );
		Assert.AreEqual( 0, allowed.Denied.Count, $"fixture must be clean before denying an accessor: {allowed}" );

		var deniedRules = new AccessRules( [.. Rules.All, "external/Target*", "external/Inner*", $"!external/Target.{executedAccessor}*"] );
		var denied = CodeAccessGate.Verify( source, runGenerator: false, assemblyName: "external", rules: deniedRules );
		Assert.IsTrue( denied.Denied.Any( x => x.Contains( executedAccessor ) ),
			$"{executedAccessor} must be checked for '{statement}': {denied}" );
	}

	[DataTestMethod]
	[DataRow( "public sealed class Probe( int size ) : System.IO.MemoryStream { }", "System.IO.MemoryStream..ctor()", true )]
	[DataRow( "public sealed partial class Probe : System.IO.MemoryStream { } public sealed partial class Probe { }", "System.IO.MemoryStream..ctor()", true )]
	[DataRow( "public sealed class Probe( int size ) : System.IO.MemoryStream( size ) { }", "System.IO.MemoryStream..ctor()", false )]
	[DataRow( "public sealed class Probe( int size ) : System.IO.MemoryStream( size ) { }", "System.IO.MemoryStream..ctor( System.Int32 )", true )]
	[DataRow( "public sealed class Probe : System.IO.MemoryStream { public Probe( int size ) : base( size ) { } }", "System.IO.MemoryStream..ctor()", false )]
	[DataRow( "public sealed class Probe : System.IO.MemoryStream { public Probe( int size ) : this() { } public Probe() : base( 1 ) { } }", "System.IO.MemoryStream..ctor()", false )]
	public void ImplicitBaseConstructor_IsCheckedOnlyWhenCalled( string source, string blocked, bool expectDenied )
	{
		var rules = new AccessRules( [.. Rules.All, "-" + blocked] );
		var result = CodeAccessGate.Verify( source, rules: rules );
		Assert.IsFalse( result.EmitFailed, $"fixture must compile: {result}" );
		Assert.AreEqual( expectDenied, result.Denied.Any( x => x.Contains( blocked ) ), result.ToString() );
	}

	[DataTestMethod]
	[DataRow( "await using var scope = disposable;", "System.Runtime.CompilerServices.ValueTaskAwaiter.GetResult()" )]
	[DataRow( "await using ( disposable ) { }", "System.Runtime.CompilerServices.ValueTaskAwaiter.get_IsCompleted()" )]
	[DataRow( "await foreach ( var item in items ) { }", "System.Runtime.CompilerServices.ValueTaskAwaiter`1.GetResult()" )]
	[DataRow( "await foreach ( var item in items ) { }", "System.Threading.Tasks.ValueTask.GetAwaiter()" )]
	[DataRow( "await disposable.DisposeAsync();", "System.Threading.Tasks.ValueTask.GetAwaiter()" )]
	public void LoweredAwaits_CheckTheAwaiterPattern( string statement, string blocked )
	{
		var source = $$"""
			public static class Probe
			{
				public static async System.Threading.Tasks.Task Run( System.IAsyncDisposable disposable, System.Collections.Generic.IAsyncEnumerable<int> items )
				{
					{{statement}}
				}
			}
			""";

		var clean = CodeAccessGate.Verify( source );
		Assert.IsFalse( clean.EmitFailed, $"fixture must compile: {clean}" );
		Assert.IsFalse( clean.Denied.Any( x => x.Contains( blocked ) ), $"'{blocked}' must be allowed by default: {clean}" );

		var result = CodeAccessGate.Verify( source, rules: new AccessRules( [.. Rules.All, "-" + blocked] ) );
		Assert.IsTrue( result.Denied.Any( x => x.Contains( blocked ) ), $"'{blocked}' must be checked for '{statement}': {result}" );
	}

	[DataTestMethod]
	[DataRow( "+=", "add_Changed", "remove_Changed" )]
	[DataRow( "-=", "remove_Changed", "add_Changed" )]
	public void EventAccess_ChecksExactlyTheExecutedAccessor( string operation, string executedAccessor, string otherAccessor )
	{
		var source = $$"""
			public sealed class Target
			{
				public event System.Action Changed;
			}

			public static class Probe
			{
				public static void Run( Target target, System.Action handler )
				{
					target.Changed {{operation}} handler;
				}
			}
			""";

		var allowedRules = new AccessRules( [.. Rules.All, "external/Target*", $"!external/Target.{otherAccessor}*"] );
		var allowed = CodeAccessGate.Verify( source, runGenerator: false, assemblyName: "external", rules: allowedRules );
		Assert.IsFalse( allowed.EmitFailed, $"fixture must compile: {allowed}" );
		Assert.AreEqual( 0, allowed.Denied.Count, $"{otherAccessor} must not be checked for '{operation}': {allowed}" );

		var deniedRules = new AccessRules( [.. Rules.All, "external/Target*", $"!external/Target.{executedAccessor}*"] );
		var denied = CodeAccessGate.Verify( source, runGenerator: false, assemblyName: "external", rules: deniedRules );
		Assert.IsTrue( denied.Denied.Any( x => x.Contains( executedAccessor ) ),
			$"{executedAccessor} must be checked for '{operation}': {denied}" );
	}

	[DataTestMethod]
	[DataRow( "NoSlash" )]
	[DataRow( "!" )]
	[DataRow( "-" )]
	[DataRow( "+" )]
	[DataRow( "/Member" )]
	[DataRow( "Assembly/" )]
	public void MalformedRules_FailFast( string rule )
	{
		Assert.ThrowsException<InvalidOperationException>( () => new AccessRules( [rule] ) );
	}

	[TestMethod]
	public void RuleCatalog_IsUniqueAndTargetsKnownSymbols()
	{
		var duplicates = Rules.All
			.GroupBy( x => x, StringComparer.Ordinal )
			.Where( x => x.Count() > 1 )
			.Select( x => x.Key )
			.ToArray();
		Assert.AreEqual( 0, duplicates.Length, $"Duplicate code-access rules: {string.Join( ", ", duplicates )}" );

		// Construction validates every rule's prefix and gated assembly/signature shape.
		_ = new AccessRules( Rules.All );

		var references = CodeAccessGate.BuildReferences().ToList();
		var wholesaleAssemblies = Rules.All
			.Where( x => x[0] is not '!' and not '-' and not '+' )
			.Select( SplitGatedRule )
			.Where( x => x.Pattern == "*" )
			.Select( x => x.Assembly )
			.Distinct( StringComparer.OrdinalIgnoreCase );
		foreach ( var assembly in wholesaleAssemblies )
			references.Add( FrameworkReferences.FindByName( assembly ) );

		var compilation = CSharpCompilation.Create(
			"RuleCatalogAudit",
			references: references,
			options: new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary )
				.WithMetadataImportOptions( MetadataImportOptions.All ) );
		var assemblies = references
			.Select( compilation.GetAssemblyOrModuleSymbol )
			.OfType<IAssemblySymbol>()
			.GroupBy( x => x.Identity.Name, StringComparer.OrdinalIgnoreCase )
			.Select( x => x.First() )
			.ToArray();
		var assemblyNames = assemblies.Select( x => x.Identity.Name ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		var signatures = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var assembly in assemblies )
		{
			foreach ( var type in AllTypes( assembly.GlobalNamespace ) )
			{
				var prefix = ImplementationAssembly.Of( type ) + "/";
				signatures.Add( prefix + SymbolSignature.Type( type ) );
				foreach ( var method in type.GetMembers().OfType<IMethodSymbol>() )
					signatures.Add( prefix + SymbolSignature.Method( method ) );
			}
		}

		// Honoured by name from any assembly but not in the reference pack, these only ever match a self-defined copy
		string[] nameOnly =
		[
			"-System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute*",
			"-System.Runtime.CompilerServices.InterceptsLocationAttribute*",
		];

		var unmatched = new List<string>();
		foreach ( var raw in Rules.All.Except( nameOnly ) )
		{
			var rule = raw[0] is '!' or '-' or '+' ? raw[1..] : raw;
			if ( raw[0] is not '-' and not '+' )
			{
				var gated = SplitGatedRule( rule );
				if ( gated.Pattern == "*" )
				{
					if ( !assemblyNames.Contains( gated.Assembly ) )
						unmatched.Add( raw );
					continue;
				}
			}

			// Hard blocks are assembly-agnostic, so match them against the bare signature.
			var qualified = raw[0] is not '-' and not '+';
			if ( !signatures.Any( signature => FileSystemName.MatchesSimpleExpression( rule, qualified ? signature : signature[(signature.IndexOf( '/' ) + 1)..] ) ) )
				unmatched.Add( raw );
		}

		Assert.AreEqual( 0, unmatched.Count, $"Code-access rules matching no referenced symbol:\n{string.Join( "\n", unmatched )}" );
	}

	static (string Assembly, string Pattern) SplitGatedRule( string rule )
	{
		var slash = rule.IndexOf( '/' );
		return (rule[..slash], rule[(slash + 1)..]);
	}

	static IEnumerable<INamedTypeSymbol> AllTypes( INamespaceSymbol scope )
	{
		foreach ( var type in scope.GetTypeMembers() )
		{
			foreach ( var expanded in AllTypes( type ) )
				yield return expanded;
		}

		foreach ( var child in scope.GetNamespaceMembers() )
		{
			foreach ( var type in AllTypes( child ) )
				yield return type;
		}
	}

	static IEnumerable<INamedTypeSymbol> AllTypes( INamedTypeSymbol type )
	{
		yield return type;
		foreach ( var nested in type.GetTypeMembers() )
		{
			foreach ( var expanded in AllTypes( nested ) )
				yield return expanded;
		}
	}
}
