using Sandbox.Movement;

namespace Sandbox;

[Expose]
[Icon( "event_seat" ), Group( "Game" ), Title( "Chair" )]
public partial class BaseChair : Component, Component.IPressable, ISitTarget
{
	[Expose]
	public enum AnimatorSitPose
	{
		Standing = 0,
		Chair = 1,
		ChairForward = 2,
		ChairCrossed = 3,
		KneelingOpen = 4,
		Kneeling = 5,
		Ground = 6,
		GroundCrossed = 7,
	}

	/// <summary>
	/// A GameObject representing the seat position
	/// </summary>
	[Header( "Seat" )]
	[Property] public GameObject SeatPosition { get; set; }

	/// <summary>
	/// The sitting pose to use when a player is seated
	/// </summary>
	[Header( "Animation" )]
	[Property] public AnimatorSitPose SitPose { get; set; } = AnimatorSitPose.Chair;

	/// <summary>
	/// Height offset for sitting position, from -1 (lowest) to 1 (highest)
	/// </summary>
	[Range( -1, 1 )]
	[Property] public float SitHeight { get; set; } = 0;

	/// <summary>
	/// A GameObject representing the eye position
	/// </summary>
	[Header( "Eyes" )]
	[Property] public GameObject EyePosition { get; set; }

	/// <summary>
	/// Pitch range for seated players
	/// </summary>
	[Property] public Vector2 PitchRange { get; set; } = new Vector2( -90, 70 );

	/// <summary>
	/// Yaw range for seated players
	/// </summary>
	[Property] public Vector2 YawRange { get; set; } = new Vector2( -120, 120 );

	[Header( "Exit" )]
	[Property] public GameObject[] ExitPoints { get; set; }

	/// <summary>
	/// Chair is usable if the player can enter
	/// </summary>
	public bool CanPress( IPressable.Event e )
	{
		var player = e.Source as PlayerController;
		if ( player is null ) return false;
		return CanEnter( player );
	}

	/// <summary>
	/// Called when the player has pressed to use the chair. 
	/// Only called if CanPress returned true.
	/// </summary>
	public bool Press( IPressable.Event e )
	{
		var player = e.Source as PlayerController;
		if ( player is null ) return false;

		EnterChair( player );
		return true;
	}

	/// <summary>
	/// Called on the host to enter the chair.
	/// </summary>
	[Rpc.Host]
	private void EnterChair( PlayerController player )
	{
		if ( player.Network.Owner != Rpc.Caller )
			return;

		if ( !CanEnter( player ) ) return;

		using ( Rpc.FilterInclude( player.Network.Owner ) )
		{
			// TODO - when https://github.com/Facepunch/sbox/issues/3270 is done
			// we should be able to set the parent here on the host and have it replicate
			// rather than telling the client to parent themselves!

			Sit( player );
		}
	}

	/// <summary>
	/// Called on the client to place the player in the chair.
	/// </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void Sit( PlayerController player )
	{
		var seatPos = SeatPosition ?? GameObject;

		player.Body.Enabled = false;
		player.ColliderObject.Enabled = false;

		player.GameObject.SetParent( seatPos, false );
		player.GameObject.LocalTransform = global::Transform.Zero;
	}

	/// <summary>
	/// Called on the host to request leaving the chair.
	/// </summary>
	[Rpc.Host]
	public void AskToLeave( PlayerController player )
	{
		if ( player.Network.Owner != Rpc.Caller )
			return;

		if ( GetOccupant() != player )
			return;

		if ( !CanLeave( player ) ) return;

		using ( Rpc.FilterInclude( player.Network.Owner ) )
		{
			// TODO - when https://github.com/Facepunch/sbox/issues/3270 is done
			// we should be able to set the parent here on the host and have it replicate
			// rather than telling the client to parent themselves!

			Eject( player );
		}
	}

	/// <summary>
	/// Return true if this player can leave the chair
	/// </summary>
	public virtual bool CanLeave( PlayerController player )
	{
		return true;
	}

	/// <summary>
	/// Called on the client to eject the player from the chair.
	/// </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void Eject( PlayerController player )
	{
		if ( GetOccupant() != player )
			return;

		var exitPoint = FindBestExitPoint();

		var seatPos = SeatPosition ?? GameObject;
		player.GameObject.SetParent( null, true );

		player.WorldPosition = exitPoint;
		player.EyeAngles = WorldRotation.Inverse * player.EyeAngles;
	}

	/// <summary>
	/// Returns a position to place the player when they exit the chair. This searches
	/// through ExitPoints to find the best one, which is usually the one the player is most
	/// facing towards.
	/// </summary>
	public Vector3 FindBestExitPoint()
	{
		if ( ExitPoints == null || ExitPoints.Length == 0 ) return (SeatPosition ?? GameObject).WorldPosition;

		return ExitPoints.OrderByDescending( ScoreExitPoint ).First().WorldPosition;
	}

	private float ScoreExitPoint( GameObject exitPoint )
	{
		var eyeForward = GetOccupant()?.EyeTransform.Forward ?? WorldTransform.Forward;

		var seatPos = (SeatPosition ?? GameObject).WorldPosition;
		var toExit = exitPoint.WorldPosition - seatPos;
		var forwardScore = Vector3.Dot( toExit.Normal, eyeForward );

		// todo: Trace test? Make sure we're not going through the world?

		return forwardScore * 1000.0f;
	}

	/// <summary>
	/// Return true if this player can enter the chair
	/// </summary>
	public virtual bool CanEnter( PlayerController player )
	{
		if ( player is null ) return false;
		if ( IsOccupied ) return false;

		return true;
	}

	/// <summary>
	/// Get the transform representing the eye position when seated
	/// </summary>
	public virtual Transform GetEyeTransform()
	{
		var seatPos = EyePosition ?? SeatPosition ?? GameObject;
		return seatPos.WorldTransform;
	}

	/// <summary>
	/// Returns true if the chair is currently occupied
	/// </summary>
	public bool IsOccupied => GetOccupant().IsValid();

	/// <summary>
	/// Gets the player that is currently occupying the chair
	/// </summary>
	public PlayerController GetOccupant() => GetComponentInChildren<PlayerController>();

	/// <summary>
	/// Called to update the player's animator when seated
	/// </summary>
	public virtual void UpdatePlayerAnimator( PlayerController controller, SkinnedModelRenderer renderer )
	{
		// Make sure the controller is aligned with the chair
		controller.LocalTransform = global::Transform.Zero;

		// Make sure the body rotation is zero
		renderer.LocalRotation = Rotation.Identity;

		// Update animation parameters
		renderer.Set( "sit", (int)SitPose );
		renderer.Set( "sit_offset_height", SitHeight * 12.0f );
		renderer.Set( "b_grounded", true );
		renderer.Set( "b_climbing", false );
		renderer.Set( "b_swim", false );
		renderer.Set( "duck", false );

		// Look in the direction the player is aiming
		var eyesForward = controller.EyeTransform.Forward;
		renderer.SetLookDirection( "aim_eyes", eyesForward, controller.AimStrengthEyes );
		renderer.SetLookDirection( "aim_head", eyesForward, controller.AimStrengthHead );
		renderer.SetLookDirection( "aim_body", eyesForward, controller.AimStrengthBody );

		// Clamp the eye angles
		ClampEyes( controller );
	}

	/// <summary>
	/// Clamps the eye angles of a seated player between the PitchRange and YawRange
	/// </summary>
	protected void ClampEyes( PlayerController controller )
	{
		var ea = controller.EyeAngles;
		ea.pitch = MathX.Clamp( ea.pitch, PitchRange.x, PitchRange.y );
		ea.yaw = MathX.Clamp( ea.yaw, YawRange.x, YawRange.y );
		controller.EyeAngles = ea;
	}

	/// <summary>
	/// Calculates the eye transform for a seated player
	/// </summary>
	public virtual Transform CalculateEyeTransform( PlayerController controller )
	{
		// clamp the player's eye angles
		ClampEyes( controller );

		var seatEyeTx = GetEyeTransform();

		var transform = new Transform();
		transform.Position = seatEyeTx.Position;
		transform.Rotation = WorldRotation * controller.EyeAngles.ToRotation();

		return transform;
	}

	/// <summary>
	/// Draws the player model sitting in the chair if it's selected
	/// </summary>
	protected override void DrawGizmos()
	{
		var selectedObject = Scene.Editor?.SelectedGameObject;
		if ( selectedObject == null ) return;

		if ( selectedObject != GameObject && selectedObject != SeatPosition && selectedObject != EyePosition )
			return;

		var seatPos = (SeatPosition ?? GameObject).WorldTransform;
		var localSeatPos = GameObject.WorldTransform.ToLocal( seatPos );

		var so = Gizmo.Draw.Model( "models/citizen/citizen.vmdl", localSeatPos.WithScale( 1 ) );
		so.ColorTint = Color.White.WithAlpha( 0.6f );
		so.SetAnimParameter( "sit", (int)SitPose );
		so.SetAnimParameter( "sit_offset_height", SitHeight * 12.0f );
		so.SetAnimParameter( "b_grounded", true );
		so.SetAnimParameter( "b_climbing", false );
		so.SetAnimParameter( "b_swim", false );
		so.SetAnimParameter( "duck", false );
		so.Update( RealTime.Delta * 10.0f );
	}

	/// <summary>
	/// The title of this chair's tooltip. Empty to disable.
	/// </summary>
	[Property, Feature( "Tooltip" )]
	public string TooltipTitle { get; set; } = "Sit";

	/// <summary>
	/// The icon for this chair's tooltip. Either Material Icons or an Emoji.
	/// </summary>
	[Property, Feature( "Tooltip" )]
	public string TooltipIcon { get; set; } = "airline_seat_recline_normal";

	/// <summary>
	/// A longer description for this chair's tooltip.
	/// </summary>
	[Property, Feature( "Tooltip" )]
	public string TooltipDescription { get; set; } = "";

	public virtual IPressable.Tooltip? GetTooltip( IPressable.Event e )
	{
		if ( string.IsNullOrWhiteSpace( TooltipTitle ) && string.IsNullOrWhiteSpace( TooltipIcon ) )
			return default;

		if ( IsOccupied )
			return default;

		return new IPressable.Tooltip( TooltipTitle, TooltipIcon, TooltipDescription );
	}
}

