/* =========================================================
   ECCO PORTAL AUTH - REFERENCE DATA (all environments)
   Permissions, roles, role grants, attribute definitions and access policies.
   Re-runnable: rows are inserted only when missing; existing rows (which an
   administrator may have edited in the portal) are left untouched.
   Run after 001_PortalAuth_Schema.sql.
   ========================================================= */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ---- Permissions ---- */
DECLARE @Permissions TABLE (PermissionKey VARCHAR(100), Category NVARCHAR(100), Description NVARCHAR(400));
INSERT INTO @Permissions VALUES
    ('correspondence.read',          N'Correspondence', N'Search and view correspondence records'),
    ('correspondence.payload.read',  N'Correspondence', N'View request payloads (personal data)'),
    ('correspondence.create',        N'Correspondence', N'Create correspondence requests'),
    ('correspondence.resubmit',      N'Correspondence', N'Resubmit existing correspondence requests'),
    ('correspondence.export',        N'Correspondence', N'Export search results to Excel'),
    ('correspondence.delete',        N'Correspondence', N'Permanently delete correspondence records and their status history'),
    ('correspondence.pdf.generate',  N'Correspondence', N'Generate or regenerate the PDF of a correspondence request'),
    ('correspondence.recycle',       N'Correspondence', N'View the recycle bin, restore deleted records and purge them'),
    ('letters.templates.manage',     N'Letters',        N'Create, edit, publish and retire letter templates'),
    ('alerts.read',                  N'Insights',       N'See and acknowledge operational alerts'),
    ('analytics.view',               N'Insights',       N'View the analytics dashboard'),
    ('ai.use',                       N'Insights',       N'Use the Ask ECCO AI assistant'),
    ('admin.users.manage',           N'Administration', N'Manage users, roles, attributes and access policies'),
    ('admin.audit.read',             N'Administration', N'View the access audit log'),
    ('sla.manage',                   N'Administration', N'Change the SLA targets and how the SLA clock is counted');

INSERT INTO auth.Permission (PermissionKey, Category, Description)
SELECT p.PermissionKey, p.Category, p.Description
FROM @Permissions p
WHERE NOT EXISTS (SELECT 1 FROM auth.Permission x WHERE x.PermissionKey = p.PermissionKey);

/* ---- Roles ---- */
DECLARE @Roles TABLE (RoleName NVARCHAR(100), Description NVARCHAR(400));
INSERT INTO @Roles VALUES
    (N'Administrator', N'Full access, including user, role and policy administration'),
    (N'Operator',      N'Day-to-day correspondence operations: create, resubmit, export, payloads, AI'),
    (N'Analyst',       N'Read-only analysis: search, analytics, export and the AI assistant'),
    (N'Viewer',        N'Read-only search of correspondence records');

INSERT INTO auth.Role (RoleName, Description, IsSystem)
SELECT r.RoleName, r.Description, 1
FROM @Roles r
WHERE NOT EXISTS (SELECT 1 FROM auth.Role x WHERE x.RoleName = r.RoleName);

/* ---- Role grants (only seeded for roles that have no grants yet) ---- */
DECLARE @Grants TABLE (RoleName NVARCHAR(100), PermissionKey VARCHAR(100));
INSERT INTO @Grants
SELECT N'Administrator', PermissionKey FROM @Permissions;
INSERT INTO @Grants VALUES
    (N'Operator', 'correspondence.read'), (N'Operator', 'correspondence.payload.read'),
    (N'Operator', 'correspondence.create'), (N'Operator', 'correspondence.resubmit'),
    (N'Operator', 'correspondence.export'), (N'Operator', 'analytics.view'), (N'Operator', 'ai.use'),
    (N'Operator', 'correspondence.pdf.generate'), (N'Operator', 'alerts.read'), (N'Analyst', 'alerts.read'),
    (N'Analyst', 'correspondence.read'), (N'Analyst', 'correspondence.export'),
    (N'Analyst', 'analytics.view'), (N'Analyst', 'ai.use'),
    (N'Viewer', 'correspondence.read');

INSERT INTO auth.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM @Grants g
JOIN auth.Role r ON r.RoleName = g.RoleName
JOIN auth.Permission p ON p.PermissionKey = g.PermissionKey
WHERE NOT EXISTS (SELECT 1 FROM auth.RolePermission rp WHERE rp.RoleId = r.RoleId)            -- role not yet configured
   OR (r.RoleName = N'Administrator'                                                            -- Administrator always gets new permissions
       AND NOT EXISTS (SELECT 1 FROM auth.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId));

/* New permissions for roles configured before they existed (only rows that are missing). */
INSERT INTO auth.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM (VALUES (N'Operator', 'correspondence.pdf.generate'), (N'Operator', 'alerts.read'), (N'Analyst', 'alerts.read')) AS g(RoleName, PermissionKey)
JOIN auth.Role r ON r.RoleName = g.RoleName
JOIN auth.Permission p ON p.PermissionKey = g.PermissionKey
WHERE NOT EXISTS (SELECT 1 FROM auth.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = p.PermissionId);

/* ---- ABAC subject attributes ---- */
DECLARE @Attributes TABLE (AttributeKey VARCHAR(100), DisplayName NVARCHAR(200), DataType VARCHAR(20), Description NVARCHAR(400));
INSERT INTO @Attributes VALUES
    ('scope.customer',       N'Customer scope',        'list',    N'Requesting domains (customers) whose records the user can see and act on. * = all.'),
    ('scope.deliveryMethod', N'Delivery method scope', 'list',    N'Delivery methods whose records the user can see. * = all.'),
    ('piiClearance',         N'PII clearance',         'boolean', N'Allows viewing request payloads, which contain personal data.'),
    ('maxExportRows',        N'Export row limit',      'integer', N'Largest "export all matching" the user may run. Empty = no limit.');

INSERT INTO auth.AttributeDefinition (AttributeKey, DisplayName, DataType, Description)
SELECT a.AttributeKey, a.DisplayName, a.DataType, a.Description
FROM @Attributes a
WHERE NOT EXISTS (SELECT 1 FROM auth.AttributeDefinition x WHERE x.AttributeKey = a.AttributeKey);

/* ---- ABAC access policies (deny rules) ---- */
DECLARE @Policies TABLE (PolicyName NVARCHAR(200), Description NVARCHAR(1000), PermissionKey VARCHAR(100),
                         ConditionJson NVARCHAR(MAX), DenyMessage NVARCHAR(400), Priority INT, IsActive BIT);
INSERT INTO @Policies VALUES
    (N'Payloads require PII clearance',
     N'Request payloads contain personal data; only users with the piiClearance attribute may open them.',
     'correspondence.payload.read',
     N'{"all":[{"attr":"subject.piiClearance","op":"neq","values":["true"]}]}',
     N'Viewing payloads requires PII clearance.', 10, 1),
    (N'Resubmit failed records only',
     N'Only records whose latest status is Failed can be resubmitted.',
     'correspondence.resubmit',
     N'{"all":[{"attr":"resource.status","op":"notIn","values":["Failed"]}]}',
     N'Only Failed records can be resubmitted.', 20, 1),
    (N'Resubmit within customer scope',
     N'Users can only resubmit records for customers in their scope.customer attribute.',
     'correspondence.resubmit',
     N'{"all":[{"attr":"resource.customer","op":"notIn","valuesFrom":"subject.scope.customer"}]}',
     N'This record belongs to a customer outside your scope.', 30, 1),
    (N'Create within customer scope',
     N'New requests must be for a requesting domain in the user''s scope.customer attribute.',
     'correspondence.create',
     N'{"all":[{"attr":"resource.customer","op":"notIn","valuesFrom":"subject.scope.customer"}]}',
     N'You can only create correspondence for customers in your scope.', 30, 1),
    (N'Export row limit',
     N'"Export all matching" is limited to the user''s maxExportRows attribute when it is set.',
     'correspondence.export',
     N'{"all":[{"attr":"resource.rowCount","op":"gt","valuesFrom":"subject.maxExportRows"}]}',
     N'This export is larger than your export row limit. Narrow the search first.', 40, 1),
    (N'Delete within customer scope',
     N'Records can only be deleted for customers in the user''s scope.customer attribute.',
     'correspondence.delete',
     N'{"all":[{"attr":"resource.customer","op":"notIn","valuesFrom":"subject.scope.customer"}]}',
     N'This record belongs to a customer outside your scope.', 30, 1),
    (N'Resubmit during business hours (sample)',
     N'Sample environment rule, inactive by default: blocks resubmits outside 12:00-22:00 UTC.',
     'correspondence.resubmit',
     N'{"any":[{"attr":"env.hourUtc","op":"lt","values":["12"]},{"attr":"env.hourUtc","op":"gte","values":["22"]}]}',
     N'Resubmits are only allowed during business hours.', 50, 0);

INSERT INTO auth.AccessPolicy (PolicyName, Description, PermissionKey, Effect, ConditionJson, DenyMessage, Priority, IsActive)
SELECT p.PolicyName, p.Description, p.PermissionKey, 'Deny', p.ConditionJson, p.DenyMessage, p.Priority, p.IsActive
FROM @Policies p
WHERE NOT EXISTS (SELECT 1 FROM auth.AccessPolicy x WHERE x.PolicyName = p.PolicyName);

COMMIT TRANSACTION;
PRINT 'ECCO Portal Auth reference data applied.';
GO
