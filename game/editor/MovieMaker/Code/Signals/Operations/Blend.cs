using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

partial record PropertySignal<T>
{
	public PropertySignal<T> Blend( PropertySignal<T> second, float alpha )
	{
		if ( alpha <= 0f ) return this;
		if ( alpha >= 1f ) return second;

		if ( Equals( second ) )
		{
			return this;
		}

		return Interpolator.GetDefault<T>() is not null
			? new BlendOperation<T>( this, second, alpha )
			: this;
	}
}

[JsonDiscriminator( "Blend" )]
file sealed record BlendOperation<T>( PropertySignal<T> First, PropertySignal<T> Second, float Alpha ) : InterpolateOperation<T>( First, Second )
{
	public override float GetAlpha( MovieTime time ) => Alpha;

	protected override PropertySignal<T> OnSmooth( MovieTime size ) =>
		this with { First = First.Smooth( size ), Second = Second.Smooth( size ) };
}
