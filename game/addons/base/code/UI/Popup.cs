using Sandbox.UI.Construct;
namespace Sandbox.UI;

public partial class Popup : BasePopup
{
	/// <summary>
	/// Which panel triggered this popup. Set by <see cref="SetPositioning"/> or the constructor.
	/// </summary>
	public Panel PopupSource { get; set; }

	/// <summary>
	/// Currently selected option in the popup. Used internally for keyboard navigation.
	/// </summary>
	public Panel SelectedChild { get; set; }

	/// <summary>
	/// Positioning mode for this popup.
	/// </summary>
	public PositionMode Position { get; set; }

	/// <summary>
	/// Offset away from <see cref="PopupSource"/> based on <see cref="Position"/>.
	/// </summary>
	public float PopupSourceOffset { get; set; }

	/// <summary>
	/// If true, will close this popup when the <see cref="PopupSource"/> is hidden.
	/// </summary>
	public bool CloseWhenParentIsHidden { get; set; } = false;

	/// <summary>
	/// Dictates where a <see cref="Popup"/> is positioned.
	/// </summary>
	public enum PositionMode
	{
		/// <summary>
		/// To the left of the source panel, centered.
		/// </summary>
		Left,

		/// <summary>
		/// To the left of the source panel, aligned to the bottom.
		/// </summary>
		LeftBottom,

		/// <summary>
		/// Above the source panel, aligned to the left.
		/// </summary>
		AboveLeft,

		/// <summary>
		/// Below the source panel, aliging on the left. Do not stretch to size of <see cref="Popup.PopupSource"/>.
		/// </summary>
		BelowLeft,

		/// <summary>
		/// Below the source panel, centered horizontally.
		/// </summary>
		BelowCenter,

		/// <summary>
		/// Below the source panel, stretch to the width of the <see cref="Popup.PopupSource"/>.
		/// </summary>
		BelowStretch,

		/// <summary>
		/// Above, centered
		/// </summary>
		AboveCenter,

		/// <summary>
		/// Position where the mouse cursor is currently
		/// </summary>
		UnderMouse
	}

	public Popup()
	{

	}

	/// <inheritdoc cref="SetPositioning"/>
	public Popup( Panel sourcePanel, PositionMode position, float offset )
	{
		SetPositioning( sourcePanel, position, offset );
	}

	/// <summary>
	/// Sets <see cref="PopupSource"/>, <see cref="Position"/> and <see cref="PopupSourceOffset"/>.
	/// Applies relevant CSS classes.
	/// </summary>
	/// <param name="sourcePanel">Which panel triggered this popup.</param>
	/// <param name="position">Desired positioning mode.</param>
	/// <param name="offset">Offset away from the <paramref name="sourcePanel"/>.</param>
	public void SetPositioning( Panel sourcePanel, PositionMode position, float offset )
	{
		Parent = sourcePanel.FindPopupPanel();
		PopupSource = sourcePanel;
		Position = position;
		PopupSourceOffset = offset;

		AddClass( "popup-panel" );
		PositionMe( true );

		switch ( Position )
		{
			case PositionMode.Left:
				AddClass( "left" );
				break;

			case PositionMode.LeftBottom:
				AddClass( "left-bottom" );
				break;

			case PositionMode.AboveLeft:
				AddClass( "above-left" );
				break;

			case PositionMode.AboveCenter:
				AddClass( "above-center" );
				break;

			case PositionMode.BelowLeft:
				AddClass( "below-left" );
				break;

			case PositionMode.BelowCenter:
				AddClass( "below-center" );
				break;

			case PositionMode.BelowStretch:
				AddClass( "below-stretch" );
				break;
		}
	}

	/// <summary>
	/// Header panel that holds <see cref="TitleLabel"/> and <see cref="IconPanel"/>.
	/// </summary>
	protected Panel Header;

	/// <summary>
	/// Label that dispalys <see cref="Title"/>.
	/// </summary>
	protected Label TitleLabel;

	/// <summary>
	/// Panel that dispalys <see cref="Icon"/>.
	/// </summary>
	protected IconPanel IconPanel;

	void CreateHeader()
	{
		if ( Header.IsValid() ) return;

		Header = Add.Panel( "header" );

		IconPanel = Header.Add.Icon( null );
		TitleLabel = Header.Add.Label( null, "title" );
	}

	/// <summary>
	/// If set, will add an unselectable header with given text and <see cref="Icon"/>.
	/// </summary>
	public string Title
	{
		get => TitleLabel?.Text;
		set
		{
			CreateHeader();
			TitleLabel.Text = value;
		}
	}

	/// <summary>
	/// If set, will add an unselectable header with given icon and <see cref="Title"/>.
	/// </summary>
	public string Icon
	{
		get => IconPanel?.Text;
		set
		{
			CreateHeader();
			IconPanel.Text = value;
		}
	}

	/// <summary>
	/// Closes all panels, marks this one as a success and closes it.
	/// </summary>
	public void Success()
	{
		AddClass( "success" );
		Popup.CloseAll();
	}

	/// <summary>
	/// Closes all panels, marks this one as a failure and closes it.
	/// </summary>
	public void Failure()
	{
		AddClass( "failure" );
		Popup.CloseAll();
	}

	/// <summary>
	/// Add an option to this popup with given text and click action.
	/// </summary>
	public Panel AddOption( string text, Action action = null )
	{
		return AddChild( new Button( text, () =>
		{
			CloseAll();
			action?.Invoke();
		} ) );
	}

	/// <summary>
	/// Add an option to this popup with given text, icon and click action.
	/// </summary>
	public Panel AddOption( string text, string icon, Action action = null )
	{
		return AddChild( new Button( text, icon, () => { CloseAll(); action?.Invoke(); } ) );
	}

	/// <summary>
	/// Move selection in given direction.
	/// </summary>
	/// <param name="dir">Positive numbers move selection downwards, negative - upwards.</param>
	public void MoveSelection( int dir )
	{
		var currentIndex = GetChildIndex( SelectedChild );

		if ( currentIndex >= 0 ) currentIndex += dir;
		else if ( currentIndex < 0 ) currentIndex = dir == 1 ? 0 : -1;

		SelectedChild?.SetClass( "active", false );
		SelectedChild = GetChild( currentIndex, true );
		SelectedChild?.SetClass( "active", true );
	}

	public override void Tick()
	{
		base.Tick();

		if ( CloseWhenParentIsHidden && !PopupSource.IsValid() )
		{
			Delete();
			return;
		}

		PositionMe( false );
	}

	public override void OnLayout( ref Rect layoutRect )
	{
		var padding = 10;
		var h = Screen.Height - padding;
		var w = Screen.Width - padding;

		if ( layoutRect.Bottom > h )
		{
			layoutRect.Top -= layoutRect.Bottom - h;
			layoutRect.Bottom -= layoutRect.Bottom - h;
		}

		if ( layoutRect.Right > w )
		{
			layoutRect.Left -= layoutRect.Right - w;
			layoutRect.Right -= layoutRect.Right - w;
		}
	}

	void PositionMe( bool isInitial )
	{
		var rect = PopupSource.Box.Rect * PopupSource.ScaleFromScreen;

		var w = Screen.Width * PopupSource.ScaleFromScreen;
		var h = Screen.Height * PopupSource.ScaleFromScreen;


		Style.MaxHeight = Screen.Height - 50;

		switch ( Position )
		{
			case PositionMode.Left:
				{
					Style.Left = null;
					Style.Right = ((w - rect.Left) + PopupSourceOffset);
					Style.Top = rect.Top + rect.Height * 0.5f;
					break;
				}
			case PositionMode.LeftBottom:
				{
					Style.Left = null;
					Style.Right = ((w - rect.Left) + PopupSourceOffset);
					Style.Top = null;
					Style.Bottom = (h - rect.Bottom);
					break;
				}

			case PositionMode.AboveLeft:
				{
					Style.Left = rect.Left;
					Style.Bottom = (Parent.Box.Rect * Parent.ScaleFromScreen).Height - rect.Top + PopupSourceOffset;
					break;
				}

			case PositionMode.AboveCenter:
				{
					Style.Left = rect.Left + rect.Width * 0.5f;
					Style.Bottom = (Parent.Box.Rect * Parent.ScaleFromScreen).Height - rect.Top + PopupSourceOffset;
					break;
				}

			case PositionMode.BelowLeft:
				{
					Style.Left = rect.Left;
					Style.Top = rect.Bottom + PopupSourceOffset;
					break;
				}

			case PositionMode.BelowCenter:
				{
					Style.Left = rect.Center.x; // centering is done via styles
					Style.Top = rect.Bottom + PopupSourceOffset;
					break;
				}

			case PositionMode.BelowStretch:
				{
					Style.Left = rect.Left;
					Style.Width = rect.Width;
					Style.Top = rect.Bottom + PopupSourceOffset;
					break;
				}

			case PositionMode.UnderMouse:
				{
					if ( isInitial )
					{
						Style.Left = Mouse.Position.x * PopupSource.ScaleFromScreen;
						Style.Top = (Mouse.Position.y + PopupSourceOffset) * PopupSource.ScaleFromScreen;
					}
					break;
				}
		}

		Style.Dirty();
	}
}
