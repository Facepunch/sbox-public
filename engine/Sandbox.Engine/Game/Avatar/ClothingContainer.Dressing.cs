using Sandbox.Engine;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Holds a collection of clothing items. Won't let you add items that aren't compatible.
/// </summary>
public partial class ClothingContainer
{
	/// <summary>
	/// Dresses a skinned model with an outfit. Will apply all the clothes it can immediately, then download any missing clothing.
	/// </summary>
	public async Task ApplyAsync( SkinnedModelRenderer body, CancellationToken token )
	{
		if ( !body.IsValid() )
			return;

		bool isMenu = GlobalContext.Current == GlobalContext.Menu;

		var scene = body.Scene;

		// apply any changes that we can, immediately
		Apply( body );

		bool hasChanges = false;

		//
		// Find any clothing that needs downloading
		// Download it, and apply it to the container.
		//
		foreach ( var item in Clothing.Where( x => x.Clothing == null || string.IsNullOrEmpty( x.Clothing.ResourcePath ) ).ToArray() )
		{
			if ( item.ItemDefinitionId == 0 ) continue;
			var def = Sandbox.Services.Inventory.FindDefinition( item.ItemDefinitionId );
			if ( def == null )
			{
				Log.Warning( $"FindDefinition null : {item.ItemDefinitionId}" );
				continue;
			}

			Sandbox.Clothing clothing = default;


			//
			// If we're in the menu we can't just use Cloud.Load because the package and resource will be loaded
			// in the GAME resource system instead of the menu resource system.
			//
			if ( isMenu )
			{
				clothing = GlobalContext.Menu.ResourceSystem.Get<Clothing>( def.Asset );

				if ( clothing != null )
				{
					item.Clothing = clothing;
					hasChanges = true;
					continue;
				}

				var o = new PackageLoadOptions
				{
					PackageIdent = def.PackageIdent,
					ContextTag = "menu",
					CancellationToken = token
				};

				var activePackage = await PackageManager.InstallAsync( o );
				if ( activePackage == null )
				{
					Log.Warning( $"Error installing clothing package {def.PackageIdent}" );
					continue;
				}

				var primaryasset = activePackage.Package.PrimaryAsset;

				GlobalContext.Menu.FileMount.Mount( activePackage.FileSystem );

				clothing = GlobalContext.Menu.ResourceSystem.LoadGameResource<Clothing>( primaryasset, activePackage.FileSystem );

				// these should match - else we wno't be able to find them later
				if ( primaryasset != def.Asset )
				{
					Log.Warning( $"Clothing primary assets don't match for {def.PackageIdent} ({primaryasset} vs {def.Asset})" );
				}
			}
			else
			{
				// Cloud.Load is always going to load them in the global context, so we need to switch to that context here
				clothing = GlobalContext.Game.ResourceSystem.Get<Clothing>( def.Asset );

				if ( clothing != null )
				{
					item.Clothing = clothing;
					hasChanges = true;
					continue;
				}

				clothing = await Cloud.Load<Clothing>( def.PackageIdent );
			}

			if ( clothing is null )
			{
				Log.Warning( $"Clothing from package was null: {def.PackageIdent}" );
				continue;
			}


			token.ThrowIfCancellationRequested();

			if ( !body.IsValid() )
				return;

			if ( clothing != null )
			{
				item.Clothing = clothing;
				hasChanges = true;
			}
		}

		using ( scene.Push() )
		{
			//
			// If we have any changes, then re-apply all the clothing to the container
			// so that things get removed if they don't work with other items (but in the right order).
			// Then apply them to the target renderer.
			//
			if ( hasChanges )
			{
				foreach ( var entry in Clothing.ToArray() )
				{
					Add( entry );
				}

				Apply( body );
			}
		}
	}

	/// <summary>
	/// Dress a skinned model renderer with an outfit. Doesn't download missing clothing.
	/// </summary>
	public void Apply( SkinnedModelRenderer body )
	{
		bool isHuman = DetermineHuman( body );

		// remove out outfit
		Reset( body );
		Normalize();

		// apply indicentals
		body.Set( "scale_height", Height.Remap( 0, 1, 0.8f, 1.2f, true ) );
		body.Set( "scale_heel", Clothing?.Select( x => x.Clothing?.HeelHeight ?? 0.0f ).DefaultIfEmpty( 0 ).Max() ?? 0.0f );

		// TODO - we should expose the render attributes, somehow, in a way accessible to editor inspector and serialization!
		body.Attributes.Set( "skin_age", Age );
		body.Attributes.Set( "skin_tint", Tint );

		// Clean the clothing. Remove any invalid items, any items with broken models
		// any items that can't be worn with other items.
		List<ClothingContainer.ClothingEntry> set = Clothing?
														.Where( x => IsValidClothing( x, isHuman ) )
														.ToList() ?? new();

		TagSet tags = new();

		Material skinMaterial = default;
		Material eyesMaterial = default;

		//
		// apply alternate human skin, if we have one
		//
		if ( isHuman )
		{
			skinMaterial = set.Select( x => x.Clothing.HumanSkinMaterial ).Where( x => !string.IsNullOrWhiteSpace( x ) ).Select( x => Material.Load( x ) ).FirstOrDefault();
			eyesMaterial = set.Select( x => x.Clothing.HumanEyesMaterial ).Where( x => !string.IsNullOrWhiteSpace( x ) ).Select( x => Material.Load( x ) ).FirstOrDefault();

			tags.Add( "human" );

			var humanskin = set.Where( x => x.Clothing.HasHumanSkin ).FirstOrDefault();
			if ( humanskin is not null && Model.Load( humanskin.Clothing.HumanSkinModel ) is Model model && model.IsValid() )
			{
				body.Model = model;
				tags.Add( humanskin.Clothing.HumanSkinTags );

				body.BodyGroups = humanskin.Clothing.HumanSkinBodyGroups;
				body.MaterialGroup = humanskin.Clothing.HumanSkinMaterialGroup;
			}
			else
			{
				body.BodyGroups = body.Model.Parts.DefaultMask;
				body.MaterialGroup = "default";
			}
		}
		else
		{
			skinMaterial = set.Select( x => x.Clothing.SkinMaterial ).Where( x => !string.IsNullOrWhiteSpace( x ) ).Select( x => Material.Load( x ) ).FirstOrDefault();
			eyesMaterial = set.Select( x => x.Clothing.EyesMaterial ).Where( x => !string.IsNullOrWhiteSpace( x ) ).Select( x => Material.Load( x ) ).FirstOrDefault();
		}

		body.SetMaterialOverride( skinMaterial, "skin" );
		body.SetMaterialOverride( eyesMaterial, "eyes" );

		//
		// Create clothes models
		//
		foreach ( var entry in set )
		{
			var c = entry.Clothing;

			var modelPath = c.GetModel( set.Select( x => x.Clothing ).Except( new[] { c } ), tags );

			if ( string.IsNullOrEmpty( modelPath ) || !string.IsNullOrEmpty( c.SkinMaterial ) )
				continue;

			var model = Model.Load( modelPath );
			if ( !model.IsValid() || model.IsError )
				continue;

			var go = new GameObject( false, $"Clothing - {c.ResourceName}" );
			go.Parent = body.GameObject;
			go.Tags.Add( "clothing" );

			var r = go.Components.Create<SkinnedModelRenderer>();
			r.Model = model;
			r.BoneMergeTarget = body;

			// TODO - we should expose the render attributes, somehow, in a way accessible to editor inspector and serialization!
			r.Attributes.Set( "skin_age", Age );
			r.Attributes.Set( "skin_tint", Tint );

			r.SetMaterialOverride( skinMaterial, "skin" );
			r.SetMaterialOverride( eyesMaterial, "eyes" );

			if ( !string.IsNullOrEmpty( c.MaterialGroup ) )
				r.MaterialGroup = c.MaterialGroup;

			if ( c.AllowTintSelect )
			{
				var tintValue = entry.Tint?.Clamp( 0, 1 ) ?? c.TintDefault;
				var tintColor = c.TintSelection.Evaluate( tintValue );
				r.Tint = tintColor;
			}

			go.Enabled = true;
		}

		//
		// Set body groups
		//
		foreach ( var (name, value) in GetBodyGroups( set.Select( x => x.Clothing ) ) )
		{
			if ( value == 0 ) continue;

			body.SetBodyGroup( name, value );
		}
	}

	/// <summary>
	/// Clear the outfit from this model, make it named
	/// </summary>
	public void Reset( SkinnedModelRenderer body )
	{
		//
		// Start with defaults
		//
		body.Set( "scale_height", 1.0f );
		body.MaterialGroup = "default";
		body.MaterialOverride = null;
		body.BodyGroups = body.Model?.Parts.DefaultMask ?? 0;

		//
		// Remove old models
		//
		foreach ( var children in body.GameObject.Children )
		{
			if ( children.Tags.Has( "clothing" ) )
			{
				children.Destroy();
			}
		}
	}

	static bool DetermineHuman( SkinnedModelRenderer b, bool defaultValue = false )
	{
		if ( b is null ) return defaultValue;
		if ( b.Model is null ) return defaultValue;

		if ( b.Model.Name.Contains( "citizen.vmdl", StringComparison.OrdinalIgnoreCase ) ) return false;
		return true;
	}

	static bool IsValidModel( string modelName )
	{
		if ( string.IsNullOrWhiteSpace( modelName ) )
			return false;

		var model = Model.Load( modelName );

		if ( !model.IsValid() ) return false;
		if ( model.IsError ) return false;

		return true;
	}

	static bool IsValidClothing( ClothingContainer.ClothingEntry e, bool targetIsHuman )
	{
		if ( e is null ) return false;
		if ( e.Clothing is null ) return false;
		if ( targetIsHuman && e.Clothing.HasHumanSkin ) return true;

		var model = e.Clothing.Model;

		if ( targetIsHuman )
		{
			model = e.Clothing.HumanAltModel;

			// If we have a citizen model, but not a human model, make clothing invalid
			if ( string.IsNullOrEmpty( model ) && !string.IsNullOrEmpty( e.Clothing.Model ) )
				return false;
		}

		if ( string.IsNullOrEmpty( model ) )
			return true;

		if ( !IsValidModel( model ) )
		{
			Log.Warning( $"Clothing model '{model}' in {e.Clothing} is invalid, removing" );
			return false;
		}

		return true;
	}
}
