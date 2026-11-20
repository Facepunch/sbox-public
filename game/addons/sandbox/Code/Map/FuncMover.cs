using System.Text.RegularExpressions;

/// <summary>
/// Moves and Rotates an Object.
/// </summary>
[Category( "Gameplay" ), Icon( "open_with" ), EditorHandle( Icon = "🚚" ), Tint(EditorTint.Green)]
public sealed class FuncMover : Component
{
	[Property] bool StartOn { get; set; } = true;

	//Linear Move
	[Property, FeatureEnabled( "LinearMove", Icon = "🚀" )] public bool LinearMove { get; set; } = true;
	[Property, Feature( "LinearMove" )] public Vector3 LinearDistance { get; set; } = Vector3.Up;
	[Property, Feature( "LinearMove" )][Sync( SyncFlags.FromHost )] public float LinearSpeed { get; set; } = 10.0f;
	[Property, Feature( "LinearMove" )] public bool LinearLoop { get; set; } = true;
	[Property, Feature( "LinearMove" ), ShowIf(nameof(LinearLoop),true)] public float LinearPauseDuration { get; set; } = 0.0f;

	//Rotate Move
	[Property, FeatureEnabled( "RotateMove", Icon = "🔄" )] public bool RotateMove { get; set; } = false;
	[Property, Feature( "RotateMove" )] public Angles RotationDirection { get; set; } = Angles.Zero;
	[Property, Feature( "RotateMove" )][Sync( SyncFlags.FromHost )] public float RotationSpeed { get; set; } = 10.0f;

	[Property, ReadOnly, Sync( SyncFlags.FromHost ), Group("Debug")]
	bool IsOn { get; set; }

	Transform startPos;
	Vector3 targetPos;
	private bool movingToTarget = true;

	TimeSince pauseTimer = 0f;
	bool isPaused = false;

	protected override void OnStart()
	{
		base.OnStart();

		IsOn = StartOn;

		startPos = LocalTransform;
		targetPos = startPos.Position + LinearDistance;
	}

	protected override void OnFixedUpdate()
	{
		if ( LinearMove )
		{
			if ( isPaused && pauseTimer > LinearPauseDuration )
			{
				isPaused = false;
			}

			Vector3 moveDirection = (startPos.Position - WorldPosition).Normal;

			if ( LinearLoop )
			{
				moveDirection = movingToTarget ? (targetPos - WorldPosition).Normal : (startPos.Position - WorldPosition).Normal;
			}
			else
			{
				moveDirection = IsOn ? (targetPos - WorldPosition).Normal : (startPos.Position - WorldPosition).Normal;
			}

			if ( !isPaused && LinearLoop )
			{
				WorldPosition += moveDirection * LinearSpeed * Time.Delta;
			}
			else if (!LinearLoop )
			{
				WorldPosition += moveDirection * LinearSpeed * Time.Delta;
			}
			
			if ( movingToTarget && Vector3.DistanceBetween( WorldPosition, targetPos ) <= 1 )
			{
				movingToTarget = false;
				WorldPosition = targetPos;
				isPaused = true;
				pauseTimer = 0f;
			}
			else if ( !movingToTarget && Vector3.DistanceBetween( WorldPosition, startPos.Position ) <= 1 )
			{
				movingToTarget = true;
				WorldPosition = startPos.Position;
				isPaused = true;
				pauseTimer = 0f;
			}

		}

		if ( RotateMove )
		{
			if(!IsOn ) return;
			var delta = Time.Delta * RotationSpeed;
			var rot = Rotation.From( RotationDirection ) * delta;
			WorldRotation *= rot;
		}
	}

	protected override void DrawGizmos()
	{
		base.DrawGizmos();

		if ( LinearMove )
		{
			Gizmo.Draw.Color = Color.Green;
			Gizmo.Draw.LineSphere( 0, 10f );
			Gizmo.Draw.Color = Color.Red;
			Gizmo.Draw.LineSphere( LinearDistance, 10f );
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.Line( 0, LinearDistance );

			Gizmo.Draw.Color = Color.Cyan;
			Gizmo.Draw.SolidSphere( 0 + (LinearDistance * (MathF.Sin( Time.Now * (LinearSpeed / 100) ).Remap( -1, 1 ))), 10 );
		}
	}

	public void Toggle() { ToggleInternal(); }

	[Rpc.Host]
	void ToggleInternal()
	{
		IsOn = !IsOn;
	}
	[Rpc.Host, Button( "TurnOn", "radio_button_checked" ), Group( "Debug" )]
	public void TurnOn()
	{
		IsOn = true;
	}
	[Rpc.Host, Button("TurnOff", "radio_button_unchecked" ), Group( "Debug" )]
	public void TurnOff()
	{
		IsOn = false;
	}
	[Rpc.Host]
	public void SetLinearSpeed( float speed )
	{
		LinearSpeed = speed;
	}
	[Rpc.Host]
	public void SetRotationSpeed( float speed )
	{
		RotationSpeed = speed;
	}
}
