using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TaskDesk.Tests.Integration;

public sealed partial class WebTests(TaskDeskWebFactory factory) : IClassFixture<TaskDeskWebFactory>
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    // ---------------- Web UI ----------------

    [Fact]
    public async Task Anonymous_users_are_redirected_to_login()
    {
        var response = await NoRedirectClient().GetAsync("/Tasks");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_page_sends_security_headers()
    {
        var response = await factory.CreateClient().GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task Staff_can_sign_in_and_see_the_dashboard()
    {
        var client = await SignInAsync("staff.it@taskdesk.local");

        var html = await client.GetStringAsync("/");

        Assert.Contains("Ruwan Bandara", html);
        Assert.Contains("Assigned to me", html);
    }

    [Fact]
    public async Task Wrong_password_shows_a_generic_error()
    {
        var client = NoRedirectClient();
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");

        var response = await client.PostAsync("/Account/Login", Form(new()
        {
            ["Email"] = "staff.fin@taskdesk.local",
            ["Password"] = "Wrong-password-1!",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid email or password", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected()
    {
        var response = await NoRedirectClient().PostAsync("/Account/Login", Form(new()
        {
            ["Email"] = "staff.it@taskdesk.local",
            ["Password"] = TaskDeskWebFactory.Password,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Staff_cannot_open_manager_reports()
    {
        var client = await SignInAsync("staff.it@taskdesk.local");

        var response = await client.GetAsync("/Reports/Workload");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Manager_sees_the_workload_report()
    {
        var client = await SignInAsync("manager.it@taskdesk.local");

        var response = await client.GetAsync("/Reports/Workload");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Information Technology", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Staff_can_create_a_task_through_the_form()
    {
        var client = await SignInAsync("staff.it@taskdesk.local");
        var token = await GetAntiforgeryTokenAsync(client, "/Tasks/Create");
        var title = $"Integration test task {Guid.NewGuid():N}";

        var response = await client.PostAsync("/Tasks/Create", Form(new()
        {
            ["Title"] = title,
            ["Description"] = "Created by an automated test",
            ["Priority"] = "High",
            ["DueDate"] = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"),
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var details = await client.GetStringAsync(response.Headers.Location!.ToString());
        Assert.Contains(title, details);
        Assert.Contains("History", details);
    }

    // ---------------- Monitoring ----------------

    [Fact]
    public async Task Readiness_probe_reports_database_health()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"database\"", await response.Content.ReadAsStringAsync());
    }

    // ---------------- REST / XML API ----------------

    [Fact]
    public async Task Api_requires_an_api_key()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/tasks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Api_rejects_a_wrong_api_key()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "not-the-key");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/tasks")).StatusCode);
    }

    [Fact]
    public async Task Api_create_is_idempotent()
    {
        var client = ApiClient();
        var body = new
        {
            externalReference = $"HD-{Guid.NewGuid():N}"[..20],
            title = "Printer on 3rd floor jammed",
            priority = "High",
            departmentCode = "IT",
            assigneeEmail = "staff.it@taskdesk.local",
        };

        var first = await client.PostAsJsonAsync("/api/v1/tasks", body);
        var retry = await client.PostAsJsonAsync("/api/v1/tasks", body);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var a = await first.Content.ReadFromJsonAsync<TaskDto>();
        var b = await retry.Content.ReadFromJsonAsync<TaskDto>();
        Assert.Equal(a!.Id, b!.Id);
    }

    [Fact]
    public async Task Api_supports_xml()
    {
        var client = ApiClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

        const string xml = """
            <CreateTaskRequest>
              <ExternalReference>HD-XML-0001</ExternalReference>
              <Title>Request created from XML</Title>
              <Priority>Low</Priority>
              <DepartmentCode>FIN</DepartmentCode>
            </CreateTaskRequest>
            """;

        var response = await client.PostAsync("/api/v1/tasks", new StringContent(xml, Encoding.UTF8, "application/xml"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, body);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<Title>Request created from XML</Title>", body);
    }

    [Fact]
    public async Task Api_validation_errors_return_400_and_business_errors_422()
    {
        var client = ApiClient();

        var invalid = await client.PostAsJsonAsync("/api/v1/tasks", new { externalReference = "X", departmentCode = "IT" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var unknownDept = await client.PostAsJsonAsync("/api/v1/tasks",
            new { externalReference = "X-422", title = "Valid title", departmentCode = "NOPE" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownDept.StatusCode);
    }

    [Fact]
    public async Task Mock_soap_holiday_service_answers_soap_requests()
    {
        var envelope = TaskDesk.Integration.SoapHolidayCalendarClient.BuildRequest(2026).ToString();
        var response = await factory.CreateClient().PostAsync("/mock/soap/holidays", new StringContent(envelope, Encoding.UTF8, "text/xml"));
        var holidays = TaskDesk.Integration.SoapHolidayCalendarClient.ParseResponse(await response.Content.ReadAsStringAsync());

        Assert.Contains(holidays, h => h.Name == "National Day" && h.Date == new DateOnly(2026, 2, 4));
    }

    // ---------------- helpers ----------------

    private HttpClient NoRedirectClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private HttpClient ApiClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TaskDeskWebFactory.ApiKey);
        return client;
    }

    private async Task<HttpClient> SignInAsync(string email)
    {
        var client = NoRedirectClient();
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");

        var response = await client.PostAsync("/Account/Login", Form(new()
        {
            ["Email"] = email,
            ["Password"] = TaskDeskWebFactory.Password,
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        var match = AntiforgeryToken().Match(html);
        Assert.True(match.Success, "Antiforgery token not found on " + url);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);

    private sealed record TaskDto(int Id);
}
