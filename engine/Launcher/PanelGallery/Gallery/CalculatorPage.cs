using System.Globalization;

namespace Sandbox.PanelGallery;

/// <summary>
/// A small functional app combining buttons, keyboard input, responsive layout and history.
/// </summary>
public class CalculatorPage : GalleryPage
{
	readonly Sandbox.UI.Label _display;
	readonly Sandbox.UI.Label _expression;
	readonly Panel _history;
	string _entry = "0";
	decimal _accumulator;
	string _operator;
	bool _newEntry = true;
	bool _error;

	public CalculatorPage() : base( "Calculator", "A working calculator made from panels. Click the keypad or type numbers and operators. Enter calculates, Backspace deletes and Escape clears. Click a history entry to reuse its result." )
	{
		AcceptsFocus = true;
		var app = Add.Panel( "calculator-app" );
		var calculator = app.Add.Panel( "calculator" );
		calculator.Add.Label( "PANEL CALCULATOR", "calculator-brand" );
		_expression = calculator.Add.Label( "Ready", "calculator-expression" );
		_display = calculator.Add.Label( "0", "calculator-display" );
		var keypad = calculator.Add.Panel( "calculator-keypad" );
		foreach ( var key in new[] { "C", "±", "%", "÷", "7", "8", "9", "×", "4", "5", "6", "−", "1", "2", "3", "+", "⌫", "0", ".", "=" } )
		{
			var button = new Sandbox.UI.Button( key, () => { Press( key ); Focus(); } );
			button.AddClass( "calculator-key" );
			button.SetClass( "operator", "+−×÷".Contains( key ) );
			button.SetClass( "equals", key == "=" );
			button.AcceptsFocus = false;
			keypad.AddChild( button );
		}
		var history = app.Add.Panel( "calculator-history" );
		history.Add.Label( "History", "page-title" );
		history.Add.Label( "Your last eight calculations. Select a result to reuse it.", "page-blurb" );
		_history = history.Add.Panel( "calculator-history-items" );
		history.AddChild( new Sandbox.UI.Button( "Clear history", "delete_outline", "flatbutton", () => _history.DeleteChildren( true ) ) );
	}

	static string Format( decimal value ) => value.ToString( "0.############", CultureInfo.InvariantCulture );

	void Press( string key )
	{
		if ( key == "C" || _error ) { _entry = "0"; _operator = null; _accumulator = 0; _newEntry = true; _error = false; _expression.Text = "Ready"; }
		try
		{
			if ( key.Length == 1 && char.IsAsciiDigit( key[0] ) )
			{
				if ( _newEntry ) { _entry = "0"; _newEntry = false; }
				if ( _entry.Length < 15 ) _entry = _entry == "0" ? key : _entry + key;
			}
			else if ( key == "." )
			{
				if ( _newEntry ) { _entry = "0"; _newEntry = false; }
				if ( !_entry.Contains( '.' ) ) _entry += ".";
			}
			else if ( key == "⌫" ) { _entry = _entry.Length > 1 ? _entry[..^1] : "0"; if ( _entry == "-" ) _entry = "0"; }
			else if ( key == "±" ) _entry = Format( -decimal.Parse( _entry, CultureInfo.InvariantCulture ) );
			else if ( key == "%" ) _entry = Format( decimal.Parse( _entry, CultureInfo.InvariantCulture ) / 100 );
			else if ( key == "=" || key is "+" or "−" or "×" or "÷" )
			{
				var value = decimal.Parse( _entry, CultureInfo.InvariantCulture );
				if ( _operator is not null && !_newEntry )
				{
					var expression = $"{Format( _accumulator )} {_operator} {Format( value )}";
					value = _operator switch { "+" => _accumulator + value, "−" => _accumulator - value, "×" => _accumulator * value, _ => _accumulator / value };
					_entry = Format( value );
					_expression.Text = expression + " =";
					Remember( expression, _entry );
				}
				_accumulator = value;
				_operator = key == "=" ? null : key;
				_newEntry = true;
				if ( _operator is not null ) _expression.Text = $"{_entry} {_operator}";
			}
		}
		catch ( DivideByZeroException ) { _entry = "Cannot divide by zero"; _error = true; }
		catch ( OverflowException ) { _entry = "Number too large"; _error = true; }
		_display.Text = _entry;
		_display.SetClass( "error", _error );
	}

	void Remember( string expression, string result )
	{
		var button = new Sandbox.UI.Button( $"{expression} = {result}", () =>
		{
			_entry = result; _operator = null; _newEntry = true; _error = false;
			_display.Text = result; _display.RemoveClass( "error" ); _expression.Text = "From history"; Focus();
		} );
		button.AddClass( "flatbutton" );
		_history.AddChild( button );
		if ( _history.Children.Count() > 8 ) _history.Children.First().Delete( true );
	}

	public override void OnKeyTyped( char key )
	{
		if ( char.IsAsciiDigit( key ) || key is '.' or '+' or '-' or '*' or '/' or '=' or '%' )
			Press( key switch { '-' => "−", '*' => "×", '/' => "÷", _ => key.ToString() } );
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Button == "enter" ) Press( "=" );
		else if ( e.Button == "backspace" ) Press( "⌫" );
		else if ( e.Button == "escape" ) Press( "C" );
		else return;
		e.StopPropagation = true;
	}
}
