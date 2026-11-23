using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GuildProfessions.Server.Tests;

/// <summary>
/// Types réellement référencés par un type du serveur : champs, propriétés,
/// signatures, et corps des méthodes (lambdas et machines d'état async
/// comprises, puisque ce sont des types imbriqués). NetArchTest compare par
/// préfixe de nom (« Data.Character » couvrirait « Data.CharacterQuestLog ») :
/// ici, la comparaison est exacte, au type près.
/// </summary>
internal static class TypeReferences
{
	private static readonly Lazy<ModuleDefinition> Module = new(() =>
		ModuleDefinition.ReadModule(typeof(GuildProfessions.Server.Discord.GkeepModule).Assembly.Location));

	/// <summary>Tous les types du serveur (imbriqués compris), avec leur nom CLR (« A.B+C »).</summary>
	public static IEnumerable<TypeDefinition> AllTypes() => Module.Value.Types.SelectMany(Flatten);

	public static string ClrName(TypeReference type) => type.FullName.Replace('/', '+');

	private static IEnumerable<TypeDefinition> Flatten(TypeDefinition type) => [type, .. type.NestedTypes.SelectMany(Flatten)];

	/// <summary>Noms CLR des types référencés par ce type et ses types imbriqués.</summary>
	public static HashSet<string> Of(TypeDefinition type)
	{
		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (var current in Flatten(type))
		{
			Add(names, current.BaseType);
			foreach (var contract in current.Interfaces)
			{
				Add(names, contract.InterfaceType);
			}
			foreach (var field in current.Fields)
			{
				Add(names, field.FieldType);
			}
			foreach (var property in current.Properties)
			{
				Add(names, property.PropertyType);
			}
			foreach (var method in current.Methods)
			{
				Add(names, method.ReturnType);
				foreach (var parameter in method.Parameters)
				{
					Add(names, parameter.ParameterType);
				}
				if (!method.HasBody)
				{
					continue;
				}
				foreach (var variable in method.Body.Variables)
				{
					Add(names, variable.VariableType);
				}
				foreach (var instruction in method.Body.Instructions)
				{
					AddOperand(names, instruction);
				}
			}
		}
		return names;
	}

	private static void AddOperand(HashSet<string> names, Instruction instruction)
	{
		switch (instruction.Operand)
		{
			case TypeReference type:
				Add(names, type);
				break;
			case MethodReference method:
				Add(names, method.DeclaringType);
				Add(names, method.ReturnType);
				foreach (var parameter in method.Parameters)
				{
					Add(names, parameter.ParameterType);
				}
				if (method is GenericInstanceMethod generic)
				{
					foreach (var argument in generic.GenericArguments)
					{
						Add(names, argument);
					}
				}
				break;
			case FieldReference field:
				Add(names, field.DeclaringType);
				Add(names, field.FieldType);
				break;
		}
	}

	private static void Add(HashSet<string> names, TypeReference? type)
	{
		switch (type)
		{
			case null:
				return;
			case GenericInstanceType generic:
				Add(names, generic.ElementType);
				foreach (var argument in generic.GenericArguments)
				{
					Add(names, argument);
				}
				return;
			case TypeSpecification specification:
				// Tableaux, références, pointeurs, nullables de référence…
				Add(names, specification.ElementType);
				return;
			default:
				names.Add(ClrName(type));
				return;
		}
	}
}
