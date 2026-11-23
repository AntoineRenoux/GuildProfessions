using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Modules.Evenements.Application;

/// <summary>Dates et fuseaux des événements : délègue au socle partagé (<see cref="GuildTime"/>).</summary>
public static class EventSchedule
{
	/// <inheritdoc cref="GuildTime.DefaultTimeZone"/>
	public static string DefaultTimeZone(string? locale) => GuildTime.DefaultTimeZone(locale);

	/// <inheritdoc cref="GuildTime.TryParseStart"/>
	public static bool TryParseStart(string date, string time, string timeZoneId, DateTimeOffset now, out long startsAt) =>
		GuildTime.TryParseStart(date, time, timeZoneId, now, out startsAt);
}
