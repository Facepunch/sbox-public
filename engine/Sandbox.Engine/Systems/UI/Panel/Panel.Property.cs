using System.Reflection;

namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// True when a bind has changed and OnParametersSet call is pending a call
	/// </summary>

	bool templateBindsChanged = true;
	Task parametersSetTask;

	internal void ParametersChanged( bool immediately )
	{
		templateBindsChanged = true;

		// task is still running
		if ( parametersSetTask != null && !parametersSetTask.IsCompleted )
			return;

		if ( immediately )
		{
			templateBindsChanged = false;

			parametersSetTask = OnParametersSetInternalAsync();
		}
	}

	internal async Task OnParametersSetInternalAsync()
	{
		try
		{
			await OnParametersSetAsync();
		}
		catch ( TaskCanceledException )
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
	/// Same as <see cref="SetProperty"/>, but first tries to set the property on the panel object, then process any special properties such as <c>class</c>.
	/// </summary>
	/// <inheritdoc cref="SetProperty"/>
	public virtual void SetPropertyObject( string name, object value )
	{
		var prop = GetType().GetProperty( name, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy );

		if ( prop != null && prop.PropertyType.IsAssignableFrom( value?.GetType() ) )
		{
			prop.SetValue( this, value );
			return;
		}

		SetProperty( name, Convert.ToString( value ) );
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
			StringValue = value;
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

	Dictionary<string, string> Attributes;

	/// <summary>
	/// Used in templates, gets an attribute that was set in the template.
	/// </summary>
	public void SetAttribute( string k, string v )
	{
		Attributes ??= new();
		Attributes[k] = v;
	}

	/// <summary>
	/// Used in templates, try to get the attribute that was set in creation.
	/// </summary>
	public string GetAttribute( string k, string defaultIfNotFound = default )
	{
		if ( Attributes == null ) return defaultIfNotFound;

		if ( Attributes.TryGetValue( k, out var v ) )
			return v;

		return defaultIfNotFound;
	}

	/// <summary>
	/// Called after all templated panel binds have been set.
	/// </summary>
	protected virtual void OnParametersSet()
	{
		//Log.Info( $"{this} - OnParametersSet" );
	}

	/// <summary>
	/// Called after all templated panel binds have been set.
	/// </summary>
	protected virtual Task OnParametersSetAsync()
	{
		return Task.CompletedTask;
	}

	/// <summary>
	/// Called by the templating system when an element has content between its tags.
	/// </summary>
	public virtual void SetContent( string value )
	{

	}
}
