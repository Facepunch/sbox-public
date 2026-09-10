namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// Collects properties being controlled by a <see cref="IMovieClip"/>,
/// then applies them in a batch with <see cref="Apply"/>.
/// </summary>
public sealed class MovieUpdateBuilder( TrackBinder? binder = null )
{
	public TrackBinder Binder { get; } = binder ?? TrackBinder.Default;

	private readonly Dictionary<ITrackProperty, int> _indices = new();
	private readonly List<(ITrackProperty Target, object? Value)> _properties = new();

	/// <summary>
	/// Adds property writes from <paramref name="clip"/> at the given <paramref name="time"/>
	/// to this update.
	/// </summary>
	public void Add( IMovieClip clip, MovieTime time )
	{
		foreach ( var track in clip.GetTracks( time ) )
		{
			if ( track is not IPropertyTrack propertyTrack ) continue;

			Add( propertyTrack, time );
		}
	}

	/// <summary>
	/// Adds property writes from <paramref name="propertyTrack"/> at the given <paramref name="time"/>
	/// to this update.
	/// </summary>
	public void Add( IPropertyTrack propertyTrack, MovieTime time )
	{
		if ( Binder.Get( propertyTrack ) is not { IsBound: true, CanWrite: true } property ) return;

		if ( propertyTrack.TryGetValue( time, out var value ) )
		{
			Add( property, value );
		}
		else if ( property.HasDefaultValue )
		{
			AddDefault( property );
		}
	}

	/// <summary>
	/// Adds writing <paramref name="value"/> to <paramref name="property"/> to this update.
	/// </summary>
	public void Add( ITrackProperty property, object? value )
	{
		Assert.AreEqual( Binder, property.Binder );

		if ( _indices.TryGetValue( property, out var index ) )
		{
			_properties[index] = (property, value);
			return;
		}

		_indices[property] = _properties.Count;
		_properties.Add( (property, value) );
	}

	/// <summary>
	/// Adds writing the default value of <paramref name="property"/> to this update.
	/// This will only happen if no other tracks write to this property.
	/// </summary>
	public void AddDefault( ITrackProperty property )
	{
		if ( property.HasDefaultValue && !Contains( property ) )
		{
			Add( property, property.DefaultValue );
		}
	}

	private bool Contains( ITrackProperty property )
	{
		return _indices.ContainsKey( property );
	}

	/// <summary>
	/// Applies all property writes in a <see cref="CallbackBatch"/>.
	/// </summary>
	public bool Apply()
	{
		if ( _properties.Count == 0 )
		{
			return false;
		}

		// TODO: move ClearBones / UpdateAnimationPlaybackRate etc here, avoid duplication in editor code

		using var sceneScope = Binder.Scene.Push();

		// We need to batch any property changes in case we're setting Enabled on multiple
		// components / game objects. This batch will make sure OnEnabled gets called in the
		// correct order.

		using var batchScope = CallbackBatch.Batch();

		foreach ( var item in _properties )
		{
			item.Target.Value = item.Value;
		}

		return true;
	}

	/// <summary>
	/// Clears all property writes so a new update can be built.
	/// </summary>
	public void Clear()
	{
		_indices.Clear();
		_properties.Clear();
	}
}
