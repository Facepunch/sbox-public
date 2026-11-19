using System.Linq;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// The maps Borealis.Source2 read into the world (<see cref="SceneMap.Geometry"/>, no native scene objects): each
/// map's objects made in this world's render world while the map is in the scene world (worlds that include it draw them too).
/// </summary>
internal sealed partial class WorldRender
{
	readonly Dictionary<SceneMap, (MapGeometry Geometry, MapGeometry.Instance Instance)> maps = new();
	int mapsSeen = -1;

	/// <summary>Add maps the scene world gained, take out those it lost, and make what has loaded since.</summary>
	void UpdateMaps()
	{
		if ( MirrorContent.Gpu is not { } gpu ) return;

		if ( mapsSeen != source.MapsVersion )
		{
			mapsSeen = source.MapsVersion;

			List<SceneMap> current;
			lock ( source.InternalSceneMaps ) current = source.InternalSceneMaps.Where( x => x.Geometry is not null ).ToList();

			List<SceneMap> gone = null;
			foreach ( var (map, made) in maps )
			{
				if ( !current.Contains( map ) || map.Geometry != made.Geometry ) (gone ??= new()).Add( map );
			}

			if ( gone is not null )
			{
				foreach ( var map in gone )
				{
					maps.Remove( map, out var made );
					made.Instance.Dispose();
				}
			}

			foreach ( var map in current )
			{
				if ( !maps.ContainsKey( map ) ) maps[map] = (map.Geometry, map.Geometry.Instantiate( gpu, World, 0 ));
			}
		}

		foreach ( var (_, made) in maps ) made.Instance.Update();
	}

	/// <summary>Take every map's objects out, and let go of what they held.</summary>
	void DisposeMaps()
	{
		foreach ( var (_, made) in maps ) made.Instance.Dispose();
		maps.Clear();
		mapsSeen = -1;
	}
}
