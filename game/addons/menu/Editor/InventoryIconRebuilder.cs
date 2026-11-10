using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.Services;

/// <summary>
/// Rebuilds local and workshop inventory thumbnails with the shared Human clothing preview.
/// </summary>
public static class InventoryIconRebuilder
{
	const string OutputDirectory = @"C:\temp\sbox-workshop-icon";
	static bool isRunning;

	/// <summary>
	/// Exports local clothing with Steam item definition IDs and downloads workshop inventory clothing.
	/// </summary>
	[Menu( "Editor", "Menu Project/Rebuild Inventory Icons" )]
	public static void RebuildInventoryIcons()
	{
		if ( isRunning )
			return;

		_ = RebuildAsync();
	}

	static async Task RebuildAsync()
	{
		isRunning = true;
		int exported = 0;
		int skipped = 0;
		int failed = 0;

		try
		{
			// Include hidden and unowned workshop items, then prefer local clothing for matching IDs.
			var catalog = Inventory.Definitions.Where( x => !string.IsNullOrWhiteSpace( x.PackageIdent ) )
				.ToDictionary( x => x.Id, x => (x.Name, Clothing: (Clothing)null, Workshop: x) );
			foreach ( var asset in AssetSystem.All.Where( x => x.AssetType.FileExtension == "clothing" && !x.IsCloud && !x.IsTransient ).OrderBy( x => x.Path ).ToArray() )
			{
				try
				{
					var clothing = asset.LoadResource<Clothing>();
					if ( clothing?.SteamItemDefinitionId is not int id || id <= 0 ) continue;

					catalog[id] = (clothing.Title, clothing, null);
				}
				catch ( Exception e )
				{
					failed++;
					Log.Warning( $"Inventory icon discovery failed for {asset.Path}: {e.Message}" );
				}
			}

			var items = catalog.OrderBy( x => x.Key ).ToArray();
			if ( items.Length == 0 )
			{
				Log.Warning( "No local clothing with Steam item definition IDs or workshop inventory definitions are available." );
				return;
			}

			using var progress = Editor.Application.Editor.ProgressSection();
			progress.TotalCount = items.Length;
			var token = progress.GetCancel();

			for ( int i = 0; i < items.Length; i++ )
			{
				token.ThrowIfCancellationRequested();
				var id = items[i].Key;
				var item = items[i].Value;
				progress.Title = $"{id}: {item.Name}";
				progress.Current = i;

				try
				{
					var success = item.Clothing is not null
						? ExportIcon( id, item.Clothing, token )
						: await ExportIconAsync( item.Workshop, token, fraction => progress.Title = $"{id}: {item.Name} — downloading {fraction:P0}" );

					if ( success )
					{
						exported++;
					}
					else
					{
						skipped++;
					}
				}
				catch ( OperationCanceledException ) when ( token.IsCancellationRequested )
				{
					throw;
				}
				catch ( Exception e )
				{
					failed++;
					Log.Warning( $"Inventory icon {id} ({item.Name}) failed: {e.Message}" );
				}

				progress.Current = i + 1;
				await Task.Delay( 1, token );
			}

			Log.Info( $"Inventory icons: exported {exported}, skipped {skipped}, failed {failed}. Output: {OutputDirectory}" );
		}
		catch ( OperationCanceledException )
		{
			Log.Info( $"Inventory icon rebuild cancelled: exported {exported}, skipped {skipped}, failed {failed}." );
		}
		catch ( Exception e )
		{
			Log.Error( $"Inventory icon rebuild failed: {e.Message}" );
		}
		finally
		{
			isRunning = false;
		}
	}

	/// <summary>
	/// Installs one workshop item and writes its thumbnail without modifying the clothing resource or its published icon path.
	/// Returns false when the package is not clothing or has no Human representation.
	/// </summary>
	internal static async Task<bool> ExportIconAsync( Inventory.ItemDefinition item, CancellationToken token, Action<float> downloadProgress = null )
	{
		token.ThrowIfCancellationRequested();
		var package = await Package.FetchAsync( item.PackageIdent, partial: false, useCache: false )
			?? throw new InvalidOperationException( $"Package '{item.PackageIdent}' was not found." );
		token.ThrowIfCancellationRequested();

		if ( package.TypeName != "clothing" )
		{
			Log.Warning( $"Skipping inventory icon {item.Id}: package '{item.PackageIdent}' is not clothing." );
			return false;
		}

		var asset = await AssetSystem.InstallAsync( package, skipIfInstalled: false, loading: downloadProgress, token: token )
			?? throw new InvalidOperationException( "The workshop package could not be installed." );
		token.ThrowIfCancellationRequested();
		var clothing = asset.LoadResource<Clothing>()
			?? throw new InvalidOperationException( "The workshop package has no clothing resource." );

		return ExportIcon( item.Id, clothing, token );
	}

	/// <summary>
	/// Renders local or downloaded clothing to the item definition's upload filename.
	/// </summary>
	internal static bool ExportIcon( int itemDefId, Clothing clothing, CancellationToken token )
	{
		token.ThrowIfCancellationRequested();
		var preview = new Editor.ClothingScene();
		try
		{
			preview.Update();
			preview.InstallClothing( clothing );
			if ( !preview.HasRenderableClothing )
			{
				Log.Warning( $"Skipping inventory icon {itemDefId} ({clothing.Title}): no Human model is available." );
				return false;
			}

			using var bitmap = Editor.ClothingIconRenderer.Render( preview );
			token.ThrowIfCancellationRequested();
			Directory.CreateDirectory( OutputDirectory );
			File.WriteAllBytes( Path.Combine( OutputDirectory, $"{itemDefId}.png" ), bitmap.ToPng() );
			return true;
		}
		finally
		{
			preview.Scene.Destroy();
		}
	}
}
