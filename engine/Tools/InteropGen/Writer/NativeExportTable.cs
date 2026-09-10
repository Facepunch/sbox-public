using System.Collections.Generic;

namespace Facepunch.InteropGen;

internal enum NativeSlotKind
{
	/// <summary> Slot 0: the native error/abort callback. </summary>
	Error,

	/// <summary> A dynamic_cast from a base class to a derived class. </summary>
	CastFromTo,

	/// <summary> A dynamic_cast from a derived class back to a base class. </summary>
	CastToFrom,

	/// <summary> A native function exported to managed. </summary>
	Function,

	/// <summary> A native variable's getter. </summary>
	VariableGet,

	/// <summary> A native variable's setter. </summary>
	VariableSet,
}

/// <summary>
/// One entry in the native export function-pointer table.
/// </summary>
internal sealed record NativeSlot( NativeSlotKind Kind, Class Class, Class BaseClass, Function Function, Variable Variable );

/// <summary>
/// The single source of truth for the native export ("nativeFunctions") table layout. The C++ side
/// (igen_ initializer) fills each slot with a pointer; the managed side reads each slot back into a
/// delegate field; and the array size is just the slot count. Driving all three from this one ordered
/// enumeration makes "size == order" true by construction, so the two emitters can never drift out of
/// sync (which would silently misalign function pointers at runtime).
/// </summary>
internal static class NativeExportTable
{
	public static IEnumerable<NativeSlot> Slots( Definition definition, SkipPolicy skip )
	{
		yield return new NativeSlot( NativeSlotKind.Error, null, null, null, null );

		foreach ( Class c in definition.NativeClasses )
		{
			if ( skip.ShouldSkip( c ) )
			{
				continue;
			}

			foreach ( Class bc in c.BaseClasses )
			{
				yield return new NativeSlot( NativeSlotKind.CastFromTo, c, bc, null, null );
				yield return new NativeSlot( NativeSlotKind.CastToFrom, c, bc, null, null );
			}

			foreach ( Function f in c.Functions )
			{
				yield return new NativeSlot( NativeSlotKind.Function, c, null, f, null );
			}

			foreach ( Variable v in c.Variables )
			{
				yield return new NativeSlot( NativeSlotKind.VariableGet, c, null, null, v );
				yield return new NativeSlot( NativeSlotKind.VariableSet, c, null, null, v );
			}
		}
	}
}
