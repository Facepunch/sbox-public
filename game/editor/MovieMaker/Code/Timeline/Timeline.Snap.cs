
using Sandbox.MovieMaker;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

partial class Timeline
{
	private readonly List<ISnapSource> _snapSources = new();

	private static MovieTime ClampTime( MovieTime time, MovieTime? min, MovieTime? max )
	{
		if ( min is { } minTime )
		{
			time = MovieTime.Max( time, minTime );
		}

		if ( max is { } maxTime )
		{
			time = MovieTime.Min( time, maxTime );
		}

		return time;
	}

	public MovieTime ScenePositionToTime( Vector2 scenePos, SnapOptions? options = null, bool showSnap = true )
	{
		options ??= new SnapOptions();

		var time = ClampTime( PixelsToTime( scenePos.x ), options.Min, options.Max );

		_snapSources.Clear();
		_snapSources.Add( this );

		if ( Session.ObjectSnap )
		{
			_snapSources.AddRange( GetSnapSources( scenePos, options, Items ) );
		}

		var primaryOffset = options.SnapOffsets.DefaultIfEmpty().MinBy( x => x.Absolute );
		var ctx = new SnapContext(
			targetTime: time,
			targetRange: (options.Min ?? default, options.Max ?? MovieTime.MaxValue),
			maxDistance: PixelsToTime( 16f ),
			sources: _snapSources );

		foreach ( var offset in options.SnapOffsets.DefaultIfEmpty() )
		{
			ctx.Update( offset, offset == primaryOffset );
		}

		if ( showSnap )
		{
			UpdateSnapTargets( ctx.BestSources.Select( x => (x.Key, x.Value) ) );
		}

		return MovieTime.Max( ctx.BestTime, MovieTime.Zero );
	}

	private static IEnumerable<ISnapSource> GetSnapSources( Vector2 scenePos, SnapOptions options, IEnumerable<GraphicsItem> items )
	{
		foreach ( var item in items )
		{
			var sceneRect = item.GetRealSceneRect();

			if ( sceneRect.Top > scenePos.y || sceneRect.Bottom < scenePos.y ) continue;

			foreach ( var childSource in GetSnapSources( scenePos, options, item.Children ) )
			{
				yield return childSource;
			}

			if ( item is ISnapSource source && options.Filter?.Invoke( source ) is not false )
			{
				yield return source;
			}
		}
	}

	private sealed class SnapContext
	{
		public MovieTime TargetTime { get; }
		public MovieTimeRange TargetRange { get; }
		public MovieTime MaxDistance { get; }
		public IReadOnlyList<ISnapSource> Sources { get; }

		public SnapContext( MovieTime targetTime, MovieTimeRange targetRange, MovieTime maxDistance, IEnumerable<ISnapSource> sources )
		{
			TargetTime = targetTime;
			TargetRange = targetRange;
			MaxDistance = maxDistance;
			Sources = [.. sources];

			BestTime = targetTime;
		}

		public MovieTime BestTime { get; private set; }
		public float BestScore { get; private set; } = float.PositiveInfinity;
		public Dictionary<MovieTime, Rect> BestSources { get; } = new();

		public bool Update( MovieTime offset, bool isPrimary )
		{
			var changed = false;

			foreach ( var source in Sources )
			{
				foreach ( var (snapTime, priority, show) in source.GetSnapTargets( TargetTime + offset, isPrimary ) )
				{
					var snappedTime = snapTime - offset;

					if ( !TargetRange.Contains( snappedTime ) ) continue;

					var timeDiff = (snappedTime - TargetTime).Absolute;
					var force = priority < 0;

					if ( !force && timeDiff * Math.Max( 4 - priority, 1 ) > MaxDistance * 4 ) continue;

					var score = (float)(timeDiff.TotalSeconds / MaxDistance.TotalSeconds) - priority;

					if ( score < BestScore )
					{
						if ( snappedTime != BestTime )
						{
							BestSources.Clear();
						}

						BestScore = score;
						BestTime = snappedTime;

						changed = true;
					}

					if ( !show || snappedTime != BestTime ) continue;

					var rect = source.SceneSnapBounds;

					if ( BestSources.TryGetValue( snapTime, out var existing ) )
					{
						rect.Add( existing );
					}

					BestSources[snapTime] = rect;
				}
			}

			return changed;
		}
	}
}
