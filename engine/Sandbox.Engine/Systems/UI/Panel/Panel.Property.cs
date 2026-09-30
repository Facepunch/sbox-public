namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// True when a parameter has changed and OnParametersSet is pending.
	/// </summary>
	bool parametersChanged = true;
	Task parametersSetTask;

	internal void ParametersChanged( bool immediately )
	{
		parametersChanged = true;

		// task is still running
		if ( parametersSetTask != null && !parametersSetTask.IsCompleted )
			return;

		if ( immediately )
		{
			parametersChanged = false;

			parametersSetTask = OnParametersSetInternalAsync();
		}
	}

	internal async Task OnParametersSetInternalAsync()
	{
		try
		{
			await OnParametersSetAsync();
		}
		catch ( OperationCanceledException )
		{
			return;
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception in OnParametersSetAsync: {e.Message}" );
		}

		if ( !IsValid )
			return;

		try
		{
			OnParametersSet();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception in OnParametersSet: {e.Message}" );
		}

		StateHasChanged();
	}

	/// <summary>
	/// Does nothing. Left over from the template system.
	/// </summary>
	[Obsolete( "Leftover from the template system. Use SetProperty." )]
	public virtual void SetPropertyObject( string name, object value )
	{
	}

	string previousPropertyClass;

	/// <summary>
	/// Set a property on the panel, such as special properties (<c>class</c>, <c>id</c>, <c>style</c> and <c>value</c>, etc.) and properties of the panel's C# class.
	/// </summary>
	/// <param name="name">name of the property to modify.</param>
	/// <param name="value">Value to assign to the property.</param>
	public virtual void SetProperty( string name, string value )
	{
		if ( name == "id" )
		{
			Id = value;
			return;
		}

		if ( name == "value" )
		{
#pragma warning disable CS0618
			StringValue = value;
#pragma warning restore CS0618
			return;
		}

		if ( name == "class" )
		{
			if ( !string.IsNullOrEmpty( previousPropertyClass ) )
			{
				RemoveClass( previousPropertyClass );
			}

			previousPropertyClass = value;
			AddClass( value );
			return;
		}

		if ( name == "style" )
		{
			Style.Set( value );
			return;
		}

		SetAttribute( name, value );

		Game.TypeLibrary.SetProperty( this, name, value );
	}

	Dictionary<string, string> _attributes;

	/// <summary>
	/// Stores an attribute value by name. Every attribute set via <see cref="SetProperty"/> lands here.
	/// </summary>
	public void SetAttribute( string k, string v )
	{
		if ( string.IsNullOrEmpty( k ) ) return;

		_attributes ??= [];
		_attributes[k] = v;
	}

	/// <summary>
	/// Gets an attribute value by name, or <paramref name="defaultIfNotFound"/> if it was never set.
	/// </summary>
	public string GetAttribute( string k, string defaultIfNotFound = default )
	{
		if ( _attributes == null ) return defaultIfNotFound;

		if ( _attributes.TryGetValue( k, out var v ) )
			return v;

		return defaultIfNotFound;
	}

	/// <summary>
	/// Called after the razor parameters on this panel have been set or changed.
	/// </summary>
	protected virtual void OnParametersSet()
	{
		//Log.Info( $"{this} - OnParametersSet" );
	}

	/// <summary>
	/// Called after the razor parameters on this panel have been set or changed, before <see cref="OnParametersSet"/>.
	/// </summary>
	protected virtual Task OnParametersSetAsync()
	{
		return Task.CompletedTask;
	}

	/// <summary>
	/// Called by the razor renderer when an element has text content between its tags.
	/// </summary>
	public virtual void SetContent( string value )
	{

	}
}
