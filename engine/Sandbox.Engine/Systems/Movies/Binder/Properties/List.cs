using System.Collections;

namespace Sandbox.MovieMaker.Properties;

#nullable enable

file sealed record ListItemProperty<TList, TItem>( ITrackProperty<TList?> Parent, int Index ) : ITrackProperty<TItem>
	where TList : IList<TItem>
{
	private string? _name;

	public bool IsBound => Parent.Value?.Count > Index;
	public string Name => _name ??= Index.ToString();

	public ListItemProperty( ListItemProperty<TList, TItem> copy )
	{
		// Copy constructor to avoid copying private fields

		Parent = copy.Parent;
		Index = copy.Index;
	}

	public TItem Value
	{
		get => Parent.Value is { } list && list.Count > Index ? list[Index] : default!;
		set
		{
			if ( Parent.Value is { } list && list.Count > Index )
			{
				list[Index] = value;
			}
		}
	}

	ITrackTarget ITrackProperty.Parent => Parent;
}

file sealed record ListCountProperty<TList, TItem>( ITrackProperty<TList?> Parent ) : ITrackProperty<int>
	where TList : IList<TItem>
{
	private bool _canCreate = true;

	public string Name => nameof( IList.Count );

	public int Value
	{
		get => Parent.Value?.Count ?? 0;
		set
		{
			if ( Parent.Value is { } list )
			{
				//
			}
			else if ( Parent.Parent.IsBound && Parent.CanWrite && _canCreate )
			{
				try
				{
					Parent.Value = list = Activator.CreateInstance<TList>();
				}
				catch
				{
					_canCreate = false;
					return;
				}
			}
			else
			{
				return;
			}

			while ( list.Count > value )
			{
				list.RemoveAt( list.Count - 1 );
			}

			while ( list.Count < value )
			{
				list.Add( default! );
			}
		}
	}

	ITrackTarget ITrackProperty.Parent => Parent;
}

[Expose]
file sealed class ListPropertyFactory : ITrackPropertyFactory
{
	private Type? GetElementType( ITrackTarget parent )
	{
		if ( !parent.TargetType.IsAssignableTo( typeof( IList ) ) )
		{
			return null;
		}

		foreach ( var interfaceType in parent.TargetType.GetInterfaces() )
		{
			if ( !interfaceType.IsConstructedGenericType ) continue;
			if ( interfaceType.GetGenericTypeDefinition() != typeof( IList<> ) ) continue;

			return interfaceType.GetGenericArguments()[0];
		}

		return null;
	}

	public IEnumerable<string> GetPropertyNames( ITrackTarget parent )
	{
		if ( GetElementType( parent ) is null ) return [];

		return parent is { IsBound: true, Value: IList list }
			? [nameof( IList.Count ), .. Enumerable.Range( 0, list.Count ).Select( x => x.ToString() )]
			: [nameof( IList.Count )];
	}

	public Type? GetTargetType( ITrackTarget parent, string name )
	{
		if ( GetElementType( parent ) is not { } elemType ) return null;

		if ( name == nameof( IList.Count ) )
		{
			return typeof( int );
		}

		if ( int.TryParse( name, out var value ) && value >= 0 )
		{
			return elemType;
		}

		return null;
	}

	public ITrackProperty<T> CreateProperty<T>( ITrackTarget parent, string name )
	{
		if ( GetElementType( parent ) is not { } elemType )
		{
			throw new ArgumentException( $"Parent needs to be an {nameof( IList<> )} property.", nameof( parent ) );
		}

		if ( name == nameof( IList.Count ) )
		{
			if ( typeof( T ) != typeof( int ) )
			{
				throw new ArgumentException( $"Expected {nameof( Int32 )} property type.", nameof( T ) );
			}

			var propertyType = typeof( ListCountProperty<,> )
				.MakeGenericType( parent.TargetType, elemType );

			return (ITrackProperty<T>)Activator.CreateInstance( propertyType, [parent] )!;
		}

		if ( int.TryParse( name, out var index ) )
		{
			if ( typeof( T ) != elemType )
			{
				throw new ArgumentException( $"Expected {elemType} property type.", nameof( T ) );
			}

			var propertyType = typeof( ListItemProperty<,> )
				.MakeGenericType( parent.TargetType, elemType );

			return (ITrackProperty<T>)Activator.CreateInstance( propertyType, [parent, index] )!;
		}

		throw new ArgumentException( "Unknown property name.", nameof( name ) );
	}
}
