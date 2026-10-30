using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskDesk.Domain;
using TaskDesk.Integration;
using TaskDesk.Models.Api;
using TaskDesk.Tests.TestSupport;

namespace TaskDesk.Tests.Services;

public sealed class IntegrationTaskServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly FakeClock _clock = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Create_is_idempotent_per_client_and_external_reference()
    {
        var request = new ApiCreateTaskRequest
        {
            ExternalReference = "HD-2026-0042",
            Title = "Laptop not booting",
            DepartmentCode = "it",
            AssigneeEmail = "STAFF.IT@test.local",
            DueDate = "2026-11-02",
            Priority = TaskPriority.High,
        };

        await using (var db = _database.CreateContext())
        {
            var (task, created) = await Service(db).CreateAsync("Helpdesk", request, CancellationToken.None);
            Assert.True(created);
            Assert.Equal("IT", task.DepartmentCode);
            Assert.Equal("2026-11-02", task.DueDate);
        }

        await using (var db = _database.CreateContext())
        {
            var (task, created) = await Service(db).CreateAsync("Helpdesk", request, CancellationToken.None);
            Assert.False(created);
            Assert.Equal("HD-2026-0042", task.ExternalReference);
        }

        // A different client may use the same reference - it is a different source system.
        await using (var db = _database.CreateContext())
        {
            var (_, created) = await Service(db).CreateAsync("FacilitiesSystem", request, CancellationToken.None);
            Assert.True(created);
        }

        await using var verify = _database.CreateContext();
        Assert.Equal(2, await verify.Tasks.CountAsync());
    }

    [Fact]
    public async Task Clients_only_see_their_own_tasks()
    {
        await using var db = _database.CreateContext();
        var service = Service(db);
        var (task, _) = await service.CreateAsync("Helpdesk",
            new ApiCreateTaskRequest { ExternalReference = "X1", Title = "Private", DepartmentCode = "IT" }, CancellationToken.None);

        Assert.NotNull(await service.GetAsync("Helpdesk", task.Id, CancellationToken.None));
        Assert.Null(await service.GetAsync("SomeoneElse", task.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Assignee_from_another_department_is_rejected()
    {
        await using var db = _database.CreateContext();

        await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).CreateAsync("Helpdesk",
            new ApiCreateTaskRequest { ExternalReference = "X2", Title = "Wrong dept", DepartmentCode = "IT", AssigneeEmail = "staff.fin@test.local" },
            CancellationToken.None));
    }

    private IntegrationTaskService Service(TaskDesk.Data.AppDbContext db) =>
        new(db, _clock, NullLogger<IntegrationTaskService>.Instance);
}

public sealed class SoapHolidayCalendarClientTests
{
    private const string SampleResponse = """
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body>
            <GetHolidaysResponse xmlns="http://taskdesk.example/holidays">
              <Holidays>
                <Holiday><Date>2026-05-01</Date><Name>May Day</Name></Holiday>
                <Holiday><Date>2026-02-04</Date><Name>National Day</Name></Holiday>
              </Holidays>
            </GetHolidaysResponse>
          </soap:Body>
        </soap:Envelope>
        """;

    [Fact]
    public async Task Sends_a_soap_envelope_and_parses_the_response()
    {
        var handler = new StubHandler(HttpStatusCode.OK, SampleResponse);
        var client = CreateClient(handler);

        var holidays = await client.GetHolidaysAsync(2026, CancellationToken.None);

        Assert.Equal(2, holidays.Count);
        Assert.Equal(new DateOnly(2026, 2, 4), holidays[0].Date); // sorted
        Assert.Equal("\"http://taskdesk.example/holidays/GetHolidays\"", handler.SoapAction);
        Assert.Contains("<Year>2026</Year>", handler.RequestBody);
    }

    [Fact]
    public async Task Results_are_cached()
    {
        var handler = new StubHandler(HttpStatusCode.OK, SampleResponse);
        var client = CreateClient(handler);

        await client.GetHolidaysAsync(2026, CancellationToken.None);
        await client.GetHolidaysAsync(2026, CancellationToken.None);

        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body><soap:Fault><faultcode>soap:Server</faultcode><faultstring>Down</faultstring></soap:Fault></soap:Body></soap:Envelope>")]
    [InlineData(HttpStatusCode.OK, "this is not xml")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    public async Task Failures_degrade_gracefully_to_no_holidays(HttpStatusCode status, string body)
    {
        var client = CreateClient(new StubHandler(status, body));

        var holidays = await client.GetHolidaysAsync(2026, CancellationToken.None);

        Assert.Empty(holidays);
    }

    [Fact]
    public void Mock_service_response_matches_the_client_contract()
    {
        // The request built by the client must be parseable, and the mock's reply format is the same as SampleResponse.
        var request = SoapHolidayCalendarClient.BuildRequest(2026).ToString();
        Assert.Contains("GetHolidays", request);
        Assert.Equal(2, SoapHolidayCalendarClient.ParseResponse(SampleResponse).Count);
    }

    private static SoapHolidayCalendarClient CreateClient(StubHandler handler) =>
        new(
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new HolidayServiceOptions { Url = "http://holidays.test/service.asmx" }),
            NullLogger<SoapHolidayCalendarClient>.Instance);

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? SoapAction { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            SoapAction = request.Headers.TryGetValues("SOAPAction", out var values) ? values.Single() : null;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/xml") };
        }
    }
}
