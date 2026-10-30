using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace TaskDesk.Common;

public sealed class OfficeOptions
{
    public const string SectionName = "Office";

    /// <summary>Office time zone offset from UTC in minutes (Sri Lanka = +330, no daylight saving).</summary>
    public int UtcOffsetMinutes { get; set; } = 330;

    public string Name { get; set; } = "Head Office";
}

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    public int SlowRequestThresholdMs { get; set; } = 1000;

    public bool EmitServerTimingHeader { get; set; }
}

/// <summary>Single source of "now". Data is stored in UTC; due dates follow the office calendar.</summary>
public interface IClock
{
    DateTime UtcNow { get; }

    DateOnly Today { get; }

    DateTime ToOfficeTime(DateTime utc);
}

public sealed class OfficeClock(IOptions<OfficeOptions> options) : IClock
{
    private TimeSpan Offset => TimeSpan.FromMinutes(options.Value.UtcOffsetMinutes);

    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow + Offset);

    public DateTime ToOfficeTime(DateTime utc) => DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);
}

/// <summary>Who is making the current request (a signed-in user, an API client or "system").</summary>
public interface ICurrentUser
{
    string? UserId { get; }

    string UserName { get; }

    string? IpAddress { get; }

    string? CorrelationId { get; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;
}

public static class Paging
{
    public static (int Page, int PageSize) Normalize(int page, int pageSize, int max = 100) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, max));
}

/// <summary>XML parsing hardened against XXE / entity-expansion attacks (OWASP).</summary>
public static class SafeXml
{
    public static XDocument Parse(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1_000_000,
        };

        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(reader);
    }
}

/// <summary>Writes CSV values safely: quoting plus protection against spreadsheet formula injection.</summary>
public static class Csv
{
    private static readonly char[] FormulaPrefixes = ['=', '+', '-', '@', '\t', '\r'];

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (FormulaPrefixes.Contains(value[0]))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
