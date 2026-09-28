namespace ClaimsApi.Domain;

public enum ClaimStatus
{
    Draft,
    Submitted,
    Approved,
    Denied,
    Paid
}

/// <summary>Raised when a business rule is violated (for example an illegal status transition).</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Raised when input is invalid (as opposed to a valid request that conflicts with current state).</summary>
public class DomainValidationException(string message) : DomainException(message);

public class Patient
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public DateOnly DateOfBirth { get; private set; }
    public string InsuranceId { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private Patient() { }

    public static Patient Create(string firstName, string lastName, DateOnly dateOfBirth, string insuranceId)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new DomainValidationException("First name is required.");
        if (string.IsNullOrWhiteSpace(lastName)) throw new DomainValidationException("Last name is required.");
        if (string.IsNullOrWhiteSpace(insuranceId)) throw new DomainValidationException("Insurance ID is required.");
        if (dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow)) throw new DomainValidationException("Date of birth cannot be in the future.");

        return new Patient
        {
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            DateOfBirth = dateOfBirth,
            InsuranceId = insuranceId.Trim()
        };
    }
}

public class ClaimLine
{
    public int Id { get; private set; }
    public string ProcedureCode { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }

    private ClaimLine() { }

    public ClaimLine(string procedureCode, string description, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(procedureCode)) throw new DomainValidationException("Procedure code is required.");
        if (amount <= 0) throw new DomainValidationException("Line amount must be greater than zero.");

        ProcedureCode = procedureCode.Trim().ToUpperInvariant();
        Description = description?.Trim() ?? string.Empty;
        Amount = amount;
    }
}

/// <summary>
/// A billing claim. Lifecycle: Draft -> Submitted -> (Approved | Denied); Approved -> Paid.
/// All transitions are enforced here so no caller can put a claim in an invalid state.
/// </summary>
public class Claim
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PatientId { get; private set; }
    public ClaimStatus Status { get; private set; } = ClaimStatus.Draft;
    public List<ClaimLine> Lines { get; private set; } = new();
    public string? DecisionReason { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }

    public decimal Total => Lines.Sum(l => l.Amount);

    private Claim() { }

    public static Claim Create(Guid patientId, IEnumerable<ClaimLine> lines)
    {
        var claim = new Claim { PatientId = patientId, Lines = lines.ToList() };
        if (claim.Lines.Count == 0) throw new DomainValidationException("A claim needs at least one line.");
        return claim;
    }

    public void Submit()
    {
        Require(ClaimStatus.Draft, "Only draft claims can be submitted.");
        Status = ClaimStatus.Submitted;
        SubmittedAt = DateTime.UtcNow;
    }

    public void Approve()
    {
        Require(ClaimStatus.Submitted, "Only submitted claims can be approved.");
        Status = ClaimStatus.Approved;
        DecidedAt = DateTime.UtcNow;
    }

    public void Deny(string reason)
    {
        Require(ClaimStatus.Submitted, "Only submitted claims can be denied.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainValidationException("A denial reason is required.");
        Status = ClaimStatus.Denied;
        DecisionReason = reason.Trim();
        DecidedAt = DateTime.UtcNow;
    }

    public void MarkPaid()
    {
        Require(ClaimStatus.Approved, "Only approved claims can be paid.");
        Status = ClaimStatus.Paid;
        PaidAt = DateTime.UtcNow;
    }

    private void Require(ClaimStatus expected, string message)
    {
        if (Status != expected) throw new DomainException($"{message} Current status: {Status}.");
    }
}

