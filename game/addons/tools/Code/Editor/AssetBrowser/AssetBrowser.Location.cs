using System.IO;

namespace Editor;

public partial class AssetBrowser : Widget
{
	public enum LocationType
	{
		Generic,
		Assets,
		Code,
		Localization
	}

	public abstract record Location
	{
		public string Path { get; init; }
		public string RelativePath { get; init; }

		public string Name { get; init; }
		public string Icon { get; init; }
		public string ContentsIcon { get; init; }

		public Project Project { get; init; }

		public string RootPath { get; init; }
		public string RootTitle { get; init; }
		public bool IsRoot { get; init; } = false;

		public virtual LocationType Type { get; init; } = LocationType.Generic;

		/// <summary>
		/// Is this location an aggregate of multiple locations, such as "Recents" or "Everything"
		/// </summary>
		public virtual bool IsAggregate => false;

		protected Location( string name, string icon )
		{
			Name = name;
			Path = name;
			Icon = icon;
		}

		public virtual bool CanGoUp() => !IsRoot;
		public virtual bool IsValid() => true;

		public virtual IEnumerable<Location> GetDirectories()
		{
			throw new NotImplementedException();
		}

		public virtual IEnumerable<FileInfo> GetFiles()
		{
			throw new NotImplementedException();
		}

		public static bool TryParse( string absolutePath, out Location location )
		{
			// file path -> containing directory
			if ( !string.IsNullOrEmpty( System.IO.Path.GetExtension( absolutePath ) ) )
				absolutePath = absolutePath[..(absolutePath.LastIndexOf( '/' ))];

			if ( absolutePath.Equals( "@everything", StringComparison.OrdinalIgnoreCase ) )
			{
				location = new EverythingLocation();
				return true;
			}

			if ( absolutePath.Equals( "@recents", StringComparison.OrdinalIgnoreCase ) )
			{
				location = new RecentsLocation();
				return true;
			}

			if ( absolutePath.StartsWith( "mount://", StringComparison.OrdinalIgnoreCase ) )
			{
				var sourceName = absolutePath.Substring( 8 );

				var i = sourceName.IndexOf( '/' );
				string ident = i == -1 ? sourceName : sourceName.Substring( 0, i );
				string relative = i == -1 ? "" : sourceName[i..];

				var host = Sandbox.Mounting.Directory.Get( ident );
				if ( host is null )
				{
					location = null;
					return false;
				}

				location = new MountLocation( host, absolutePath.TrimEnd( '/' ) );
				return true;
			}

			if ( Directory.Exists( absolutePath ) )
			{
				var dir = new DirectoryInfo( absolutePath );
				if ( dir.Attributes.HasFlag( FileAttributes.Hidden ) )
				{
					location = null;
					return false;
				}

				location = new DiskLocation( dir );
				return true;
			}

			location = null;
			return false;
		}
	}
}
