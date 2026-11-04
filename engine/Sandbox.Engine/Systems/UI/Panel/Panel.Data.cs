
namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// Set via the <c>value</c> attribute. Nothing reads it.
	/// </summary>
	[Hide, Obsolete( "Leftover from the template system. Nothing reads this." )]
	public virtual string StringValue { get; set; }

	/// <summary>
	/// Call this when the value has changed due to user input etc. Triggers a $"{name}.changed"
	/// event with the value on the event.
	/// </summary>
	protected void CreateValueEvent( string name, object value )
	{
		CreateEvent( $"{name}.changed", value );
	}
}
