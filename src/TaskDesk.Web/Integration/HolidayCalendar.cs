using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TaskDesk.Common;

namespace TaskDesk.Integration;

public sealed record Holiday(DateOnly Date, string Name);

public interface IHolidayCalendar
{
    Task<IReadOnlyList<Holiday>> GetHolidaysAsync(int year, CancellationToken cancellationToken);
}

public sealed class HolidayServiceOptions
{
    public const string SectionName = "Integration:HolidayService";

    /// <summary>SOAP endpoint. Empty = integration switched off (the app keeps working without it).</summary>
    public string? Url { get; set; }

    public int TimeoutSeconds { get; set; } = 5;

    public int CacheHours { get; set; } = 12;
}

/// <summary>
/// SOAP 1.1 client for the corporate Holiday Calendar service (e.g. an HR / core system web service).
/// Resilience: short timeout, results cached, and any failure degrades gracefully to "no holiday data"
/// so the To-Do app never goes down because a downstream system is unavailable.
/// </summary>
public sealed class SoapHolidayCalendarClient(
    HttpClient httpClient,
    IMemoryCache cache,
    IOptions<HolidayServiceOptions> options,
    ILogger<SoapHolidayCalendarClient> logger) : IHolidayCalendar
{
    public const string ServiceNamespace = "http://taskdesk.example/holidays";
    public const string SoapAction = ServiceNamespace + "/GetHolidays";

    internal static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    internal static readonly XNamespace Ns = ServiceNamespace;

    public async Task<IReadOnlyList<Holiday>> GetHolidaysAsync(int year, CancellationToken cancellationToken)
    {
        var url = options.Value.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            return [];
        }

        var cacheKey = $"holidays:{year}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<Holiday>? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(BuildRequest(year).ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml"),
            };
            request.Headers.Add("SOAPAction", $"\"{SoapAction}\"");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            var holidays = ParseResponse(body); // throws on SOAP Fault
            response.EnsureSuccessStatusCode();

            cache.Set(cacheKey, holidays, TimeSpan.FromHours(options.Value.CacheHours));
            logger.LogInformation("Loaded {Count} holidays for {Year} from the holiday service", holidays.Count, year);
            return holidays;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or XmlException or FormatException or InvalidOperationException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Holiday service call failed for {Year}; continuing without holiday data", year);
            return [];
        }
    }

    public static XDocument BuildRequest(int year) =>
        new(new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soap", Soap),
            new XElement(Soap + "Body",
                new XElement(Ns + "GetHolidays",
                    new XElement(Ns + "Year", year)))));

    public static IReadOnlyList<Holiday> ParseResponse(string xml)
    {
        var document = SafeXml.Parse(xml);

        var fault = document.Descendants(Soap + "Fault").FirstOrDefault();
        if (fault is not null)
        {
            throw new InvalidOperationException($"SOAP fault: {(string?)fault.Element("faultstring") ?? "unknown"}");
        }

        return document.Descendants(Ns + "Holiday")
            .Select(h => new Holiday(
                DateOnly.ParseExact((string?)h.Element(Ns + "Date") ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                (string?)h.Element(Ns + "Name") ?? "Holiday"))
            .OrderBy(h => h.Date)
            .ToList();
    }
}

/// <summary>
/// A stand-in for the corporate SOAP service so the integration can be demonstrated locally.
/// Only mapped in Development / Testing. Sample data: fixed-date holidays only - the real service
/// would also return Poya days and other religious holidays, which move every year.
/// </summary>
public static class MockHolidaySoapService
{
    private static readonly (int Month, int Day, string Name)[] FixedHolidays =
    [
        (2, 4, "National Day"),
        (5, 1, "May Day"),
        (12, 25, "Christmas Day"),
    ];

    public static IEndpointConventionBuilder MapMockHolidaySoapService(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/mock/soap/holidays", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();

            int year;
            try
            {
                var envelope = SafeXml.Parse(body);
                year = (int)envelope.Descendants(SoapHolidayCalendarClient.Ns + "Year").Single();
            }
            catch (Exception ex) when (ex is XmlException or InvalidOperationException or FormatException)
            {
                return Results.Content(Fault("Client", "Malformed GetHolidays request."), "text/xml; charset=utf-8", statusCode: 500);
            }

            return Results.Content(Response(year), "text/xml; charset=utf-8");
        }).AllowAnonymous().ExcludeFromDescription();

    private static string Response(int year)
    {
        var soap = SoapHolidayCalendarClient.Soap;
        var ns = SoapHolidayCalendarClient.Ns;
        var document = new XDocument(new XElement(soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soap", soap),
            new XElement(soap + "Body",
                new XElement(ns + "GetHolidaysResponse",
                    new XElement(ns + "Holidays",
                        FixedHolidays.Select(h => new XElement(ns + "Holiday",
                            new XElement(ns + "Date", new DateOnly(year, h.Month, h.Day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                            new XElement(ns + "Name", h.Name))))))));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string Fault(string code, string message)
    {
        var soap = SoapHolidayCalendarClient.Soap;
        return new XDocument(new XElement(soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soap", soap),
            new XElement(soap + "Body",
                new XElement(soap + "Fault",
                    new XElement("faultcode", "soap:" + code),
                    new XElement("faultstring", message))))).ToString(SaveOptions.DisableFormatting);
    }
}
