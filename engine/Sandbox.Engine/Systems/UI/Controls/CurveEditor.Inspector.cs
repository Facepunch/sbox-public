using Sandbox.UI.Construct;
using System.Globalization;

namespace Sandbox.UI;

public partial class CurveEditor
{
	/// <summary>
	/// Selection-specific controls; owns its layout and presentation state.
	/// </summary>
	sealed class CurveKeyInspector : Toolbar
	{
		readonly CurveEditor _editor;
		readonly Label _title;
		readonly Button _tangents, _smooth;
		readonly NumberEntry Time;
		readonly NumberEntry Value;

		public CurveKeyInspector( CurveEditor editor )
		{
			_editor = editor;
			AddClass( "curve-inspector" );
			Vertical = true;
			_title = Add.Label( "Key", "curve-inspector-title" );
			NumberEntry AddField( string label )
			{
				var field = Add.Panel( "curve-key-field" );
				field.Add.Label( label );
				return field.AddChild<NumberEntry>();
			}
			Time = AddField( "Time" );
			Value = AddField( "Value" );
			Time.Tooltip = "Key time; moves selected keys together";
			Value.Tooltip = "Key value; moves selected keys together";
			Time.OnTextEdited = text => editor.EditNumber( text, true );
			Value.OnTextEdited = text => editor.EditNumber( text, false );
			foreach ( var entry in new[] { Time, Value } )
			{
				entry.AddEventListener( "onfocus", editor.BeginEdit );
				entry.AddEventListener( "onblur", () => { editor.EndEdit(); editor.Sync(); } );
			}
			AddSeparator();
			var menu = new Menu();
			foreach ( var mode in Enum.GetValues<Curve.HandleMode>() )
				menu.AddOption( mode.ToString(), null, () => editor.SetSelectedMode( mode ) );
			_tangents = AddMenu( "Tangents", null, menu );
			_tangents.AddClass( "curve-tangent-mode" );
			_tangents.Tooltip = "Interpolation of selected keys";
			_smooth = AddButton( "Smooth", null, editor.SmoothSelected );
			_smooth.Tooltip = "Smooth selected tangents without overshoot";
		}

		public void Sync()
		{
			bool selected = _editor._selection.Count > 0;
			_title.Text = selected ? $"{_editor.ChannelName( _editor.ActiveCurve )} · Key {_editor.SelectedIndex + 1}" : "Select a key";
			Time.Disabled = Value.Disabled = _tangents.Disabled = _smooth.Disabled = !selected;
			var modes = _editor._selection.Select( x => _editor._curves[x.CurveIndex].Frames[x.KeyIndex].Mode ).Distinct().ToArray();
			_tangents.Text = modes.Length == 1 ? modes[0].ToString() : "Tangents";
			if ( _editor.SelectedIndex < 0 )
			{
				Time.Text = "";
				Value.Text = "";
				return;
			}
			var key = _editor.ActiveCurveValue.Frames[_editor.SelectedIndex];
			if ( !Time.HasFocus )
				Time.Text = (_editor.ActiveCurveValue.TimeRange.x + key.Time * (_editor.ActiveCurveValue.TimeRange.y - _editor.ActiveCurveValue.TimeRange.x)).ToString( "0.###", CultureInfo.InvariantCulture );
			if ( !Value.HasFocus )
				Value.Text = (_editor.ActiveCurveValue.ValueRange.x + key.Value * (_editor.ActiveCurveValue.ValueRange.y - _editor.ActiveCurveValue.ValueRange.x)).ToString( "0.###", CultureInfo.InvariantCulture );
		}
	}
}
