using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace Sandbox.Internal;

/// <summary>
/// This isolates the dll so it can be unloaded and can have the same name as other loaded dlls
/// </summary>
class IsolatedAssemblyContext : AssemblyLoadContext
{
	public LoadContext Parent;
	public Assembly Assembly;

	public IsolatedAssemblyContext() : base( "IsolatedAssemblyContext", true )
	{
	}

	protected override Assembly Load( AssemblyName assemblyName )
	{

		return Parent.LoadFromChild( assemblyName );
	}
}

class LoadContext : AssemblyLoadContext
{
	/// <summary>
	/// If the assembly isn't found in this context, we'll load from
	/// the context used by the root Assembly.
	/// </summary>
	Assembly root;

	List<IsolatedAssemblyContext> Children = new();

	public LoadContext( Assembly root = null ) : base( "TLC", true )
	{
		this.root = root;
	}

	public Assembly LoadFromChild( AssemblyName assemblyName )
	{
		foreach ( var child in Children )
		{
			var childAsmName = child.Assembly.GetName();

			if ( childAsmName.Name != assemblyName.Name )
			{
				continue;
			}

			return child.Assembly;
		}

		return Load( assemblyName );
	}

	protected override Assembly Load( AssemblyName assemblyName )
	{
		// library.log.Trace( $"Searching for {assemblyName}" );

		foreach ( var assembly in Assemblies )
		{
			if ( assembly.GetName().Name == assemblyName.Name )
			{
				return assembly;
			}
		}

		if ( root is not null )
		{
			var rootContext = GetLoadContext( root );
			var asm = rootContext?.LoadFromAssemblyName( assemblyName ) ?? default;

			if ( asm is not null )
			{
				return asm;
			}
		}

		return base.Load( assemblyName );
	}

	/// <summary>
	/// The assembly might have a pdb embedded inside. So load it with PEReader and have a look inside to see if it
	/// is in there. Then if it is, load the assembly with the pdb.
	/// I don't know why this isn't done by default.. but apparently it's not. So we have to do it manually.
	/// </summary>
	public Assembly LoadWithEmbeds( byte[] assemblyData, bool unloadOldVersions = true )
	{
		using var stream = new MemoryStream( assemblyData );

		byte[] pdbData = null;

		var lc = new IsolatedAssemblyContext();
		lc.Parent = this;

		//
		// Open PEReader
		//
		using ( var peReader = new PEReader( stream, PEStreamOptions.LeaveOpen ) )
		{
			var pdbEntry = peReader.ReadDebugDirectory().FirstOrDefault( x => x.Type == DebugDirectoryEntryType.EmbeddedPortablePdb );

			//
			// At some point we should probably throw a fit if it doesn't have the pdb.. but we have a few months of addons
			// that do not have the pdb embedded, and it doesn't really matter that much.
			//

			if ( pdbEntry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb )
			{
				unsafe
				{
					var pdbReader = peReader.ReadEmbeddedPortablePdbDebugDirectoryData( pdbEntry ).GetMetadataReader();
					pdbData = new ReadOnlySpan<byte>( pdbReader.MetadataPointer, pdbReader.MetadataLength ).ToArray();
				}
			}
		}

		stream.Position = 0;

		//
		// We have a pdb - load using that!
		//
		if ( pdbData != null )
		{
			using var pdbStream = new System.IO.MemoryStream( pdbData );
			lc.Assembly = lc.LoadFromStream( stream, pdbStream );
		}
		else
		{
			lc.Assembly = lc.LoadFromStream( stream );
		}

		if ( unloadOldVersions )
		{
			var asmName = lc.Assembly.GetName().Name;
			var matches = Children
				.Where( x => x.Assembly.GetName().Name == asmName )
				.ToArray();

			foreach ( var oldAssembly in matches )
			{
				oldAssembly.Assembly = null;
				oldAssembly.Unload();

				Children.Remove( oldAssembly );
			}
		}

		Children.Add( lc );

		return lc.Assembly;
	}

	public void UnloadChild( Assembly asm )
	{
		var match = Children.FirstOrDefault( x => x.Assembly == asm );
		if ( match == null ) return;

		match.Assembly = null;
		match.Unload();

		Children.Remove( match );
	}
}
