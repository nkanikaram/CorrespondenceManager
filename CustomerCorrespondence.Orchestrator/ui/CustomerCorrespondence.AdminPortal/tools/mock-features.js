/*
 * Mock versions of the portal feature endpoints added with SSO:
 *   record details, recycle bin, letter templates (+ preview), alerts and the change feed.
 * Microsoft sign-in itself (config, token exchange, group mappings) lives in mock-auth.js.
 * In-memory only; mirrors the shapes of the .NET API so the UI can be exercised without it.
 */
'use strict';

var crypto = require('crypto');
var pdfs = require('./mock-pdf');

var RECYCLE_DAYS = 30;

// ---- SLA (mirrors Sla/Sla.cs) ----
var sla = {
  businessDays: true, atRiskPercent: 80, defaultDays: 5,
  stopStatuses: ['Completed', 'Delivered', 'EDeliveryConfirmed', 'ImagingConfirmed', 'Archived'],
  customers: [{ customerName: 'Claims System', targetDays: 3 }],
  updatedUtc: null, updatedBy: null
};
var DAY = 86400000;
function isWeekend(d) { var w = d.getUTCDay(); return w === 0 || w === 6; }
function nextMidnight(t) { var d = new Date(t); return Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate() + 1); }
function businessMs(start, end) {
  var total = 0, t = start;
  while (t < end) { var n = Math.min(nextMidnight(t), end); if (!isWeekend(new Date(t))) total += n - t; t = n; }
  return total;
}
function addBusinessMs(start, ms) {
  var t = start, left = ms;
  for (;;) {
    if (isWeekend(new Date(t))) { t = nextMidnight(t); continue; }
    var toMid = nextMidnight(t) - t;
    if (left <= toMid) return t + left;
    left -= toMid; t = nextMidnight(t);
  }
}
/** A record's finishing time: stable per record (the mock has no status history timestamps). */
function finishedAt(r) {
  var h = 0; for (var i = 0; i < r.correspondenceId.length; i++) h = (h * 31 + r.correspondenceId.charCodeAt(i)) >>> 0;
  return Math.min(Date.now(), Date.parse(r.requestCreationTS) + (0.2 + (h % 1000) / 1000 * 7) * DAY);
}
function slaFor(r) {
  var created = Date.parse(r.requestCreationTS);
  if (isNaN(created)) return null;
  var target = (sla.customers.filter(function (c) { return c.customerName.toLowerCase() === String(r.requestingDomainName || '').toLowerCase(); })[0] || { targetDays: sla.defaultDays }).targetDays;
  var stop = sla.stopStatuses.some(function (s) { return s.toLowerCase() === String(r.latestStatus || '').toLowerCase(); });
  var end = stop ? finishedAt(r) : Date.now();
  var targetMs = target * DAY;
  var used = sla.businessDays ? businessMs(created, end) : end - created;
  var percent = Math.floor(used / targetMs * 100);
  var state = stop ? (used <= targetMs ? 'Met' : 'Missed') : used > targetMs ? 'Breached' : percent >= sla.atRiskPercent ? 'AtRisk' : 'OnTrack';
  return {
    state: state, targetDays: target, businessDays: sla.businessDays,
    dueUtc: new Date(sla.businessDays ? addBusinessMs(created, targetMs) : created + targetMs).toISOString(),
    stoppedUtc: stop ? new Date(end).toISOString() : null,
    elapsedSeconds: Math.floor((end - created) / 1000), percentUsed: percent
  };
}
var bin = [];          // { entry, record }
var alerts = [];
var alertSeq = 0;
var changes = [];      // { at, kind, id }

// ---- Letter templates ----
var PLACEHOLDERS = [
  ['RecipientName', 'Recipient full name'], ['RecipientFirstName', 'Recipient first name'], ['RecipientLastName', 'Recipient last name'],
  ['ClaimNumber', 'Claim number'], ['PolicyNumber', 'Policy number (masked, last 4 digits)'], ['CustomerId', 'Customer id'],
  ['DocId', 'Document id'], ['DocumentType', 'Document type, e.g. Welcome Kit'], ['RequestingSystem', 'Requesting system'],
  ['DeliveryMethod', 'Delivery method'], ['Today', "Today's date"], ['CompanyName', 'Company name (branding)'],
  ['CompanyPhone', 'Company phone (branding)'], ['CompanyWebsite', 'Company website (branding)']
].map(function (p) { return { name: p[0], description: p[1] }; });
var KNOWN = PLACEHOLDERS.map(function (p) { return p.name.toLowerCase(); });

var templateSeq = 0;
var templates = [];
function addTemplate(t, status) {
  var now = new Date().toISOString();
  var row = {
    templateId: ++templateSeq, documentType: t.documentType, version: 1 + templates.filter(function (x) { return x.documentType === t.documentType; }).length,
    status: status || 'Draft', name: t.name || t.documentType + ' letter', subject: t.subject || '', salutation: t.salutation || 'Dear {{RecipientName}},',
    body: t.body || '', closing: t.closing || 'Sincerely,', signatureName: t.signatureName || null, signatureTitle: t.signatureTitle || null,
    createdUtc: now, createdBy: 'seed', updatedUtc: now, publishedUtc: null, publishedBy: null
  };
  templates.push(row);
  return row;
}
addTemplate({
  documentType: 'Welcome Kit', name: 'Welcome kit cover letter', subject: 'Welcome — your policy {{PolicyNumber}}',
  body: 'Thank you for choosing {{CompanyName}}. Your welcome kit for claim {{ClaimNumber}} is enclosed.\n\nIf you have any questions, call us on {{CompanyPhone}} or visit {{CompanyWebsite}}.',
  signatureName: 'Customer Care Team', signatureTitle: '{{CompanyName}}'
});
addTemplate({
  documentType: 'Claim Update', name: 'Claim status update', subject: 'An update on claim {{ClaimNumber}}',
  body: 'We are writing about your claim {{ClaimNumber}}.\n\nWe will contact you if we need anything else.',
  signatureName: 'Claims Team', signatureTitle: '{{CompanyName}}'
});

var BRAND = { CompanyName: 'ECCO Insurance', CompanyPhone: '1-800-555-0100', CompanyWebsite: 'www.example.com' };
var SAMPLE = { RecipientName: 'Jordan Sample', ClaimNumber: 'CL-10001', PolicyNumber: 'PL-******1234', DocId: 'D-000001',
  DocumentType: 'Welcome Kit', RequestingSystem: 'Life Services', DeliveryMethod: 'Mail', CustomerId: 'C-1001' };

function unknownPlaceholders(t) {
  var text = [t.subject, t.salutation, t.body, t.closing, t.signatureName, t.signatureTitle].join('\n');
  var found = [];
  (text.match(/\{\{\s*([A-Za-z.]+)\s*\}\}/g) || []).forEach(function (tok) {
    var n = tok.replace(/[{}\s]/g, '');
    if (KNOWN.indexOf(n.toLowerCase()) === -1 && n.indexOf('payload.') !== 0 && found.indexOf(n) === -1) found.push(n);
  });
  return found;
}

function valuesFromPayload(json) {
  var v = {};
  try {
    var p = JSON.parse(json).Process || {};
    var mail = p.MailDeliveryInfo || {};
    v.RecipientName = mail.RecipientFullName || '';
    v.ClaimNumber = p.ECCOTrackingClaimNumber || '';
    v.PolicyNumber = p.ECCOTrackingPolicyNumber ? '******' + String(p.ECCOTrackingPolicyNumber).slice(-4) : '';
    v.CustomerId = p.ECCOTrackingCustomerId || '';
    v.DocId = p.DocId || '';
    v.DocumentType = p.DocumentDescription || '';
    v.RequestingSystem = p.RequestingSystemName || '';
    v.DeliveryMethod = p.DeliveryMethod || '';
  } catch (e) { /* invalid JSON: everything missing */ }
  return v;
}

function renderLetter(t, values) {
  var missing = [];
  var all = Object.assign({ Today: new Date().toISOString().slice(0, 10) }, BRAND, values);
  var parts = (all.RecipientName || '').split(' ');
  all.RecipientFirstName = all.RecipientFirstName || parts[0] || '';
  all.RecipientLastName = all.RecipientLastName || parts.slice(1).join(' ');
  function fill(text) {
    return String(text || '').replace(/\{\{\s*([A-Za-z.]+)\s*\}\}/g, function (_, name) {
      var key = Object.keys(all).filter(function (k) { return k.toLowerCase() === name.toLowerCase(); })[0];
      var val = key ? all[key] : '';
      if (!val) { if (missing.indexOf(name) === -1) missing.push(name); return '[' + name + ']'; }
      return val;
    });
  }
  var lines = [BRAND.CompanyName, all.Today, '', fill(t.subject), '', fill(t.salutation), ''];
  String(fill(t.body)).split('\n').forEach(function (l) { lines.push(l); });
  lines.push('', fill(t.closing), fill(t.signatureName), fill(t.signatureTitle));
  return { bytes: pdfs.buildPdf(fill(t.subject) || 'Letter', lines), missing: missing };
}

// ---- Change feed / alerts ----
function noteChange(kind, id) { changes.push({ at: Date.now(), kind: kind, id: id }); if (changes.length > 2000) changes.shift(); }

function raiseAlert(ruleKey, severity, title, message) {
  var a = { alertId: ++alertSeq, ruleKey: ruleKey, severity: severity, title: title, message: message, metricValue: null, baselineValue: null,
    raisedUtc: new Date().toISOString(), acknowledgedUtc: null, acknowledgedBy: null, notifiedTeams: false, notifiedEmail: false };
  alerts.unshift(a);
  noteChange('alert', a.alertId);
  return a;
}

/** Same idea as the API's FailedRate rule, against the mock records (run at start-up and every 2 minutes). */
function evaluate(records) {
  var dayAgo = Date.now() - 86400000;
  var recent = records.filter(function (r) { return new Date(r.requestCreationTS).getTime() >= dayAgo; });
  var sample = recent.length >= 20 ? recent : records.slice().sort(function (a, b) { return b.requestCreationTS.localeCompare(a.requestCreationTS); }).slice(0, 60);
  var failed = sample.filter(function (r) { return r.latestStatus === 'Failed'; }).length;
  var rate = sample.length ? failed / sample.length : 0;
  var open = alerts.filter(function (a) { return a.ruleKey === 'FailedRate' && !a.acknowledgedUtc; })[0];
  if (rate >= 0.12 && !open) {
    raiseAlert('FailedRate', rate >= 0.25 ? 'Critical' : 'Warning', 'Failed rate is up',
      Math.round(rate * 100) + '% of the ' + sample.length + ' most recent requests failed (usual: about 8%).');
  }
}

/** Same idea as the API's SlaBreaches rule: open requests whose SLA ran out since the previous check. */
var lastSlaCheck = null;
function evaluateSla(records) {
  var now = Date.now();
  var since = lastSlaCheck || now - 24 * 3600000; // the mock's first check looks back a day, so there's something to see
  lastSlaCheck = now;
  var breached = 0, atRisk = 0, fresh = 0;
  records.forEach(function (r) {
    var s = slaFor(r);
    if (!s) return;
    if (s.state === 'AtRisk') atRisk++;
    if (s.state !== 'Breached') return;
    breached++;
    var due = Date.parse(s.dueUtc);
    if (due > since && due <= now) fresh++;
  });
  if (!fresh || alerts.some(function (a) { return a.ruleKey === 'SlaBreaches' && !a.acknowledgedUtc; })) return;
  var many = fresh !== 1;
  raiseAlert('SlaBreaches', fresh >= 25 ? 'Critical' : 'Warning', fresh + ' request' + (many ? 's' : '') + ' breached the SLA',
    fresh + ' open request' + (many ? 's' : '') + ' passed ' + (many ? 'their' : 'its') + ' SLA since the previous check. ' +
    breached + ' open request' + (breached === 1 ? ' is' : 's are') + ' past the SLA in total' + (atRisk ? ', and ' + atRisk + ' more ' + (atRisk === 1 ? 'is' : 'are') + ' at risk.' : '.') +
    ' In the portal, choose "Breached only" above the grid to see them.');
}

function historyFor(r) {
  var flow = { Completed: ['Submitted', 'Processing', 'Completed'], Processing: ['Submitted', 'Processing'], Submitted: ['Submitted'],
    Failed: ['Submitted', 'Processing', 'Failed'], AwaitingImaging: ['Submitted', 'Processing', 'AwaitingImaging'] }[r.latestStatus] || [r.latestStatus];
  var start = new Date(r.requestCreationTS).getTime();
  return flow.map(function (s, i) {
    return { status: s, statusCode: String(100 + i * 10), updatedTS: new Date(start + i * 3600000 * (i + 1)).toISOString(),
      updatedBy: i === 0 ? r.requestingDomainName : 'ProcessRequestWorker', eventInstanceId: crypto.createHash('md5').update(r.correspondenceId + s).digest('hex').slice(0, 12) };
  });
}

/**
 * Routes; returns true when handled. ctx: { records(), setRecords(list), user, readBody, send, requirePermission, can, check, inScope, audit, auditLog() }.
 */
function routes(req, res, pathname, query, ctx) {
  var m;
  var resource = function (r) { return { customer: r.requestingDomainName, deliveryMethod: r.deliveryMethodCode, status: r.latestStatus }; };
  var find = function (id) { return ctx.records().filter(function (r) { return r.correspondenceId === id; })[0]; };

  // SLA settings
  if (pathname === '/api/portal/sla' && (req.method === 'GET' || req.method === 'PUT')) {
    if (!ctx.requirePermission('sla.manage')) return true;
    if (req.method === 'GET') { ctx.send(res, 200, sla); return true; }
    ctx.readBody(req).then(function (b) {
      var problems = [];
      if (!(b.defaultDays > 0 && b.defaultDays <= 365)) problems.push('The default SLA must be more than 0 and at most 365 days.');
      if (!(b.atRiskPercent >= 1 && b.atRiskPercent <= 99)) problems.push('"At risk" must be between 1% and 99% of the SLA.');
      if (!(b.stopStatuses || []).length) problems.push('Choose at least one status that finishes a request.');
      (b.customers || []).forEach(function (c) {
        if (!(c.targetDays > 0 && c.targetDays <= 365)) problems.push('The SLA for ' + c.customerName + ' must be more than 0 and at most 365 days.');
      });
      if (problems.length) return ctx.send(res, 400, { message: 'Check the SLA settings.', problems: problems });
      sla = { businessDays: !!b.businessDays, atRiskPercent: Number(b.atRiskPercent), defaultDays: Number(b.defaultDays),
        stopStatuses: b.stopStatuses.slice(), customers: (b.customers || []).map(function (c) { return { customerName: c.customerName, targetDays: Number(c.targetDays) }; }),
        updatedUtc: new Date().toISOString(), updatedBy: ctx.user.userName };
      ctx.audit('SlaChanged', 'Default ' + sla.defaultDays + ' days');
      ctx.send(res, 200, sla);
    });
    return true;
  }

  // Record details
  if (req.method === 'GET' && (m = pathname.match(/^\/api\/portal\/correspondence\/([^/]+)\/details$/))) {
    if (!ctx.requirePermission('correspondence.read')) return true;
    var r = find(decodeURIComponent(m[1]));
    if (!r || !ctx.inScope(r)) { ctx.send(res, 404, { message: "That correspondence record doesn't exist." }); return true; }
    var payload = null, hidden = null;
    if (!ctx.can('correspondence.payload.read')) hidden = "You don't have permission to see payloads.";
    else {
      var d = ctx.check('correspondence.payload.read', resource(r));
      if (d.allowed) payload = r.requestPayload; else hidden = d.message;
    }
    var pdf = pdfs.status(r.correspondenceId);
    ctx.send(res, 200, {
      record: { correspondenceId: r.correspondenceId, docId: r.docId, claimNumber: r.claimNumber, customerId: null, requestingDomainName: r.requestingDomainName,
        deliveryMethod: r.deliveryMethodCode, requestCreationTS: r.requestCreationTS, latestStatus: r.latestStatus, archiveDocumentTypeCode: 'WK',
        archiveDocumentSubTypeCode: null, versionCode: '1', vfResponseId: null },
      statusHistory: historyFor(r), payload: payload, payloadHiddenReason: hidden,
      pdf: Object.assign({ correspondenceId: r.correspondenceId }, pdf, { lastError: ctx.can('correspondence.pdf.generate') ? pdf.lastError || null : null }),
      activity: ctx.auditLog().filter(function (a) { return a.resourceId === r.correspondenceId; }).slice(0, 100)
    });
    return true;
  }

  // Delete → recycle bin
  if (req.method === 'POST' && pathname === '/api/portal/correspondence/delete') {
    if (!ctx.requirePermission('correspondence.delete')) return true;
    ctx.readBody(req).then(function (b) {
      var ids = (b.correspondenceIds || []).filter(function (v, i, a) { return v && a.indexOf(v) === i; });
      if (!ids.length) return ctx.send(res, 400, { message: 'Select at least one record to delete.' });
      if (ids.length > 500) return ctx.send(res, 400, { message: 'Delete at most 500 records at a time.' });
      var deleted = [], denied = [], notFound = [];
      ids.forEach(function (id) {
        var rec = find(id);
        if (!rec || !ctx.inScope(rec)) { notFound.push(id); return; }
        var decision = ctx.check('correspondence.delete', resource(rec));
        if (!decision.allowed) {
          denied.push({ correspondenceId: id, message: decision.message, policy: decision.policy });
          ctx.audit('AccessDenied', 'correspondence.delete on record ' + id + ": policy '" + decision.policy + "'", id, 'Denied');
          return;
        }
        deleted.push(id);
        bin.unshift({
          record: rec, pdf: pdfs.status(id),
          entry: { correspondenceId: id, docId: rec.docId, claimNumber: rec.claimNumber, requestingDomainName: rec.requestingDomainName,
            deliveryMethodCode: rec.deliveryMethodCode, requestCreationTS: rec.requestCreationTS, latestStatus: rec.latestStatus,
            deletedUtc: new Date().toISOString(), deletedByUserName: ctx.user.userName, deleteReason: b.reason || null,
            purgeAfterUtc: new Date(Date.now() + RECYCLE_DAYS * 86400000).toISOString(), hasPdf: pdfs.status(id).status === 'Generated' }
        });
        ctx.audit('CorrespondenceDeleted', 'moved to the recycle bin', id);
        noteChange('record', id);
      });
      ctx.setRecords(ctx.records().filter(function (x) { return deleted.indexOf(x.correspondenceId) === -1; }));
      ctx.send(res, 200, { deleted: deleted, denied: denied, notFound: notFound, recycleBinDays: RECYCLE_DAYS });
    });
    return true;
  }

  if (req.method === 'GET' && pathname === '/api/portal/recycle-bin') {
    if (!ctx.requirePermission('correspondence.recycle')) return true;
    var q = String(query.search || '').trim().toLowerCase();
    ctx.send(res, 200, {
      retentionDays: RECYCLE_DAYS,
      items: bin.filter(function (b) { return ctx.inScope(b.record); }).map(function (b) { return b.entry; }).filter(function (e) {
        return !q || [e.docId, e.claimNumber, e.correspondenceId].join(' ').toLowerCase().indexOf(q) !== -1;
      })
    });
    return true;
  }
  if (req.method === 'POST' && pathname === '/api/portal/recycle-bin/restore') {
    if (!ctx.requirePermission('correspondence.recycle')) return true;
    ctx.readBody(req).then(function (b) {
      var ids = (b.correspondenceIds || []).filter(function (v, i, a) { return v && a.indexOf(v) === i; });
      if (!ids.length) return ctx.send(res, 400, { message: 'Select at least one record to restore.' });
      if (ids.length > 500) return ctx.send(res, 400, { message: 'Restore at most 500 records at a time.' });
      var restored = [], notFound = [];
      ids.forEach(function (rid) {
        var it = bin.filter(function (x) { return x.entry.correspondenceId === rid; })[0];
        if (!it || !ctx.inScope(it.record)) { notFound.push(rid); return; }
        bin.splice(bin.indexOf(it), 1);
        ctx.setRecords([it.record].concat(ctx.records()));
        pdfs.restore(rid, it.pdf);
        ctx.audit('CorrespondenceRestored', 'restored from the recycle bin', rid);
        noteChange('record', rid);
        restored.push(rid);
      });
      ctx.send(res, 200, { restored: restored, failed: [], notFound: notFound });
    });
    return true;
  }
  if ((m = pathname.match(/^\/api\/portal\/recycle-bin\/([^/]+)(\/restore)?$/)) && (req.method === 'DELETE' || (req.method === 'POST' && m[2]))) {
    if (!ctx.requirePermission('correspondence.recycle')) return true;
    var id = decodeURIComponent(m[1]);
    var item = bin.filter(function (b) { return b.entry.correspondenceId === id; })[0];
    if (!item || !ctx.inScope(item.record)) { ctx.send(res, 404, { message: "That record isn't in the recycle bin." }); return true; }
    bin.splice(bin.indexOf(item), 1);
    if (req.method === 'POST') {
      ctx.setRecords([item.record].concat(ctx.records()));
      pdfs.restore(id, item.pdf);
      ctx.audit('CorrespondenceRestored', 'restored from the recycle bin', id);
      noteChange('record', id);
      ctx.send(res, 200, { restored: id });
    } else {
      pdfs.forget([id]);
      ctx.audit('CorrespondencePurged', 'permanently deleted from the recycle bin', id);
      ctx.send(res, 204);
    }
    return true;
  }

  // Letters
  if (pathname.indexOf('/api/portal/letters/') === 0) {
    var manage = ctx.can('letters.templates.manage');
    if (req.method === 'GET' && pathname === '/api/portal/letters/placeholders') { ctx.send(res, 200, PLACEHOLDERS); return true; }
    if (req.method === 'POST' && pathname === '/api/portal/letters/preview') {
      if (!manage && !ctx.requirePermission('correspondence.create')) return true;
      ctx.readBody(req).then(function (b) {
        var values = b.payload ? valuesFromPayload(b.payload) : manage ? SAMPLE : null;
        if (!values) return ctx.send(res, 400, { message: 'Send the payload to preview its letter.' });
        var t = b.template || (b.templateId && templates.filter(function (x) { return x.templateId === b.templateId; })[0]) ||
          templates.filter(function (x) { return x.status === 'Published' && x.documentType.toLowerCase() === String(values.DocumentType || '').toLowerCase(); })[0];
        if (!t) return ctx.send(res, 404, { message: 'No published letter template for document type "' + (values.DocumentType || '(none)') + '". New PDFs of this type use the data sheet.' });
        var out = renderLetter(t, values);
        res.writeHead(200, { 'Content-Type': 'application/pdf', 'Content-Length': out.bytes.length, 'Cache-Control': 'no-store',
          'X-Letter-Missing': out.missing.join(','), 'X-Letter-Template': (t.documentType || 'unsaved') + (t.version ? ' v' + t.version : ' (unsaved)'),
          'Access-Control-Expose-Headers': 'X-Letter-Missing, X-Letter-Template' });
        res.end(out.bytes);
      });
      return true;
    }
    if (!ctx.requirePermission('letters.templates.manage')) return true;
    if (req.method === 'GET' && pathname === '/api/portal/letters/templates') {
      ctx.send(res, 200, templates.slice().sort(function (a, b) { return a.documentType.localeCompare(b.documentType) || b.version - a.version; }));
      return true;
    }
    if (req.method === 'POST' && pathname === '/api/portal/letters/templates') {
      ctx.readBody(req).then(function (b) {
        if (!String(b.documentType || '').trim()) return ctx.send(res, 400, { message: 'Document type is required.' });
        if (!String(b.body || '').trim()) return ctx.send(res, 400, { message: 'The letter needs a body.' });
        b.documentType = b.documentType.trim();
        var t = addTemplate(b);
        t.createdBy = ctx.user.userName;
        ctx.audit('LetterTemplateDrafted', t.documentType + ' template ' + t.templateId);
        ctx.send(res, 200, t);
      });
      return true;
    }
    if ((m = pathname.match(/^\/api\/portal\/letters\/templates\/(\d+)(\/publish|\/retire)?$/))) {
      var tpl = templates.filter(function (x) { return x.templateId === Number(m[1]); })[0];
      if (!tpl) { ctx.send(res, 404, { message: 'Template not found.' }); return true; }
      if (req.method === 'PUT' && !m[2]) {
        ctx.readBody(req).then(function (b) {
          if (tpl.status !== 'Draft') return ctx.send(res, 409, { message: 'Only drafts can be edited. Create a new version to change a published template.' });
          if (!String(b.body || '').trim()) return ctx.send(res, 400, { message: 'The letter needs a body.' });
          ['name', 'subject', 'salutation', 'body', 'closing', 'signatureName', 'signatureTitle'].forEach(function (f) { if (b[f] !== undefined) tpl[f] = b[f]; });
          tpl.updatedUtc = new Date().toISOString();
          ctx.send(res, 200, tpl);
        });
        return true;
      }
      if (req.method === 'POST' && m[2] === '/publish') {
        var unknown = unknownPlaceholders(tpl);
        if (unknown.length) { ctx.send(res, 400, { message: 'Unknown placeholders: ' + unknown.map(function (u) { return '{{' + u + '}}'; }).join(', ') }); return true; }
        if (tpl.status !== 'Draft') { ctx.send(res, 409, { message: 'Only drafts can be published.' }); return true; }
        templates.forEach(function (x) { if (x.documentType === tpl.documentType && x.status === 'Published') x.status = 'Retired'; });
        tpl.status = 'Published'; tpl.publishedUtc = new Date().toISOString(); tpl.publishedBy = ctx.user.userName; tpl.updatedUtc = tpl.publishedUtc;
        ctx.audit('LetterTemplatePublished', tpl.documentType + ' v' + tpl.version);
        ctx.send(res, 200, tpl);
        return true;
      }
      if (req.method === 'POST' && m[2] === '/retire') {
        if (tpl.status === 'Retired') { ctx.send(res, 409, { message: 'That version is already retired.' }); return true; }
        tpl.status = 'Retired'; tpl.updatedUtc = new Date().toISOString();
        ctx.audit('LetterTemplateRetired', tpl.documentType + ' v' + tpl.version);
        ctx.send(res, 200, tpl);
        return true;
      }
    }
    return false;
  }

  // Alerts
  if (req.method === 'GET' && pathname === '/api/portal/alerts') {
    if (!ctx.requirePermission('alerts.read')) return true;
    var open = query.open === 'true';
    ctx.send(res, 200, alerts.filter(function (a) { return !open || !a.acknowledgedUtc; }).slice(0, Math.min(Number(query.top) || 50, 500)));
    return true;
  }
  if (req.method === 'GET' && pathname === '/api/portal/alerts/rules') {
    if (!ctx.requirePermission('alerts.read')) return true;
    ctx.send(res, 200, {
      enabled: true, intervalMinutes: 2, cooldownHours: 6,
      rules: [
        { key: 'FailedRate', enabled: true, description: 'Failed share of requests created in the last 24 h is at least 1.5× the usual rate (and 10% higher), or at least 25%; needs 20+ requests.' },
        { key: 'ProcessingBacklog', enabled: true, description: '25+ requests in progress for more than 48 h.' },
        { key: 'PdfFailures', enabled: true, description: '1+ PDFs failed every generation attempt.' },
        { key: 'SlaBreaches', enabled: true, description: '1+ open requests passed their SLA (Administration > SLA) since the previous check; critical from 25.' }
      ],
      channels: { teams: false, email: null }
    });
    return true;
  }
  if (req.method === 'POST' && (m = pathname.match(/^\/api\/portal\/alerts\/(\d+)\/acknowledge$/))) {
    if (!ctx.requirePermission('alerts.read')) return true;
    var alert = alerts.filter(function (a) { return a.alertId === Number(m[1]); })[0];
    if (!alert) { ctx.send(res, 404, { message: 'Alert not found.' }); return true; }
    if (!alert.acknowledgedUtc) { alert.acknowledgedUtc = new Date().toISOString(); alert.acknowledgedBy = ctx.user.userName; }
    ctx.audit('AlertAcknowledged', alert.title);
    ctx.send(res, 200, alert);
    return true;
  }

  // Change feed (the UI polls this when there's no WebSocket; the mock has no hub)
  if (req.method === 'GET' && pathname === '/api/portal/changes') {
    if (!ctx.requirePermission('correspondence.read')) return true;
    var since = query.since ? new Date(query.since).getTime() : Date.now();
    var now = Date.now();
    var recent = changes.filter(function (c) { return c.at > since && c.at <= now; });
    var ids = function (kind) { return recent.filter(function (c) { return c.kind === kind; }).map(function (c) { return c.id; }).filter(function (v, i, a) { return a.indexOf(v) === i; }); };
    ctx.send(res, 200, {
      asOfUtc: new Date(now).toISOString(),
      newRecords: ids('new').length,
      changedIds: ids('record'),
      pdfChanges: ids('pdf').map(function (id) { return { correspondenceId: id, status: pdfs.status(id).status }; }),
      newAlerts: ctx.can('alerts.read') ? alerts.filter(function (a) { return new Date(a.raisedUtc).getTime() > since; }) : []
    });
    return true;
  }
  return false;
}

module.exports = { routes: routes, evaluate: evaluate, evaluateSla: evaluateSla, noteChange: noteChange, raiseAlert: raiseAlert, slaFor: slaFor };
