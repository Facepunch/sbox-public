using System.Collections.Generic;
using System.Linq;

namespace Editor;

[Icon( "search" )]
sealed class IconPickerWidget : Widget
{
	public Action<string> ValueChanged { get; set; }
	public int IconsPerPage = 80;
	public int IconsPerRow = 8;

	string currentCategory = "All Icons";

	string _icon;
	public string Icon
	{
		get => _icon;
		set
		{
			_icon = value;
			Rebuild();
			ValueChanged?.Invoke( _icon );
		}
	}

	int CurrentPage = 0;
	Label HeaderLabel;
	GridLayout ContentLayout;

	public IconPickerWidget( Widget parent = null ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Alignment = TextFlag.CenterTop;
		FocusMode = FocusMode.Click;


		var headerLayout = Layout.Row();
		headerLayout.Margin = 8;
		headerLayout.Spacing = 2;
		Layout.Add( headerLayout );

		var buttonLeft = headerLayout.Add( new IconButton( "arrow_back", ButtonLeft, this ) );

		HeaderLabel = headerLayout.Add( new Label( this ) );
		HeaderLabel.Alignment = TextFlag.Center;

		var buttonRight = headerLayout.Add( new IconButton( "arrow_forward", ButtonRight, this ) );

		ContentLayout = Layout.Grid();
		ContentLayout.Margin = 8;
		ContentLayout.Spacing = 6;
		ContentLayout.Alignment = TextFlag.LeftTop;
		Layout.Add( ContentLayout );

		Layout.AddStretchCell( 1 );

		Rebuild();
	}

	void Rebuild()
	{
		var currentPage = CurrentPage;
		int totalPages = GetTotalPages();
		if (totalPages == 0) totalPages = 1;
		if (currentPage >= totalPages) currentPage = Math.Max(0, totalPages - 1);
		if (currentPage < 0) currentPage = 0;
		HeaderLabel.Text = $"Page {currentPage + 1}/{totalPages}";

		var iconNames = GetAvailableIconNames();

		ContentLayout.Clear( true );
		int startingIndex = currentPage * IconsPerPage;
		int endingIndex = Math.Min( startingIndex + IconsPerPage, iconNames.Length );
		int x = 0;
		int y = 0;
		for ( int i = startingIndex; i < endingIndex; i++ )
		{
			var icon = iconNames[i];
			var iconButton = new IconButton( icon );
			iconButton.IconSize = 20;
			iconButton.ToolTip = icon;
			iconButton.OnClick = () => Icon = icon;
			if ( icon == Icon )
				iconButton.OnPaintOverride = () =>
				{
					Paint.ClearPen();
					Paint.SetBrush( Theme.Blue.WithAlpha( 1f ) );
					Paint.DrawRect( iconButton.LocalRect, 2 );
					Paint.SetBrushAndPen( Theme.ControlBackground, Theme.ControlBackground );
					Paint.DrawIcon( iconButton.LocalRect, icon, 20 );
					return true;
				};
			ContentLayout.AddCell( x, y, iconButton );
			x++;
			if ( x >= IconsPerRow )
			{
				x = 0;
				y++;
			}
		}

		Update();
	}

	void ButtonLeft()
	{
		var currentPage = CurrentPage;
		currentPage--;
		if ( currentPage < 0 )
			currentPage = GetTotalPages() - 1;
		CurrentPage = currentPage;
		Rebuild();
	}

	void ButtonRight()
	{
		var currentPage = CurrentPage;
		currentPage++;
		if ( currentPage >= GetTotalPages() )
			currentPage = 0;
		CurrentPage = currentPage;
		Rebuild();
	}

	string[] GetAvailableIconNames()
	{
		return EmojiCategories[currentCategory];
	}

	int GetTotalPages()
	{
		var baseNames = EmojiCategories[currentCategory];
		return (int)MathF.Ceiling( baseNames.Length / (float)IconsPerPage );
	}

	/// <summary>
	/// Open a Icon Picker popup
	/// </summary>
	public static void OpenPopup( Widget parent, string icon, Action<string> onChange )
	{
		var popup = new PopupWidget( parent );
		popup.Visible = false;
		popup.FixedWidth = 250;
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 8;

		var editor = popup.Layout.Add( new IconPickerWidget( popup ), 1 );

		editor.currentCategory = "All Icons";
		var startingPageIndex = Array.IndexOf( EmojiCategories[editor.currentCategory], icon ) / editor.IconsPerPage;
		editor.CurrentPage = startingPageIndex;
		editor.Icon = icon;
		editor.ValueChanged = onChange;

		popup.OpenAtCursor();
	}

	static Dictionary<string, (string Emoji, string Name)> EmojiCategoryInfo = new()
	{
		["All Icons"] = ("😀", "All Icons"),
	};

	static string[] SmileysPeopleEmojis = new[] { "😀", "😁", "😂", "🤣", "😃", "😄", "😅", "😆", "😉", "😊", "🙂", "🙃", "😇", "🥰", "😍", "🤩", "😘", "😗", "😚", "😙", "🤗", "🤭", "🤫", "🤔", "🤨", "😐", "😑", "😶", "😏", "😣", "😥", "😮‍💨", "😮", "😯", "😲", "😳", "🥺", "😦", "😧", "😨", "😰", "😢", "😭", "😱", "😖", "😣", "😤", "😡", "😠", "🤬", "🤯", "😳", "🥵", "🥶", "😴", "😪", "😵", "🤐", "🥴", "🤢", "🤮", "🤧", "😷", "🤒", "🤕", "👶", "🧒", "👦", "👧", "🧑", "👨", "👩", "🧓", "👴", "👵", "👲", "👳‍♂️", "👳‍♀️", "🧕", "👮‍♂️", "👮‍♀️", "👷‍♂️", "👷‍♀️", "💂‍♂️", "💂‍♀️", "🕵️‍♂️", "🕵️‍♀️", "👩‍⚕️", "👨‍⚕️", "👩‍🎓", "👨‍🎓", "👩‍🏫", "👨‍🏫", "👩‍⚖️", "👨‍⚖️", "👩‍🌾", "👨‍🌾", "👩‍🍳", "👨‍🍳", "👩‍🔧", "👨‍🔧", "👩‍🏭", "👨‍🏭", "👩‍💼", "👨‍💼", "👩‍🔬", "👨‍🔬", "👩‍🎨", "👨‍🎨", "👩‍✈️", "👨‍✈️", "👩‍🚀", "👨‍🚀", "👩‍🚒", "👨‍🚒", "🧑‍🌾", "🧑‍🍳", "🧑‍⚕️", "🧑‍🏫", "🧑‍💻", "🧑‍🎓", "🧑‍⚖️", "🧑‍🔧", "🧑‍🚀", "🧑‍✈️", "🧑‍🚒", "🤝", "👍", "👎", "👊", "✊", "🤛", "🤜", "🤞", "✌️", "🤟", "🤘", "👌", "🤏", "👈", "👉", "👆", "👇", "☝️", "✋", "🤚", "🖐️", "🖖", "👋", "🤙", "💪", "🦾", "🦵", "🦿", "🦶", "👂", "👃", "🧠", "👀", "👁️", "👅", "👄", "💋" };

	static string[] AnimalsNatureEmojis = new[] { "🐶", "🐱", "🐭", "🐹", "🐰", "🦊", "🐻", "🐼", "🐨", "🐯", "🦁", "🐮", "🐷", "🐽", "🐸", "🐵", "🦄", "🐔", "🐧", "🐦", "🐤", "🐣", "🐥", "🦆", "🦅", "🦉", "🦇", "🐺", "🐗", "🐴", "🐝", "🐛", "🐌", "🐞", "🦋", "🐢", "🐍", "🦎", "🐙", "🦑", "🐠", "🐟", "🐬", "🐳", "🐋", "🐊", "🐅", "🐆", "🦓", "🦍", "🐘", "🦏", "🦛", "🐪", "🐫", "🦒", "🐃", "🐂", "🐄", "🐖", "🐏", "🐑", "🐐", "🐓", "🦃", "🕊️", "🌲", "🌳", "🌴", "🌵", "🎄", "🌾", "🌿", "☘️", "🍀", "🍁", "🍂", "🍃", "🌸", "🌼", "🌻", "🌺", "🌹", "💐", "🌷", "🪴", "🌱", "🌵" };
	static string[] FoodDrinkEmojis = new[] { "🍏", "🍎", "🍐", "🍊", "🍋", "🍌", "🍉", "🍇", "🍓", "🫐", "🍈", "🍒", "🍑", "🥭", "🍍", "🥥", "🥝", "🍅", "🍆", "🥑", "🥦", "🥬", "🥒", "🌶️", "🥕", "🌽", "🥔", "🍠", "🧄", "🧅", "🥐", "🥖", "🥨", "🥯", "🍎", "🥞", "🧇", "🧀", "🥚", "🍳", "🥓", "🥩", "🍗", "🍖", "🌭", "🍔", "🍟", "🍕", "🫓", "🫔", "🥪", "🌮", "🌯", "🫕", "🍝", "🍜", "🍲", "🥟", "🥠", "🥡", "🍣", "🍤", "🍙", "🍚", "🍘", "🍥", "🍦", "🍧", "🍨", "🍩", "🍪", "🎂", "🍰", "🧁", "🍫", "🍬", "🍭", "🍮", "🍯", "☕", "🍵", "🍶", "🍺", "🍻", "🥂", "🍷", "🍸", "🍹", "🧉", "🧋", "🥤", "🧃", "🧊" };
	static string[] ActivitiesEmojis = new[] { "⚽", "🏀", "🏈", "⚾", "🎾", "🏐", "🏉", "🎱", "🏓", "🏸", "🥅", "🏒", "🏑", "🥍", "🏏", "⛳", "🏹", "🎣", "🥊", "🥋", "⛸️", "🛷", "🥌", "🎿", "⛷️", "🏂", "🏋️‍♂️", "🏋️‍♀️", "🤼‍♂️", "🤼‍♀️", "🤸‍♂️", "🤸‍♀️", "⛹️‍♂️", "⛹️‍♀️", "🤺", "🏇", "🧗‍♂️", "🧗‍♀️", "🚴‍♂️", "🚴‍♀️", "🚵‍♂️", "🚵‍♀️", "🏊‍♂️", "🏊‍♀️", "🤽‍♂️", "🤽‍♀️", "🤾‍♂️", "🤾‍♀️", "🏌️‍♂️", "🏌️‍♀️", "🏄‍♂️", "🏄‍♀️", "🏆", "🏅", "🎖️", "🥇", "🥈", "🥉", "🎳", "🎮", "🎲", "🎯" };
	static string[] TravelPlacesEmojis = new[] { "🚗", "🚕", "🚙", "🚌", "🚎", "🏎️", "🚓", "🚑", "🚒", "🚐", "🚚", "🚛", "🚜", "🛵", "🛺", "🛻", "🚲", "🛴", "🚂", "🚆", "🚇", "🚊", "🚉", "✈️", "🛩️", "🛫", "🛬", "🚀", "🛸", "⛵", "🛶", "🚤", "🛳️", "⛴️", "🗺️", "🗽", "🗼", "🏰", "🏯", "🏟️", "🏛️", "🕌", "🕍", "⛩️", "🛕", "🕋", "⛲", "🏝️", "🏖️", "🏜️", "🌋", "⛰️", "🏔️", "🏕️", "🏞️", "🌅", "🌄", "🌇", "🌆", "🏙️", "🌃", "🌉" };
	static string[] ObjectsEmojis = new[] { "⌚", "📱", "📲", "💻", "🖥️", "🖨️", "🖱️", "🎧", "🎤", "🎬", "📷", "📸", "📹", "📼", "📺", "📻", "🧭", "🧱", "🔧", "🔨", "⚒️", "🛠️", "🪓", "🔩", "⚙️", "🧰", "🧲", "🪛", "🔌", "💡", "🔦", "🕯️", "🪔", "🧯", "🛢️", "💸", "💵", "💴", "💶", "💷", "💰", "💳", "🧾", "💎", "⚖️", "🧪", "🧫", "🧬", "🔬", "🔭", "📡", "🧯", "🧱", "🧹", "🧺", "🧻", "🧼", "🪒", "🪥", "🧴", "🧷", "🧵", "🧶" };
	static string[] FlagsEmojis = new[] { "🏳️", "🏴", "🏳️‍🌈", "🇺🇳", "🇦🇫", "🇦🇱", "🇩🇿", "🇦🇸", "🇦🇩", "🇦🇴", "🇦🇮", "🇦🇶", "🇦🇬", "🇦🇷", "🇦🇲", "🇦🇼", "🇦🇺", "🇦🇹", "🇦🇿", "🇧🇸", "🇧🇭", "🇧🇩", "🇧🇧", "🇧🇾", "🇧🇪", "🇧🇿", "🇧🇯", "🇧🇲", "🇧🇹", "🇧🇴", "🇧🇦", "🇧🇼", "🇧🇷", "🇮🇴", "🇻🇬", "🇧🇳", "🇧🇬", "🇧🇫", "🇧🇮", "🇰🇭", "🇨🇲", "🇨🇦", "🇮🇨", "🇨🇻", "🇧🇶", "🇰🇾", "🇨🇫", "🇹🇩", "🇨🇱", "🇨🇳", "🇨🇴", "🇰🇭", "🇨🇬", "🇨🇩", "🇨🇷", "🇨🇮", "🇭🇷", "🇨🇺", "🇨🇼", "🇨🇾", "🇨🇿", "🇩🇰", "🇩🇯", "🇩🇲", "🇩🇴", "🇪🇄", "🇪🇬", "🇸🇻", "🇬🇼", "🇪🇷", "🇪🇪", "🇪🇹", "🇫🇰", "🇫🇴", "🇫🇯", "🇫🇮", "🇫🇷", "🇬🇫", "🇵🇫", "🇹🇫", "🇬🇦", "🇬🇲", "🇬🇪", "🇩🇪", "🇬🇭", "🇬🇮", "🇬🇷", "🇬🇱", "🇬🇩", "🇬🇵", "🇬🇺", "🇬🇹", "🇬🇬", "🇬🇳", "🇬🇼", "🇬🇾", "🇭🇹", "🇭🇳", "🇭🇰", "🇭🇺", "🇮🇸", "🇮🇳", "🇮🇩", "🇮🇷", "🇮🇶", "🇮🇪", "🇮🇲", "🇮🇱", "🇮🇹", "🇯🇲", "🇯🇵", "🇯🇪", "🇯🇴", "🇰🇿", "🇰🇪", "🇰🇮", "🇽🇰", "🇰🇼", "🇰🇬", "🇱🇦", "🇱🇻", "🇱🇧", "🇱🇸", "🇱🇷", "🇱🇮", "🇱🇹", "🇱🇺", "🇲🇴", "🇲🇰", "🇲🇬", "🇲🇼", "🇲🇾", "🇲🇻", "🇲🇱", "🇲🇹", "🇲🇭", "🇲🇱", "🇲🇺", "🇾🇹", "🇲🇽", "🇫🇲", "🇲🇩", "🇲🇨", "🇲🇳", "🇲🇪", "🇲🇦", "🇲🇿", "🇲🇲", "🇳🇦", "🇳🇷", "🇳🇵", "🇳🇱", "🇳🇨", "🇳🇿", "🇳🇮", "🇳🇪", "🇳🇬", "🇳🇺", "🇳🇫", "🇰🇵", "🇲🇵", "🇰🇵", "🇳🇴", "🇴🇲", "🇵🇰", "🇵🇼", "🇵🇸", "🇵🇦", "🇵🇬", "🇵🇾", "🇵🇪", "🇵🇭", "🇵🇱", "🇵🇹", "🇵🇷", "🇶🇦", "🇷🇴", "🇷🇺", "🇷🇼", "🇧🇱", "🇸🇭", "🇰🇳", "🇱🇨", "🇵🇲", "🇻🇨", "🇼🇸", "🇸🇲", "🇸🇹", "🇸🇦", "🇸🇳", "🇷🇸", "🇸🇨", "🇸🇱", "🇸🇬", "🇸🇰", "🇸🇮", "🇬🇸", "🇸🇧", "🇸🇴", "🇿🇦", "🇰🇷", "🇸🇸", "🇪🇸", "🇱🇰", "🇸🇩", "🇸🇷", "🇸🇿", "🇸🇪", "🇨🇭", "🇸🇾", "🇹🇼", "🇯🇵", "🇹🇿", "🇹🇭", "🇹🇱", "🇹🇬", "🇹🇰", "🇹🇴", "🇹🇹", "🇹🇳", "🇹🇷", "🇹🇲", "🇹🇨", "🇹🇻", "🇺🇬", "🇺🇦", "🇦🇪", "🇬🇧", "🇺🇸", "🇺🇾", "🇺🇿", "🇻🇺", "🇻🇦", "🇻🇪", "🇻🇳", "🇼🇫", "🇪🇭", "🇾🇪", "🇿🇲", "🇿🇼" };
	static string[] SymbolsEmojis = new[] { "❤️", "🧡", "💛", "💚", "💙", "💜", "🖤", "🤍", "🤎", "💔", "❣️", "💕", "💞", "💓", "💗", "💖", "💘", "💝", "💟", "☮️", "☪️", "✝️", "☦️", "🕉️", "🔯", "☸️", "✡️", "🔱", "☯️", "✨", "⭐", "🌟", "💫", "🌈", "⚡", "🔥", "💥", "🌪️", "🌊", "💧", "☔", "❄️", "☃️", "⚓", "🔒", "🔓", "🔑", "🛡️", "🚭", "⚠️", "🚸", "⛔", "❌", "✅", "✔️", "➕", "➖", "✖️", "➗", "♻️", "🔁", "🔂", "🔄", "🔃", "🆗", "🔤", "🔡", "🔠", "🔢", "🔣", "🔟", "🔠", "ℹ️", "🔔", "🔕", "⬛", "⬜", "◼️", "◻️", "◾", "◽", "▪️", "▫️", "🔺", "🔻", "🔴", "🔵", "🟠", "🟡", "🟢", "🟣", "🟤", "⚫", "⚪", "🟥", "🟧", "🟨", "🟩", "🟦", "🟪", "🟫", "⬆️", "↗️", "➡️", "↘️", "⬇️", "↙️", "⬅️", "↖️", "↕️", "↔️", "🔄", "🔀", "🔀", "🔁", "🔂", "▶️", "⏸️", "⏯️", "⏹️", "⏺️", "⏭️", "⏮️", "⏩", "⏪", "🔊", "🔉", "🔈", "🔇", "🔕", "📶", "📳", "📴", "♀️", "♂️", "⚧️", "✖️", "➕", "➖", "➗", "♾️", "‼️", "⁉️", "❓", "❔", "❕", "❗", "〰️", "💯", "🔟", "🔠", "🔡", "🔢", "🔣", "🔤", "🅰️", "🆎", "🅱️", "🆑", "🆒", "🆓", "ℹ️", "🆔", "Ⓜ️", "🆕", "🆖", "🅾️", "🆗", "🅿️", "🆘", "🆙", "🆚", "🈁", "🈂️", "🈷️", "🈶", "🈯", "🉐", "🈹", "🈚", "🈲", "🉑", "🈸", "🈴", "🈳", "㊗️", "㊙️", "🈺", "🈵", "♈️", "♉️", "♊️", "♋️", "♌️", "♍️", "♎️", "♏️", "♐️", "♑️", "♒️", "♓️", "♔", "♕", "♖", "♗", "♘", "♙", "♚", "♛", "♜", "♝", "♞", "♟", "🂡", "🂢", "🂣", "🂤", "🂥", "🂦", "🂧", "🂨", "🂩", "🂪", "🂫", "🂭", "🂮", "🂱", "🂲", "🂳", "🂴", "🂵", "🂶", "🂷", "🂸", "🂹", "🂺", "🂻", "🂽", "🂾", "🃁", "🃂", "🃃", "🃄", "🃅", "🃆", "🃇", "🃈", "🃉", "🃊", "🃋", "🃍", "🃎", "🃏", "Ⓐ", "Ⓑ", "Ⓒ", "Ⓓ", "Ⓔ", "Ⓕ", "Ⓖ", "Ⓗ", "Ⓘ", "Ⓙ", "Ⓚ", "Ⓛ", "Ⓜ", "Ⓝ", "Ⓞ", "Ⓟ", "Ⓠ", "Ⓡ", "Ⓢ", "Ⓣ", "Ⓤ", "Ⓥ", "Ⓦ", "Ⓧ", "Ⓨ", "Ⓩ", "🄰", "🄱", "🄲", "🄳", "🄴", "🄵", "🄶", "🄷", "🄸", "🄹", "🄺", "🄻", "🄼", "🄽", "🄾", "🄿", "🅀", "🅁", "🅂", "🅃", "🅄", "🅅", "🅆", "🅇", "🅈", "🅉", "🅊", "🅋", "🅌", "🅍", "🅎", "🜀", "🜁", "🜂", "🜃", "🜄", "🜅", "🜆", "🜇", "🜈", "🜉", "🜊", "🜋", "🜌", "🜍", "🜎", "🜏", "🜐", "🜑", "🜒", "🜓", "🜔", "🜕", "🜖", "🜗", "🜘", "🜙", "🜚", "🜛", "🜜", "🜝", "🜞", "🜟", "🜠", "🜡", "🜢", "🜣", "🜤", "🜥", "🜦", "🜧", "🜨", "🜩", "🜪", "🜫", "🜬", "🜭", "🜮", "🜯", "🜰", "🜱", "🜲", "🜳", "🜴", "🜵", "🜶", "🜷", "🜸", "🜹", "🜺", "🜻", "🜼", "🜽", "🜾", "🜿" };
	
	static Dictionary<string, string[]> EmojiCategories = new()
	{
		["All Icons"] = SmileysPeopleEmojis.Concat(AnimalsNatureEmojis).Concat(FoodDrinkEmojis).Concat(TravelPlacesEmojis).Concat(ActivitiesEmojis).Concat(ObjectsEmojis).Concat(SymbolsEmojis).Concat(FlagsEmojis).ToArray(),
	};
	

	static void AddRange(List<string> list, int start, int end)
	{
		for (int i = start; i <= end; i++)
		{
			try
			{
				list.Add(char.ConvertFromUtf32(i));
			}
			catch
			{
				// Skip invalid codepoints
			}
		}
	}
}
