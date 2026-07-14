using System;
using System.Linq;
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
			"Trusted/*",
			"Runtime/Allowed.*",
			"!Runtime/Allowed.Denied()",
			"-Blocked.*",
			"+Blocked.Safe()",
		] );

		Assert.IsTrue( rules.WholesaleAssembly( "Trusted" ) );
		Assert.IsTrue( rules.IsAllowed( "Allowed.Safe()" ) );
		Assert.IsFalse( rules.IsAllowed( "Allowed.Denied()" ) );
		Assert.IsTrue( rules.IsBlocked( "Blocked.Dangerous()" ) );
		Assert.IsFalse( rules.IsBlocked( "Blocked.Safe()" ) );
	}

	[DataTestMethod]
	[DataRow( "target.Value += 1;", "Target.set_Value( System.Int32 )", "get_Value" )]
	[DataRow( "target.Value++;", "Target.get_Value()", "set_Value" )]
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
}
