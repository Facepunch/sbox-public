#nullable enable

using System;

namespace Editor
{
	partial class DragData
	{
		private string? _assetsText;
		private DragAssetData[]? _assetsCache;

		/// <summary>
		/// Interprets <see cref="Text"/> as a list of asset paths or cloud asset URLs,
		/// getting a list of helper objects to access each asset. Generated and cached
		/// internally on first access after <see cref="Text"/> changes.
		/// </summary>
		public IReadOnlyList<DragAssetData> Assets
		{
			get
			{
				if ( _assetsText != Text )
				{
					_assetsText = Text;
					_assetsCache = null;
				}

				return _assetsCache ??= (Text ?? string.Empty)
					.Split( '\n' )
					.Select( DragAssetData.Parse )
					.OfType<DragAssetData>()
					.ToArray();
			}
		}
	}

	/// <summary>
	/// Represents an asset being dragged into an editor window. Assets will either
	/// be sourced from a package (see <see cref="PackageIdent"/>) or a local path (see <see cref="AssetPath"/>).
	/// Instances of this type are accessed through <see cref="DragData.Assets"/>.
	/// </summary>
	public class DragAssetData
	{
		/// <summary>
		/// For package assets, the identifier of the source package. Will always be of the form <c>org.package[#version]</c>.
		/// </summary>
		public string? PackageIdent { get; }

		/// <summary>
		/// For local assets, the path to the asset. Equivalent to <see cref="Asset.Path"/>.
		/// </summary>
		public string? AssetPath { get; }

		/// <summary>
		/// For cloud assets, a value between <c>0.0</c> and <c>1.0</c> representing download progress.
		/// Download will only start after the first call to <see cref="GetAssetAsync"/>.
		/// </summary>
		public float DownloadProgress { get; private set; }

		/// <summary>
		/// True when the asset is ready for use locally.
		/// For cloud assets, download will only start after the first call to <see cref="GetAssetAsync"/>.
		/// </summary>
		public bool IsInstalled => _getAssetTask is { IsCompletedSuccessfully: true } && _getAssetTask.Result is not null;

		private Task<Package?>? _getPackageTask;
		private Task<Asset?>? _getAssetTask;

		public event Action<float>? ProgressChanged;

		internal static DragAssetData? Parse( string value )
		{
			if ( AssetSystem.FindByPath( value ) is { } asset )
			{
				return new DragAssetData( asset );
			}

			if ( Package.TryParseIdent( value, out var ident ) )
			{
				return new DragAssetData( Package.FormatIdent(
					ident.org, ident.package, ident.version, ident.local ) );
			}

			return null;
		}

		private DragAssetData( string packageIdent )
		{
			PackageIdent = packageIdent;
		}

		private DragAssetData( Asset asset )
		{
			AssetPath = asset.Path;
			DownloadProgress = 1f;

			_getPackageTask = Task.FromResult<Package?>( null );
			_getAssetTask = Task.FromResult( asset )!;
		}

		/// <summary>
		/// For package assets, completes when the source package information is available.
		/// </summary>
		public Task<Package?> GetPackageAsync() => _getPackageTask ??= GetPackageInternalAsync();

		private Task<Package?> GetPackageInternalAsync()
		{
			return Package.FetchAsync( PackageIdent, false, true );
		}

		/// <summary>
		/// Completes when the asset is ready to use. For cloud assets, the first call to this
		/// will start downloading and installing the source package. This is safe to call
		/// multiple times, the same task will be returned.
		/// Returns null if the package cannot be installed.
		/// </summary>
		public Task<Asset?> GetAssetAsync() => _getAssetTask ??= GetAssetInternalAsync();

		private async Task<Asset?> GetAssetInternalAsync()
		{
			var package = await GetPackageAsync();

			if ( string.IsNullOrEmpty( package?.PrimaryAsset ) )
			{
				return null;
			}

			Asset? asset;
			try
			{
				asset = await AssetSystem.InstallAsync( package, loading: progress =>
				{
					DownloadProgress = progress;
					ProgressChanged?.Invoke( progress );
				} );
			}
			catch ( AggregateException ex )
			{
				Log.Error( ex, $"Failed to install dragged package: {package.FullIdent}" );
				return null;
			}

			DownloadProgress = 1f;
			ProgressChanged?.Invoke( 1f );

			return asset;
		}
	}
}
