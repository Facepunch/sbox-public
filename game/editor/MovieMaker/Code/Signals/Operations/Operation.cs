using System.Diagnostics.CodeAnalysis;
using Sandbox.MovieMaker;
using System.Text;

namespace Editor.MovieMaker;

#nullable enable

public abstract record PropertyOperation<T> : PropertySignal<T>
{
	protected override bool PrintMembers( StringBuilder builder )
	{
		return false;
	}
}

public abstract record UnaryOperation<T>( PropertySignal<T> Signal ) : PropertyOperation<T>
{
	protected override PropertySignal<T> OnTransform( MovieTransform value ) =>
		this with { Signal = value * Signal };

	protected override PropertySignal<T> OnReduce( MovieTime? start, MovieTime? end ) =>
		this with { Signal = Signal.Reduce( start, end ) };

	protected override PropertySignal<T> OnSmooth( MovieTime size ) => this with { Signal = Signal.Smooth( size ) };

	public override IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange ) =>
		Signal.GetPaintHints( timeRange );
}

public abstract record BinaryOperation<T>( PropertySignal<T> First, PropertySignal<T> Second )
	: PropertyOperation<T>
{
	protected override PropertySignal<T> OnTransform( MovieTransform value ) =>
		this with { First = value * First, Second = value * Second };

	protected override PropertySignal<T> OnReduce( MovieTime? start, MovieTime? end ) =>
		this with { First = First.Reduce( start, end ), Second = Second.Reduce( start, end ) };

	public override IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange ) =>
		First.GetPaintHints( timeRange ).Union( Second.GetPaintHints( timeRange ) );

	protected override PropertySignal<T> OnSmooth( MovieTime size )
	{
		var first = First.Smooth( size );
		var second = Second.Smooth( size );

		if ( first == First && second == Second )
		{
			return this;
		}

		return this with { First = first, Second = second };
	}

	protected bool TryReduceTransition( MovieTime? start, MovieTime? end, MovieTimeRange transitionTimeRange,
		[NotNullWhen( true )] out PropertySignal<T>? reduced,
		[NotNullWhen( false )] out PropertySignal<T>? before,
		[NotNullWhen( false )] out PropertySignal<T>? after )
	{
		reduced = null;
		before = null;
		after = null;

		// Check if transition happens outside the reduction range

		if ( start >= transitionTimeRange.End )
		{
			reduced = Second.Reduce( start, end );
			return true;
		}

		if ( end <= transitionTimeRange.Start )
		{
			reduced = First.Reduce( start, end );
			return true;
		}

		// Check if both First and Second are identical when reduced on either side of the transition

		var firstBefore = First.Reduce( start, transitionTimeRange.End );
		var firstAfter = First.Reduce( transitionTimeRange.Start, end );
		var secondBefore = Second.Reduce( start, transitionTimeRange.End );
		var secondAfter = Second.Reduce( transitionTimeRange.Start, end );

		if ( firstBefore.Equals( secondBefore ) )
		{
			reduced = Second.Reduce( start, end );
			return true;
		}

		if ( secondAfter.Equals( firstAfter ) )
		{
			reduced = First.Reduce( start, end );
			return true;
		}

		// Check if reduced First and Second are identical to un-reduced: can just return this

		if ( firstBefore.Equals( First ) && secondAfter.Equals( Second ) )
		{
			reduced = this;
			return true;
		}

		// Couldn't reduce

		before = firstBefore;
		after = secondAfter;
		return false;
	}

	protected IEnumerable<MovieTimeRange> GetTransitionPaintHints( MovieTimeRange timeRange, MovieTimeRange transitionTimeRange )
	{
		return First.GetPaintHints( timeRange.ClampEnd( transitionTimeRange.End ) )
			.Union( Second.GetPaintHints( timeRange.ClampStart( transitionTimeRange.Start ) ) );
	}
}
