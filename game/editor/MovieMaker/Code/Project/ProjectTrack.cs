using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker;

#nullable enable

public partial interface IProjectTrack : ITrack, IComparable<IProjectTrack>
{
	Guid Id { get; }

	MovieProject Project { get; }
	new IProjectTrack? Parent { get; }
	IReadOnlyList<IProjectTrack> Children { get; }

	bool IsEmpty { get; }
	int Order { get; }

	void Remove();
	IProjectTrack? GetChild( string name );

	ICompiledTrack Compile( bool headerOnly ) => Compile( Parent?.Compile( headerOnly ), headerOnly );
	ICompiledTrack Compile( ICompiledTrack? compiledParent, bool headerOnly );

	ITrack? ITrack.Parent => Parent;
	IEnumerable<MovieResource> References { get; }

	int IComparable<IProjectTrack>.CompareTo( IProjectTrack? other )
	{
		if ( ReferenceEquals( this, other ) )
		{
			return 0;
		}

		if ( other is null )
		{
			return 1;
		}

		var orderComparison = Order.CompareTo( other.Order );
		if ( orderComparison != 0 ) return orderComparison;

		return string.Compare( Name, other.Name, StringComparison.Ordinal );
	}
}

internal interface IProjectTrackInternal : IProjectTrack
{
	new IProjectTrackInternal? Parent { get; set; }

	void AddChild( IProjectTrackInternal child );
	void RemoveChild( IProjectTrackInternal child );
}

public abstract partial class ProjectTrack<T>( MovieProject project, Guid id, string name ) : IProjectTrackInternal, IHotloadManaged
{
	private readonly List<IProjectTrack> _children = new();
	private bool _childrenChanged;

	public MovieProject Project { get; } = project;

	public Guid Id { get; } = id;
	public string Name { get; set; } = name;
	public Type TargetType { get; } = typeof(T);

	public IProjectTrack? Parent { get; private set; }
	public virtual IEnumerable<MovieResource> References => [];

	public virtual bool IsEmpty => Children.Count == 0;

	public virtual int Order => 0;

	public IReadOnlyList<IProjectTrack> Children
	{
		get
		{
			UpdateChildren();
			return _children;
		}
	}

	public void Remove() => Project.RemoveTrackInternal( this );

	public IProjectTrack? GetChild( string name ) => Children.FirstOrDefault( x => x.Name == name );

	public abstract ICompiledTrack Compile( ICompiledTrack? compiledParent, bool headerOnly );

	IProjectTrackInternal? IProjectTrackInternal.Parent
	{
		get => (IProjectTrackInternal?)Parent;
		set => Parent = value;
	}

	void IProjectTrackInternal.AddChild( IProjectTrackInternal child )
	{
		if ( child.Parent != null )
		{
			throw new ArgumentException( "Track already has a parent!", nameof(child) );
		}

		child.Parent = this;
		_children.Add( child );
		_childrenChanged = true;

		Project.InvalidateTracks();
	}

	void IProjectTrackInternal.RemoveChild( IProjectTrackInternal child )
	{
		if ( child.Parent == this )
		{
			child.Parent = null;
		}

		_children.Remove( child );
		_childrenChanged = true;

		Project.InvalidateTracks();
	}

	private void UpdateChildren()
	{
		if ( !_childrenChanged ) return;

		_childrenChanged = false;

		// Child tracks can become null during hotload if their value type was removed.

		_children.RemoveAll( x => (IProjectTrack?)x is null );
		_children.Sort();
	}

	void IHotloadManaged.Persisted() => _childrenChanged = true;
	void IHotloadManaged.Created( IReadOnlyDictionary<string, object?> state ) => _childrenChanged = true;
}

public partial interface IProjectReferenceTrack : IProjectTrack, IReferenceTrack
{
	public static IProjectReferenceTrack Create( MovieProject project, Guid id, string name, Type targetType )
	{
		var trackType = typeof(ProjectReferenceTrack<>).MakeGenericType( targetType );

		return (IProjectReferenceTrack)Activator.CreateInstance( trackType, project, id, name )!;
	}

	new ProjectReferenceTrack<GameObject>? Parent { get; }
	new Guid Id { get; }
	new Guid? ReferenceId { get; set; }

	IReferenceTrack<GameObject>? IReferenceTrack.Parent => Parent;
	IProjectTrack? IProjectTrack.Parent => Parent;

	Guid IReferenceTrack.Id => Id;
	Guid IProjectTrack.Id => Id;
	Guid? IReferenceTrack.ReferenceId => ReferenceId;
}

public partial class ProjectReferenceTrack<T>( MovieProject project, Guid id, string name )
	: ProjectTrack<T>( project, id, name ), IProjectReferenceTrack, IReferenceTrack<T>
	where T : class, IValid
{
	public override int Order => -1000;

	public new ProjectReferenceTrack<GameObject>? Parent => (ProjectReferenceTrack<GameObject>?)base.Parent;

	public Guid? ReferenceId { get; set; }

	public override ICompiledTrack Compile( ICompiledTrack? compiledParent, bool headerOnly ) =>
		new CompiledReferenceTrack<T>( Id, Name, (CompiledReferenceTrack<GameObject>)compiledParent!, ReferenceId );

	ITrack? ITrack.Parent => Parent;
}

public interface IProjectBlockTrack : IProjectTrack
{
	MovieTimeRange TimeRange { get; }
	IReadOnlyList<ITrackBlock> Blocks { get; }
}

public partial interface IProjectPropertyTrack : IPropertyTrack, IProjectBlockTrack
{
	public static IProjectPropertyTrack Create( MovieProject project, Guid id, string name, Type targetType )
	{
		var trackType = typeof(ProjectPropertyTrack<>).MakeGenericType( targetType );

		return (IProjectPropertyTrack)Activator.CreateInstance( trackType, project, id, name )!;
	}

	new IProjectTrack? Parent { get; }

	new IReadOnlyList<IProjectPropertyBlock> Blocks { get; }

	ITrack IPropertyTrack.Parent => Parent!;

	/// <summary>
	/// Add empty space from the start of <paramref name="timeRange"/>, with
	/// the duration of <paramref name="timeRange"/>. Will split any blocks that
	/// span the start time.
	/// </summary>
	bool Insert( MovieTimeRange timeRange );

	/// <summary>
	/// Remove the given <paramref name="timeRange"/>, then collapse the removed
	/// time range so any blocks after the end of the range will start earlier.
	/// </summary>
	bool Remove( MovieTimeRange timeRange );

	/// <summary>
	/// Remove any blocks within the <paramref name="timeRange"/>, splitting any
	/// blocks that span the start or end. This doesn't shift any blocks, so will
	/// leave an empty region of time.
	/// </summary>
	bool Clear( MovieTimeRange timeRange );

	/// <summary>
	/// Adds a block, replacing any blocks that overlap its time range.
	/// This will split any blocks that partially overlap.
	/// </summary>
	bool Add( MovieTimeRange timeRange, IPropertySignal signal );

	/// <summary>
	/// Adds a block, replacing any blocks that overlap its time range.
	/// This will split any blocks that partially overlap.
	/// </summary>
	bool Add( IProjectPropertyBlock block );

	bool AddRange( IEnumerable<IProjectPropertyBlock> blocks );

	void SetBlocks( IReadOnlyList<IProjectPropertyBlock> blocks );

	/// <summary>
	/// Copies blocks that overlap the given <paramref name="timeRange"/> and returns
	/// the copies.
	/// </summary>
	IReadOnlyList<IProjectPropertyBlock> Slice( MovieTimeRange timeRange );

	IReadOnlyList<IProjectPropertyBlock> CreateSourceBlocks( ProjectSourceClip source );
	
	IReadOnlyList<ITrackBlock> IProjectBlockTrack.Blocks => Blocks;
	IProjectTrack? IProjectTrack.Parent => Parent;
	ITrack? ITrack.Parent => Parent;
}

public sealed partial class ProjectPropertyTrack<T>( MovieProject project, Guid id, string name )
	: ProjectTrack<T>( project, id, name ), IProjectPropertyTrack, IPropertyTrack<T>
{
	private readonly List<PropertyBlock<T>> _blocks = new();
	private bool _blocksChanged;

	public MovieTimeRange TimeRange => (0d, Blocks.Select( x => x.TimeRange.End )
		.DefaultIfEmpty()
		.Max());

	public IReadOnlyList<PropertyBlock<T>> Blocks
	{
		get
		{
			UpdateBlocks();
			return _blocks;
		}
	}

	public override bool IsEmpty => base.IsEmpty && Blocks.Count == 0;

	IReadOnlyList<IProjectPropertyBlock> IProjectPropertyTrack.Blocks => Blocks;

	public override ICompiledTrack Compile( ICompiledTrack? compiledParent, bool headerOnly )
	{
		var compiled = new CompiledPropertyTrack<T>( Name, compiledParent!, [] );

		if ( headerOnly ) return compiled;

		return compiled with { Blocks = [..Blocks.SelectMany( x => x.Compile( this ) )] };
	}

	public T GetLastValue( MovieTime time ) => Blocks.GetLastBlock( time ).GetValue( time );

	public bool TryGetValue( MovieTime time, [MaybeNullWhen( false )] out T value )
	{
		if ( Blocks.GetBlock( time ) is { } block )
		{
			value = block.GetValue( time );
			return true;
		}

		value = default;
		return false;
	}

	bool IPropertyTrack.TryGetValue( MovieTime time, out object? value )
	{
		if ( TryGetValue( time, out var val ) )
		{
			value = val;
			return true;
		}

		value = null;
		return false;
	}

	public bool Insert( MovieTimeRange timeRange )
	{
		return Clear( timeRange.Start )
			| Shift( timeRange.Start, timeRange.Duration );
	}

	public bool Remove( MovieTimeRange timeRange )
	{
		return Clear( timeRange )
			| Shift( timeRange.End, -timeRange.Duration );
	}

	private bool Shift( MovieTime from, MovieTime offset )
	{
		var changed = false;

		for ( var i = 0; i < _blocks.Count; ++i )
		{
			var block = _blocks[i];

			if ( block.TimeRange.Start >= from )
			{
				_blocks[i] += offset;
				_blocksChanged = changed = true;
			}
		}

		return changed;
	}

	public bool Clear( MovieTimeRange timeRange )
	{
		var overlaps = this.GetBlocks( timeRange ).ToArray();

		if ( overlaps.Length == 0 ) return false;

		foreach ( var overlap in overlaps )
		{
			_blocks.Remove( overlap );

			if ( overlap.TimeRange.Start < timeRange.Start )
			{
				_blocks.Add( overlap.Slice( (overlap.TimeRange.Start, timeRange.Start) )! );
			}

			if ( overlap.TimeRange.End > timeRange.End )
			{
				_blocks.Add( overlap.Slice( (timeRange.End, overlap.TimeRange.End) )! );
			}
		}

		_blocksChanged = true;

		return true;
	}

	public bool Add( MovieTimeRange timeRange, PropertySignal<T> signal )
	{
		if ( timeRange.End < 0 ) return false;

		timeRange = timeRange.ClampStart( 0 );

		// Remove any overlaps

		Clear( timeRange );

		// Add to the end of _blocks, it'll get sorted later

		_blocksChanged = true;
		_blocks.AddRange( signal.AsBlocks( timeRange ) );

		return true;
	}

	public bool Add( PropertyBlock<T> block )
	{
		if ( block.TimeRange.Start < 0 )
		{
			throw new ArgumentException( "Block can't have negative start time." );
		}

		if ( _blocks.Any( x => x.TimeRange.Contains( block.TimeRange ) && x.Signal.Equals( block.Signal ) ) )
		{
			return false;
		}

		Clear( block.TimeRange );

		_blocksChanged = true;
		_blocks.Add( block );

		return true;
	}

	public bool AddRange( IEnumerable<PropertyBlock<T>> blocks )
	{
		var changed = false;

		foreach ( var block in blocks )
		{
			changed |= Add( block );
		}

		return changed;
	}

	bool IProjectPropertyTrack.Add( MovieTimeRange timeRange, IPropertySignal signal ) =>
		Add( timeRange, (PropertySignal<T>)signal );

	bool IProjectPropertyTrack.Add( IProjectPropertyBlock block ) => Add( (PropertyBlock<T>)block );

	bool IProjectPropertyTrack.AddRange( IEnumerable<IProjectPropertyBlock> blocks ) =>
		AddRange( blocks.Cast<PropertyBlock<T>>() );

	public void SetBlocks( IReadOnlyList<IProjectPropertyBlock> blocks )
	{
		_blocksChanged = true;
		_blocks.Clear();

		_blocks.AddRange( blocks.Cast<PropertyBlock<T>>() );
	}

	public IReadOnlyList<PropertyBlock<T>> Slice( MovieTimeRange timeRange )
	{
		return Blocks
			.Where( x => x.TimeRange.Intersect( timeRange ) is { } intersection && (!intersection.IsEmpty || timeRange.IsEmpty) )
			.Select( x => x.Slice( timeRange ) )
			.OfType<PropertyBlock<T>>()
			.ToImmutableArray();
	}

	IReadOnlyList<IProjectPropertyBlock> IProjectPropertyTrack.Slice( MovieTimeRange timeRange ) => Slice( timeRange );

	IReadOnlyList<IProjectPropertyBlock> IProjectPropertyTrack.CreateSourceBlocks( ProjectSourceClip source ) =>
		source.AsBlocks<T>( this );

	private void UpdateBlocks()
	{
		if ( !_blocksChanged ) return;

		_blocksChanged = false;

		// Sort by time

		_blocks.Sort( ( a, b ) => a.TimeRange.Start.CompareTo( b.TimeRange.Start ) );

		// Merge touching blocks that have identical values at their interface

		var comparer = EqualityComparer<T>.Default;

		for ( var i = _blocks.Count - 2; i >= 0; --i )
		{
			var prev = _blocks[i];
			var next = _blocks[i + 1];

			if ( prev.TimeRange.End != next.TimeRange.Start ) continue;

			var prevValue = prev.GetValue( prev.TimeRange.End );
			var nextValue = next.GetValue( next.TimeRange.Start );

			if ( !comparer.Equals( prevValue, nextValue ) )
			{
				continue;
			}

			var combinedTimeRange = prev.TimeRange.Union( next.TimeRange );
			var combinedSignal = prev.Signal.HardCut( next.Signal, prev.TimeRange.End ).Reduce( combinedTimeRange );

			_blocks[i] = new PropertyBlock<T>( combinedSignal, combinedTimeRange );
			_blocks.RemoveAt( i + 1 );
		}
	}
}
