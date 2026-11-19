using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Mounting;

/// <summary>
/// Represents a directory in the resource tree structure.
/// Contains child directories and resource files.
/// </summary>
public class ResourceFolder
{
	/// <summary>
	/// The name of this directory
	/// </summary>
	public string Name { get; private set; }

	/// <summary>
	/// The full path to this directory
	/// </summary>
	public string Path { get; private set; }

	/// <summary>
	/// Parent directory. Null if this is the root.
	/// </summary>
	public ResourceFolder Parent { get; private set; }

	/// <summary>
	/// Child directories
	/// </summary>
	public List<ResourceFolder> Folders { get; private set; } = new();

	/// <summary>
	/// Resource files in this directory
	/// </summary>
	public List<ResourceLoader> Files { get; private set; } = new();

	/// <summary>
	/// Check if this is the root directory
	/// </summary>
	public bool IsRoot => Parent == null;

	/// <summary>
	/// Get the depth level of this directory (0 for root)
	/// </summary>
	public int Depth
	{
		get
		{
			int depth = 0;
			var current = Parent;
			while ( current != null )
			{
				depth++;
				current = current.Parent;
			}
			return depth;
		}
	}

	private ResourceFolder( string name, string path, ResourceFolder parent )
	{
		Name = name;
		Path = path;
		Parent = parent;
	}

	/// <summary>
	/// Create a root directory
	/// </summary>
	internal static ResourceFolder CreateRoot()
	{
		return new ResourceFolder( "", "", null );
	}

	/// <summary>
	/// Get or create a child directory by name
	/// </summary>
	private ResourceFolder GetOrCreateChild( string name )
	{
		var existing = Folders.FirstOrDefault( f => f.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) );
		if ( existing != null )
			return existing;

		var childPath = IsRoot ? name : $"{Path}/{name}";
		var child = new ResourceFolder( name, childPath, this );
		Folders.Add( child );
		return child;
	}

	/// <summary>
	/// Add a resource loader to this directory tree, creating intermediate directories as needed
	/// </summary>
	internal void AddResource( ResourceLoader resource )
	{
		// Extract the path after "mount://mountname/"
		var path = resource.Path;
		var mountPrefix = "mount://";

		if ( !path.StartsWith( mountPrefix ) )
			return;

		// Remove mount prefix and mount name
		var afterMount = path.Substring( mountPrefix.Length );
		var firstSlash = afterMount.IndexOf( '/' );
		if ( firstSlash == -1 )
			return;

		var relativePath = afterMount.Substring( firstSlash + 1 );

		// Split into directory parts and filename
		var parts = relativePath.Split( '/', StringSplitOptions.RemoveEmptyEntries );
		if ( parts.Length == 0 )
			return;

		// Navigate/create directory structure
		var currentDir = this;
		for ( int i = 0; i < parts.Length - 1; i++ )
		{
			currentDir = currentDir.GetOrCreateChild( parts[i] );
		}

		// Add the file to the final directory
		currentDir.Files.Add( resource );
		resource.Folder = currentDir;
	}

	/// <summary>
	/// Get all files recursively from this directory and all subdirectories
	/// </summary>
	public IEnumerable<ResourceLoader> GetAllFilesRecursive()
	{
		foreach ( var file in Files )
			yield return file;

		foreach ( var folder in Folders )
		{
			foreach ( var file in folder.GetAllFilesRecursive() )
				yield return file;
		}
	}

	/// <summary>
	/// Build a directory tree from a collection of resource loaders
	/// </summary>
	internal static ResourceFolder BuildTree( IEnumerable<ResourceLoader> resources )
	{
		var root = CreateRoot();

		foreach ( var resource in resources )
		{
			root.AddResource( resource );
		}

		return root;
	}

	/// <summary>
	/// Returns true if this directory or any subdirectory contains a resource of the specified type
	/// </summary>
	public bool ContainsType( ResourceType type )
	{
		return Files.Any( f => f.Type == type ) || Folders.Any( d => d.ContainsType( type ) );
	}

}
