using Sandbox.Navigation.Pathfinding;
using Sandbox.Compression;
using Sandbox.Navigation.Generation;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Sandbox.Navigation;

internal class NavMeshTile : IDisposable
{
	public Vector2Int TilePosition;

	// Payload ([uncompressedLen:4][lz4]); a tile-owned buffer grown to a high-water mark and reused in
	// place for live tiles (so per-frame regeneration stays alloc-free), or the exact array when baked.
	private readonly object heightfieldGate = new();
	private byte[] _compressedHeightField;
	private int _compressedHeightFieldLength;
	private bool _compressedHeightFieldOwned;

	private HashSet<NavMeshSpatialAuxiliaryData> _spatialData = new();

	public bool IsHeightFieldValid => _compressedHeightFieldLength > 0;

	/// <summary>
	/// True if the cached heightfield originates from baked data rather than live geometry.
	/// </summary>
	public bool IsBakedHeightField { get; private set; }

	// Honour the length, not the array size: the buffer may be grown past the current payload.
	public byte[] CopyCompressedHeightField()
	{
		lock ( heightfieldGate ) return _compressedHeightField.AsSpan( 0, _compressedHeightFieldLength ).ToArray();
	}

	private void ClearCompressedHeightField()
	{
		_compressedHeightField = null;
		_compressedHeightFieldLength = 0;
		_compressedHeightFieldOwned = false;
	}

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
		lock ( heightfieldGate )
		{
			ClearCompressedHeightField();
		}
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
		lock ( heightfieldGate )
		{
			if ( chf == null )
			{
				ClearCompressedHeightField();
				return;
			}

			// Safe to reuse the buffer in place: the cache state machine never overlaps a write with a read for the same tile.
			Compress( chf );
			IsBakedHeightField = false;
		}
	}

	internal void SetCompressedHeightField( byte[] compressedData )
	{
		lock ( heightfieldGate )
		{
			_compressedHeightField = compressedData;
			_compressedHeightFieldLength = compressedData?.Length ?? 0;
			_compressedHeightFieldOwned = false;
			IsBakedHeightField = true;
		}
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
			SetCachedHeightField( null );
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
					navMesh.LoadTileOnMainThread( this, null );
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

	public MeshData BuildNavmesh( CompactHeightfield heightField, Config generatorConfig, NavMesh navMesh )
	{
		if ( heightField == null )
		{
			return null;
		}

		var navMeshGenerator = NavMesh.NavMeshGeneratorPool.Get();
		try
		{
			navMeshGenerator.Init( generatorConfig, heightField );
			var linkVertices = navMeshGenerator.LinkVertices;
			var linkRadii = navMeshGenerator.LinkRadii;
			var linkAreas = navMeshGenerator.LinkAreas;
			var linkBidirectional = navMeshGenerator.LinkBidirectional;
			var linkUserData = navMeshGenerator.LinkUserData;

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

			using var pmesh = navMeshGenerator.Generate();
			if ( pmesh == null )
			{
				return null;
			}

			MeshBuildParameters createParams = new MeshBuildParameters();

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

			var result = MeshBuilder.CreateNavMeshData( createParams );

			return result;
		}
		finally
		{
			navMeshGenerator.LinkUserData.Clear();
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

	private void UpdateLinkData( MeshTile tile, OffMeshConnection offMeshConnection, NavMeshLinkData linkData )
	{
		var conPoly = tile.data.polys[offMeshConnection.poly];

		linkData.IsStartConnected = false;
		for ( int k = conPoly.firstLink; k != MeshConstants.NULL_LINK; k = tile.links[k].next )
		{
			if ( tile.links[k].edge == 0 )
				linkData.IsStartConnected = true;
		}
		linkData.IsEndConnected = false;
		for ( int k = conPoly.firstLink; k != MeshConstants.NULL_LINK; k = tile.links[k].next )
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

	private void Compress( CompactHeightfield chf )
	{
		using var tileStream = ByteStream.Create( MaxTileByteSize );
		tileStream.Write( chf );

		var data = tileStream.ToSpan();

		using var compressed = new PooledSpan<byte>( LZ4.MaxCompressedSize( data.Length ) );
		var compressedLength = LZ4.CompressBlock( data, compressed.Span, System.IO.Compression.CompressionLevel.Fastest );

		var payloadLength = sizeof( int ) + compressedLength;

		// Grow the tile-owned buffer only when the payload no longer fits; otherwise reuse it in place.
		if ( !_compressedHeightFieldOwned || _compressedHeightField == null || _compressedHeightField.Length < payloadLength )
		{
			_compressedHeightField = GC.AllocateUninitializedArray<byte>( payloadLength );
			_compressedHeightFieldOwned = true;
		}

		BinaryPrimitives.WriteInt32LittleEndian( _compressedHeightField.AsSpan( 0, sizeof( int ) ), data.Length );
		compressed.Span.Slice( 0, compressedLength ).CopyTo( _compressedHeightField.AsSpan( sizeof( int ) ) );

		// Publish length last so a reader never sees a torn payload.
		_compressedHeightFieldLength = payloadLength;
	}

	public CompactHeightfield DecompressCachedHeightField()
	{
		lock ( heightfieldGate )
		{
			if ( _compressedHeightFieldLength == 0 )
			{
				return null;
			}

			var payload = _compressedHeightField.AsSpan( 0, _compressedHeightFieldLength );
			var expectedLength = BinaryPrimitives.ReadInt32LittleEndian( payload.Slice( 0, sizeof( int ) ) );

			if ( expectedLength == 0 )
			{
				return null;
			}

			var compressedBuffer = payload.Slice( sizeof( int ) );
			using var decompressedBuffer = new PooledSpan<byte>( expectedLength );

			LZ4.DecompressBlock( compressedBuffer, decompressedBuffer.Span );

			var byteStream = ByteStream.CreateReader( decompressedBuffer.Span );

			var cf = CompactHeightfield.Read( ref byteStream );

			byteStream.Dispose();

			return cf;
		}
	}
}
