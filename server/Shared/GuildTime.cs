namespace GuildProfessions.Server.Shared;

/// <summary>
/// Dates saisies sur Discord et fuseau de la guilde : fonctions pures,
/// partagées par les modules qui demandent une date (événements, échéances
/// d'objectifs), sans base ni Discord.
/// </summary>
public static class GuildTime
{
	/// <summary>Fuseau par défaut d'une guilde, faute de mieux : Paris pour une guilde francophone.</summary>
	public static string DefaultTimeZone(string? locale) =>
		locale is not null && locale.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "Europe/Paris" : "UTC";

	/// <summary>
	/// Date et heure saisies sur Discord (« 29/11 », « 29/11/2026 » ou « 2026-11-29 »,
	/// et « 20:45 », « 20h45 » ou « 20h ») dans un fuseau donné → secondes Unix.
	/// Sans année, la prochaine occurrence de la date.
	/// </summary>
	public static bool TryParseStart(string date, string time, string timeZoneId, DateTimeOffset now, out long startsAt)
	{
		startsAt = 0;
		TimeZoneInfo zone;
		try
		{
			zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
		}
		catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
		{
			return false;
		}

		var t = time.Trim().ToLowerInvariant().Replace('h', ':');
		if (t.EndsWith(':'))
		{
			t += "00";
		}
		var timeParts = t.Split(':');
		if (timeParts.Length != 2 || !int.TryParse(timeParts[0], out var hour) || !int.TryParse(timeParts[1], out var minute) ||
			hour is < 0 or > 23 || minute is < 0 or > 59)
		{
			return false;
		}

		var d = date.Trim();
		int day, month, year;
		var localNow = TimeZoneInfo.ConvertTime(now, zone);
		if (d.Contains('-'))
		{
			var p = d.Split('-');
			if (p.Length != 3 || !int.TryParse(p[0], out year) || !int.TryParse(p[1], out month) || !int.TryParse(p[2], out day))
			{
				return false;
			}
		}
		else
		{
			var p = d.Split('/', '.');
			if (p.Length is < 2 or > 3 || !int.TryParse(p[0], out day) || !int.TryParse(p[1], out month))
			{
				return false;
			}
			year = localNow.Year;
			if (p.Length == 3 && !int.TryParse(p[2], out year))
			{
				return false;
			}
			if (year < 100)
			{
				year += 2000;
			}
			if (p.Length == 2 && (month < localNow.Month || (month == localNow.Month && day < localNow.Day)))
			{
				year++;
			}
		}
		if (month is < 1 or > 12 || year is < 2000 or > 2100 || day < 1 || day > DateTime.DaysInMonth(year, month))
		{
			return false;
		}
		var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
		startsAt = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUnixTimeSeconds();
		return true;
	}
}
