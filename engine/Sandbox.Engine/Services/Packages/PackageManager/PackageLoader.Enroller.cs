using System.IO;

namespace Sandbox;

internal sealed partial class PackageLoader : IDisposable
{
	/// <summary>
	/// Holds a list of assemblies with the intention of enrolling them into
	/// services like TypeLibrary, Event. Handles deduplication and switching due to 
	/// hotloading etc..
	/// </summary>
	public class Enroller : System.IDisposable
	{

		// TODO: do we need to handle the situation where an assembly is hotloaded, or changed
		// in a way that means it adds other assemblies? Or will that never happen?

		public string Name { get; private set; }

		private PackageLoader packageLoader;

		/// <summary>
		/// All of the assembles loaded into this enroller
		/// </summary>
		private HashSet<LoadedAssembly> loaded = new HashSet<LoadedAssembly>();

		internal Enroller( PackageLoader loader, string name )
		{
			this.packageLoader = loader;
			this.Name = name;

			Log.Trace( $"Enroller: {Name} - Create" );
		}

		/// <summary>
		/// Get all of the loaded assemblies. This is only to be used when sending 
		/// assemblies to children.
		/// </summary>
		public LoadedAssembly[] GetLoadedAssemblies() => loaded.ToArray();

		public void Dispose()
		{
			if ( packageLoader == null )
			{
				throw new ObjectDisposedException( Name );
			}

			packageLoader.Enrollers.Remove( this );

			foreach ( var a in loaded )
			{
				OnAssemblyRemoved?.Invoke( a );
			}

			Log.Trace( $"Enroller: {Name} - Remove" );

			packageLoader = null;
			loaded = null;
		}

		/// <summary>
		/// Loads a package and its dependencies. Developer sessions enroll assemblies from the network instead.
		/// </summary>
		public void LoadPackage( string packageName, bool enrollAssemblies = true )
		{
			packageLoader.LoadPackage( packageName, true );
			if ( !enrollAssemblies ) return;

			foreach ( var assembly in packageLoader.GetLoadedAssemblies( packageName ) )
			{
				Add( assembly );
			}
		}

		public bool LoadAssemblyFromStream( string name, Stream stream )
		{
			Assert.False( name.Contains( '/' ), "Assembly name shouldn't contain a slash" );
			Assert.False( name.Contains( '\\' ), "Assembly name shouldn't contain a slash" );
			Assert.False( name.Contains( ':' ), "Assembly name shouldn't contain a colon" );

			if ( !packageLoader.LoadAssemblyFromStream( name, stream, out LoadedAssembly assembly ) )
			{
				return false;
			}

			if ( !assembly.IsFirstVersion )
			{
				return true;
			}

			// if this is the first time we're loading this assembly, then we need to add it

			Assert.True( loaded.All( x => x.Name != name ) );

			Add( assembly );

			return true;
		}

		/// <summary>
		/// Called by PackageLoader when the assembly has undergone a fast hotload
		/// </summary>
		internal void OnHotloadEvent( LoadedAssembly incoming )
		{
			Log.Trace( $"Enroller: OnHotloadEvent - {Name} {incoming.Assembly}" );

			OnAssemblyFastHotload?.Invoke( incoming );
		}

		/// <summary>
		/// Called after hotload, to register a swapped assembly
		/// </summary>
		internal void OnRegisterEvent( LoadedAssembly incoming )
		{
			Log.Trace( $"Enroller: OnRegisterEvent - {Name} {incoming.Assembly}" );

			loaded.Add( incoming );
			OnAssemblyAdded?.Invoke( incoming );
		}

		/// <summary>
		/// Called after hotload, to register a swapped assembly
		/// </summary>
		internal void OnUnregisterEvent( LoadedAssembly outgoing )
		{
			Log.Trace( $"Enroller: OnUnregisterEvent - {Name} {outgoing.Assembly}" );


			if ( !loaded.Remove( outgoing ) )
			{
				Log.Trace( $"Enroller: {Name} - {outgoing.Assembly} - not found" );
				foreach ( var l in loaded )
				{
					Log.Trace( $" - {l.Assembly}" );
				}
				return;
			}

			OnAssemblyRemoved?.Invoke( outgoing );
		}

		/// <summary>
		/// Called internally on all the assemblies when a package is loaded.
		/// This then calls down to all of the listeners, to let them know.
		/// </summary>
		internal void Add( LoadedAssembly ev )
		{
			if ( loaded.Contains( ev ) ) return;

			Log.Trace( $"Enroller: {Name} - Adding  {ev.Assembly}" );
			loaded.Add( ev );

			OnAssemblyAdded?.Invoke( ev );
		}

		internal LoadedAssembly FindAssembly( Package package, string v )
		{
			return loaded.FirstOrDefault( x => x.Package == package && x.Name == v );
		}

		public Action<LoadedAssembly> OnAssemblyAdded;
		public Action<LoadedAssembly> OnAssemblyRemoved;
		public Action<LoadedAssembly> OnAssemblyFastHotload;
	}

	List<Enroller> Enrollers = new();

	public Enroller CreateEnroller( string name )
	{
		var enroller = new Enroller( this, name );
		Enrollers.Add( enroller );
		return enroller;
	}
}
