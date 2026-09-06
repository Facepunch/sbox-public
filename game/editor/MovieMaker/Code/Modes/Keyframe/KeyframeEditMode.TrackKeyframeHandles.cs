using Sandbox.MovieMaker;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

partial class KeyframeEditMode
{
	/// <summary>
	/// Manages the keyframe handles for a particular <see cref="TimelineTrack"/>.
	/// </summary>
	private sealed class TrackKeyframeHandles : IEnumerable<KeyframeHandle>
	{
		private readonly TimelineTrack _timelineTrack;
		private readonly List<KeyframeHandle> _handles = new();

		private readonly List<IProjectPropertyBlock> _sourceBlocks = new();
		private readonly List<MovieTime> _cutTimes = new();

		public TrackView View => _timelineTrack.View;
		public IProjectPropertyTrack Track => (IProjectPropertyTrack)View.Track;

		public TrackKeyframeHandles( TimelineTrack timelineTrack )
		{
			_timelineTrack = timelineTrack;
		}

		public void AddRange( IEnumerable<IKeyframe> keyframes, MovieTime timeOffset )
		{
			foreach ( var keyframe in keyframes )
			{
				var kf = new Keyframe( keyframe.Time + timeOffset, keyframe.Value, keyframe.Interpolation, keyframe.Connection );

				var handle = new KeyframeHandle( _timelineTrack, kf );

				_handles.Add( handle );

				handle.Selected = true;
			}

			_handles.Sort();

			WriteToTrack();
		}

		/// <summary>
		/// If keyframe is inside a source block, make its value relative
		/// </summary>
		/// <param name="keyframe"></param>
		/// <returns></returns>
		private Keyframe MakeRelativeIfNeeded( Keyframe keyframe )
		{
			if ( _sourceBlocks.FirstOrDefault( x => x.TimeRange.Contains( keyframe.Time ) ) is not { } sourceBlock )
			{
				return keyframe;
			}

			if ( Transformer.GetDefault( Track.TargetType ) is { } transformer )
			{
				return keyframe with
				{
					Value = transformer.Difference( sourceBlock.GetValue( keyframe.Time ), keyframe.Value )
				};
			}

			return keyframe;
		}

		public KeyframeHandle Add( Keyframe keyframe )
		{
			return AddCore( MakeRelativeIfNeeded( keyframe ) );
		}

		private KeyframeHandle AddCore( Keyframe keyframe )
		{
			var handle = new KeyframeHandle( _timelineTrack, keyframe );

			_handles.Add( handle );
			_handles.Sort();

			WriteToTrack();
			return handle;
		}

		public bool TryUpdate( Keyframe keyframe, [NotNullWhen( true )] out KeyframeHandle? handle )
		{
			return TryUpdateCore( MakeRelativeIfNeeded( keyframe ), out handle );
		}

		private bool TryUpdateCore( Keyframe keyframe, [NotNullWhen( true )] out KeyframeHandle? handle )
		{
			// Hack: see KeyframeSignal<T>.GetValue for why we check for one tick later than this keyframe.
			// We use LastOrDefault because we can have two keyframes at the same time, one ends a block
			// and the next starts a new block. We want the one that starts a block.

			handle = _handles.LastOrDefault( x => x.Time == keyframe.Time )
				?? _handles.FirstOrDefault( x => x.Time == keyframe.Time + MovieTime.Epsilon );

			if ( handle is null ) return false;

			// Don't change the keyframe time if we've found a match on the next tick,
			// and don't change the connection state of the existing keyframe

			keyframe = keyframe with { Time = handle.Time, Connection = handle.Keyframe.Connection };

			// Return false if nothing has changed

			if ( handle.Keyframe.Equals( keyframe ) ) return false;

			handle.Keyframe = keyframe;

			WriteToTrack();
			return true;
		}

		/// <summary>
		/// Adds or updates a handle representing the given <paramref name="keyframe"/>. If an existing handle
		/// is updated, we won't change its connection mode.
		/// </summary>
		/// <param name="keyframe">Keyframe time, value, interpolation and connection mode.</param>
		/// <param name="handle">Handle that was added or updated.</param>
		/// <returns>True iff a handle was <i>added</i>.</returns>
		public bool AddOrUpdate( Keyframe keyframe, out KeyframeHandle handle )
		{
			keyframe = MakeRelativeIfNeeded( keyframe );

			if ( TryUpdateCore( keyframe, out var handleOrNull ) )
			{
				handle = handleOrNull;
				return true;
			}

			if ( handleOrNull is not null )
			{
				handle = handleOrNull;
				return false;
			}

			handle = AddCore( keyframe );
			return true;
		}

		public bool SplitHandle( KeyframeHandle handle )
		{
			if ( handle.Keyframe.Connection is not KeyframeConnection.Connect ) return false;

			handle.Keyframe = handle.Keyframe with { Connection = KeyframeConnection.StartBlock };

			_handles.Add( new KeyframeHandle( _timelineTrack, handle.Keyframe with { Connection = KeyframeConnection.EndBlock } ) );
			_handles.Sort();

			WriteToTrack();
			return true;
		}

		public bool RemoveAll( Predicate<KeyframeHandle> match )
		{
			if ( _handles.RemoveAll( match ) <= 0 ) return false;

			WriteToTrack();
			return true;
		}

		public void UpdatePositions()
		{
			foreach ( var handle in _handles )
			{
				handle.UpdatePosition();
			}
		}

		/// <summary>
		/// Remove overlapping unselected keyframes.
		/// </summary>
		public void CleanUpKeyframes()
		{
			_handles.Sort();

			foreach ( var handle in _handles )
			{
				handle.IsOverlappingNextBlock = false;
			}

			for ( var i = _handles.Count - 1; i >= 1; --i )
			{
				var prev = _handles[i - 1];
				var next = _handles[i];

				// We're looking for keyframes overlapping in time...

				if ( prev.Keyframe.Time != next.Keyframe.Time ) continue;

				// ...and with identical Connection modes, otherwise this is a block boundary.

				if ( prev.Keyframe.Connection != next.Keyframe.Connection )
				{
					prev.IsOverlappingNextBlock = true;
					continue;
				}

				// We keep dragged keyframes so we don't nuke stuff that temporarily overlaps

				if ( prev.IsDragging || next.IsDragging ) continue;

				_handles.RemoveAt( i );

				next.Destroy();
			}
		}

		public void ReadFromTrack()
		{
			foreach ( var handle in _handles )
			{
				handle.Destroy();
			}

			_handles.Clear();

			foreach ( var keyframe in View.Keyframes )
			{
				_handles.Add( new KeyframeHandle( _timelineTrack, keyframe ) );
			}

			_handles.Sort();

			// Blocks that keyframes could apply a local (additive editing) effect to

			_sourceBlocks.Clear();
			_sourceBlocks.AddRange( Track.Blocks
				.Where( x => x.Signal is not IKeyframeSignal )
				.Select( GetBlockWithoutKeyframes ) );

			// Keyframe blocks must be cut by these times
			// Offset start by epsilon so keyframes at the very start of an additive block won't
			// be included in that block, letting you join non-additive and additive keyframe blocks

			_cutTimes.Clear();
			_cutTimes.AddRange( _sourceBlocks
				.SelectMany( x => new[] { x.TimeRange.Start + MovieTime.Epsilon, x.TimeRange.End } )
				.Distinct() );
		}

		[field: ThreadStatic]
		private static List<Keyframe>? WriteToTrack_Block { get; set; }

		[field: ThreadStatic]
		private static List<IProjectPropertyBlock>? WriteToTrack_Blocks { get; set; }

		public void WriteToTrack()
		{
			// Handles might have moved, re-sort them and remove overlaps

			CleanUpKeyframes();

			// Keyframes inside a source block will be an additive operation on that block,
			// otherwise they'll produce a new keyframe-only block

			var block = WriteToTrack_Block ??= new List<Keyframe>();
			var blocks = WriteToTrack_Blocks ??= new List<IProjectPropertyBlock>();

			block.Clear();
			blocks.Clear();

			var prevCutTime = MovieTime.Zero;

			foreach ( var handle in _handles )
			{
				var cutTime = _cutTimes.LastOrDefault( x => x <= handle.Time );

				// Start a new block if the next keyframe is a StartBlock...

				var endPrevBlock = handle.Keyframe.Connection is KeyframeConnection.StartBlock;

				// ...or the prev keyframe was an EndBlock...

				if ( block.Count > 0 && block[^1].Connection is KeyframeConnection.EndBlock )
				{
					endPrevBlock = true;
				}

				// ...or if we're in a different source block when additive editing

				if ( cutTime != prevCutTime )
				{
					endPrevBlock = true;
					prevCutTime = cutTime;
				}

				if ( endPrevBlock && block.Count > 0 )
				{
					blocks.Add( FinishBlock( block ) );
					block.Clear();
				}

				if ( block.Count > 0 && block[^1].Time == handle.Time )
				{
					// Use first when overlapping, which will be a selected keyframe
					continue;
				}

				block.Add( handle.Keyframe );
			}

			if ( block.Count > 0 )
			{
				blocks.Add( FinishBlock( block ) );
			}

			// Re-add any source blocks that don't have keyframes in them

			foreach ( var sourceBlock in _sourceBlocks )
			{
				if ( blocks.Any( x => x.TimeRange == sourceBlock.TimeRange ) ) continue;

				blocks.Add( sourceBlock );
			}

			blocks.Sort( ( a, b ) =>
				a.TimeRange.Start.CompareTo( b.TimeRange.Start ) );

			Track.SetBlocks( blocks );
			View.MarkValueChanged();
		}

		private static IProjectPropertyBlock GetBlockWithoutKeyframes( IProjectPropertyBlock block )
		{
			return block.Signal is IAdditiveSignal { First: { } source, Second: IKeyframeSignal }
				? block.WithSignal( source )
				: block;
		}

		private IProjectPropertyBlock FinishBlock( IReadOnlyList<Keyframe> keyframes )
		{
			var start = keyframes[0].Time;
			var end = keyframes[^1].Time;

			var sourceBlock = _sourceBlocks.FirstOrDefault( x => x.TimeRange.Grow( -MovieTime.Epsilon ).Contains( start ) );
			var propertyType = Track.TargetType;

			return sourceBlock?.WithSignal( PropertySignal.FromKeyframes( propertyType, keyframes, sourceBlock.Signal ) )
				?? PropertyBlock.FromSignal( PropertySignal.FromKeyframes( propertyType, keyframes ), (start, end) );
		}

		public IEnumerator<KeyframeHandle> GetEnumerator() => _handles.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
