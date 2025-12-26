namespace Editor;

/// <summary>
/// A widget (usually window) implementing this will be able to edit assets via the asset browser.
/// The widget should be marked with the attribute of the asset's extension, like this <c>[CanEdit( "asset:vsndstck" )]</c>
/// </summary>
public interface IAssetEditor : IValid
{
	/// <summary>
	/// A list of open editors that support multiple assets at once.
	/// </summary>
	static Dictionary<string, IAssetEditor> OpenMultiAssetEditors = new();

	/// <summary>
	/// A list of open editors for individual assets.
	/// </summary>
	static Dictionary<string, IAssetEditor> OpenSingleEditors = new();

	/// <summary>
	/// Open given asset in a new asset editor window. Will reuse already open editors for same asset type if the editor supports it. (<see cref="CanOpenMultipleAssets"/>)
	/// </summary>
	/// <returns>Whether an asset editor was found for given asset.</returns>
	public static bool OpenInEditor( Asset asset, out IAssetEditor editor )
	{
		System.ArgumentNullException.ThrowIfNull( asset );
		
		Log.Info($"Open In Editor {asset.GetSourceFile()}");
		//
		// Stick this on the recent opened list
		//
		asset.RecordOpened();

		var path = asset.AbsolutePath ?? asset.Name;

		if ( OpenSingleEditors.TryGetValue( path, out var openEditor ) && openEditor.IsValid )
		{
			Log.Info("Should not focus. ");

			// ReSharper disable once SuspiciousTypeConversion.Global
			if ( openEditor is BaseWindow window )
			{
				window.Focus();
			}

			editor = openEditor;
			return true;
		}

		var extension = asset.AssetType.FileExtension;

		if ( OpenMultiAssetEditors.TryGetValue( extension, out openEditor ) && openEditor.IsValid )
		{
			Log.Info( $"Open In MultiAssetEditor {asset.GetSourceFile()}" );
			openEditor.AssetOpen( asset );

			editor = openEditor;
			return true;
		}

		//
		// This is the new way, anything else should be considered bullshit
		//


		TryAgain:

		if ( TryOpenUsingStaticMethod( asset ) )
		{
			editor = null;
			return true;
		}

		var found = EditorTypeLibrary.GetTypesWithAttribute<EditorForAssetTypeAttribute>()
							.Where( x => x.Attribute.Extension == extension )
							.FirstOrDefault();

		// not found lets create a generic editor
		if ( found.Type == null && extension != "__fallback" && asset.AssetType.IsGameResource )
		{
			extension = "__fallback";
			goto TryAgain;
		}

		// nothing found for real
		if ( found.Type == null )
		{
			editor = null;
			return false;
		}

		var created = found.Type.Create<Widget>();

		Log.Info( $"widgetWindow create {created} {created.GetType().ToString()}" );

		if ( created is not IAssetEditor ae )
		{
			created.Destroy();
			Log.Warning( $"Found editor {created} for {extension} - but it didn't implement IAssetEditor" );

			editor = null;
			return false;
		}

		ae.AssetOpen( asset );

		if ( ae.CanOpenMultipleAssets )
		{
			OpenMultiAssetEditors[extension] = ae;
		}
		else
		{
			OpenSingleEditors[path] = ae;
		}

		editor = ae;
		return true;
	}

	static bool TryOpenUsingStaticMethod( Asset asset )
	{
		var extension = asset.AssetType.FileExtension;

		var found = EditorTypeLibrary.GetMethodsWithAttribute<EditorForAssetTypeAttribute>()
					.Where( x => x.Attribute.Extension == extension )
					.FirstOrDefault();

		if ( found.Method is null )
			return false;

		if ( found.Method.Parameters.Count() == 1 )
		{
			var p = found.Method.Parameters[0];

			// if it wants an asset, pass it in
			if ( p.ParameterType == typeof( Asset ) )
			{
				found.Method.Invoke( null, new[] { asset } );
				return true;
			}

			// if it wants a gameresource type, load it and convert it
			if ( p.ParameterType.IsAssignableTo( typeof( GameResource ) ) )
			{
				var resource = asset.LoadResource( p.ParameterType );
				found.Method.Invoke( null, new[] { resource } );
				return true;
			}
		}

		return false;

	}

	/// <summary>
	/// If this editor is able to edit multiple assets at the same time then return true
	/// and we'll try to create only one version of that editor and AssetOpen will be called multiple times.
	/// </summary>
	public bool CanOpenMultipleAssets { get; }

	/// <summary>
	/// Open the asset in this editor.
	/// </summary>
	public void AssetOpen( Asset asset );

	void SelectMember( string memberName );
}
