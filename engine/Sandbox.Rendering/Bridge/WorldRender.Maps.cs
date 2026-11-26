namespace Sandbox.Rendering.Bridge;

/// <summary>
/// The maps Borealis.Source2 read into the world (<see cref="SceneMap.Geometry"/>, no native scene objects): each
/// map's objects made in this world's render world while the map is in the scene world (worlds that include it draw them too).
/// </summary>
internal sealed partial class WorldRender
{
	readonly Dictionary<SceneMap, (MapGeometry Geometry, MapGeometry.Instance Instance)> maps = new();
	readonly HashSet<SceneMap> currentMaps = new();
	readonly List<SceneMap> removedMaps = new();
	int mapsSeen = -1;

	/// <summary>Add maps the scene world gained, take out those it lost, and make what has loaded since.</summary>
	void UpdateMaps()
	{
		if ( MirrorContent.Gpu is not { } gpu ) return;

		if ( mapsSeen != source.MapsVersion )
		{
			mapsSeen = source.MapsVersion;

			lock ( source.InternalSceneMaps )
			{
				foreach ( var map in source.InternalSceneMaps )
					if ( map.Geometry is not null ) currentMaps.Add( map );
			}

			foreach ( var (map, made) in maps )
				if ( !currentMaps.Contains( map ) || map.Geometry != made.Geometry ) removedMaps.Add( map );

			foreach ( var map in removedMaps )
			{
				maps.Remove( map, out var made );
				made.Instance.Dispose();
			}
			removedMaps.Clear();

			foreach ( var map in currentMaps )
			{
				if ( !maps.ContainsKey( map ) ) maps[map] = (map.Geometry, map.Geometry.Instantiate( gpu, World, 0 ));
			}
			currentMaps.Clear();
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
