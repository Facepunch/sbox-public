using Sandbox.UI;

namespace Sandbox;

partial class Feed : PanelComponent
{
	[Property] public Texture DeathIcon { get; set; }
	[Property] public Texture HeadshotIcon { get; set; }
	[Property] public Texture ExplosionIcon { get; set; }
	[Property] public Texture SuicideIcon { get; set; }
	[Property] public Texture FallIcon { get; set; }
	[Property] public Texture NpcIcon { get; set; }

	protected override void OnUpdate()
	{
		SetClass( "hide", !Hud.IsVisible( HudElement.KillFeed ) );
		Panel.Style.Top = Hud.MarginLength;
		Panel.Style.Right = Hud.MarginLength;
	}

	[Rpc.Broadcast]
	public void NotifyKill( string victimName, long victimSteamId, string attackerName, long attackerSteamId, string damageTags, string attackerTags, string victimTags, Texture weaponIcon )
	{
		if ( Application.IsDedicatedServer ) return;

		// DisplayName applies the viewer's own Steam nicknames, so resolve it here rather than trusting the host's
		victimName = ResolveName( victimName, victimSteamId );
		attackerName = ResolveName( attackerName, attackerSteamId );

		if ( string.IsNullOrEmpty( victimName ) ) return;

		bool isSuicide = damageTags.Contains( "suicide" );

		Panel panel = new Panel();
		panel.AddClass( "entry" );

		if ( !string.IsNullOrEmpty( attackerName ) && !isSuicide )
		{
			if ( attackerTags.Contains( "npc" ) ) AddIcon( panel, NpcIcon )?.AddClass( "npc-icon" );
			var left = panel.AddChild<Label>( "name attacker" );
			left.Text = attackerName;
			left.SetClass( "npc", attackerTags.Contains( "npc" ) );
		}
		else
		{
			panel.AddClass( "no-attacker" );
		}

		Panel icons = panel.AddChild<Panel>( "icons" );
		if ( weaponIcon.IsValid() )
		{
			AddIcon( icons, weaponIcon );
		}
		else if ( damageTags.Contains( DamageTags.Fall ) || damageTags.Contains( DamageTags.Crush ) )
		{
			// thrown into a wall reads the same as a hard landing
			AddIcon( icons, FallIcon );
		}
		else
		{
			AddIcon( icons, isSuicide ? SuicideIcon : DeathIcon );
		}

		if ( damageTags.Contains( DamageTags.Headshot ) ) AddIcon( icons, HeadshotIcon )?.AddClass( "modifier" );
		if ( damageTags.Contains( DamageTags.Explosion ) ) AddIcon( icons, ExplosionIcon )?.AddClass( "modifier" );

		if ( victimTags.Contains( "npc" ) ) AddIcon( panel, NpcIcon )?.AddClass( "npc-icon" );
		var right = panel.AddChild<Label>( "name victim" );
		right.Text = victimName;
		right.SetClass( "npc", victimTags.Contains( "npc" ) );

		bool isMe = attackerSteamId > 0 && attackerSteamId == Connection.Local.SteamId;
		if ( isMe )
			panel.AddClass( "is-me" );

		Panel?.AddChild( panel );
		Invoke( 7, () => panel.Delete() );
	}

	private static string ResolveName( string fallback, long steamId )
	{
		if ( steamId == 0 || string.IsNullOrEmpty( fallback ) ) return fallback;

		return Connection.All.FirstOrDefault( x => (long)x.SteamId == steamId )?.DisplayName ?? fallback;
	}

	private Panel AddIcon( in Panel panel, Texture icon )
	{
		if ( !icon.IsValid() )
		{
			Log.Warning( "Couldn't create kill feed icon" );
			return null;
		}

		if ( icon.Width < 1 || icon.Height < 1 )
		{
			Log.Warning( "Tried to add an icon that is zero-sized" );
			return null;
		}

		var iconPanel = panel.AddChild<Panel>( "icon" );
		iconPanel.Style.SetBackgroundImage( icon );
		iconPanel.Style.AspectRatio = icon.Width / icon.Height;

		return iconPanel;
	}
}
