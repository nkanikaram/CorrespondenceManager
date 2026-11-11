using CustomerCorrespondence.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using ProcessRequestEndPoint.Auth;

namespace ProcessRequestEndPoint.Controllers
{
    /// <summary>
    /// Portal-facing write operations. Each one authorizes the signed-in user (RBAC permission +
    /// ABAC policies on the target record) and then hands the request to the existing
    /// CommManagerController pipeline unchanged, so validation, document ids and messaging stay
    /// in one place. The original /api/CommManager endpoints are untouched for system callers.
    /// </summary>
    [ApiController]
    [Route("api/portal/correspondence")]
    public class PortalCorrespondenceController : ControllerBase
    {
        private readonly PortalAccessService _access;
        private readonly AuthRepository _repository;

        public PortalCorrespondenceController(PortalAccessService access, AuthRepository repository)
        {
            _access = access;
            _repository = repository;
        }

        /// <summary>Creates a correspondence request on behalf of the signed-in user.</summary>
        [RequirePermission(Permissions.CorrespondenceCreate)]
        [HttpPost("requests")]
        public async Task<IActionResult> Create([FromBody] SequentialProcessRequest request)
        {
            var resource = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer"] = request?.Process?.RequestingSystemName?.Trim(),
                ["deliveryMethod"] = request?.Process?.DeliveryMethod?.Trim()
            };
            // Report a missing customer as the validation error it is (the scope policy would deny it anyway).
            if (string.IsNullOrWhiteSpace(resource["customer"]))
                return BadRequest(new { message = "RequestingSystemName is required." });

            var decision = await _access.CheckAsync(Permissions.CorrespondenceCreate, resource);
            if (!decision.Allowed)
            {
                await _access.AuditDeniedAsync(Permissions.CorrespondenceCreate, decision, $"customer '{resource["customer"]}'");
                return Forbidden(decision);
            }

            var result = await ProcessAsync(request!);
            await AuditOutcomeAsync("CorrespondenceCreated", result, $"customer '{resource["customer"]}'");
            return result;
        }

        /// <summary>
        /// Resubmits an existing record's original payload as a new request. Policies see the stored
        /// record (its customer and latest status), and the payload must belong to that record's customer.
        /// </summary>
        [RequirePermission(Permissions.CorrespondenceResubmit)]
        [HttpPost("{correspondenceId:guid}/resubmit")]
        public async Task<IActionResult> Resubmit(Guid correspondenceId, [FromBody] SequentialProcessRequest request)
        {
            var resource = await _repository.GetCorrespondenceResourceAsync(correspondenceId);
            if (resource == null) return NotFound(new { message = "That correspondence record doesn't exist." });

            var decision = await _access.CheckAsync(Permissions.CorrespondenceResubmit, resource);
            if (!decision.Allowed)
            {
                await _access.AuditDeniedAsync(Permissions.CorrespondenceResubmit, decision, $"record {correspondenceId}");
                return Forbidden(decision);
            }

            var payloadCustomer = request?.Process?.RequestingSystemName?.Trim();
            if (!string.Equals(payloadCustomer, resource["customer"], StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "The payload's requesting system doesn't match the original record." });

            var result = await ProcessAsync(request!);
            await AuditOutcomeAsync("CorrespondenceResubmitted", result, $"record {correspondenceId}", correspondenceId);
            return result;
        }

        public sealed class DeleteCorrespondenceRequest
        {
            public List<Guid> CorrespondenceIds { get; set; } = new();
        }

        private const int MaxDeleteBatch = 500;

        /// <summary>
        /// Deletes records by moving them (with their status history and PDF) to the recycle bin, where they can be
        /// restored for RecycleBinDays before the retention job purges them. Each record is checked against the
        /// access policies (e.g. customer scope); refused ones are reported back with the reason.
        /// </summary>
        [RequirePermission(Permissions.CorrespondenceDelete)]
        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] DeleteCorrespondenceRequest request)
        {
            var ids = (request?.CorrespondenceIds ?? new List<Guid>()).Where(id => id != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0) return BadRequest(new { message = "Select at least one record to delete." });
            if (ids.Count > MaxDeleteBatch) return BadRequest(new { message = $"Delete at most {MaxDeleteBatch} records at a time." });

            var allowed = new List<Guid>();
            var denied = new List<object>();
            var notFound = new List<Guid>();
            foreach (var id in ids)
            {
                var resource = await _repository.GetCorrespondenceResourceAsync(id);
                if (resource == null) { notFound.Add(id); continue; }
                var decision = await _access.CheckAsync(Permissions.CorrespondenceDelete, resource);
                if (decision.Allowed) { allowed.Add(id); continue; }
                denied.Add(new { correspondenceId = id, message = decision.Message, policy = decision.PolicyName });
                await _access.AuditDeniedAsync(Permissions.CorrespondenceDelete, decision, $"record {id}");
            }

            var user = await _access.CurrentAsync();
            var bin = HttpContext.RequestServices.GetRequiredService<Records.RecycleBinRepository>();
            var retention = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<Records.RetentionOptions>>().Value;
            var moved = await bin.RecycleAsync(allowed, user?.UserId, user?.UserName, "Deleted in portal", retention.RecycleBinDays, HttpContext.RequestAborted);
            foreach (var id in moved)
                await _repository.WriteAuditAsync(user?.UserId, user?.UserName, "CorrespondenceDeleted", "Success",
                    $"record {id} moved to the recycle bin", _access.ClientIp(), id);
            return Ok(new { deleted = moved, denied, notFound, recycleBinDays = retention.RecycleBinDays });
        }

        private async Task<IActionResult> ProcessAsync(SequentialProcessRequest request)
        {
            var inner = ActivatorUtilities.CreateInstance<CommManagerController>(HttpContext.RequestServices);
            inner.ControllerContext = ControllerContext;
            return await inner.ProcessCorrespondenceRequest(request);
        }

        private async Task AuditOutcomeAsync(string eventType, IActionResult result, string detail, Guid? resourceId = null)
        {
            var status = (result as IStatusCodeActionResult)?.StatusCode ?? 200;
            var user = await _access.CurrentAsync();
            await _repository.WriteAuditAsync(user?.UserId, user?.UserName, eventType, status < 400 ? "Success" : "Failure",
                $"{detail} (HTTP {status})", _access.ClientIp(), resourceId);
        }

        private ObjectResult Forbidden(PolicyDecision decision) =>
            StatusCode(StatusCodes.Status403Forbidden, new { message = decision.Message, policy = decision.PolicyName });
    }
}
