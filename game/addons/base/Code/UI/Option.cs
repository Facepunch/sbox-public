
namespace Sandbox.UI
{
	/// <summary>
	/// An option for a <see cref="DropDown"/> or <see cref="ButtonGroup"/>.
	/// </summary>
	public class Option
	{
		/// <summary>
		/// The user-friendly text to show.
		/// </summary>
		public string Title;

		/// <summary>
		/// Icon for this option.
		/// </summary>
		public string Icon;

		public string Subtitle;

		/// <summary>
		/// Tooltip for this option.
		/// </summary>
		public string Tooltip;

		/// <summary>
		/// The internal value for this option.
		/// </summary>
		public object Value;

		public Option()
		{

		}

		public Option( string title, object value )
		{
			Title = title;
			Value = value;
		}
		public Option( string title, string icon, object value )
		{
			Title = title;
			Icon = icon;
			Value = value;
		}
	}
}
