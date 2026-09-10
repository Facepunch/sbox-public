
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

partial class MotionEditMode
{
	private static MotionEditMode? Current => Focused as MotionEditMode;

	[Shortcut( "motion-edit.interp-none", "0", typeof( MovieEditor ) )]
	public static void Shortcut_SetInterpolationNone() => Current?.SetInterpolation( InterpolationMode.None );

	[Shortcut( "motion-edit.interp-linear", "1", typeof( MovieEditor ) )]
	public static void Shortcut_SetInterpolationLinear() => Current?.SetInterpolation( InterpolationMode.Linear );

	[Shortcut( "motion-edit.interp-in", "2", typeof( MovieEditor ) )]
	public static void Shortcut_SetInterpolationIn() => Current?.SetInterpolation( InterpolationMode.QuadraticIn );

	[Shortcut( "motion-edit.interp-out", "3", typeof( MovieEditor ) )]
	public static void Shortcut_SetInterpolationOut() => Current?.SetInterpolation( InterpolationMode.QuadraticOut );

	[Shortcut( "motion-edit.interp-in-out", "4", typeof( MovieEditor ) )]
	public static void Shortcut_SetInterpolationInOut() => Current?.SetInterpolation( InterpolationMode.QuadraticInOut );

	[Shortcut( "motion-edit.clear", "ESC", typeof( MovieEditor ) )]
	private static void Shortcut_Clear()
	{
		if ( Current is not { } inst ) return;

		if ( inst.HasChanges )
		{
			inst.ClearChanges();
		}
		else if ( inst.TimeSelection is not null )
		{
			inst.TimeSelection = null;
		}
	}

	[Shortcut( "motion-edit.commit", "ENTER", typeof( MovieEditor ) )]
	private static void Shortcut_Commit()
	{
		Current?.CommitChanges();
	}
}
