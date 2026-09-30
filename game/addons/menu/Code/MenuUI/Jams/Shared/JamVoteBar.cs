using Sandbox;
using Sandbox.UI;
using Sandbox.UI.Construct;
using Sandbox.Utility;

namespace MenuProject.MenuUI.Jams;

/// <summary>
/// One finalist's zero-based vote bar. Every column receives the same maximum and height.
/// </summary>
public sealed class JamVoteBar : Panel
{
	readonly Label count;
	readonly Label change;
	int lastVotes = -1;
	int lastMaximum = -1;
	string lastIdentity;
	float displayedVotes;
	float displayedFraction;
	float fromVotes;
	float fromFraction;
	float entranceDelay;
	int voteDelta;
	RealTimeSince motionAge;
	RealTimeSince pulseAge = 100;

	/// <summary>
	/// Keeps the count in the ordinary label layer above the painted chart.
	/// </summary>
	public JamVoteBar()
	{
		var plot = Add.Panel( "bar-labels" );
		count = plot.Add.Label( "", "bar-count" );
		change = plot.Add.Label( "", "bar-change" );
	}

	/// <summary>
	/// The current absolute vote count.
	/// </summary>
	[Parameter] public int Votes { get; set; }

	/// <summary>
	/// The vote percentage displayed vertically beside the bar.
	/// </summary>
	[Parameter] public string VoteShare { get; set; }

	/// <summary>
	/// The common upper bound for the entire gallery.
	/// </summary>
	[Parameter] public int Maximum { get; set; }

	/// <summary>
	/// Marks every game tied for the lowest nonempty-round count.
	/// </summary>
	[Parameter] public bool Lowest { get; set; }

	/// <summary>
	/// Highlights the local player's selected game.
	/// </summary>
	[Parameter] public bool Selected { get; set; }

	/// <summary>
	/// Adds a gentle heartbeat while the round is open.
	/// </summary>
	[Parameter] public bool Live { get; set; }

	/// <summary>
	/// Starts both final bars together and adds restrained urgency near the deadline.
	/// </summary>
	[Parameter] public bool GrandFinal { get; set; }

	/// <summary>
	/// Strengthens the heartbeat during the final minute while voting is open.
	/// </summary>
	[Parameter] public bool Urgent { get; set; }

	/// <summary>
	/// Highlights the confirmed winner after the result hold.
	/// </summary>
	[Parameter] public bool Winner { get; set; }

	/// <summary>
	/// Pulses when this contender takes the lead, independently of ordinary vote changes.
	/// </summary>
	[Parameter] public bool LeadChange { get; set; }

	/// <summary>
	/// The animated geometry fraction, shared with the final's comparison drawing.
	/// </summary>
	internal float DisplayedFraction => displayedFraction;

	/// <summary>
	/// Shared one-second rhythm for every bar and the gallery's live indicator.
	/// </summary>
	internal static float Heartbeat
	{
		get
		{
			var phase = RealTime.Now % 1f;
			return phase < 0.3f ? System.MathF.Pow( System.MathF.Sin( phase / 0.3f * System.MathF.PI ), 2 ) : 0;
		}
	}

	/// <summary>
	/// Identifies the entry and round so switching categories never looks like a vote change.
	/// </summary>
	[Parameter] public string Identity { get; set; }

	/// <summary>
	/// Stable slate order, used for a short stagger when the gallery first appears.
	/// </summary>
	[Parameter] public int Seed { get; set; }

	/// <summary>
	/// Eases toward real counts and distinguishes increases from decreases without fabricating activity.
	/// </summary>
	public override void Tick()
	{
		base.Tick();
		if ( lastVotes < 0 || lastIdentity != Identity )
		{
			lastIdentity = Identity;
			lastVotes = Votes;
			lastMaximum = Maximum;
			fromVotes = 0;
			fromFraction = 0;
			entranceDelay = GrandFinal ? 0 : System.Math.Clamp( Seed - 1, 0, 4 ) * 0.07f;
			motionAge = 0;
			pulseAge = 100;
			voteDelta = 0;
		}
		else if ( lastVotes != Votes || lastMaximum != Maximum )
		{
			if ( lastVotes != Votes )
			{
				voteDelta = Votes - lastVotes;
				change.Text = voteDelta > 0 ? $"+{voteDelta:N0}" : $"−{System.Math.Abs( voteDelta ):N0}";
				pulseAge = 0;
			}

			fromVotes = displayedVotes;
			fromFraction = displayedFraction;
			lastVotes = Votes;
			lastMaximum = Maximum;
			entranceDelay = 0;
			motionAge = 0;
		}

		var progress = System.Math.Clamp( ((float)motionAge - entranceDelay) / 0.8f, 0, 1 );
		var eased = Easing.CubicOut( progress );
		var target = System.Math.Clamp( Votes / (float)System.Math.Max( 1, Maximum ), 0, 1 );
		displayedVotes = fromVotes.LerpTo( Votes, eased );
		displayedFraction = fromFraction.LerpTo( target, eased );
		count.Text = ((int)System.MathF.Round( displayedVotes )).ToString( "N0" );

		// Both labels follow the painted top through animation and viewport changes.
		count.Style.Bottom = Length.Fraction( displayedFraction );
		change.Style.Bottom = Length.Fraction( displayedFraction );
		change.Style.Opacity = System.Math.Clamp( 1 - (float)pulseAge / 1.4f, 0, 1 );
		SetClass( "gaining", voteDelta > 0 && pulseAge < 1.4f );
		SetClass( "losing", voteDelta < 0 && pulseAge < 1.4f );
	}

	/// <summary>
	/// Paints a luminous bar, with a short width and colour pulse when its real tally changes.
	/// </summary>
	public override void OnDraw( Painter painter )
	{
		base.OnDraw( painter );
		using var scope = painter.Scope();
		var width = Box.Rect.Width;
		var bottom = Box.Rect.Height - ScaleToScreen;
		var top = 32 * ScaleToScreen;
		var height = System.Math.Max( 0, bottom - top );
		var barHeight = height * displayedFraction;
		var heartbeat = Live ? Heartbeat * (Urgent ? 1.5f : 1) : 0;
		var envelope = System.Math.Clamp( 1 - (float)pulseAge / 1.1f, 0, 1 );
		var pulse = envelope * (0.65f + 0.35f * System.MathF.Cos( (float)pulseAge * 18 ));
		var barWidth = System.Math.Min( width * 0.4f, 84 * ScaleToScreen ) * (1 + pulse * 0.12f);

		const int gridDivisions = 10;
		for ( var i = 0; i <= gridDivisions; i++ )
		{
			painter.Stroke = Stroke.Solid( Color.White.WithAlpha( i % 5 == 0 ? 0.12f : 0.06f ), ScaleToScreen );
			var y = top + height * i / gridDivisions;
			painter.Line( new Vector2( 0, y ), new Vector2( width, y ) );
		}

		painter.Stroke = Stroke.None;
		var baseColor = Color.Parse( Winner ? "#ffd074" : Lowest ? "#ff7795" : Selected ? "#398eff" : "#61a9ff" ) ?? Color.White;
		var pulseColor = Color.Parse( voteDelta >= 0 ? "#76ffd2" : "#ff668a" ) ?? Color.White;
		var color = Color.Lerp( baseColor, pulseColor, pulse );
		var bar = new Rect( (width - barWidth) / 2, bottom - barHeight, barWidth, barHeight );

		if ( barHeight > 0 )
		{
			var glowPulse = pulse * 0.2f + (LeadChange ? Heartbeat * 0.12f : 0);
			painter.RectShadow( bar, 5 * ScaleToScreen, color.WithAlpha( 0.12f + heartbeat * 0.02f + glowPulse * 0.4f ), 18 * ScaleToScreen );
			// HDR highlights bloom in the menu, with extra energy during vote changes.
			painter.Fill = Fill.LinearGradient( (color * (1 + glowPulse * 3)).WithAlpha( 1 ), color * 0.55f, 90 );
			painter.Rect( bar, 5 * ScaleToScreen );
			painter.Fill = (Color.Lerp( color, Color.White, 0.45f ) * (3 + heartbeat * 0.6f + glowPulse * 7)).WithAlpha( 1 );
			painter.Rect( new Rect( bar.Left + 3 * ScaleToScreen, bar.Top, bar.Width - 6 * ScaleToScreen,
				System.Math.Min( 2 * ScaleToScreen, barHeight ) ), ScaleToScreen );
		}

		// Follow the bar's centre, leaving enough room for the label even with very few votes.
		painter.Translate( bar.Right + 14 * ScaleToScreen, bottom - System.Math.Max( barHeight * 0.5f, 60 * ScaleToScreen ) );
		painter.Rotate( -90 );
		painter.TextStyle = new TextStyle
		{
			FontName = "Inter",
			FontSize = 12,
			Color = Color.Parse( "#8d94a3" ) ?? Color.White,
			Alignment = TextFlag.Center
		};
		painter.Text( VoteShare ?? "", new Rect( -60 * ScaleToScreen, -10 * ScaleToScreen, 120 * ScaleToScreen, 20 * ScaleToScreen ) );
	}
}
