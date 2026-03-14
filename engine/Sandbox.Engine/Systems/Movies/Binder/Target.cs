using System;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// <para>
/// Something in the scene that is being controlled by an <see cref="ITrack"/>.
/// This could be a <see cref="GameObject"/> or <see cref="Component"/> reference, or a property contained
/// within another <see cref="ITrackTarget"/>.
/// </para>
/// <para>
/// These targets are created using <see cref="TrackBinder.Get(ITrack)"/>.
/// </para>
/// <para>
/// If <see cref="IsBound"/> is true, this target is connected to a live instance of something in the scene,
/// so accessing it will affect that connected instance.
/// </para>
/// </summary>
public interface ITrackTarget
{
	/// <summary>
	/// Name of this target, for debugging and editing.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// Value type of this target.
	/// </summary>
	Type TargetType { get; }

	/// <summary>
	/// If true, this target is connected to a real object in the scene, so can be accessed.
	/// </summary>
	bool IsBound { get; }

	/// <summary>
	/// If true, the target is bound and active in the scene hierarchy.
	/// </summary>
	bool IsActive { get; }

	/// <summary>
	/// If bound, the current value of this target in the scene.
	/// </summary>
	object? Value { get; }

	/// <summary>
	/// Component / game object / property that contains this target, if from a nested track.
	/// </summary>
	ITrackTarget? Parent { get; }
}

/// <inheritdoc cref="ITrackTarget"/>
/// <typeparam name="T">Target value type.</typeparam>
public interface ITrackTarget<out T> : ITrackTarget
{
	/// <inheritdoc cref="ITrackTarget.Value"/>
	new T Value { get; }

	Type ITrackTarget.TargetType => typeof( T );
	object? ITrackTarget.Value => Value;
}

/// <summary>
/// A target referencing a <see cref="GameObject"/> or <see cref="Component"/> in the scene.
/// </summary>
public interface ITrackReference : ITrackTarget
{
	/// <summary>
	/// The <see cref="IReferenceTrack.Id"/> of the reference track this target was created from.
	/// </summary>
	Guid Id { get; }

	/// <summary>
	/// Optional game object target that contains this one, if from a nested track.
	/// </summary>
	new ITrackReference<GameObject>? Parent { get; }

	/// <summary>
	/// Explicitly this reference to a particular object in the scene, or null to force it to stay unbound.
	/// </summary>
	void Bind( IValid? value );

	/// <summary>
	/// Clear any explicit binding, so this reference will auto-bind based on its name, type, and parent.
	/// </summary>
	void Reset();

	ITrackTarget? ITrackTarget.Parent => Parent;
	bool ITrackTarget.IsBound => (Value as IValid).IsValid();
}

/// <inheritdoc cref="ITrackReference"/>
/// <typeparam name="T">Reference value type.</typeparam>
public interface ITrackReference<T> : ITrackReference, ITrackTarget<T?>
	where T : class, IValid
{
	/// <inheritdoc cref="ITrackReference.Bind"/>
	void Bind( T? value );

	void ITrackReference.Bind( IValid? value ) => Bind( (T?)value );
}

/// <summary>
/// A target referencing a member property or field of another target.
/// </summary>
public partial interface ITrackProperty : ITrackTarget, IValid
{
	/// <summary>
	/// Target that this member belongs to.
	/// </summary>
	new ITrackTarget Parent { get; }

	/// <summary>
	/// False if this member is readonly.
	/// </summary>
	bool CanWrite => true;

	/// <summary>
	/// If bound, gets or sets the current value of this member.
	/// </summary>
	new object? Value { get; set; }

	/// <summary>
	/// If bound and writable, update this property's value from the
	/// given <paramref name="track"/> at the given <paramref name="time"/>.
	/// </summary>
	bool Update( IPropertyTrack track, MovieTime time );

	bool IValid.IsValid => true;
	bool ITrackTarget.IsBound => Parent is { IsBound: true };
	bool ITrackTarget.IsActive => Parent is { IsActive: true };
	ITrackTarget ITrackTarget.Parent => Parent;
}

/// <inheritdoc cref="ITrackProperty"/>
/// <typeparam name="T">Property value type.</typeparam>
public partial interface ITrackProperty<T> : ITrackProperty, ITrackTarget<T>
{
	/// <inheritdoc cref="ITrackProperty.Value"/>
	new T Value { get; set; }

	/// <inheritdoc cref="ITrackProperty.Update"/>
	public bool Update( IPropertyTrack<T> track, MovieTime time )
	{
		if ( !IsBound || !CanWrite ) return false;
		if ( !track.TryGetValue( time, out var value ) )
		{
			// Special handling for Enabled: false when there's no value

			if ( track is { Parent: IReferenceTrack, Name: nameof( GameObject.Enabled ) } && this is ITrackProperty<bool> boolProperty )
			{
				boolProperty.Value = false;
				return true;
			}

			return false;
		}

		Value = value;

		return true;
	}

	T ITrackTarget<T>.Value => Value;

	bool ITrackProperty.Update( IPropertyTrack track, MovieTime time ) =>
		track is IPropertyTrack<T> typedTrack && Update( typedTrack, time );

	object? ITrackProperty.Value
	{
		get => Value;
		set => Value = (T)value!;
	}
}
