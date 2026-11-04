namespace Sandbox;

/// <summary>
/// Automatically synchronize a property of a networked object from the owner to other clients.
/// </summary>
[AttributeUsage( AttributeTargets.Property )]
[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.WrapPropertySet, "__sync_SetValue" )]
[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.WrapPropertyGet, "__sync_GetValue" )]
public class SyncAttribute : Attribute
{
	/// <summary>
	/// Query this value for changes rather than counting on set being called.
	/// Retained for compatibility; prefer assigning synchronized values through their property setters.
	///
	/// Obsoleted: 13/12/2024
	/// </summary>
	[Obsolete( "Query polling is obsolete. Assign synchronized values through their property setters instead." )]
	public bool Query
	{
		set
		{
#pragma warning disable CS0618 // Preserve the legacy property while Query remains supported.
			if ( value )
				Flags |= SyncFlags.Query;
			else
				Flags &= ~SyncFlags.Query;
#pragma warning restore CS0618
		}
	}

	/// <summary>
	/// Flags that describe how this property is synchronized.
	/// </summary>
	public SyncFlags Flags { get; set; }

	public SyncAttribute( SyncFlags flags )
	{
		Flags = flags;
	}

	public SyncAttribute()
	{

	}
}
