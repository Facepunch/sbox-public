using Sandbox.Animation;

namespace Sandbox;

public abstract partial class Component
{
	/// <summary>
	/// Automatically receives game-defined animation events from skinned model renderers on
	/// this GameObject or its descendants, while this component is active.
	/// </summary>
	public interface IAnimationEvent
	{
		/// <summary>
		/// Called when the renderer fires a game-defined animation event.
		/// </summary>
		/// <param name="data">The event payload. Treat it as read-only; other handlers receive the same instance.</param>
		void OnAnimationEvent( AnimationEvent data );
	}
}
