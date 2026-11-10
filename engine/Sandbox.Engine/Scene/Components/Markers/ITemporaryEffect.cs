namespace Sandbox;

public partial class Component
{
	/// <summary>
	/// Allows components to indicate their state in a generic way. This is useful if you have a temporary effect system in which
	/// you want to remove GameObjects when their effects have all finished.
	/// </summary>
	public interface ITemporaryEffect
	{
		/// <summary>
		/// Should return true if the effect is active in a visible way
		/// </summary>
		bool IsActive { get; }

		/// <summary>
		/// Indicates to the target object that we want it to die. If it's looping then
		/// it should stop now and put itself in a state where it will eventually die.
		/// </summary>
		void DisableLooping() { }

		/// <summary>
		/// Disable the any looping effects. This indicates to the target object that we want it to die soon.
		/// </summary>
		public static void DisableLoopingEffects( GameObject go )
		{
			if ( !go.IsValid() ) return;

			// Walks in place instead of collecting a list, this is called per particle when a follower dies
			go.Components.Execute<ITemporaryEffect>( static fx => fx.DisableLooping(), FindMode.EverythingInSelfAndDescendants );
		}
	}

}
