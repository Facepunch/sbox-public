using Microsoft.AspNetCore.Components;

namespace Sandbox.UI;

public partial class CurveEditor
{
	/// <summary>
	/// A named curve and its display colour. Curves keep their own time/value units.
	/// </summary>
	public readonly record struct CurveChannel( string Name, Color Color, Curve Value );
	CurveChannel[] _channels;

	/// <summary>
	/// Display and edit multiple independent curves. Assigning clears undo history without notifying.
	/// </summary>
	[Parameter]
	public IReadOnlyList<CurveChannel> Channels
	{
		get => _curves.Select( ( curve, index ) => new CurveChannel( ChannelName( index ), ChannelColor( index ), curve ) ).ToArray();
		set
		{
			ArgumentNullException.ThrowIfNull( value );
			if ( value.Count == 0 )
				throw new ArgumentException( "At least one curve is required.", nameof( value ) );
			var channels = value.ToArray();
			Assign( channels.Select( x => Normalize( x.Value ) ).ToArray(), channels );
		}
	}

	/// <summary>
	/// Raised once per multi-curve edit, including undo and cancellation. The collection is a snapshot.
	/// </summary>
	[Parameter]
	public Action<IReadOnlyList<CurveChannel>> ChannelsChanged { get; set; }

	/// <summary>
	/// Show the advanced key inspector beside the canvas. Hidden by default.
	/// </summary>
	[Parameter]
	public bool ShowInspector
	{
		get => HasClass( "show-inspector" );
		set
		{
			if ( ShowInspector == value )
				return;
			if ( !value )
			{
				EndEdit();
				_canvas?.Focus();
			}
			SetClass( "show-inspector", value );
			_tools?.Sync();
		}
	}

	string ChannelName( int index ) => _channels is not null ? _channels[index].Name : IsRange ? (index == 0 ? "A" : "B") : "Curve";
	Color ChannelColor( int index ) => _channels is not null ? _channels[index].Color : _canvas.ComputedStyle?.FontColor ?? Color.White;
	bool SameChannels( CurveChannel[] channels ) => _channels is null ? channels is null
		: channels is not null && _channels.Length == channels.Length && _channels.Zip( channels ).All( x => x.First.Name == x.Second.Name && x.First.Color == x.Second.Color );

}
