namespace Sandbox.Layout;

/// <summary>
/// Grid line numbering used internally: line 0 is the start of the explicit grid, so explicit line N
/// (1-based CSS) is N-1 and negative implicit lines are negative. Distinct from CSS line numbers where
/// negatives count from the end.
/// </summary>
internal readonly record struct GridSpan( int Start, int End )
{
	public int Span => Math.Max( End - Start, 0 );
}

/// <summary>Numbers of tracks before, in, and after the explicit grid, in one axis.</summary>
internal struct TrackCounts
{
	public int NegativeImplicit;
	public int Explicit;
	public int PositiveImplicit;

	public int Length => NegativeImplicit + Explicit + PositiveImplicit;

	/// <summary>Origin-zero line at the very start of the (implicit) grid.</summary>
	public int ImplicitStartLine => -NegativeImplicit;

	/// <summary>Origin-zero line at the very end of the (implicit) grid.</summary>
	public int ImplicitEndLine => Explicit + PositiveImplicit;

	/// <summary>Index of the track that follows an origin-zero line.</summary>
	public int LineToNextTrack( int line ) => line + NegativeImplicit;

	/// <summary>Origin-zero line preceding a track index.</summary>
	public int TrackToPrevLine( int trackIndex ) => trackIndex - NegativeImplicit;

	/// <summary>
	/// Index into the track vector (which interleaves gutters and tracks) of the gutter at an origin-zero line.
	/// Returns -1 when the line is outside the grid.
	/// </summary>
	public int TryLineToTrackVecIndex( int line )
	{
		if ( line < -NegativeImplicit )
		{
			return -1;
		}

		if ( line > Explicit + PositiveImplicit )
		{
			return -1;
		}

		return 2 * (line + NegativeImplicit);
	}

	public int LineToTrackVecIndex( int line )
	{
		var index = TryLineToTrackVecIndex( line );
		if ( index < 0 )
		{
			throw new InvalidOperationException( $"Grid line {line} is outside the grid" );
		}

		return index;
	}
}

internal enum GridTrackKind : byte
{
	Track,
	Gutter,
}

/// <summary>
/// A track (or a gutter between tracks) during sizing: its sizing functions plus the running base size and
/// growth limit of css-grid-1 §11.
/// </summary>
internal sealed class GridTrack
{
	public GridTrackKind Kind;
	public bool IsCollapsed;
	public TrackBreadth Min;
	public TrackBreadth Max;
	public StyleLength FitContentLimitLength;

	public float Offset;
	public float BaseSize;
	public float GrowthLimit;
	public float ContentAlignmentAdjustment;
	public float ItemIncurredIncrease;
	public float BaseSizePlannedIncrease;
	public float GrowthLimitPlannedIncrease;
	public bool InfinitelyGrowable;

	/// <summary>Tracks are pooled per thread; <see cref="ReturnAll"/> hands a list's tracks back.</summary>
	public static GridTrack Create( TrackSizingFunction function )
	{
		return Rent(
			GridTrackKind.Track,
			function.Min,
			function.IsFitContent ? TrackBreadth.MaxContent : function.Max,
			function.FitContentLimit );
	}

	public static GridTrack Gutter( StyleLength size )
	{
		var breadth = size.IsPercent ? TrackBreadth.Percent( size.Value ) : TrackBreadth.Points( size.IsPoints ? size.Value : 0 );
		return Rent( GridTrackKind.Gutter, breadth, breadth, StyleLength.Undefined );
	}

	private static GridTrack Rent( GridTrackKind kind, TrackBreadth min, TrackBreadth max, StyleLength fitContentLimit )
	{
		var track = GridPool<GridTrack>.Rent();
		track.Kind = kind;
		track.IsCollapsed = false;
		track.Min = min;
		track.Max = max;
		track.FitContentLimitLength = fitContentLimit;
		track.Offset = 0;
		track.BaseSize = 0;
		track.GrowthLimit = 0;
		track.ContentAlignmentAdjustment = 0;
		track.ItemIncurredIncrease = 0;
		track.BaseSizePlannedIncrease = 0;
		track.GrowthLimitPlannedIncrease = 0;
		track.InfinitelyGrowable = false;
		return track;
	}

	/// <summary>Returns every track in the list to the pool and clears the list.</summary>
	public static void ReturnAll( List<GridTrack> tracks )
	{
		foreach ( var track in tracks )
		{
			GridPool<GridTrack>.Return( track );
		}

		tracks.Clear();
	}

	public void Collapse()
	{
		IsCollapsed = true;
		Min = TrackBreadth.Points( 0 );
		Max = TrackBreadth.Points( 0 );
		FitContentLimitLength = StyleLength.Undefined;
	}

	public bool IsFitContent => FitContentLimitLength.IsDefined;
	public bool IsFlexible => Max.IsFraction;
	public float FlexFactor => Max.IsFraction ? Max.Value : 0;
	public bool UsesPercentage => Min.Kind == TrackBreadthKind.Percent
		|| Max.Kind == TrackBreadthKind.Percent
		|| FitContentLimitLength.IsPercent;

	public bool HasIntrinsicSizingFunction => MinIsIntrinsic || MaxIsIntrinsic;

	/// <summary>Min is auto, min-content or max-content.</summary>
	public bool MinIsIntrinsic => Min.IsIntrinsic;

	public bool MinIsMinOrMaxContent => Min.IsMinContent || Min.IsMaxContent;

	/// <summary>Max is auto, min-content, max-content or fit-content.</summary>
	public bool MaxIsIntrinsic => Max.IsIntrinsic || IsFitContent;

	/// <summary>Max is max-content or auto (treated as max-content when growing to max-content contributions).</summary>
	public bool MaxIsMaxContentAlike => !IsFitContent && (Max.IsMaxContent || Max.IsAuto);

	public bool MaxIsMaxOrFitContent => IsFitContent || Max.IsMaxContent;

	public bool MaxIsAuto => !IsFitContent && Max.IsAuto;
	public bool MaxIsMinContent => !IsFitContent && Max.IsMinContent;

	/// <summary>Definite value of the min sizing function, or undefined.</summary>
	public float MinDefiniteValue( float percentageBasis ) => Min.ResolveFixed( percentageBasis );

	/// <summary>Definite value of the max sizing function (fit-content has none), or undefined.</summary>
	public float MaxDefiniteValue( float percentageBasis ) => IsFitContent ? Num.Undefined : Max.ResolveFixed( percentageBasis );

	/// <summary>Definite value of the max sizing function, treating fit-content(x) as x.</summary>
	public float MaxDefiniteLimit( float percentageBasis )
	{
		if ( IsFitContent )
		{
			return FitContentLimitLength.IsPercent
				? FitContentLimitLength.Resolve( percentageBasis )
				: FitContentLimitLength.Value;
		}

		return Max.ResolveFixed( percentageBasis );
	}

	public bool MaxHasDefiniteValue( float percentageBasis ) => Num.IsDefined( MaxDefiniteValue( percentageBasis ) );

	public float FitContentLimit( float axisInnerSize )
	{
		if ( !IsFitContent )
		{
			return float.PositiveInfinity;
		}

		if ( FitContentLimitLength.IsPercent )
		{
			return Num.IsDefined( axisInnerSize )
				? FitContentLimitLength.Value * axisInnerSize * 0.01f
				: float.PositiveInfinity;
		}

		return FitContentLimitLength.Value;
	}

	public float FitContentLimitedGrowthLimit( float axisInnerSize ) => MathF.Min( GrowthLimit, FitContentLimit( axisInnerSize ) );

	/// <summary>Percentage part of a sizing function resolved against a size, or undefined if not a percentage.</summary>
	public static float ResolvedPercentageSize( TrackBreadth breadth, float size )
	{
		return breadth.Kind == TrackBreadthKind.Percent
			? breadth.Value * size * 0.01f
			: Num.Undefined;
	}

	public override string ToString()
	{
		return $"{(Kind == GridTrackKind.Gutter ? "gutter" : "track")} {Min}/{Max} base={BaseSize} limit={GrowthLimit} offset={Offset}";
	}
}
