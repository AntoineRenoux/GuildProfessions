using GuildProfessions.Server.Data;

namespace GuildProfessions.Server.Modules.Presence.Application;

/// <summary>
/// Statistiques de présence : fonctions pures sur des raids déjà lus (taux par
/// fenêtre, résumé par joueur). Sorties d'AttendanceService, qui garde
/// l'ingestion, la lecture et les corrections d'officiers.
/// </summary>
public static class AttendanceStats
{
	/// <summary>Présence minimale pour compter un raid (minutes).</summary>
	public const int MinMinutes = 15;

	/// <summary>Un raid de guilde réunit au moins ce nombre de membres de la guilde.</summary>
	public const int MinGuildMembers = 5;

	/// <summary>Présence d'un personnage à un raid, corrections d'officier comprises : true, false, ou null (excusé).</summary>
	public static bool? Attended(RaidSession session, string character)
	{
		var correction = session.Corrections.SingleOrDefault(c => c.Character == character)?.Status;
		return correction switch
		{
			"present" => true,
			"absent" => false,
			"excused" => null,
			_ => session.Participants.Any(p => p.Character == character && p.Minutes >= MinMinutes),
		};
	}

	/// <summary>Raids qui comptent : assez de membres de la guilde.</summary>
	public static bool IsGuildRaid(RaidSession session) => session.Participants.Count >= MinGuildMembers;

	public sealed record Rate(int Attended, int Eligible)
	{
		public double? Percent => Eligible == 0 ? null : 100.0 * Attended / Eligible;
	}

	public sealed record PlayerStats(string Character, Rate Last30, Rate Last60, Rate Last90, Rate Previous30, int Loot90);

	/// <summary>Taux sur une fenêtre [from, to[ : raids présents / raids de guilde de la fenêtre (excusés retirés).</summary>
	public static Rate RateFor(IEnumerable<RaidSession> sessions, string character, long from, long to)
	{
		var attended = 0;
		var eligible = 0;
		foreach (var session in sessions.Where(s => IsGuildRaid(s) && s.StartedAt >= from && s.StartedAt < to))
		{
			var state = Attended(session, character);
			if (state is null)
			{
				continue;
			}
			eligible++;
			attended += state.Value ? 1 : 0;
		}
		return new Rate(attended, eligible);
	}

	public static PlayerStats StatsFor(IReadOnlyList<RaidSession> sessions, string character, DateTimeOffset now)
	{
		var t = now.ToUnixTimeSeconds();
		long Days(int d) => t - d * 86400L;
		return new PlayerStats(character,
			RateFor(sessions, character, Days(30), t + 1),
			RateFor(sessions, character, Days(60), t + 1),
			RateFor(sessions, character, Days(90), t + 1),
			RateFor(sessions, character, Days(60), Days(30)),
			sessions.Where(s => s.StartedAt >= Days(90)).SelectMany(s => s.Loot).Count(l => l.Character == character && !l.Voided));
	}

	/// <summary>Tous les joueurs vus en raid sur 90 jours, triés par présence sur la fenêtre choisie.</summary>
	public static List<PlayerStats> Summary(IReadOnlyList<RaidSession> sessions, DateTimeOffset now, int days)
	{
		var names = sessions.SelectMany(s => s.Participants.Select(p => p.Character).Concat(s.Corrections.Select(c => c.Character))).Distinct();
		return names.Select(n => StatsFor(sessions, n, now))
			.OrderByDescending(p => Pick(p, days).Percent ?? -1)
			.ThenBy(p => p.Character)
			.ToList();
	}

	public static Rate Pick(PlayerStats stats, int days) => days switch
	{
		<= 30 => stats.Last30,
		<= 60 => stats.Last60,
		_ => stats.Last90,
	};

	public static string Percent(Rate rate) => rate.Percent is { } p ? $"{p:0}%" : "—";
}
