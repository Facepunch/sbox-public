using System;
using System.IO;
using System.Globalization;
using N = System.Numerics;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	// Bring old VMAP attributes into the format consumed by the scene importer.
	static Element[] Upgrade( Element[] elements, int schema )
	{
		if ( schema >= 29 ) return elements;
		if ( schema <= 6 && elements.Length > 0 && elements[0].GetValueOrDefault( "world" ) is Element legacyWorld &&
			legacyWorld.GetValueOrDefault( "children" ) is Element[] children )
		{
			var sets = Array.FindAll( children, x => x?.ClassName == "CMapSelectionSet" );
			elements[0]["rootSelectionSet"] = new Element( "CMapSelectionSet", "RootSelectionSet" ) { ["children"] = sets };
			legacyWorld["children"] = Array.FindAll( children, x => x?.ClassName != "CMapSelectionSet" );
		}
		// Light class and key renames precede the world-wide sky/sun merge.
		if ( schema <= 7 )
			foreach ( var element in elements ) UpgradeEarlyGeometry( element, schema );
		if ( schema <= 8 )
			foreach ( var element in elements ) UpgradeLegacyNodeName( element );
		if ( schema <= 12 )
			foreach ( var element in elements )
				UpgradeLegacyEntity( element, schema );
		// The v22 variable type migration changed Hammer's property editor metadata.
		// Scene bindings use the stored names, keys and values in either schema.
		if ( schema <= 21 && elements.Length > 0 && elements[0].GetValueOrDefault( "world" ) is Element world )
			UpgradeWorldLights( world, schema );
		if ( schema <= 27 && elements.Any( DisallowsGridOverride ) )
			UpgradeGridOverrides( elements[0] );
		foreach ( var node in elements )
		{
			if ( schema <= 27 && node.ClassName == "CMapWorld" )
				node["mapUsageType"] = node.Get( "backgroundMap", false ) ? "background" : "standard";
			if ( node.ClassName == "CMapMesh" && node.GetValueOrDefault( "physicsSimplificationError" ) is float error && error > 0 )
			{
				// Before version 29 this was already the squared error used by the decimator.
				node["physicsSimplificationError"] = MathF.Sqrt( error );
				node["physicsSimplificationOverride"] = true;
			}
			if ( schema <= 16 ) UpgradeLegacyMeshAndTileData( node, schema );
			if ( schema <= 20 ) RemoveLegacyProxies( node );
			if ( schema <= 17 && node.ClassName == "CDmeDotaTileGrid" && node.ContainsKey( "blendTransition" ) )
			{
				var values = node.Numbers<int>( "blendTransition" );
				var colors = new Color32[values.Length];
				for ( int i = 0; i < colors.Length; i++ ) colors[i] = new( (byte)values[i], 255, 0, 0 );
				node["blendTransitionAndPath"] = colors;
				node.Remove( "blendTransition" );
				node.Remove( "blendMaxPathOpacity" );
			}
			if ( schema <= 25 && node.ClassName is ("CMapEntity" or "CMapWorld" or "CMapPrefab") &&
				node.GetValueOrDefault( "connectionsData" ) is Element[] connections )
			{
				foreach ( var connection in connections )
					if ( connection.GetValueOrDefault( "targetType" ) is int ) connection["targetType"] = 7;
			}
			if ( node.ClassName != "CMapEntity" || node.GetValueOrDefault( "entity_properties" ) is not Element keys ) continue;
			if ( schema <= 13 ) keys.Remove( "spawnFlags" );
			switch ( keys.GetValueOrDefault( "classname" ) )
			{
				case "light_environment" or "light_spot" or "light_ortho" or "light_omni" when schema <= 23:
					bool omni = keys.Get<string>( "classname" ) == "light_omni";
					bool hasFog = keys.GetValueOrDefault( "fog_lighting" ) is int;
					if ( omni && hasFog ) keys["fog_lighting"] = 0;
					else if ( node.GetValueOrDefault( "volumefogenabled" ) is bool fog )
						keys["fog_lighting"] = !omni && !hasFog && fog && node.GetValueOrDefault( "castshadows" ) is true ? 1 : 0;
					keys.Remove( "volumefogenabled" );
					break;
				case "env_cubemap_box" when schema <= 19:
					if ( keys.Remove( "boxproject_mins", out var mins ) ) keys["box_mins"] = mins;
					if ( keys.Remove( "boxproject_maxs", out var maxs ) ) keys["box_maxs"] = maxs;
					break;
				case "info_particle_system" when schema <= 24 && keys.GetValueOrDefault( "snapshot_file" ) is string { Length: > 0 } path:
					keys["snapshot_file"] = Path.ChangeExtension( path, "vsnap" );
					break;
				case "env_global_light":
					CopyIfMissing( "ambientcolor2", "ambientcolor3" );
					break;
				case "ent_dota_lightinfo":
					CopyIfMissing( "shadow_color_day", "shadow_secondary_color_day" );
					CopyIfMissing( "shadow_color_night", "shadow_secondary_color_night" );
					break;
			}
			void CopyIfMissing( string source, string destination )
			{
				if ( keys.GetValueOrDefault( source ) is string { Length: > 0 } value ) keys.TryAdd( destination, value );
			}
		}
		return elements;
	}

	static bool DisallowsGridOverride( Element node ) => node.ClassName == "CMapWorld" && node.GetValueOrDefault( "disallowOverride" ) is true;

	// Version 27 stored a map-wide restriction on any world reachable from the root.
	static void UpgradeGridOverrides( Element root )
	{
		var reachable = new HashSet<Element>();
		var pending = new Stack<Element>();
		pending.Push( root );
		while ( pending.TryPop( out var node ) )
		{
			if ( node is null || !reachable.Add( node ) ) continue;
			foreach ( var value in node.Values )
			{
				if ( value is Element child ) pending.Push( child );
				else if ( value is Element[] children )
					foreach ( var item in children ) pending.Push( item );
			}
		}
		if ( !reachable.Any( DisallowsGridOverride ) ) return;
		foreach ( var node in reachable )
			if ( node.ClassName == "CMapDotaTileGrid" ) node["compileAsOverride"] = false;
	}

	static void UpgradeEarlyGeometry( Element node, int schema )
	{
		if ( schema <= 2 && node.ClassName == "MapClass" ) node.ClassName = "MapNode";
		if ( schema <= 3 && node.ClassName.Equals( "DmeMesh", StringComparison.OrdinalIgnoreCase ) &&
			node.GetValueOrDefault( "baseStates" ) is Element[] states )
		{
			var bind = Array.Find( states, x => x?.Name == "bind" );
			if ( bind?.ContainsKey( "position$0" ) == true && bind.ContainsKey( "position$0Indices" ) )
				WeldLegacyPositions( bind );
		}
		if ( schema <= 5 && node.ClassName == "CMapSolid" && node.GetValueOrDefault( "mesh" ) is Element { ClassName: "CHammerMesh" } hammer &&
			hammer.GetValueOrDefault( "dmemesh" ) is Element mesh )
		{
			node.ClassName = "CMapMesh";
			node["dmemesh"] = mesh;
			node.Remove( "mesh" );
			if ( mesh.GetValueOrDefault( "baseStates" ) is Element[] bases )
				foreach ( var state in bases ) state?.Remove( "flipVCoordinates" );
		}

		if ( node.ClassName == "CDmePolygonMeshDataStream" && node.GetValueOrDefault( "semanticName" ) as string == "VertexPaintBlendParams" )
			ShiftBlends( node, "data" );
		else if ( node.ClassName == "CDmExtraVertexStream" && node.GetValueOrDefault( "m_pVertexData" ) is Element data )
			ShiftBlends( data, "VertexPaintBlendParams" );

		static void ShiftBlends( Element stream, string key )
		{
			if ( !stream.ContainsKey( key ) ) return;
			var values = stream.Numbers<N.Vector4>( key ).ToArray();
			for ( int i = 0; i < values.Length; i++ ) values[i] = new( values[i].Y, values[i].Z, values[i].W, 0 );
			stream[key] = values;
		}
	}

	static void WeldLegacyPositions( Element bind )
	{
		// Hammer keeps the earliest distinct position within this squared distance.
		// Search neighboring cells to retain that ordering without scanning the entire mesh.
		const float cellSize = 0.032f;
		var points = bind.Numbers<N.Vector3>( "position$0" );
		var remap = new int[points.Length];
		var welded = new List<N.Vector3>( points.Length );
		var cells = new Dictionary<(int X, int Y, int Z), List<int>>();
		for ( int i = 0; i < points.Length; i++ )
		{
			var p = points[i];
			int x = (int)MathF.Floor( p.X / cellSize ), y = (int)MathF.Floor( p.Y / cellSize ), z = (int)MathF.Floor( p.Z / cellSize );
			int match = int.MaxValue;
			for ( int dx = -1; dx <= 1; dx++ )
				for ( int dy = -1; dy <= 1; dy++ )
					for ( int dz = -1; dz <= 1; dz++ )
						if ( cells.TryGetValue( (x + dx, y + dy, z + dz), out var nearby ) )
							foreach ( int index in nearby )
								if ( index < match && N.Vector3.DistanceSquared( p, welded[index] ) < 0.001f ) match = index;
			if ( match == int.MaxValue )
			{
				match = welded.Count;
				welded.Add( p );
				if ( !cells.TryGetValue( (x, y, z), out var cell ) ) cells[(x, y, z)] = cell = [];
				cell.Add( match );
			}
			remap[i] = match;
		}
		var indices = bind.Numbers<int>( "position$0Indices" ).ToArray();
		for ( int i = 0; i < indices.Length; i++ ) indices[i] = remap[indices[i]];
		bind["position$0"] = welded.ToArray();
		bind["position$0Indices"] = indices;
	}

	static void UpgradeLegacyNodeName( Element node )
	{
		if ( node.Remove( "id", out var id ) && id is int ) node["nodeID"] = id;
		if ( node.Name == "unnamed" ) node.Name = "";
		else if ( node.ClassName == "EditGameClassProps" && !string.IsNullOrEmpty( node.Name ) )
		{
			node["targetname"] = node.Name;
			node.Name = "";
		}
	}

	static void UpgradeLegacyEntity( Element node, int schema )
	{
		if ( node.GetValueOrDefault( "entity_properties" ) is not Element keys ) return;
		if ( schema <= 9 && node.ClassName is ("CMapEntity" or "CMapWorld" or "CMapPrefab") )
		{
			if ( keys.Remove( "connections", out var connections ) ) node["connectionsData"] = connections;
			if ( node.ClassName == "CMapPrefab" )
			{
				var names = node.Get<string[]>( "variableOverrideKeys", [] ).Where( name => !string.IsNullOrEmpty( name ) ).ToArray();
				node["variableOverrideNames"] = names;
				node["variableOverrideValues"] = names.Select( name => keys.Get( name, "" ) ).ToArray();
				if ( keys.GetValueOrDefault( "targetname" ) is string target ) node["targetName"] = target;
				node.Remove( "variableOverrideKeys" );
				node.Remove( "entity_properties" );
				return;
			}
		}
		if ( node.ClassName != "CMapEntity" ) return;
		var classname = keys.Get( "classname", "" );
		if ( schema <= 10 )
		{
			classname = classname switch { "light" => "light_omni", "light_sun" => "light_directional", _ => classname };
			keys["classname"] = classname;
			if ( keys.GetValueOrDefault( "bouncescale" ) is string bounce )
			{
				var parts = bounce.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
				keys["bouncescale"] = parts.Length >= 4 && int.TryParse( parts[3], out int scale )
					? (scale / 255f).ToString( "F6", CultureInfo.InvariantCulture ) : "1.0";
			}
		}
		if ( schema <= 11 && classname == "env_sky" && keys.GetValueOrDefault( "skyname" ) is string sky )
			keys["skyname"] = Path.ChangeExtension( sky, "vmat" );
		if ( classname is ("light_omni" or "light_spot" or "light_directional") && keys.GetValueOrDefault( "_light" ) is string light )
		{
			var parts = light.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
			Span<int> rgba = stackalloc int[] { 0, 0, 0, 255 };
			for ( int i = 0; i < Math.Min( rgba.Length, parts.Length ); i++ )
			{
				if ( !int.TryParse( parts[i], out int value ) ) break;
				rgba[i] = value;
			}
			keys["color"] = $"{rgba[0]} {rgba[1]} {rgba[2]}";
			keys["brightness"] = (rgba[3] / 255f).ToString( "F6", CultureInfo.InvariantCulture );
		}
	}

	static void UpgradeLegacyMeshAndTileData( Element node, int schema )
	{
		if ( node.ClassName == "CDmePolygonMeshDataStream" && node.GetValueOrDefault( "semanticName" ) is string semantic &&
			semantic.Equals( "VertexPaintTintColor", StringComparison.OrdinalIgnoreCase ) && node.ContainsKey( "data" ) )
		{
			var colors = node.Numbers<N.Vector4>( "data" ).ToArray();
			for ( int i = 0; i < colors.Length; i++ )
			{
				var c = colors[i];
				colors[i] = new( Gamma( c.X ), Gamma( c.Y ), Gamma( c.Z ), c.W );
			}
			node["data"] = colors;
		}
		else if ( node.ClassName == "CDmeDotaTileGrid" )
		{
			if ( string.Equals( node.Name, "baseLayer", StringComparison.OrdinalIgnoreCase ) ) node.Name = "defaultLayer";
			if ( node.ContainsKey( "blendColor" ) )
			{
				var colors = node.Numbers<Color32>( "blendColor" ).ToArray();
				for ( int i = 0; i < colors.Length; i++ )
				{
					var c = colors[i];
					colors[i] = new( GammaByte( c.r ), GammaByte( c.g ), GammaByte( c.b ), c.a );
				}
				node["blendColor"] = colors;
			}
			if ( schema <= 14 && node.ContainsKey( "cellsNodeID" ) )
			{
				var ids = node.Numbers<int>( "cellsNodeID" );
				var configuration = new int[checked(ids.Length * 3)];
				for ( int i = 0; i < ids.Length; i++ )
				{
					configuration[3 * i] = 2;
					configuration[3 * i + 1] = ids[i];
					configuration[3 * i + 2] = -1;
				}
				node["cellConfiguration"] = configuration;
				node.Remove( "cellsNodeID" );
			}
		}
		else if ( schema <= 15 && node.ClassName is ("CMapGroupProxy" or "CMapTileProxy") )
		{
			string key = node.ClassName == "CMapGroupProxy" ? "targetGroup" : "targetTileSet";
			node.Remove( key, out var value );
			node.TryAdd( key + "NodeID", (value as Element)?.GetValueOrDefault( "nodeID" ) ?? -1 );
		}

		static float Gamma( float value )
		{
			value = Math.Clamp( value, 0f, 1f );
			return value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow( value, 1f / 2.4f ) - 0.055f;
		}
		static byte GammaByte( byte value ) => (byte)(Gamma( value / 255f ) * 255f);
	}

	static void RemoveLegacyProxies( Element node )
	{
		foreach ( var (key, value) in node )
		{
			if ( value is Element { ClassName: "CMapProxyInstance" } ) node.Remove( key );
			else if ( value is Element[] refs && Array.Exists( refs, e => e?.ClassName == "CMapProxyInstance" ) )
				node[key] = Array.FindAll( refs, e => e?.ClassName != "CMapProxyInstance" );
		}
	}

	static void UpgradeWorldLights( Element world, int schema )
	{
		Element sun = null, sky = null;
		List<Element> dotaLights = null;
		string specularAngles = "64 283 0";
		var visited = new HashSet<Element>();
		void Visit( Element node )
		{
			if ( !visited.Add( node ) ) return;
			if ( node.GetValueOrDefault( "entity_properties" ) is Element keys )
			{
				switch ( keys.GetValueOrDefault( "classname" ) )
				{
					case "light_directional": sun = keys; break;
					case "light_sky": sky = keys; break;
					case "env_global_light" when schema <= 18: specularAngles = keys.GetValueOrDefault( "specularangles" ) as string ?? "64 283 0"; break;
					case "ent_dota_lightinfo" when schema <= 18: (dotaLights ??= []).Add( keys ); break;
				}
			}
			if ( node.GetValueOrDefault( "children" ) is Element[] children )
				foreach ( var child in children ) if ( child is not null ) Visit( child );
		}
		Visit( world );
		if ( dotaLights is not null )
			foreach ( var info in dotaLights )
			{
				info.TryAdd( "specular_direction_day", specularAngles );
				info.TryAdd( "specular_direction_night", specularAngles );
			}
		var light = sun ?? sky;
		if ( light is null ) return;
		light["classname"] = "light_environment";
		if ( sun is null ) light["color"] = "0 0 0";
		if ( sky is null ) return;
		light["skycolor"] = sky.GetValueOrDefault( "color", "" );
		light["skyintensity"] = sky.GetValueOrDefault( "intensity", "" );
		light["lower_hemisphere_is_black"] = sky.GetValueOrDefault( "lower_hemisphere_is_black", "" );
	}
}
