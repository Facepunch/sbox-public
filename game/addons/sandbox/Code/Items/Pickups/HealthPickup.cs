/// <summary>
/// A pickup that gives the player some health.
/// </summary>
public sealed class HealthPickup : BasePickup
{
	/// <summary>
	/// How much health to give to the player
	/// </summary>
	[Property, Group( "Health" )] float HealthGive { get; set; } = 0;

	public override bool CanPickup( Player player, PlayerInventory inventory )
	{
		if ( player.Health >= player.MaxHealth && HealthGive > 0 )
			return false;

		return true;
	}

	protected override bool OnPickup( Player player, PlayerInventory inventory )
	{
		player.Health = (player.Health + HealthGive).Clamp( 0, player.MaxHealth );
		player.PlayerData.AddStat( $"pickup.health" );

		return true;
	}
}
