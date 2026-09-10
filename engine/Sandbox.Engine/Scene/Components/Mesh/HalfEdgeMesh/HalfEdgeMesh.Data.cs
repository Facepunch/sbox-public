using System.Collections;

namespace HalfEdgeMesh;

partial class Mesh
{
	public VertexData<TData> CreateVertexData<TData>( string name ) where TData : struct =>
		VertexList.CreateDataStream<VertexData<TData>>( name );

	public FaceData<TData> CreateFaceData<TData>( string name ) where TData : struct =>
		FaceList.CreateDataStream<FaceData<TData>>( name );

	public HalfEdgeData<TData> CreateHalfEdgeData<TData>( string name ) where TData : struct =>
		HalfEdgeList.CreateDataStream<HalfEdgeData<TData>>( name );
}

internal interface IDataStream
{
	internal void Allocate( int sourceIndex );
	internal void AllocateMultiple( int count );
}

internal sealed class VertexData<TData> : ComponentData<TData> where TData : struct
{
	public TData this[VertexHandle h] { get => this[h.Index]; set => this[h.Index] = value; }
}

internal sealed class FaceData<TData> : ComponentData<TData> where TData : struct
{
	public TData this[FaceHandle h] { get => this[h.Index]; set => this[h.Index] = value; }
}

internal sealed class HalfEdgeData<TData> : ComponentData<TData> where TData : struct
{
	public TData this[HalfEdgeHandle h] { get => this[h.Index]; set => this[h.Index] = value; }
}

internal abstract class ComponentData<T> : IEnumerable<T>, IDataStream where T : struct
{
	internal readonly List<T> _list = new();

	public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public int Count => _list.Count;

	public T this[int index]
	{
		get => index >= 0 && index < Count ? _list[index] : default;
		set
		{
			if ( index >= 0 && index < Count )
				_list[index] = value;
		}
	}

	public void CopyFrom( T[] source )
	{
		int count = Math.Min( _list.Count, source.Length );
		for ( int i = 0; i < count; i++ )
			_list[i] = source[i];
	}

	void IDataStream.Allocate( int sourceIndex )
	{
		_list.Add( sourceIndex >= 0 && sourceIndex < Count ? _list[sourceIndex] : default );
	}

	void IDataStream.AllocateMultiple( int count )
	{
		_list.Capacity += count;
		for ( var i = 0; i < count; i++ )
			_list.Add( default );
	}
}

internal class ComponentList<T> : IEnumerable<T>
{
	private readonly List<T> _list = new();
	private readonly List<bool> _active = new();
	private readonly Dictionary<string, IDataStream> _streams = new();

	public int Count => _list.Count;

	public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public IEnumerable<int> ActiveList => Enumerable.Range( 0, Count ).Where( x => _active[x] );

	public T this[int index]
	{
		get => _list[index];
		internal set => _list[index] = value;
	}

	public int Allocate( T component, int sourceIndex = -1 )
	{
		_list.Add( component );
		_active.Add( true );

		foreach ( var stream in _streams.Values )
			stream.Allocate( sourceIndex );

		return Count - 1;
	}

	public void AllocateMultiple( int count, T component )
	{
		_list.Capacity += count;
		for ( var i = 0; i < count; i++ )
		{
			_list.Add( component );
			_active.Add( true );
		}

		foreach ( var stream in _streams.Values )
			stream.AllocateMultiple( count );
	}

	public void Deallocate( int index )
	{
		_active[index] = false;
	}

	public bool IsAllocated( int index )
	{
		if ( index < 0 || index >= _active.Count )
			return false;

		return _active[index];
	}

	public TDataStream CreateDataStream<TDataStream>( string name ) where TDataStream : IDataStream, new()
	{
		if ( _streams.ContainsKey( name ) )
			throw new ArgumentException( "A stream with the same name already exists.", nameof( name ) );

		var stream = new TDataStream();
		stream.AllocateMultiple( _list.Count );
		_streams[name] = stream;

		return stream;
	}
}
