/// <summary>
/// Lets scene components and systems add actions to an object's right-click menu.
/// Options are collected with a sort order (lower is higher in the menu), then the Inspector builds the menu.
/// </summary>
public interface IContextMenuEvent : ISceneEvent<IContextMenuEvent>
{
	public sealed record Option( string Icon, string Text, Action Action, Action<Sandbox.UI.Menu> SubmenuBuilder, int Order, bool Enabled = true );

	public sealed class Event( GameObject target )
	{
		/// <summary>
		/// The network root of the object that was right-clicked.
		/// </summary>
		public GameObject Target { get; } = target;

		private readonly List<Option> _options = new();

		/// <summary>
		/// Options collected so far, sorted by order. Stable, so options sharing an order keep insertion order.
		/// </summary>
		public IEnumerable<Option> Options => _options.OrderBy( o => o.Order );

		/// <summary>
		/// Add an option. A disabled one is shown greyed out and can't be picked.
		/// </summary>
		public void AddOption( string icon, string text, Action action, int order = 0, bool enabled = true )
		{
			_options.Add( new Option( icon, text, action, null, order, enabled ) );
		}

		public void AddSubmenu( string icon, string text, Action<Sandbox.UI.Menu> builder, int order = 0 )
		{
			_options.Add( new Option( icon, text, null, builder, order ) );
		}

		/// <summary>
		/// Build the collected options into a menu.
		/// </summary>
		public void Populate( Sandbox.UI.Menu menu )
		{
			foreach ( var option in Options )
			{
				if ( option.SubmenuBuilder is not null )
					option.SubmenuBuilder( menu.AddMenu( option.Text, option.Icon ) );
				else
					menu.AddOption( option.Text, option.Icon, option.Action ).Enabled = option.Enabled;
			}
		}
	}

	void PopulateContextMenu( Event e ) { }
}
