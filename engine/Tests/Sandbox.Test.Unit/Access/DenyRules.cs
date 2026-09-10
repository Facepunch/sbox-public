using Mono.Cecil;
using Sandbox;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AccessTests;

// A deny rule that matches nothing looks exactly like one that works, so "!ArrayPool.Shared" sat
// dead for years - written without the generic arity and get_ prefix AccessSignature emits.
[TestClass]
public class DenyRuleTest
{
	[TestMethod]
	public void EveryDenyRuleMatchesSomething()
	{
		var rules = new AccessRules();
		var signatures = WhitelistedAssemblySignatures( rules );

		Assert.IsTrue( signatures.Count > 0, "Found no assemblies to scan - the test is not proving anything" );

		var dead = rules.Blacklist
			.Where( rule => !signatures.Any( rule.IsMatch ) )
			.Select( rule => rule.ToString() )
			.ToArray();

		Assert.AreEqual( 0, dead.Length,
			"Deny rules matching nothing in the whitelisted assemblies:\n" + string.Join( "\n", dead ) );
	}

	// Built with the same AccessSignature the scanner uses - a rule matches these or nothing.
	static List<string> WhitelistedAssemblySignatures( AccessRules rules )
	{
		var runtimeDir = Path.GetDirectoryName( typeof( object ).Assembly.Location );
		var signatures = new List<string>();

		foreach ( var name in rules.AssemblyWhitelist.Distinct() )
		{
			var path = Path.Combine( runtimeDir, $"{name}.dll" );
			if ( !File.Exists( path ) )
				continue;

			using var module = ModuleDefinition.ReadModule( path );

			foreach ( var type in module.Types )
				AddType( type, signatures );
		}

		return signatures;
	}

	static void AddType( TypeDefinition type, List<string> signatures )
	{
		signatures.Add( AccessSignature.Type( type ) );

		foreach ( var method in type.Methods )
			signatures.Add( AccessSignature.Method( method ) );

		foreach ( var nested in type.NestedTypes )
			AddType( nested, signatures );
	}
}
