using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// A single chat entry. Used twice per entry: in the chat box history, and in the list of recent
/// messages shown while the box is closed, where it fades out on its own.
/// </summary>
public sealed class ChatLine : Panel
{
	/// <summary>
	/// How long a recent message stays fully visible
	/// </summary>
	const float VisibleTime = 8;

	/// <summary>
	/// How long it then takes to fade out
	/// </summary>
	const float FadeTime = 0.6f;

	readonly ChatEntry _entry;
	readonly bool _fades;
	readonly Label _name;
	readonly Label _text;

	public ChatLine( ChatEntry entry, bool fades )
	{
		_entry = entry;
		_fades = fades;

		if ( entry.Kind == ChatEntryKind.Player )
		{
			if ( entry.SteamId != 0 )
				AddChild( new Image() { Classes = "avatar", Texture = Texture.LoadAvatar( entry.SteamId ) } );
			else
				Add.Panel( "avatar" );
		}
		else
		{
			AddClass( "system" );
		}

		if ( entry.Kind == ChatEntryKind.Player )
		{
			_name = new Label() { Tokenize = false, Text = $"{entry.Name}:", Classes = "name" };
			AddChild( _name );
		}

		// not tokenized, so a message starting with # can't pull a phrase out of the localization files
		_text = new Label() { Tokenize = false, Text = entry.Text, Classes = "text" };
		AddChild( _text );

		// a system message can have a colour of its own, everything else is coloured by the stylesheet
		if ( entry.TextColor is { } color )
			_text.Style.FontColor = color;
	}

	public override void Tick()
	{
		base.Tick();

		if ( !_fades )
			return;

		var alpha = 1 - MathX.Clamp( (_entry.Age - VisibleTime) / FadeTime, 0f, 1f );
		Style.Opacity = alpha;

		if ( alpha <= 0 )
			Delete();
	}
}
