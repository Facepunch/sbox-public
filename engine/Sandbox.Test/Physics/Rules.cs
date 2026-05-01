using System;
using Sandbox.Physics;

namespace Physics;

[TestClass]
public class Rules
{
	private static CollisionRules Create()
	{
		var rules = new CollisionRules();

		rules.Deserialize( """
		{
		  "Defaults": {
		    "solid": "Collide",
		    "trigger": "Trigger",
		    "ladder": "Ignore",
		    "water": "Trigger",
		    "ragdoll": "Ignore",
		    "player": "Unset",
		    "enemy": "Unset"
		  },
		  "Pairs": [
		    {
		      "a": "solid",
		      "b": "solid",
		      "r": "Collide"
		    },
		    {
		      "a": "trigger",
		      "b": "solid",
		      "r": "Trigger"
		    },
		    {
		      "a": "solid",
		      "b": "trigger",
		      "r": "Collide"
		    },
		    {
		      "a": "ragdoll",
		      "b": "ragdoll",
		      "r": "Collide"
		    },
		    {
		      "a": "solid",
		      "b": "ragdoll",
		      "r": "Collide"
		    },
		    {
		      "a": "ragdoll",
		      "b": "player",
		      "r": "Ignore"
		    },
		    {
		      "a": "ragdoll",
		      "b": "enemy",
		      "r": "Ignore"
		    },
		    {
		      "a": "player",
		      "b": "player",
		      "r": "Collide"
		    },
		    {
		      "a": "player",
		      "b": "enemy",
		      "r": "Ignore"
		    },
		    {
		      "a": "enemy",
		      "b": "enemy",
		      "r": "Ignore"
		    }
		  ],
		  "__guid": "ae9f48a4-c90f-42d4-9823-fa083ec6bfec",
		  "__schema": "configdata",
		  "__type": "CollisionRules",
		  "__version": 1
		}
		""" );

		return rules;
	}

	/// <summary>
	/// Does sphere overlap test work against meshes?
	/// </summary>
	[TestMethod]
	[DataRow( "solid", "solid", CollisionRules.Result.Collide )]
	[DataRow( "player", "enemy", CollisionRules.Result.Ignore )]
	[DataRow( "enemy", "player", CollisionRules.Result.Ignore )]
	[DataRow( "player", "player", CollisionRules.Result.Collide )]
	[DataRow( "player", "water", CollisionRules.Result.Trigger )]
	[DataRow( "water", "player", CollisionRules.Result.Trigger )]
	[DataRow( "solid", "trigger", CollisionRules.Result.Trigger )]
	[DataRow( "trigger", "solid", CollisionRules.Result.Trigger )]
	[DataRow( "unknown", "unknown", CollisionRules.Result.Collide )]
	[DataRow( "unknown", "trigger", CollisionRules.Result.Trigger )]
	public void GetPair( string left, string right, CollisionRules.Result result )
	{
		var rules = Create();

		Assert.AreEqual( result, rules.GetCollisionRule( left, right ) );
	}

	[TestMethod]
	[DataRow( "player", CollisionRules.Result.Collide, "solid", "player" )]
	[DataRow( "solid", CollisionRules.Result.Collide, "solid", "ragdoll", "player", "enemy" )]
	[DataRow( "player", CollisionRules.Result.Trigger, "trigger", "water" )]
	public void FromTag( string tag, CollisionRules.Result result, params string[] expected )
	{
		var rules = Create();
		StringToken tagToken = tag;
		var set = rules.RuntimeTags
			.Where( x => rules.GetCollisionRule( x, tagToken ) == result )
			.Select( x => StringToken.GetValue( x.Value ) )
			.ToHashSet();

		foreach ( var other in set )
		{
			Console.WriteLine( other );
		}

		foreach ( var e in expected )
		{
			Assert.IsTrue( set.Contains( e ), $"Tag set doesn't include expected tag \"{e}\"." );
		}

		foreach ( var included in set )
		{
			Assert.IsTrue( expected.Contains( included ), $"Tag set includes unexpected tag \"{included}\"." );
		}
	}

	[TestMethod]
	[DataRow( "enemy,player", CollisionRules.Result.Collide, "solid" )]
	[DataRow( "player,trigger", CollisionRules.Result.Collide )]
	[DataRow( "player,trigger", CollisionRules.Result.Trigger, "solid", "trigger", "water", "player" )]
	public void FromTags( string tagsCsv, CollisionRules.Result result, params string[] expected )
	{
		var tags = tagsCsv.Split( ',' );

		var rules = Create();
		var set = rules.RuntimeTags
			.Where( x =>
			{
				var r = CollisionRules.Result.Collide;
				foreach ( var tag in tags )
				{
					var cr = rules.GetCollisionRule( x, tag );
					if ( cr > r ) r = cr;
				}
				return r == result;
			} )
			.Select( x => StringToken.GetValue( x.Value ) )
			.ToHashSet();

		foreach ( var other in set )
		{
			Console.WriteLine( other );
		}

		foreach ( var e in expected )
		{
			Assert.IsTrue( set.Contains( e ), $"Tag set doesn't include expected tag \"{e}\"." );
		}

		foreach ( var included in set )
		{
			Assert.IsTrue( expected.Contains( included ), $"Tag set includes unexpected tag \"{included}\"." );
		}
	}
}
