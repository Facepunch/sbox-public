namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// When added to a <see cref="MovieRecorderOptions"/>, handles how to capture the properties of
/// a particular component type.
/// </summary>
public interface IComponentCapturer
{
	/// <summary>
	/// Returns true if this recorder can handle the given <paramref name="componentType"/>.
	/// </summary>
	bool SupportsType( Type componentType );

	/// <summary>
	/// Handle capturing the properties of the given <paramref name="component"/> instance.
	/// Find properties to capture using <see cref="IMovieTrackRecorder.Property"/>, then call <see cref="IMovieTrackRecorder.Capture"/> on them.
	/// </summary>
	void Capture( IMovieTrackRecorder recorder, Component component );
}

/// <summary>
/// Generic helper implementation of <see cref="IComponentCapturer"/>.
/// </summary>
/// <typeparam name="T">Component type this capturer supports. Will match on derived component types too.</typeparam>
public abstract class ComponentCapturer<T> : IComponentCapturer
	where T : class
{
	bool IComponentCapturer.SupportsType( Type componentType )
	{
		return componentType.IsAssignableTo( typeof( T ) );
	}

	void IComponentCapturer.Capture( IMovieTrackRecorder recorder, Component component )
	{
		if ( component is T typedComponent )
		{
			OnCapture( recorder, typedComponent );
		}
	}

	/// <inheritdoc cref="IComponentCapturer.Capture"/>
	protected abstract void OnCapture( IMovieTrackRecorder recorder, T component );
}
