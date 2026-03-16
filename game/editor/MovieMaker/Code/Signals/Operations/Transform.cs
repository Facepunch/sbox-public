using Sandbox.MovieMaker;
using System.Diagnostics.CodeAnalysis;

namespace Editor.MovieMaker;

#nullable enable

partial record PropertySignal<T>
{
	public static PropertySignal<T> operator *( MovieTransform transform, PropertySignal<T> signal ) =>
		transform == MovieTransform.Identity ? signal : signal.OnTransform( transform );

	public static PropertySignal<T> operator +( PropertySignal<T> signal, MovieTime translation ) =>
		new MovieTransform( Translation: translation ) * signal;

	public static PropertySignal<T> operator *( MovieTimeScale scale, PropertySignal<T> signal ) =>
		new MovieTransform( Scale: scale ) * signal;

	public static PropertySignal<T> operator *( PropertySignal<T> signal, MovieTimeScale scale ) =>
		new MovieTransform( Scale: scale ) * signal;

	protected virtual PropertySignal<T> OnTransform( MovieTransform value ) =>
		new TransformOperation<T>( this, value );
}

partial record PropertyBlock<T>
{
	[return: NotNullIfNotNull( nameof(block) )]
	public static PropertyBlock<T>? operator *( MovieTransform transform, PropertyBlock<T>? block ) =>
		transform != MovieTransform.Identity && block is not null
			? new PropertyBlock<T>( transform * block.Signal, transform * block.TimeRange )
			: block;

	[return: NotNullIfNotNull( nameof( block ) )]
	public static PropertyBlock<T>? operator +( PropertyBlock<T>? block, MovieTime translation ) =>
		new MovieTransform( Translation: translation ) * block;

	[return: NotNullIfNotNull( nameof( block ) )]
	public static PropertyBlock<T>? operator *( MovieTimeScale scale, PropertyBlock<T>? block ) =>
		new MovieTransform( Scale: scale ) * block;

	[return: NotNullIfNotNull( nameof( block ) )]
	public static PropertyBlock<T>? operator *( PropertyBlock<T>? block, MovieTimeScale scale ) =>
		new MovieTransform( Scale: scale ) * block;
}

[JsonDiscriminator( "Shift" )]
file sealed record TransformOperation<T>( PropertySignal<T> Signal, MovieTransform Value )
	: UnaryOperation<T>( Signal )
{
	public override T GetValue( MovieTime time ) => Signal.GetValue( Value.Inverse * time );

	protected override PropertySignal<T> OnTransform( MovieTransform transform ) =>
		this with { Value = transform * Value };

	protected override PropertySignal<T> OnReduce( MovieTime? start, MovieTime? end )
	{
		var reduced = Signal.Reduce( Value.Inverse * start, Value.Inverse * end );

		return reduced != Signal ? Value * reduced : this;
	}
}
