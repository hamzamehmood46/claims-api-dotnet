using ClaimsApi.Domain;

namespace ClaimsApi.Tests;

public class ClaimLifecycleTests
{
    private static Claim NewClaim() =>
        Claim.Create(Guid.NewGuid(), [new ClaimLine("99213", "Office visit", 120m), new ClaimLine("85025", "Blood count", 30.50m)]);

    [Fact]
    public void Total_sums_line_amounts()
    {
        Assert.Equal(150.50m, NewClaim().Total);
    }

    [Fact]
    public void Happy_path_draft_to_paid()
    {
        var claim = NewClaim();

        claim.Submit();
        claim.Approve();
        claim.MarkPaid();

        Assert.Equal(ClaimStatus.Paid, claim.Status);
        Assert.NotNull(claim.SubmittedAt);
        Assert.NotNull(claim.DecidedAt);
        Assert.NotNull(claim.PaidAt);
    }

    [Fact]
    public void Denial_records_reason()
    {
        var claim = NewClaim();
        claim.Submit();

        claim.Deny("Not covered");

        Assert.Equal(ClaimStatus.Denied, claim.Status);
        Assert.Equal("Not covered", claim.DecisionReason);
    }

    [Fact]
    public void Denial_requires_a_reason()
    {
        var claim = NewClaim();
        claim.Submit();

        Assert.Throws<DomainValidationException>(() => claim.Deny(" "));
    }

    [Fact]
    public void Draft_claim_cannot_be_approved_or_paid()
    {
        var claim = NewClaim();

        Assert.Throws<DomainException>(claim.Approve);
        Assert.Throws<DomainException>(claim.MarkPaid);
    }

    [Fact]
    public void Claim_cannot_be_submitted_twice()
    {
        var claim = NewClaim();
        claim.Submit();

        Assert.Throws<DomainException>(claim.Submit);
    }

    [Fact]
    public void Denied_claim_cannot_be_paid()
    {
        var claim = NewClaim();
        claim.Submit();
        claim.Deny("Duplicate");

        Assert.Throws<DomainException>(claim.MarkPaid);
    }

    [Fact]
    public void Claim_needs_lines_and_positive_amounts()
    {
        Assert.Throws<DomainValidationException>(() => Claim.Create(Guid.NewGuid(), []));
        Assert.Throws<DomainValidationException>(() => new ClaimLine("99213", "x", 0m));
        Assert.Throws<DomainValidationException>(() => new ClaimLine("", "x", 10m));
    }

    [Fact]
    public void Procedure_codes_are_normalised()
    {
        Assert.Equal("J3490", new ClaimLine(" j3490 ", "Drug", 10m).ProcedureCode);
    }

    [Fact]
    public void Patient_rejects_future_birth_date_and_blank_names()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        Assert.Throws<DomainValidationException>(() => Patient.Create("A", "B", tomorrow, "INS-1"));
        Assert.Throws<DomainValidationException>(() => Patient.Create("", "B", new DateOnly(1990, 1, 1), "INS-1"));
    }
}
