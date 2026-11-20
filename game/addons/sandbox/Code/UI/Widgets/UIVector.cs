using Sandbox.UI;
using Sandbox.UI.Construct;

namespace Sandbox;

/// <summary>
/// A Vector2, Vector3 or Vector4 property, a number box for each axis with the axis letter in front of it.
/// </summary>
public class UIVector : BaseControl
{
	static readonly string[] Axes = { "x", "y", "z", "w" };

	readonly List<UIVectorAxis> _entries = new();

	const float ScrubSnap = 0.01f;

	float _snap = ScrubSnap;

	object _shownValue;
	int _shownFocus = -1;

	/// <summary>
	/// Whether we have a control for this type.
	/// </summary>
	public static bool Supports( Type type ) => AxisCount( type ) > 0;

	static int AxisCount( Type type )
	{
		if ( type == typeof( Vector2 ) ) return 2;
		if ( type == typeof( Vector3 ) ) return 3;
		if ( type == typeof( Vector4 ) ) return 4;
		return 0;
	}

	public override void Rebuild()
	{
		DeleteChildren( true );
		_entries.Clear();
		_shownValue = null;

		if ( Property is null ) return;

		var count = AxisCount( Property.PropertyType );
		var hasRange = Property.TryGetAttribute<RangeAttribute>( out var range );
		_snap = hasRange ? MathF.Min( ScrubSnap, (range.Max - range.Min) / 10f ) : ScrubSnap;

		for ( int i = 0; i < count; i++ )
		{
			var entry = AddChild<UIVectorAxis>( $"axis {Axes[i]}" );
			entry.Prefix = Axes[i].ToUpperInvariant();
			entry.NumberFormat = "0.###";

			if ( hasRange )
			{
				entry.MinValue = range.Min;
				entry.MaxValue = range.Max;
			}

			var index = i;
			entry.OnTextEdited = text => SetAxis( index, text, !entry.HasFocus );

			_entries.Add( entry );
		}

		// a Vector2 keeps the boxes as wide as the ones of a Vector3, so the rows of a sheet line up
		if ( count == 2 )
			Add.Panel( "axis filler" );
	}

	public override void Tick()
	{
		base.Tick();

		if ( Property is null || _entries.Count == 0 ) return;

		// only touch the boxes when the value or the box being typed in changes
		var value = Property.GetValue<object>();
		var focused = _entries.FindIndex( x => x.HasFocus );
		if ( Equals( value, _shownValue ) && focused == _shownFocus ) return;

		_shownValue = value;
		_shownFocus = focused;

		var values = Read();

		for ( int i = 0; i < _entries.Count; i++ )
		{
			var entry = _entries[i];
			if ( entry.HasFocus ) continue;

			var text = values[i].ToString( entry.NumberFormat );
			if ( entry.Text != text )
				entry.Text = text;
		}
	}

	void SetAxis( int index, string text, bool scrubbed )
	{
		var values = Read();
		var value = text.ToFloat( values[index] );

		if ( scrubbed && _snap > 0f )
			value = MathF.Round( value / _snap ) * _snap;

		values[index] = value;
		Write( values );
	}

	float[] Read()
	{
		var type = Property.PropertyType;

		if ( type == typeof( Vector2 ) )
		{
			var v = Property.GetValue<Vector2>();
			return [v.x, v.y];
		}

		if ( type == typeof( Vector3 ) )
		{
			var v = Property.GetValue<Vector3>();
			return [v.x, v.y, v.z];
		}

		var w = Property.GetValue<Vector4>();
		return [w.x, w.y, w.z, w.w];
	}

	void Write( float[] values )
	{
		var type = Property.PropertyType;

		if ( type == typeof( Vector2 ) )
			Property.SetValue( new Vector2( values[0], values[1] ) );
		else if ( type == typeof( Vector3 ) )
			Property.SetValue( new Vector3( values[0], values[1], values[2] ) );
		else
			Property.SetValue( new Vector4( values[0], values[1], values[2], values[3] ) );
	}
}

/// <summary>
/// One axis of a <see cref="UIVector"/>.
/// </summary>
public class UIVectorAxis : NumberEntry
{
	Panel _cover;

	public UIVectorAxis()
	{
		FocusOnClick = false;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		// the same press NumberEntry starts its scrub on
		var scrub = CanEdit && e.Button == "mouseleft" && e.Target == PrefixLabel;

		if ( e.Target != PrefixLabel )
			Focus();

		base.OnMouseDown( e );

		if ( scrub ) StartDrag();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );
		EndDrag();
	}

	public override void Tick()
	{
		base.Tick();

		// the press can end without a mouse up reaching us, the cover never outlives it
		if ( _cover is not null && !HasActive )
			EndDrag();
	}

	public override void OnDeleted()
	{
		base.OnDeleted();
		EndDrag();
	}

	void StartDrag()
	{
		EndDrag();

		var root = FindRootPanel();
		if ( root is null ) return;

		_cover = root.Add.Panel( "vector-drag-cover" );
		_cover.Style.Set( "position: fixed; left: 0px; top: 0px; right: 0px; bottom: 0px; pointer-events: all; cursor: ew-resize; z-index: 100000;" );
	}

	void EndDrag()
	{
		_cover?.Delete( true );
		_cover = null;
	}
}
