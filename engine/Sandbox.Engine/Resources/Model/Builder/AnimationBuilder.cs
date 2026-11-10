using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Builds an animation for a <see cref="Model"/>.
/// </summary>
public sealed class AnimationBuilder
{
	/// <summary>
	/// The name of the animation.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// The frames per second of the animation.
	/// </summary>
	public float FrameRate { get; set; }

	/// <summary>
	/// Whether the animation loops.
	/// </summary>
	public bool Looping { get; set; }

	/// <summary>
	/// Whether the animation is applied additively on top of the base pose.
	/// </summary>
	public bool Delta { get; set; }

	/// <summary>
	/// Whether interpolation between frames is disabled.
	/// </summary>
	public bool DisableInterpolation { get; set; }

	/// <summary>
	/// The number of frames in the animation.
	/// </summary>
	public int FrameCount => _frames.Count;

	private struct Frame( int offset, int length )
	{
		public int Offset = offset;
		public int Length = length;
	}

	private readonly List<Transform> _boneTransforms = [];
	private readonly List<Frame> _frames = [];

	internal AnimationBuilder()
	{
	}

	/// <summary>
	/// Sets the name of the animation.
	/// </summary>
	/// <param name="name">The animation name.</param>
	public AnimationBuilder WithName( string name )
	{
		Name = name;
		return this;
	}

	/// <summary>
	/// Sets the frames per second of the animation.
	/// </summary>
	/// <param name="frameRate">The playback frame rate.</param>
	public AnimationBuilder WithFrameRate( float frameRate )
	{
		FrameRate = frameRate;
		return this;
	}

	/// <summary>
	/// Sets whether the animation loops.
	/// </summary>
	/// <param name="looping">Whether the animation should loop.</param>
	public AnimationBuilder WithLooping( bool looping = true )
	{
		Looping = looping;
		return this;
	}

	/// <summary>
	/// Sets whether the animation is applied additively on top of the base pose.
	/// </summary>
	/// <param name="delta">Whether the animation should be additive.</param>
	public AnimationBuilder WithDelta( bool delta = true )
	{
		Delta = delta;
		return this;
	}

	/// <summary>
	/// Sets whether interpolation between frames is disabled.
	/// </summary>
	/// <param name="disableInterpolation">Whether frame interpolation should be disabled.</param>
	public AnimationBuilder WithInterpolationDisabled( bool disableInterpolation = true )
	{
		DisableInterpolation = disableInterpolation;
		return this;
	}

	/// <summary>
	/// Adds a frame using transforms in skeleton bone order.
	/// </summary>
	/// <param name="boneTransforms">One transform for each animated bone. Empty frames are ignored.</param>
	public AnimationBuilder AddFrame( Span<Transform> boneTransforms )
	{
		if ( boneTransforms.IsEmpty )
			return this;

		_frames.Add( new Frame( _boneTransforms.Count, boneTransforms.Length ) );
		_boneTransforms.AddRange( boneTransforms );

		return this;
	}

	/// <summary>
	/// Adds a frame using transforms in skeleton bone order.
	/// </summary>
	/// <param name="boneTransforms">One transform for each animated bone. Null or empty frames are ignored.</param>
	public AnimationBuilder AddFrame( List<Transform> boneTransforms )
	{
		if ( boneTransforms is null || boneTransforms.Count == 0 )
			return this;

		AddFrame( CollectionsMarshal.AsSpan( boneTransforms ) );

		return this;
	}

	internal ReadOnlySpan<Transform> GetFrame( int frameIndex )
	{
		if ( frameIndex < 0 || frameIndex >= FrameCount )
			throw new ArgumentOutOfRangeException( nameof( frameIndex ), $"Frame index {frameIndex} is out of range. Must be between 0 and {FrameCount - 1}." );

		var frame = _frames[frameIndex];
		return CollectionsMarshal.AsSpan( _boneTransforms ).Slice( frame.Offset, frame.Length );
	}
}
