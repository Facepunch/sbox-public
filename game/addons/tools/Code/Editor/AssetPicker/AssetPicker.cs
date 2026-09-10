using Editor.AssetPickers;

namespace Editor;

public abstract class AssetPicker : Dialog
{
	public struct PickerOptions
	{
		/// <summary>
		/// Allows searching and selecting cloud assets
		/// </summary>
		public bool EnableCloud { get; set; } = true;

		/// <summary>
		/// Allows searching and selecting assets from mounted games
		/// </summary>
		public bool EnableMounts { get; set; } = true;

		/// <summary>
		/// Cloud assets will be shown in a separate tab
		/// </summary>
		public bool SeparateCloudTab { get; set; } = true;

		/// <summary>
		/// Allows selecting multiple assets
		/// </summary>
		public bool EnableMultiselect { get; set; } = false;

		/// <summary>
		/// Pass-thru of additioanl types requested from native that aren't the primary resource type
		/// </summary>
		public List<AssetType> AdditionalTypes { get; internal set; }

		public PickerOptions() { }
	}

	/// <summary>
	/// Asset was highlighted, but not picked.
	/// </summary>
	public Action<Asset[]> OnAssetHighlighted { get; set; }

	/// <summary>
	/// An asset was picked. The asset picker will be closed after this.
	/// </summary>
	public Action<Asset[]> OnAssetPicked { get; set; }

	/// <summary>
	/// A package was picked. The asset picker will be closed after this.
	/// </summary>
	public Action<Package> OnPackagePicked { get; set; }

	public AssetType AssetType { get; init; }

	public PickerOptions Options { get; init; }

	public string Title { set => Window.Title = value; }

	public AssetPicker( Widget parent, AssetType assetType, PickerOptions options ) : base( parent, true )
	{
		AssetType = assetType;
		Options = options;

		Window.SetWindowIcon( "find_in_page" );

		Window.WindowFlags = WindowFlags.Dialog | WindowFlags.Customized | WindowFlags.WindowTitle | WindowFlags.CloseButton | WindowFlags.WindowSystemMenuHint;

		Window.SetModal( true );
	}

	public override void Show()
	{
		Window.Show();
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		EditorUtility.StopAssetSound();
	}

	public void SetSelection( string resource )
	{
		Asset asset = AssetSystem.FindByPath( resource );
		if ( asset is null && Package.TryGetCached( resource, out Package package ) )
		{
			SetSelection( package );
			return;
		}

		SetSelection( asset );
	}

	protected virtual bool ShouldFilterAsset( Asset asset )
	{
		//
		// Filter out unwanted crap
		//

		if ( asset.Name.Contains( ".generated", StringComparison.OrdinalIgnoreCase ) )
		{
			return true;
		}

		if ( asset.AbsolutePath.Contains( ".sbox" ) )
		{
			return true;
		}

		return false;
	}

	protected void Submit( Asset asset ) => Submit( [asset] );

	protected virtual void Submit( Asset[] assets )
	{
		OnAssetPicked?.Invoke( assets );
		Window.Close();
	}

	protected virtual async void Submit( Package package )
	{
		package = await Package.FetchAsync( package.FullIdent, false );

		OnPackagePicked?.Invoke( package );

		if ( AssetSystem.CanCloudInstall( package ) )
		{
			Asset a = null;
			if ( !AssetSystem.IsCloudInstalled( package ) )
			{
				Hide();
				a = await AssetSystem.InstallAsync( package.FullIdent );
				if ( a is null )
				{
					Log.Error( $"Failed to install package: {package.FullIdent}" );
				}
			}
			else
			{
				string assetPath = package.GetMeta<string>( "PrimaryAsset" );
				a = AssetSystem.FindByPath( assetPath );

				if ( a is null )
				{
					Log.Error( $"Failed to find installed package: {package.FullIdent}" );
				}
			}

			Submit( a );
		}
		else
		{
			Hide();
		}
	}


	public virtual void SetSelection( Asset asset ) { }
	public virtual void SetSelection( Package package ) { }

	public virtual void SetSearchText( string value ) { }

	// --

	public static AssetPicker Create( Widget parent, AssetType assetType, PickerOptions? options = null )
	{
		options ??= new();
		Type resourceType = assetType?.ResourceType;
		if ( resourceType is null && assetType is not null )
		{
			switch ( assetType.FileExtension )
			{
				case "vmap": resourceType = typeof( Map ); break;
				case "vmat": resourceType = typeof( Material ); break;
				case "vmdl": resourceType = typeof( Model ); break;
				case "vsnd": resourceType = typeof( SoundFile ); break;
				case "sound": resourceType = typeof( SoundEvent ); break;
			}
		}
		if ( resourceType is not null )
		{
			var matchingTypes = EditorTypeLibrary
				.GetTypesWithAttribute<AssetPickerAttribute>()
				.Where( x => typeof( AssetPicker ).IsAssignableFrom( x.Type.TargetType ) )
				.SelectMany( x => x.Attribute.ResourceTypes
					.Where( assetType => resourceType.IsAssignableTo( assetType ) )
					.Select( assetType => new { ParentType = x.Type, AssetType = assetType } ) )
				.ToList();

			var customType = matchingTypes
				.OrderBy( x => GetSpecificityScore( x.AssetType, resourceType ) )
				.Select( x => x.ParentType )
				.FirstOrDefault();

			if ( customType != null )
			{
				try
				{
					var picker = customType.Create<AssetPicker>( [parent, assetType, options] );
					if ( picker.IsValid() )
						return picker;
				}
				catch ( Exception ex )
				{
					Log.Error( ex, $"Failed to create AssetPicker for: {assetType} ({customType.Name})" );
				}
			}
		}
		else
		{
			//
			// No resource type, so all types are allowed
			//
			return new GenericPicker( parent, null, options.Value );
		}

		var types = new List<AssetType>() { assetType };

		if ( assetType.IsGameResource )
		{
			// also all types that inherit from the resource type
			types.AddRange( EditorTypeLibrary.GetTypes( assetType.ResourceType ).Select( t => AssetType.FromType( t.TargetType ) ) );
		}

		if ( options?.AdditionalTypes != null )
		{
			types.AddRange( options?.AdditionalTypes );
		}

		return new GenericPicker( parent, types.Distinct().ToList(), options.Value );
	}

	// helper to weight the implementations by inheritance depth
	private static int GetSpecificityScore( Type candidate, Type target )
	{
		int score = 0;
		while ( target != null && target != typeof( object ) )
		{
			if ( target == candidate )
				return score;
			target = target.BaseType;
			score++;
		}
		return int.MaxValue;
	}
}
