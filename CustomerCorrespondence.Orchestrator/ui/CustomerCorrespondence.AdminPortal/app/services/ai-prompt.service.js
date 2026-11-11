(function () {
  'use strict';

  /**
   * Builds the AI request (tools + system prompt grounded in the loaded data)
   * and turns the model's response into app actions. Pure — the HTTP call lives
   * in aiService.
   *
   * Result shapes returned by parseResponse:
   *   { type: 'text', text }
   *   { type: 'search' | 'send_correspondence' | 'update_correspondence' | 'export' | 'chart', params, text }
   *   { type: 'clear_filters' | 'undo', text }
   *   { type: 'multi', actions: [{ type, params? }], text }
   */
  angular.module('adminPortal').factory('aiPrompt', aiPrompt);

  var EXPORT_TOOL = {
    name: 'export_results',
    description:
      'Export the correspondence grid to an Excel (.xls) file. Call this when the user asks to export, download, ' +
      'or save the results as Excel/CSV/a spreadsheet/a file. ' +
      'scope="page" → only the rows currently visible on this page (default when unspecified). ' +
      'scope="selected" → only the rows the user has checked/selected in the grid. ' +
      'scope="all" → every record matching the current search/filter, across all pages. ' +
      'Examples: "export this to excel" → scope="page". "download all results" → scope="all". ' +
      '"export the selected rows" / "export what I checked" → scope="selected".',
    input_schema: {
      type: 'object',
      properties: {
        scope: { type: 'string', enum: ['page', 'all', 'selected'], description: 'Which rows to export.' },
        columns: {
          type: 'array',
          items: { type: 'string', enum: ['id', 'docId', 'claimNumber', 'customer', 'delivery', 'created', 'status'] },
          description:
            'Optional: only include these columns, in this order, e.g. ["claimNumber","customer","status"]. ' +
            'Set this when the user names specific fields, e.g. "export just the claim number and status". ' +
            'Omit entirely to export every column.'
        }
      },
      required: ['scope']
    }
  };

  var CHART_TOOL = {
    name: 'show_chart',
    description:
      'Render a small chart of the currently loaded dataset in the chat, instead of a text answer. ' +
      'Call this when the user asks to "chart", "graph", "plot", or "visualize" a breakdown — ' +
      'e.g. "show me a chart of records by status", "graph the customer breakdown", "chart delivery methods". ' +
      'metric="status" → breakdown by LatestStatus. metric="customer" → breakdown by customer. ' +
      'metric="delivery" → breakdown by delivery method. ' +
      'metric="deliveryByStatus" → a cross-tab of delivery method BY status, e.g. "chart delivery method by status", ' +
      '"break down deliveries by their status". chartType="bar"/unspecified → one combined stacked bar. ' +
      'chartType="pie"/"donut" → small multiples — one pie/donut PER delivery method, each showing that method\'s own ' +
      'status split (a single pie can\'t hold two dimensions, so several small ones are used instead). ' +
      'chartType picks how it is drawn — infer it from the user\'s wording: "pie chart" → pie, "donut"/"doughnut" → ' +
      'donut, "bar chart"/"graph"/unspecified → bar (the default). ' +
      'Only works when data is already loaded — if none is loaded, call search_grid first in the same turn.',
    input_schema: {
      type: 'object',
      properties: {
        metric: { type: 'string', enum: ['status', 'customer', 'delivery', 'deliveryByStatus'], description: 'Which breakdown to chart.' },
        chartType: { type: 'string', enum: ['bar', 'pie', 'donut'], description: 'How to draw it. Defaults to bar when not mentioned. Ignored for metric="deliveryByStatus".' }
      },
      required: ['metric']
    }
  };

  // Shared by the "create a new draft" and "patch the open draft" tools so their fields can't drift apart.
  var CORRESPONDENCE_FIELD_PROPERTIES = {
    claimNumber: { type: 'string', description: 'Claim number or tracking ID, e.g. CL-5432, 1234567890, claim-5432' },
    policyNumber: { type: 'string', description: 'Policy number, e.g. PL-95042' },
    customerId: { type: 'string', description: 'Customer ID if mentioned' },
    requestingSystemName: { type: 'string', description: 'System requesting this, e.g. "Payment Guard Advantage", "Annuity Correspondence", "Claims System"' },
    deliveryMethod: { type: 'string', enum: ['Mail', 'ePresent'], description: 'How to deliver: Mail (default, physical mail) or ePresent (electronic delivery — infer from context, e.g. an email address given as the contact method).' },
    documentDescription: { type: 'string', description: 'What document/kit to send, e.g. "Welcome Kit", "Annuity Letter", "Claims Update"' },
    recipientFullName: { type: 'string', description: 'Recipient\'s full name, e.g. "John Doe"' },
    recipientFirstName: { type: 'string', description: 'Recipient\'s first name only' },
    recipientLastName: { type: 'string', description: 'Recipient\'s last name only' },
    addressLine1: { type: 'string', description: 'Street address, e.g. "123 Main St" or "123 Main Street, Apt 4"' },
    city: { type: 'string', description: 'City, e.g. "Springfield"' },
    state: { type: 'string', description: 'State (2-letter code), e.g. "IL", "CA", "NY"' },
    zip: { type: 'string', description: 'ZIP/postal code, e.g. "62701"' },
    country: { type: 'string', description: 'Country — default is "US"' },
    versionCode: { type: 'string', description: 'Template version if specified' },
    solutionsId: { type: 'string', description: 'Template GUID if specified' }
  };

  var SEND_CORRESPONDENCE_TOOL = {
    name: 'send_correspondence_request',
    description:
      '**AI-Powered Payload Generation**: Create and submit a NEW correspondence request payload. ' +
      'ONLY call this tool when the user describes a correspondence they want to SEND or SUBMIT, AND no draft is ' +
      'currently open (see CURRENTLY OPEN DRAFT PAYLOAD below — if one exists, use update_correspondence_request instead). ' +
      'The AI will: extract all recipient, claim, policy, and delivery details from the user\'s description; ' +
      'generate a proper JSON payload; open it in a form for review; user can modify and submit. ' +
      'Examples: ' +
      '"Send a welcome kit to John Doe at 123 Main St, Springfield IL 62701 for claim CL-5432". ' +
      '"Submit a PGA Welcome Kit for policy PL-95042 to jane@example.com". ' +
      '"Send an annuity letter to Mike Smith, 456 Oak Ave, Chicago, IL 60601, claim number 1234567890". ' +
      'Extract ALL fields mentioned: names, addresses, phone, claim/policy numbers, delivery method, document type. ' +
      'NOTE: "Processing" as a status (e.g. "show records in Processing status") is NOT a send action — use search_grid instead. ' +
      'When in doubt, extract every detail the user provides.',
    input_schema: { type: 'object', properties: CORRESPONDENCE_FIELD_PROPERTIES, required: [] }
  };

  var UPDATE_CORRESPONDENCE_TOOL = {
    name: 'update_correspondence_request',
    description:
      'Patch the correspondence request draft that is CURRENTLY OPEN for review (the user just asked to send ' +
      'something and the form is showing — see CURRENTLY OPEN DRAFT PAYLOAD below). Call this instead of ' +
      'send_correspondence_request when the user gives a correction or addition to that open draft — e.g. ' +
      '"actually make delivery Email", "add a phone number", "change the claim to CL-999", "use 456 Oak Ave instead". ' +
      'Only include the fields that changed — every other field keeps its current value. ' +
      'Do NOT use this if no draft is currently open — use send_correspondence_request for a brand new request.',
    input_schema: { type: 'object', properties: CORRESPONDENCE_FIELD_PROPERTIES, required: [] }
  };

  var CLEAR_FILTERS_TOOL = {
    name: 'clear_filters',
    description:
      'Clear every active filter/search criterion on the correspondence grid and reload the full, unfiltered dataset. ' +
      'Call this when the user asks to clear, reset, or remove the filters/search — ' +
      'e.g. "clear all the filters", "clear the filters in the grid", "reset the search", "remove all filters", "show everything again". ' +
      'Do NOT use search_grid for this — search_grid with no params is not the same as clearing filters. ' +
      'Takes no parameters.',
    input_schema: { type: 'object', properties: {} }
  };

  var UNDO_TOOL = {
    name: 'undo_last_search',
    description:
      'Revert the grid to the filters that were active immediately before the most recent search/filter change. ' +
      'Call this when the user says "undo", "go back", "undo that", "revert the last change", "undo the last search". ' +
      'Takes no parameters.',
    input_schema: { type: 'object', properties: {} }
  };

  var SEARCH_TOOL = {
    name: 'search_grid',
    description:
      'Refresh the correspondence data grid with optional filters. ' +
      'ALWAYS call this tool when the user asks to LIST, SHOW, FIND, FILTER, or LOAD records — ' +
      'even if data is already in the grid. ' +
      'Status values are exact strings the user names — "Processing" is a status, not an action. ' +
      'Status is NOT limited to Completed/Processing/Submitted/Failed — the data also has values like ' +
      '"AwaitingImaging", "New", "Error", and possibly others. Never substitute or drop a status name ' +
      'you don\'t recognize — pass it through exactly as the user said it. ' +
      'Examples: ' +
      '"list all completed requests" → status="Completed". ' +
      '"show records in Processing status" → status="Processing". ' +
      '"show failed records" → status="Failed". ' +
      '"show submitted records" → status="Submitted". ' +
      '"show records that are Processing or Completed" / "in progress and completed" → statuses=["Processing","Completed"] (do NOT use `status` when more than one is named). ' +
      '"find claim CL-12345" → claimNumber="CL-12345". ' +
      '"find policy PL-9876" → policyNumber="PL-9876". ' +
      'Only skip the tool if the user is asking a QUESTION about data already shown (counts, summaries).',
    input_schema: {
      type: 'object',
      properties: {
        freeText: { type: 'string', description: 'General free-text search across all fields' },
        correspondenceId: { type: 'string', description: 'Filter by exact CorrespondenceId' },
        docId: { type: 'string', description: 'Filter by exact DocId / document ID' },
        claimNumber: { type: 'string', description: 'Filter by claim number, e.g. CL-95042 or 1234567890' },
        policyNumber: { type: 'string', description: 'Filter by policy number, e.g. PL-95042' },
        customer: { type: 'string', description: 'Filter by RequestingDomainName (customer name)' },
        customerId: { type: 'string', description: 'Filter by ECCOTrackingCustomerId' },
        status: {
          type: 'string',
          description: 'Filter by a SINGLE LatestStatus, using EXACTLY the status name the user says (case as given, e.g. "AwaitingImaging", "Processing", "Completed", "Submitted", "Failed", "New", "Error" — this is not a fixed list, the data may contain other status names too). ' +
            '"Processing" is a STATUS VALUE — always use search_grid for this, never send_correspondence_request. For more than one status at once, use `statuses` instead and omit this.'
        },
        statuses: {
          type: 'array',
          items: { type: 'string' },
          description: 'Filter by MULTIPLE statuses at once (matches any of them), using EXACTLY the status names the user says, e.g. "records that are Processing or Completed" → ["Processing","Completed"], "AwaitingImaging and Submitted" → ["AwaitingImaging","Submitted"]. Status names are not a fixed enum — pass whatever the user names verbatim. Use this instead of `status` whenever the user names more than one status.'
        },
        delivery: { type: 'string', description: 'Filter by DeliveryMethodCode, e.g. Mail, Email' },
        fromDate: { type: 'string', description: 'Start of RequestCreationTS date range (ISO 8601, e.g. 2024-01-01)' },
        toDate: { type: 'string', description: 'End of RequestCreationTS date range (ISO 8601, e.g. 2024-12-31)' },
        pageSize: { type: 'number', description: 'Max results to return. Use for "top N" / "latest N" queries, e.g. 20 for "top 20".' },
        sortBy: { type: 'string', description: 'Field to sort by. Use "requestCreationTS" to sort by creation date.' },
        sortDirection: { type: 'string', enum: ['asc', 'desc'], description: 'Sort direction. "desc" = newest first, "asc" = oldest first.' }
      },
      required: []
    }
  };

  var TOOLS = [SEND_CORRESPONDENCE_TOOL, UPDATE_CORRESPONDENCE_TOOL, SEARCH_TOOL, EXPORT_TOOL, CLEAR_FILTERS_TOOL, UNDO_TOOL, CHART_TOOL];

  // Caps a breakdown to its top N entries — an uncapped list (one line per distinct customer)
  // would be resent as system-prompt text on every call regardless of relevance.
  var MAX_BREAKDOWN_ROWS = 12;

  // "Summarize/timeline/history" questions need more than the usual 5-record preview — enough
  // individual records to narrate a chronology, not just counts.
  var NARRATIVE_RE = /\b(summar\w*|timeline|history|what happened|recap|walk me through)\b/i;

  function countsToText(counts) {
    var sorted = Object.keys(counts || {})
      .map(function (k) { return [k, counts[k]]; })
      .sort(function (a, b) { return b[1] - a[1]; });
    var lines = sorted.slice(0, MAX_BREAKDOWN_ROWS).map(function (e) { return '  - ' + e[0] + ': ' + e[1]; });
    var rest = sorted.slice(MAX_BREAKDOWN_ROWS);
    if (rest.length > 0) {
      var restTotal = rest.reduce(function (sum, e) { return sum + e[1]; }, 0);
      lines.push('  - (+' + rest.length + ' more, ' + restTotal + ' records total)');
    }
    return lines.join('\n');
  }

  function paramsToText(params) {
    if (!params) return null;
    var entries = Object.keys(params).filter(function (k) {
      var v = params[k];
      return v !== undefined && v !== null && v !== '';
    });
    if (entries.length === 0) return null;
    return entries.map(function (k) { return '  - ' + k + ': ' + params[k]; }).join('\n');
  }

  var CORRECTION_RULES =
    '\n6. The user gives a PARTIAL correction or refinement of what is currently on screen ' +
    '(e.g. "actually make that Completed instead", "also filter by Payment Guard Advantage", ' +
    '"remove the date range", "just show the last 10 of those") → call search_grid with the FULL merged ' +
    'filter set: start from CURRENTLY ACTIVE GRID FILTERS above, apply only the change the user described, ' +
    'and keep every other active filter unless the user asked to remove it.\n' +
    '7. The user says "undo", "go back", "revert that", "undo the last search/change" → call undo_last_search. ' +
    'Do not guess what the previous filters were — the app restores them.\n' +
    '8. The user asks for two things in one message (e.g. "show failed records from last week and export them") ' +
    '→ call BOTH tools in the same turn (e.g. search_grid AND export_results); each is executed in order.\n' +
    '9. The user asks to CHART, GRAPH, PLOT, or VISUALIZE a breakdown → call show_chart with metric="status"|"customer"|"delivery". ' +
    'If they also ask to see a specific chart from a fresh search (e.g. "show failed records as a chart"), call search_grid AND show_chart together. ' +
    'If the user asks for the SAME breakdown in MULTIPLE chart types (e.g. "as both a pie and a donut chart", "show it as a bar and a pie"), ' +
    'call show_chart once PER requested chartType, same metric each time — do not merge them into a single call.\n' +
    '10. The user asks to export only SPECIFIC FIELDS (e.g. "export just claim number and status") → call export_results ' +
    'with a columns array naming only those fields. Otherwise omit columns entirely.\n' +
    '11. A DRAFT PAYLOAD IS CURRENTLY OPEN (see CURRENTLY OPEN DRAFT PAYLOAD below) and the user gives a correction or ' +
    'addition to it (e.g. "actually make delivery Email", "add a phone number") → call update_correspondence_request ' +
    'with ONLY the changed fields, never send_correspondence_request.\n' +
    '12. The user asks to SUMMARIZE, give a TIMELINE, HISTORY, or RECAP of a claim/customer/correspondence ' +
    '(e.g. "summarize everything for claim CL-12345", "what happened with this customer", "give me a timeline") ' +
    '→ if the records aren\'t already loaded for that claim/customer, call search_grid first (sortBy="requestCreationTS", ' +
    'sortDirection="asc" so the sample reads chronologically); then write a short chronological narrative using the ' +
    'sample records\' status/created/docId fields below — do not just repeat counts.\n' +
    '13. When a request needs multiple tool calls (e.g. a search plus several charts), call ALL of them in THIS turn — ' +
    'never respond with only a plan/preamble like "First, let me search..." without actually calling the tools. ' +
    'Keep any accompanying text to one short sentence; the tool calls themselves do the work, not the text.';

  /**
   * options: { activeParams, activeDraft, statusValues, columnFilters }
   */
  function buildSystemPrompt(prompt, searchContext, options, now) {
    options = options || {};
    now = now || new Date();
    var nowIso = now.toISOString(); // full UTC timestamp — matches how RequestCreationTS is stored
    var todayIso = nowIso.slice(0, 10);
    var dateContext = 'The current date and time is ' + nowIso + ' (UTC, ISO 8601). Today\'s date is ' + todayIso + '. ' +
      'Use the full timestamp — not just the date — to resolve any relative date OR time the user mentions.';

    var activeFiltersText = paramsToText(options.activeParams);
    var activeFiltersSection = activeFiltersText ? '\n=== CURRENTLY ACTIVE GRID FILTERS ===\n' + activeFiltersText + '\n' : '';
    // Column filters the user set in the grid itself. They narrow every count and record below;
    // clear_filters removes them too.
    var columnFilters = options.columnFilters || [];
    if (columnFilters.length) {
      activeFiltersSection += '\n=== GRID COLUMN FILTERS (set by the user; already applied to all counts and records below) ===\n' +
        columnFilters.join('\n') + '\n';
    }

    // The authoritative status list — grounds status params in what the data actually contains
    // and lets the model suggest a correction instead of guessing from training knowledge.
    var statusValues = options.statusValues || [];
    var statusValuesSection = statusValues.length > 0
      ? '\n=== KNOWN STATUS VALUES (exact, case-sensitive strings that exist in the data) ===\n' + statusValues.join(', ') + '\n' +
        'When the user names a status, match it to one of these exactly (case-insensitively if needed) before calling search_grid. ' +
        'If what they said is close to one of these but not an exact match (e.g. a typo or different casing), use the correct value from this list rather than the user\'s literal wording. ' +
        'If a search using one of these statuses returns 0 results, say so plainly — do not imply the status itself might not exist, since it is confirmed to be real.\n'
      : '';

    var activeDraftText = paramsToText(options.activeDraft);
    var activeDraftSection = activeDraftText
      ? '\n=== CURRENTLY OPEN DRAFT PAYLOAD (awaiting review, not yet submitted) ===\n' + activeDraftText + '\n'
      : '';

    var sampleSize = NARRATIVE_RE.test(prompt) ? 15 : 5;

    if (!searchContext || !searchContext.items || searchContext.items.length === 0) {
      return 'You are an AI assistant for a correspondence management system. ' +
        dateContext + ' ' +
        'You have two capabilities: ' +
        '(1) call search_grid to load/filter records by status, claim, customer, docId, dates, etc. ' +
        '    "Processing"/"Completed"/"Failed"/"Submitted" are STATUS VALUES — use search_grid for these. ' +
        '    For "top N" or "latest N" queries: set pageSize=N, sortBy="requestCreationTS", sortDirection="desc". ' +
        '    Convert any date to ISO 8601 YYYY-MM-DD (US-style M/D/YYYY dates are month/day/year, e.g. "08/16/2026" → "2026-08-16"). ' +
        '    "from DATE"/"since DATE" with no end date → set ONLY fromDate (open-ended, that date onward); do not set toDate. ' +
        '    Relative dates ("today", "yesterday", "this week", "last N days", "this/last month", "this/last year") — ' +
        '    resolve using today\'s date (' + todayIso + ') and ALWAYS set both fromDate and toDate ' +
        '    (e.g. "today" → fromDate=toDate=today; "last 7 days" → fromDate=today-7, toDate=today). ' +
        '    Relative TIME ("last N hours", "last N minutes", "since this morning") — use the full ' +
        '    current timestamp (' + nowIso + ') to compute a precise ISO 8601 fromDate; do not set toDate. ' +
        '(2) call send_correspondence_request ONLY when the user explicitly says "send" or "submit". ' +
        '(3) call export_results when the user asks to export/download/save as CSV, with scope="page"|"all"|"selected". ' +
        '(4) call clear_filters when the user asks to clear/reset/remove the filters or search — never use search_grid for this. ' +
        '(5) call undo_last_search when the user says "undo"/"go back"/"revert that" — do not guess the previous filters. ' +
        '(6) if the user asks for two things at once (e.g. "show X and export it"), call both tools in the same turn. ' +
        '(7) call show_chart with metric="status"|"customer"|"delivery" when asked to chart/graph/plot/visualize — ' +
        'combine with search_grid in the same turn if a fresh search is also implied. ' +
        '(8) call update_correspondence_request instead of send_correspondence_request when CURRENTLY OPEN DRAFT ' +
        'PAYLOAD below is present and the user is correcting/adding to it — send only the changed fields. ' +
        '(9) When a request needs multiple tool calls, call ALL of them in THIS turn — never respond with only ' +
        'a plan/preamble like "First, let me search..." without actually calling the tools. Keep any ' +
        'accompanying text to one short sentence. ' +
        'Default to search_grid when in doubt.' +
        statusValuesSection +
        activeFiltersSection +
        activeDraftSection;
    }

    var agg = searchContext.allAggregates;
    var fullSection = agg
      ? `=== FULL DATASET — ALL ${agg.total} RECORDS (pre-computed) ===
Status counts (USE THESE for any count question — do NOT count the sample records below):
${countsToText(agg.statusCounts)}
Customer breakdown:
${countsToText(agg.customerCounts)}
Delivery breakdown:
${countsToText(agg.deliveryCounts)}`
      : 'Full-dataset aggregates still loading. For now use current-page counts below.';

    var sampleItems = searchContext.items.slice(0, sampleSize).map(function (r) {
      return { id: r.id, docId: r.docId, claimNumber: r.claimNumber, customer: r.customer, delivery: r.delivery, created: r.created, status: r.status };
    });

    return `You are an AI assistant for a correspondence management system.

${dateContext}

DECISION RULES — follow these exactly:
1. User wants to LIST / SHOW / FILTER / FIND records (most common) → call search_grid.
   Supported filters — extract whichever the user mentions:
   • status         → "Completed" | "Processing" | "Submitted" | "Failed"   ("Processing" is a STATUS, not an action)
   • claimNumber    → claim or tracking number
   • policyNumber   → policy number
   • correspondenceId → correspondence ID
   • docId          → document ID
   • customer       → RequestingDomainName
   • customerId     → ECCOTrackingCustomerId
   • delivery       → DeliveryMethodCode
   • fromDate/toDate → date OR datetime range for RequestCreationTS. Accepts either a plain date
     (YYYY-MM-DD) or a full ISO 8601 UTC timestamp (YYYY-MM-DDTHH:mm:ssZ) — use a full timestamp
     whenever the user's phrasing implies a specific time-of-day, not just a calendar day.
     - Convert any date the user types (MM/DD/YYYY, "Aug 16 2026", "8-16-2026", etc.) to ISO 8601 YYYY-MM-DD.
       US-style numeric dates (M/D/YYYY) are month/day/year — "08/16/2026" → "2026-08-16", NOT "2026-16-08".
     - "from DATE" / "since DATE" / "after DATE" (open-ended, no end mentioned) → set ONLY fromDate to that date.
       Do NOT also set toDate — leaving toDate unset means "from that date onward".
     - "on DATE" / a single specific day → set fromDate AND toDate to that same date.
     - "records from Month Year" (a whole month, no day) → fromDate = first of month, toDate = last of month.
     - Relative dates (whole-day granularity) — resolve using today's date above, ALWAYS setting both fromDate and toDate:
       "today"                → fromDate = toDate = today
       "yesterday"             → fromDate = toDate = today minus 1 day
       "this week"             → fromDate = most recent Monday through today, toDate = today
       "last 7 days" / "past week" → fromDate = today minus 7 days, toDate = today
       "last N days"           → fromDate = today minus N days, toDate = today
       "this month"            → fromDate = first of current month, toDate = today
       "last month"            → fromDate = first of previous month, toDate = last day of previous month
       "this year"             → fromDate = Jan 1 of current year, toDate = today
       "last year"             → fromDate = Jan 1 of previous year, toDate = Dec 31 of previous year
     - Relative TIME (sub-day granularity) — use the full current timestamp above, set ONLY fromDate
       as a precise ISO 8601 timestamp (open-ended up to now; do NOT set toDate, since there can't be
       future records):
       "in the last N hours" / "last N minutes" → fromDate = current timestamp minus N hours/minutes (full ISO datetime)
       "since Xam/Xpm today" / "since this morning" → fromDate = today at that time (full ISO datetime, converted to UTC)
       "records added just now" / "in the last few minutes" → fromDate = current timestamp minus ~5 minutes
   • pageSize       → for "top N" / "latest N" queries, e.g. "top 20" → pageSize=20
   • sortBy         → field to sort by; use "requestCreationTS" for date-based sorting
   • sortDirection  → "asc" (oldest first) or "desc" (newest first)
   IMPORTANT: For ANY "top N", "latest N", or "most recent N" query, you MUST include
              sortBy="requestCreationTS" AND sortDirection="desc" in addition to pageSize=N.
   Examples:
     "top 20 Processing requests"        → status="Processing", pageSize=20, sortBy="requestCreationTS", sortDirection="desc"
     "top 10 completed requests"         → status="Completed", pageSize=10, sortBy="requestCreationTS", sortDirection="desc"
     "latest 5 failed requests"          → status="Failed", pageSize=5, sortBy="requestCreationTS", sortDirection="desc"
     "requests that are Processing or Completed" → statuses=["Processing","Completed"] (never combine two values into 'status')
     "records from Jan 2024"             → fromDate="2024-01-01", toDate="2024-01-31"
     "all records from 08/16/2026"       → fromDate="2026-08-16" (no toDate — open-ended, means that date onward)
     "records on 08/16/2026"             → fromDate="2026-08-16", toDate="2026-08-16" (single day)
     "records from today"                → fromDate=toDate=${todayIso}
     "show yesterday's records"          → fromDate=toDate=(today minus 1 day, computed from ${todayIso})
     "records from the last 7 days"      → fromDate=(today minus 7 days), toDate=${todayIso}
     "records from this month"           → fromDate=(first of this month), toDate=${todayIso}
     "records from the last 2 hours"     → fromDate="${nowIso}" minus 2 hours (full ISO timestamp), no toDate
     "records added in the last 30 minutes" → fromDate="${nowIso}" minus 30 minutes (full ISO timestamp), no toDate
     "correspondence ID abc-123"         → correspondenceId="abc-123"
     "show doc ID D-456"                 → docId="D-456"
     "records in Processing status"      → status="Processing"
     "find claim CL-12345"                → claimNumber="CL-12345"
     "find policy PL-9876"               → policyNumber="PL-9876"
     "top 5 failed since 2024-06-01"     → status="Failed", fromDate="2024-06-01", pageSize=5, sortBy="requestCreationTS", sortDirection="desc"

2. User explicitly asks to SEND or SUBMIT a NEW correspondence → call send_correspondence_request.
   Only for creating new requests — NEVER for searching or listing.
   NEVER call this because the user mentioned "processing" as a status value.

3. User asks to EXPORT, DOWNLOAD, or SAVE the results (CSV/Excel/file) → call export_results.
   scope="page" for "this page"/unspecified, scope="all" for "everything"/"all records", scope="selected" for "the selected/checked rows".

4. User asks to CLEAR, RESET, or REMOVE the filters/search (e.g. "clear all the filters in the grid",
   "reset the search", "remove the filters") → call clear_filters. Do NOT call search_grid for this.

5. User asks a QUESTION about counts, totals, breakdowns, or summaries — including phrases like
   "how many records for each customer/status", "how many records are there", "break this down by X"
   → Do NOT call any tool. Answer directly using the pre-computed numbers below.
   → CRITICAL: The 5 sample records are for context only. NEVER count them to answer quantity questions.
${CORRECTION_RULES}
${statusValuesSection}
${activeFiltersSection}
${activeDraftSection}
${fullSection}

=== CURRENT PAGE (page ${searchContext.page} — ${searchContext.items.length} of ${searchContext.total} total) ===
Page status counts:
${countsToText(searchContext.pageStatusCounts)}

Sample records (first ${sampleItems.length} of ${searchContext.items.length} on this page, in list order — do NOT count these for totals):
${JSON.stringify(sampleItems)}`;
  }

  /** options: { history: [{ role, content }], activeParams, activeDraft, statusValues } */
  function buildRequestBody(prompt, searchContext, options, config, now) {
    options = options || {};
    // Recent turns give the model short-term memory ("that", "those", "narrow it further") —
    // capped by the caller to bound token usage.
    var priorMessages = (options.history || []).map(function (t) { return { role: t.role, content: t.content }; });
    return {
      model: config.aiModel,
      max_tokens: config.aiMaxTokens,
      system: buildSystemPrompt(prompt, searchContext, options, now),
      tools: TOOLS,
      messages: priorMessages.concat([{ role: 'user', content: prompt }])
    };
  }

  function blockToAction(block) {
    var input = block.input || {};
    switch (block.name) {
      case 'search_grid': return { type: 'search', params: input };
      case 'export_results': return { type: 'export', params: input.scope ? input : { scope: 'page' } };
      case 'clear_filters': return { type: 'clear_filters' };
      case 'send_correspondence_request': return { type: 'send_correspondence', params: input };
      case 'update_correspondence_request': return { type: 'update_correspondence', params: input };
      case 'undo_last_search': return { type: 'undo' };
      case 'show_chart': return { type: 'chart', params: input.metric ? input : { metric: 'status' } };
      default: return null;
    }
  }

  function findTool(blocks, name) {
    return blocks.filter(function (b) { return b.name === name; })[0];
  }

  function parseResponse(payload) {
    payload = payload || {};
    var content = payload.content || [];

    if (payload.stop_reason === 'tool_use') {
      var textBlock = content.filter(function (b) { return b.type === 'text'; })[0];
      var modelText = textBlock && textBlock.text;
      var toolBlocks = content.filter(function (b) { return b.type === 'tool_use'; });
      var withText = function (fallback) { return modelText || fallback; };

      // Several actions in one turn (e.g. "show failed records and export them") — run each in order.
      if (toolBlocks.length > 1) {
        var actions = toolBlocks.map(blockToAction).filter(Boolean);
        if (actions.length > 1) return { type: 'multi', actions: actions, text: withText('Performing multiple actions…') };
      }

      var sendBlock = findTool(toolBlocks, 'send_correspondence_request');
      if (sendBlock) {
        var sendParams = sendBlock.input || {};
        var desc = [
          sendParams.documentDescription,
          sendParams.claimNumber ? 'claim ' + sendParams.claimNumber : null,
          sendParams.policyNumber ? 'policy ' + sendParams.policyNumber : null,
          sendParams.recipientFullName ? 'for ' + sendParams.recipientFullName : null
        ].filter(Boolean).join(', ') || 'correspondence request';
        return { type: 'send_correspondence', params: sendParams, text: withText('Submitting ' + desc + '…') };
      }

      var updateBlock = findTool(toolBlocks, 'update_correspondence_request');
      if (updateBlock) {
        var updateParams = updateBlock.input || {};
        var changed = Object.keys(updateParams).join(', ') || 'the draft';
        return { type: 'update_correspondence', params: updateParams, text: withText('Updating ' + changed + '…') };
      }

      if (findTool(toolBlocks, 'undo_last_search')) return { type: 'undo', text: withText('Undoing the last change…') };
      if (findTool(toolBlocks, 'clear_filters')) return { type: 'clear_filters', text: withText('Clearing all filters…') };

      var chartBlock = findTool(toolBlocks, 'show_chart');
      if (chartBlock) {
        var chartParams = chartBlock.input && chartBlock.input.metric ? chartBlock.input : { metric: 'status' };
        // deliveryByStatus is a cross-tab — the default text explains how it's being shown.
        var chartText = chartParams.metric === 'deliveryByStatus'
          ? (chartParams.chartType === 'pie' || chartParams.chartType === 'donut'
            ? 'Delivery method by status, as one ' + chartParams.chartType + ' per delivery method…'
            : 'Delivery method by status as one combined stacked bar…')
          : 'Charting by ' + chartParams.metric + '…';
        return { type: 'chart', params: chartParams, text: withText(chartText) };
      }

      var exportBlock = findTool(toolBlocks, 'export_results');
      if (exportBlock) {
        var exportParams = exportBlock.input && exportBlock.input.scope ? exportBlock.input : { scope: 'page' };
        var scopeText = exportParams.scope === 'all' ? 'all matching records' : exportParams.scope === 'selected' ? 'selected rows' : 'this page';
        return { type: 'export', params: exportParams, text: withText('Exporting ' + scopeText + ' to Excel…') };
      }

      var searchBlock = findTool(toolBlocks, 'search_grid');
      if (searchBlock) {
        var p = searchBlock.input || {};
        var what = p.claimNumber ? 'claim ' + p.claimNumber
          : p.policyNumber ? 'policy ' + p.policyNumber
            : p.customer ? 'customer "' + p.customer + '"'
              : p.status ? 'status "' + p.status + '"'
                : p.delivery ? 'delivery "' + p.delivery + '"'
                  : p.freeText ? '"' + p.freeText + '"'
                    : 'all records';
        return { type: 'search', params: p, text: withText('Searching for ' + what + '…') };
      }
    }

    if (payload.stop_reason === 'max_tokens') {
      // Out of budget mid-turn — any text is an incomplete plan and any tool call was cut off.
      return { type: 'text', text: 'That request needed more steps than I could finish in one go — try breaking it into a smaller request (e.g. fewer chart types at once).' };
    }
    var block = content[0];
    return { type: 'text', text: block && block.type === 'text' ? block.text : 'No response received.' };
  }

  function aiPrompt() {
    return {
      TOOLS: TOOLS,
      NARRATIVE_RE: NARRATIVE_RE,
      countsToText: countsToText,
      buildSystemPrompt: buildSystemPrompt,
      buildRequestBody: buildRequestBody,
      parseResponse: parseResponse
    };
  }
})();
