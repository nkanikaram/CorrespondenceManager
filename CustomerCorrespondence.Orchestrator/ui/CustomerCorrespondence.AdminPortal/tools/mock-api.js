/*
 * Mock backend for local UI work without the .NET API: serves the app plus
 * in-memory versions of every /api endpoint the portal calls.
 *
 *   npm run start:mock        → http://localhost:8081
 *
 * /api/ai/messages is a keyword-based stand-in for the AI proxy — it returns
 * realistic tool_use payloads so the assistant's action plumbing can be
 * exercised, but it is not a language model.
 *
 * Sign-in, RBAC and ABAC mirror the API (see tools/mock-auth.js). Users: admin,
 * operator, analyst, viewer — password EccoDev!2026.
 */
'use strict';

var http = require('http');
var fs = require('fs');
var path = require('path');
var url = require('url');
var crypto = require('crypto');
var vm = require('vm');
var auth = require('./mock-auth');
var pdfs = require('./mock-pdf');
var features = require('./mock-features');

var ROOT = path.resolve(__dirname, '..');
var args = process.argv.slice(2);
var portArg = args.indexOf('--port');
var PORT = Number(portArg !== -1 && args[portArg + 1]) || Number(process.env.PORT) || 8081;
var MAX_PAGE_SIZE = 500; // mimic the backend's page-size cap

// The grid's own filter engine, so server-side filtering here matches the UI exactly.
var gridFilter = (function () {
  var service;
  var sandbox = { angular: { module: function () { return { factory: function (name, fn) { service = fn(); } }; } } };
  vm.runInNewContext(fs.readFileSync(path.join(ROOT, 'app/services/grid-filter.service.js'), 'utf8'), sandbox);
  return service;
})();

var MIME = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.json': 'application/json', '.svg': 'image/svg+xml', '.png': 'image/png', '.ico': 'image/x-icon'
};

// ---- Seeded data ----
var seed = 42;
function rand() { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; }
function pick(list) { return list[Math.floor(rand() * list.length)]; }

var STATUSES = ['Completed', 'Completed', 'Completed', 'Processing', 'Submitted', 'Failed', 'AwaitingImaging'];
var CUSTOMERS = ['Payment Guard Advantage', 'Annuity Correspondence', 'Claims System', 'Life Services'];
var DELIVERY = ['Mail', 'Mail', 'MAIL', 'ePresent', 'Email'];
var NAMES = ['John Doe', 'Jane Smith', 'Mike Smith', 'Ana Lopez', 'Wei Chen', 'Priya Patel'];

var records = [];
for (var i = 0; i < 320; i++) {
  var created = new Date(Date.now() - Math.floor(rand() * 60 * 86400000));
  var claim = 'CL-' + (10000 + Math.floor(rand() * 900));
  var status = pick(STATUSES);
  var name = status === 'Failed' && rand() < 0.5 ? '' : pick(NAMES);
  var customer = pick(CUSTOMERS);
  records.push({
    correspondenceId: crypto.randomUUID(),
    docId: 'D-' + (100000 + i),
    claimNumber: claim,
    requestingDomainName: customer,
    deliveryMethodCode: pick(DELIVERY),
    requestCreationTS: created.toISOString(),
    latestStatus: status,
    requestPayload: JSON.stringify({
      Process: {
        DocId: '', RequestingSystemName: customer, ECCOTrackingClaimNumber: claim,
        ECCOTrackingCustomerId: '', ECCOTrackingPolicyNumber: 'PL-' + (100000 + i), DeliveryMethod: 'Mail',
        DocumentDescription: 'Welcome Kit',
        MailDeliveryInfo: {
          RecipientFullName: name,
          RecipientAddress: { RecipientAddressLine1: '123 Main St', RecipientAddressCity: 'Springfield', RecipientAddressST: 'IL', RecipientAddressZIP: '62701', RecipientCountry: 'US' }
        }
      }
    })
  });
}

pdfs.seed(records);
features.evaluate(records);
features.evaluateSla(records);
setInterval(function () { features.evaluate(records); features.evaluateSla(records); }, 120000).unref();

var conversations = {};
var usage = { inputTokens: 0, outputTokens: 0, claudeCalls: 0, routerHits: 0 };

// ---- Helpers ----
function send(res, status, body) {
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(body === undefined ? '' : JSON.stringify(body));
}

function readBody(req) {
  return new Promise(function (resolve) {
    var chunks = [];
    req.on('data', function (c) { chunks.push(c); });
    req.on('end', function () {
      try { resolve(chunks.length ? JSON.parse(Buffer.concat(chunks).toString('utf8')) : {}); } catch (e) { resolve({}); }
    });
  });
}

function contains(value, needle) {
  return String(value || '').toLowerCase().indexOf(String(needle).toLowerCase()) !== -1;
}

var SORT_FIELDS = {
  id: 'correspondenceId', docId: 'docId', claimNumber: 'claimNumber', customer: 'requestingDomainName',
  delivery: 'deliveryMethodCode', created: 'requestCreationTS', requestCreationTS: 'requestCreationTS', status: 'latestStatus'
};

/** Record as the grid sees it (field names the grid filter descriptors use). */
function gridRow(r) {
  return { id: r.correspondenceId, docId: r.docId, claimNumber: r.claimNumber, customer: r.requestingDomainName,
    delivery: r.deliveryMethodCode, created: r.requestCreationTS, status: r.latestStatus, sla: slaGroup(features.slaFor(r)) };
}

/** The SLA filter's value (as Sla/Sla.cs SlaSql): Breached | At risk | On track | Finished. Accepts "AtRisk" etc. too. */
function slaGroup(sla) {
  if (!sla) return null;
  return { Breached: 'Breached', AtRisk: 'At risk', OnTrack: 'On track', Met: 'Finished', Missed: 'Finished' }[sla.state] || null;
}
function normalizeSlaFilter(node) {
  if (!node || typeof node !== 'object') return node;
  if (Array.isArray(node.filters)) return { logic: node.logic, filters: node.filters.map(normalizeSlaFilter) };
  if (node.field !== 'sla') return node;
  var key = String(node.value || '').replace(/[^a-z]/gi, '').toLowerCase();
  var value = { breached: 'Breached', atrisk: 'At risk', ontrack: 'On track', finished: 'Finished', met: 'Finished', missed: 'Finished' }[key];
  if (!value || (node.operator !== 'eq' && node.operator !== 'neq')) {
    var e = new Error('The SLA filter must be one of: Breached, At risk, On track, Finished.'); e.filterError = true; throw e;
  }
  return Object.assign({}, node, { value: value });
}

/** 'yyyy-MM-dd' date values (as the UI sends them) become local Dates for gridFilter's by-day comparison. */
function reviveDates(node) {
  if (!node || typeof node !== 'object') return node;
  if (Array.isArray(node.filters)) return { logic: node.logic, filters: node.filters.map(reviveDates) };
  var m = node.field === 'created' && typeof node.value === 'string' && /^(\d{4})-(\d{2})-(\d{2})$/.exec(node.value);
  return m ? Object.assign({}, node, { value: new Date(+m[1], +m[2] - 1, +m[3]) }) : node;
}

/** ABAC data scope: the user's scope.customer / scope.deliveryMethod ('*' = all, none = nothing). */
function inScope(user, r) {
  var customers = auth.scopeValues(user, 'scope.customer');
  var methods = auth.scopeValues(user, 'scope.deliveryMethod');
  var has = function (list, v) { return list.some(function (x) { return x.toLowerCase() === String(v || '').toLowerCase(); }); };
  return (customers === null || has(customers, r.requestingDomainName)) && (methods === null || has(methods, r.deliveryMethodCode));
}

function search(q, user) {
  var scoped = records.filter(function (r) { return inScope(user, r); });
  if (q.filter) {
    var keep = {};
    gridFilter.filterBy(scoped.map(gridRow), normalizeSlaFilter(reviveDates(q.filter)), { dateFields: ['created'] }).forEach(function (row) { keep[row.id] = true; });
    scoped = scoped.filter(function (r) { return keep[r.correspondenceId]; });
  }
  var rows = scoped.filter(function (r) {
    var terms = q.freeTerms && q.freeTerms.length ? q.freeTerms : (q.freeText ? [q.freeText] : []);
    if (terms.length && !terms.some(function (t) { return ['correspondenceId', 'docId', 'claimNumber', 'requestingDomainName', 'latestStatus'].some(function (k) { return contains(r[k], t); }); })) return false;
    if (q.correspondenceId && r.correspondenceId !== q.correspondenceId) return false;
    if (q.docId && r.docId.toLowerCase() !== String(q.docId).toLowerCase()) return false;
    if (q.claimNumber && !contains(r.claimNumber, q.claimNumber)) return false;
    if (q.customerName && !contains(r.requestingDomainName, q.customerName)) return false;
    if (q.latestStatuses && q.latestStatuses.length) {
      if (!q.latestStatuses.some(function (s) { return s.toLowerCase() === r.latestStatus.toLowerCase(); })) return false;
    } else if (q.latestStatus && q.latestStatus.toLowerCase() !== r.latestStatus.toLowerCase()) return false;
    if (q.deliveryMethodCode && q.deliveryMethodCode.toLowerCase() !== r.deliveryMethodCode.toLowerCase()) return false;
    if (q.fromDate && r.requestCreationTS < new Date(q.fromDate).toISOString()) return false;
    if (q.toDate) {
      var to = new Date(q.toDate);
      if (/^\d{4}-\d{2}-\d{2}$/.test(q.toDate)) to = new Date(to.getTime() + 86400000 - 1);
      if (r.requestCreationTS > to.toISOString()) return false;
    }
    return true;
  });
  var field = SORT_FIELDS[q.sortBy] || 'requestCreationTS';
  var dir = q.sortBy ? (q.sortDirection === 'asc' ? 1 : -1) : -1;
  rows.sort(function (a, b) { return String(a[field]).localeCompare(String(b[field])) * dir; });

  var pageSize = Math.min(Number(q.pageSize) || 25, MAX_PAGE_SIZE);
  var page = Math.max(Number(q.page) || 1, 1);
  var items = rows.slice((page - 1) * pageSize, page * pageSize).map(function (r) {
    if (q.includePayload && !q.payloadsHidden) return r;
    var copy = Object.assign({}, r);
    delete copy.requestPayload;
    return copy;
  });
  return { page: page, pageSize: pageSize, total: rows.length, items: items };
}

/** Keyword stand-in for the AI proxy, shaped like an Anthropic Messages API response. */
function fakeAi(body) {
  var messages = body.messages || [];
  var prompt = String((messages[messages.length - 1] || {}).content || '').toLowerCase();
  var tools = [];
  var text = null;

  function tool(name, input) { tools.push({ type: 'tool_use', id: 'toolu_' + tools.length, name: name, input: input }); }

  var statusMatch = STATUSES.filter(function (s) { return prompt.indexOf(s.toLowerCase()) !== -1; })[0];
  var top = prompt.match(/\b(?:top|latest)\s+(\d+)/);
  var claim = prompt.match(/\b[a-z]{2,3}-\d{3,6}\b/);

  if (/\bundo\b|go back/.test(prompt)) tool('undo_last_search', {});
  else if (/clear|reset/.test(prompt)) tool('clear_filters', {});
  else if (/\bsend\b|\bsubmit\b/.test(prompt)) {
    tool('send_correspondence_request', {
      documentDescription: 'Welcome Kit', recipientFullName: 'John Doe', addressLine1: '123 Main St',
      city: 'Springfield', state: 'IL', zip: '62701', claimNumber: claim ? claim[0].toUpperCase() : 'CL-5432', deliveryMethod: 'Mail'
    });
  } else {
    if (statusMatch || claim || top || /\b(show|find|list|records)\b/.test(prompt)) {
      var input = {};
      if (statusMatch) input.status = statusMatch;
      if (claim) input.claimNumber = claim[0].toUpperCase();
      if (top) { input.pageSize = Number(top[1]); input.sortBy = 'requestCreationTS'; input.sortDirection = 'desc'; }
      if (/how many/.test(prompt) && !statusMatch && !claim) input = null;
      if (input) tool('search_grid', input);
    }
    if (/chart|graph|plot|visuali/.test(prompt)) {
      tool('show_chart', {
        metric: /deliver.*status/.test(prompt) ? 'deliveryByStatus' : /customer/.test(prompt) ? 'customer' : /deliver/.test(prompt) ? 'delivery' : 'status',
        chartType: /donut|doughnut/.test(prompt) ? 'donut' : /pie/.test(prompt) ? 'pie' : 'bar'
      });
    }
    if (/export|download|excel/.test(prompt)) {
      tool('export_results', { scope: /\ball\b|everything/.test(prompt) ? 'all' : /select|checked/.test(prompt) ? 'selected' : 'page' });
    }
  }

  if (!tools.length) {
    var counts = {};
    records.forEach(function (r) { counts[r.latestStatus] = (counts[r.latestStatus] || 0) + 1; });
    text = '**Mock assistant** — here is the status breakdown across all ' + records.length + ' records:\n\n' +
      '| Status | Count |\n|---|---|\n' +
      Object.keys(counts).map(function (k) { return '| ' + k + ' | ' + counts[k] + ' |'; }).join('\n') +
      '\n\n(The mock server answers with fixed data; connect the real backend for AI answers.)';
  }

  var inputTokens = Math.round(String(body.system || '').length / 4) + 600;
  var outputTokens = 40 + tools.length * 30;
  usage.inputTokens += inputTokens;
  usage.outputTokens += outputTokens;
  usage.claudeCalls++;

  return {
    id: 'msg_mock',
    type: 'message',
    role: 'assistant',
    content: tools.length ? [{ type: 'text', text: 'On it.' }].concat(tools) : [{ type: 'text', text: text }],
    stop_reason: tools.length ? 'tool_use' : 'end_turn',
    usage: { input_tokens: inputTokens, output_tokens: outputTokens }
  };
}

// ---- API routes ----
function api(req, res, pathname, query) {
  var m;
  if (auth.routes(req, res, pathname, readBody, send)) return;

  // Legacy system-to-system endpoint: unchanged (no portal sign-in), like the real API.
  if (req.method === 'POST' && pathname === '/api/CommManager/processcorrespondencerequest') {
    return readBody(req).then(function () { send(res, 202, { docId: 'D-' + Math.floor(rand() * 1e6) }); });
  }

  var user = auth.authenticate(req);
  if (!user) return send(res, 401, { message: 'Sign in to continue.' });
  if (user.mustChangePassword) return send(res, 403, { message: 'Change your temporary password first.' });

  function requirePermission(key) {
    if (auth.permissionsOf(user).indexOf(key) !== -1) return true;
    auth.writeAudit(user, null, 'AccessDenied', 'Denied', key + ': no role grants it');
    send(res, 403, { message: "You don't have permission to do this." });
    return false;
  }
  function denied(permission, decision, what) {
    auth.writeAudit(user, null, 'AccessDenied', 'Denied', permission + ' on ' + what + ": policy '" + decision.policy + "'");
    send(res, 403, { message: decision.message, policy: decision.policy });
  }

  if (req.method === 'POST' && pathname === '/api/correspondence/search') {
    if (!requirePermission('correspondence.read')) return;
    return readBody(req).then(function (b) {
      var hiddenReason = null;
      if (b.includePayload) {
        var payload = auth.check(user, 'correspondence.payload.read', {});
        if (!payload.allowed) { b.payloadsHidden = true; hiddenReason = payload.message; }
      }
      var result;
      try { result = search(b, user); } catch (e) { return send(res, 400, { message: e.message }); }
      if (String(b.purpose || '').toLowerCase() === 'export') {
        var exp = auth.check(user, 'correspondence.export', { rowCount: String(result.total) });
        if (!exp.allowed) return denied('correspondence.export', exp, result.total + ' rows');
      }
      result.items = result.items.map(function (r) { return Object.assign({}, r, { sla: features.slaFor(r) }); });
      result.payloadsHidden = !!hiddenReason;
      result.payloadsHiddenReason = hiddenReason;
      send(res, 200, result);
    });
  }
  if (req.method === 'GET' && pathname === '/api/correspondence/statuses') {
    if (!requirePermission('correspondence.read')) return;
    return send(res, 200, STATUSES.filter(function (s, i, a) { return a.indexOf(s) === i; }));
  }
  if (req.method === 'GET' && (m = pathname.match(/^\/api\/(correspondence\/values|admin\/lookups)\/(customer|delivery)$/))) {
    var adminLookup = m[1] === 'admin/lookups';
    if (!requirePermission(adminLookup ? 'admin.users.manage' : 'correspondence.read')) return;
    var key = m[2] === 'customer' ? 'requestingDomainName' : 'deliveryMethodCode';
    var seen = {};
    records.filter(function (r) { return adminLookup || inScope(user, r); }).forEach(function (r) { seen[r[key]] = true; });
    return send(res, 200, Object.keys(seen).sort());
  }
  if (req.method === 'POST' && pathname === '/api/portal/correspondence/requests') {
    if (!requirePermission('correspondence.create')) return;
    return readBody(req).then(function (b) {
      var customer = b.Process && b.Process.RequestingSystemName;
      if (!customer || !String(customer).trim()) return send(res, 400, { message: 'RequestingSystemName is required.' });
      var decision = auth.check(user, 'correspondence.create', { customer: customer, deliveryMethod: b.Process && b.Process.DeliveryMethod });
      if (!decision.allowed) return denied('correspondence.create', decision, "customer '" + customer + "'");
      var docId = 'D-' + Math.floor(rand() * 1e6);
      var created = {
        correspondenceId: crypto.randomUUID(), docId: docId, claimNumber: (b.Process && b.Process.ECCOTrackingClaimNumber) || null,
        requestingDomainName: customer, deliveryMethodCode: (b.Process && b.Process.DeliveryMethod) || 'Mail',
        requestCreationTS: new Date().toISOString(), latestStatus: 'Submitted', requestPayload: JSON.stringify(b)
      };
      records.unshift(created);
      features.noteChange('new', created.correspondenceId);
      auth.writeAudit(user, null, 'CorrespondenceCreated', 'Success', "customer '" + customer + "' (HTTP 202)", null, created.correspondenceId);
      send(res, 202, { docId: docId });
    });
  }
  if (req.method === 'POST' && (m = pathname.match(/^\/api\/portal\/correspondence\/([^/]+)\/resubmit$/))) {
    if (!requirePermission('correspondence.resubmit')) return;
    var record = records.filter(function (r) { return r.correspondenceId === decodeURIComponent(m[1]); })[0];
    if (!record) return send(res, 404, { message: "That correspondence record doesn't exist." });
    return readBody(req).then(function (b) {
      var decision = auth.check(user, 'correspondence.resubmit', { customer: record.requestingDomainName, deliveryMethod: record.deliveryMethodCode, status: record.latestStatus });
      if (!decision.allowed) return denied('correspondence.resubmit', decision, 'record ' + record.correspondenceId);
      var payloadCustomer = b.Process && b.Process.RequestingSystemName;
      if (String(payloadCustomer || '').toLowerCase() !== record.requestingDomainName.toLowerCase()) {
        return send(res, 400, { message: "The payload's requesting system doesn't match the original record." });
      }
      auth.writeAudit(user, null, 'CorrespondenceResubmitted', 'Success', 'record ' + record.correspondenceId + ' (HTTP 202)');
      send(res, 202, { docId: 'D-' + Math.floor(rand() * 1e6) });
    });
  }
  if (pdfs.routes(req, res, pathname, {
    records: function () { return records; },
    readBody: readBody,
    send: send,
    requirePermission: requirePermission,
    can: function (key) { return auth.permissionsOf(user).indexOf(key) !== -1; },
    check: function (permission, resource) { return auth.check(user, permission, resource); },
    inScope: function (r) { return inScope(user, r); },
    audit: function (eventType, detail) {
      auth.writeAudit(user, null, eventType, 'Success', detail);
      var pm = /record (\S+)/.exec(detail || '');
      if (eventType === 'PdfGenerated' && pm) features.noteChange('pdf', pm[1]);
    }
  })) return;

  if (features.routes(req, res, pathname, query, {
    records: function () { return records; },
    setRecords: function (list) { records = list; },
    user: user,
    readBody: readBody,
    send: send,
    requirePermission: requirePermission,
    can: function (key) { return auth.permissionsOf(user).indexOf(key) !== -1; },
    check: function (permission, resource) { return auth.check(user, permission, resource); },
    inScope: function (r) { return inScope(user, r); },
    audit: function (eventType, detail, resourceId, outcome) { auth.writeAudit(user, null, eventType, outcome || 'Success', detail, null, resourceId); },
    auditLog: auth.auditLog
  })) return;

  if (/^\/api\/(ai|Ai|chathistory)\//.test(pathname) && !requirePermission('ai.use')) return;
  if (req.method === 'GET' && pathname === '/api/Ai/usage/summary') return send(res, 200, usage);
  if (req.method === 'POST' && pathname === '/api/ai/messages') {
    return readBody(req).then(function (b) { send(res, 200, fakeAi(b)); });
  }

  // Conversations belong to the signed-in user (any client-sent userId is ignored).
  if (pathname === '/api/chathistory/conversations') {
    if (req.method === 'GET') {
      var list = Object.keys(conversations).map(function (id) { return conversations[id]; })
        .filter(function (c) { return c.userId === user.userId; })
        .sort(function (a, b) { return b.updatedTS.localeCompare(a.updatedTS); })
        .map(function (c) { return { conversationId: c.conversationId, title: c.title, createdTS: c.createdTS, updatedTS: c.updatedTS }; });
      return send(res, 200, list);
    }
    if (req.method === 'POST') {
      return readBody(req).then(function (b) {
        var now = new Date().toISOString();
        var id = crypto.randomUUID();
        conversations[id] = { conversationId: id, userId: user.userId, title: b.title || null, createdTS: now, updatedTS: now, messages: [] };
        send(res, 200, { conversationId: id });
      });
    }
  }
  if ((m = pathname.match(/^\/api\/chathistory\/conversations\/([^/]+)(\/messages)?$/))) {
    var convo = conversations[decodeURIComponent(m[1])];
    if (!convo || convo.userId !== user.userId) return send(res, 404, { message: 'Conversation not found' });
    if (m[2] && req.method === 'POST') {
      return readBody(req).then(function (b) {
        var now = new Date().toISOString();
        convo.messages.push({ messageId: crypto.randomUUID(), role: b.role, content: b.content, createdTS: now });
        convo.updatedTS = now;
        send(res, 200, {});
      });
    }
    if (!m[2] && req.method === 'GET') return send(res, 200, convo);
    if (!m[2] && req.method === 'DELETE') {
      delete conversations[convo.conversationId];
      return send(res, 204);
    }
  }
  return send(res, 404, { message: 'No mock for ' + req.method + ' ' + pathname });
}

// ---- Static files ----
function serveStatic(res, pathname) {
  var rel = decodeURIComponent(pathname === '/' ? '/index.html' : pathname);
  var file = path.resolve(ROOT, '.' + rel);
  if (file.indexOf(ROOT + path.sep) !== 0) return send(res, 403, { message: 'Forbidden' });
  fs.readFile(file, function (err, data) {
    if (err) return send(res, 404, { message: 'Not found' });
    res.writeHead(200, { 'Content-Type': MIME[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-cache' });
    res.end(data);
  });
}

http.createServer(function (req, res) {
  var parsed = url.parse(req.url, true);
  if (parsed.pathname.indexOf('/api/') === 0) {
    Promise.resolve(api(req, res, parsed.pathname, parsed.query)).catch(function (e) { send(res, 500, { message: String(e) }); });
  } else {
    serveStatic(res, parsed.pathname);
  }
}).listen(PORT, function () {
  console.log('Mock Correspondence Manager running at http://localhost:' + PORT);
  console.log('Sign in as admin, operator, analyst or viewer with password EccoDev!2026');
  if (args.indexOf('--open') !== -1) {
    var target = 'http://localhost:' + PORT + '/';
    var cmd = process.platform === 'win32' ? 'start "" "' + target + '"' : process.platform === 'darwin' ? 'open ' + target : 'xdg-open ' + target;
    require('child_process').exec(cmd);
  }
});
