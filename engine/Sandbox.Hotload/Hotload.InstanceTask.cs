using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sandbox.Diagnostics;

namespace Sandbox
{
	public partial class Hotload
	{
		private record struct InstanceTask( IInstanceProcessor Handler, object OldInstance, object NewInstance, FieldInfo SrcField, FieldInfo DstField, ReferencePath Path )
		{
			public override string ToString()
			{
				return $"{Handler}: {OldInstance}";
			}
		}

		public sealed class ReferencePath
		{
			private static readonly ConditionalWeakTable<MemberInfo, ReferencePath> _roots =
				new ConditionalWeakTable<MemberInfo, ReferencePath>();

			public static ReferencePath GetRoot( MemberInfo member )
			{
				Assert.NotNull( member );

				if ( _roots.TryGetValue( member, out var root ) )
				{
					return root;
				}

				// Atomic: a separate check and Add throws when two threads ask for the same new root.
				return _roots.GetValue( member, m => new ReferencePath( null, m ) );
			}

			/// <summary>
			/// Drops every path. Called after each hotload: paths are only for diagnosing that hotload, and a kept tree
			/// pinned unloaded assemblies. A child keyed by a long-lived member (an engine type, say) stays reachable
			/// through its weakly keyed table, and its <see cref="Parent"/> chain held every outgoing field above it.
			/// </summary>
			internal static void Clear() => _roots.Clear();

			public ReferencePath Parent { get; }
			public MemberInfo Member { get; }
			public ReferencePath Root { get; }

			private readonly Dictionary<MemberInfo, ReferencePath> _children =
				new Dictionary<MemberInfo, ReferencePath>();

			public bool IsRoot => Parent == null;


			private ReferencePath( ReferencePath parent, MemberInfo member )
			{
				Parent = parent;
				Member = member;
				Root = parent?.Root ?? this;
			}

			public ReferencePath this[MemberInfo member]
			{
				get
				{
					Assert.NotNull( member );

					if ( _children.TryGetValue( member, out var child ) ) return child;

					_children.Add( member, child = new ReferencePath( this, member ) );

					return child;
				}
			}

			public override string ToString()
			{
				return Parent == null
					? Member switch
					{
						Type type => $"[Instance {type.ToSimpleString()}]",
						FieldInfo { IsStatic: true } or PropertyInfo { GetMethod.IsStatic: true } =>
							$"[({FormatAssemblyName( Member.DeclaringType.Assembly )}) {Member.DeclaringType.ToSimpleString()}::{Member.Name}]",
						_ => "[Unknown]"
					}
					: Member switch
					{
						Type => $"{Parent}[]",
						FieldInfo field when field.TryGetBackedProperty( out var property ) =>
							$"{Parent}.{property.Name}",
						_ => $"{Parent}.{Member.Name}"
					};
			}
		}
	}
}
