using System.Collections.Concurrent;
using System.Reflection;

namespace Editor;

/// <summary>
/// Lets all native and managed tools know about any engine / game entities.
/// </summary>
public static partial class GameData
{
	/// <summary>
	/// This gets set when the assemblies are changed, which means we have fresh game data... maybe.
	/// This ultimately triggers a tools.gamedata.refresh on the next frame.
	/// </summary>
	internal static bool GameDataDirty { get; set; }

	/// <summary>
	/// Add this assembly to the console library, which will scan it for console commands and make them available.
	/// </summary>
	static internal async void AddAssembly( Assembly assembly )
	{
		if ( assembly == null )
			return;

		var classes = Parse( assembly, await GetAssemblyPackage( assembly ) );

		// Content build jobs in this process read the native classes (a model's game data)
		await ContentCompilerHost.NoJobsRunning();

		Add( classes );
		GameDataDirty = true;
	}

	/// <summary>
	/// For a content worker, before any job: the classes the editor has, from the native game data model compiles read
	/// and the tools' and projects' assemblies, as the editor adds them.
	/// </summary>
	internal static void InitializeForCompiles( Native.CGameData gameData, IEnumerable<Assembly> assemblies )
	{
		Initialize( gameData );
		foreach ( var assembly in assemblies ) Add( Parse( assembly, null ) );
	}

	/// <summary>
	/// Types that Hammer and ModelDoc should be aware of: entities, paths (always entities?), path nodes, model game data
	/// </summary>
	static ConcurrentBag<MapClass> Parse( Assembly assembly, Package package )
	{
		var toAdd = new ConcurrentBag<MapClass>();

		System.Threading.Tasks.Parallel.ForEach( assembly.GetTypes(), ( t ) =>
		{
			var hammer = t.HasAttribute( typeof( HammerEntityAttribute ), false ) || t.HasAttribute( typeof( PathNodeAttribute ), false );
			var modelDoc = t.HasAttribute( typeof( Sandbox.ModelEditor.GameDataAttribute ), false ) || t.GetInterface( "Sandbox.IModelBreakCommand" ) != null;

			if ( !hammer && !modelDoc )
				return;

			try
			{
				MapClass mapClass = hammer ? EntityParser.ParseType( t ) : ModelDocParser.ParseType( t );
				if ( mapClass == null ) return;

				mapClass.Assembly = assembly;
				mapClass.Package = package;

				toAdd.Add( mapClass );
			}
			catch ( System.Exception ex )
			{
				Log.Warning( ex, $"Failed to parse Hammer entity {t}, an exception occurred: {ex.Message}" );
			}
		} );

		return toAdd;
	}

	static void Add( IEnumerable<MapClass> classes )
	{
		foreach ( var mapClass in classes )
		{
			// If something has the same class name as an entity already there, ignore it
			if ( InternalEntityClasses.Any( e => e.Name == mapClass.Name ) )
			{
				// Log.Warning( $"Skipping {mapClass.Name} from {assembly}" ); // This will probably spam for packages that embed base entities
				continue;
			}

			InternalEntityClasses.Add( mapClass );

			//
			// Update our native class if it already exists, we never want to delete it since it's definitely referenced in 100 places
			// We should also consider using smart pointers on the native side.
			//
			var nativeClass = NativeGameData.ClassForName( mapClass.Name, out int __ );
			if ( !nativeClass.IsValid )
			{
				nativeClass = Native.CGameDataClass.Create();
				NativeGameData.AddClass( mapClass.ToNative( nativeClass ), $"sbox/{mapClass.Assembly.GetName().Name}" );
			}
			else
			{
				mapClass.ToNative( nativeClass );
			}
		}
	}

	/// <summary>
	/// Remove this assembly and its console commands.
	/// </summary>
	static internal void RemoveAssembly( Assembly assembly )
	{
		if ( assembly == null )
			return;

		foreach ( var t in InternalEntityClasses.Where( x => x.Assembly == assembly ).ToArray() )
		{
			InternalEntityClasses.Remove( t );

			//
			// Removing from native is a bit tricky, we can't just remove it since it's probably referenced in 100 places
			// It's not really doing any harm sticking around though... In AddAssembly we write over it
			//
		}

		GameDataDirty = true;
	}

	[EditorEvent.Frame]
	static internal void RunFrame()
	{
		if ( GameDataDirty )
		{
			GameDataDirty = false;
			EditorEvent.Run( "tools.gamedata.refresh" );
		}
	}

	static async Task<Package> GetAssemblyPackage( Assembly assembly )
	{
		//
		// Assemblies are named package.org.ident - they do not have a good way to tell if they're local?
		// So I suppose for now we don't care about the difference
		//

		var name = assembly.GetName().Name;

		//
		// Some entities (lights) come from Sandbox.Game and don't have a package
		//
		if ( !name.StartsWith( "package." ) )
		{
			return null;
		}

		var ident = name[8..];

		if ( !Package.TryParseIdent( ident, out _ ) )
		{
			return await Package.Fetch( $"{ident}#local", true ); // :|
		}

		return await Package.Fetch( ident, true );
	}
}
