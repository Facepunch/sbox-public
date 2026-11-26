using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// The chat UI. The platform handles sending, validation and delivery, this only shows the messages it reports through <see cref="IChatEvent"/>
/// </summary>
public sealed class ChatBox : PanelComponent, IChatEvent
{
	/// <summary>
	/// Messages kept in the history
	/// </summary>
	const int MaxEntries = 100;

	const float SubmitDelay = 0.15f;

	public static bool IsOpen { get; private set; }

	Panel _box;
	Panel _history;
	Panel _recent;
	Panel _recentList;
	ChatTextEntry _entry;

	/// <summary>
	/// Slideout speed, make sure to keep this inline with ChatBox scss
	/// </summary>
	const float SlideOutTime = 0.1f;

	RealTimeSince _sinceOpened;
	RealTimeUntil _closingUntil;
	bool _closing;

	protected override void OnEnabled()
	{
		base.OnEnabled();

		if ( Panel is null || _box.IsValid() )
			return;

		// recent messages, shown while the box is closed
		_recent = Panel.Add.Panel( "recent" );
		_recentList = _recent.Add.Panel( "recent-list" );

		_box = Panel.Add.Panel( "box" );

		// a click anywhere in the box that doesn't land on something else puts the cursor back in the text entry
		_box.AddEventListener( "onclick", () => { if ( IsOpen ) _entry.Focus(); } );
		_history = _box.Add.Panel( "history" );
		_history.PreferScrollToBottom = true;

		var row = _box.Add.Panel( "input-row" );

		_entry = row.AddChild<ChatTextEntry>( "entry" );
		_entry.MaxLength = Sandbox.Platform.Chat.MaxMessageLength;
		_entry.Placeholder = "type your message here...";
		_entry.AddEventListener( "onsubmit", Submit );
		_entry.AddEventListener( "oncancel", Close );
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		if ( IsOpen )
			Close();
	}

	void IChatEvent.OnChatMessage( ChatMessageEvent e )
	{
		if ( !_history.IsValid() )
			return;

		Add( ChatEntry.From( e ) );
	}

	/// <summary>
	/// Shows a message with no sender on this client only, optionally in a colour of its own. It doesn't go through the platform, so other players don't see it.
	/// </summary>
	public static void AddSystem( string text, Color? color = null )
	{
		Game.ActiveScene?.Get<ChatBox>()?.Add( ChatEntry.System( text, color ) );
	}

	void Add( ChatEntry entry )
	{
		if ( !_history.IsValid() )
			return;

		_history.AddChild( new ChatLine( entry, fades: false ) );
		_recentList.AddChild( new ChatLine( entry, fades: true ) );

		while ( _history.ChildrenCount > MaxEntries )
			_history.GetChild( 0 ).Delete( true );
	}

	protected override void OnUpdate()
	{
		if ( !_box.IsValid() )
			return;

		var visible = Sandbox.Platform.Chat.Enabled && Hud.IsVisible( HudElement.Chat );
		Panel.Style.Display = visible ? DisplayMode.Flex : DisplayMode.None;

		// both sit above the vitals
		_box.Style.Bottom = Length.Pixels( Hud.StackBottom * _box.ScaleFromScreen );
		_recent.Style.Bottom = Length.Pixels( Hud.StackBottom * _recent.ScaleFromScreen );
		_recent.Style.Display = GamePreferences.DrawChatMessages ? null : DisplayMode.None;

		_box.Style.Left = Hud.MarginLength;
		for ( int i = 0; i < _recentList.ChildrenCount; i++ )
		{
			var line = _recentList.GetChild( i );
			if ( line is not null ) line.Style.PaddingLeft = Hud.MarginLength;
		}

		if ( !visible )
		{
			if ( IsOpen ) Close();
			return;
		}

		if ( !IsOpen && Input.Pressed( "Chat" ) && !SpawnMenuHost.IsOpen && InputFocus.Current is not TextEntry )
			Open();

		if ( IsOpen && Input.EscapePressed )
		{
			Input.EscapePressed = false;
			Close();
		}

		if ( _closing && _closingUntil <= 0 )
		{
			_closing = false;
			SetClass( "closing", false );
		}

		if ( IsOpen && InputFocus.Current is null )
			_entry.Focus();
	}

	/// <summary>
	/// Opens the box for typing
	/// </summary>
	public void Open()
	{
		IsOpen = true;
		_sinceOpened = 0;
		_entry.Text = "";
		_entry.Focus();

			_closing = false;
		SetClass( "closing", false );
		SetClass( "open", true );
	}

	/// <summary>
	/// Closes the box without sending what was typed
	/// </summary>
	public void Close()
	{
		IsOpen = false;
		_entry.Blur();
		_entry.Text = "";

		_closing = true;
		_closingUntil = SlideOutTime;
		SetClass( "closing", true );
		SetClass( "open", false );
	}

	void Submit()
	{
		if ( !IsOpen || _sinceOpened < SubmitDelay )
			return;

		var text = _entry.Text?.Trim();
		if ( !string.IsNullOrEmpty( text ) )
			Sandbox.Platform.Chat.Say( text );

		Close();
	}

	class ChatTextEntry : TextEntry
	{
		public ChatTextEntry()
		{
			Multiline = true;
		}

		public override bool CanEnterCharacter( char c )
		{
			return c != '\n' && c != '\r' && base.CanEnterCharacter( c );
		}

		public override void OnButtonTyped( ButtonEvent e )
		{
			if ( e.Button is "enter" or "pad_enter" )
			{
				e.StopPropagation = true;
				Blur();
				CreateEvent( "onsubmit", Text );
				return;
			}

			base.OnButtonTyped( e );
		}
	}
}
