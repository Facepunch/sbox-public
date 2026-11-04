using Sandbox.Engine;
using System.Collections.Generic;
using System.Reflection;

namespace SystemTests;

[TestClass]
[DoNotParallelize]
public class ManagedCommandContextTests
{
	static readonly List<GlobalContext> Observed = new();
	static int _value;

	static int GameConVar
	{
		get
		{
			Observed.Add( GlobalContext.Current );
			return _value;
		}
		set
		{
			Observed.Add( GlobalContext.Current );
			_value = value;
		}
	}

	static ManagedCommand Build()
	{
		var member = typeof( ManagedCommandContextTests ).GetProperty( nameof( GameConVar ), BindingFlags.Static | BindingFlags.NonPublic );
		return new ManagedCommand( typeof( ManagedCommandContextTests ).Assembly, member, new ConVarAttribute( "test_context_convar" ) );
	}

	[TestMethod]
	public void GameConVarAccessorsRunInTheGameContext()
	{
		Observed.Clear();
		using var scope = new GlobalContext.GlobalContextScope( GlobalContext.Menu );

		var command = Build();

		Assert.AreNotEqual( 0, Observed.Count );
		Assert.IsFalse( Observed.Contains( GlobalContext.Menu ), "A game convar's default was read in the menu context" );
		Observed.Clear();

		_ = command.Value;
		_ = command.BuildDescription();
		command.Value = "5";

		Assert.AreNotEqual( 0, Observed.Count );
		Assert.IsFalse( Observed.Contains( GlobalContext.Menu ), "A game convar's accessor ran in the menu context" );
	}
}
