using Sandbox.Animation;
using static Sandbox.SkinnedModelRenderer;

namespace Sandbox.MovieMaker.Properties;

#nullable enable

/// <summary>
/// Reads / writes a control parameter on a <see cref="GraphParameters"/>, from <see cref="AnimationAccessor.Parameters"/>.
/// </summary>
file sealed record GraphParamProperty<T>( ITrackProperty<GraphParameters?> Parent, string Name ) : ITrackProperty<T>
{
	private IGraphParamAccessor<T> Accessor { get; } = DefaultGraphParamAccessor.Instance as IGraphParamAccessor<T>
		?? throw new NotImplementedException( $"No IGraphParamAccessor for type {typeof( T )}" );

	public bool IsBound => Parent.Value is { } parameters
		&& parameters.TryGetKind( Name, out var kind )
		&& Accessor.Accepts( kind );

	public T Value
	{
		get => Parent.Value is { } parameters && Accessor.TryGet( parameters, Name, out var value ) ? value : default!;
		set
		{
			// Live value only - the movie writes it every frame it's playing

			if ( Parent.Value is { } parameters )
			{
				Accessor.TrySet( parameters, Name, value );
			}
		}
	}

	ITrackTarget ITrackProperty.Parent => Parent;
}

[Expose]
file sealed class GraphParamPropertyFactory : ITrackPropertyFactory<ITrackProperty<GraphParameters?>>
{
	public IEnumerable<string> GetPropertyNames( ITrackProperty<GraphParameters?> parent )
	{
		var graph = parent is { IsBound: true } ? parent.Value?.Graph : null;

		return graph?.Parameters.Select( x => x.Name ) ?? [];
	}

	public string BaseCategoryName => "Anim Graph";

	/// <summary>
	/// Any property in a <see cref="GraphParameters"/> is an anim graph parameter, but we
	/// can only determine the type if it actually exists.
	/// </summary>
	public Type GetTargetType( ITrackProperty<GraphParameters?> parent, string name )
	{
		var graph = parent is { IsBound: true } ? parent.Value?.Graph : null;

		return graph?.Parameters.FirstOrDefault( x => x.Name == name )?.Type ?? typeof( Unknown );
	}

	public ITrackProperty<T> CreateProperty<T>( ITrackProperty<GraphParameters?> parent, string name ) =>
		new GraphParamProperty<T>( parent, name );
}

file interface IGraphParamAccessor<T>
{
	bool Accepts( GraphValueKind kind );
	bool TryGet( GraphParameters parameters, string name, out T value );
	bool TrySet( GraphParameters parameters, string name, T value );
}

/// <summary>
/// Accessors for each value type in <see cref="Graph.Parameter.Type"/>.
/// </summary>
file sealed class DefaultGraphParamAccessor :
	IGraphParamAccessor<bool>, IGraphParamAccessor<float>, IGraphParamAccessor<string>,
	IGraphParamAccessor<Vector3>, IGraphParamAccessor<Transform>
{
	public static DefaultGraphParamAccessor Instance { get; } = new();

	bool IGraphParamAccessor<bool>.Accepts( GraphValueKind kind ) => kind == GraphValueKind.Bool;
	bool IGraphParamAccessor<float>.Accepts( GraphValueKind kind ) => kind == GraphValueKind.Float;
	bool IGraphParamAccessor<string>.Accepts( GraphValueKind kind ) => kind == GraphValueKind.Id;
	bool IGraphParamAccessor<Vector3>.Accepts( GraphValueKind kind ) => kind == GraphValueKind.Vector;
	bool IGraphParamAccessor<Transform>.Accepts( GraphValueKind kind ) => kind == GraphValueKind.Target;

	bool IGraphParamAccessor<bool>.TryGet( GraphParameters parameters, string name, out bool value ) => parameters.TryGetBool( name, out value );
	bool IGraphParamAccessor<float>.TryGet( GraphParameters parameters, string name, out float value ) => parameters.TryGetFloat( name, out value );
	bool IGraphParamAccessor<string>.TryGet( GraphParameters parameters, string name, out string value ) => parameters.TryGetString( name, out value );
	bool IGraphParamAccessor<Vector3>.TryGet( GraphParameters parameters, string name, out Vector3 value ) => parameters.TryGetVector( name, out value );
	bool IGraphParamAccessor<Transform>.TryGet( GraphParameters parameters, string name, out Transform value ) => parameters.TryGetTargetTransform( name, out value );

	bool IGraphParamAccessor<bool>.TrySet( GraphParameters parameters, string name, bool value ) => parameters.TrySet( name, value );
	bool IGraphParamAccessor<float>.TrySet( GraphParameters parameters, string name, float value ) => parameters.TrySet( name, value );
	bool IGraphParamAccessor<string>.TrySet( GraphParameters parameters, string name, string value ) => parameters.TrySet( name, value ?? "" );
	bool IGraphParamAccessor<Vector3>.TrySet( GraphParameters parameters, string name, Vector3 value ) => parameters.TrySet( name, value );
	bool IGraphParamAccessor<Transform>.TrySet( GraphParameters parameters, string name, Transform value ) => parameters.TrySet( name, value );
}

/// <summary>
/// Pseudo-property on an <see cref="AnimationAccessor"/>, with a sub-property for each Bool anim graph parameter
/// that's true while a one-shot pulse from <see cref="GraphParameters.Trigger"/> is waiting to be evaluated. Pulses
/// don't change the parameter's own value, so they get their own tracks.
/// </summary>
[Expose]
public sealed class GraphTriggerAccessor
{
	public const string PropertyName = "Triggers";

	public GraphParameters Parameters { get; }

	internal GraphTriggerAccessor( GraphParameters parameters )
	{
		Parameters = parameters;
	}
}

/// <summary>
/// True while a pulse is queued for a Bool parameter. Writing true queues a pulse, but only when the previous
/// write was false: a recorded pulse can span several playback frames, and should only trigger once.
/// </summary>
file sealed record GraphTriggerProperty( ITrackProperty<GraphTriggerAccessor?> Parent, string Name ) : ITrackProperty<bool>
{
	public bool IsBound => Parent.Value?.Parameters is { } parameters
		&& parameters.TryGetKind( Name, out var kind )
		&& kind == GraphValueKind.Bool;

	public bool Value
	{
		get => Parent.Value?.Parameters.IsTriggerPending( Name ) ?? false;
		set
		{
			if ( Parent.Value?.Parameters is not { } parameters ) return;

			var wasSet = ReferenceEquals( _lastWrite.Parameters, parameters ) && _lastWrite.Value;

			_lastWrite = (parameters, value);

			if ( value && !wasSet )
			{
				parameters.TryTrigger( Name );
			}
		}
	}

	/// <summary>
	/// What we last wrote, and to which renderer's parameters.
	/// </summary>
	private (GraphParameters? Parameters, bool Value) _lastWrite;

	ITrackTarget ITrackProperty.Parent => Parent;
}

[Expose]
file sealed class GraphTriggerPropertyFactory : ITrackPropertyFactory<ITrackProperty<GraphTriggerAccessor?>, bool>
{
	public string BaseCategoryName => "Anim Graph Triggers";

	/// <summary>
	/// Any property inside a <see cref="GraphTriggerAccessor"/> is a trigger, even before the graph is loaded.
	/// </summary>
	public bool PropertyExists( ITrackProperty<GraphTriggerAccessor?> parent, string name ) => true;

	public ITrackProperty<bool> CreateProperty( ITrackProperty<GraphTriggerAccessor?> parent, string name ) =>
		new GraphTriggerProperty( parent, name );

	public IEnumerable<string> GetPropertyNames( ITrackProperty<GraphTriggerAccessor?> parent )
	{
		var graph = parent is { IsBound: true } ? parent.Value?.Parameters.Graph : null;

		return graph?.Parameters.Where( x => x.Type == typeof( bool ) ).Select( x => x.Name ) ?? [];
	}
}

file sealed record GraphTriggerAccessorProperty( ITrackProperty<AnimationAccessor?> Parent ) : ITrackProperty<GraphTriggerAccessor?>
{
	public string Name => GraphTriggerAccessor.PropertyName;

	public GraphTriggerAccessor? Value
	{
		get
		{
			if ( Parent.Value?.Parameters is not { } parameters ) return null;

			// Cached so reading each trigger every frame doesn't allocate

			if ( _cached?.Parameters != parameters )
			{
				_cached = new GraphTriggerAccessor( parameters );
			}

			return _cached;
		}

		set
		{
			// Can't write (CanWrite = false)
		}
	}

	private GraphTriggerAccessor? _cached;

	bool ITrackProperty.CanWrite => false;

	ITrackTarget ITrackProperty.Parent => Parent;
}

[Expose]
file sealed class GraphTriggerAccessorPropertyFactory : ITrackPropertyFactory<ITrackProperty<AnimationAccessor?>, GraphTriggerAccessor?>
{
	public string BaseCategoryName => "Members";

	public IEnumerable<string> GetPropertyNames( ITrackProperty<AnimationAccessor?> parent ) =>
		[GraphTriggerAccessor.PropertyName];

	public bool PropertyExists( ITrackProperty<AnimationAccessor?> parent, string name ) =>
		name == GraphTriggerAccessor.PropertyName;

	public ITrackProperty<GraphTriggerAccessor?> CreateProperty( ITrackProperty<AnimationAccessor?> parent, string name ) =>
		new GraphTriggerAccessorProperty( parent );
}

#pragma warning disable CS0618

/// <summary>
/// Reads / writes an AnimGraph1 parameter on a <see cref="SkinnedModelRenderer"/>. Kept so movies recorded with
/// <see cref="SkinnedModelRenderer.Parameters"/> still play.
/// </summary>
file sealed record AnimParamProperty<T>( ITrackProperty<ParameterAccessor?> Parent, string Name ) : ITrackProperty<T>
{
	private IAnimParamAccessor<T> Accessor { get; } = DefaultAnimParamAccessor.Instance as IAnimParamAccessor<T>
		?? throw new NotImplementedException( $"No IAnimParamAccessor for type {typeof( T )}" );

	public bool IsBound => Parent.Value?.Graph?.GetParameterType( Name ) == typeof( T );

	public T Value
	{
		get => Parent.Value is { } accessor ? Accessor.Get( accessor, Name ) : default!;
		set
		{
			if ( Parent.Value is not { } accessor ) return;

			if ( accessor.IsAutoReset( Name ) )
			{
				// The graph resets these after every update, so writing the same value every frame would keep
				// re-triggering them (an attack animation restarting). Only write when the track's value changes.

				if ( ReferenceEquals( _lastWrite.Accessor, accessor ) && EqualityComparer<T>.Default.Equals( _lastWrite.Value, value ) ) return;

				_lastWrite = (accessor, value);
			}

			Accessor.Set( accessor, Name, value );
		}
	}

	/// <summary>
	/// What we last wrote to an auto-reset parameter, and to which renderer's parameters.
	/// </summary>
	private (ParameterAccessor? Accessor, T Value) _lastWrite;

	ITrackTarget ITrackProperty.Parent => Parent;
}

[Expose]
file sealed class AnimParamPropertyFactory : ITrackPropertyFactory<ITrackProperty<ParameterAccessor?>>
{
	public IEnumerable<string> GetPropertyNames( ITrackProperty<ParameterAccessor?> parent )
	{
		var graph = parent is { IsBound: true } ? parent.Value?.Graph : null;

		if ( graph is null ) yield break;

		for ( var i = 0; i < graph.ParamCount; ++i )
		{
			yield return graph.GetParameterName( i );
		}
	}

	public string BaseCategoryName => "Anim Graph";

	/// <summary>
	/// Any property in a <see cref="ParameterAccessor"/> is an anim graph parameter, but we
	/// can only determine the type if it actually exists.
	/// </summary>
	public Type GetTargetType( ITrackProperty<ParameterAccessor?> parent, string name )
	{
		var graph = parent is { IsBound: true } ? parent.Value?.Graph : null;

		return graph?.GetParameterType( name ) ?? typeof( Unknown );
	}

	public ITrackProperty<T> CreateProperty<T>( ITrackProperty<ParameterAccessor?> parent, string name ) =>
		new AnimParamProperty<T>( parent, name );
}

file interface IAnimParamAccessor<T>
{
	T Get( ParameterAccessor accessor, string name );
	void Set( ParameterAccessor accessor, string name, T value );
}

file sealed class DefaultAnimParamAccessor :
	IAnimParamAccessor<bool>, IAnimParamAccessor<byte>, IAnimParamAccessor<int>, IAnimParamAccessor<float>,
	IAnimParamAccessor<Vector3>, IAnimParamAccessor<Rotation>
{
	public static DefaultAnimParamAccessor Instance { get; } = new();

	bool IAnimParamAccessor<bool>.Get( ParameterAccessor accessor, string name ) => accessor.GetBool( name );
	byte IAnimParamAccessor<byte>.Get( ParameterAccessor accessor, string name ) => (byte)accessor.GetInt( name );
	int IAnimParamAccessor<int>.Get( ParameterAccessor accessor, string name ) => accessor.GetInt( name );
	float IAnimParamAccessor<float>.Get( ParameterAccessor accessor, string name ) => accessor.GetFloat( name );
	Vector3 IAnimParamAccessor<Vector3>.Get( ParameterAccessor accessor, string name ) => accessor.GetVector( name );
	Rotation IAnimParamAccessor<Rotation>.Get( ParameterAccessor accessor, string name ) => accessor.GetRotation( name );

	void IAnimParamAccessor<bool>.Set( ParameterAccessor accessor, string name, bool value ) => accessor.Set( name, value );
	void IAnimParamAccessor<byte>.Set( ParameterAccessor accessor, string name, byte value ) => accessor.Set( name, value );
	void IAnimParamAccessor<int>.Set( ParameterAccessor accessor, string name, int value ) => accessor.Set( name, value );
	void IAnimParamAccessor<float>.Set( ParameterAccessor accessor, string name, float value ) => accessor.Set( name, value );
	void IAnimParamAccessor<Vector3>.Set( ParameterAccessor accessor, string name, Vector3 value ) => accessor.Set( name, value );
	void IAnimParamAccessor<Rotation>.Set( ParameterAccessor accessor, string name, Rotation value ) => accessor.Set( name, value );
}
