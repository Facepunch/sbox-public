using Sandbox.Npcs.Layers;

namespace Sandbox.Npcs;

public partial class BaseNpc : Component
{
	/// <summary>
	/// Senses layer - handles environmental awareness and target detection. Null if the NPC has none.
	/// </summary>
	public SensesLayer Senses { get; private set; }

	/// <summary>
	/// Movement layer - walks the navmesh, flies or otherwise moves the NPC. Null if the NPC has none.
	/// An NPC can carry several, such as one to walk and one to fly: this is the enabled one, and
	/// <see cref="SetMovement"/> switches between them.
	/// </summary>
	public MovementLayer Movement { get; private set; }

	// The mover that has the body, to notice a switch
	private MovementLayer _activeMover;

	/// <summary>
	/// Hand the NPC to another of its movement layers, such as from walking to flying. Its other movers are
	/// disabled and let go of the body where it is, and this one is enabled and takes it from there: a ground mover
	/// drops to the floor first. A move that was under way is dropped, and a <see cref="Tasks.MoveToTask"/> running
	/// across the switch fails. Host only.
	/// </summary>
	public void SetMovement( MovementLayer mover )
	{
		if ( !Networking.IsHost || !mover.IsValid() || mover.GameObject != GameObject )
			return;

		// Knocked down, it takes over once we're up
		if ( KnockdownState == KnockdownStates.Down )
		{
			_pendingMover = mover;
			return;
		}

		EnableOnly( mover );
		UpdateMovement();
	}

	private MovementLayer _pendingMover;

	private void EnableOnly( MovementLayer mover )
	{
		foreach ( var other in GetComponents<MovementLayer>( true ) )
		{
			if ( other != mover )
				other.Enabled = false;
		}

		mover.Enabled = true;
	}

	// Movement is the enabled mover. Called every update too, for movers game code enabled and disabled itself.
	private void UpdateMovement()
	{
		if ( !Movement.IsValid() || !Movement.Enabled )
			Movement = GetComponent<MovementLayer>() ?? Movement;

		var active = Movement.IsValid() && Movement.Enabled ? Movement : null;
		if ( active == _activeMover )
			return;

		_activeMover = active;
		if ( active.IsValid() )
			OnMovementChanged();
	}

	/// <summary>
	/// Animation layer - handles look-at and animation state
	/// </summary>
	[RequireComponent]
	public AnimationLayer Animation { get; set; }

	/// <summary>
	/// Speech layer - handles talking, lipsync, etc. Null if the NPC has none.
	/// </summary>
	public SpeechLayer Speech { get; private set; }

	protected override void OnAwake()
	{
		Senses = GetComponent<SensesLayer>( true );
		Movement = GetComponent<MovementLayer>() ?? GetComponent<MovementLayer>( true );
		_activeMover = Movement.IsValid() && Movement.Enabled ? Movement : null;
		Speech = GetComponent<SpeechLayer>( true );
	}
}
