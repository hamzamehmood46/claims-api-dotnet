using ClaimsApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClaimsApi.Application;

public class NotFoundException(string message) : Exception(message);

public interface IClaimsDbContext
{
    DbSet<Patient> Patients { get; }
    DbSet<Claim> Claims { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public record CreatePatientRequest(string FirstName, string LastName, DateOnly DateOfBirth, string InsuranceId);
public record PatientDto(Guid Id, string FirstName, string LastName, DateOnly DateOfBirth, string InsuranceId)
{
    public static PatientDto From(Patient p) => new(p.Id, p.FirstName, p.LastName, p.DateOfBirth, p.InsuranceId);
}

public record ClaimLineRequest(string ProcedureCode, string? Description, decimal Amount);
public record CreateClaimRequest(Guid PatientId, List<ClaimLineRequest> Lines);
public record ClaimLineDto(string ProcedureCode, string Description, decimal Amount);
public record ClaimDto(
    Guid Id, Guid PatientId, ClaimStatus Status, decimal Total, string? DecisionReason,
    DateTime CreatedAt, DateTime? SubmittedAt, DateTime? DecidedAt, DateTime? PaidAt,
    IReadOnlyList<ClaimLineDto> Lines)
{
    public static ClaimDto From(Claim c) => new(
        c.Id, c.PatientId, c.Status, c.Total, c.DecisionReason,
        c.CreatedAt, c.SubmittedAt, c.DecidedAt, c.PaidAt,
        c.Lines.Select(l => new ClaimLineDto(l.ProcedureCode, l.Description, l.Amount)).ToList());
}

public static class Paging
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));
}

public class PatientService(IClaimsDbContext db)
{
    public async Task<PatientDto> CreateAsync(CreatePatientRequest request, CancellationToken ct)
    {
        var patient = Patient.Create(request.FirstName, request.LastName, request.DateOfBirth, request.InsuranceId);
        db.Patients.Add(patient);
        await db.SaveChangesAsync(ct);
        return PatientDto.From(patient);
    }

    public async Task<PatientDto> GetAsync(Guid id, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException($"Patient {id} was not found.");
        return PatientDto.From(patient);
    }

    public async Task<PagedResult<PatientDto>> SearchAsync(string? query, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);

        var patients = db.Patients.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLower();
            patients = patients.Where(p =>
                p.LastName.ToLower().Contains(q) || p.FirstName.ToLower().Contains(q) || p.InsuranceId.ToLower().Contains(q));
        }

        var total = await patients.CountAsync(ct);
        var items = await patients
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<PatientDto>(items.Select(PatientDto.From).ToList(), page, pageSize, total);
    }
}

public class ClaimService(IClaimsDbContext db)
{
    public async Task<ClaimDto> CreateAsync(CreateClaimRequest request, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == request.PatientId, ct))
            throw new NotFoundException($"Patient {request.PatientId} was not found.");

        var claim = Claim.Create(
            request.PatientId,
            request.Lines.Select(l => new ClaimLine(l.ProcedureCode, l.Description ?? string.Empty, l.Amount)));

        db.Claims.Add(claim);
        await db.SaveChangesAsync(ct);
        return ClaimDto.From(claim);
    }

    public async Task<ClaimDto> GetAsync(Guid id, CancellationToken ct) =>
        ClaimDto.From(await LoadAsync(id, tracking: false, ct));

    public async Task<PagedResult<ClaimDto>> ListAsync(ClaimStatus? status, Guid? patientId, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);

        var claims = db.Claims.AsNoTracking().Include(c => c.Lines).AsQueryable();
        if (status is not null) claims = claims.Where(c => c.Status == status);
        if (patientId is not null) claims = claims.Where(c => c.PatientId == patientId);

        var total = await claims.CountAsync(ct);
        var items = await claims
            .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<ClaimDto>(items.Select(ClaimDto.From).ToList(), page, pageSize, total);
    }

    public Task<ClaimDto> SubmitAsync(Guid id, CancellationToken ct) => TransitionAsync(id, c => c.Submit(), ct);
    public Task<ClaimDto> ApproveAsync(Guid id, CancellationToken ct) => TransitionAsync(id, c => c.Approve(), ct);
    public Task<ClaimDto> DenyAsync(Guid id, string reason, CancellationToken ct) => TransitionAsync(id, c => c.Deny(reason), ct);
    public Task<ClaimDto> PayAsync(Guid id, CancellationToken ct) => TransitionAsync(id, c => c.MarkPaid(), ct);

    private async Task<ClaimDto> TransitionAsync(Guid id, Action<Claim> transition, CancellationToken ct)
    {
        var claim = await LoadAsync(id, tracking: true, ct);
        transition(claim);
        await db.SaveChangesAsync(ct);
        return ClaimDto.From(claim);
    }

    private async Task<Claim> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = db.Claims.Include(c => c.Lines).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Claim {id} was not found.");
    }
}
