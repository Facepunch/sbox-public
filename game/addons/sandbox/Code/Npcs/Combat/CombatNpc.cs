namespace Sandbox.Npcs.CombatNpc;

/// <summary>
/// The sandbox's soldier: the engine's combat NPC with a friendly switch for the spawn menu, and press to follow
/// for a friendly one.
/// </summary>
public sealed class CombatNpc : BaseCombatNpc, Component.IPressable
{
	/// <summary>
	/// When true, this NPC is friendly to players and will follow them, engaging hostile NPCs.
	/// Turning this off makes the NPC hostile to players and friendly NPCs.
	/// </summary>
	[Property, Title( "#entity.property.npc_friendly" ), Description( "#entity.description.npc_friendly" ), ClientEditable]
	public bool Friendly
	{
		get => Faction == Factions.Ally;
		set
		{
			// Serialized alongside Faction. A saved false also represents custom, non-ally factions.
			if ( value == Friendly )
				return;

			Faction = value ? Factions.Ally : Factions.Enemy;

			if ( !value )
				Leader = null;
		}
	}

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		if ( !Friendly )
			return null;

		return Leader.IsValid()
			? new IPressable.Tooltip( "#npc.action.stop_following", "person_off", null )
			: new IPressable.Tooltip( "#npc.action.follow", "follow_the_signs", null );
	}

	bool IPressable.CanPress( IPressable.Event e ) => Friendly;

	bool IPressable.Press( IPressable.Event e )
	{
		ToggleFollow( e.Source.GameObject );
		return true;
	}

	[Rpc.Host]
	private void ToggleFollow( GameObject presserObject )
	{
		if ( !Friendly || !presserObject.IsValid() )
			return;

		var player = presserObject.Root.GetComponent<PlayerController>();
		if ( !player.IsValid() )
			return;

		// Only for yourself
		if ( Rpc.Caller is not null && player.Network.Owner != Rpc.Caller )
			return;

		Leader = Leader == player.GameObject ? null : player.GameObject;
	}
}
