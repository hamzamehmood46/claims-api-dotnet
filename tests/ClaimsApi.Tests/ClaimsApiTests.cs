using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClaimsApi.Api.Auth;
using ClaimsApi.Application;
using ClaimsApi.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClaimsApi.Tests;

public sealed class ClaimsFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"claims-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Claims", $"Data Source={_dbPath}");
        builder.UseSetting("Jwt:Key", "test-signing-key-that-is-long-enough-123456");
        builder.UseSetting("Auth:Users:0:Username", "biller");
        builder.UseSetting("Auth:Users:0:Password", "biller-pw");
        builder.UseSetting("Auth:Users:0:Role", "Biller");
        builder.UseSetting("Auth:Users:1:Username", "reviewer");
        builder.UseSetting("Auth:Users:1:Password", "reviewer-pw");
        builder.UseSetting("Auth:Users:1:Role", "Reviewer");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_dbPath); } catch (IOException) { /* best effort */ }
    }
}

public class ClaimsApiTests(ClaimsFactory factory) : IClassFixture<ClaimsFactory>
{
    private async Task<HttpClient> ClientAsync(string? user = null, string? password = null)
    {
        var client = factory.CreateClient();
        if (user is null) return client;

        var response = await client.PostAsJsonAsync("/api/auth/token", new TokenRequest(user, password!));
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private Task<HttpClient> BillerAsync() => ClientAsync("biller", "biller-pw");
    private Task<HttpClient> ReviewerAsync() => ClientAsync("reviewer", "reviewer-pw");

    private static async Task<PatientDto> CreatePatientAsync(HttpClient biller, string last = "Nguyen")
    {
        var response = await biller.PostAsJsonAsync("/api/patients",
            new CreatePatientRequest("Alex", last, new DateOnly(1985, 4, 12), "INS-" + Guid.NewGuid().ToString("N")[..8]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PatientDto>())!;
    }

    private static async Task<ClaimDto> CreateClaimAsync(HttpClient biller, Guid patientId)
    {
        var response = await biller.PostAsJsonAsync("/api/claims",
            new CreateClaimRequest(patientId, [new ClaimLineRequest("99213", "Office visit", 120m)]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClaimDto>())!;
    }

    [Fact]
    public async Task Endpoints_require_authentication()
    {
        var anonymous = await ClientAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/patients")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/claims")).StatusCode);
    }

    [Fact]
    public async Task Bad_credentials_are_rejected()
    {
        var client = await ClientAsync();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/token", new TokenRequest("biller", "nope"));
        var unknownUser = await client.PostAsJsonAsync("/api/auth/token", new TokenRequest("ghost", "nope"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
    }

    [Fact]
    public async Task Full_claim_lifecycle_across_roles()
    {
        var biller = await BillerAsync();
        var reviewer = await ReviewerAsync();
        var patient = await CreatePatientAsync(biller);
        var claim = await CreateClaimAsync(biller, patient.Id);
        Assert.Equal(ClaimStatus.Draft, claim.Status);

        var submitted = await (await biller.PostAsync($"/api/claims/{claim.Id}/submit", null)).Content.ReadFromJsonAsync<ClaimDto>();
        Assert.Equal(ClaimStatus.Submitted, submitted!.Status);

        var approved = await (await reviewer.PostAsync($"/api/claims/{claim.Id}/approve", null)).Content.ReadFromJsonAsync<ClaimDto>();
        Assert.Equal(ClaimStatus.Approved, approved!.Status);

        var paid = await (await biller.PostAsync($"/api/claims/{claim.Id}/pay", null)).Content.ReadFromJsonAsync<ClaimDto>();
        Assert.Equal(ClaimStatus.Paid, paid!.Status);
        Assert.Equal(120m, paid.Total);
    }

    [Fact]
    public async Task Roles_are_enforced()
    {
        var biller = await BillerAsync();
        var reviewer = await ReviewerAsync();
        var patient = await CreatePatientAsync(biller);
        var claim = await CreateClaimAsync(biller, patient.Id);
        await biller.PostAsync($"/api/claims/{claim.Id}/submit", null);

        Assert.Equal(HttpStatusCode.Forbidden, (await biller.PostAsync($"/api/claims/{claim.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.PostAsJsonAsync("/api/claims",
            new CreateClaimRequest(patient.Id, [new ClaimLineRequest("99213", null, 10m)]))).StatusCode);
    }

    [Fact]
    public async Task Denial_requires_reason_and_is_recorded()
    {
        var biller = await BillerAsync();
        var reviewer = await ReviewerAsync();
        var claim = await CreateClaimAsync(biller, (await CreatePatientAsync(biller)).Id);
        await biller.PostAsync($"/api/claims/{claim.Id}/submit", null);

        var missing = await reviewer.PostAsJsonAsync($"/api/claims/{claim.Id}/deny", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var denied = await (await reviewer.PostAsJsonAsync($"/api/claims/{claim.Id}/deny", new { reason = "Not covered" }))
            .Content.ReadFromJsonAsync<ClaimDto>();
        Assert.Equal(ClaimStatus.Denied, denied!.Status);
        Assert.Equal("Not covered", denied.DecisionReason);
    }

    [Fact]
    public async Task Illegal_transition_returns_conflict()
    {
        var biller = await BillerAsync();
        var claim = await CreateClaimAsync(biller, (await CreatePatientAsync(biller)).Id);

        var response = await biller.PostAsync($"/api/claims/{claim.Id}/pay", null); // still a draft

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_input_returns_bad_request_and_unknown_ids_return_not_found()
    {
        var biller = await BillerAsync();

        var invalid = await biller.PostAsJsonAsync("/api/patients",
            new CreatePatientRequest("", "Smith", new DateOnly(1990, 1, 1), "INS-1"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await biller.GetAsync($"/api/claims/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await biller.PostAsJsonAsync("/api/claims",
            new CreateClaimRequest(Guid.NewGuid(), [new ClaimLineRequest("99213", null, 10m)]))).StatusCode);
    }

    [Fact]
    public async Task Patient_search_filters_and_pages()
    {
        var biller = await BillerAsync();
        var tag = "Zed" + Guid.NewGuid().ToString("N")[..6];
        for (var i = 0; i < 5; i++) await CreatePatientAsync(biller, tag);

        var page1 = await biller.GetFromJsonAsync<PagedResult<PatientDto>>($"/api/patients?q={tag}&page=1&pageSize=2");
        var page3 = await biller.GetFromJsonAsync<PagedResult<PatientDto>>($"/api/patients?q={tag}&page=3&pageSize=2");

        Assert.Equal(5, page1!.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page3!.Items);
    }

    [Fact]
    public async Task Claims_can_be_filtered_by_status_and_patient()
    {
        var biller = await BillerAsync();
        var patient = await CreatePatientAsync(biller);
        var draft = await CreateClaimAsync(biller, patient.Id);
        var submitted = await CreateClaimAsync(biller, patient.Id);
        await biller.PostAsync($"/api/claims/{submitted.Id}/submit", null);

        var result = await biller.GetFromJsonAsync<PagedResult<ClaimDto>>(
            $"/api/claims?patientId={patient.Id}&status=Submitted");

        Assert.Single(result!.Items);
        Assert.Equal(submitted.Id, result.Items[0].Id);
        Assert.DoesNotContain(result.Items, c => c.Id == draft.Id);
    }

    [Fact]
    public async Task Page_size_is_capped()
    {
        var biller = await BillerAsync();

        var result = await biller.GetFromJsonAsync<PagedResult<PatientDto>>("/api/patients?pageSize=100000");

        Assert.Equal(Paging.MaxPageSize, result!.PageSize);
    }
}
