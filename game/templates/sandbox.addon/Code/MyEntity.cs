using Sandbox;
using System.Linq;

/// <summary>
/// A basic component for a Sandbox Entity that follows the nearest player (rolling around like a ball)
/// </summary>
public sealed class MyEntity : Component
{
	[RequireComponent] public Rigidbody Body { get; set; }

	[Property] public float Speed { get; set; } = 70f;

	TimeSince _timeSinceLastCheck = 0f;
	PlayerController _targetPlayer = null;

	protected override void OnFixedUpdate()
	{
		// Every 1 seconds we'll check for the closest player and store it in _targetPlayer
		if ( _timeSinceLastCheck > 1f )
		{
			_timeSinceLastCheck = 0f;
			var players = Scene.GetAllComponents<PlayerController>();
			var closestPlayer = players.OrderBy( p => p.WorldPosition.DistanceSquared( Body.WorldPosition ) ).FirstOrDefault();
			_targetPlayer = closestPlayer;
		}

		// If the player we're tracking is valid, lets move towards them
		if ( _targetPlayer.IsValid() )
		{
			var targetPosition = _targetPlayer.WorldPosition;
			var distance = targetPosition - Body.WorldPosition;
			if ( distance.Length < 256f ) return;

			// Move towards the target at our set speed
			Body.Velocity = distance.Normal * Speed;

			// Rotate like a ball in the direction we're moving
			Body.AngularVelocity = new Vector3( distance.Normal.Dot( Vector3.Right ), 0f, distance.Normal.Dot( Vector3.Up ) ) * Speed * 0.25f;
		}
	}
}
