namespace Sandbox;

/// <summary>
/// An effect that gets aimed after it spawns - a tracer, beam or laser. Whoever spawns the effect
/// feeds in whatever context it has. Only <see cref="SetTarget"/> is required - the rest default to
/// doing nothing, so an effect implements just what it uses.
/// </summary>
public interface ITargetedEffect
{
	/// <summary>
	/// The point the effect travels to or points at. Anchor it to a GameObject to follow it.
	/// </summary>
	void SetTarget( SceneAnchor target );

	/// <summary>
	/// The point the effect starts from, when that isn't where the effect spawned.
	/// </summary>
	void SetStartPoint( SceneAnchor start ) { }

	/// <summary>
	/// The surface normal at the target, when the target is a hit - for effects that orient to the
	/// surface (sparks, splashes).
	/// </summary>
	void SetNormal( Vector3 normal ) { }
}
