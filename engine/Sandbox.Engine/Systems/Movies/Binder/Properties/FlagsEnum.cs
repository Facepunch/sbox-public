using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sandbox.MovieMaker.Properties;

#nullable enable

/// <summary>
/// Toggles a flag on an enum property.
/// </summary>
file sealed record FlagsEnumProperty<T>( ITrackProperty<T> Parent, string Name, T FlagValue ) : ITrackProperty<bool>
	where T : struct, Enum
{
	private static IEnumHelper<T> Helper => field ??= (IEnumHelper<T>)Activator.CreateInstance( typeof( EnumHelper<,> )
		.MakeGenericType( typeof( T ), Enum.GetUnderlyingType( typeof( T ) ) ) )!;

	public bool IsBound => Parent is { IsBound: true };

	public bool Value
	{
		get => Parent.Value.HasFlag( FlagValue );
		set
		{
			Parent.Value = value
				? Helper.Or( Parent.Value, FlagValue )
				: Helper.AndNot( Parent.Value, FlagValue );
		}
	}

	ITrackTarget ITrackProperty.Parent => Parent;
}

[Expose]
file sealed class FlagsEnumPropertyFactory : ITrackPropertyFactory
{
	private static bool IsFlagsEnumType( Type type )
	{
		return type.IsEnum && type.GetCustomAttribute<FlagsAttribute>() is not null;
	}

	public IEnumerable<string> GetPropertyNames( ITrackTarget parent )
	{
		if ( !IsFlagsEnumType( parent.TargetType ) )
		{
			yield break;
		}

		// zero-valued enum value isn't a valid flag!

		var zero = Enum.ToObject( parent.TargetType, 0 );

		foreach ( var value in Enum.GetValues( parent.TargetType ) )
		{
			if ( Equals( value, zero ) ) continue;

			yield return value.ToString()!;
		}
	}

	public DisplayInfo GetDisplayInfo( ITrackTarget parent, string name ) =>
		Enum.TryParse( parent.TargetType, name, out var value )
			? DisplayInfo.FromEnumValue( value )
			: DisplayInfo.FromName( name );

	public Type? GetTargetType( ITrackTarget parent, string name )
	{
		if ( !IsFlagsEnumType( parent.TargetType ) ) return null;
		if ( !Enum.TryParse( parent.TargetType, name, out var value ) ) return null;

		// zero-valued enum value isn't a valid flag!

		if ( Equals( value, Enum.ToObject( parent.TargetType, 0 ) ) ) return null;

		return typeof( bool );
	}

	public ITrackProperty<T> CreateProperty<T>( ITrackTarget parent, string name )
	{
		Assert.True( IsFlagsEnumType( parent.TargetType ) );

		var propertyType = typeof( FlagsEnumProperty<> ).MakeGenericType( parent.TargetType );
		var value = Enum.Parse( parent.TargetType, name );

		return (ITrackProperty<T>)Activator.CreateInstance( propertyType, parent, name, value )!;
	}
}

// HACK: Why don't Enum types implement IBitwiseOperators etc??

file interface IEnumHelper<TEnum>
	where TEnum : struct, Enum
{
	TEnum Or( TEnum a, TEnum b );
	TEnum AndNot( TEnum a, TEnum b );
}

file sealed class EnumHelper<TEnum, TUnderlying> : IEnumHelper<TEnum>
	where TEnum : struct, Enum
	where TUnderlying : struct,
		IBitwiseOperators<TUnderlying, TUnderlying, TUnderlying>,
		IEqualityOperators<TUnderlying, TUnderlying, bool>
{
	public TEnum Or( TEnum a, TEnum b )
	{
		var aInt = Unsafe.As<TEnum, TUnderlying>( ref a );
		var bInt = Unsafe.As<TEnum, TUnderlying>( ref b );

		var resultInt = aInt | bInt;

		return Unsafe.As<TUnderlying, TEnum>( ref resultInt );
	}

	public TEnum AndNot( TEnum a, TEnum b )
	{
		var aInt = Unsafe.As<TEnum, TUnderlying>( ref a );
		var bInt = Unsafe.As<TEnum, TUnderlying>( ref b );

		var resultInt = aInt & ~bInt;

		return Unsafe.As<TUnderlying, TEnum>( ref resultInt );
	}
}
