namespace Sandbox;

/// <summary>The operation implemented by an overloaded operator method.</summary>
public enum OperatorKind
{
	None,
	UnaryPlus, UnaryNegation, LogicalNot, OnesComplement,
	Increment, Decrement, True, False,
	Addition, Subtraction, Multiply, Division, Modulus,
	BitwiseAnd, BitwiseOr, ExclusiveOr,
	LeftShift, RightShift, UnsignedRightShift,
	Equality, Inequality, LessThan, GreaterThan, LessThanOrEqual, GreaterThanOrEqual,
	ImplicitConversion, ExplicitConversion,
	CheckedUnaryNegation, CheckedIncrement, CheckedDecrement,
	CheckedAddition, CheckedSubtraction, CheckedMultiply, CheckedDivision, CheckedExplicitConversion,
	AdditionAssignment, SubtractionAssignment, MultiplicationAssignment, DivisionAssignment, ModulusAssignment,
	BitwiseAndAssignment, BitwiseOrAssignment, ExclusiveOrAssignment,
	LeftShiftAssignment, RightShiftAssignment, UnsignedRightShiftAssignment,
	IncrementAssignment, DecrementAssignment,
	CheckedAdditionAssignment, CheckedSubtractionAssignment, CheckedMultiplicationAssignment, CheckedDivisionAssignment,
	CheckedIncrementAssignment, CheckedDecrementAssignment
}

public sealed partial class MethodDescription
{
	/// <summary>The operator implemented by this method, or None for an ordinary method.</summary>
	public OperatorKind OperatorKind { get; private set; }

	/// <summary>Whether this method implements a recognized overloaded operator or conversion.</summary>
	public bool IsOperator => OperatorKind != OperatorKind.None;

	internal static OperatorKind ClassifyOperator( MethodInfo method )
	{
		if ( !method.IsSpecialName ) return OperatorKind.None;

		// C# 14 compound assignment and in-place increment/decrement operators are instance methods.
		if ( !method.IsStatic )
		{
			return method.Name switch
			{
				"op_AdditionAssignment" => OperatorKind.AdditionAssignment,
				"op_SubtractionAssignment" => OperatorKind.SubtractionAssignment,
				"op_MultiplicationAssignment" => OperatorKind.MultiplicationAssignment,
				"op_DivisionAssignment" => OperatorKind.DivisionAssignment,
				"op_ModulusAssignment" => OperatorKind.ModulusAssignment,
				"op_BitwiseAndAssignment" => OperatorKind.BitwiseAndAssignment,
				"op_BitwiseOrAssignment" => OperatorKind.BitwiseOrAssignment,
				"op_ExclusiveOrAssignment" => OperatorKind.ExclusiveOrAssignment,
				"op_LeftShiftAssignment" => OperatorKind.LeftShiftAssignment,
				"op_RightShiftAssignment" => OperatorKind.RightShiftAssignment,
				"op_UnsignedRightShiftAssignment" => OperatorKind.UnsignedRightShiftAssignment,
				"op_IncrementAssignment" => OperatorKind.IncrementAssignment,
				"op_DecrementAssignment" => OperatorKind.DecrementAssignment,
				"op_CheckedAdditionAssignment" => OperatorKind.CheckedAdditionAssignment,
				"op_CheckedSubtractionAssignment" => OperatorKind.CheckedSubtractionAssignment,
				"op_CheckedMultiplicationAssignment" => OperatorKind.CheckedMultiplicationAssignment,
				"op_CheckedDivisionAssignment" => OperatorKind.CheckedDivisionAssignment,
				"op_CheckedIncrementAssignment" => OperatorKind.CheckedIncrementAssignment,
				"op_CheckedDecrementAssignment" => OperatorKind.CheckedDecrementAssignment,
				_ => OperatorKind.None
			};
		}

		return method.Name switch
		{
			"op_UnaryPlus" => OperatorKind.UnaryPlus,
			"op_UnaryNegation" => OperatorKind.UnaryNegation,
			"op_LogicalNot" => OperatorKind.LogicalNot,
			"op_OnesComplement" => OperatorKind.OnesComplement,
			"op_Increment" => OperatorKind.Increment,
			"op_Decrement" => OperatorKind.Decrement,
			"op_True" => OperatorKind.True,
			"op_False" => OperatorKind.False,
			"op_Addition" => OperatorKind.Addition,
			"op_Subtraction" => OperatorKind.Subtraction,
			"op_Multiply" => OperatorKind.Multiply,
			"op_Division" => OperatorKind.Division,
			"op_Modulus" => OperatorKind.Modulus,
			"op_BitwiseAnd" => OperatorKind.BitwiseAnd,
			"op_BitwiseOr" => OperatorKind.BitwiseOr,
			"op_ExclusiveOr" => OperatorKind.ExclusiveOr,
			"op_LeftShift" => OperatorKind.LeftShift,
			"op_RightShift" => OperatorKind.RightShift,
			"op_UnsignedRightShift" => OperatorKind.UnsignedRightShift,
			"op_Equality" => OperatorKind.Equality,
			"op_Inequality" => OperatorKind.Inequality,
			"op_LessThan" => OperatorKind.LessThan,
			"op_GreaterThan" => OperatorKind.GreaterThan,
			"op_LessThanOrEqual" => OperatorKind.LessThanOrEqual,
			"op_GreaterThanOrEqual" => OperatorKind.GreaterThanOrEqual,
			"op_Implicit" => OperatorKind.ImplicitConversion,
			"op_Explicit" => OperatorKind.ExplicitConversion,
			"op_CheckedUnaryNegation" => OperatorKind.CheckedUnaryNegation,
			"op_CheckedIncrement" => OperatorKind.CheckedIncrement,
			"op_CheckedDecrement" => OperatorKind.CheckedDecrement,
			"op_CheckedAddition" => OperatorKind.CheckedAddition,
			"op_CheckedSubtraction" => OperatorKind.CheckedSubtraction,
			"op_CheckedMultiply" => OperatorKind.CheckedMultiply,
			"op_CheckedDivision" => OperatorKind.CheckedDivision,
			"op_CheckedExplicit" => OperatorKind.CheckedExplicitConversion,
			_ => OperatorKind.None
		};
	}
}
