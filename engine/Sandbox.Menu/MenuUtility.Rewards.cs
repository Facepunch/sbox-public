using System;

namespace Sandbox;

/// <summary>
/// Item rarity grades, matching the backend's <c>ItemDef.ItemRarity</c> enum.
/// </summary>
public enum ItemRarity : byte
{
	None,
	Common,
	Uncommon,
	Rare,
	Epic,
	Legendary,
	Mythic,
	Exotic
}

public static class ItemRarityExtensions
{
	/// <summary>
	/// The outline/border colour for a rarity grade, matching sbox.web.
	/// Returns null for <see cref="ItemRarity.None"/>.
	/// </summary>
	public static string GetColor( this ItemRarity rarity ) => rarity switch
	{
		ItemRarity.Common => "#b0c3d9",
		ItemRarity.Uncommon => "#5e98d9",
		ItemRarity.Rare => "#4b69ff",
		ItemRarity.Epic => "#8847ff",
		ItemRarity.Legendary => "#d32ce6",
		ItemRarity.Mythic => "#eb4b4b",
		ItemRarity.Exotic => "#e4ae39",
		_ => null,
	};

	/// <summary>
	/// Parse a rarity string (from Steam item properties) into the enum.
	/// </summary>
	public static ItemRarity ParseRarity( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			return ItemRarity.None;

		if ( Enum.TryParse<ItemRarity>( value, ignoreCase: true, out var result ) )
			return result;

		return ItemRarity.None;
	}
}

public static partial class MenuUtility
{
	/// <summary>
	/// Menu-facing access to the player's reward drops. Wraps the backend account API and
	/// maps its <see cref="Sandbox.Services"/> models onto the menu-facing types below, so the
	/// menu addon never has to reference Sandbox.Services directly.
	/// </summary>
	public static class Rewards
	{
		/// <summary>
		/// Get the player's reward state — their windows (requirements + progress) and any
		/// pending unclaimed offer to resume. Poll after a session and after claiming.
		/// </summary>
		public static async Task<RewardState> GetRewards()
		{
			var state = await Backend.Account.GetRewards();
			return new RewardState( state );
		}

		/// <summary>
		/// Open a reward claim: returns the items on offer to choose from (or the existing
		/// unclaimed offer to resume), or null if there's nothing to claim right now.
		/// </summary>
		public static async Task<RewardOffer> ClaimReward()
		{
			var offer = await Backend.Account.ClaimReward();
			if ( offer is null )
				return null;

			return new RewardOffer( offer );
		}

		/// <summary>
		/// Commit the player's pick(s) from an open offer. Grants the chosen item(s) to
		/// their inventory and closes the offer.
		/// </summary>
		public static async Task<RewardResult> ChooseReward( RewardChoice choice )
		{

			var serviceChoice = new Services.RewardChoice();
			serviceChoice.DropId = choice.DropId;
			serviceChoice.ItemDefIds = choice.ItemDefIds;

			var result = await Backend.Account.ChooseReward( serviceChoice );
			return new RewardResult( result );
		}
	}
}

/// <summary>
/// The full reward picture for a player: their windows (requirements + progress) and any
/// pending unclaimed offer they should resume. Menu-facing proxy for the backend's reward state.
/// </summary>
public class RewardState
{
	public RewardWindow[] Windows { get; set; } = Array.Empty<RewardWindow>();
	public RewardOffer Pending { get; set; }

	internal RewardState() { }

	internal RewardState( Services.RewardState state )
	{
		if ( state is null )
			return;

		if ( state.Windows is not null )
		{
			Windows = new RewardWindow[state.Windows.Length];
			for ( int i = 0; i < state.Windows.Length; i++ )
			{
				Windows[i] = new RewardWindow( state.Windows[i] );
			}
		}

		if ( state.Pending is not null )
		{
			Pending = new RewardOffer( state.Pending );
		}
	}
}

/// <summary>
/// A reward track shown to the player: a set of requirements ("facets") with the player's
/// current progress, and whether they can claim a reward right now.
/// </summary>
public class RewardWindow
{
	public string Key { get; set; }
	public string Title { get; set; }
	public bool IsEligible { get; set; }
	public RewardFacet[] Facets { get; set; } = Array.Empty<RewardFacet>();

	internal RewardWindow() { }

	internal RewardWindow( Services.RewardWindow window )
	{
		Key = window.Key;
		Title = window.Title;
		IsEligible = window.IsEligible;

		if ( window.Facets is not null )
		{
			Facets = new RewardFacet[window.Facets.Length];
			for ( int i = 0; i < window.Facets.Length; i++ )
			{
				Facets[i] = new RewardFacet( window.Facets[i] );
			}
		}
	}
}

/// <summary>
/// A single requirement of a <see cref="RewardWindow"/> together with the player's progress
/// against it. Built for display — a checklist row / progress dots.
/// </summary>
public class RewardFacet
{
	public string Key { get; set; }
	public string Title { get; set; }
	public string Description { get; set; }
	public double Current { get; set; }
	public double Required { get; set; }
	public bool Met { get; set; }

	internal RewardFacet( Services.RewardFacet facet )
	{
		Key = facet.Key;
		Title = facet.Title;
		Description = facet.Description;
		Current = facet.Current;
		Required = facet.Required;
		Met = facet.Met;
	}
}

/// <summary>
/// An open reward claim: the candidate items the player may choose from, and how many of them
/// they get to keep. Created by claiming; resolved by choosing.
/// </summary>
public class RewardOffer
{
	public long DropId { get; set; }
	public int PickCount { get; set; }
	public RewardItem[] Items { get; set; } = Array.Empty<RewardItem>();

	internal RewardOffer() { }

	internal RewardOffer( Services.RewardOffer offer )
	{
		DropId = offer.DropId;
		PickCount = offer.PickCount;

		if ( offer.Items is not null )
		{
			Items = new RewardItem[offer.Items.Length];
			for ( int i = 0; i < offer.Items.Length; i++ )
			{
				Items[i] = new RewardItem( offer.Items[i] );
			}
		}
	}
}

/// <summary>A single reward item, with just enough to draw it in a picker.</summary>
public class RewardItem
{
	public long ItemDefId { get; set; }
	public string Name { get; set; }
	public string Icon { get; set; }
	public ItemRarity Rarity { get; set; }

	/// <summary>The hex colour for this item's rarity (e.g. "#8847ff"), or null if no rarity.</summary>
	public string RarityColor => Rarity.GetColor();

	internal RewardItem() { }

	internal RewardItem( Services.RewardItem item )
	{
		ItemDefId = item.ItemDefId;
		Name = item.Name;
		Icon = item.Icon;

		// Resolve rarity from the Steam inventory definition
		var def = Services.Inventory.FindDefinition( (int)item.ItemDefId );
		Rarity = ItemRarityExtensions.ParseRarity( def?.Rarity );
	}
}

/// <summary>The player's pick(s) from an open offer — pass back to <see cref="MenuUtility.Rewards.ChooseReward"/>.</summary>
public class RewardChoice
{
	public long DropId { get; set; }
	public long[] ItemDefIds { get; set; }
}

/// <summary>The outcome of choosing — the granted items, or an error reason.</summary>
public class RewardResult
{
	public bool Success { get; set; }
	public string Error { get; set; }
	public RewardItem[] Items { get; set; } = Array.Empty<RewardItem>();

	internal RewardResult() { }

	internal RewardResult( Services.RewardResult result )
	{
		if ( result is null )
		{
			Success = false;
			Error = "no_response";
			return;
		}

		Success = result.Success;
		Error = result.Error;

		if ( result.Items is not null )
		{
			Items = new RewardItem[result.Items.Length];
			for ( int i = 0; i < result.Items.Length; i++ )
			{
				Items[i] = new RewardItem( result.Items[i] );
			}
		}
	}
}
