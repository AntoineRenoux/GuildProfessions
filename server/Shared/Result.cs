namespace GuildProfessions.Server.Shared;

/// <summary>Nature d'une erreur métier : la présentation peut réagir selon le type, sans lire le texte.</summary>
public enum ErrorType
{
	/// <summary>Donnée saisie invalide ou incomplète.</summary>
	Validation,

	/// <summary>La chose visée n'existe pas (ou plus) dans la guilde.</summary>
	NotFound,

	/// <summary>L'utilisateur n'a pas le droit de faire ça.</summary>
	Forbidden,

	/// <summary>L'état actuel ne permet pas l'opération (déjà fait, déjà pris…).</summary>
	Conflict,
}

/// <summary>Erreur métier typée, avec son message déjà localisé pour l'utilisateur.</summary>
public sealed record Error(ErrorType Type, string Message);

/// <summary>
/// Issue d'une opération métier (pattern Result) : un succès porte le message
/// à afficher, un échec une <see cref="Error"/> typée. Les erreurs métier ne
/// passent jamais par une exception.
/// </summary>
public sealed class Result
{
	private Result(bool isSuccess, string message, Error? error)
	{
		IsSuccess = isSuccess;
		Message = message;
		Error = error;
	}

	public bool IsSuccess { get; }

	/// <summary>Message localisé à afficher, que l'opération ait réussi ou non.</summary>
	public string Message { get; }

	public Error? Error { get; }

	public static Result Success(string message) => new(true, message, null);

	public static Result Failure(ErrorType type, string message) => new(false, message, new Error(type, message));

	public static Result Invalid(string message) => Failure(ErrorType.Validation, message);

	public static Result NotFound(string message) => Failure(ErrorType.NotFound, message);

	public static Result Forbidden(string message) => Failure(ErrorType.Forbidden, message);

	public static Result Conflict(string message) => Failure(ErrorType.Conflict, message);
}
