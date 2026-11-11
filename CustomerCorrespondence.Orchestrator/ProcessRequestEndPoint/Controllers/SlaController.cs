using Microsoft.AspNetCore.Mvc;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Sla;

namespace ProcessRequestEndPoint.Controllers
{
    /// <summary>The SLA settings shown and edited under Administration > SLA.</summary>
    [ApiController]
    [Route("api/portal/sla")]
    public class SlaController : ControllerBase
    {
        private readonly SlaService _sla;
        private readonly PortalAccessService _access;
        private readonly AuthRepository _auth;

        public SlaController(SlaService sla, PortalAccessService access, AuthRepository auth)
        {
            _sla = sla;
            _access = access;
            _auth = auth;
        }

        [RequirePermission(Permissions.SlaManage)]
        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var config = await _sla.GetAsync(ct);
            return config == null
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "The SLA tables don't exist yet. Run Build/DBScripts/Portal/004_Sla.sql." })
                : Ok(config);
        }

        [RequirePermission(Permissions.SlaManage)]
        [HttpPut]
        public async Task<IActionResult> Save([FromBody] SlaConfig change, CancellationToken ct)
        {
            if (change == null) return BadRequest(new { message = "Nothing to save." });
            change.StopStatuses = (change.StopStatuses ?? new()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            change.Customers = (change.Customers ?? new()).Where(t => t != null).ToList();
            var problems = SlaService.Validate(change);
            if (problems.Count > 0) return BadRequest(new { message = "Check the SLA settings.", problems });

            var me = await _access.CurrentAsync();
            await _sla.SaveAsync(change, me!.UserName, ct);
            await _auth.WriteAuditAsync(me.UserId, me.UserName, "SlaChanged", "Success",
                $"Default {change.DefaultDays:0.##} {(change.BusinessDays ? "business" : "calendar")} days, at risk at {change.AtRiskPercent}%, " +
                $"{change.Customers.Count} customer target(s)", _access.ClientIp());
            return Ok(await _sla.GetAsync(ct));
        }
    }
}
