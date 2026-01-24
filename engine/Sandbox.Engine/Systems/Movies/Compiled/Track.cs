using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Sandbox.MovieMaker.Compiled;

#nullable enable

/// <inheritdoc cref="ITrack"/>
public interface ICompiledTrack : ITrack, IComparable<ICompiledTrack>
{
	new ICompiledTrack? Parent { get; }

	int IComparable<ICompiledTrack>.CompareTo( ICompiledTrack? other ) => TrackComparer.Compare( this, other );
}

/// <inheritdoc cref="IReferenceTrack"/>
public interface ICompiledReferenceTrack : ICompiledTrack, IReferenceTrack
{
	new CompiledReferenceTrack<GameObject>? Parent { get; }

	ICompiledTrack? ICompiledTrack.Parent => Parent;
	IReferenceTrack<GameObject>? IReferenceTrack.Parent => Parent;
}

public interface ICompiledBlockTrack : ICompiledTrack
{
	IReadOnlyList<ICompiledBlock> Blocks { get; }

	public MovieTimeRange TimeRange => Blocks.Count == 0 ? default : (Blocks[0].TimeRange.Start, Blocks[^1].TimeRange.End);
}

file static class TrackComparer
{
	public static int Compare( ICompiledTrack? a, ICompiledTrack? b )
	{
		if ( a is null && b is null ) return 0;
		if ( a is null ) return -1;
		if ( b is null ) return 1;

		var aDepth = a.GetDepth();
		var bDepth = b.GetDepth();

		if ( aDepth != bDepth ) return aDepth - bDepth;

		var nameCompare = a.Name.CompareTo( b.Name, StringComparison.Ordinal );
		if ( nameCompare != 0 ) return nameCompare;

		// Reference tracks can have the same name! Use track ID as a tie-breaker.

		if ( a is ICompiledReferenceTrack aRef && b is ICompiledReferenceTrack bRef )
		{
			return aRef.Id.CompareTo( bRef.Id );
		}

		return 0;
	}
}

public sealed record CompiledReferenceTrack<T>( Guid Id, string Name, CompiledReferenceTrack<GameObject>? Parent = null, Guid? ReferenceId = null ) : ICompiledReferenceTrack, IReferenceTrack<T>
	where T : class, IValid;

/// <inheritdoc cref="IActionTrack"/>
public sealed record CompiledActionTrack( string Name, Type TargetType, ICompiledTrack Parent, ImmutableArray<CompiledActionBlock> Blocks ) : IActionTrack, ICompiledBlockTrack
{
	ITrack IActionTrack.Parent => Parent;
	IReadOnlyList<ICompiledBlock> ICompiledBlockTrack.Blocks => Blocks;
}

/// <inheritdoc cref="IPropertyTrack"/>
public interface ICompiledPropertyTrack : IPropertyTrack, ICompiledBlockTrack
{
	new ICompiledTrack Parent { get; }
	new IReadOnlyList<ICompiledPropertyBlock> Blocks { get; }

	ICompiledPropertyBlock? GetBlock( MovieTime time );

	ITrack IPropertyTrack.Parent => Parent;
	IReadOnlyList<ICompiledBlock> ICompiledBlockTrack.Blocks => Blocks;
}

/// <inheritdoc cref="IPropertyTrack{T}"/>
[method: JsonConstructor]
public sealed record CompiledPropertyTrack<T>( string Name, ICompiledTrack Parent, ImmutableArray<ICompiledPropertyBlock<T>> Blocks ) : ICompiledPropertyTrack, IPropertyTrack<T>
{
	public CompiledPropertyTrack( string name, ICompiledTrack parent, IEnumerable<ICompiledPropertyBlock>? blocks )
		: this( name, parent, blocks?.Cast<ICompiledPropertyBlock<T>>().ToImmutableArray() ?? ImmutableArray<ICompiledPropertyBlock<T>>.Empty )
	{

	}

	private readonly ImmutableArray<ICompiledPropertyBlock<T>> _blocks = Validate( Blocks );

	public ImmutableArray<ICompiledPropertyBlock<T>> Blocks
	{
		get => _blocks;
		init => _blocks = Validate( value );
	}

	public ICompiledPropertyBlock<T>? GetBlock( MovieTime time )
	{
		// TODO: binary search?

		// We go backwards because if we're exactly on a block boundary, we want to use the later block

		for ( var i = Blocks.Length - 1; i >= 0; --i )
		{
			var block = Blocks[i];

			if ( block.TimeRange.Start > time ) continue;
			if ( block.TimeRange.End < time ) break;

			return block;
		}

		return default;
	}

	public bool TryGetValue( MovieTime time, [MaybeNullWhen( false )] out T value )
	{
		if ( GetBlock( time ) is { } block )
		{
			value = block.GetValue( time );
			return true;
		}

		value = default;
		return false;
	}

	IReadOnlyList<ICompiledPropertyBlock> ICompiledPropertyTrack.Blocks => Blocks;
	ICompiledPropertyBlock? ICompiledPropertyTrack.GetBlock( MovieTime time ) => GetBlock( time );

	private static ImmutableArray<ICompiledPropertyBlock<T>> Validate( ImmutableArray<ICompiledPropertyBlock<T>> blocks )
	{
		if ( blocks.IsDefault )
		{
			throw new ArgumentException( "Blocks must be an array.", nameof( Blocks ) );
		}

		if ( blocks.Length == 0 ) return blocks;

		var prevTime = blocks[0].TimeRange.Start;

		if ( prevTime < 0d )
		{
			throw new ArgumentException( "Blocks must have non-negative start times.", nameof( Blocks ) );
		}

		foreach ( var block in blocks )
		{
			if ( block.TimeRange.Start < prevTime )
			{
				throw new ArgumentException( "Blocks must not overlap.", nameof( Blocks ) );
			}

			prevTime = block.TimeRange.End;
		}

		return blocks;
	}
}
