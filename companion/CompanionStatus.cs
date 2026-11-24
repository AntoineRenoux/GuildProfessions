namespace GuildProfessions.Companion;

/// <summary>Ce que fait le compagnon, affiché par son icône (infobulle et première ligne du menu).</summary>
public enum CompanionState
{
	Starting,
	Sleeping,
	Syncing,
	Problem,
}

/// <summary>
/// État partagé entre le moteur de synchro et l'icône de notification. Le
/// moteur le met à jour, l'icône l'affiche (événement Changed, émis depuis
/// n'importe quel fil : l'icône se replace sur son fil d'interface).
/// </summary>
public sealed class CompanionStatus
{
	private readonly object _gate = new();

	public CompanionState State { get; private set; } = CompanionState.Starting;

	/// <summary>Dernière synchro réussie (heure locale), null avant la première.</summary>
	public DateTime? LastSync { get; private set; }

	/// <summary>Le problème en cours, à régler par le joueur (lien révoqué, WoW introuvable…).</summary>
	public string? Problem { get; private set; }

	public event Action? Changed;

	public static CompanionStatus Shared { get; } = new();

	public void Set(CompanionState state)
	{
		lock (_gate)
		{
			State = state;
			if (state != CompanionState.Problem)
			{
				Problem = null;
			}
		}
		Changed?.Invoke();
	}

	/// <summary>Synchro réussie : un problème éventuel est réglé, retour à l'état courant (veille ou synchro active).</summary>
	public void Synced(DateTime at, CompanionState current)
	{
		lock (_gate)
		{
			LastSync = at;
			Problem = null;
			State = current;
		}
		Changed?.Invoke();
	}

	public void Fail(string problem)
	{
		lock (_gate)
		{
			State = CompanionState.Problem;
			Problem = problem;
		}
		Changed?.Invoke();
	}

	/// <summary>Une ligne lisible : ce qui se passe, et l'heure de la dernière synchro.</summary>
	public string Summary()
	{
		lock (_gate)
		{
			var last = LastSync is { } at ? $" · dernière synchro à {at:HH:mm}" : "";
			return State switch
			{
				CompanionState.Starting => "Démarrage…",
				CompanionState.Sleeping => "En veille : se réveille quand tu lances WoW" + last,
				CompanionState.Syncing => "WoW lancé : synchronisation active" + last,
				_ => "À régler : " + (Problem ?? "voir le journal"),
			};
		}
	}

	/// <summary>Infobulle de l'icône (courte : Windows la coupe à 127 caractères).</summary>
	public string ToolTip()
	{
		var text = "Guildkeep · " + Summary();
		return text.Length > 120 ? text[..119] + "…" : text;
	}
}
