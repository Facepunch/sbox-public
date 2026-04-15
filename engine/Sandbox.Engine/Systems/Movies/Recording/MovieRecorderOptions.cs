using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Sandbox.Internal;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// Returns <see langword="null"/> if the passed <paramref name="gameObject"/> shouldn't be recorded.
/// Called once per object.
/// </summary>
public delegate bool MovieRecorderFilter( GameObject gameObject );

/// <summary>
/// Called each time <see cref="MovieRecorder.Capture"/> is invoked.
/// </summary>
public delegate void MovieRecorderAction( MovieRecorder recorder );

/// <summary>
/// Call this static method when building <see cref="MovieRecorderOptions.Default"/>. The method
/// must have exactly one parameter, of type <see cref="MovieRecorderOptions"/>, and must return
/// a <see cref="MovieRecorderOptions"/>.
/// </summary>
[AttributeUsage( AttributeTargets.Method )]
public sealed class DefaultMovieRecorderOptionsAttribute : Attribute;

/// <summary>
/// Configures a <see cref="MovieRecorder"/>, deciding how often it captures and which properties
/// should be recorded.
/// </summary>
/// <param name="SampleRate">How often to capture the value of recorded properties.</param>
public sealed record MovieRecorderOptions( int SampleRate = MovieRecorderOptions.DefaultSampleRate )
{
	/// <summary>
	/// Default value for <see cref="SampleRate"/>.
	/// </summary>
	public const int DefaultSampleRate = 30;

	/// <summary>
	/// Default options, using <see cref="WithDefaultCaptureActions"/> and <see cref="WithDefaultComponentCapturers"/>.
	/// </summary>
	[field: MaybeNull]
	public static MovieRecorderOptions Default => field ??= BuildDefault();

	/// <summary>
	/// Decide which objects are allowed to be recorded. Called the first time a <see cref="GameObject"/> is passed to
	/// <see cref="MovieRecorder.GetTrackRecorder(GameObject)"/>, which will return <see langword="null"/> if any
	/// delegate in this list returns <see langword="false"/>.
	/// </summary>
	public ImmutableArray<MovieRecorderFilter> Filters { get; init; } = [];

	/// <summary>
	/// Delegates called each time <see cref="MovieRecorder.Capture"/> is invoked, to control which objects should be recorded.
	/// These actions will call <see cref="IMovieTrackRecorder.Capture"/> on one or more track recorders.
	/// </summary>
	public ImmutableArray<MovieRecorderAction> CaptureActions { get; init; } = [];

	/// <summary>
	/// When <see cref="IMovieTrackRecorder.Capture"/> is called on a component track, any instances in this list that
	/// match the component type will be used to decide which properties on that component should be recorded.
	/// </summary>
	public ImmutableArray<IComponentCapturer> ComponentCapturers { get; init; } = [];

	public MovieRecorderOptions WithFilter( MovieRecorderFilter filter )
	{
		return this with { Filters = [.. Filters, filter] };
	}

	public MovieRecorderOptions WithCaptureAction( MovieRecorderAction action )
	{
		return this with { CaptureActions = [.. CaptureActions, action] };
	}

	public MovieRecorderOptions WithComponentCapturer<T>()
		where T : IComponentCapturer, new()
	{
		return this with { ComponentCapturers = [.. ComponentCapturers, new T()] };
	}

	public MovieRecorderOptions WithComponentCapturer( IComponentCapturer recorder )
	{
		return this with { ComponentCapturers = [.. ComponentCapturers, recorder] };
	}

	public MovieRecorderOptions WithCaptureAll<T>( Func<T, bool>? condition = null )
		where T : Component
	{
		return WithCaptureAction( recorder =>
			{
				foreach ( var component in recorder.Scene.GetAllComponents<T>() )
				{
					if ( condition?.Invoke( component ) is false ) continue;

					recorder.GetTrackRecorder( component )?.Capture();
				}
			} );
	}

	public MovieRecorderOptions WithDefaultComponentCapturers()
	{
		var componentRecorders = new List<IComponentCapturer>();

		// Add recorders from TypeLibrary

		foreach ( var type in GlobalGameNamespace.TypeLibrary.GetTypes<IComponentCapturer>() )
		{
			if ( type.IsAbstract ) continue;
			if ( type.IsGenericType ) continue;

			// Check for a parameterless constructor

			if ( type.TargetType.GetConstructor( Type.EmptyTypes ) is null ) continue;

			try
			{
				componentRecorders.Add( type.Create<IComponentCapturer>() ?? throw new Exception( $"Unable to create {type.Name}" ) );
			}
			catch ( Exception ex )
			{
				Log.Warning( ex );
			}
		}

		return this with { ComponentCapturers = [.. ComponentCapturers, .. componentRecorders] };
	}

	public MovieRecorderOptions WithDefaultCaptureActions()
	{
		return this
			.WithCaptureAll<CameraComponent>( x => !x.IsSceneEditorCamera )
			.WithCaptureAll<MapInstance>()
			.WithCaptureAll<Renderer>()
			.WithCaptureAll<Light>()
			.WithCaptureAll<AmbientLight>()
			.WithCaptureAll<ParticleEffect>()
			.WithCaptureAll<ParticleEmitter>()
			.WithCaptureAll<SoundPointComponent>();
	}

	public MovieRecorderOptions WithCaptureGameObject( GameObject gameObject ) =>
		WithCaptureAction( recorder => recorder.GetTrackRecorder( gameObject )?.Capture() );

	public MovieRecorderOptions WithCaptureComponent( Component component ) =>
		WithCaptureAction( recorder => recorder.GetTrackRecorder( component )?.Capture() );

	private static MovieRecorderOptions BuildDefault()
	{
		var options = new MovieRecorderOptions()
			.WithDefaultCaptureActions()
			.WithDefaultComponentCapturers();

		foreach ( var (method, _) in GlobalGameNamespace.TypeLibrary.GetMethodsWithAttribute<DefaultMovieRecorderOptionsAttribute>() )
		{
			try
			{
				options = (MovieRecorderOptions)((MethodInfo)method.MemberInfo).Invoke( null, [options] )!;
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, "Exception while building default movie recorder options." );
			}
		}

		return options;
	}
}
