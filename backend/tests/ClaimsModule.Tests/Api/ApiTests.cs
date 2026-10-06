using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClaimsModule.Tests.Persistence;
using FluentAssertions;

namespace ClaimsModule.Tests.Api;

[Collection(SqlServerCollection.Name)]
public sealed class ApiTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private ApiFactory _factory = null!;

    public ApiTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        if (_sql.Available)
        {
            _factory = new ApiFactory(_sql);
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private void Require() => Skip.IfNot(_sql.Available, "SQL Server not available");

    private static async Task<Guid> PolicyId(HttpClient client, string q = "meridian") =>
        (await (await client.GetAsync($"/api/policies/search?q={q}")).JsonAsync())[0].GetProperty("id").GetGuid();

    private static object Intake(Guid? policyId, object? initialReserve = null, string description = "Truck collided with a guardrail on the highway") => new
    {
        policyId,
        lossDate = DateTimeOffset.UtcNow.AddDays(-2),
        lossDescription = description,
        causeOfLossCode = "COL-VEH-COL",
        lossLocation = "Highway 9",
        parties = new[] { new { partyRole = "Claimant", partyType = "Person", firstName = "Ada", lastName = "Lovelace" } },
        riskObjects = new[] { new { assetType = "Vehicle", assetDescription = "Volvo FH16" } },
        initialReserve,
    };

    private static async Task<(Guid Id, string Number)> CreateClaim(HttpClient client, Guid? policyId = null, object? reserve = null)
    {
        var response = await client.PostAsJsonAsync("/api/claims", Intake(policyId ?? await PolicyId(client), reserve));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.JsonAsync();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("claimNumber").GetString()!);
    }

    private static Task<HttpResponseMessage> Put(HttpClient c, string url, object body) => c.PutAsJsonAsync(url, body);

    private static async Task<Guid> OpenClaim(HttpClient client, object? reserve = null)
    {
        var (id, _) = await CreateClaim(client, reserve: reserve);
        (await Put(client, $"/api/claims/{id}/status", new { targetStatus = "Open" })).EnsureSuccessStatusCode();
        return id;
    }

    // ------------------------------------------------------------------ authentication

    [SkippableFact]
    public async Task Login_issues_a_token_and_rejects_bad_credentials()
    {
        Require();
        var client = _factory.CreateClient();

        var ok = await client.PostAsJsonAsync("/api/auth/login", new { userName = "supervisor", password = "Supervisor#2026" });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ok.JsonAsync();
        body.GetProperty("user").GetProperty("role").GetString().Should().Be("supervisor");
        body.GetProperty("accessToken").GetString()!.Split('.').Should().HaveCount(3);

        var bad = await client.PostAsJsonAsync("/api/auth/login", new { userName = "supervisor", password = "nope" });
        bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var error = await bad.JsonAsync();
        error.GetProperty("type").GetString().Should().Be("Unauthorized");
        error.GetProperty("title").GetString().Should().Be("Invalid user name or password.");
        error.GetProperty("correlationId").GetGuid().Should().NotBeEmpty("every error body carries a correlation id");
    }

    [SkippableFact]
    public async Task Anonymous_calls_get_a_structured_401_but_health_login_and_swagger_stay_open()
    {
        Require();
        var client = _factory.CreateClient();

        var denied = await client.GetAsync("/api/claims");
        denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await denied.JsonAsync();
        body.GetProperty("type").GetString().Should().Be("Unauthorized");
        body.GetProperty("correlationId").GetGuid().Should().NotBeEmpty();

        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/claims/validate", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task A_tampered_token_is_rejected()
    {
        Require();
        var client = await _factory.AsHandlerAsync();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", client.DefaultRequestHeaders.Authorization!.Parameter![..^3] + "abc");

        (await client.GetAsync("/api/claims")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Role_switcher_lists_users_and_issues_password_less_tokens()
    {
        Require();
        var client = _factory.CreateClient();

        var users = await (await client.GetAsync("/api/auth/users")).JsonAsync();
        users.EnumerateArray().Select(u => u.GetProperty("role").GetString()).Should().Contain(new[] { "handler", "supervisor", "manager" });
        users.ToString().Should().NotContain("2026", "passwords are never exposed");

        var response = await client.PostAsJsonAsync("/api/auth/dev-token", new { userName = "manager" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("user").GetProperty("displayName").GetString().Should().Be("Maria Manager");
    }

    // ------------------------------------------------------------------ claims

    [SkippableFact]
    public async Task Creating_a_claim_returns_201_with_location_number_and_correlation_id_in_the_audit_log()
    {
        Require();
        var client = await _factory.AsHandlerAsync();
        var correlation = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/claims") { Content = JsonContent.Create(Intake(await PolicyId(client))) };
        request.Headers.Add("X-Correlation-Id", correlation.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.AbsolutePath.Should().StartWith("/api/claims/");
        response.Headers.GetValues("X-Correlation-Id").Single().Should().Be(correlation.ToString());
        var body = await response.JsonAsync();
        body.GetProperty("claimNumber").GetString().Should().MatchRegex(@"^CLM-\d{4}-\d{7}$");
        body.GetProperty("status").GetString().Should().Be("Draft");

        var id = body.GetProperty("id").GetGuid();
        var audit = await (await client.GetAsync($"/api/claims/{id}/audit")).JsonAsync();
        audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("correlationId").GetGuid()).Should().OnlyContain(c => c == correlation);
    }

    [SkippableFact]
    public async Task Validation_errors_use_the_structured_422_body()
    {
        Require();
        var client = await _factory.AsHandlerAsync();

        var response = await client.PostAsJsonAsync("/api/claims", new
        {
            lossDate = DateTimeOffset.UtcNow.AddDays(5), lossDescription = "short", causeOfLossCode = "NOPE",
        });

        response.StatusCode.Should().Be((HttpStatusCode)422);
        var body = await response.JsonAsync();
        body.GetProperty("type").GetString().Should().Be("ValidationError");
        body.GetProperty("title").GetString().Should().Be("One or more validation errors occurred.");
        body.GetProperty("status").GetInt32().Should().Be(422);
        var errors = body.GetProperty("errors");
        errors.GetProperty("LossDate")[0].GetString().Should().Be("Loss date cannot be in the future.");
        errors.GetProperty("CauseOfLossCode")[0].GetString().Should().Be("Cause of loss code is not recognised or is inactive.");
    }

    [SkippableFact]
    public async Task Malformed_json_and_bad_enums_are_422s_not_500s()
    {
        Require();
        var client = await _factory.AsHandlerAsync();

        var broken = await client.PostAsync("/api/claims", new StringContent("{ not json", Encoding.UTF8, "application/json"));
        broken.StatusCode.Should().Be((HttpStatusCode)422);
        (await broken.JsonAsync()).GetProperty("type").GetString().Should().Be("ValidationError");

        var (id, _) = await CreateClaim(client);
        var badEnum = await Put(client, $"/api/claims/{id}/status", new { targetStatus = "Teleported" });
        badEnum.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [SkippableFact]
    public async Task Unknown_claim_is_a_structured_404()
    {
        Require();
        var client = await _factory.AsHandlerAsync();

        var response = await client.GetAsync($"/api/claims/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.JsonAsync()).GetProperty("type").GetString().Should().Be("NotFound");
    }

    [SkippableFact]
    public async Task Invalid_status_transition_returns_422_with_valid_next_statuses()
    {
        Require();
        var client = await _factory.AsHandlerAsync();
        var (id, _) = await CreateClaim(client);

        var response = await Put(client, $"/api/claims/{id}/status", new { targetStatus = "Closed" });

        response.StatusCode.Should().Be((HttpStatusCode)422);
        var body = await response.JsonAsync();
        body.GetProperty("title").GetString().Should().Be("Transition from Draft to Closed is not permitted.");
        body.GetProperty("validNextStatuses").EnumerateArray().Select(s => s.GetString()).Should().Equal("Open");
    }

    [SkippableFact]
    public async Task Blocked_closure_lists_the_blocking_conditions_and_preflight_agrees()
    {
        Require();
        var client = await _factory.AsHandlerAsync();
        var id = await OpenClaim(client, reserve: new { component = "Indemnity", amount = 2000 });

        var preflight = await (await client.GetAsync($"/api/claims/{id}/closure-preflight")).JsonAsync();
        preflight.GetProperty("canClose").GetBoolean().Should().BeFalse();
        preflight.GetProperty("requiresJustification").GetBoolean().Should().BeTrue();

        var response = await Put(client, $"/api/claims/{id}/status", new { targetStatus = "Closed" });
        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await response.JsonAsync()).GetProperty("blockingConditions")[0].GetString().Should().Contain("justification");

        var ok = await Put(client, $"/api/claims/{id}/status", new { targetStatus = "Closed", closureJustification = "Settled" });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task List_supports_multi_status_filters_search_and_paging_with_string_enums()
    {
        Require();
        var client = await _factory.AsHandlerAsync();
        var open = await OpenClaim(client);
        var (_, draftNumber) = await CreateClaim(client);

        var response = await client.GetAsync($"/api/claims?status=Open&status=Draft&search={draftNumber}&pageSize=5");
        var body = await response.JsonAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("status").GetString().Should().Be("Draft");
        body.GetProperty("totalCount").GetInt32().Should().Be(1);

        var paged = await (await client.GetAsync("/api/claims?pageSize=1&page=2")).JsonAsync();
        paged.GetProperty("items").GetArrayLength().Should().Be(1);
        paged.GetProperty("page").GetInt32().Should().Be(2);
        paged.GetProperty("totalPages").GetInt32().Should().BeGreaterThan(1);

        (await client.GetAsync("/api/claims?pageSize=1000")).StatusCode.Should().Be((HttpStatusCode)422);
        (await client.GetAsync($"/api/claims/{open}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Removing_the_last_claimant_is_a_422()
    {
        Require();
        var client = await _factory.AsHandlerAsync();
        var (id, _) = await CreateClaim(client);
        var partyId = (await (await client.GetAsync($"/api/claims/{id}")).JsonAsync()).GetProperty("parties")[0].GetProperty("id").GetGuid();

        var response = await client.DeleteAsync($"/api/claims/{id}/parties/{partyId}");

        response.StatusCode.Should().Be((HttpStatusCode)422);

        var add = await client.PostAsJsonAsync($"/api/claims/{id}/parties", new { partyRole = "Claimant", partyType = "Company", companyName = "Acme Ltd" });
        add.StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.DeleteAsync($"/api/claims/{id}/parties/{partyId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task Dry_run_validation_reports_without_saving()
    {
        Require();
        var client = await _factory.AsHandlerAsync();

        var response = await client.PostAsJsonAsync("/api/claims/validate", Intake(null, description: "tiny"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("critical").EnumerateArray().Select(e => e.GetString()).Should().Contain("Loss description is required and must be at least 20 characters.");
        body.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()).Should().Contain(w => w!.StartsWith("No policy linked"));
    }

    // ------------------------------------------------------------------ reserves & roles

    [SkippableFact]
    public async Task Handlers_cannot_approve_but_supervisors_can_and_the_job_is_queued_once()
    {
        Require();
        var handler = await _factory.AsHandlerAsync();
        var supervisor = await _factory.AsSupervisorAsync();
        var claim = await OpenClaim(handler);
        var queued = _factory.Scheduler.Enqueued.Count;

        var submit = await handler.PostAsJsonAsync($"/api/claims/{claim}/reserves", new { component = "Indemnity", amount = 40_000, changeReason = "Estimate" });
        submit.StatusCode.Should().Be(HttpStatusCode.Created);
        var txn = (await submit.JsonAsync()).GetProperty("transaction").GetProperty("id").GetGuid();
        _factory.Scheduler.Enqueued.Count.Should().Be(queued);

        var denied = await handler.PostAsync($"/api/claims/{claim}/reserves/{txn}/approve", null);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.JsonAsync()).GetProperty("type").GetString().Should().Be("Forbidden");

        var approved = await supervisor.PostAsync($"/api/claims/{claim}/reserves/{txn}/approve", null);
        approved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await approved.JsonAsync()).GetProperty("approvalStatus").GetString().Should().Be("Approved");
        _factory.Scheduler.Enqueued.Count.Should().Be(queued + 1);

        var reserves = await (await handler.GetAsync($"/api/claims/{claim}/reserves")).JsonAsync();
        reserves.GetProperty("summary").GetProperty("totalReserves").GetDecimal().Should().Be(40_000m);
    }

    [SkippableFact]
    public async Task Self_approval_is_a_422_and_amount_authority_is_a_403()
    {
        Require();
        var supervisor = await _factory.AsSupervisorAsync();
        var manager = await _factory.AsManagerAsync();
        var handler = await _factory.AsHandlerAsync();
        var claim = await OpenClaim(handler);

        var own = (await (await supervisor.PostAsJsonAsync($"/api/claims/{claim}/reserves", new { component = "Expense", amount = 20_000, changeReason = "x" })).JsonAsync())
            .GetProperty("transaction").GetProperty("id").GetGuid();
        var self = await supervisor.PostAsync($"/api/claims/{claim}/reserves/{own}/approve", null);
        self.StatusCode.Should().Be((HttpStatusCode)422);
        (await self.JsonAsync()).GetProperty("errors").GetProperty("ReserveApproval")[0].GetString().Should().Be("Self-approval is not permitted.");

        var big = (await (await handler.PostAsJsonAsync($"/api/claims/{claim}/reserves", new { component = "Indemnity", amount = 250_000, changeReason = "x" })).JsonAsync())
            .GetProperty("transaction").GetProperty("id").GetGuid();
        (await supervisor.PostAsync($"/api/claims/{claim}/reserves/{big}/approve", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.PostAsync($"/api/claims/{claim}/reserves/{big}/approve", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Reject_retract_and_adjust_work_over_http()
    {
        Require();
        var handler = await _factory.AsHandlerAsync();
        var handler2 = await _factory.AsHandler2Async();
        var supervisor = await _factory.AsSupervisorAsync();
        var claim = await OpenClaim(handler);

        var first = (await (await handler.PostAsJsonAsync($"/api/claims/{claim}/reserves", new { component = "Indemnity", amount = 30_000, changeReason = "x" })).JsonAsync())
            .GetProperty("transaction");
        var reject = await supervisor.PostAsJsonAsync($"/api/claims/{claim}/reserves/{first.GetProperty("id").GetGuid()}/reject", new { rejectionReason = "Too high" });
        reject.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reject.JsonAsync()).GetProperty("approvalStatus").GetString().Should().Be("Rejected");

        var second = (await (await handler.PostAsJsonAsync($"/api/claims/{claim}/reserves", new { component = "Expense", amount = 15_000, changeReason = "x" })).JsonAsync())
            .GetProperty("transaction").GetProperty("id").GetGuid();
        (await handler2.PostAsync($"/api/claims/{claim}/reserves/{second}/retract", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var retract = await handler.PostAsync($"/api/claims/{claim}/reserves/{second}/retract", null);
        (await retract.JsonAsync()).GetProperty("approvalStatus").GetString().Should().Be("Cancelled");

        var small = await (await handler.PostAsJsonAsync($"/api/claims/{claim}/reserves", new { component = "ALAE", amount = 1_000, changeReason = "x" })).JsonAsync();
        var component = small.GetProperty("transaction").GetProperty("reserveComponentId").GetGuid();
        var adjust = await Put(handler, $"/api/claims/{claim}/reserves/{component}", new { amount = 500, changeReason = "More" });
        adjust.StatusCode.Should().Be(HttpStatusCode.OK);
        (await adjust.JsonAsync()).GetProperty("transaction").GetProperty("transactionType").GetString().Should().Be("Adjust");
    }

    [SkippableFact]
    public async Task Only_managers_can_set_the_override_flag()
    {
        Require();
        var handler = await _factory.AsHandlerAsync();
        var manager = await _factory.AsManagerAsync();
        var claim = await OpenClaim(handler);

        (await Put(handler, $"/api/claims/{claim}/manager-override", new { value = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Put(manager, $"/api/claims/{claim}/manager-override", new { value = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await handler.GetAsync($"/api/claims/{claim}")).JsonAsync()).GetProperty("managerOverride").GetBoolean().Should().BeTrue();
    }

    // ------------------------------------------------------------------ idempotency

    [SkippableFact]
    public async Task A_repeated_idempotency_key_replays_the_response_and_creates_nothing_new()
    {
        Require();
        var client = await _factory.AsHandlerAsync();
        var payload = Intake(await PolicyId(client));
        var before = (await (await client.GetAsync("/api/claims?pageSize=1")).JsonAsync()).GetProperty("totalCount").GetInt32();

        async Task<HttpResponseMessage> Post(object body, string key)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/claims") { Content = JsonContent.Create(body) };
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }

        var first = await Post(payload, "key-abc");
        var second = await Post(payload, "key-abc");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.GetValues("Idempotent-Replayed").Single().Should().Be("true");
        first.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        (await second.JsonAsync()).GetProperty("claimNumber").GetString().Should().Be((await first.JsonAsync()).GetProperty("claimNumber").GetString());
        second.Headers.Location.Should().Be(first.Headers.Location);
        (await (await client.GetAsync("/api/claims?pageSize=1")).JsonAsync()).GetProperty("totalCount").GetInt32().Should().Be(before + 1);

        var different = await Post(Intake(await PolicyId(client), description: "A different description of the loss event"), "key-abc");
        different.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [SkippableFact]
    public async Task Concurrent_requests_with_one_key_create_a_single_claim()
    {
        Require();
        var client = await _factory.AsHandlerAsync();
        var payload = Intake(await PolicyId(client));
        var before = (await (await client.GetAsync("/api/claims?pageSize=1")).JsonAsync()).GetProperty("totalCount").GetInt32();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/claims") { Content = JsonContent.Create(payload) };
            request.Headers.Add("Idempotency-Key", "parallel-key");
            return await client.SendAsync(request);
        }));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        (await Task.WhenAll(responses.Select(async r => (await r.JsonAsync()).GetProperty("claimNumber").GetString()))).Distinct().Should().ContainSingle();
        (await (await client.GetAsync("/api/claims?pageSize=1")).JsonAsync()).GetProperty("totalCount").GetInt32().Should().Be(before + 1);
    }

    [SkippableFact]
    public async Task Without_a_key_each_request_creates_a_claim()
    {
        Require();
        var client = await _factory.AsHandlerAsync();

        var (_, a) = await CreateClaim(client);
        var (_, b) = await CreateClaim(client);

        a.Should().NotBe(b);
    }

    // ------------------------------------------------------------------ documents

    [SkippableFact]
    public async Task Upload_then_download_through_the_signed_link_round_trips_the_bytes()
    {
        Require();
        var client = await _factory.AsHandlerAsync();
        var (claim, _) = await CreateClaim(client);

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("incident,date\nfire,2026-10-01")) { Headers = { ContentType = new("text/csv") } }, "file", "..\\..\\report.csv" },
            { new StringContent("PoliceReport"), "documentType" },
            { new StringContent("scan"), "notes" },
        };
        var upload = await client.PostAsync($"/api/claims/{claim}/documents", form);
        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        (await upload.JsonAsync()).GetProperty("documentName").GetString().Should().Be("report.csv");

        var docs = await (await client.GetAsync($"/api/claims/{claim}/documents")).JsonAsync();
        var url = new Uri(docs[0].GetProperty("downloadUrl").GetString()!);
        url.AbsolutePath.Should().StartWith("/api/files/").And.NotContain("..");

        var anonymous = _factory.CreateClient(); // signed links need no bearer token
        var download = await anonymous.GetAsync(url.PathAndQuery);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsStringAsync()).Should().Be("incident,date\nfire,2026-10-01");
        download.Content.Headers.ContentDisposition!.FileName.Should().Be("report.csv");

        var forged = await anonymous.GetAsync(url.PathAndQuery[..^3] + "000");
        forged.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var expired = await anonymous.GetAsync(url.AbsolutePath + "?expires=1&sig=abc");
        expired.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var audit = await (await client.GetAsync($"/api/claims/{claim}/audit")).JsonAsync();
        audit.GetProperty("items").EnumerateArray().Should().Contain(a => a.GetProperty("eventType").GetString() == "DOCUMENT_UPLOADED");
    }

    [SkippableFact]
    public async Task Disallowed_file_types_and_missing_files_are_422()
    {
        Require();
        var client = await _factory.AsHandlerAsync();
        var (claim, _) = await CreateClaim(client);

        using var exe = new MultipartFormDataContent
        {
            { new ByteArrayContent(new byte[] { 1, 2, 3 }) { Headers = { ContentType = new("application/x-msdownload") } }, "file", "run.exe" },
        };
        (await client.PostAsync($"/api/claims/{claim}/documents", exe)).StatusCode.Should().Be((HttpStatusCode)422);

        using var empty = new MultipartFormDataContent { { new StringContent("PoliceReport"), "documentType" } };
        (await client.PostAsync($"/api/claims/{claim}/documents", empty)).StatusCode.Should().Be((HttpStatusCode)422);
    }

    // ------------------------------------------------------------------ reference data & docs

    [SkippableFact]
    public async Task Reference_endpoints_return_seed_data()
    {
        Require();
        var client = await _factory.AsHandlerAsync();

        (await (await client.GetAsync("/api/reference/cause-of-loss-codes")).JsonAsync()).GetArrayLength().Should().Be(10);
        (await (await client.GetAsync("/api/reference/cause-of-loss-codes?perilCategory=Auto")).JsonAsync()).GetArrayLength().Should().Be(2);
        (await (await client.GetAsync("/api/reference/claim-statuses")).JsonAsync()).GetArrayLength().Should().Be(7);

        var policy = (await (await client.GetAsync("/api/policies/search?q=stanton")).JsonAsync())[0];
        policy.GetProperty("policyNumber").GetString().Should().Be("POL-2025-002002");
        var coverage = await (await client.GetAsync($"/api/policies/{policy.GetProperty("id").GetGuid()}/coverage")).JsonAsync();
        coverage.GetProperty("coverageTypes").EnumerateArray().Select(c => c.GetString()).Should().Equal("Liability", "Vehicle");
        (await client.GetAsync($"/api/policies/{Guid.NewGuid()}/coverage")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Swagger_documents_every_endpoint_with_bearer_security_and_string_enums()
    {
        Require();
        var client = _factory.CreateClient();

        var doc = await (await client.GetAsync("/swagger/v1/swagger.json")).JsonAsync();
        var paths = doc.GetProperty("paths");
        foreach (var expected in new[]
        {
            "/api/claims", "/api/claims/{id}", "/api/claims/{id}/status", "/api/claims/{id}/audit", "/api/claims/{id}/documents",
            "/api/claims/{claimId}/reserves", "/api/claims/{claimId}/reserves/{txnId}/approve", "/api/policies/search",
            "/api/reference/cause-of-loss-codes", "/api/reference/claim-statuses",
        })
        {
            paths.TryGetProperty(expected, out _).Should().BeTrue($"{expected} should be documented");
        }

        doc.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _).Should().BeTrue();
        doc.ToString().Should().Contain("UnderInvestigation", "enums are documented by name");
    }
}
