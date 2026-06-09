namespace Sandbox.UI;

public abstract partial class BaseStyles
{
	/// <summary>
	/// Set when a value containing the CSS <c>currentColor</c> keyword has been parsed into this set.
	/// currentColor resolves to the element's own computed font colour, which isn't known until the
	/// cascade has run, so we flag it here and resolve in <see cref="ResolveCurrentColor(BaseStyles)"/> after
	/// FillDefaults. Stays false otherwise so there's no cost for the common case.
	/// </summary>
	internal bool HasCurrentColor;

	/// <summary>
	/// Replaces any colour field holding the <see cref="Color.CurrentColor"/> sentinel with the
	/// element's computed font colour. <c>color: currentColor</c> itself means the inherited colour,
	/// so the font colour resolves against the parent. Must run after FillDefaults so the font colour
	/// is final.
	/// </summary>
	internal void ResolveCurrentColor( BaseStyles parent )
	{
		// 'color: currentColor' resolves to the inherited colour (the parent's, or the default).
		if ( _fontcolor.HasValue && Color.IsCurrentColor( _fontcolor.Value ) )
			_fontcolor = parent != null ? parent._fontcolor : InitialValues._fontcolor;

		var current = _fontcolor ?? Color.Black;

		ResolveCurrentColor( ref _backgroundcolor, current );
		ResolveCurrentColor( ref _borderleftcolor, current );
		ResolveCurrentColor( ref _bordertopcolor, current );
		ResolveCurrentColor( ref _borderrightcolor, current );
		ResolveCurrentColor( ref _borderbottomcolor, current );
		ResolveCurrentColor( ref _caretcolor, current );
		ResolveCurrentColor( ref _textdecorationcolor, current );
		ResolveCurrentColor( ref _textstrokecolor, current );
		ResolveCurrentColor( ref _outlinecolor, current );
		ResolveCurrentColor( ref _filtertint, current );
		ResolveCurrentColor( ref _filterbordercolor, current );
		ResolveCurrentColor( ref _backgroundtint, current );
		ResolveCurrentColor( ref _borderimagetint, current );
	}

	/// <summary>
	/// Swaps a single colour field from the currentColor sentinel to the resolved colour.
	/// </summary>
	static void ResolveCurrentColor( ref Color? field, Color current )
	{
		if ( field.HasValue && Color.IsCurrentColor( field.Value ) )
			field = current;
	}
}
