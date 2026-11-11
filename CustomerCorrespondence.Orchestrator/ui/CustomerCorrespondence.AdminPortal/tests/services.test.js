/* Unit tests for the portal's pure services (validation, prompts, analytics, parsing). */
'use strict';

var assert = require('assert');
var h = require('./harness');

[
  'app/services/storage.service.js',
  'app/services/error-recovery.service.js',
  'app/services/payload-validation.service.js',
  'app/services/search-refinement.service.js',
  'app/services/search-query.service.js',
  'app/services/correspondence-analytics.service.js',
  'app/services/markdown.service.js',
  'app/services/ai-prompt.service.js',
  'app/services/excel-export.service.js',
  'app/services/speech.service.js',
  'app/components/charts/charts.js',
  'app/components/payload/payload.js'
].forEach(h.load);

var errorRecovery = h.get('errorRecovery');
var validation = h.get('payloadValidation');
var refinement = h.get('searchRefinement');
var searchQuery = h.get('searchQuery');
var analytics = h.get('correspondenceAnalytics');
var markdown = h.get('markdown');
var aiPrompt = h.get('aiPrompt');
var excel = h.get('excelExport');
var speech = h.get('speech');
var geometry = h.get('chartGeometry');
var payloadBuilder = h.get('correspondencePayload');

// ─────────────────────────────────────────────
h.suite('errorRecovery');

h.test('no response (status -1) is a retryable network error; timeout gets its own message', function () {
  var net = errorRecovery.classifyError({ status: -1, xhrStatus: 'error' });
  assert.strictEqual(net.category, 'network');
  assert.strictEqual(net.isRetryable, true);
  var timeout = errorRecovery.classifyError({ status: -1, xhrStatus: 'timeout' });
  assert.strictEqual(timeout.message, 'Request timeout — connection took too long');
});

h.test('4xx is non-retryable validation with server message and status-specific hint', function () {
  var e = errorRecovery.classifyError({ status: 404, data: { Message: 'No such claim' } });
  assert.strictEqual(e.category, 'validation');
  assert.strictEqual(e.isRetryable, false);
  assert.strictEqual(e.message, 'No such claim');
  assert.ok(/not found/.test(e.suggestion));
});

h.test('5xx is retryable server error; already-classified errors pass through', function () {
  var e = errorRecovery.classifyError({ status: 503, data: { message: 'down' } });
  assert.strictEqual(e.category, 'server');
  assert.strictEqual(e.isRetryable, true);
  assert.strictEqual(errorRecovery.classifyError(e), e);
});

h.test('messageOf prefers server Message, then error.message, then fallback', function () {
  assert.strictEqual(errorRecovery.messageOf({ data: { Message: 'A' }, message: 'B' }), 'A');
  assert.strictEqual(errorRecovery.messageOf({ message: 'B' }), 'B');
  assert.strictEqual(errorRecovery.messageOf({}, 'C'), 'C');
});

// ─────────────────────────────────────────────
h.suite('payloadValidation');

function payload(overrides) {
  var p = {
    Process: {
      RequestingSystemName: 'Claims System',
      ECCOTrackingClaimNumber: 'CL-12345',
      DeliveryMethod: 'Mail',
      DocumentDescription: 'Welcome Kit',
      MailDeliveryInfo: {
        RecipientFullName: 'John Doe',
        RecipientAddress: { RecipientAddressLine1: '123 Main St', RecipientAddressCity: 'Springfield', RecipientAddressST: 'IL', RecipientAddressZIP: '62701' }
      }
    }
  };
  (overrides || function () {})(p.Process);
  return p;
}

h.test('a complete Mail payload is valid (only enhancement suggestions)', function () {
  var r = validation.validatePayload(payload());
  assert.strictEqual(r.isValid, true);
  assert.strictEqual(r.errors.length, 0);
  assert.strictEqual(r.warnings.length, 0);
  assert.ok(r.suggestions.length >= 1);
});

h.test('missing recipient name and address are blocking errors', function () {
  var r = validation.validatePayload(payload(function (p) {
    p.MailDeliveryInfo.RecipientFullName = '';
    p.MailDeliveryInfo.RecipientAddress.RecipientAddressLine1 = '';
  }));
  assert.strictEqual(r.isValid, false);
  assert.deepStrictEqual(r.errors.map(function (e) { return e.fieldName; }).sort(), ['address', 'recipientName']);
});

h.test('the requesting system is required (the API needs it to pick the customer scope)', function () {
  var r = validation.validatePayload(payload(function (p) { p.RequestingSystemName = '  '; }));
  assert.strictEqual(r.isValid, false);
  assert.deepStrictEqual(r.errors.map(function (e) { return e.fieldName; }), ['requestingSystemName']);
});

h.test('unusual claim format and bad ZIP/state are warnings, not errors', function () {
  var r = validation.validatePayload(payload(function (p) {
    p.ECCOTrackingClaimNumber = '1234567890';
    p.MailDeliveryInfo.RecipientAddress.RecipientAddressZIP = 'ABCDE';
    p.MailDeliveryInfo.RecipientAddress.RecipientAddressST = 'Illinois';
  }));
  assert.strictEqual(r.isValid, true);
  assert.deepStrictEqual(r.warnings.map(function (w) { return w.fieldName; }).sort(), ['claimNumber', 'state', 'zipCode']);
});

h.test('email delivery requires a valid email and suggests domain typo fixes', function () {
  var r = validation.validatePayload(payload(function (p) {
    p.DeliveryMethod = 'Email';
    p.MailDeliveryInfo.RecipientEmailAddress = 'jane@gmial.com';
  }));
  assert.strictEqual(validation.validateEmail('jane@gmial.com'), null, 'syntactically valid');
  var bad = validation.validateEmail('jane@@gmial');
  assert.strictEqual(bad.level, 'error');
  assert.ok(r.isValid);
});

h.test('accepts the bare Process object and camelCase keys', function () {
  var r = validation.validatePayload({
    ECCOTrackingClaimNumber: 'CL-123',
    requestingSystemName: 'Claims System',
    deliveryMethod: 'Mail',
    documentDescription: 'Kit',
    mailDeliveryInfo: { recipientFullName: 'Ann Lee', recipientAddress: { recipientAddressLine1: '9 Elm Street', recipientAddressCity: 'X', recipientAddressST: 'CA', recipientAddressZIP: '90210' } }
  });
  assert.strictEqual(r.isValid, true);
});

// ─────────────────────────────────────────────
h.suite('searchRefinement');

h.test('detects common typos', function () {
  assert.deepStrictEqual(refinement.detectTypos('faild cliam'), { field: 'query', original: 'faild', corrected: 'failed' });
  assert.strictEqual(refinement.detectTypos('failed claim'), null);
});

h.test('suggestions replace free text with structured filters, highest confidence first', function () {
  var r = refinement.analyzeSearchQuery({ freeText: 'failed today', customer: 'PGA' }, 'failed today', new Date('2026-11-03T12:00:00Z'));
  assert.deepStrictEqual(r.suggestions.map(function (s) { return s.id; }), ['filter-failed', 'filter-today']);
  var failed = r.suggestions[0].query;
  assert.strictEqual(failed.freeText, undefined);
  assert.strictEqual(failed.status, 'Failed');
  assert.strictEqual(failed.customer, 'PGA', 'keeps other filters');
  assert.strictEqual(r.suggestions[1].query.fromDate, '2026-11-03');
});

h.test('"processed" means Completed, not Processing; "email" does not also suggest Mail', function () {
  var ids = refinement.suggestFilters({}, 'processed email').map(function (s) { return s.id; });
  assert.deepStrictEqual(ids, ['filter-completed', 'filter-email']);
});

// ─────────────────────────────────────────────
h.suite('searchQuery');

h.test('free text containing a claim number becomes a claim filter', function () {
  var body = searchQuery.buildSearchRequest({ page: 1, pageSize: 25, freeText: 'see cl-12345 please' });
  assert.strictEqual(body.claimNumber, 'CL-12345');
  assert.strictEqual(body.freeText, undefined);
  assert.strictEqual(body.includePayload, true);
  assert.strictEqual(body.sortDirection, undefined, 'no direction without a sort field');
});

h.test('AI params map onto backend fields and ignore stale search-box text', function () {
  var body = searchQuery.buildSearchRequest({
    page: 2, pageSize: 10, freeText: 'stale text', sortBy: 'created',
    aiParams: { statuses: ['Processing', 'Completed'], status: 'Failed', customer: 'PGA', delivery: 'Mail', fromDate: '2026-01-01' }
  });
  assert.strictEqual(body.freeText, undefined);
  assert.deepStrictEqual(body.latestStatuses, ['Processing', 'Completed']);
  assert.strictEqual(body.latestStatus, undefined, 'statuses takes precedence');
  assert.strictEqual(body.customerName, 'PGA');
  assert.strictEqual(body.deliveryMethodCode, 'Mail');
  assert.strictEqual(body.sortDirection, 'desc');
});

h.test('several search terms are sent as freeTerms (any of them)', function () {
  assert.deepStrictEqual(searchQuery.splitTerms('DOC-1, doc-1; DOC-2\nDOC-3'), ['DOC-1', 'DOC-2', 'DOC-3']);
  assert.deepStrictEqual(searchQuery.splitTerms('DOC-1 DOC-2'), ['DOC-1', 'DOC-2'], 'space-separated IDs');
  assert.deepStrictEqual(searchQuery.splitTerms('Claims System'), ['Claims System'], 'words stay a phrase');
  var body = searchQuery.buildSearchRequest({ page: 1, pageSize: 25, freeText: 'CL-1, DOC-2' });
  assert.deepStrictEqual(body.freeTerms, ['CL-1', 'DOC-2']);
  assert.strictEqual(body.freeText, undefined);
  assert.strictEqual(body.claimNumber, undefined);
  assert.deepStrictEqual(searchQuery.buildFilterRequest(null, 'a, b'), { freeTerms: ['a', 'b'] });
});

h.test('field:value filters in the search box become grid column filters', function () {
  var now = new Date(2026, 10, 4);
  var p = searchQuery.parseSearchBox('status:Failed,Processing -status:Completed customer:"Claims System" from:7d to:2026-11-03 sla:at-risk DOC-9', now);
  assert.strictEqual(p.text, 'DOC-9');
  assert.deepStrictEqual(p.terms, ['DOC-9']);
  assert.deepStrictEqual(p.errors, []);
  var by = {};
  p.filters.forEach(function (f) { by[f.field] = f.entry; });
  assert.strictEqual(by.status.logic, 'and');
  assert.deepStrictEqual(by.status.filters[0], { field: 'status', operator: 'neq', value: 'Completed', ignoreCase: true });
  assert.deepStrictEqual(by.status.filters[1].filters.map(function (d) { return d.value; }), ['Failed', 'Processing']);
  assert.strictEqual(by.customer.operator, 'contains');
  assert.strictEqual(by.customer.value, 'Claims System');
  assert.strictEqual(by.created.filters[0].operator, 'gte');
  assert.strictEqual(by.created.filters[0].value.getDate(), 28, '7 days before Nov 4');
  assert.strictEqual(by.created.filters[1].operator, 'lte');
  assert.strictEqual(by.sla.value, 'At risk');

  var bad = searchQuery.parseSearchBox('from:soon status: sla:late', now);
  assert.strictEqual(bad.errors.length, 3);
  assert.strictEqual(searchQuery.parseSearchBox('abc:1', now).text, 'abc:1', 'unknown keys stay text');
});

h.test('filter label and description render the active params', function () {
  var p = { pageSize: 5, status: 'Failed', sortBy: 'requestCreationTS', sortDirection: 'desc' };
  assert.strictEqual(searchQuery.filterLabel(p), 'Top 5 | Status = "Failed" | Sort: requestCreationTS desc');
  assert.strictEqual(searchQuery.describeParams({ statuses: ['A', 'B'] }), 'Status in [A, B]');
  assert.strictEqual(searchQuery.filterLabel({}), null);
  assert.strictEqual(searchQuery.isEmpty({ statuses: [] }), true);
});

// ─────────────────────────────────────────────
h.suite('correspondenceAnalytics');

var NOW = new Date('2026-11-03T12:00:00Z').getTime();
var DAY = 86400000;
function rec(status, customer, delivery, ageDays) {
  return { latestStatus: status, requestingDomainName: customer, deliveryMethodCode: delivery, requestCreationTS: new Date(NOW - ageDays * DAY).toISOString() };
}

h.test('toGridRow normalizes camelCase and PascalCase fields', function () {
  var row = analytics.toGridRow({ CorrespondenceId: 'X1', LatestStatus: 'Failed', RequestPayload: '{}' });
  assert.strictEqual(row.id, 'X1');
  assert.strictEqual(row.status, 'Failed');
  assert.strictEqual(row.payload, '{}');
});

h.test('aggregates tally status/customer/delivery, group MAIL with Mail and bucket Processing age', function () {
  var raw = [
    rec('Completed', 'A', 'Mail', 0.2), rec('Failed', 'A', 'MAIL', 2), rec('Processing', 'B', 'Email', 0.5),
    rec('Processing', 'A', 'Mail', 5), rec('Processing', 'A', 'Mail', 9)
  ];
  var agg = analytics.computeAggregates(raw.map(analytics.normalizeForAggregates), 5, {}, NOW);
  assert.deepStrictEqual(agg.statusCounts, { Completed: 1, Failed: 1, Processing: 3 });
  assert.deepStrictEqual(agg.deliveryCounts, { Mail: 4, Email: 1 });
  assert.deepStrictEqual(agg.deliveryStatusCounts.Mail, { Completed: 1, Failed: 1, Processing: 2 });
  assert.deepStrictEqual(agg.processingAgeBuckets.map(function (b) { return b.count; }), [1, 0, 1, 1]);
  assert.strictEqual(agg.recordsFetched, 5);
});

h.test('a dimension constrained by the filter counts exactly the total', function () {
  var agg = analytics.computeAggregates([analytics.normalizeForAggregates(rec('Failed', 'A', 'Mail', 1))], 812, { latestStatus: 'Failed' }, NOW);
  assert.deepStrictEqual(agg.statusCounts, { Failed: 812 });
});

h.test('insights flag SLA risk, high failure rate and skewed breakdowns', function () {
  var insights = analytics.buildInsights({
    total: 10,
    statusCounts: { Failed: 3, Completed: 7 },
    customerCounts: { A: 8, B: 2 },
    deliveryCounts: { Mail: 9, Email: 1 },
    processingAgeBuckets: [{ bucket: '>7 Days', count: 2 }]
  });
  assert.strictEqual(insights.length, 4);
  assert.ok(/2 records have been Processing for over 7 days/.test(insights[0]));
  assert.ok(/30%/.test(insights[1]));
});

h.test('snapshot comparison reports ±20% swings', function () {
  assert.deepStrictEqual(analytics.compareWithSnapshot(null, 10, 1), []);
  var lines = analytics.compareWithSnapshot({ total: 100, failedCount: 10 }, 150, 14);
  assert.strictEqual(lines.length, 2);
  assert.ok(/up 50%/.test(lines[0]));
  assert.ok(/up 40%/.test(lines[1]));
  assert.strictEqual(analytics.snapshotSignature({ page: 1, pageSize: 25 }), '');
});

h.test('background scan alerts only when well above a baseline of 2+ scans', function () {
  var items = [{ latestStatus: 'Failed' }, { latestStatus: 'Failed' }, { latestStatus: 'Completed' }, { latestStatus: 'Completed' }];
  assert.strictEqual(analytics.evaluateBackgroundScan(items, [{ failedRate: 0.1 }]).alert, null, 'not enough history');
  var r = analytics.evaluateBackgroundScan(items, [{ failedRate: 0.1 }, { failedRate: 0.2 }]);
  assert.strictEqual(r.failedRate, 0.5);
  assert.ok(/50%.*15% baseline/.test(r.alert));
});

h.test('zero-result status hint suggests the closest real status', function () {
  var known = ['Completed', 'Processing', 'AwaitingImaging'];
  assert.strictEqual(analytics.closestStatusMatch('Complted', known), 'Completed');
  assert.strictEqual(analytics.closestStatusMatch('completed', known), null, 'case-only match is not the problem');
  assert.ok(/did you mean "AwaitingImaging"/.test(analytics.zeroResultStatusHint({ status: 'AwaitingImage' }, { total: 0 }, known)));
  assert.strictEqual(analytics.zeroResultStatusHint({ status: 'Complted' }, { total: 3 }, known), null);
});

// ─────────────────────────────────────────────
h.suite('markdown');

h.test('splits text and tables, strips markdown punctuation', function () {
  var segs = markdown.parseSegments('## Summary\n**Bold** line\n\n| Status | Count |\n|---|---|\n| Failed | 3 |\nAfter');
  assert.deepStrictEqual(segs.map(function (s) { return s.kind; }), ['text', 'table', 'text']);
  assert.strictEqual(segs[0].content, 'Summary\nBold line');
  assert.deepStrictEqual(segs[1].rows, [['Failed', '3']]);
});

h.test('headline skips headings; short answers need no drill-down', function () {
  var a = markdown.analyze('## Explanation\nThe record failed validation.\n- one\n- two');
  assert.strictEqual(a.headline, 'The record failed validation.');
  assert.strictEqual(a.needsDrillDown, true);
  assert.strictEqual(markdown.analyze('Done.').needsDrillDown, false);
  assert.strictEqual(markdown.analyze('| A | B |\n|---|---|\n| 1 | 2 |').headline, 'Table with 1 row');
});

h.test('speech text reads tables as rows and bullets as a list', function () {
  var spoken = markdown.toSpeechText('**Counts**\n| Customer | Count |\n|---|---|\n| PGA | 15 |\n| Life | 3 |\n- 10% failed\n- rest ok');
  assert.strictEqual(spoken, 'Counts. PGA: 15. Life: 3. 10 percent failed, and rest ok');
});

// ─────────────────────────────────────────────
h.suite('aiPrompt');

var NOW_DATE = new Date('2026-11-03T12:00:00.000Z');
var ctx = {
  total: 40, page: 1, pageSize: 25,
  items: [{ id: '1', docId: 'D1', status: 'Failed' }],
  pageStatusCounts: { Failed: 1 },
  allAggregates: { total: 40, statusCounts: { Failed: 40 }, customerCounts: {}, deliveryCounts: {} },
  allDataLoaded: true
};

h.test('system prompt is grounded in dates, data, filters, statuses and the open draft', function () {
  var s = aiPrompt.buildSystemPrompt('how many failed?', ctx, {
    activeParams: { status: 'Failed', toDate: undefined },
    statusValues: ['Failed', 'Completed'],
    activeDraft: { claimNumber: 'CL-1' }
  }, NOW_DATE);
  assert.ok(s.indexOf('Today\'s date is 2026-11-03') !== -1);
  assert.ok(s.indexOf('=== FULL DATASET — ALL 40 RECORDS (pre-computed) ===') !== -1);
  assert.ok(s.indexOf('=== CURRENTLY ACTIVE GRID FILTERS ===\n  - status: Failed\n') !== -1);
  assert.ok(s.indexOf('Failed, Completed') !== -1);
  assert.ok(s.indexOf('=== CURRENTLY OPEN DRAFT PAYLOAD') !== -1);
  assert.ok(s.indexOf('"docId":"D1"') !== -1);
});

h.test('no loaded data uses the compact prompt; narrative prompts sample 15 records', function () {
  assert.ok(/^You are an AI assistant for a correspondence management system\. The current date/.test(aiPrompt.buildSystemPrompt('hi', null, {}, NOW_DATE)));
  var many = Object.assign({}, ctx, { items: Array.from({ length: 20 }, function (_, i) { return { id: String(i) }; }) });
  assert.ok(aiPrompt.buildSystemPrompt('summarize claim CL-1', many, {}, NOW_DATE).indexOf('Sample records (first 15 of 20') !== -1);
  assert.ok(aiPrompt.buildSystemPrompt('how many', many, {}, NOW_DATE).indexOf('Sample records (first 5 of 20') !== -1);
});

h.test('breakdowns are capped at 12 rows with an overflow summary', function () {
  var counts = {};
  for (var i = 0; i < 15; i++) counts['c' + i] = 100 - i;
  var text = aiPrompt.countsToText(counts);
  assert.strictEqual(text.split('\n').length, 13);
  assert.ok(/\(\+3 more, 261 records total\)$/.test(text));
});

h.test('request body carries model, tools and prior turns', function () {
  var body = aiPrompt.buildRequestBody('next', null, { history: [{ role: 'user', content: 'a' }, { role: 'assistant', content: 'b' }] },
    { aiModel: 'm', aiMaxTokens: 4096 }, NOW_DATE);
  assert.strictEqual(body.model, 'm');
  assert.strictEqual(body.tools.length, 7);
  assert.deepStrictEqual(body.messages.map(function (m) { return m.content; }), ['a', 'b', 'next']);
});

function toolUse() {
  var blocks = Array.prototype.slice.call(arguments);
  return { stop_reason: 'tool_use', content: blocks };
}

h.test('parses single tool calls with sensible default text', function () {
  var r = aiPrompt.parseResponse(toolUse({ type: 'tool_use', name: 'search_grid', input: { status: 'Failed' } }));
  assert.deepStrictEqual(r, { type: 'search', params: { status: 'Failed' }, text: 'Searching for status "Failed"…' });
  r = aiPrompt.parseResponse(toolUse({ type: 'text', text: 'Sure.' }, { type: 'tool_use', name: 'export_results', input: {} }));
  assert.deepStrictEqual(r, { type: 'export', params: { scope: 'page' }, text: 'Sure.' });
  r = aiPrompt.parseResponse(toolUse({ type: 'tool_use', name: 'show_chart', input: { metric: 'deliveryByStatus', chartType: 'pie' } }));
  assert.strictEqual(r.text, 'Delivery method by status, as one pie per delivery method…');
});

h.test('several tool calls become an ordered multi action', function () {
  var r = aiPrompt.parseResponse(toolUse(
    { type: 'tool_use', name: 'search_grid', input: { status: 'Failed' } },
    { type: 'tool_use', name: 'export_results', input: { scope: 'all' } },
    { type: 'tool_use', name: 'unknown_tool', input: {} }
  ));
  assert.strictEqual(r.type, 'multi');
  assert.deepStrictEqual(r.actions.map(function (a) { return a.type; }), ['search', 'export']);
});

h.test('max_tokens and plain text responses', function () {
  assert.ok(/more steps than I could finish/.test(aiPrompt.parseResponse({ stop_reason: 'max_tokens', content: [{ type: 'text', text: 'First, let me' }] }).text));
  assert.deepStrictEqual(aiPrompt.parseResponse({ stop_reason: 'end_turn', content: [{ type: 'text', text: 'Hi' }] }), { type: 'text', text: 'Hi' });
  assert.strictEqual(aiPrompt.parseResponse({}).text, 'No response received.');
});

// ─────────────────────────────────────────────
h.suite('excelExport');

h.test('rows follow the requested column subset/order; workbook escapes XML and colours by status', function () {
  var record = { correspondenceId: '1', claimNumber: 'CL-1', LatestStatus: 'Failed', payload: '<x & "y">' };
  assert.deepStrictEqual(Object.keys(excel.toRow(record, ['status', 'claimNumber'])), ['ClaimNumber', 'Status']);
  var xml = excel.buildWorkbook([excel.toRow(record)]);
  assert.ok(xml.indexOf('&lt;x &amp; &quot;y&quot;&gt;') !== -1);
  assert.ok(xml.indexOf('ss:StyleID="Status_failed"') !== -1);
  assert.ok(xml.indexOf('R1C1:R1C11') !== -1) // 11 columns: 8 record fields + AgeHours, SLA, SLADue;
});

// ─────────────────────────────────────────────
h.suite('speech');

h.test('prefers a natural voice of the requested gender, never counting "female" as male', function () {
  var voices = [
    { name: 'Microsoft Zira', lang: 'en-US' },
    { name: 'Google UK English Female', lang: 'en-GB' },
    { name: 'Microsoft Guy Online (Natural)', lang: 'en-US' },
    { name: 'Microsoft Aria Online (Natural)', lang: 'en-US' }
  ];
  assert.strictEqual(speech.pickVoice(voices, 'male').name, 'Microsoft Guy Online (Natural)');
  assert.strictEqual(speech.pickVoice(voices, 'female').name, 'Microsoft Aria Online (Natural)');
  assert.strictEqual(speech.pickVoice([voices[1]], 'male').name, 'Google UK English Female', 'falls back to any voice');
  assert.strictEqual(speech.pickVoice([], 'male'), null);
});

// ─────────────────────────────────────────────
h.suite('charts & payload builder');

h.test('wedges cover the full circle in proportion, and a single slice still renders', function () {
  var w = geometry.wedges([{ label: 'a', count: 1 }, { label: 'b', count: 3 }], 100, 0);
  assert.strictEqual(w[0].start, -90);
  assert.strictEqual(w[0].end, 0);
  assert.strictEqual(w[1].end, 270);
  var single = geometry.wedges([{ label: 'only', count: 5 }], 100, 0.6)[0];
  assert.ok(/ A /.test(single.d) && single.d.indexOf('NaN') === -1);
});

h.test('AI draft payload mirrors extracted fields and reports validation issues', function () {
  var built = payloadBuilder.build({ claimNumber: 'CL-5432', recipientFullName: 'John Doe', documentDescription: 'Welcome Kit' }, ['⚠️ dup']);
  assert.strictEqual(built.payload.Process.DeliveryMethod, 'Mail');
  assert.strictEqual(built.payload.Process.MailDeliveryInfo.RecipientAddress.RecipientCountry, 'US');
  assert.ok(built.contextWithValidation.indexOf('⚠️ dup\n📋 Document: Welcome Kit') === 0);
  assert.ok(/❌ Street address is required/.test(built.contextWithValidation));
  assert.strictEqual(built.validation.isValid, false);
});
