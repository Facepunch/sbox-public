namespace Sandbox;

/// <summary>
/// All API responses should derive from this, which allows us to inject generic api data
/// </summary>
public class BaseApiResponse
{
	public double Milliseconds { get; set; }
}
