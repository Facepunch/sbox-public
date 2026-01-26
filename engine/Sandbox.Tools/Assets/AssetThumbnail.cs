using Native;
using System;

namespace Editor;

static class AssetThumbnail
{
	internal static string GetThumbnailFile( Asset asset, bool createDirectory )
	{
		bool isCloud = asset.AbsolutePath.Contains( ".sbox/cloud/" );
		var cacheFolder = $"/{(isCloud ? "thumbnails/.cloud" : "thumbnails")}/{System.IO.Path.GetDirectoryName( asset.Path )}";
		cacheFolder = cacheFolder.Replace( ":", "" );

		var cacheName = $"{cacheFolder}/{System.IO.Path.GetFileName( asset.Path )}.png";
		var fullPath = FileSystem.ProjectTemporary.GetFullPath( cacheName );

		if ( createDirectory )
			FileSystem.ProjectTemporary.CreateDirectory( cacheFolder );

		return fullPath;
	}

	internal static QPixmap GetAssetThumb( uint assetId )
	{
		var asset = AssetSystem.Get( assetId );
		return GetAssetThumb( asset )?.ptr ?? default;
	}

	internal static Pixmap GetAssetThumb( Asset asset, bool generateIfNotInCache = true )
	{
		ArgumentNullException.ThrowIfNull( asset, nameof( asset ) );

		if ( asset.HasCachedThumbnail )
		{
			return asset.CachedThumbnail;
		}

		var fullPath = GetThumbnailFile( asset, false );
		if ( System.IO.File.Exists( fullPath ) )
		{
			var pix = Pixmap.FromFile( fullPath );
			asset.CachedThumbnail = pix;
			return asset.CachedThumbnail;
		}

		if ( asset.AssetType != null )
		{
			asset.CachedThumbnail = asset.AssetType.Icon256;
		}

		if ( generateIfNotInCache )
		{
			// start an async render
			QueueThumbBuild( asset );
		}

		return asset.CachedThumbnail;
	}

	internal static void RefreshThumbnail( uint assetId )
	{
		var asset = AssetSystem.Get( assetId );
		if ( asset is null ) return;
		RenderQueue.RemoveAll( x => x == asset );
		RenderQueue.Insert( 0, asset );
	}

	static List<Asset> RenderQueue = new();
	static List<Asset> RenderingList = new();

	internal static void DequeueThumbBuild( Asset asset )
	{
		if ( RenderingList.Contains( asset ) )
			return; // too late!

		RenderQueue.RemoveAll( x => x == asset );
	}

	internal static void QueueThumbBuild( Asset asset, bool add = true )
	{
		if ( RenderingList.Contains( asset ) )
			return;

		if ( RenderQueue.RemoveAll( x => x == asset ) == 0 && !add )
			return;

		RenderQueue.Add( asset );
	}

	internal static void Frame()
	{
		for ( int i = 0; i < RenderQueue.Count && RenderingList.Count < 1; i++ )
		{
			var asset = RenderQueue[i];
			RenderingList.Add( asset );
			RenderQueue.RemoveAt( i );

			_ = RenderThumbnailAsync( asset );

			i--;
		}
	}

	static async Task RenderThumbnailAsync( Asset asset )
	{
		//
		// We always yield when calling this, so it'll be called
		// in the next frame, instead of RIGHT NOW. This prevents
		// issues with Qt, where we'd end up with recursive paint
		// errors and crashes.
		//
		await Task.Yield();

		try
		{
			using ( EditorUtility.DisableTextureStreaming() )
			{
				await asset.CacheAsync();
			}

			var pix = await RenderAssetThumb( asset );
			if ( pix != null )
			{
				asset.CachedThumbnail = pix;

				if ( asset is NativeAsset nativeAsset )
				{
					IAssetPreviewSystem.OnThumbnailGenerated( nativeAsset.native, asset.CachedThumbnail.ptr );
				}

				var fullPath = GetThumbnailFile( asset, true );

				await Task.Run( () => pix.SavePng( fullPath ) );

				EditorEvent.RunInterface<AssetSystem.IEventListener>( x => x.OnAssetThumbGenerated( asset ) );
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception when compiling thumbnail for {asset.Path}" );
		}
		finally
		{
			asset.Uncache();
			RenderingList.Remove( asset );
		}
	}

	internal static async Task<Pixmap> RenderAssetThumb( Asset asset )
	{
		ThreadSafe.AssertIsMainThread();

		//
		// If it's a resource, let it generate itself the thumbnail!
		//
		{
			var resource = asset.LoadResource();
			var bitmap = resource?.RenderThumbnail( new() { Width = 256, Height = 256 } );
			if ( bitmap != null )
			{
				return Pixmap.FromBitmap( bitmap );
			}
		}


		if ( asset.thumbnailOverride != null )
		{
			return asset.thumbnailOverride;
		}

		var extension = asset.AssetType.FileExtension;
		var methods = EditorTypeLibrary.GetMethodsWithAttribute<Asset.ThumbnailRendererAttribute>();

		foreach ( var t in methods.OrderByDescending( x => x.Attribute.Priority ) )
		{
			try
			{
				ThreadSafe.AssertIsMainThread();

				var pixmap = t.Method.InvokeWithReturn<Task<Bitmap>>( null, [asset] );
				if ( pixmap is null ) continue;

				ThreadSafe.AssertIsMainThread();

				var r = await pixmap;
				if ( r is null ) continue;

				ThreadSafe.AssertIsMainThread();

				return Pixmap.FromBitmap( r );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Exception when rendering thumb via {t.Method} ({e.Message})" );
			}
		}

		if ( asset.AssetType.IsGameResource )
			return null;

		var pix = new Pixmap( 256, 256 );

		if ( asset is NativeAsset nativeAsset )
		{
			if ( IAssetPreviewSystem.RenderAssetThumbnail( nativeAsset.native, pix.ptr ) )
			{
				return pix;
			}
		}

		return null;
	}
}
