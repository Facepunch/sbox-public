using Sandbox.Internal;
using System.Reflection;

namespace Sandbox;

internal sealed class LocalClientAssemblies : IDisposable
{
	readonly LoadContext _loadContext = new( typeof( LocalClientAssemblies ).Assembly );
	readonly List<Assembly> _assemblies = new();

	public IReadOnlyList<Assembly> Assemblies => _assemblies;

	public bool Contains( Assembly assembly ) => _assemblies.Contains( assembly );

	public static LocalClientAssemblies Load( IReadOnlyList<(string Name, byte[] Bytes)> source )
	{
		var set = new LocalClientAssemblies();

		try
		{
			foreach ( var (name, bytes) in source )
			{
				if ( bytes is null || bytes.Length == 0 )
					throw new InvalidOperationException( $"No compiled bytes for {name}" );

				var assembly = set._loadContext.LoadWithEmbeds( bytes, false )
					?? throw new InvalidOperationException( $"Failed to load {name}" );

				set._assemblies.Add( assembly );
			}

			set.SortByDependency();
			return set;
		}
		catch
		{
			set.Dispose();
			throw;
		}
	}

	void SortByDependency()
	{
		var byName = _assemblies.ToDictionary( x => x.GetName().Name );
		var sorted = new List<Assembly>();
		var visited = new HashSet<Assembly>();

		void Visit( Assembly assembly )
		{
			if ( !visited.Add( assembly ) )
				return;

			foreach ( var reference in assembly.GetReferencedAssemblies() )
			{
				if ( byName.TryGetValue( reference.Name, out var dependency ) )
					Visit( dependency );
			}

			sorted.Add( assembly );
		}

		foreach ( var assembly in _assemblies )
		{
			Visit( assembly );
		}

		_assemblies.Clear();
		_assemblies.AddRange( sorted );
	}

	public void Dispose()
	{
		foreach ( var assembly in _assemblies )
		{
			ReflectionQueryCache.RemoveAssembly( assembly );
			ReflectionCache.RemoveAssembly( assembly );
			_loadContext.UnloadChild( assembly );
		}

		_assemblies.Clear();
		_loadContext.Unload();
	}
}
