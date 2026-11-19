using Sandbox.Engine;
using System.Threading;
using ClothingCategory = Sandbox.Clothing.ClothingCategory;
using static Sandbox.ClothingContainer;

namespace Sandbox;

/// <summary>
/// Creates clothing renderers for the outfit owned by this Dresser.
/// </summary>
public sealed partial class Dresser
{
	/// <summary>
	/// Dresses a skinned model with an outfit. Will apply all the clothes it can immediately, then download any missing clothing.
	/// </summary>
	private async Task ApplyClothingAsync( ClothingContainer outfit, SkinnedModelRenderer body, CancellationToken token )
	{
		token.ThrowIfCancellationRequested();

		if ( !body.IsValid() )
			return;

		bool isMenu = GlobalContext.Current == GlobalContext.Menu;

		var scene = body.Scene;

		// apply any changes that we can, immediately
		ApplyClothing( outfit, body );

		bool hasChanges = false;

		//
		// Find any clothing that needs downloading
		// Download it, and apply it to the container.
		//
		foreach ( var item in outfit.Clothing.Where( x => x.Clothing == null || string.IsNullOrEmpty( x.Clothing.ResourcePath ) ).ToArray() )
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
				token.ThrowIfCancellationRequested();

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

		token.ThrowIfCancellationRequested();

		if ( !body.IsValid() )
			return;

		using ( scene.Push() )
		{
			//
			// If we have any changes, then re-apply all the clothing to the container
			// so that things get removed if they don't work with other items (but in the right order).
			// Then apply them to the target renderer.
			//
			if ( hasChanges )
			{
				foreach ( var entry in outfit.Clothing.ToArray() )
				{
					outfit.Add( entry );
				}

				ApplyClothing( outfit, body );
			}
		}
	}

	/// <summary>
	/// Dress a skinned model renderer with an outfit. Doesn't download missing clothing.
	/// </summary>
	private void ApplyClothing( ClothingContainer outfit, SkinnedModelRenderer body )
	{
		using var SceneScope = body.Scene.Push();

		outfit.Normalize();
		ApplyBodyReplacement( outfit, body );

		// Remove the previous outfit without resetting the body's baseline.
		RemoveClothing( body );
		_clothingRenderers.Clear();

		bool acceptsClothing = AllowedSlots != Sandbox.Clothing.Slots.None;
		TagSet tags = new();
		tags.Add( _activeBody?.BodyTags );

		if ( acceptsClothing )
			tags.Add( "human" );

		var set = outfit.Clothing?
			.Where( x => IsValidClothing( x, tags ) )
			.Where( x => !x.Clothing.ReplacesBody || x.Clothing == _activeBody )
			.ToList() ?? new();

		// Body materials are shared with compatible clothing. Material-only clothing can override them.
#pragma warning disable CS0618 // Existing clothing resources can still supply material overrides.
		Material skinMaterial = set.Select( x => x.Clothing.HumanSkinMaterial ).Concat( set.Select( x => x.Clothing.SkinMaterial ) )
			.Where( x => !string.IsNullOrWhiteSpace( x ) ).Select( Material.Load ).FirstOrDefault();
		Material eyesMaterial = set.Select( x => x.Clothing.HumanEyesMaterial ).Concat( set.Select( x => x.Clothing.EyesMaterial ) )
			.Where( x => !string.IsNullOrWhiteSpace( x ) ).Select( Material.Load ).FirstOrDefault();
#pragma warning restore CS0618

#pragma warning disable CS0618
		if ( SupportsHumanAppearance || _hasSkinAttributes )
		{
			var heelHeight = set.Select( x => x.Clothing.HeelHeight ).DefaultIfEmpty( 0 ).Max();
			if ( body.Animation.Graph is not null )
			{
				body.Animation.Parameters.Set( "scale_heel", heelHeight, persist: true );
			}
			else
			{
				body.Set( "scale_heel", heelHeight );
			}
		}
#pragma warning restore CS0618

		if ( skinMaterial is not null )
			body.SetMaterialOverride( skinMaterial, "skin" );

		if ( eyesMaterial is not null )
			body.SetMaterialOverride( eyesMaterial, "eyes" );

		// Garments can contain exposed skin meshes. Match those to the body's selected material group,
		// without replacing the body's own group materials or overriding explicit legacy material choices.
		if ( acceptsClothing && body.Model is { MaterialGroupCount: > 0 } )
		{
			var group = Math.Max( 0, body.Model.GetMaterialGroupIndex( body.MaterialGroup ) );
			foreach ( var material in body.Model.GetMaterials( group ) )
			{
				if ( material.Flags.GetInt( "skin" ) != 0 )
					skinMaterial ??= material;

				if ( material.Flags.GetInt( "eyes" ) != 0 )
					eyesMaterial ??= material;
			}
		}

		if ( acceptsClothing )
		{
			EnsureHumanUnderwear( set, tags );
		}

		//
		// Create clothes models
		//
		foreach ( var entry in set )
		{
			var c = entry.Clothing;

			if ( c.ReplacesBody )
				continue;

			var modelPath = c.GetModel( set.Select( x => x.Clothing ).Except( new[] { c } ), tags );

#pragma warning disable CS0618 // Legacy skin items replace materials instead of adding a garment.
			if ( string.IsNullOrEmpty( modelPath ) || !string.IsNullOrEmpty( c.SkinMaterial ) )
				continue;
#pragma warning restore CS0618

			var model = Model.Load( modelPath );
			if ( !model.IsValid() || model.IsError )
				continue;

			var go = new GameObject( false, $"Clothing - {c.ResourceName}" );
			go.Parent = body.GameObject;
			go.Tags.Add( "clothing" );
			// Accessories can use their own bones or be weighted below the neck, so collapsing the
			// body's upper bones isn't enough. Hide the whole accessory in the first-person preview.
			if ( IsFirstPersonHiddenClothing( c.Category ) )
				go.Tags.Add( "firstperson-hidden" );

			var r = go.Components.Create<SkinnedModelRenderer>();
			r.Model = model;
			r.BoneMergeTarget = body;
			r.DeformationMode = c.DeformationMode;
			_clothingRenderers[entry] = r;

			if ( skinMaterial is not null )
				r.SetMaterialOverride( skinMaterial, "skin" );

			if ( eyesMaterial is not null )
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
		foreach ( var (name, value) in outfit.GetBodyGroups( set.Where( x => !x.Clothing.ReplacesBody ).Select( x => x.Clothing ), body.Model ) )
		{
			if ( value == 0 ) continue;

			body.SetBodyGroup( name, value );
		}

		UpdateAppearance();
	}

	static bool IsFirstPersonHiddenClothing( ClothingCategory category ) => category is
		ClothingCategory.Hat or ClothingCategory.HatBeanie or ClothingCategory.HatFormal or
		ClothingCategory.HatCostume or ClothingCategory.HatUniform or ClothingCategory.HatSpecial or
		ClothingCategory.Headwear or ClothingCategory.HeadTech or ClothingCategory.HeadBand or
		ClothingCategory.HeadJewel or ClothingCategory.HeadSpecial or
		ClothingCategory.Hair or ClothingCategory.HairShort or ClothingCategory.HairMedium or
		ClothingCategory.HairLong or ClothingCategory.HairUpdo or ClothingCategory.HairSpecial or
		ClothingCategory.Facial or ClothingCategory.FacialHairMustache or ClothingCategory.FacialHairBeard or
		ClothingCategory.FacialHairStubble or ClothingCategory.FacialHairSideburns or ClothingCategory.FacialHairGoatee or
		ClothingCategory.Eyewear or ClothingCategory.GlassesEye or ClothingCategory.GlassesSun or ClothingCategory.GlassesSpecial or
		ClothingCategory.Eyes or ClothingCategory.Eyebrows or ClothingCategory.Eyelashes or
		ClothingCategory.NecklaceChain or ClothingCategory.NecklacePendant or ClothingCategory.NecklaceSpecial or
		ClothingCategory.EarringStud or ClothingCategory.EarringDangle or ClothingCategory.EarringSpecial or
		ClothingCategory.Piercing or ClothingCategory.PierceNose or ClothingCategory.PierceEyebrow or ClothingCategory.PierceSpecial;

	/// <summary>
	/// Cancels any existing Dresser request and resets clothing without initializing appearance.
	/// </summary>
	internal static void Reset( SkinnedModelRenderer body )
	{
		var dresser = Find( body );
		if ( dresser.IsValid() )
		{
			dresser.CancelDressing();
			dresser._clothingRenderers.Clear();
		}

		ResetClothing( body );
	}

	/// <summary>
	/// Removes clothing and restores the body's height, material group and bodygroups.
	/// </summary>
	private static void ResetClothing( SkinnedModelRenderer body )
	{
		//
		// Start with defaults
		//
#pragma warning disable CS0618
		if ( body.Animation.Graph is not null )
			body.Animation.Parameters.Set( "scale_height", 1.0f, persist: true );
		else
			body.Set( "scale_height", 1.0f );
#pragma warning restore CS0618
		body.MaterialGroup = "default";
		body.ClearMaterialOverrides();
		body.BodyGroups = body.Model?.Parts.DefaultMask ?? 0;

		RemoveClothing( body );
	}

	private static void RemoveClothing( SkinnedModelRenderer body )
	{
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

	// Default underwear paths, cached to avoid repeated allocations
	const string DefaultUnderwearPath = "models/citizen_clothes/underwear/y_front_pants/y_front_pants_white.clothing";
	const string DefaultBraPath = "models/citizen_clothes/underwear/bra/bra_white.clothing";
	static readonly Lazy<Sandbox.Clothing> DefaultUnderwear = new( () => ResourceLibrary.Get<Sandbox.Clothing>( DefaultUnderwearPath ) );
	static readonly Lazy<Sandbox.Clothing> DefaultBra = new( () => ResourceLibrary.Get<Sandbox.Clothing>( DefaultBraPath ) );

	static bool IsValidModel( string modelName )
	{
		if ( string.IsNullOrWhiteSpace( modelName ) )
			return false;

		var model = Model.Load( modelName );

		if ( !model.IsValid() ) return false;
		if ( model.IsError ) return false;

		return true;
	}

	void EnsureHumanUnderwear( List<ClothingEntry> set, TagSet tags )
	{
		var hiddenParts = set.Select( x => x.Clothing.HideBody ).DefaultIfEmpty().Aggregate( ( a, b ) => a | b );

		// Don't add underwear if legs are hidden
		if ( !hiddenParts.HasFlag( Sandbox.Clothing.BodyGroups.Legs ) )
		{
			bool hasUnderwear = set.Any( x => x.Clothing.Category is Sandbox.Clothing.ClothingCategory.Underwear or Sandbox.Clothing.ClothingCategory.Underpants );
			if ( !hasUnderwear )
				TryAddDefault( set, DefaultUnderwear.Value, tags );
		}

		// Don't add bra if chest is hidden
		if ( tags.Has( "female" ) && !hiddenParts.HasFlag( Sandbox.Clothing.BodyGroups.Chest ) && !set.Any( x => x.Clothing.Category == Sandbox.Clothing.ClothingCategory.Bra ) )
			TryAddDefault( set, DefaultBra.Value, tags );
	}

	void TryAddDefault( List<ClothingEntry> set, Sandbox.Clothing clothing, TagSet tags )
	{
		if ( clothing is null ) return;

		var entry = new ClothingEntry( clothing );

		if ( !IsValidClothing( entry, tags ) ) return;
		if ( set.Any( x => !(x.Clothing?.CanBeWornWith( clothing ) ?? true) ) ) return;

		set.Add( entry );
	}

	bool IsValidClothing( ClothingEntry entry, TagSet tags )
	{
		var clothing = entry?.Clothing;
		if ( clothing is null )
			return false;

		if ( clothing.ReplacesBody )
			return IsValidBodyReplacement( clothing );

		if ( AllowedSlots == Sandbox.Clothing.Slots.None || (_activeBody is not null && !_activeBody.CanWear( clothing )) )
			return false;

		var model = clothing.GetModel( Enumerable.Empty<Sandbox.Clothing>(), tags );
		if ( string.IsNullOrEmpty( model ) )
		{
			// An item with a mesh for a different body is not a material-only item.
#pragma warning disable CS0618 // Retain the distinction for old Citizen resources.
			return string.IsNullOrEmpty( clothing.Model )
				&& string.IsNullOrEmpty( clothing.HumanAltModel )
				&& string.IsNullOrEmpty( clothing.HumanAltFemaleModel );
#pragma warning restore CS0618
		}

		if ( !IsValidModel( model ) )
		{
			Log.Warning( $"Clothing model '{model}' in {clothing} is invalid, removing" );
			return false;
		}

		return true;
	}
}
