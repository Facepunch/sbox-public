using System;
using System.Linq;
using Sandbox;
using Sandbox.UI;

namespace MenuProject.MenuUI.Jams;

/// <summary>
/// Draws the final's head-to-head instrumentation over the actual animated bar geometry.
/// </summary>
public sealed class JamFinalDuel : Panel
{
	/// <summary>
	/// Short-lived notice when a different contender takes the lead.
	/// </summary>
	[Parameter] public string LeadNotice { get; set; }

	/// <summary>
	/// Whether votes are still being accepted.
	/// </summary>
	[Parameter] public bool Live { get; set; }

	/// <summary>
	/// True while waiting for the result to be confirmed and revealed.
	/// </summary>
	[Parameter] public bool Counting { get; set; }

	/// <summary>
	/// Confirmed winner's package identifier; never inferred from an unfinished live tally.
	/// </summary>
	[Parameter] public string Winner { get; set; }

	/// <summary>
	/// Seconds remaining in the final, used for the last-minute instrument display.
	/// </summary>
	[Parameter] public int SecondsLeft { get; set; }

	/// <summary>
	/// Paints a VS marker, aligned comparison lines, a measured vote gap, and the winner's crown.
	/// </summary>
	public override void OnDraw( Painter painter )
	{
		base.OnDraw( painter );
		var bars = Parent.Descendants.OfType<JamVoteBar>().OrderBy( x => x.Box.Rect.Left ).ToArray();
		if ( bars.Length != 2 ) return;

		using var scope = painter.Scope();
		var scale = ScaleToScreen;
		var left = bars[0];
		var right = bars[1];
		var leftRect = left.Box.Rect;
		var rightRect = right.Box.Rect;
		var origin = Box.Rect.Position;
		var middle = (leftRect.Center.x + rightRect.Center.x) * 0.5f - origin.x;
		var bottom = leftRect.Bottom - origin.y - scale;
		var plotTop = leftRect.Top - origin.y + 32 * scale;
		var height = Math.Max( 0, bottom - plotTop );
		var leftY = bottom - height * left.DisplayedFraction;
		var rightY = bottom - height * right.DisplayedFraction;
		var leftX = leftRect.Center.x - origin.x + 48 * scale;
		var rightX = rightRect.Center.x - origin.x - 48 * scale;
		var gold = Color.Parse( "#ffd074" ) ?? Color.White;
		var ink = Color.Parse( "#8eabc7" ) ?? Color.White;
		var pulse = Live ? JamVoteBar.Heartbeat : 0;

		// Compare cap heights with horizontal projection lines and a central measurement bracket.
		painter.Stroke = Stroke.Solid( ink.WithAlpha( 0.42f ), scale );
		painter.Line( new Vector2( leftX, leftY ), new Vector2( middle - 7 * scale, leftY ) );
		painter.Line( new Vector2( middle + 7 * scale, rightY ), new Vector2( rightX, rightY ) );
		painter.Line( new Vector2( middle, leftY ), new Vector2( middle, rightY ) );
		foreach ( var y in new[] { leftY, rightY } )
		{
			painter.Line( new Vector2( middle - 7 * scale, y ), new Vector2( middle + 7 * scale, y ) );
		}
		var high = Math.Min( leftY, rightY );
		var low = Math.Max( leftY, rightY );
		for ( var y = high + 8 * scale; y < low - 4 * scale; y += 8 * scale )
		{
			painter.Line( new Vector2( middle - 3 * scale, y ), new Vector2( middle + 3 * scale, y ) );
		}

		var difference = Math.Abs( left.Votes - right.Votes );
		var total = left.Votes + right.Votes;
		var gap = total > 0 ? difference * 100f / total : 0;
		Text( painter, difference == 0 ? "LEVEL" : $"Δ {difference:N0}", middle + 34 * scale,
			(high + low) * 0.5f, 60 * scale, 11 * scale, gold );

		// Keep the identity marker beneath the comparison bracket, clear of both count labels.
		var centerY = Math.Max( plotTop + height * 0.57f, low + 64 * scale );
		centerY = Math.Min( centerY, bottom - 94 * scale );
		var radius = 34 * scale;
		painter.RectShadow( new Rect( middle - radius, centerY - radius, radius * 2, radius * 2 ),
			8 * scale, gold.WithAlpha( 0.07f + pulse * 0.025f ), 20 * scale );
		painter.Fill = (Color.Parse( "#111923" ) ?? Color.Black).WithAlpha( 0.9f );
		painter.Stroke = Stroke.Solid( gold.WithAlpha( 0.4f + pulse * 0.16f ), scale );
		painter.Rect( new Rect( middle - radius, centerY - radius, radius * 2, radius * 2 ), 8 * scale );
		painter.Stroke = Stroke.Solid( ink.WithAlpha( 0.23f ), scale );
		painter.Line( new Vector2( leftX, centerY ), new Vector2( middle - radius - 10 * scale, centerY ) );
		painter.Line( new Vector2( middle + radius + 10 * scale, centerY ), new Vector2( rightX, centerY ) );
		Text( painter, Counting ? "···" : "VS", middle, centerY, 70 * scale, 29 * scale, gold * (1.25f + pulse * 0.2f) );

		var caption = Winner is not null ? "FINAL RESULT" : Counting ? "VERIFYING RESULT" : difference == 0 ? "DEAD HEAT" : "VOTE ADVANTAGE";
		Text( painter, caption, middle, centerY + 52 * scale, 220 * scale, 10 * scale, ink );
		Text( painter, difference == 0 ? "TIED" : $"{(left.Votes > right.Votes ? "←" : "→")}  {difference:N0} VOTES", middle,
			centerY + 76 * scale, 220 * scale, 19 * scale, gold );
		Text( painter, $"{gap:0.0} POINT MARGIN", middle, centerY + 98 * scale, 220 * scale, 10 * scale, ink );

		if ( !string.IsNullOrEmpty( LeadNotice ) )
		{
			Text( painter, LeadNotice, middle, plotTop - 14 * scale, 290 * scale, 12 * scale, gold * 1.4f );
		}
		else if ( Live && SecondsLeft is > 0 and <= 60 )
		{
			Text( painter, $"FINAL {SecondsLeft}s", middle, plotTop - 14 * scale, 200 * scale, 14 * scale, gold * (1 + pulse * 0.3f) );
		}

		if ( Winner is not null )
		{
			var winner = bars.FirstOrDefault( x => x.Winner );
			if ( winner is null ) return;
			var x = winner.Box.Rect.Center.x - origin.x;
			var y = bottom - height * winner.DisplayedFraction + 15 * scale;
			painter.Stroke = Stroke.Solid( gold * 1.6f, 2 * scale );
			painter.Line( new Vector2[] { new( x - 16 * scale, y ), new( x - 12 * scale, y + 16 * scale ),
				new( x + 12 * scale, y + 16 * scale ), new( x + 16 * scale, y ), new( x + 6 * scale, y + 7 * scale ),
				new( x, y - 4 * scale ), new( x - 6 * scale, y + 7 * scale ), new( x - 16 * scale, y ) } );
		}
	}

	void Text( Painter painter, string value, float x, float y, float width, float size, Color color )
	{
		// TextStyle applies the panel scale itself; geometry above is already in drawing pixels.
		painter.TextStyle = new TextStyle { FontName = "Inter", FontSize = size / ScaleToScreen, FontWeight = size >= 18 * ScaleToScreen ? 700 : 500, Color = color, Alignment = TextFlag.Center };
		painter.Text( value, new Rect( x - width * 0.5f, y - size, width, size * 2 ) );
	}
}
