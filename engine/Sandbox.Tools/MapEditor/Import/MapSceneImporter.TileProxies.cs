using System;
using System.IO;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	sealed partial class VmapReader
	{
		readonly Dictionary<SourceNode, SourceNode> proxyGroups = new();
		readonly Dictionary<SourceNode, TileChoice[]> proxyChoices = new();
		readonly Dictionary<SourceNode, (int Wet, int Dry)> proxyWater = new();
		// Hammer's instance set stores up to 32 instance/proxy pairs per path.
		const int MaxProxyDepth = 32;
		readonly Dictionary<SourceNode, (SourceNode Proxy, Transform Local, int[] Path)[]> proxySites = new();

		void BindTileProxies( List<SourceNode> proxies, Dictionary<Element, SourceNode> nodes )
		{
			var byReference = new Dictionary<ulong, SourceNode>();
			var byId = new Dictionary<int, SourceNode>();
			foreach ( var (element, node) in nodes )
			{
				if ( element.ClassName is not ("CMapGroup" or "CMapWeightedGroup" or "CMapTile" or "CMapTileSet" or "CMapTileMeshGroup" or "CMapWorldLayer" or "CMapSpawnGroup") ) continue;
				var reference = element.Get( "referenceID", 0ul );
				if ( reference != 0 ) byReference.TryAdd( reference, node );
				if ( node.NodeId > 0 ) byId.TryAdd( node.NodeId, node );
			}
			foreach ( var proxy in proxies )
			{
				var element = tileData[proxy];
				if ( proxy.TypeString == "CMapTileProxy" )
				{
					var set = byId.GetValueOrDefault( element.Get( "targetTileSetNodeID", -1 ) );
					proxyGroups[proxy] = set?.TypeString == "CMapTileSet" ? set : null;
					continue;
				}
				var reference = element.Get( "targetGroupReferenceID", 0ul );
				proxyGroups[proxy] = reference != 0 ? byReference.GetValueOrDefault( reference ) : byId.GetValueOrDefault( element.Get( "targetGroupNodeID", -1 ) );
			}
		}

		(SourceNode Proxy, Transform Local, int[] Path)[] GetProxySites( SourceNode root )
		{
			if ( proxySites.TryGetValue( root, out var sites ) ) return sites;
			var found = new List<(SourceNode, Transform, int[])>();
			Visit( root, root, Transform.Zero, stackalloc int[MaxProxyDepth], 0 );
			return proxySites[root] = found.ToArray();

			void Visit( SourceNode node, SourceNode anchor, Transform placement, Span<int> path, int depth )
			{
				if ( depth >= MaxProxyDepth ) return;
				path[depth] = node.NodeId;
				if ( node.TypeString is "CMapGroupProxy" or "CMapTileProxy" )
					found.Add( (node, placement.ToWorld( anchor.Transform.ToLocal( node.Transform ) ), path[..(depth + 1)].ToArray()) );
				else if ( node.Kind == NodeKind.Instance && node.Target is { } target )
					Visit( target, target, placement.ToWorld( anchor.Transform.ToLocal( node.Transform ) ), path, depth + 1 );
				else
					foreach ( var child in node.Children ) Visit( child, anchor, placement, path, depth );
			}
		}

		TileChoice[] GetProxyChoices( SourceNode proxy )
		{
			if ( proxyChoices.TryGetValue( proxy, out var choices ) ) return choices;
			var group = proxyGroups.GetValueOrDefault( proxy );
			if ( group is null ) return proxyChoices[proxy] = [];

			var candidates = new List<TileChoice>( group.Children.Length );
			float total = 0;
			foreach ( var target in group.Children )
			{
				float weight;
				if ( proxy.TypeString == "CMapTileProxy" )
				{
					if ( target.TypeString != "CMapTile" ) continue;
					int matches = MatchingProxyRotations( tileData[group], tileData[proxy], tileData[target] );
					if ( matches == 0 ) continue;
					weight = (matches == 4 ? 1 : matches) * tileData[target].Get( "probability", 1f );
				}
				else
				{
					if ( ContainsProxy( target ) ) continue;
					weight = target.TypeString == "CMapWeightedGroup" ? tileData[target].Get( "probability", 1f ) : 1f;
				}
				total += weight;
				candidates.Add( new( target, -1, total ) );
			}
			return proxyChoices[proxy] = candidates.ToArray();

			bool ContainsProxy( SourceNode node ) => node == proxy || node.Children.Any( ContainsProxy );
		}

		static int MatchingProxyRotations( Element set, Element proxy, Element tile )
		{
			int rotations = 15;
			foreach ( var property in set.Get<Element[]>( "properties", [] ) )
			{
				if ( (property.Get( "targetTypeFlags", 1 ) & 1) == 0 ) continue;
				int components = property.Get( "componentTypeFlags", 1 );
				int requested = TilePropertyIndex( proxy, property.Name ), actual = TilePropertyIndex( tile, property.Name );
				string fallback = property.Get<string[]>( "values", [] ).FirstOrDefault() ?? "";
				for ( int r = 0; r < 4; r++ )
				{
					if ( (rotations & (1 << r)) == 0 ) continue;
					bool match = (components & 1) == 0 || Matches( "propertyValues", "propertyValues" );
					for ( int i = 0; match && i < 4; i++ )
					{
						int local = (i - r) & 3;
						match = ((components & 2) == 0 || Matches( CornerKeys[local], CornerKeys[i] )) &&
							((components & 4) == 0 || Matches( EdgeKeys[local], EdgeKeys[i] ));
					}
					if ( !match ) rotations &= ~(1 << r);
				}
				if ( rotations == 0 ) return 0;

				bool Matches( string tileKey, string proxyKey ) => TileValueMatches(
					actual < 0 ? fallback : tile.Get<string[]>( tileKey, [] ).ElementAtOrDefault( actual ),
					requested < 0 ? fallback : proxy.Get<string[]>( proxyKey, [] ).ElementAtOrDefault( requested ) );
			}
			return System.Numerics.BitOperations.PopCount( (uint)rotations );
		}

		bool ProxyFitsWater( SourceNode proxy, int localWater )
		{
			var data = tileData[proxy];
			if ( !data.Get( "conditional", false ) ) return true;
			if ( !proxyWater.TryGetValue( proxy, out var required ) )
			{
				var names = data.Get<string[]>( "tile requirements_propertyNames", [] );
				int index = Array.FindIndex( names, n => string.Equals( n, "water", StringComparison.OrdinalIgnoreCase ) );
				var property = ownerSet.GetValueOrDefault( proxy )?.Get<Element[]>( "properties", [] )
					.FirstOrDefault( p => string.Equals( p.Name, "water", StringComparison.OrdinalIgnoreCase ) );
				var fallback = property?.Get<string[]>( "values", [] ).FirstOrDefault();
				for ( int i = 0; i < 4; i++ )
				{
					var value = index < 0 ? fallback : data.Get<string[]>( $"tile requirements_{CornerKeys[i]}", [] ).ElementAtOrDefault( index );
					if ( (index < 0 && property is null) || !TileValueMatches( value, "no" ) ) required.Wet |= 1 << i;
					if ( (index < 0 && property is null) || !TileValueMatches( value, "yes" ) ) required.Dry |= 1 << i;
				}
				proxyWater[proxy] = required;
			}
			return (localWater & required.Wet) == required.Wet && (localWater & required.Dry) == 0;
		}

		SourceNode[] PickTileProxies( SourceNode tile, Transform placement, int? localWater, bool? openGround, ReadOnlySpan<int> records, ReadOnlySpan<int> names, Func<SourceNode, Transform, SourceNode> place )
		{
			if ( GetProxySites( tile ).Length == 0 ) return [];
			var placements = new List<SourceNode>();
			Visit( tile, placement, false, records, names, stackalloc int[MaxProxyDepth], stackalloc int[MaxProxyDepth], 0 );
			return placements.ToArray();

			void Visit( SourceNode source, Transform frame, bool orientUp, ReadOnlySpan<int> saved, ReadOnlySpan<int> named, Span<int> path, Span<int> namePath, int depth )
			{
				if ( depth == MaxProxyDepth ) return;
				foreach ( var (proxy, local, suffix) in GetProxySites( source ) )
				{
					int length = depth + suffix.Length;
					if ( length > MaxProxyDepth || (localWater.HasValue && !ProxyFitsWater( proxy, localWater.Value )) ||
						(openGround.HasValue && !ProxyFitsOpenGround( proxy, openGround.Value )) ) continue;
					suffix.CopyTo( path[depth..] );
					int id = SavedProxyTarget( saved, path[..length] );
					// Only paths composed entirely of named proxies have a saved name.
					var childNames = suffix.Length == 1 ? named : default;
					uint targetName = 0;
					if ( !childNames.IsEmpty )
					{
						namePath[depth] = (int)new StringToken( tileData[proxy].Get( "proxyName", "" ) ).Value;
						if ( namePath[depth] == 0 ) childNames = default;
						else targetName = SavedProxyName( named, namePath[..length] );
					}
					var target = PickProxyTarget( proxy, id, targetName );
					if ( target is null ) continue;
					var transform = frame.ToWorld( local );
					bool up = orientUp || proxy.ProxyOrientUp;
					placements.Add( place( target, up ? transform.WithRotation( Rotation.FromYaw( transform.Rotation.Yaw() ) ) : transform ) );
					Visit( target, transform, up, saved, childNames, path, namePath, length );
				}
			}
		}

		SourceNode PickProxyTarget( SourceNode proxy, int id, uint name )
		{
			var data = tileData[proxy];
			if ( (name == 0 ? id == 0 : name == new StringToken( "EmptyProxyTarget" ).Value) && data.Get( "removable", true ) )
				return null;

			var choices = GetProxyChoices( proxy );
			if ( choices.Length == 0 ) return null;
			if ( name != 0 )
				foreach ( var choice in choices )
					if ( new StringToken( choice.Target.Name ).Value == name ) return choice.Target;
			if ( id > 0 )
				foreach ( var choice in choices )
					if ( choice.Target.NodeId == id ) return choice.Target;

			float probability = data.Get( "probability", 1f );
			return probability <= 0 || (probability < 1 && Random.Shared.NextSingle() > probability) ? null : PickTileChoice( choices ).Target;
		}

		static uint SavedProxyName( ReadOnlySpan<int> records, ReadOnlySpan<int> path )
		{
			int count = records[0];
			records = records[1..];
			for ( int i = 0; i < count; i++ )
			{
				if ( records.Length < 3 || records[0] <= 0 || records[0] > records.Length - 2 )
					throw new InvalidDataException( "Invalid named tile proxy path." );
				int length = records[0];
				if ( records.Slice( 2, length ).SequenceEqual( path ) ) return (uint)records[1];
				records = records[(2 + length)..];
			}
			return 0;
		}

		static int SavedProxyTarget( ReadOnlySpan<int> records, ReadOnlySpan<int> path )
		{
			while ( !records.IsEmpty )
			{
				int end = records.IndexOf( -1 );
				if ( end < 1 ) throw new InvalidDataException( "Invalid saved tile proxy path." );
				if ( records[1..end].SequenceEqual( path ) ) return records[0];
				records = records[(end + 1)..];
			}
			return -1;
		}
	}
}
