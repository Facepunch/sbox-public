namespace Sandbox;

public sealed partial class PlayerController : Component
{
	/// <summary>
	/// Events from the PlayerController
	/// </summary>
	public interface IEvents : ISceneEvent<IEvents>
	{
		/// <summary>
		/// Our eye angles are changing. Allows you to change the sensitivity, or stomp all together.
		/// </summary>
		void OnEyeAngles( ref Angles angles ) { }

		/// <summary>
		/// Called after we've set the camera up
		/// </summary>
		void PostCameraSetup( CameraComponent cam ) { }

		/// <summary>
		/// The player has just jumped
		/// </summary>
		void OnJumped() { }

		/// <summary>
		/// The player has landed on the ground, after falling this distance.
		/// </summary>
		void OnLanded( float distance, Vector3 impactVelocity ) { }

		/// <summary>
		/// Used by the Using system to find components we can interact with.
		/// By default we can only interact with IPressable components.
		/// Return a component if we can use it, or else return null.
		/// </summary>
		Component GetUsableComponent( GameObject go ) { return default; }

		/// <summary>
		/// We have started using something (use was pressed)
		/// </summary>
		void StartPressing( Component target ) { }

		/// <summary>
		/// We have stopped using something
		/// </summary>
		void StopPressing( Component target ) { }

		/// <summary>
		/// We pressed USE but it did nothing
		/// </summary>
		void FailPressing() { }


		/// <summary>
		/// We have a chance to do something before input is processed
		/// </summary>
		void PreInput() { }

	}
}
