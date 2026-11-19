using System;

namespace ScriptingTests;

/// <summary>
/// Exercises registration and lifecycle pairing for runtime-owned descriptions.
/// </summary>
[Expose, AttributeUsage( AttributeTargets.Class )]
public sealed class ScriptRegistrationProbeAttribute : Attribute, ITypeAttribute
{
	/// <inheritdoc />
	public Type TargetType { get; set; }

	/// <summary>
	/// Unregister callbacks observed by the test.
	/// </summary>
	public static int Unregistrations { get; set; }

	/// <summary>
	/// Simulates a notification which fails before registration completes.
	/// </summary>
	public static bool FailRegistration { get; set; }

	/// <inheritdoc />
	public void TypeRegister()
	{
		if ( FailRegistration )
		{
			throw new InvalidOperationException( "Registration probe failure." );
		}
	}

	/// <inheritdoc />
	public void TypeUnregister()
	{
		Unregistrations++;
	}
}
