
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Serialization;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker;

#nullable enable

public sealed partial class ProjectSequenceTrack( MovieProject project, Guid id, string name )
	: ProjectReferenceTrack<GameObject>( project, id, name ), IProjectBlockTrack
{
	private readonly List<ProjectSequenceBlock> _blocks = new();
	private readonly HashSet<ICompiledReferenceTrack> _referenceTracks = new();
	private readonly HashSet<IProjectSequencePropertyTrack> _propertyTracks = new();
	private bool _tracksInvalid = true;

	public override int Order => -2000;

	public override bool IsEmpty => _blocks.Count == 0;
	public override IEnumerable<MovieResource> References => _blocks.Select( x => x.Resource ).Distinct();

	public MovieTimeRange TimeRange => _blocks
		.Select( x => x.TimeRange.End )
		.DefaultIfEmpty()
		.Max();

	public IReadOnlyList<ProjectSequenceBlock> Blocks => _blocks;

	public IEnumerable<ICompiledReferenceTrack> ReferenceTracks
	{
		get
		{
			UpdateTracks();
			return _referenceTracks;
		}
	}

	public IEnumerable<IProjectSequencePropertyTrack> PropertyTracks
	{
		get
		{
			UpdateTracks();
			return _propertyTracks;
		}
	}

	public void Invalidate()
	{
		_tracksInvalid = true;

		Project.InvalidateDuration();
	}

	public ProjectSequenceBlock AddBlock( MovieTimeRange timeRange, MovieTransform transform, MovieResource resource )
	{
		var block = new ProjectSequenceBlock( timeRange, transform, resource );

		_blocks.Add( block );
		Invalidate();

		return block;
	}

	public void RemoveBlock( ProjectSequenceBlock block )
	{
		_blocks.Remove( block );
		Invalidate();
	}

	public override ICompiledTrack Compile( ICompiledTrack? compiledParent, bool headerOnly )
	{
		return new CompiledReferenceTrack<GameObject>( Id, Name, (CompiledReferenceTrack<GameObject>)compiledParent! );
	}

	private void UpdateTracks()
	{
		if ( !_tracksInvalid ) return;

		_tracksInvalid = false;
		_propertyTracks.Clear();
		_referenceTracks.Clear();

		var resourceGroups = Blocks
			.GroupBy( x => x.Resource );

		var propertyTrackGenericType = typeof(ProjectSequencePropertyTrack<>);

		foreach ( var group in resourceGroups )
		{
			foreach ( var track in group.Key.GetCompiled().Tracks )
			{
				if ( track is ICompiledReferenceTrack referenceTrack )
				{
					_referenceTracks.Add( referenceTrack );
					continue;
				}

				if ( track is not IPropertyTrack propertyTrack ) continue;

				var propertyTrackType = propertyTrackGenericType
					.MakeGenericType( track.TargetType );

				var sequencePropertyTrack = (IProjectSequencePropertyTrack)Activator.CreateInstance( propertyTrackType, propertyTrack, group.AsEnumerable() )!;

				_propertyTracks.Add( sequencePropertyTrack );
			}
		}
	}

	IReadOnlyList<ITrackBlock> IProjectBlockTrack.Blocks => Blocks;
}

public sealed class ProjectSequenceBlock
	: ITrackBlock
{
	public MovieTimeRange TimeRange { get; set; }
	public MovieTransform Transform { get; set; }
	public MovieResource Resource { get; }

	[JsonConstructor]
	public ProjectSequenceBlock( MovieTimeRange timeRange, MovieTransform transform, MovieResource resource )
	{
		TimeRange = timeRange;
		Transform = transform;
		Resource = resource;
	}
}

public interface IProjectSequencePropertyTrack : IPropertyTrack
{
	ICompiledPropertyTrack Compile();
}

/// <summary>
/// A property track from a referenced <see cref="MovieResource"/>, with block transformations applied from a <see cref="ProjectSequenceBlock"/>.
/// </summary>
file sealed class ProjectSequencePropertyTrack<T> : IProjectSequencePropertyTrack, IPropertyTrack<T>
{
	public CompiledPropertyTrack<T> SourceTrack { get; }
	public ImmutableArray<ProjectSequenceBlock> Blocks { get; }

	public string Name => SourceTrack.Name;

	public ITrack Parent => SourceTrack.Parent;

	public ProjectSequencePropertyTrack( CompiledPropertyTrack<T> sourceTrack, IEnumerable<ProjectSequenceBlock> blocks )
	{
		SourceTrack = sourceTrack;
		Blocks = [.. blocks];
	}

	public ICompiledPropertyTrack Compile() => SourceTrack with { Blocks = [..CompileBlocks()] };

	private IEnumerable<ICompiledPropertyBlock<T>> CompileBlocks()
	{
		foreach ( var sequenceBlock in Blocks )
		{
			var sourceDuration = sequenceBlock.Resource.GetDuration();

			if ( !sourceDuration.IsPositive ) continue;

			var fullSourceRange = sequenceBlock.Transform.Inverse * sequenceBlock.TimeRange;

			foreach ( var (loopSourceRange, loopTransform) in sourceDuration.Repeat( fullSourceRange ) )
			{
				var sourceRange = loopTransform.Inverse * loopSourceRange;
				var sourceBlocks = SourceTrack.Blocks.Where( x => sourceRange.Intersect( x.TimeRange ) is { IsEmpty: false } );

				foreach ( var sourceBlock in sourceBlocks )
				{
					yield return sourceBlock
						.Slice( sourceRange )
						.Transform( sequenceBlock.Transform * loopTransform );
				}
			}
		}
	}

	public bool TryGetValue( MovieTime time, [MaybeNullWhen( false )] out T value )
	{
		if ( Blocks.GetBlock( time ) is not { } sequenceBlock )
		{
			value = default;
			return false;
		}

		var localTime = sequenceBlock.Transform.Inverse * time;
		var duration = sequenceBlock.Resource.GetDuration();

		// We don't want to loop the very last frame of the block if its duration is an
		// exact multiple of the sequence's duration.

		var bias = time == sequenceBlock.TimeRange.End ? -MovieTime.Epsilon : default;

		localTime -= (localTime + bias).GetFrameIndex( duration ) * duration;

		return SourceTrack.TryGetValue( localTime, out value );
	}
}

file static class CompiledBlockExtensions
{
	public static ICompiledPropertyBlock<T> Transform<T>( this ICompiledPropertyBlock<T> block, MovieTransform transform )
	{
		if ( transform == MovieTransform.Identity ) return block;

		return block switch
		{
			CompiledConstantBlock<T> constantBlock => constantBlock.Transform( transform ),
			CompiledSampleBlock<T> sampleBlock => sampleBlock.Transform( transform ),
			_ => throw new NotImplementedException()
		};
	}

	public static CompiledConstantBlock<T> Transform<T>( this CompiledConstantBlock<T> block, MovieTransform transform ) =>
		block with { TimeRange = transform * block.TimeRange };

	public static CompiledSampleBlock<T> Transform<T>( this CompiledSampleBlock<T> block, MovieTransform transform )
	{
		if ( transform.Scale != MovieTimeScale.Identity )
		{
			throw new NotImplementedException();
		}

		return block with { TimeRange = transform * block.TimeRange };
	}

	public static ICompiledPropertyBlock<T> Slice<T>( this ICompiledPropertyBlock<T> block, MovieTimeRange timeRange )
	{
		if ( timeRange.Contains( block.TimeRange ) ) return block;

		return block switch
		{
			CompiledConstantBlock<T> constantBlock => constantBlock.Slice( timeRange ),
			CompiledSampleBlock<T> sampleBlock => sampleBlock.Slice( timeRange ),
			_ => throw new NotImplementedException()
		};
	}

	public static CompiledConstantBlock<T> Slice<T>( this CompiledConstantBlock<T> block, MovieTimeRange timeRange ) =>
		block with { TimeRange = block.TimeRange.Clamp( timeRange ) };

	public static CompiledSampleBlock<T> Slice<T>( this CompiledSampleBlock<T> block, MovieTimeRange timeRange )
	{
		timeRange = block.TimeRange.Clamp( timeRange );

		var offset = block.Offset + timeRange.Start - block.TimeRange.Start;
		var firstSampleIndex = Math.Max( 0, offset.GetFrameIndex( block.SampleRate ) );
		var lastSampleIndex = Math.Min( block.Samples.Length, (offset + timeRange.Duration).GetFrameCount( block.SampleRate ) );

		var samples = block.Samples;

		if ( firstSampleIndex > 0 || lastSampleIndex < samples.Length )
		{
			samples = samples[firstSampleIndex..lastSampleIndex];
			offset -= MovieTime.FromFrames( firstSampleIndex, block.SampleRate );
		}

		return block with { Samples = samples, TimeRange = timeRange, Offset = offset };
	}
}
