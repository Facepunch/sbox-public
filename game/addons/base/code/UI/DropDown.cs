using Sandbox.UI.Construct;

namespace Sandbox.UI
{
	/// <summary>
	/// A UI control which provides multiple options via a dropdown box.
	/// </summary>
	[Library( "select" ), Alias( "dropdown" )]
	public class DropDown : PopupButton
	{
		/// <summary>
		/// The icon of an arrow pointing down on the right of the element.
		/// </summary>
		protected IconPanel DropdownIndicator;

		/// <summary>
		/// Called when the value has been changed,
		/// </summary>
		[Parameter] public System.Action<string> ValueChanged { get; set; }

		/// <summary>
		/// Called just before opening, allows options to be dynamic
		/// </summary>
		[Parameter] public System.Func<List<Option>> BuildOptions { get; set; }

		/// <summary>
		/// The options to show on click. You can edit these directly via this property.
		/// </summary>
		[Parameter] public List<Option> Options { get; set; } = new();

		Option selected;

		object _value;
		int _valueHash;

		/// <summary>
		/// The current string value. This is useful to have if Selected is null.
		/// </summary>
		[Parameter]
		public override object Value
		{
			get => _value;
			set
			{
				if ( _valueHash == HashCode.Combine( value ) ) return;

				_valueHash = HashCode.Combine( value );
				_value = value;

				if ( BuildOptions != null )
				{
					Options = BuildOptions.Invoke();
				}

				if ( _value != null && Options.Count == 0 )
				{
					PopulateOptionsFromType( _value.GetType() );
				}

				Select( _value?.ToString(), false );
			}
		}

		/// <summary>
		/// The currently selected option.
		/// </summary>
		public Option Selected
		{
			get => selected;
			set
			{
				if ( selected == value ) return;

				selected = value;

				if ( selected != null )
				{
					var v = $"{selected.Value}";

					Value = v;
					Icon = selected.Icon;
					Text = selected.Title;

					ValueChanged?.Invoke( v );
					CreateEvent( "onchange" );
					CreateValueEvent( "value", selected?.Value );
				}
			}
		}

		public DropDown()
		{
			AddClass( "dropdown" );
			DropdownIndicator = Add.Icon( "expand_more", "dropdown_indicator" );
		}

		public DropDown( Panel parent ) : this()
		{
			Parent = parent;
		}

		public override void SetPropertyObject( string name, object value )
		{
			base.SetPropertyObject( name, value );
		}

		/// <summary>
		/// Given the type, populate options. This is useful if you're an enum type.
		/// </summary>
		private void PopulateOptionsFromType( Type type )
		{
			if ( type == typeof( bool ) )
			{
				Options.Add( new Option( "True", true ) );
				Options.Add( new Option( "False", false ) );
				return;
			}

			if ( type.IsEnum )
			{
				var names = type.GetEnumNames();
				var values = type.GetEnumValues();

				for ( int i = 0; i < names.Length; i++ )
				{
					Options.Add( new Option( names[i], values.GetValue( i ) ) );
				}

				return;
			}

			//Log.Info( $"Dropdown Type: {type}" );
		}

		/// <summary>
		/// Open the dropdown.
		/// </summary>
		public override void Open()
		{
			Popup = new Popup( this, Popup.PositionMode.BelowStretch, 0.0f );
			Popup.CloseWhenParentIsHidden = true;
			Popup.AddClass( "flat-top" );

			if ( BuildOptions != null )
			{
				Options = BuildOptions.Invoke();
			}

			foreach ( var option in Options )
			{
				var o = Popup.AddOption( option.Title, option.Icon, () => Select( option ) );
				if ( Selected != null && option.Value == Selected.Value )
				{
					o.AddClass( "active" );
				}
			}
		}

		/// <summary>
		/// Select an option.
		/// </summary>
		protected virtual void Select( Option option, bool triggerChange = true )
		{
			if ( !triggerChange )
			{
				selected = option;

				if ( option != null )
				{
					Value = option.Value;
					Icon = option.Icon;
					Text = option.Title;
				}
			}
			else
			{
				Selected = option;
			}
		}

		/// <summary>
		/// Select an option by value string.
		/// </summary>
		protected virtual void Select( string value, bool triggerChange = true )
		{
			Select( Options.FirstOrDefault( x => IsOptionMatch( x, value ) ), triggerChange );
		}

		private bool IsOptionMatch( Option option, string value )
		{
			if ( option.Value == null || (option.Value is string stringValue && string.IsNullOrEmpty( value )) )
				return string.IsNullOrEmpty( value );

			return string.Equals( option.Value?.ToString(), value, StringComparison.OrdinalIgnoreCase );
		}

		protected override void OnParametersSet()
		{
			// Only clear if we have some options to populate
			if ( Children.Any( x => x.ElementName.Equals( "option", StringComparison.OrdinalIgnoreCase ) ) ) Options.Clear();

			foreach ( var child in Children )
			{
				if ( child.ElementName.Equals( "option", StringComparison.OrdinalIgnoreCase ) )
				{
					var o = new Option();
					o.Title = string.Join( "", child.Descendants.OfType<Label>().Select( x => x.Text ) );
					o.Value = child.GetAttribute( "value", o.Title );
					o.Icon = child.GetAttribute( "icon", null );

					Options.Add( o );
				}
			}
		}
	}
}
