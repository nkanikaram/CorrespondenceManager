using ProcessRequestEndPoint.Sla;

namespace PortalAuthTests;

[TestClass]
public class SlaCalculatorTests
{
    private static SlaConfig Config(bool businessDays = false, decimal days = 5, int atRisk = 80) => new()
    {
        BusinessDays = businessDays,
        DefaultDays = days,
        AtRiskPercent = atRisk,
        StopStatuses = new() { "Delivered", "Archived" },
        Customers = new() { new SlaCustomerTarget { CustomerName = "Claims System", TargetDays = 2 } }
    };

    // Monday 3 Nov 2025, 09:00 UTC
    private static readonly DateTime Monday = new(2025, 11, 3, 9, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void OpenRequests_GoOnTrack_AtRisk_Breached()
    {
        var c = Config();
        Assert.AreEqual("OnTrack", SlaCalculator.Evaluate(Monday, "New", null, "Life Services", c, Monday.AddDays(1)).State);
        Assert.AreEqual("AtRisk", SlaCalculator.Evaluate(Monday, "New", null, "Life Services", c, Monday.AddDays(4.1)).State);
        var late = SlaCalculator.Evaluate(Monday, "Failed", null, "Life Services", c, Monday.AddDays(6));
        Assert.AreEqual("Breached", late.State, "Failed doesn't stop the clock");
        Assert.AreEqual(120, late.PercentUsed);
        Assert.AreEqual(Monday.AddDays(5), late.DueUtc);
        Assert.AreEqual((long)TimeSpan.FromDays(6).TotalSeconds, late.ElapsedSeconds);
    }

    [TestMethod]
    public void FinishedRequests_AreMetOrMissed_AndTheClockStops()
    {
        var c = Config();
        var met = SlaCalculator.Evaluate(Monday, "Delivered", Monday.AddDays(3), "Life Services", c, Monday.AddDays(30));
        Assert.AreEqual("Met", met.State);
        Assert.AreEqual(Monday.AddDays(3), met.StoppedUtc);
        Assert.AreEqual((long)TimeSpan.FromDays(3).TotalSeconds, met.ElapsedSeconds, "time lapse stops at the finishing status");
        Assert.AreEqual("Missed", SlaCalculator.Evaluate(Monday, "archived", Monday.AddDays(9), "Life Services", c, Monday.AddDays(30)).State);
    }

    [TestMethod]
    public void CustomerTarget_OverridesTheDefault()
    {
        var r = SlaCalculator.Evaluate(Monday, "New", null, "claims system", Config(), Monday.AddDays(3));
        Assert.AreEqual(2m, r.TargetDays);
        Assert.AreEqual("Breached", r.State);
    }

    [TestMethod]
    public void BusinessDays_SkipTheWeekend()
    {
        var c = Config(businessDays: true, days: 5);
        // Friday 09:00 + 5 business days = next Friday 09:00
        var friday = Monday.AddDays(4);
        Assert.AreEqual(friday.AddDays(7), SlaCalculator.AddBusinessTime(friday, TimeSpan.FromDays(5)));
        // Created Saturday: the clock starts Monday 00:00, so 5 business days end as Friday ends (Saturday 00:00)
        var saturday = Monday.AddDays(5);
        Assert.AreEqual(new DateTime(2025, 11, 15, 0, 0, 0, DateTimeKind.Utc), SlaCalculator.AddBusinessTime(saturday, TimeSpan.FromDays(5)));
        // Friday 09:00 → Monday 09:00 is one business day
        Assert.AreEqual(TimeSpan.FromDays(1), SlaCalculator.BusinessTime(friday, friday.AddDays(3)));
        Assert.AreEqual(TimeSpan.FromDays(10), SlaCalculator.BusinessTime(Monday, Monday.AddDays(14)));
        var r = SlaCalculator.Evaluate(friday, "New", null, "Life Services", c, friday.AddDays(3));
        Assert.AreEqual(20, r.PercentUsed);
        Assert.AreEqual("OnTrack", r.State);
    }

    [TestMethod]
    public void Cutoffs_MatchEvaluate_ForEveryCreationTime()
    {
        // The grid's SLA filter compares creation times with these cutoffs in SQL; it must agree with the SLA column.
        var random = new Random(7);
        foreach (var businessDays in new[] { true, false })
        foreach (var days in new[] { 0.5m, 1m, 2.5m, 5m, 12m })
        {
            var c = Config(businessDays, days, atRisk: 75);
            for (var n = 0; n < 400; n++)
            {
                // "Now" on any day of the week, sometimes exactly at midnight.
                var now = Monday.AddMinutes(random.Next(0, 14 * 24 * 60));
                if (n % 10 == 0) now = now.Date;
                var created = now.AddMinutes(-random.Next(0, (int)(days * 3 * 24 * 60) + 3 * 24 * 60));
                if (n % 7 == 0) created = created.Date; // midnight, e.g. the start of a weekend
                var (breachedBefore, atRiskUpTo) = SlaCalculator.Cutoffs(days, c, now);
                var expected = created < breachedBefore ? "Breached" : created <= atRiskUpTo ? "AtRisk" : "OnTrack";
                var actual = SlaCalculator.Evaluate(created, "New", null, "Life Services", c, now).State;
                Assert.AreEqual(expected, actual, $"business={businessDays} days={days} created={created:u} now={now:u}");
            }
        }
    }

    [TestMethod]
    public void LatestStartWithUsed_StepsBackOverWeekends()
    {
        // Monday 09:00 minus one business day = Friday 09:00.
        Assert.AreEqual(Monday.AddDays(-3), SlaCalculator.LatestStartWithUsed(Monday, TimeSpan.FromDays(1), true));
        // From Sunday noon, 2 business hours back = Friday 22:00.
        var sunday = Monday.AddDays(-1).Date.AddHours(12);
        Assert.AreEqual(Monday.Date.AddDays(-3).AddHours(22), SlaCalculator.LatestStartWithUsed(sunday, TimeSpan.FromHours(2), true));
        Assert.AreEqual(Monday.AddDays(-2), SlaCalculator.LatestStartWithUsed(Monday, TimeSpan.FromDays(2), false));
    }

    [TestMethod]
    public void SlaGroups_AcceptFriendlySpellings()
    {
        Assert.AreEqual("At risk", SlaSql.NormalizeGroup("AtRisk"));
        Assert.AreEqual("At risk", SlaSql.NormalizeGroup("at risk"));
        Assert.AreEqual("On track", SlaSql.NormalizeGroup("on-track"));
        Assert.AreEqual("Finished", SlaSql.NormalizeGroup("Met"));
        Assert.AreEqual("Breached", SlaSql.NormalizeGroup("BREACHED"));
        Assert.IsNull(SlaSql.NormalizeGroup("late"));
    }

    [TestMethod]
    public void Validate_RejectsBadSettings()
    {
        var bad = Config(days: 0, atRisk: 100);
        bad.StopStatuses.Clear();
        bad.Customers.Add(new SlaCustomerTarget { CustomerName = "CLAIMS SYSTEM", TargetDays = 3 });
        var problems = SlaService.Validate(bad);
        Assert.AreEqual(4, problems.Count, string.Join(" | ", problems));
        Assert.AreEqual(0, SlaService.Validate(Config()).Count);
    }
}
