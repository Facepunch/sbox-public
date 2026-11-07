using Sandbox.Movement;
namespace Sandbox;


public sealed partial class PlayerController : Component
{
	public MoveMode Mode { get; private set; }

	readonly List<MoveMode> _moveModes = new();

	void ChooseBestMoveMode()
	{
		_moveModes.Clear();
		Components.GetAll( _moveModes, FindMode.EnabledInSelf );

		MoveMode best = null;
		var bestScore = 0;
		foreach ( var mode in _moveModes )
		{
			var score = mode.Score( this );
			if ( best is not null && score <= bestScore ) continue;

			best = mode;
			bestScore = score;
		}

		_moveModes.Clear();

		if ( Mode == best ) return;

		Mode?.OnModeEnd( best );

		Mode = best;

		if ( Body?.PhysicsBody is { } body )
		{
			body.Sleeping = false;
		}

		Mode?.OnModeBegin();
	}
}
