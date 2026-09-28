using System.ComponentModel.DataAnnotations;
using ClaimsApi.Api.Auth;
using ClaimsApi.Application;
using ClaimsApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsApi.Api.Controllers;

public record DenyClaimRequest([Required, StringLength(500)] string Reason);

[ApiController]
[Authorize]
[Route("api/patients")]
public class PatientsController(PatientService patients) : ControllerBase
{
    /// <summary>Searches patients by name or insurance ID (paged).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<PatientDto>>> Search(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await patients.SearchAsync(q, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PatientDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await patients.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = Roles.Biller)]
    public async Task<ActionResult<PatientDto>> Create(CreatePatientRequest request, CancellationToken ct)
    {
        var patient = await patients.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = patient.Id }, patient);
    }
}

[ApiController]
[Authorize]
[Route("api/claims")]
public class ClaimsController(ClaimService claims) : ControllerBase
{
    /// <summary>Lists claims, optionally filtered by status and patient (paged).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ClaimDto>>> List(
        [FromQuery] ClaimStatus? status, [FromQuery] Guid? patientId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await claims.ListAsync(status, patientId, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClaimDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await claims.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = Roles.Biller)]
    public async Task<ActionResult<ClaimDto>> Create(CreateClaimRequest request, CancellationToken ct)
    {
        var claim = await claims.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = claim.Id }, claim);
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = Roles.Biller)]
    public async Task<ActionResult<ClaimDto>> Submit(Guid id, CancellationToken ct) =>
        Ok(await claims.SubmitAsync(id, ct));

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = Roles.Reviewer)]
    public async Task<ActionResult<ClaimDto>> Approve(Guid id, CancellationToken ct) =>
        Ok(await claims.ApproveAsync(id, ct));

    [HttpPost("{id:guid}/deny")]
    [Authorize(Roles = Roles.Reviewer)]
    public async Task<ActionResult<ClaimDto>> Deny(Guid id, DenyClaimRequest request, CancellationToken ct) =>
        Ok(await claims.DenyAsync(id, request.Reason, ct));

    [HttpPost("{id:guid}/pay")]
    [Authorize(Roles = Roles.Biller)]
    public async Task<ActionResult<ClaimDto>> Pay(Guid id, CancellationToken ct) =>
        Ok(await claims.PayAsync(id, ct));
}
