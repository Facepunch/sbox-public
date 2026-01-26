
using System.Threading;

namespace Sandbox.UI;

public partial class Panel
{
	internal double TimeNow => PanelRealTime.TimeNow;
	internal double TimeDelta => PanelRealTime.TimeDelta;

	/// <summary>
	/// Can be used to store random data without sub-classing the panel.
	/// </summary>
	[Hide]
	public object UserData { get; set; }


	CancellationTokenSource _deleteTokenSource;

	/// <summary>
	/// Get a token that is cancelled when the panel is deleted
	/// </summary>
	[Hide]
	public CancellationToken DeletionToken
	{
		get
		{
			if ( IsDeleting || !IsValid )
				return CancellationToken.None;

			_deleteTokenSource ??= new CancellationTokenSource();
			return _deleteTokenSource.Token;
		}
	}
}

static class PanelRealTime
{
	public static double TimeNow;
	public static double TimeDelta;

	public static void Update()
	{
		var delta = RealTime.Delta;

		// If we're running lower than 30fps, clamp it to avoid weirdness
		delta = delta.Clamp( 0.000f, 1.0f / 30.0f );

		TimeDelta = delta;
		TimeNow += TimeDelta;
	}
}
