namespace Sandbox;

public sealed partial class ModelBuilder
{
	private readonly List<AnimationBuilder> _animations = [];

	/// <summary>
	/// Adds an animation to this model and returns a builder to construct the animation.
	/// Duplicate names are given a numeric suffix.
	/// Animations without frames are omitted from the finished model.
	/// </summary>
	/// <param name="name">The name of the animation.</param>
	/// <param name="frameRate">The frames per second of the animation.</param>
	/// <returns>A builder for the new animation.</returns>
	public AnimationBuilder AddAnimation( string name, float frameRate )
	{
		var uniqueName = name;
		var suffix = 1;

		while ( _animations.Any( builder => builder.Name == uniqueName ) )
		{
			uniqueName = $"{name}_{suffix++}";
		}

		var builder = new AnimationBuilder { Name = uniqueName, FrameRate = frameRate };
		_animations.Add( builder );
		return builder;
	}

	private unsafe CAnimationGroupBuilder CreateAnimationGroup()
	{
		if ( _animations.Count == 0 || _animations.Sum( animation => animation.FrameCount ) == 0 )
			return default;

		var builder = CAnimationGroupBuilder.Create();
		try
		{
			PopulateAnimationGroup( builder );
			return builder;
		}
		catch
		{
			builder.DeleteThis();
			throw;
		}
	}

	private unsafe void PopulateAnimationGroup( CAnimationGroupBuilder builder )
	{
		foreach ( var animation in _animations )
		{
			if ( animation.FrameCount == 0 )
				continue;

			var animationIndex = builder.AddAnimation();
			builder.SetName( animationIndex, animation.Name );
			builder.SetFrameRate( animationIndex, animation.FrameRate );
			builder.SetLooping( animationIndex, animation.Looping );
			builder.SetDelta( animationIndex, animation.Delta );
			builder.SetDisableInterpolation( animationIndex, animation.DisableInterpolation );

			for ( var frameIndex = 0; frameIndex < animation.FrameCount; frameIndex++ )
			{
				var boneTransforms = animation.GetFrame( frameIndex );
				fixed ( Transform* framePointer = boneTransforms )
				{
					builder.AddFrame( animationIndex, (IntPtr)framePointer, boneTransforms.Length );
				}
			}
		}
	}
}
