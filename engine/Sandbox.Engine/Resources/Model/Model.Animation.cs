namespace Sandbox;

public partial class Model
{
	/// <summary>
	/// Number of animations this model has.
	/// </summary>
	public int AnimationCount => native.GetNumAnim();

	/// <summary>
	/// Returns name of an animation at given animation index.
	/// </summary>
	/// <param name="animationIndex">Animation index to get name of, starting at 0.</param>
	/// <returns>Name of the animation.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when given index exceeds range of [0,AnimationCount-1]</exception>
	public string GetAnimationName( int animationIndex )
	{
		if ( animationIndex < 0 || animationIndex >= AnimationCount )
			throw new ArgumentOutOfRangeException( "animationIndex", $"Tried to access out of range animation index {animationIndex}, range is 0-{AnimationCount - 1}" );

		return native.GetAnimationName( animationIndex );
	}

	private List<string> _animationNames;
	public IReadOnlyList<string> AnimationNames => _animationNames ??= Enumerable.Range( 0, AnimationCount )
			.Select( GetAnimationName ).ToList();
}
