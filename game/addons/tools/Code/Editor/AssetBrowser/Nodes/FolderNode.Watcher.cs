using System;
using System.Collections.Generic;
using System.IO;

namespace Editor;

partial class FolderNode
{
	/// <summary>
	/// Tells folder nodes when the contents of their folder change.
	///
	/// This exists so we don't spam FileSystemWatcher and shit our pants.
	/// This is the only place we use FileSystemWatcher, we should really try to use Zio instead.
	/// </summary>
	static class DirectoryWatcher
	{
		/// <summary>
		/// A recursive watcher over one tree, shared by every folder inside it.
		/// </summary>
		sealed class Root
		{
			public string Path;
			public FileSystemWatcher Watcher;
		}

		/// <summary>
		/// Paths from a watcher and paths from a location don't have to be spelled identically, so
		/// compare them the way the filesystem would.
		/// </summary>
		static readonly StringComparison PathComparison =
			OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

		static readonly StringComparer PathComparer =
			OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

		static readonly List<Root> Roots = new();

		/// <summary>
		/// Keyed by folder, because roots watch subfolders too - a build touching thousands of
		/// files would otherwise have us walk every node in the browser for each one.
		/// </summary>
		static readonly Dictionary<string, List<WeakReference<FolderNode>>> Nodes = new( PathComparer );

		/// <summary>
		/// Mark <paramref name="node"/> dirty when something in <paramref name="path"/> is created,
		/// deleted or renamed, for as long as the node is alive.
		/// </summary>
		public static void Watch( string path, FolderNode node )
		{
			if ( string.IsNullOrEmpty( path ) || node is null )
				return;

			path = Normalize( path );

			lock ( Nodes )
			{
				if ( !EnsureRootFor( path ) )
					return;

				if ( !Nodes.TryGetValue( path, out var forFolder ) )
				{
					forFolder = new List<WeakReference<FolderNode>>();
					Nodes[path] = forFolder;
				}

				forFolder.Add( new WeakReference<FolderNode>( node ) );
			}
		}

		/// <summary>
		/// Make sure a root covers <paramref name="path"/>. Roots are recursive, so anything below
		/// one that already exists needs nothing new. Caller holds the lock.
		/// </summary>
		static bool EnsureRootFor( string path )
		{
			foreach ( var root in Roots )
			{
				if ( Covers( root.Path, path ) )
					return true;
			}

			try
			{
				var watcher = new FileSystemWatcher( path ) { IncludeSubdirectories = true };

				watcher.Created += OnChanged;
				watcher.Deleted += OnChanged;
				watcher.Renamed += OnRenamed;
				watcher.EnableRaisingEvents = true;

				// Anything we were already watching that sits under this is now covered twice over
				Roots.RemoveAll( x =>
				{
					if ( !Covers( path, x.Path ) )
						return false;

					x.Watcher.Dispose();
					return true;
				} );

				Roots.Add( new Root { Path = path, Watcher = watcher } );

				return true;
			}
			catch ( Exception e )
			{
				// Out of inotify instances, or the folder went away between us being asked and
				// getting here. Neither is worth taking the asset browser down for - the folder
				// just won't notice changes made outside the editor.
				Log.Warning( $"Couldn't watch {path} for changes ({e.Message})" );
				return false;
			}
		}

		static bool Covers( string root, string path )
		{
			if ( path.Equals( root, PathComparison ) )
				return true;

			return path.StartsWith( root, PathComparison )
				&& path.Length > root.Length
				&& (path[root.Length] == '/' || path[root.Length] == '\\');
		}

		static void OnChanged( object sender, FileSystemEventArgs e ) => NotifyFolderOf( e.FullPath );

		static void OnRenamed( object sender, RenamedEventArgs e )
		{
			NotifyFolderOf( e.FullPath );

			// A rename can move something between folders, so the old one changed too
			NotifyFolderOf( e.OldFullPath );
		}

		/// <summary>
		/// Dirty whichever nodes are listening to the folder that holds <paramref name="fullPath"/>.
		/// Roots are recursive, so most of what they report belongs to some folder deeper down - and
		/// often to no folder anyone is showing.
		/// </summary>
		static void NotifyFolderOf( string fullPath )
		{
			var folder = Path.GetDirectoryName( fullPath );
			if ( string.IsNullOrEmpty( folder ) )
				return;

			folder = Normalize( folder );

			List<FolderNode> listening = null;

			lock ( Nodes )
			{
				if ( !Nodes.TryGetValue( folder, out var forFolder ) )
					return;

				for ( var i = forFolder.Count - 1; i >= 0; i-- )
				{
					if ( !forFolder[i].TryGetTarget( out var node ) )
					{
						// The node is gone and nobody told us, which is the point of the weak
						// reference - tidy it away now we've noticed
						forFolder.RemoveAt( i );
						continue;
					}

					listening ??= new List<FolderNode>();
					listening.Add( node );
				}

				if ( forFolder.Count == 0 )
					Nodes.Remove( folder );
			}

			if ( listening is null )
				return;

			// Outside the lock - these go on to touch the tree, and holding a lock across that is
			// how you get to deadlock a UI
			foreach ( var node in listening )
			{
				node.Dirty();
			}
		}

		static string Normalize( string path )
		{
			path = path.Replace( '\\', '/' );

			return path.Length > 1 && path.EndsWith( '/' )
				? path.TrimEnd( '/' )
				: path;
		}
	}
}
