
namespace Sandbox.UI
{
	/// <summary>
	/// A button that opens a <see cref="Popup"/> panel.
	/// Useless on its own - you need to implement Open
	/// </summary>
	public abstract class PopupButton : Button
	{
		/// <summary>
		/// The opened <see cref="UI.Popup"/>.
		/// </summary>
		protected Popup Popup;

		public PopupButton()
		{
			AddClass( "popupbutton" );
		}

		protected override void OnClick( MousePanelEvent e )
		{
			base.OnClick( e );

			Open();
		}

		/// <summary>
		/// Open a popup. You should set <see cref="Popup"/> here.
		/// </summary>
		public abstract void Open();

		public override void Tick()
		{
			base.Tick();

			SetClass( "open", Popup.IsValid() && !Popup.IsDeleting );
			SetClass( "active", Popup.IsValid() && !Popup.IsDeleting );

			if ( Popup.IsValid() )
			{
				Popup.Style.Width = Box.Rect.Width * ScaleFromScreen;
			}
		}
	}
}
