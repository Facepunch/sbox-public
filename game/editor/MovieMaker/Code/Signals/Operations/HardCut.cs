using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker;

#nullable enable

partial record PropertySignal<T>
{
	public PropertySignal<T> HardCut( PropertySignal<T> after, MovieTime time )
	{
		return !Equals( after )
			? new HardCutOperation<T>( this, after, time )
			: this;
	}

	public PropertySignal<T> Clamp( MovieTimeRange timeRange ) =>
		ClampStart( timeRange.Start ).ClampEnd( timeRange.End );

	public PropertySignal<T> ClampStart( MovieTime time ) =>
		GetValue( time ).AsSignal().HardCut( this, time );

	public PropertySignal<T> ClampEnd( MovieTime time ) =>
		HardCut( GetValue( time ), time );

	public IEnumerable<PropertyBlock<T>> AsBlocks( MovieTimeRange timeRange )
	{
		var signal = Reduce( timeRange );

		if ( signal is not HardCutOperation<T> hardCut ) return [new PropertyBlock<T>( signal, timeRange )];

		return
		[
			..hardCut.First.AsBlocks( timeRange with { End = hardCut.Time } ),
			..hardCut.Second.AsBlocks( timeRange with { Start = hardCut.Time } )
		];
	}
}

[JsonDiscriminator( "HardCut" )]
file sealed record HardCutOperation<T>( PropertySignal<T> First, PropertySignal<T> Second, MovieTime Time )
	: BinaryOperation<T>( First, Second )
{
	public override T GetValue( MovieTime time )
	{
		return time < Time ? First.GetValue( time ) : Second.GetValue( time );
	}

	protected override PropertySignal<T> OnTransform( MovieTransform value ) => this with
	{
		First = value * First,
		Second = value * Second,
		Time = value * Time
	};

	protected override PropertySignal<T> OnReduce( MovieTime? start, MovieTime? end )
	{
		return !TryReduceTransition( start, end, Time, out var reduced,
			out var before, out var after )
			? before.HardCut( after, Time )
			: reduced;
	}

	public override IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange )
	{
		return GetTransitionPaintHints( timeRange, Time );
	}

	public override IEnumerable<ICompiledPropertyBlock<T>> Compile( MovieTimeRange timeRange, int sampleRate )
	{
		if ( timeRange.End <= Time )
		{
			return First.Compile( timeRange, sampleRate );
		}

		if ( timeRange.Start >= Time )
		{
			return Second.Compile( timeRange, sampleRate );
		}

		return
		[
			..First.Compile( timeRange.ClampEnd( Time ), sampleRate ),
			..Second.Compile( timeRange.ClampStart( Time ), sampleRate )
		];
	}
}
