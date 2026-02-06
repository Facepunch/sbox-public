using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;


/// <summary>
/// Takes a bunch of assemblies and works out the proper loading order for them
/// using topological sort to ensure dependencies are loaded before dependents.
/// </summary>
internal class AssemblyOrderer
{
	private readonly List<Entry> entries = new();

	/// <summary>
	/// Represents an assembly and its dependencies.
	/// </summary>
	class Entry
	{
		/// <summary>Unique identifier for this assembly</summary>
		public string Ident;

		/// <summary>The actual assembly name from metadata</summary>
		public string AssemblyName;

		/// <summary>The raw assembly bytes</summary>
		public byte[] Bytes;

		/// <summary>Names of assemblies this assembly depends on</summary>
		public List<string> References;
	}

	/// <summary>
	/// Adds an assembly to the orderer for dependency resolution.
	/// </summary>
	/// <param name="ident">Unique identifier for this assembly</param>
	/// <param name="assemblyBytes">Raw bytes of the assembly</param>
	public void Add( string ident, byte[] assemblyBytes )
	{
		var e = new Entry
		{
			Ident = ident,
			Bytes = assemblyBytes,
			References = new List<string>()
		};

		entries.Add( e );

		// Read assembly metadata to extract name and dependencies
		using ( var stream = new MemoryStream( assemblyBytes ) )
		{
			using var reader = new PEReader( stream );
			var metadataReader = reader.GetMetadataReader();

			var assemblyDef = metadataReader.GetAssemblyDefinition();
			e.AssemblyName = metadataReader.GetString( assemblyDef.Name );

			// Collect all referenced assembly names
			foreach ( var handle in metadataReader.AssemblyReferences )
			{
				var reference = metadataReader.GetAssemblyReference( handle );
				e.References.Add( metadataReader.GetString( reference.Name ) );
			}
		}
	}

	/// <summary>
	/// Gets all assemblies in dependency order (dependencies before dependents).
	/// </summary>
	/// <returns>Enumerable of (identifier, bytes) tuples in load order</returns>
	public IEnumerable<(string Identifier, byte[] Bytes)> GetDependencyOrdered()
	{
		var sortedAssemblyIdentifiers = TopologicalSort();
		foreach ( var entry in sortedAssemblyIdentifiers )
		{
			yield return (entry.Ident, entry.Bytes);
		}
	}

	/// <summary>
	/// Performs a topological sort on the assemblies based on their dependencies.
	/// This ensures that no assembly is ordered before its dependencies.
	/// </summary>
	private List<Entry> TopologicalSort()
	{
		var remaining = new List<Entry>( entries );
		var sorted = new List<Entry>();

		// Kahn's algorithm: repeatedly find entries with no remaining dependencies
		while ( remaining.Any() )
		{
			bool foundIndependent = false;

			// Find an entry with no unresolved dependencies
			foreach ( var entry in remaining )
			{
				if ( HasUnresolvedDependencies( entry, remaining ) )
					continue;

				// This entry has no dependencies left in the remaining set
				sorted.Add( entry );
				remaining.Remove( entry );
				foundIndependent = true;
				break;
			}

			// If we couldn't find any entry without dependencies, we have a circular dependency
			if ( !foundIndependent )
			{
				Log.Warning( "Failed to resolve assembly sort order - circular dependency detected" );
				return entries; // Return original order as fallback
			}
		}

		return sorted;
	}

	/// <summary>
	/// Checks if an entry has dependencies that are still in the remaining (unprocessed) set.
	/// </summary>
	/// <param name="entry">The entry to check</param>
	/// <param name="remaining">Set of entries not yet processed</param>
	/// <returns>True if there are unresolved dependencies</returns>
	private bool HasUnresolvedDependencies( Entry entry, List<Entry> remaining )
	{
		// Find which of this entry's references are in our assembly set
		var relevantDependencies = entry.References
			.Where( refName => entries.Any( e => e.AssemblyName == refName ) )
			.ToHashSet();

		// Remove dependencies that have already been processed
		relevantDependencies.RemoveWhere( refName => !remaining.Any( e => e.AssemblyName == refName ) );

		// If any dependencies remain, they're unresolved
		return relevantDependencies.Any();
	}

}
