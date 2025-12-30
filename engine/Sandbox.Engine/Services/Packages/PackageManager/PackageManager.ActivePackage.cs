using System.IO;
using System.Net;
using System.Threading;

namespace Sandbox;

internal static partial class PackageManager
{
	/// <summary>
	/// Describes a package that is currently mounted. Mounted packages are shared between client, server and editor.
	/// We keep track of which host is using which package using Tags.
	/// </summary>
	public class ActivePackage : ICompileReferenceProvider
	{
		public Package Package { get; private set; }
		public BaseFileSystem FileSystem { get; private set; }

		public PackageFileSystem PackageFileSystem { get; private set; }

		public BaseFileSystem AssemblyFileSystem { get; private set; }

		/// <summary>
		/// The project settings folder
		/// </summary>
		public BaseFileSystem ProjectSettings { get; private set; }

		/// <summary>
		/// The project's localization folder
		/// </summary>
		public BaseFileSystem Localization { get; private set; }

		public HashSet<string> Tags { get; } = new( StringComparer.OrdinalIgnoreCase );

		/// <summary>
		/// Mounted on FileSystem, this is where the codearchive is mounted to
		/// </summary>
		MemoryFileSystem memoryFileSystem;

		internal static async Task<ActivePackage> Create( Package package, CancellationToken token, PackageLoadOptions options )
		{
			var o = new ActivePackage();
			o.Package = package;

			if ( package is LocalPackage localPackage )
			{
				var projectSettingsPath = System.IO.Path.Combine( localPackage.Project.GetRootPath(), "ProjectSettings" );

				o.ProjectSettings = new AggregateFileSystem();
				if ( System.IO.Directory.Exists( projectSettingsPath ) )
				{
					o.ProjectSettings.CreateAndMount( projectSettingsPath );
				}

				o.Localization ??= new AggregateFileSystem();
				if ( System.IO.Directory.Exists( localPackage.LocalizationPath ) )
				{
					o.Localization.CreateAndMount( localPackage.LocalizationPath );
				}

				o.FileSystem = new AggregateFileSystem();

				if ( System.IO.Directory.Exists( localPackage.CodePath ) )
				{
					if ( localPackage.CodePath != null )
					{
						o.FileSystem.CreateAndMount( localPackage.CodePath );
					}
				}

				if ( System.IO.Directory.Exists( localPackage.ContentPath ) )
				{
					o.FileSystem.CreateAndMount( localPackage.ContentPath );
				}

				o.AssemblyFileSystem = new AggregateFileSystem();

				if ( Application.IsStandalone )
				{
					var binPath = Path.Combine( localPackage.Project.GetRootPath(), ".bin" );
					System.IO.Directory.CreateDirectory( binPath );
					o.AssemblyFileSystem.CreateAndMount( binPath );
				}
				else
				{
					o.AssemblyFileSystem.Mount( localPackage.AssemblyFileSystem );
				}

			}
			else
			{
				await o.DownloadAsync( token, options );
			}

			ActivePackages.Add( o );

			o.Mount();

			return o;
		}

		public void AddContextTag( string tag )
		{
			Tags.Add( tag );

			// this tag just became active
			OnPackageInstalledToContext?.Invoke( this, tag );
		}

		public void RemoveContextTag( string tag )
		{
			Tags.Remove( tag );
		}

		/// <summary>
		/// Set the filesystem up from this downloaded asset
		/// </summary>
		private async Task DownloadAsync( CancellationToken token, PackageLoadOptions options )
		{
			Assert.True( Package.IsRemote );

			PackageFileSystem = await Package.Download( token, options );

			if ( PackageFileSystem is null )
			{
				throw new WebException( $"Unable to download package '{Package.FullIdent}'" );
			}

			//
			// Mount downloaded filesystem as our main filesystem
			//
			FileSystem = new AggregateFileSystem();
			FileSystem.Mount( PackageFileSystem );

			//
			// Mount localization data from this package
			//
			Localization ??= new AggregateFileSystem();
			if ( FileSystem.DirectoryExists( "localization" ) )
			{
				// Mount as a subsystem of the package's FileSystem
				Localization.Mount( FileSystem.CreateSubSystem( "localization" ) );
			}

			//
			// If the ProjectSettings folder exists, we can create a filesystem for it.
			// If not, just create a memory filesystem, which will be empty, but at least won't be null.
			//
			if ( FileSystem.DirectoryExists( "ProjectSettings" ) )
			{
				ProjectSettings = FileSystem.CreateSubSystem( "ProjectSettings" );
			}
			else
			{
				ProjectSettings = new MemoryFileSystem();
			}

			//
			// Mount assembly from this package
			//
			AssemblyFileSystem ??= new AggregateFileSystem();
			if ( FileSystem.DirectoryExists( ".bin" ) )
			{
				// Mount as a subsystem of the package's FileSystem
				AssemblyFileSystem.Mount( FileSystem.CreateSubSystem( ".bin" ) );
			}
		}

		private void Mount()
		{
			MountedFileSystem.Mount( FileSystem );
			MountedFileSystem.Mount( AssemblyFileSystem );

			// Reload any already resident resources with the ones we've just mounted
			NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources();

			// Sandbox.FileSystem.Mounted.Mount( FileSystem );

			// this only makes sense if the package is a local package
			// Engine.SearchPath.Add( AbsolutePath, "GAME", true );
		}

		/// <summary>
		/// Called to unmount and remove this package from being active
		/// </summary>
		public void Delete()
		{
			MountedFileSystem.UnMount( FileSystem );
			MountedFileSystem.UnMount( AssemblyFileSystem );

			FileSystem.Dispose();
			FileSystem = default;

			PackageFileSystem?.Dispose();
			PackageFileSystem = null;

			AssemblyFileSystem.Dispose();
			AssemblyFileSystem = null;

			// Reload any resident resources that were just unmounted (they shouldn't be used & will appear as an error, or a local variant)
			NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources();
		}

		internal bool HasCodeArchives()
		{
			return FileSystem.FindFile( "/", "*.cll", true ).Any();
		}

		internal async Task<bool> CompileCodeArchive()
		{
			// get all the code archives
			var codeArchives = FileSystem.FindFile( "/", "*.cll", true ).ToArray();

			// It's okay for packages not to have code archives, but return as a fail
			if ( codeArchives.Count() == 0 )
				return false;

			var analytic = new Api.Events.EventRecord( "package.compile" );
			analytic.SetValue( "package", Package.FullIdent );
			analytic.SetValue( "version", Package.Revision?.VersionId );
			analytic.SetValue( "archives", codeArchives );

			Assert.AreNotEqual( 0, codeArchives.Length, "We have package files mounted" );

			var group = new CompileGroup( Package.Ident );
			group.AccessControl = AccessControl;
			group.ReferenceProvider = this;

			using ( analytic.ScopeTimer( "LoadArchives" ) )
			{
				foreach ( var file in codeArchives )
				{
					var bytes = await FileSystem.ReadAllBytesAsync( file );
					if ( bytes is null || bytes.Length <= 1 )
						throw new System.Exception( "Couldn't load code archive - error opening" );
					// Deserialize to a code archive
					var archive = new CodeArchive( bytes );
					// Create a compiler for it
					var compiler = group.GetOrCreateCompiler( archive.CompilerName );
					compiler.UpdateFromArchive( archive );
				}
			}

			// Compile that bad boy
			using ( analytic.ScopeTimer( "Compile" ) )
			{
				await group.BuildAsync();
			}

			if ( !group.BuildResult.Success )
			{
				// Add an analytic so we can track these failures on the backend
				var er = new Api.Events.EventRecord( "package.compile.error" );
				er.SetValue( "package", Package.FullIdent );
				er.SetValue( "version", Package.Revision?.VersionId );
				er.SetValue( "errors", group.BuildResult.BuildDiagnosticsString( Microsoft.CodeAnalysis.DiagnosticSeverity.Error ) );
				er.Submit();

				return false;
			}

			analytic.SetValue( "Diagnostics", group.BuildResult.Diagnostics
												.Where( x => x.Severity > Microsoft.CodeAnalysis.DiagnosticSeverity.Warning )
												.Select( x => new
												{
													x.Severity,
													x.Location?.SourceTree?.FilePath,
													x.Location?.GetLineSpan().StartLinePosition,
													Message = x.GetMessage()
												} )
												.ToArray() );

			// Should be successful
			Assert.True( group.BuildResult.Success );

			using ( analytic.ScopeTimer( "Write" ) )
			{
				memoryFileSystem = new MemoryFileSystem();
				memoryFileSystem.CreateDirectory( "/.bin" );
				// Copy the compiled assemblies to the filesystem
				foreach ( var assembly in group.BuildResult.Output )
				{
					Log.Trace( $"WRITE /.bin/{assembly.Compiler.AssemblyName}.dll" );
					memoryFileSystem.WriteAllBytes( $"/.bin/{assembly.Compiler.AssemblyName}.dll", assembly.AssemblyData );
				}
				FileSystem.Mount( memoryFileSystem );
			}

			analytic.Submit();

			return true;
		}

		public Microsoft.CodeAnalysis.PortableExecutableReference Lookup( string reference )
		{
			// we can't do anything unless it's in a package
			if ( !reference.StartsWith( "package." ) )
				return default;

			var targetAssemblyName = $"{reference}.dll";
			Log.Trace( $"ActivePackage: Looking for reference: {targetAssemblyName}" );

			//
			// Do any of the active packages have this dll?
			//
			foreach ( var package in ActivePackages )
			{
				if ( package == this )
					continue;

				// TODO - maybe we should filter to make sure the package has the same tag as us?

				var found = package.AssemblyFileSystem.FindFile( "/", targetAssemblyName, true ).FirstOrDefault();
				if ( found == null ) continue;

				var bytes = package.AssemblyFileSystem.ReadAllBytes( found ).ToArray();
				return Microsoft.CodeAnalysis.MetadataReference.CreateFromImage( bytes );
			}

			return default;
		}
	}
}
