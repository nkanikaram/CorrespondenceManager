/*
 * In-memory stand-in for the API's portal auth (ProcessRequestEndPoint/Auth): the same seeded
 * users, PBKDF2 hashes, HS256 tokens with a security stamp, RBAC permissions and ABAC deny
 * policies (same condition grammar as AccessPolicyEvaluator.cs), plus the /api/auth and
 * /api/admin routes. Used by tools/mock-api.js.
 */
'use strict';

var crypto = require('crypto');

var ISSUER = 'ecco-correspondence-portal';
var TOKEN_MINUTES = 60;
var LOCKOUT_THRESHOLD = 5;
var LOCKOUT_MINUTES = 15;
var MIN_PASSWORD = 12;
var RESET_MINUTES = 30;
var resetTokens = {};     // token → { userId, stamp, expires }
var lastResetLink = null; // for the e2e tests: GET /api/_mock/last-reset-link
var DEV_PASSWORD = 'EccoDev!2026';
var ITERATIONS = 600000;
var SIGNING_KEY = crypto.randomBytes(32);

// ---- Reference data (mirrors Build/DBScripts/Auth/002_PortalAuth_ReferenceData.sql) ----
var PERMISSIONS = [
  { permissionKey: 'correspondence.read', category: 'Correspondence', description: 'Search and view correspondence records' },
  { permissionKey: 'correspondence.payload.read', category: 'Correspondence', description: 'View request payloads (personal data)' },
  { permissionKey: 'correspondence.create', category: 'Correspondence', description: 'Create correspondence requests' },
  { permissionKey: 'correspondence.resubmit', category: 'Correspondence', description: 'Resubmit existing correspondence requests' },
  { permissionKey: 'correspondence.export', category: 'Correspondence', description: 'Export search results to Excel' },
  { permissionKey: 'correspondence.delete', category: 'Correspondence', description: 'Delete correspondence records (they go to the recycle bin)' },
  { permissionKey: 'correspondence.recycle', category: 'Correspondence', description: 'View the recycle bin, restore records and delete them permanently' },
  { permissionKey: 'correspondence.pdf.generate', category: 'Correspondence', description: 'Generate or regenerate the PDF of a correspondence request' },
  { permissionKey: 'analytics.view', category: 'Insights', description: 'View the analytics dashboard' },
  { permissionKey: 'ai.use', category: 'Insights', description: 'Use the Ask ECCO AI assistant' },
  { permissionKey: 'admin.users.manage', category: 'Administration', description: 'Manage users, roles, attributes and access policies' },
  { permissionKey: 'admin.audit.read', category: 'Administration', description: 'View the access audit log' },
  { permissionKey: 'letters.templates.manage', category: 'Administration', description: 'Create, edit and publish letter templates' },
  { permissionKey: 'alerts.read', category: 'Insights', description: 'See server alerts and acknowledge them' },
  { permissionKey: 'sla.manage', category: 'Administration', description: 'Change the SLA targets and how the SLA clock is counted' }
];

var roles = [
  { roleId: 1, roleName: 'Administrator', description: 'Full access, including user, role and policy administration', isSystem: true,
    permissions: PERMISSIONS.map(function (p) { return p.permissionKey; }) },
  { roleId: 2, roleName: 'Operator', description: 'Day-to-day correspondence operations: create, resubmit, export, payloads, AI', isSystem: true,
    permissions: ['correspondence.read', 'correspondence.payload.read', 'correspondence.create', 'correspondence.resubmit', 'correspondence.export', 'analytics.view', 'ai.use', 'correspondence.pdf.generate', 'alerts.read'] },
  { roleId: 3, roleName: 'Analyst', description: 'Read-only analysis: search, analytics, export and the AI assistant', isSystem: true,
    permissions: ['correspondence.read', 'correspondence.export', 'analytics.view', 'ai.use', 'alerts.read'] },
  { roleId: 4, roleName: 'Viewer', description: 'Read-only search of correspondence records', isSystem: true, permissions: ['correspondence.read'] }
];

var ATTRIBUTES = [
  { attributeKey: 'maxExportRows', displayName: 'Export row limit', dataType: 'integer', description: 'Largest "export all matching" the user may run. Empty = no limit.' },
  { attributeKey: 'piiClearance', displayName: 'PII clearance', dataType: 'boolean', description: 'Allows viewing request payloads, which contain personal data.' },
  { attributeKey: 'scope.customer', displayName: 'Customer scope', dataType: 'list', description: 'Requesting domains (customers) whose records the user can see and act on. * = all.' },
  { attributeKey: 'scope.deliveryMethod', displayName: 'Delivery method scope', dataType: 'list', description: 'Delivery methods whose records the user can see. * = all.' }
];

var policies = [
  { policyId: 1, policyName: 'Payloads require PII clearance', permissionKey: 'correspondence.payload.read', priority: 10, isActive: true,
    description: 'Request payloads contain personal data; only users with the piiClearance attribute may open them.',
    conditionJson: '{"all":[{"attr":"subject.piiClearance","op":"neq","values":["true"]}]}', denyMessage: 'Viewing payloads requires PII clearance.' },
  { policyId: 2, policyName: 'Resubmit failed records only', permissionKey: 'correspondence.resubmit', priority: 20, isActive: true,
    description: 'Only records whose latest status is Failed can be resubmitted.',
    conditionJson: '{"all":[{"attr":"resource.status","op":"notIn","values":["Failed"]}]}', denyMessage: 'Only Failed records can be resubmitted.' },
  { policyId: 3, policyName: 'Resubmit within customer scope', permissionKey: 'correspondence.resubmit', priority: 30, isActive: true,
    description: 'Users can only resubmit records for customers in their scope.customer attribute.',
    conditionJson: '{"all":[{"attr":"resource.customer","op":"notIn","valuesFrom":"subject.scope.customer"}]}', denyMessage: 'This record belongs to a customer outside your scope.' },
  { policyId: 4, policyName: 'Create within customer scope', permissionKey: 'correspondence.create', priority: 30, isActive: true,
    description: 'New requests must be for a requesting domain in the user\'s scope.customer attribute.',
    conditionJson: '{"all":[{"attr":"resource.customer","op":"notIn","valuesFrom":"subject.scope.customer"}]}', denyMessage: 'You can only create correspondence for customers in your scope.' },
  { policyId: 5, policyName: 'Export row limit', permissionKey: 'correspondence.export', priority: 40, isActive: true,
    description: '"Export all matching" is limited to the user\'s maxExportRows attribute when it is set.',
    conditionJson: '{"all":[{"attr":"resource.rowCount","op":"gt","valuesFrom":"subject.maxExportRows"}]}', denyMessage: 'This export is larger than your export row limit. Narrow the search first.' },
  { policyId: 7, policyName: 'Delete within customer scope', permissionKey: 'correspondence.delete', priority: 30, isActive: true,
    description: 'Records can only be deleted for customers in the user\'s scope.customer attribute.',
    conditionJson: '{"all":[{"attr":"resource.customer","op":"notIn","valuesFrom":"subject.scope.customer"}]}', denyMessage: 'This record belongs to a customer outside your scope.' },
  { policyId: 6, policyName: 'Resubmit during business hours (sample)', permissionKey: 'correspondence.resubmit', priority: 50, isActive: false,
    description: 'Sample environment rule, inactive by default: blocks resubmits outside 12:00-22:00 UTC.',
    conditionJson: '{"any":[{"attr":"env.hourUtc","op":"lt","values":["12"]},{"attr":"env.hourUtc","op":"gte","values":["22"]}]}', denyMessage: 'Resubmits are only allowed during business hours.' }
];
policies.forEach(function (p) { p.effect = 'Deny'; p.updatedUtc = new Date().toISOString(); });

// ---- Users (mirrors 003_PortalAuth_DevUsers.sql; password EccoDev!2026) ----
var users = [];
var audit = [];
var preferences = {};    // userId → saved display preferences (mirrors auth.UserPreference)
var PREF_RULES = { theme: ['light', 'dark', 'system'], timeZone: ['local', 'utc'], pageSize: [10, 25, 50, 100], density: ['comfortable', 'compact'],
  font: ['default', 'calibri', 'arial', 'verdana', 'georgia', 'mono'], textSize: ['sm', 'md', 'lg', 'xl'],
  headerColor: /^#[0-9a-f]{6}$/i, textColor: /^(contrast|#[0-9a-f]{6})$/i };
var auditSeq = 0;

function hashPassword(password) {
  var salt = crypto.randomBytes(16);
  return { hash: crypto.pbkdf2Sync(password, salt, ITERATIONS, 32, 'sha256'), salt: salt, iterations: ITERATIONS };
}

function verifyPassword(password, user) {
  var actual = crypto.pbkdf2Sync(String(password || ''), user.password.salt, user.password.iterations, 32, 'sha256');
  return crypto.timingSafeEqual(actual, user.password.hash);
}

function addUser(userName, displayName, email, roleNames, attributes, password, mustChange) {
  var user = {
    userId: crypto.randomUUID(), userName: userName, displayName: displayName, email: email || null,
    isActive: true, mustChangePassword: !!mustChange, failedLoginCount: 0, lockoutEndUtc: null, lastLoginUtc: null,
    securityStamp: crypto.randomUUID(), roles: roleNames.slice(), attributes: attributes || {},
    groupRoles: [], groupAttributes: {}, externalProvider: null, externalObjectId: null,
    password: password ? hashPassword(password) : null
  };
  users.push(user);
  return user;
}

addUser('admin', 'Naveen Kanikaram', 'admin@ecco.local', ['Administrator'],
  { 'scope.customer': ['*'], 'scope.deliveryMethod': ['*'], piiClearance: ['true'] }, DEV_PASSWORD);
addUser('operator', 'Olivia Operator', 'operator@ecco.local', ['Operator'],
  { 'scope.customer': ['Life Services', 'Claims System'], 'scope.deliveryMethod': ['*'], piiClearance: ['true'], maxExportRows: ['500'] }, DEV_PASSWORD);
addUser('analyst', 'Ari Analyst', 'analyst@ecco.local', ['Analyst'],
  { 'scope.customer': ['*'], 'scope.deliveryMethod': ['*'], maxExportRows: ['100'] }, DEV_PASSWORD);
addUser('viewer', 'Val Viewer', 'viewer@ecco.local', ['Viewer'],
  { 'scope.customer': ['Annuity Correspondence'], 'scope.deliveryMethod': ['Email'] }, DEV_PASSWORD);

// ---- Microsoft sign-in (mock): test accounts and group mappings ----
var GROUPS = { operators: '6f1c2a10-0000-4000-8000-00000000a001', analysts: '6f1c2a10-0000-4000-8000-00000000a002', lifeTeam: '6f1c2a10-0000-4000-8000-00000000a003' };
var MOCK_ACCOUNTS = [
  { key: 'alex', name: 'Alex Rivera', upn: 'alex.rivera@contoso.example', oid: '0a1b0000-0000-4000-8000-000000000001', mfa: true,
    groups: [GROUPS.operators, GROUPS.lifeTeam], description: 'Groups: CC Operators, Life Services team → Operator, Life Services records' },
  { key: 'sam', name: 'Sam Patel', upn: 'sam.patel@contoso.example', oid: '0a1b0000-0000-4000-8000-000000000002', mfa: true,
    groups: [GROUPS.analysts], description: 'Group: CC Analysts → Analyst, all customers' },
  { key: 'nogroup', name: 'Noa Guest', upn: 'noa.guest@contoso.example', oid: '0a1b0000-0000-4000-8000-000000000003', mfa: true,
    groups: [], description: 'In no mapped group → refused' },
  { key: 'nomfa', name: 'Max NoMfa', upn: 'max.nomfa@contoso.example', oid: '0a1b0000-0000-4000-8000-000000000004', mfa: false,
    groups: [GROUPS.operators], description: 'Signed in without MFA → refused' }
];
var mappingSeq = 0;
var mappings = [
  { claimType: 'group', claimValue: GROUPS.operators, displayName: 'CC Operators', roleName: 'Operator' },
  { claimType: 'group', claimValue: GROUPS.analysts, displayName: 'CC Analysts', roleName: 'Analyst' },
  { claimType: 'group', claimValue: GROUPS.analysts, displayName: 'CC Analysts', attributeKey: 'scope.customer', attributeValue: '*' },
  { claimType: 'group', claimValue: GROUPS.analysts, displayName: 'CC Analysts', attributeKey: 'scope.deliveryMethod', attributeValue: '*' },
  { claimType: 'group', claimValue: GROUPS.lifeTeam, displayName: 'Life Services team', attributeKey: 'scope.customer', attributeValue: 'Life Services' },
  { claimType: 'group', claimValue: GROUPS.lifeTeam, displayName: 'Life Services team', attributeKey: 'scope.deliveryMethod', attributeValue: '*' },
  { claimType: 'group', claimValue: GROUPS.lifeTeam, displayName: 'Life Services team', attributeKey: 'piiClearance', attributeValue: 'true' }
].map(function (x) { return Object.assign({ mappingId: ++mappingSeq, roleName: null, attributeKey: null, attributeValue: null }, x); });

/** Replaces the user's group grants with what their groups map to now; ends their sessions when that changes. */
function syncGroups(user, acct) {
  var hit = mappings.filter(function (x) { return (x.claimType === 'group' ? acct.groups : acct.roles || []).indexOf(x.claimValue) !== -1; });
  var groupRoles = hit.filter(function (x) { return x.roleName; }).map(function (x) { return x.roleName; })
    .filter(function (v, i, a) { return a.indexOf(v) === i; }).sort();
  var groupAttributes = {};
  hit.filter(function (x) { return x.attributeKey; }).forEach(function (x) {
    var list = groupAttributes[x.attributeKey] = groupAttributes[x.attributeKey] || [];
    if (list.indexOf(x.attributeValue) === -1) list.push(x.attributeValue);
  });
  if (JSON.stringify(groupRoles) !== JSON.stringify(user.groupRoles) || JSON.stringify(groupAttributes) !== JSON.stringify(user.groupAttributes)) {
    user.groupRoles = groupRoles;
    user.groupAttributes = groupAttributes;
    user.securityStamp = crypto.randomUUID();
  }
}

function findUser(id) { return users.filter(function (u) { return u.userId === id; })[0]; }
function findByName(name) {
  var n = String(name || '').toUpperCase();
  return users.filter(function (u) { return u.userName.toUpperCase() === n; })[0];
}

/** Manual roles plus those from the user's Entra groups. */
function allRoles(user) {
  return user.roles.concat((user.groupRoles || []).filter(function (r) { return user.roles.indexOf(r) === -1; }));
}
function allAttributes(user) {
  var out = {};
  [user.attributes, user.groupAttributes || {}].forEach(function (src) {
    Object.keys(src).forEach(function (k) {
      out[k] = (out[k] || []).concat(src[k].filter(function (v) { return (out[k] || []).indexOf(v) === -1; }));
    });
  });
  return out;
}

function permissionsOf(user) {
  var set = {};
  var mine = allRoles(user);
  roles.forEach(function (r) {
    if (mine.indexOf(r.roleName) !== -1) r.permissions.forEach(function (p) { set[p] = true; });
  });
  return Object.keys(set).sort();
}

function profileDto(user) {
  return {
    userId: user.userId, userName: user.userName, displayName: user.displayName, email: user.email,
    mustChangePassword: user.mustChangePassword, roles: allRoles(user).sort(), permissions: permissionsOf(user), attributes: allAttributes(user),
    signInMethod: user.externalProvider ? 'Entra' : 'Local'
  };
}

function writeAudit(user, userName, eventType, outcome, detail, ip, resourceId) {
  if (!resourceId) { var rm = /record ([0-9a-f-]{36})/i.exec(detail || ''); if (rm) resourceId = rm[1]; }
  audit.unshift({
    auditId: ++auditSeq, occurredUtc: new Date().toISOString(), userName: user ? user.userName : (userName || null),
    eventType: eventType, outcome: outcome, detail: detail || null, ipAddress: ip || '127.0.0.1', resourceId: resourceId || null
  });
  if (audit.length > 1000) audit.length = 1000;
}

// ---- Tokens (HS256 JWT) ----
function b64url(buf) { return Buffer.from(buf).toString('base64').replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_'); }

function issueToken(user) {
  var now = Math.floor(Date.now() / 1000);
  var exp = now + TOKEN_MINUTES * 60;
  var header = b64url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  var payload = b64url(JSON.stringify({
    sub: user.userId, unique_name: user.userName, jti: crypto.randomUUID(), 'ecco:stamp': user.securityStamp,
    iss: ISSUER, aud: ISSUER, nbf: now, iat: now, exp: exp
  }));
  var sig = b64url(crypto.createHmac('sha256', SIGNING_KEY).update(header + '.' + payload).digest());
  return { accessToken: header + '.' + payload + '.' + sig, expiresAtUtc: new Date(exp * 1000).toISOString() };
}

/** The signed-in user for a request, or null (missing/invalid/expired token, inactive user, rotated stamp). */
function authenticate(req) {
  var m = /^Bearer\s+(.+)$/i.exec(req.headers.authorization || '');
  if (!m) return null;
  var parts = m[1].split('.');
  if (parts.length !== 3) return null;
  var expected = b64url(crypto.createHmac('sha256', SIGNING_KEY).update(parts[0] + '.' + parts[1]).digest());
  if (expected.length !== parts[2].length || !crypto.timingSafeEqual(Buffer.from(expected), Buffer.from(parts[2]))) return null;
  var claims;
  try { claims = JSON.parse(Buffer.from(parts[1].replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8')); } catch (e) { return null; }
  if (claims.iss !== ISSUER || claims.aud !== ISSUER || claims.exp * 1000 < Date.now() - 30000) return null;
  var user = findUser(claims.sub);
  if (!user || !user.isActive || user.securityStamp !== claims['ecco:stamp']) return null;
  return user;
}

// ---- ABAC (same grammar and fail-closed semantics as AccessPolicyEvaluator.cs) ----
function resolve(attr, user, resource, now) {
  if (attr.indexOf('subject.') === 0) return ((user.groupAttributes ? allAttributes(user) : user.attributes)[attr.slice(8)] || []).slice();
  if (attr.indexOf('resource.') === 0) {
    var v = resource[attr.slice(9)];
    return v === undefined || v === null || String(v).trim() === '' ? [] : [String(v)];
  }
  if (attr === 'env.hourUtc') return [String(now.getUTCHours())];
  if (attr === 'env.dayOfWeek') return [['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'][now.getUTCDay()]];
  throw new Error("Unknown attribute '" + attr + "'.");
}

function anyIn(actual, expected) {
  if (!actual.length) return false;
  if (expected.indexOf('*') !== -1) return true;
  return actual.some(function (a) { return expected.some(function (e) { return a.toLowerCase() === String(e).toLowerCase(); }); });
}

function compare(actual, expected, test) {
  if (!actual.length || !expected.length) return false;
  var a = Number(actual[0]); var e = Number(expected[0]);
  return !isNaN(a) && !isNaN(e) && test(a - e);
}

function matches(node, user, resource, now) {
  if (!node || typeof node !== 'object') throw new Error('A condition must be a JSON object.');
  var list = node.all || node.any;
  if (list) {
    if (!Array.isArray(list) || !list.length) throw new Error("'all' and 'any' must be non-empty arrays.");
    var results = list.map(function (c) { return matches(c, user, resource, now); });
    return node.all ? results.every(Boolean) : results.some(Boolean);
  }
  if (!node.attr || !node.op) throw new Error("Each clause needs 'attr' and 'op'.");
  var actual = resolve(node.attr, user, resource, now);
  var expected = Array.isArray(node.values) ? node.values.map(String) : node.valuesFrom ? resolve(node.valuesFrom, user, resource, now) : [];
  switch (node.op) {
    case 'exists': return actual.length > 0;
    case 'notExists': return actual.length === 0;
    case 'eq': case 'in': return anyIn(actual, expected);
    case 'neq': case 'notIn': return !anyIn(actual, expected);
    case 'gt': return compare(actual, expected, function (d) { return d > 0; });
    case 'gte': return compare(actual, expected, function (d) { return d >= 0; });
    case 'lt': return compare(actual, expected, function (d) { return d < 0; });
    case 'lte': return compare(actual, expected, function (d) { return d <= 0; });
    default: throw new Error("Unknown operator '" + node.op + "'.");
  }
}

function check(user, permission, resource) {
  if (!user || !user.isActive || permissionsOf(user).indexOf(permission) === -1) {
    return { allowed: false, policy: null, message: "You don't have permission to do this." };
  }
  var now = new Date();
  var relevant = policies.filter(function (p) { return p.isActive && p.permissionKey === permission; })
    .sort(function (a, b) { return a.priority - b.priority; });
  for (var i = 0; i < relevant.length; i++) {
    var p = relevant[i];
    var hit;
    try { hit = matches(JSON.parse(p.conditionJson), user, resource || {}, now); } catch (e) {
      return { allowed: false, policy: p.policyName, message: "Access policy '" + p.policyName + "' is misconfigured." };
    }
    if (hit) return { allowed: false, policy: p.policyName, message: p.denyMessage };
  }
  return { allowed: true };
}

function validateCondition(json) {
  try { matches(JSON.parse(json), { attributes: {} }, {}, new Date()); return null; } catch (e) { return e.message; }
}

/** null = unrestricted ('*'); [] = nothing. */
function scopeValues(user, key) {
  var values = allAttributes(user)[key] || [];
  return values.indexOf('*') !== -1 ? null : values;
}

function validatePassword(password, userName) {
  var problems = [];
  if (!password) return ['Password is required.'];
  if (password.length < MIN_PASSWORD) problems.push('Use at least ' + MIN_PASSWORD + ' characters.');
  var classes = [/[A-Z]/, /[a-z]/, /[0-9]/, /[^A-Za-z0-9]/].filter(function (re) { return re.test(password); }).length;
  if (classes < 3) problems.push('Mix at least three of: upper case, lower case, digits, symbols.');
  if (userName && password.toLowerCase().indexOf(userName.toLowerCase()) !== -1) problems.push("Don't include your user name.");
  return problems;
}

// ---- Routes: return true when handled ----
function routes(req, res, pathname, readBody, send) {
  var m;
  var ip = req.socket.remoteAddress;

  if (req.method === 'POST' && pathname === '/api/auth/login') {
    readBody(req).then(function (b) {
      var invalid = 'Incorrect user name or password.';
      var user = findByName(String(b.userName || '').trim());
      if (!user) { writeAudit(null, b.userName, 'LoginFailed', 'Failure', 'Unknown user name', ip); return send(res, 401, { message: invalid }); }
      if (user.lockoutEndUtc && new Date(user.lockoutEndUtc) > new Date()) {
        writeAudit(user, null, 'LoginFailed', 'Failure', 'Account locked', ip);
        return send(res, 401, { message: 'Too many failed attempts. The account is locked for ' + Math.ceil((new Date(user.lockoutEndUtc) - Date.now()) / 60000) + ' more minutes.', locked: true });
      }
      if (!user.password) { writeAudit(user, null, 'LoginFailed', 'Failure', 'Microsoft account used the local form', ip); return send(res, 403, { message: 'Sign in with your Microsoft work account.' }); }
      if (!verifyPassword(b.password, user)) {
        user.failedLoginCount++;
        if (user.failedLoginCount >= LOCKOUT_THRESHOLD) {
          user.failedLoginCount = 0;
          user.lockoutEndUtc = new Date(Date.now() + LOCKOUT_MINUTES * 60000).toISOString();
          writeAudit(user, null, 'LockedOut', 'Failure', 'Wrong password', ip);
          return send(res, 401, { message: 'Too many failed attempts. The account is locked for ' + LOCKOUT_MINUTES + ' more minutes.', locked: true });
        }
        writeAudit(user, null, 'LoginFailed', 'Failure', 'Wrong password', ip);
        return send(res, 401, { message: invalid });
      }
      if (!user.isActive) { writeAudit(user, null, 'LoginFailed', 'Failure', 'Account deactivated', ip); return send(res, 401, { message: invalid }); }
      user.failedLoginCount = 0; user.lockoutEndUtc = null; user.lastLoginUtc = new Date().toISOString();
      writeAudit(user, null, 'LoginSucceeded', 'Success', null, ip);
      var t = issueToken(user);
      send(res, 200, { accessToken: t.accessToken, expiresAtUtc: t.expiresAtUtc, user: profileDto(user) });
    });
    return true;
  }

  if (req.method === 'GET' && pathname === '/api/auth/config') {
    send(res, 200, {
      entra: { enabled: true, mock: true, clientId: null, tenantId: null, authority: null, mockAccounts: MOCK_ACCOUNTS.map(function (a) {
        return { key: a.key, name: a.name, description: a.description };
      }) },
      localLogin: { enabled: true },
      passwordReset: { enabled: true, linkLifetimeMinutes: RESET_MINUTES }
    });
    return true;
  }

  // ---- Forgot password (mirrors PasswordResetService: same answer for every request; the link is printed, not emailed) ----
  if (req.method === 'POST' && pathname === '/api/auth/forgot-password') {
    readBody(req).then(function (b) {
      var value = String(b.userNameOrEmail || '').trim();
      if (!value) return send(res, 400, { message: 'Enter your user name or email address.' });
      var user = findByName(value);
      if (!user) {
        var byEmail = users.filter(function (u) { return u.email && u.email.toLowerCase() === value.toLowerCase(); });
        user = byEmail.length === 1 ? byEmail[0] : null;
      }
      var refusal = !user ? 'No single account matches' : !user.isActive ? 'Account deactivated'
        : user.externalProvider ? 'Microsoft sign-in account' : !user.email ? 'Account has no email address' : null;
      if (refusal) {
        writeAudit(user, user ? null : value, 'PasswordResetIgnored', 'Failure', refusal, ip);
      } else {
        Object.keys(resetTokens).forEach(function (k) { if (resetTokens[k].userId === user.userId) delete resetTokens[k]; });
        var token = crypto.randomBytes(32).toString('base64url');
        resetTokens[token] = { userId: user.userId, stamp: user.securityStamp, expires: Date.now() + RESET_MINUTES * 60000 };
        lastResetLink = { userName: user.userName, link: 'http://' + (req.headers.host || 'localhost:8081') + '/#!/reset-password?token=' + token };
        writeAudit(user, null, 'PasswordResetRequested', 'Success', 'Reset link emailed to ' + user.email, ip);
        console.log('[mock] password reset link for ' + user.userName + ': ' + lastResetLink.link);
      }
      send(res, 202, { message: "If that account exists and has an email address, we've sent it a link to choose a new password. Check your inbox (and junk folder)." });
    });
    return true;
  }

  if (req.method === 'GET' && pathname === '/api/_mock/last-reset-link') { send(res, 200, lastResetLink || {}); return true; }

  if (req.method === 'POST' && (pathname === '/api/auth/reset-password/check' || pathname === '/api/auth/reset-password')) {
    readBody(req).then(function (b) {
      var entry = resetTokens[String(b.token || '')];
      var user = entry && users.filter(function (u) { return u.userId === entry.userId; })[0];
      if (!entry || entry.expires < Date.now() || !user || !user.isActive || user.securityStamp !== entry.stamp) {
        return send(res, 400, { message: 'This reset link has expired or has already been used. Ask for a new one.', linkInvalid: true });
      }
      if (pathname.slice(-6) === '/check') return send(res, 200, { userName: user.userName, displayName: user.displayName });
      var problems = validatePassword(b.newPassword, user.userName);
      if (problems.length) return send(res, 400, { message: 'Choose a stronger password.', problems: problems });
      delete resetTokens[b.token];
      user.password = hashPassword(b.newPassword);
      user.mustChangePassword = false;
      user.failedLoginCount = 0;
      user.lockoutEndUtc = null;
      user.securityStamp = crypto.randomUUID();
      writeAudit(user, null, 'PasswordReset', 'Success', 'Self-service reset by emailed link', ip);
      send(res, 200, { message: 'Your password has been changed. Sign in with your new password.', userName: user.userName });
    });
    return true;
  }

  if (req.method === 'POST' && pathname === '/api/auth/entra') {
    readBody(req).then(function (b) {
      var key = /^mock:(.*)$/.exec(String(b.idToken || ''));
      var acct = key && MOCK_ACCOUNTS.filter(function (a) { return a.key === key[1]; })[0];
      if (!acct) return send(res, 401, { message: 'The Microsoft sign-in could not be verified. Try again.' });
      if (!acct.mfa) {
        writeAudit(null, acct.upn, 'LoginFailed', 'Failure', 'Entra: no MFA in the token', ip);
        return send(res, 401, { message: 'Your organisation requires multi-factor authentication for this portal. Sign in again and complete the MFA prompt.' });
      }
      var user = users.filter(function (u) { return u.externalProvider === 'Entra' && u.externalObjectId === acct.oid; })[0];
      if (!user) {
        if (findByName(acct.upn)) return send(res, 409, { message: 'A portal account with your user name already exists. Ask a portal administrator to resolve it.' });
        user = addUser(acct.upn, acct.name, acct.upn, [], {}, null, false);
        user.externalProvider = 'Entra';
        user.externalObjectId = acct.oid;
        writeAudit(user, null, 'UserProvisioned', 'Success', 'Entra ' + acct.oid, ip);
      }
      if (!user.isActive) return send(res, 401, { message: 'Your portal access has been switched off. Ask a portal administrator.' });
      syncGroups(user, acct);
      if (!permissionsOf(user).length) {
        writeAudit(user, null, 'LoginFailed', 'Denied', 'Entra: no mapped group or role', ip);
        return send(res, 403, { message: "Your Microsoft account isn't in a group that has access to this portal. Ask a portal administrator." });
      }
      user.lastLoginUtc = new Date().toISOString();
      writeAudit(user, null, 'LoginSucceeded', 'Success', 'Microsoft (MFA)', ip);
      var t = issueToken(user);
      send(res, 200, { accessToken: t.accessToken, expiresAtUtc: t.expiresAtUtc, user: profileDto(user) });
    });
    return true;
  }

  if (req.method === 'GET' && pathname === '/api/auth/password-policy') {
    send(res, 200, { minLength: MIN_PASSWORD, rules: ['At least ' + MIN_PASSWORD + ' characters', 'Three of: upper case, lower case, digits, symbols', 'Must not contain your user name'] });
    return true;
  }

  if (pathname.indexOf('/api/auth/') !== 0 && pathname.indexOf('/api/admin/') !== 0 && pathname.indexOf('/api/account') !== 0) return false;

  var me = authenticate(req);
  if (!me) { send(res, 401, { message: 'Sign in to continue.' }); return true; }

  if (req.method === 'GET' && pathname === '/api/auth/me') { send(res, 200, profileDto(me)); return true; }

  // ---- The account menu (mirrors Controllers/AccountController.cs) ----
  if (req.method === 'GET' && pathname === '/api/account') {
    var signIns = audit.filter(function (a) { return a.userName === me.userName && a.eventType === 'LoginSucceeded'; }); // newest first
    var previous = signIns[1] || null;
    var failed = previous ? audit.filter(function (a) {
      return a.userName === me.userName && (a.eventType === 'LoginFailed' || a.eventType === 'LockedOut') && a.occurredUtc > previous.occurredUtc;
    }).length : 0;
    send(res, 200, {
      scope: { customers: scopeValues(me, 'scope.customer'), deliveryMethods: scopeValues(me, 'scope.deliveryMethod') },
      previousSignIn: previous && { utc: previous.occurredUtc, ip: previous.ipAddress, method: /^Microsoft/.test(previous.detail || '') ? 'Microsoft' : 'Password' },
      failedSignInsSincePrevious: failed,
      preferences: preferences[me.userId] || {},
      support: { email: 'ecco-support@example.com', guideUrl: 'https://example.com/ecco/user-guide' },
      version: '1.0.0-mock', environment: 'Mock'
    });
    return true;
  }
  if (req.method === 'PUT' && pathname === '/api/account/preferences') {
    readBody(req).then(function (b) {
      var problems = [], clean = {};
      Object.keys(PREF_RULES).forEach(function (k) {
        if (b[k] === undefined || b[k] === null) return;
        var rule = PREF_RULES[k];
        var ok = rule instanceof RegExp ? rule.test(String(b[k])) : rule.indexOf(b[k]) !== -1;
        if (!ok) problems.push(k + (rule instanceof RegExp ? ' must be a colour like #0b6bcb.' : ' must be one of: ' + rule.join(', ') + '.'));
        else clean[k] = b[k];
      });
      if (problems.length) return send(res, 400, { message: 'Check your preferences.', problems: problems });
      preferences[me.userId] = clean;
      send(res, 200, clean);
    });
    return true;
  }
  if (req.method === 'POST' && pathname === '/api/account/sign-out-others') {
    me.securityStamp = crypto.randomUUID();
    writeAudit(me, null, 'SignedOutOtherSessions', 'Success', null, ip);
    send(res, 200, Object.assign(issueToken(me), { user: profileDto(me) }));
    return true;
  }
  if (req.method === 'POST' && pathname === '/api/auth/logout') { writeAudit(me, null, 'Logout', 'Success', null, ip); send(res, 204); return true; }
  if (req.method === 'POST' && pathname === '/api/auth/change-password') {
    readBody(req).then(function (b) {
      if (!verifyPassword(b.currentPassword, me)) return send(res, 400, { message: 'Your current password is incorrect.' });
      var problems = validatePassword(b.newPassword, me.userName);
      if (b.newPassword === b.currentPassword) problems.push('Choose a password different from the current one.');
      if (problems.length) return send(res, 400, { message: 'Choose a stronger password.', problems: problems });
      me.password = hashPassword(b.newPassword);
      me.mustChangePassword = false;
      me.securityStamp = crypto.randomUUID();
      writeAudit(me, null, 'PasswordChanged', 'Success', null, ip);
      var t = issueToken(me);
      send(res, 200, { accessToken: t.accessToken, expiresAtUtc: t.expiresAtUtc, user: profileDto(me) });
    });
    return true;
  }

  // Everything else needs an up-to-date password, like the API's PermissionHandler.
  if (me.mustChangePassword) { send(res, 403, { message: 'Change your temporary password first.' }); return true; }

  function requirePermission(key) {
    if (permissionsOf(me).indexOf(key) !== -1) return true;
    writeAudit(me, null, 'AccessDenied', 'Denied', key + ': no role grants it', ip);
    send(res, 403, { message: "You don't have permission to do this." });
    return false;
  }

  if (req.method === 'GET' && pathname === '/api/admin/audit') {
    if (!requirePermission('admin.audit.read')) return true;
    var q = require('url').parse(req.url, true).query;
    var top = Math.min(Math.max(Number(q.top) || 200, 1), 1000);
    send(res, 200, audit.filter(function (a) { return !q.userName || a.userName === q.userName; }).slice(0, top));
    return true;
  }

  if (!requirePermission('admin.users.manage')) return true;

  if (req.method === 'GET' && pathname === '/api/admin/users') {
    send(res, 200, users.slice().sort(function (a, b) { return a.userName.localeCompare(b.userName); }).map(function (u) {
      return {
        userId: u.userId, userName: u.userName, displayName: u.displayName, email: u.email, isActive: u.isActive,
        mustChangePassword: u.mustChangePassword, isLockedOut: !!(u.lockoutEndUtc && new Date(u.lockoutEndUtc) > new Date()),
        lastLoginUtc: u.lastLoginUtc, roles: allRoles(u).sort(), attributes: allAttributes(u),
        externalProvider: u.externalProvider, groupRoles: (u.groupRoles || []).slice().sort(), groupAttributes: u.groupAttributes || {}
      };
    }));
    return true;
  }

  function validateRolesAndAttributes(b) {
    var roleNames = roles.map(function (r) { return r.roleName; });
    var unknown = (b.roles || []).filter(function (r) { return roleNames.indexOf(r) === -1; });
    if (unknown.length) return 'Unknown roles: ' + unknown.join(', ');
    var keys = Object.keys(b.attributes || {});
    for (var i = 0; i < keys.length; i++) {
      var def = ATTRIBUTES.filter(function (a) { return a.attributeKey === keys[i]; })[0];
      if (!def) return "Unknown attribute '" + keys[i] + "'.";
      var values = (b.attributes[keys[i]] || []).filter(function (v) { return String(v).trim(); });
      if (def.dataType !== 'list' && values.length > 1) return "'" + def.displayName + "' takes a single value.";
      for (var j = 0; j < values.length; j++) {
        if (def.dataType === 'boolean' && values[j] !== 'true' && values[j] !== 'false') return "'" + def.displayName + "' must be true or false.";
        if (def.dataType === 'integer' && !/^\d+$/.test(values[j])) return "'" + def.displayName + "' must be a whole number.";
      }
    }
    return null;
  }

  function cleanAttributes(attrs) {
    var out = {};
    Object.keys(attrs || {}).forEach(function (k) {
      var values = attrs[k].map(function (v) { return String(v).trim(); }).filter(Boolean)
        .filter(function (v, i, a) { return a.indexOf(v) === i; });
      if (values.length) out[k] = values;
    });
    return out;
  }

  if (req.method === 'POST' && pathname === '/api/admin/users') {
    readBody(req).then(function (b) {
      var name = String(b.userName || '').trim();
      if (!/^[A-Za-z0-9._@-]{3,100}$/.test(name)) return send(res, 400, { message: 'User names are 3-100 characters: letters, digits and . _ @ -' });
      if (findByName(name)) return send(res, 409, { message: "The user name '" + name + "' is taken." });
      var problems = validatePassword(b.password, name);
      if (problems.length) return send(res, 400, { message: 'Choose a stronger temporary password.', problems: problems });
      var invalid = validateRolesAndAttributes(b);
      if (invalid) return send(res, 400, { message: invalid });
      var u = addUser(name, (b.displayName || '').trim() || name, (b.email || '').trim() || null, b.roles || [], cleanAttributes(b.attributes), b.password, true);
      u.isActive = b.isActive !== false;
      writeAudit(me, null, 'UserCreated', 'Success', name + ': roles ' + (b.roles || []).join(', '), ip);
      send(res, 200, { userId: u.userId });
    });
    return true;
  }

  if ((m = pathname.match(/^\/api\/admin\/users\/([^/]+)(\/reset-password|\/unlock)?$/))) {
    var target = findUser(decodeURIComponent(m[1]));
    if (!target) { send(res, 404, { message: 'User not found.' }); return true; }
    if (!m[2] && req.method === 'DELETE') {
      if (target === me) { send(res, 400, { message: "You can't delete your own account." }); return true; }
      var activeAdmins = users.filter(function (u) { return u.isActive && allRoles(u).indexOf('Administrator') !== -1; }).length;
      if (target.isActive && allRoles(target).indexOf('Administrator') !== -1 && activeAdmins <= 1) {
        send(res, 400, { message: "You can't delete the last active Administrator." }); return true;
      }
      users.splice(users.indexOf(target), 1);
      writeAudit(me, null, 'UserDeleted', 'Success', target.userName + ' (' + target.displayName + '): roles ' + target.roles.join(', '), ip);
      send(res, 204); return true;
    }
    if (m[2] === '/unlock' && req.method === 'POST') {
      target.failedLoginCount = 0; target.lockoutEndUtc = null;
      writeAudit(me, null, 'UserUnlocked', 'Success', target.userName, ip);
      send(res, 204); return true;
    }
    if (m[2] === '/reset-password' && req.method === 'POST') {
      if (target.externalProvider) { send(res, 400, { message: "This account signs in with Microsoft; its password is managed in Entra ID." }); return true; }
      readBody(req).then(function (b) {
        var problems = validatePassword(b.newPassword, target.userName);
        if (problems.length) return send(res, 400, { message: 'Choose a stronger temporary password.', problems: problems });
        target.password = hashPassword(b.newPassword);
        target.mustChangePassword = true; target.failedLoginCount = 0; target.lockoutEndUtc = null;
        target.securityStamp = crypto.randomUUID();
        writeAudit(me, null, 'PasswordReset', 'Success', target.userName, ip);
        send(res, 204);
      });
      return true;
    }
    if (!m[2] && req.method === 'PUT') {
      readBody(req).then(function (b) {
        if (target === me && b.isActive === false) return send(res, 400, { message: "You can't deactivate your own account." });
        if (target === me && me.roles.indexOf('Administrator') !== -1 && (b.roles || []).indexOf('Administrator') === -1) {
          return send(res, 400, { message: "You can't remove the Administrator role from yourself." });
        }
        var invalid = validateRolesAndAttributes(b);
        if (invalid) return send(res, 400, { message: invalid });
        target.displayName = (b.displayName || '').trim() || target.userName;
        target.email = (b.email || '').trim() || null;
        target.isActive = b.isActive !== false;
        target.roles = (b.roles || []).filter(function (r) { return (target.groupRoles || []).indexOf(r) === -1; });
        target.attributes = cleanAttributes(b.attributes);
        target.securityStamp = crypto.randomUUID();
        writeAudit(me, null, 'UserUpdated', 'Success', target.userName + ': active=' + target.isActive + ', roles ' + target.roles.join(', '), ip);
        if (target === me) { // editing yourself: fresh token instead of ending your session
          var t = issueToken(me);
          return send(res, 200, { accessToken: t.accessToken, expiresAtUtc: t.expiresAtUtc, user: profileDto(me) });
        }
        send(res, 204);
      });
      return true;
    }
  }

  if (req.method === 'GET' && pathname === '/api/admin/roles') {
    send(res, 200, roles.map(function (r) {
      return Object.assign({}, r, { userCount: users.filter(function (u) { return allRoles(u).indexOf(r.roleName) !== -1; }).length, permissions: r.permissions.slice().sort() });
    }));
    return true;
  }
  if ((m = pathname.match(/^\/api\/admin\/roles\/(\d+)\/permissions$/)) && req.method === 'PUT') {
    var role = roles.filter(function (r) { return r.roleId === Number(m[1]); })[0];
    if (!role) { send(res, 404, { message: 'Role not found.' }); return true; }
    readBody(req).then(function (b) {
      var keys = PERMISSIONS.map(function (p) { return p.permissionKey; });
      var unknown = (b.permissions || []).filter(function (p) { return keys.indexOf(p) === -1; });
      if (unknown.length) return send(res, 400, { message: 'Unknown permissions: ' + unknown.join(', ') });
      if (role.roleName === 'Administrator' && (b.permissions || []).indexOf('admin.users.manage') === -1) {
        return send(res, 400, { message: 'The Administrator role must keep admin.users.manage, or nobody could administer access.' });
      }
      role.permissions = (b.permissions || []).slice();
      writeAudit(me, null, 'RolePermissionsChanged', 'Success', role.roleName + ': ' + role.permissions.join(', '), ip);
      send(res, 204);
    });
    return true;
  }
  if (req.method === 'GET' && pathname === '/api/admin/group-mappings') { send(res, 200, mappings); return true; }
  if (req.method === 'POST' && pathname === '/api/admin/group-mappings') {
    readBody(req).then(function (b) {
      var claimType = b.claimType === 'role' ? 'role' : 'group';
      var value = String(b.claimValue || '').trim();
      if (!value) return send(res, 400, { message: claimType === 'group' ? "Enter the group's object id." : "Enter the app role's value." });
      if (claimType === 'group' && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)) {
        return send(res, 400, { message: "A group is identified by its object id (a GUID) — copy it from the group's Overview page in Entra ID." });
      }
      var hasRole = !!b.roleName, hasAttr = !!(b.attributeKey && String(b.attributeValue || '').trim());
      if (!hasRole && !hasAttr) return send(res, 400, { message: 'Choose a role or a data-access value to grant.' });
      if (hasRole && !roles.some(function (r) { return r.roleName === b.roleName; })) return send(res, 400, { message: 'Unknown role ' + b.roleName + '.' });
      if (hasAttr && !ATTRIBUTES.some(function (a) { return a.attributeKey === b.attributeKey; })) return send(res, 400, { message: "Unknown attribute '" + b.attributeKey + "'." });
      var row = { mappingId: ++mappingSeq, claimType: claimType, claimValue: value, displayName: (b.displayName || '').trim() || null,
        roleName: hasRole ? b.roleName : null, attributeKey: hasAttr ? b.attributeKey : null, attributeValue: hasAttr ? String(b.attributeValue).trim() : null };
      mappings.push(row);
      writeAudit(me, null, 'GroupMappingAdded', 'Success', claimType + ' ' + (row.displayName || value) + ' -> ' + (hasRole ? 'role ' + row.roleName : row.attributeKey + '=' + row.attributeValue), ip);
      send(res, 200, row);
    });
    return true;
  }
  if ((m = pathname.match(/^\/api\/admin\/group-mappings\/(\d+)$/)) && req.method === 'DELETE') {
    var mi = mappings.findIndex(function (x) { return x.mappingId === Number(m[1]); });
    if (mi === -1) { send(res, 404, { message: 'Mapping not found.' }); return true; }
    var removed = mappings.splice(mi, 1)[0];
    writeAudit(me, null, 'GroupMappingRemoved', 'Success', removed.claimType + ' ' + (removed.displayName || removed.claimValue), ip);
    send(res, 204);
    return true;
  }
  if (req.method === 'GET' && pathname === '/api/admin/permissions') { send(res, 200, PERMISSIONS); return true; }
  if (req.method === 'GET' && pathname === '/api/admin/attributes') { send(res, 200, ATTRIBUTES); return true; }
  if (req.method === 'GET' && pathname === '/api/admin/policies') { send(res, 200, policies); return true; }
  if ((m = pathname.match(/^\/api\/admin\/policies\/(\d+)$/)) && req.method === 'PUT') {
    var policy = policies.filter(function (p) { return p.policyId === Number(m[1]); })[0];
    if (!policy) { send(res, 404, { message: 'Policy not found.' }); return true; }
    readBody(req).then(function (b) {
      var condition = b.conditionJson && String(b.conditionJson).trim() ? b.conditionJson : policy.conditionJson;
      var error = validateCondition(condition);
      if (error) return send(res, 400, { message: 'The condition is not valid: ' + error });
      policy.isActive = !!b.isActive;
      policy.conditionJson = condition;
      if (b.denyMessage) policy.denyMessage = b.denyMessage;
      policy.updatedUtc = new Date().toISOString();
      writeAudit(me, null, 'PolicyChanged', 'Success', policy.policyName + ': active=' + policy.isActive, ip);
      send(res, 204);
    });
    return true;
  }
  return false;
}

module.exports = {
  routes: routes,
  authenticate: authenticate,
  check: check,
  permissionsOf: permissionsOf,
  scopeValues: scopeValues,
  writeAudit: writeAudit,
  auditLog: function () { return audit; }
};
