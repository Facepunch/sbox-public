using Sandbox.Services;
using System.Linq;

namespace ServicesTests;

/// <summary>
/// Checks compatibility with games that still call the retired map services.
/// </summary>
[TestClass]
public class MapServicesTest
{
	/// <summary>
	/// Legacy stat writes and achievement unlocks leave their collections empty.
	/// </summary>
	[TestMethod]
	public void MapServicesIgnoreWrites()
	{
#pragma warning disable CS0612, CS0618 // Exercise the obsolete public APIs.
		Achievements.Map.Unlock( "finish" );
		Assert.IsNull( Achievements.Map.Get( "finish" ) );
		Assert.IsFalse( Achievements.Map.All.Any() );

		Stats.Map.SetValue( "score", 10 );
		Assert.IsNull( Stats.Map.GetLocal( "score" ).Name );
		Assert.IsNull( Stats.Map.GetGlobal( "score" ).Name );
		Assert.IsFalse( Stats.Map.Local.Any() );
		Assert.IsFalse( Stats.Map.Global.Any() );
#pragma warning restore CS0612, CS0618
	}
}
