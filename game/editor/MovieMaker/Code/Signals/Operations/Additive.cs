using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Serialization;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

partial record PropertySignal<T>
{
	public static PropertySignal<T> operator -( PropertySignal<T> a, PropertySignal<T> b )
	{
		if ( Transformer.GetDefault<T>() is not { } transformer ) return a;
		if ( a.Equals( b ) ) return transformer.Identity;
		if ( b.IsIdentity ) return a;

		return new ToLocalOperation<T>( a, b );
	}

	[return: NotNullIfNotNull( nameof( a ) )]
	public static PropertySignal<T> operator +( PropertySignal<T> a, PropertySignal<T> b )
	{
		if ( Transformer.GetDefault<T>() is null ) return a;
		if ( a.IsIdentity ) return b;
		if ( b.IsIdentity ) return a;

		return new ToGlobalOperation<T>( a, b );
	}

	[JsonIgnore]
	public virtual bool IsIdentity => false;
}

public interface IAdditiveSignal : IPropertySignal
{
	PropertySignal First { get; }
	PropertySignal Second { get; }
}

[JsonDiscriminator( "ToLocal" )]
file sealed record ToLocalOperation<T>( PropertySignal<T> First, PropertySignal<T> Second )
	: BinaryOperation<T>( First, Second )
{
	public override T GetValue( MovieTime time ) => _transformer.Apply(
		_transformer.Invert( Second.GetValue( time ) ),
		First.GetValue( time ) );

	[SkipHotload]
	private static ITransformer<T> _transformer = Transformer.GetDefaultOrThrow<T>();
}

[JsonDiscriminator( "ToGlobal" )]
file sealed record ToGlobalOperation<T>( PropertySignal<T> First, PropertySignal<T> Second )
	: BinaryOperation<T>( First, Second ), IAdditiveSignal
{
	public override T GetValue( MovieTime time ) => _transformer.Apply(
		First.GetValue( time ), Second.GetValue( time ) );

	protected override IEnumerable<Keyframe> OnGetKeyframes() => Second.Keyframes;

	protected override PropertySignal<T> OnWithKeyframes( IReadOnlyList<Keyframe<T>> keyframes )
	{
		var second = Second.WithKeyframes( [..keyframes.Select( x =>
			x with { Value = _transformer.Difference( First.GetValue( x.Time ), x.Value ) } )] );

		return second.IsIdentity ? First : First + second;
	}

	PropertySignal IAdditiveSignal.First => First;
	PropertySignal IAdditiveSignal.Second => Second;

	[SkipHotload]
	private static ITransformer<T> _transformer = Transformer.GetDefaultOrThrow<T>();
}
