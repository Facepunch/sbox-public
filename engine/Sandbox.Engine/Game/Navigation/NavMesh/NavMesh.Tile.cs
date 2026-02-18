using DotRecast.Detour;
using Sandbox.Compression;
using Sandbox.Navigation.Generation;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Sandbox.Navigation;

internal class NavMeshTile : IDisposable
{
	public Vector2Int TilePosition;

	private byte[] _compressedHeightField;

	private HashSet<NavMeshSpatialAuxiliaryData> _spatialData = new();

	public bool IsHeightFieldValid => _compressedHeightField != null;

	public byte[] CompressedHeightField => _compressedHeightField;

	public void HeightfieldBuildComplete()
	{
		IsHeightfieldBuildInProgress = false;
	}

	public void NavmeshBuildComplete()
	{
		IsNavmeshBuildInProgress = false;
	}

	public void RequestFullRebuild()
	{
		IsFullRebuildRequested = true;
	}

	public void RequestNavmeshBuild()
	{
		IsNavmeshBuildRequested = true;
	}

	public void Dispose()
	{
		_compressedHeightField = null;
	}

	public bool IsNavmeshBuildRequested { get; private set; } = false;
	public bool IsNavmeshBuildInProgress { get; private set; } = false;
	public bool IsFullRebuildRequested { get; private set; } = false;
	public bool IsHeightfieldBuildInProgress { get; private set; } = false;

	public void AddSpatialData( NavMeshSpatialAuxiliaryData area )
	{
		lock ( _spatialData )
		{
			_spatialData.Add( area );
		}
		RequestNavmeshBuild();
	}

	public void RemoveSpatialData( NavMeshSpatialAuxiliaryData area )
	{
		lock ( _spatialData )
		{
			_spatialData.Remove( area );
		}
		RequestNavmeshBuild();
	}

	const int MaxTileByteSize = 96 * 1024 + 8;

	public void SetCachedHeightField( CompactHeightfield chf )
	{
		if ( chf == null )
		{
			_compressedHeightField = null;
			return;
		}

		// Atomic swap: compress first, then assign in one step to avoid
		// a window where concurrent readers see null
		var compressed = Compress( chf );
		_compressedHeightField = compressed;
	}

	internal void SetCompressedHeightField( byte[] compressedData )
	{
		_compressedHeightField = compressedData;
	}

	public void DispatchNavmeshBuild( NavMesh navMesh )
	{
		var generatorConfig = navMesh.CreateTileGenerationConfig( TilePosition );

		IsNavmeshBuildInProgress = true;
		IsNavmeshBuildRequested = false;

		Task.Run( () =>
		{
			using var chf = DecompressCachedHeightField();
			var navMeshData = BuildNavmesh( chf, generatorConfig, navMesh );

			MainThread.Queue( () =>
			{
				navMesh.LoadTileOnMainThread( this, navMeshData );

				NavmeshBuildComplete();
			} );
		} );

	}

	public bool DispatchHeightFieldBuild( NavMesh navMesh, PhysicsWorld physicsWorld )
	{
		ThreadSafe.AssertIsMainThread();

		var generatorConfig = navMesh.CreateTileGenerationConfig( TilePosition );

		IsHeightfieldBuildInProgress = true;
		IsFullRebuildRequested = false;

		var heightFieldGenerator = NavMesh.HeightFieldGeneratorPool.Get();
		heightFieldGenerator.Init( generatorConfig );
		heightFieldGenerator.CollectGeometry( navMesh, physicsWorld, generatorConfig.Bounds );

		if ( heightFieldGenerator.IsEmpty )
		{
			NavMesh.HeightFieldGeneratorPool.Return( heightFieldGenerator );
			navMesh.UnloadTileOnMainThread( TilePosition );
			IsNavmeshBuildRequested = false;
			HeightfieldBuildComplete();
			return false;
		}

		Task.Run( () =>
		{
			CompactHeightfield heightFieldData = null;
			try
			{
				heightFieldData = heightFieldGenerator.Generate();
				SetCachedHeightField( heightFieldData );
			}
			finally
			{
				NavMesh.HeightFieldGeneratorPool.Return( heightFieldGenerator );
			}

			var hasHeightField = heightFieldData != null;
			heightFieldData?.Dispose();

			MainThread.Queue( () =>
			{
				// received nothing -> tile is empty
				if ( !hasHeightField )
				{
					IsNavmeshBuildRequested = false;
					navMesh.UnloadTileOnMainThread( TilePosition );
				}
				else
				{
					IsNavmeshBuildRequested = true;
				}

				HeightfieldBuildComplete();
			} );
		} );

		return true;
	}

	List<Vector3> linkVertices = new();
	List<float> linkRadii = new();
	List<int> linkFlags = new();
	List<int> linkAreas = new();
	List<bool> linkBidirectional = new();
	List<object> linkUserData = new();

	public DtMeshData BuildNavmesh( CompactHeightfield heightField, Config generatorConfig, NavMesh navMesh )
	{
		if ( heightField == null )
		{
			return null;
		}

		var navMeshGenerator = NavMesh.NavMeshGeneratorPool.Get();
		navMeshGenerator.Init( generatorConfig, heightField );

		linkVertices.Clear();
		linkRadii.Clear();
		linkFlags.Clear();
		linkAreas.Clear();
		linkBidirectional.Clear();
		linkUserData.Clear();

		lock ( _spatialData )
		{
			foreach ( var data in _spatialData )
			{
				var areaId = navMesh.AreaDefinitionToId( data.AreaDefinition );
				areaId = data.IsBlocked ? Constants.NULL_AREA : areaId;
				switch ( data )
				{
					case NavMeshAreaData area:
						navMeshGenerator.MarkArea( area, areaId );
						break;
					case NavMeshLinkData link:
						linkVertices.Add( NavMesh.ToNav( link.StartPosition ) );
						linkVertices.Add( NavMesh.ToNav( link.EndPosition ) );
						linkRadii.Add( link.ConnectionRadius );
						linkAreas.Add( areaId );
						linkBidirectional.Add( link.IsBiDirectional );
						linkUserData.Add( link.UserData );
						break;
					default:
						throw new NotImplementedException();
				}
			}
		}

		try // We need to make sure the generator is returned to the pool
		{
			using var pmesh = navMeshGenerator.Generate();
			if ( pmesh == null )
			{
				return null;
			}

			DtNavMeshCreateParams createParams = new DtNavMeshCreateParams();

			createParams.pmesh = pmesh;

			createParams.walkableHeight = generatorConfig.WalkableHeight;
			createParams.walkableRadius = generatorConfig.WalkableRadius;
			createParams.walkableClimb = generatorConfig.WalkableClimb;
			createParams.bmin = pmesh.BMin;
			createParams.bmax = pmesh.BMax;
			createParams.cs = generatorConfig.CellSize;
			createParams.ch = generatorConfig.CellHeight;
			createParams.buildBvTree = true;
			createParams.tileLayer = 0;
			createParams.tileX = generatorConfig.TileX;
			createParams.tileZ = generatorConfig.TileY;

			createParams.offMeshConVerts = CollectionsMarshal.AsSpan( linkVertices );
			createParams.offMeshConRad = CollectionsMarshal.AsSpan( linkRadii );
			createParams.offMeshConAreas = CollectionsMarshal.AsSpan( linkAreas );
			createParams.offMeshConBidirectional = CollectionsMarshal.AsSpan( linkBidirectional );
			createParams.offMeshConUserData = CollectionsMarshal.AsSpan( linkUserData );
			createParams.offMeshConCount = linkVertices.Count / 2;

			var result = DtNavMeshBuilder.CreateNavMeshData( createParams );

			return result;
		}
		finally
		{
			NavMesh.NavMeshGeneratorPool.Return( navMeshGenerator );
		}
	}

	public void UpdateLinkStatus( NavMesh navmesh )
	{
		var tileRef = navmesh.navmeshInternal.GetTileRefAt( TilePosition.x, TilePosition.y, 0 );
		if ( tileRef == default )
		{
			return;
		}

		var tile = navmesh.navmeshInternal.GetTileByRef( tileRef );

		lock ( _spatialData )
		{
			foreach ( var data in _spatialData )
			{
				if ( data is not NavMeshLinkData linkData )
				{
					continue;
				}

				foreach ( var offMeshConnection in tile.data.offMeshCons )
				{
					if ( offMeshConnection == null )
					{
						continue;
					}

					if ( offMeshConnection.userData == linkData.UserData )
					{
						UpdateLinkData( tile, offMeshConnection, linkData );
					}
				}
			}
		}
	}

	private void UpdateLinkData( DtMeshTile tile, DtOffMeshConnection offMeshConnection, NavMeshLinkData linkData )
	{
		var conPoly = tile.data.polys[offMeshConnection.poly];

		linkData.IsStartConnected = false;
		for ( int k = conPoly.firstLink; k != DtDetour.DT_NULL_LINK; k = tile.links[k].next )
		{
			if ( tile.links[k].edge == 0 )
				linkData.IsStartConnected = true;
		}
		linkData.IsEndConnected = false;
		for ( int k = conPoly.firstLink; k != DtDetour.DT_NULL_LINK; k = tile.links[k].next )
		{
			if ( tile.links[k].edge == 1 )
				linkData.IsEndConnected = true;
		}

		if ( linkData.IsStartConnected )
		{
			linkData.StartPositionOnNavMesh = NavMesh.FromNav( tile.data.verts[conPoly.verts[0]] );
		}

		if ( linkData.IsEndConnected )
		{
			linkData.EndPositionOnNavMesh = NavMesh.FromNav( tile.data.verts[conPoly.verts[1]] );
		}
	}

	private byte[] Compress( CompactHeightfield chf )
	{
		using var tileStream = ByteStream.Create( MaxTileByteSize );
		tileStream.Write( chf );

		var data = tileStream.ToSpan();

		var compressed = LZ4.CompressBlock( data, System.IO.Compression.CompressionLevel.Fastest );
		var payload = new byte[sizeof( int ) + compressed.Length];
		BinaryPrimitives.WriteInt32LittleEndian( payload.AsSpan( 0, sizeof( int ) ), data.Length );
		compressed.CopyTo( payload.AsSpan( sizeof( int ) ) );
		return payload;
	}

	public CompactHeightfield DecompressCachedHeightField()
	{
		if ( _compressedHeightField == null )
		{
			return null;
		}

		var expectedLength = BinaryPrimitives.ReadInt32LittleEndian( _compressedHeightField.AsSpan().Slice( 0, sizeof( int ) ) );

		if ( expectedLength == 0 )
		{
			return null;
		}

		var compressedBuffer = _compressedHeightField.AsSpan().Slice( sizeof( int ) );
		using var decompressedBuffer = new PooledSpan<byte>( expectedLength );

		LZ4.DecompressBlock( compressedBuffer, decompressedBuffer.Span );

		var byteStream = ByteStream.CreateReader( decompressedBuffer.Span );

		var cf = CompactHeightfield.Read( ref byteStream );

		byteStream.Dispose();

		return cf;
	}
}
