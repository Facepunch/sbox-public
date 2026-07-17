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
		Assert.IsTrue( rules.IsAllowed( "Allowed.Safe()" ) );
		Assert.IsFalse( rules.IsAllowed( "Allowed.Denied()" ) );
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
			"Runtime/System.Int32*",
			"Runtime/System.Void*",
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

		var allowedRules = new AccessRules( [.. Rules.All, "external/Target*", $"!external/Target.{otherAccessor}*" ] );
		var allowed = CodeAccessGate.Verify( source, runGenerator: false, assemblyName: "external", rules: allowedRules );
		Assert.IsFalse( allowed.EmitFailed, $"fixture must compile: {allowed}" );
		Assert.AreEqual( 0, allowed.Denied.Count, $"{otherAccessor} must not be checked for '{operation}': {allowed}" );

		var deniedRules = new AccessRules( [.. Rules.All, "external/Target*", $"!external/Target.{executedAccessor}*" ] );
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
				signatures.Add( SymbolSignature.Type( type ) );
				foreach ( var method in type.GetMembers().OfType<IMethodSymbol>() )
					signatures.Add( SymbolSignature.Method( method ) );
			}
		}

		var unmatched = new List<string>();
		foreach ( var raw in Rules.All )
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

				rule = gated.Pattern;
			}

			if ( !signatures.Any( signature => FileSystemName.MatchesSimpleExpression( rule, signature ) ) )
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
