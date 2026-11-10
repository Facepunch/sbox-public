
namespace Sandbox;

public partial class GameObject
{
	/// <summary>
	/// The world transform of the game object.
	/// </summary>
	[ActionGraphInclude, Group( "Transform/World" )]
	public Transform WorldTransform
	{
		get => _gameTransform.World;
		set => _gameTransform.World = value;
	}

	/// <summary>
	/// The world position of the game object.
	/// </summary>
	[ActionGraphInclude, Group( "Transform/World" )]
	public Vector3 WorldPosition
	{
		get => WorldTransform.Position;
		set
		{
			// Same guard the old Transform.Position accessor had - a NaN position
			// silently poisons the whole hierarchy, catch it at the source.
			if ( value.IsNaN )
				throw new System.ArgumentOutOfRangeException( nameof( value ), "Position is NaN" );

			WorldTransform = WorldTransform.WithPosition( value );
		}
	}

	/// <summary>
	/// The world rotation of the game object.
	/// </summary>
	[ActionGraphInclude, Group( "Transform/World" )]
	public Rotation WorldRotation
	{
		get => WorldTransform.Rotation;
		set => WorldTransform = WorldTransform.WithRotation( value );
	}

	/// <summary>
	/// The world scale of the game object.
	/// </summary>
	[ActionGraphInclude, Group( "Transform/World" )]
	public Vector3 WorldScale
	{
		get => WorldTransform.Scale;
		set => WorldTransform = WorldTransform.WithScale( value );
	}
}
