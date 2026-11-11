(function () {
  'use strict';

  /**
   * Translates between the AI's SearchParams shape and the backend search
   * request, and renders params as human-readable labels.
   *
   * SearchParams: { freeText, correspondenceId, docId, claimNumber, policyNumber,
   *   customer, customerId, status, statuses[], delivery, fromDate, toDate,
   *   pageSize, sortBy, sortDirection }
   */
  angular.module('adminPortal').factory('searchQuery', searchQuery);

  function hasValue(v) {
    return v !== undefined && v !== null && v !== '';
  }

  /** Extracts a claim number (e.g. "CL-12345") embedded in free-text search input. */
  function detectClaim(text) {
    var match = (text || '').match(/\b[A-Z]{2,3}-[0-9]{3,6}\b/i);
    return match ? match[0].toUpperCase() : undefined;
  }

  var MAX_TERMS = 100; // the API's limit (CorrespondenceQueryBuilder.MaxFreeTerms)

  /**
   * The search box's plain text as separate terms, matched as "any of them". Terms are separated by commas,
   * semicolons, tabs or new lines; IDs separated only by spaces (every word has a digit) are split too, while
   * other words stay together as a phrase ("Claims System"). Quotes keep a phrase whole. Repeats are dropped.
   */
  function splitTerms(text) {
    var out = [];
    var seen = {};
    function add(t) {
      t = t.trim().replace(/^"(.*)"$/, '$1').replace(/^'(.*)'$/, '$1').trim();
      if (t && !seen[t.toLowerCase()]) { seen[t.toLowerCase()] = true; out.push(t); }
    }
    String(text || '').split(/[,;\t\r\n]+/).forEach(function (segment) {
      var words = segment.trim().split(/\s+/).filter(Boolean);
      var allIds = words.length > 1 && /^[^"']/.test(segment.trim()) && words.every(function (w) { return /\d/.test(w); });
      if (allIds) words.forEach(add);
      else add(segment);
    });
    return out;
  }

  // field:value in the search box -> a grid column filter. kind: text (contains), pick (equals one of), date.
  var FIELD_KEYS = {
    status: { field: 'status', kind: 'pick' }, st: { field: 'status', kind: 'pick' },
    customer: { field: 'customer', kind: 'text' }, cust: { field: 'customer', kind: 'text' },
    delivery: { field: 'delivery', kind: 'text' }, channel: { field: 'delivery', kind: 'text' },
    doc: { field: 'docId', kind: 'text' }, docid: { field: 'docId', kind: 'text' },
    claim: { field: 'claimNumber', kind: 'text' }, claimnumber: { field: 'claimNumber', kind: 'text' },
    id: { field: 'id', kind: 'text' }, correspondenceid: { field: 'id', kind: 'text' },
    sla: { field: 'sla', kind: 'sla' },
    from: { field: 'created', kind: 'date', operator: 'gte' }, since: { field: 'created', kind: 'date', operator: 'gte' },
    after: { field: 'created', kind: 'date', operator: 'gt' },
    to: { field: 'created', kind: 'date', operator: 'lte' }, until: { field: 'created', kind: 'date', operator: 'lte' },
    before: { field: 'created', kind: 'date', operator: 'lt' },
    created: { field: 'created', kind: 'date', operator: 'eq' }, on: { field: 'created', kind: 'date', operator: 'eq' }
  };
  var SLA_GROUPS = { breached: 'Breached', atrisk: 'At risk', risk: 'At risk', ontrack: 'On track', finished: 'Finished', done: 'Finished' };
  var FIELD_TOKEN = /(^|[\s,;])(-?)([a-z]+)[:=]("[^"]*"|'[^']*'|[^\s"']*)/gi;

  /** "2026-11-01", "11/01/2026", "today", "yesterday" or "7d" (7 days ago) as a local Date, or null. */
  function parseDate(text, now) {
    var t = String(text || '').trim().toLowerCase();
    var today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    var m;
    if (t === 'today') return today;
    if (t === 'yesterday') return new Date(today.getFullYear(), today.getMonth(), today.getDate() - 1);
    if ((m = /^(\d{1,3})d$/.exec(t))) return new Date(today.getFullYear(), today.getMonth(), today.getDate() - +m[1]);
    if ((m = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(t))) return validDate(+m[1], +m[2], +m[3]);
    if ((m = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(t))) return validDate(+m[3], +m[1], +m[2]);
    return null;
  }
  function validDate(y, mo, d) {
    var date = new Date(y, mo - 1, d);
    return date.getFullYear() === y && date.getMonth() === mo - 1 && date.getDate() === d ? date : null;
  }

  /**
   * Reads the search box: field:value filters (status:Failed, customer:"Claims System", from:2026-11-01, sla:breached,
   * -status:Completed to exclude; several values with commas: status:Failed,Processing) and the remaining plain terms.
   * Returns { text: the box without the filters, terms: splitTerms(text), filters: [{ field, entry, token }], errors: [] }.
   * Each filter's entry is a grid filter for that column; dates on one column (from: + to:) are combined.
   * An unknown key ("abc:1") is left as text.
   */
  function parseSearchBox(text, now) {
    now = now || new Date();
    var byField = {};
    var order = [];
    var errors = [];
    var rest = String(text || '').replace(FIELD_TOKEN, function (whole, lead, minus, key, raw) {
      var spec = FIELD_KEYS[key.toLowerCase()];
      if (!spec) return whole;
      var token = whole.slice(lead.length);
      var value = raw.replace(/^"(.*)"$/, '$1').replace(/^'(.*)'$/, '$1').trim();
      if (!value) { errors.push('"' + token + '" needs a value, e.g. ' + key + ':' + example(spec) + '.'); return lead; }
      var conditions = [];
      if (spec.kind === 'date') {
        var date = parseDate(value, now);
        if (!date) { errors.push('"' + value + '" isn\'t a date. Use 2026-11-01, 11/01/2026, today, yesterday or 7d.'); return lead; }
        conditions.push({ field: spec.field, operator: minus ? negate(spec.operator) : spec.operator, value: date });
      } else {
        var values = value.split(',').map(function (v) { return v.trim(); }).filter(Boolean);
        for (var i = 0; i < values.length; i++) {
          var v = values[i];
          if (spec.kind === 'sla') {
            v = SLA_GROUPS[v.toLowerCase().replace(/[\s_-]/g, '')];
            if (!v) { errors.push('"' + values[i] + '" isn\'t an SLA group. Use breached, at-risk, on-track or finished.'); return lead; }
          }
          var exact = spec.kind !== 'text';
          conditions.push({ field: spec.field, operator: exact ? (minus ? 'neq' : 'eq') : (minus ? 'doesnotcontain' : 'contains'), value: v, ignoreCase: true });
        }
      }
      if (!byField[spec.field]) { byField[spec.field] = { all: [], any: [], tokens: [] }; order.push(spec.field); }
      var slot = byField[spec.field];
      slot.tokens.push(token);
      // Several values of one filter: any of them; excluded values and date bounds: all of them.
      if (minus || spec.kind === 'date') slot.all = slot.all.concat(conditions);
      else slot.any = slot.any.concat(conditions);
      return lead;
    });

    var filters = order.map(function (field) {
      var slot = byField[field];
      var parts = slot.all.slice();
      if (slot.any.length === 1) parts.push(slot.any[0]);
      else if (slot.any.length > 1) parts.push({ logic: 'or', filters: slot.any });
      var entry = parts.length === 1 ? parts[0] : { logic: 'and', filters: parts };
      return { field: field, entry: entry, token: slot.tokens.join(' ') };
    });
    rest = rest.replace(/\s{2,}/g, ' ').replace(/^[\s,;]+|[\s,;]+$/g, '');
    var terms = splitTerms(rest);
    if (terms.length > MAX_TERMS) errors.push('Search for at most ' + MAX_TERMS + ' terms at a time (' + terms.length + ' entered).');
    return { text: rest, terms: terms, filters: filters, errors: errors };
  }
  function negate(op) { return { gte: 'lt', gt: 'lte', lte: 'gt', lt: 'gte', eq: 'neq' }[op] || op; }
  function example(spec) { return { pick: 'Failed', text: 'acme', sla: 'breached', date: '2026-11-01' }[spec.kind]; }

  /** Puts the search box's terms on a request: one stays freeText (or claimNumber); several become freeTerms. */
  function applyTerms(body, text, claim) {
    var terms = splitTerms(text);
    if (terms.length > 1) body.freeTerms = terms;
    else if (claim) body.claimNumber = claim;
    else if (terms.length === 1) body.freeText = terms[0];
    return body;
  }

  /** Copies the AI filter fields onto a backend request body. */
  function applyFilterFields(body, ai) {
    if (!ai) return body;
    if (ai.policyNumber) body.policyNumber = ai.policyNumber;
    if (ai.correspondenceId) body.correspondenceId = ai.correspondenceId;
    if (ai.docId) body.docId = ai.docId;
    if (ai.customer) body.customerName = ai.customer;
    if (ai.customerId) body.customerId = ai.customerId;
    if (ai.statuses && ai.statuses.length > 0) body.latestStatuses = ai.statuses;
    else if (ai.status) body.latestStatus = ai.status;
    if (ai.delivery) body.deliveryMethodCode = ai.delivery;
    if (ai.fromDate) body.fromDate = ai.fromDate;
    if (ai.toDate) body.toDate = ai.toDate;
    return body;
  }

  /**
   * Builds the POST /correspondence/search body.
   * When AI params drive the search, only AI-provided fields are used — never stale UI text.
   * opts: { page, pageSize, aiParams, freeText, sortBy, sortDirection }
   */
  function buildSearchRequest(opts) {
    var ai = opts.aiParams;
    var searchText = ai ? (ai.freeText || '') : (opts.freeText || '');
    var claim = ai && hasValue(ai.claimNumber) ? ai.claimNumber : detectClaim(searchText);
    var body = {
      page: opts.page,
      pageSize: opts.pageSize,
      sortBy: opts.sortBy || undefined,
      sortDirection: opts.sortBy ? (opts.sortDirection || 'desc') : undefined,
      includePayload: true
    };
    if (ai && hasValue(ai.claimNumber)) body.claimNumber = ai.claimNumber;
    applyTerms(body, body.claimNumber ? '' : searchText, claim);
    return applyFilterFields(body, ai);
  }

  /** Filter-only request body (no paging/sort) for "export all" over the active search. */
  function buildFilterRequest(aiParams, freeText) {
    var text = aiParams ? (aiParams.freeText || '') : (freeText || '');
    var body = {};
    if (aiParams && aiParams.claimNumber) body.claimNumber = aiParams.claimNumber;
    applyTerms(body, body.claimNumber ? '' : text, detectClaim(text));
    return applyFilterFields(body, aiParams);
  }

  /** "Status = "Failed", Claim = "CL-1"…" — used by the "Understood as" explainer and saved views. */
  function describeParams(params) {
    if (!params) return 'all records';
    var parts = [];
    if (params.statuses && params.statuses.length > 0) parts.push('Status in [' + params.statuses.join(', ') + ']');
    else if (params.status) parts.push('Status = "' + params.status + '"');
    if (params.claimNumber) parts.push('Claim = "' + params.claimNumber + '"');
    if (params.policyNumber) parts.push('Policy = "' + params.policyNumber + '"');
    if (params.correspondenceId) parts.push('CorrespondenceId = "' + params.correspondenceId + '"');
    if (params.docId) parts.push('DocId = "' + params.docId + '"');
    if (params.customer) parts.push('Customer = "' + params.customer + '"');
    if (params.customerId) parts.push('CustomerId = "' + params.customerId + '"');
    if (params.delivery) parts.push('Delivery = "' + params.delivery + '"');
    if (params.fromDate) parts.push('From ' + params.fromDate);
    if (params.toDate) parts.push('To ' + params.toDate);
    if (params.freeText) parts.push('"' + params.freeText + '"');
    if (params.pageSize) parts.push('Top ' + params.pageSize);
    if (params.sortBy) parts.push('Sort: ' + params.sortBy + ' ' + (params.sortDirection || 'desc'));
    return parts.length ? parts.join(', ') : 'all records';
  }

  /** The "🤖 AI Filter: …" badge above the grid. Null when no filter applies. */
  function filterLabel(params) {
    if (!params) return null;
    var parts = [];
    if (params.pageSize) parts.push('Top ' + params.pageSize);
    if (params.statuses && params.statuses.length > 0) parts.push('Status in [' + params.statuses.join(', ') + ']');
    else if (params.status) parts.push('Status = "' + params.status + '"');
    if (params.claimNumber) parts.push('Claim = "' + params.claimNumber + '"');
    if (params.policyNumber) parts.push('Policy = "' + params.policyNumber + '"');
    if (params.correspondenceId) parts.push('CorrespondenceId = "' + params.correspondenceId + '"');
    if (params.docId) parts.push('DocId = "' + params.docId + '"');
    if (params.customer) parts.push('Customer = "' + params.customer + '"');
    if (params.customerId) parts.push('CustomerId = "' + params.customerId + '"');
    if (params.delivery) parts.push('Delivery = "' + params.delivery + '"');
    if (params.fromDate) parts.push('From ' + params.fromDate);
    if (params.toDate) parts.push('To ' + params.toDate);
    if (params.freeText) parts.push('"' + params.freeText + '"');
    if (params.sortBy) parts.push('Sort: ' + params.sortBy + ' ' + (params.sortDirection || 'desc'));
    return parts.length ? parts.join(' | ') : null;
  }

  /**
   * The AI filter as removable chips: [{ keys: [param names], label }]. Removing a chip drops its
   * keys from the params. Related params share a chip (date range, top-N with its sort).
   */
  function paramChips(params) {
    if (!params) return [];
    var chips = [];
    function add(keys, label) { chips.push({ keys: keys, label: label }); }
    if (params.pageSize) add(['pageSize', 'sortBy', 'sortDirection'], 'Top ' + params.pageSize + (params.sortBy ? ' by ' + params.sortBy + ' ' + (params.sortDirection || 'desc') : ''));
    else if (params.sortBy) add(['sortBy', 'sortDirection'], 'Sort: ' + params.sortBy + ' ' + (params.sortDirection || 'desc'));
    if (params.statuses && params.statuses.length > 0) add(['statuses', 'status'], 'Status: ' + params.statuses.join(', '));
    else if (params.status) add(['status'], 'Status: ' + params.status);
    if (params.claimNumber) add(['claimNumber'], 'Claim: ' + params.claimNumber);
    if (params.policyNumber) add(['policyNumber'], 'Policy: ' + params.policyNumber);
    if (params.correspondenceId) add(['correspondenceId'], 'Correspondence Id: ' + params.correspondenceId);
    if (params.docId) add(['docId'], 'Doc Id: ' + params.docId);
    if (params.customer) add(['customer'], 'Customer: ' + params.customer);
    if (params.customerId) add(['customerId'], 'Customer Id: ' + params.customerId);
    if (params.delivery) add(['delivery'], 'Delivery: ' + params.delivery);
    if (params.fromDate && params.toDate) add(['fromDate', 'toDate'], 'Created ' + params.fromDate + ' to ' + params.toDate);
    else if (params.fromDate) add(['fromDate'], 'Created from ' + params.fromDate);
    else if (params.toDate) add(['toDate'], 'Created until ' + params.toDate);
    if (params.freeText) add(['freeText'], 'Text: "' + params.freeText + '"');
    return chips;
  }

  /** Only text-searchable values belong in the search box. */
  function searchBoxText(params) {
    return params.claimNumber || params.policyNumber || params.correspondenceId || params.docId ||
      params.freeText || params.customer || '';
  }

  function isEmpty(params) {
    if (!params) return true;
    return !Object.keys(params).some(function (k) {
      var v = params[k];
      return Array.isArray(v) ? v.length > 0 : hasValue(v);
    });
  }

  function searchQuery() {
    return {
      detectClaim: detectClaim,
      buildSearchRequest: buildSearchRequest,
      buildFilterRequest: buildFilterRequest,
      splitTerms: splitTerms,
      parseSearchBox: parseSearchBox,
      MAX_TERMS: MAX_TERMS,
      describeParams: describeParams,
      filterLabel: filterLabel,
      paramChips: paramChips,
      searchBoxText: searchBoxText,
      isEmpty: isEmpty
    };
  }
})();
