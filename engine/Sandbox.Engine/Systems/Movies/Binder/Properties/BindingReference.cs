using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox.MovieMaker.Properties;

#nullable enable

/// <summary>
/// Helper methods for working with <see cref="BindingReference{T}"/>.
/// </summary>
public static class BindingReference
{
	/// <summary>
	/// Can we make a <see cref="IReferenceTrack{T}"/> or <see cref="BindingReference{T}"/>
	/// of the given <paramref name="type"/>? Returns true if <paramref name="type"/> is
	/// either <see cref="GameObject"/>, or derived from <see cref="Component"/>.
	/// </summary>
	public static bool CanMakeReference( this Type type )
	{
		return type == typeof( GameObject ) || type.IsAssignableTo( typeof( Component ) );
	}

	/// <summary>
	/// If <paramref name="refType"/> is a constructed <see cref="BindingReference{T}"/>,
	/// gets the wrapped type. Otherwise, returns <see langword="null"/>.
	/// </summary>
	public static Type? GetUnderlyingType( Type refType )
	{
		return refType.IsConstructedGenericType && refType.GetGenericTypeDefinition() == typeof( BindingReference<> )
			? refType.GetGenericArguments()[0]
			: null;
	}

	internal static IBindingReferenceProperty AsReference( this ITrackProperty property )
	{
		Assert.True( property.TargetType.CanMakeReference() );

		var propertyType = typeof( BindingReferenceProperty<> )
			.MakeGenericType( property.TargetType );

		return (IBindingReferenceProperty)Activator.CreateInstance( propertyType, property )!;
	}
}

/// <summary>
/// Used by movie property tracks with <see cref="GameObject"/> or <see cref="Component"/> value
/// types to reference other tracks. This value will be resolved to whatever the referenced track
/// is bound to during playback. Needed for properties like <see cref="SkinnedModelRenderer.BoneMergeTarget"/>.
/// </summary>
/// <typeparam name="T">Either <see cref="GameObject"/>, or a <see cref="Component"/> type.</typeparam>
/// <param name="TrackId">Track to look up the binding of during playback.</param>
[Expose]
[JsonConverter( typeof( ReferenceConverterFactory ) )]
public readonly record struct BindingReference<T>( Guid? TrackId )
	where T : class, IValid
{
	public static implicit operator BindingReference<T>( Guid? trackId ) => new( trackId );

	/// <summary>
	/// Resolve this binding reference by looking up the current binding for <see cref="TrackId"/>.
	/// </summary>
	/// <param name="binder">Binder to look up the current binding in.</param>
	public T? Get( TrackBinder binder ) => TrackId is { } trackId && binder.TryGetBinding<T>( trackId, out var binding ) ? binding : null;
}

internal interface IBindingReferenceProperty : ITrackProperty
{
	IValid? InnerValue { get; set; }
}

/// <summary>
/// Procedural property inside <see cref="GameObject"/>, that makes the object look at a world position.
/// </summary>
file sealed record BindingReferenceProperty<T>( ITrackProperty<T?> Inner ) : ITrackProperty<BindingReference<T>>, IBindingReferenceProperty
	where T : class, IValid
{
	public string Name => Inner.Name;

	public BindingReference<T> Value
	{
		get => Inner.Value is { IsValid: true } value && Inner.Binder.GetTrackId( value ) is { } trackId
			? new BindingReference<T>( trackId )
			: default;

		set => Inner.Value = value is { TrackId: { } trackId } && Inner.Binder.TryGetBinding<T>( trackId, out var target )
			? target
			: default;
	}

	ITrackTarget ITrackProperty.Parent => Inner.Parent;

	bool ITrackProperty.CanRead => Inner.CanRead;
	bool ITrackProperty.CanWrite => Inner.CanWrite;

	IValid? IBindingReferenceProperty.InnerValue
	{
		get => Inner.Value;
		set => Inner.Value = (T?)value;
	}
}

file sealed class ReferenceConverterFactory : JsonConverterFactory
{
	public override bool CanConvert( Type typeToConvert ) => BindingReference.GetUnderlyingType( typeToConvert ) is not null;

	public override JsonConverter? CreateConverter( Type typeToConvert, JsonSerializerOptions options )
	{
		return (JsonConverter)Activator.CreateInstance( typeof( ReferenceConverter<> ).MakeGenericType( BindingReference.GetUnderlyingType( typeToConvert )! ) )!;
	}
}

file sealed class ReferenceConverter<T> : JsonConverter<BindingReference<T>>
	where T : class, IValid
{
	public override BindingReference<T> Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		return JsonSerializer.Deserialize<Guid?>( ref reader, options );
	}

	public override void Write( Utf8JsonWriter writer, BindingReference<T> value, JsonSerializerOptions options )
	{
		JsonSerializer.Serialize( writer, value.TrackId, options );
	}
}
