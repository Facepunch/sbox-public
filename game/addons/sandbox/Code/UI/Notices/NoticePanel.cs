namespace Sandbox.UI;

public class NoticePanel : Panel
{
	/// <summary>
	/// How long the slide in / slide out takes. You need to manually sync this timing with NoticePanel scss file!!
	/// </summary>
	const float SlideDuration = 0.3f;

	readonly Panel _fill;

	bool _leaving;
	RealTimeSince _sinceLeaving;

	public RealTimeUntil TimeUntilDie;

	/// <summary>
	/// How long the notice lives for. Zero will make the notification stay visible until it is manually dismissed
	/// </summary>
	public float Duration { get; }

	/// <summary>
	/// If true, the notice won't be automatically dismissed. Call Dismiss() to remove it.
	/// </summary>
	public bool Manual { get; private set; }

	public bool IsDead => !Manual && TimeUntilDie < 0;

	public NoticePanel( string icon, Color iconColor, string text, float seconds )
	{
		var body = Add.Panel( "body" );

		if ( !string.IsNullOrEmpty( icon ) )
		{
			var iconLabel = new Label() { Text = icon, Classes = "icon" };
			body.AddChild( iconLabel );
			iconLabel.Style.FontColor = iconColor;
		}

		body.AddChild( new Label() { Tokenize = false, Text = text, Classes = "text", IsRich = text?.Contains( '<' ) == true } );

		var bar = Add.Panel( "bar" );
		_fill = bar.Add.Panel( "fill" );

		Duration = MathF.Max( seconds, 0 );
		Manual = seconds <= 0;
		if ( Manual )
			AddClass( "manual" );
		else
			TimeUntilDie = seconds;

	}

	/// <summary>
	/// Dismiss a manual notice, causing it to slide out and be deleted.
	/// </summary>
	public void Dismiss()
	{
		Manual = false;
		RemoveClass( "manual" );
		TimeUntilDie = 0;
	}

	public override void Tick()
	{
		base.Tick();

		if ( IsDead && !_leaving )
		{
			_leaving = true;
			_sinceLeaving = 0;
			AddClass( "leaving" );
		}

		if ( _leaving && _sinceLeaving >= SlideDuration )
		{
			Delete();
			return;
		}

		UpdateBar();
	}

	void UpdateBar()
	{
		if ( Manual || Duration <= 0 )
			return;

		var left = MathX.Clamp( (float)TimeUntilDie.Relative / Duration, 0f, 1f );
		_fill.Style.Width = Length.Percent( left * 100 );
	}
}
