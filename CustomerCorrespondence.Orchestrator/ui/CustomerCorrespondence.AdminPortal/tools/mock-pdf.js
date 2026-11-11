/*
 * Mock of the API's correspondence PDFs (ProcessRequestEndPoint/Pdf) for tools/mock-api.js:
 * per-record PDF state, a tiny one-page PDF per record (standard Helvetica, like the real renderer),
 * and the /api/portal/correspondence/pdf/status, GET/POST /{id}/pdf routes.
 */
'use strict';

var state = {}; // correspondenceId -> { status, generatedUtc, sizeBytes, attempts, lastError }

function escapePdf(text) {
  return String(text).replace(/[^\x20-\x7e]/g, '?').replace(/([()\\])/g, '\\$1');
}

/** Minimal valid PDF: one Letter page of text lines (no compression, standard font). */
function buildPdf(title, lines) {
  var content = 'BT /F1 18 Tf 54 730 Td (' + escapePdf(title) + ') Tj ET\n';
  lines.forEach(function (line, i) {
    content += 'BT /F1 10 Tf 54 ' + (700 - i * 16) + ' Td (' + escapePdf(line) + ') Tj ET\n';
  });
  var objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>',
    '<< /Length ' + Buffer.byteLength(content, 'latin1') + ' >>\nstream\n' + content + 'endstream'
  ];
  var out = '%PDF-1.4\n';
  var offsets = [];
  objects.forEach(function (body, i) {
    offsets.push(Buffer.byteLength(out, 'latin1'));
    out += (i + 1) + ' 0 obj\n' + body + '\nendobj\n';
  });
  var xref = Buffer.byteLength(out, 'latin1');
  out += 'xref\n0 ' + (objects.length + 1) + '\n0000000000 65535 f \n' +
    offsets.map(function (o) { return String(o).padStart(10, '0') + ' 00000 n \n'; }).join('') +
    'trailer\n<< /Size ' + (objects.length + 1) + ' /Root 1 0 R >>\nstartxref\n' + xref + '\n%%EOF\n';
  return Buffer.from(out, 'latin1');
}

function render(record) {
  var payload = {};
  try { payload = JSON.parse(record.requestPayload || '{}'); } catch (e) { /* keep empty */ }
  var p = payload.Process || payload;
  var mail = p.MailDeliveryInfo || {};
  return buildPdf('Correspondence Request', [
    'Correspondence Id: ' + record.correspondenceId,
    'Doc Id: ' + record.docId,
    'Claim number: ' + record.claimNumber,
    'Requesting system: ' + record.requestingDomainName,
    'Delivery method: ' + record.deliveryMethodCode,
    'Document: ' + (p.DocumentDescription || '-'),
    'Recipient: ' + (mail.RecipientFullName || '-'),
    'Status when generated: ' + record.latestStatus,
    '',
    '(Mock PDF: the real API renders the full layout and every payload field.)'
  ]);
}

/** Pretend the background worker already handled the newest records. */
function seed(records) {
  records.slice().sort(function (a, b) { return b.requestCreationTS.localeCompare(a.requestCreationTS); }).slice(0, 30)
    .forEach(function (r, i) {
      state[r.correspondenceId] = i === 3
        ? { status: 'Failed', attempts: 2, lastError: "The payload couldn't be decrypted with this environment's key." }
        : { status: 'Generated', generatedUtc: new Date().toISOString(), sizeBytes: render(r).length, attempts: 1 };
    });
}

function forget(ids) {
  ids.forEach(function (id) { delete state[id]; });
}

/**
 * Routes; returns true when handled. ctx: { user, records, readBody, send, requirePermission, check, inScope, audit }.
 */
function routes(req, res, pathname, ctx) {
  var m;
  if (req.method === 'POST' && pathname === '/api/portal/correspondence/pdf/status') {
    if (!ctx.requirePermission('correspondence.read')) return true;
    var canGenerate = ctx.can('correspondence.pdf.generate');
    ctx.readBody(req).then(function (b) {
      ctx.send(res, 200, {
        storageConfigured: true,
        items: (b.correspondenceIds || []).slice(0, 500).map(function (id) {
          var s = state[id] || { status: 'None', attempts: 0 };
          return { correspondenceId: id, status: s.status, generatedUtc: s.generatedUtc || null, sizeBytes: s.sizeBytes || null,
            attempts: s.attempts || 0, lastError: canGenerate ? (s.lastError || null) : null };
        })
      });
    });
    return true;
  }

  if (!(m = pathname.match(/^\/api\/portal\/correspondence\/([^/]+)\/pdf$/))) return false;
  var id = decodeURIComponent(m[1]);
  var record = ctx.records().filter(function (r) { return r.correspondenceId === id; })[0];
  var permission = req.method === 'GET' ? 'correspondence.payload.read' : 'correspondence.pdf.generate';
  if (!ctx.requirePermission(permission)) return true;
  if (!record || !ctx.inScope(record)) { ctx.send(res, 404, { message: "That correspondence record doesn't exist." }); return true; }
  var decision = ctx.check(permission, { customer: record.requestingDomainName, deliveryMethod: record.deliveryMethodCode, status: record.latestStatus });
  if (!decision.allowed) { ctx.send(res, 403, { message: decision.message, policy: decision.policy }); return true; }

  if (req.method === 'GET') {
    var s = state[id];
    if (!s || s.status !== 'Generated') {
      ctx.send(res, 404, { message: s ? 'The PDF is ' + s.status.toLowerCase() + '.' : 'No PDF has been generated for this record yet.' });
      return true;
    }
    var bytes = render(record);
    ctx.audit('PdfDownloaded', 'record ' + id);
    res.writeHead(200, {
      'Content-Type': 'application/pdf', 'Content-Length': bytes.length, 'Cache-Control': 'no-store',
      'Content-Disposition': 'attachment; filename="correspondence-' + id + '.pdf"'
    });
    res.end(bytes);
    return true;
  }
  if (req.method === 'POST') {
    var pdf = render(record);
    state[id] = { status: 'Generated', generatedUtc: new Date().toISOString(), sizeBytes: pdf.length, attempts: ((state[id] || {}).attempts || 0) + 1 };
    ctx.audit('PdfGenerated', 'record ' + id);
    ctx.send(res, 200, { correspondenceId: id, status: 'Generated', generatedUtc: state[id].generatedUtc, sizeBytes: pdf.length });
    return true;
  }
  return false;
}

function status(id) {
  var s = state[id] || { status: 'None', attempts: 0 };
  return { status: s.status, generatedUtc: s.generatedUtc || null, sizeBytes: s.sizeBytes || null, attempts: s.attempts || 0, lastError: s.lastError || null };
}

function restore(id, saved) {
  if (saved && saved.status !== 'None') state[id] = Object.assign({}, saved);
}

module.exports = { routes: routes, seed: seed, forget: forget, buildPdf: buildPdf, status: status, restore: restore };
