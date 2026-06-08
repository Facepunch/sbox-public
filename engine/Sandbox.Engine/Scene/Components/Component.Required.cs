namespace Sandbox;

public partial class Component
{
	/// <summary>
	/// Check all of our properties for a [RequireComponent] attribute. 
	/// If we find one, and the property is null, try to find one or create one.
	/// Runs in the editor as well as in game.
	/// </summary>
	void CheckRequireComponent()
	{
		var type = Game.TypeLibrary.GetType( GetType() );

		foreach ( var prop in ReflectionQueryCache.RequiredComponentMembers( GetType() ) )
		{
			if ( prop.PropertyType.IsAssignableTo( typeof( Component ) ) )
			{
				GetOrCreateRequiredComponent( prop );
			}
		}
	}

	private void GetOrCreateRequiredComponent( PropertyDescription prop )
	{
		var val = prop.GetValue( this );
		if ( val is not null ) return;

		var findMode = prop.GetCustomAttribute<RequireComponentAttribute>()?.FindMode ?? FindMode.EverythingInSelf;
		var c = Components.Get( prop.PropertyType, findMode );
		if ( c is not null )
		{
			prop.SetValue( this, c );
			return;
		}

		// Missing in self, so create it. We try to match GetOrCreate's behaviour
		{
			var typeDesc = Game.TypeLibrary.GetType( prop.PropertyType );
			prop.SetValue( this, Components.Create( typeDesc ) );
		}
	}
}
