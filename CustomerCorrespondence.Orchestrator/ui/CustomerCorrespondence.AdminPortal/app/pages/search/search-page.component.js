(function () {
  'use strict';

  /**
   * <search-page> — Correspondence Manager.
   *
   * Server-side search/sort/paging/filtering over correspondence requests, plus everything
   * the AI assistant drives through assistantBridge: structured filters with
   * undo, full-dataset aggregates/insights, Excel export, AI-drafted payloads.
   *
   * Controls are shown according to the user's permissions (authService.can); the API enforces
   * them, along with the attribute rules (data scope, PII clearance, record status, limits).
   */
  angular.module('adminPortal').component('searchPage', {
    templateUrl: 'app/pages/search/search-page.html',
    controller: SearchPageController
  });

  var PAGE_SIZES = [10, 25, 50, 100];
  var DEFAULT_PAGE_SIZE = 25;
  // Fetch remaining pages in parallel groups to stay within API concurrency limits.
  var PARALLEL = 10;
  // Session-local aggregate snapshots per filter combination ("Failed is up 40% since last time").
  var SNAPSHOT_KEY = 'ecco.ai.searchSnapshots';
  // Periodic whole-dataset Failed-rate scan, independent of the user's current search. Runs only
  // while this page is open — an always-on version would need a backend job.
  var SCAN_KEY = 'ecco.ai.backgroundScanHistory';
  var SCAN_INTERVAL_MS = 10 * 60 * 1000;
  var SCAN_FIRST_DELAY_MS = 15000;
  var DAY_MS = 86400000;

  SearchPageController.$inject = [
    '$q', '$timeout', '$interval', '$log', '$window',
    'correspondenceApi', 'correspondenceAnalytics', 'searchQuery', 'searchRefinement',
    'payloadValidation', 'correspondencePayload', 'excelExport', 'errorRecovery', 'storage', 'assistantBridge', 'gridFilter',
    'authService', 'CORRESPONDENCE_COLUMNS', '$scope', 'adminApi', '$injector', '$location', 'accountService'
  ];

  function SearchPageController(
    $q, $timeout, $interval, $log, $window,
    correspondenceApi, analytics, searchQuery, searchRefinement,
    payloadValidation, correspondencePayload, excelExport, errorRecovery, storage, assistantBridge, gridFilter,
    authService, CORRESPONDENCE_COLUMNS, $scope, adminApi, $injector, $location, accountService
  ) {
    var $ctrl = this;
    var undoStack = [];   // filter states before each AI-driven change, for "undo"
    var fetchId = 0;      // lets a slow, superseded analytics fetch detect it's stale
    var searchSeq = 0;    // drops out-of-order search responses
    var timeouts = [];
    var intervals = [];

    $ctrl.bridge = assistantBridge;
    $ctrl.can = authService.can;

    $ctrl.$onInit = function () {
      $ctrl.freeText = '';
      $ctrl.results = null;
      $ctrl.page = 1;
      $ctrl.pageSize = preferredPageSize();
      $ctrl.sort = { by: null, dir: 'asc' };
      $ctrl.filters = gridFilter.emptyFilter();
      $ctrl.selectedIds = {};
      $ctrl.aggregates = null;
      $ctrl.dataLoaded = false;
      $ctrl.aiInsights = [];
      $ctrl.backgroundAlerts = [];
      $ctrl.activeAiParams = null;
      $ctrl.filterLabel = null;
      $ctrl.statusValues = [];
      $ctrl.appliedFreeText = '';
      $ctrl.valueOptions = { customer: [], delivery: [] };

      assistantBridge.setContextProvider(assistantContext);
      assistantBridge.setActions({
        search: function (params) { return applyAiParams(params); },
        clearFilters: function () { return applyAiParams(null); },
        undo: undo,
        export: aiExport,
        sendCorrespondence: aiSendCorrespondence,
        updateCorrespondence: aiUpdateCorrespondence
      });

      applyLinkFilter();
      doSearch(1, $ctrl.pageSize);

      correspondenceApi.getStatusValues()
        .then(function (values) { $ctrl.statusValues = values || []; })
        .catch(function () { $ctrl.statusValues = []; });

      // Checkbox-filter lists come from the API, limited to what this user may see.
      $q.all({
        customer: correspondenceApi.getFieldValues('customer').catch(function () { return []; }),
        delivery: correspondenceApi.getFieldValues('delivery').catch(function () { return []; })
      }).then(function (values) { $ctrl.valueOptions = values; });

      loadServerAlerts();
    };

    $ctrl.$onDestroy = function () {
      if (pdfPoll) $timeout.cancel(pdfPoll);
      timeouts.forEach(function (t) { $timeout.cancel(t); });
      intervals.forEach(function (t) { $interval.cancel(t); });
      assistantBridge.setContextProvider(null);
      assistantBridge.setActions({});
    };

    // ════════ Assistant context ════════

    function searchContext() {
      var r = $ctrl.results;
      if (!r) return null;
      var pageStatusCounts = {};
      r.items.forEach(function (it) {
        var s = String(it.status || 'Unknown');
        pageStatusCounts[s] = (pageStatusCounts[s] || 0) + 1;
      });
      return {
        total: r.total,
        page: r.page,
        pageSize: r.pageSize,
        items: r.items,
        pageStatusCounts: pageStatusCounts,
        allAggregates: $ctrl.aggregates,
        allDataLoaded: $ctrl.dataLoaded
      };
    }

    function assistantContext() {
      return {
        searchContext: searchContext(),
        activeParams: $ctrl.activeAiParams,
        columnFilters: columnFilterChips().map(function (c) { return c.label; }),
        activeDraft: $ctrl.draftParams || null,
        statusValues: $ctrl.statusValues,
        insights: $ctrl.backgroundAlerts.concat($ctrl.aiInsights)
      };
    }

    // ════════ Search ════════

    function isTopN() {
      return !!($ctrl.activeAiParams && $ctrl.activeAiParams.pageSize != null);
    }

    function without(obj, keys) {
      var copy = angular.extend({}, obj);
      keys.forEach(function (k) { delete copy[k]; });
      return copy;
    }

    /**
     * Runs a search and renders the grid. Resolves once the full-dataset aggregates have
     * loaded too (or null when skipped/unavailable) — awaited by callers that need fresh
     * breakdowns right after this search, e.g. the AI chaining a chart onto the same turn.
     * The grid and spinner update well before that.
     *
     * skipAnalytics: same dataset (paging/sorting) — keep aggregates, filters and labels.
     */
    function doSearch(p, size, aiParams, manualSort, skipAnalytics) {
      size = size || $ctrl.pageSize;
      var seq = ++searchSeq;
      $ctrl.loading = true;
      $ctrl.error = null;
      if (!aiParams && !skipAnalytics) {
        $ctrl.filterLabel = null;
        $ctrl.activeAiParams = null;
      }
      if (!aiParams) $ctrl.appliedFreeText = ($ctrl.freeText || '').trim();

      // AI sort wins, then an explicit column sort, then the grid's current sort.
      var sort = manualSort || $ctrl.sort;
      var sortBy = (aiParams && aiParams.sortBy) || sort.by;
      var sortDirection = (aiParams && aiParams.sortDirection) || sort.dir;
      var body = searchQuery.buildSearchRequest({
        page: p,
        pageSize: size,
        aiParams: aiParams,
        freeText: $ctrl.freeText,
        sortBy: sortBy,
        sortDirection: sortDirection
      });
      // Grid column filters run on the server, across every matching record.
      var wireFilter = gridFilter.toWire($ctrl.filters);
      if (wireFilter) body.filter = wireFilter;
      $log.debug('search request body', body);

      return correspondenceApi.search(body).then(function (resp) {
        if (seq !== searchSeq) return null;
        var rawTotal = analytics.totalOf(resp);
        $ctrl.results = {
          page: resp.page || resp.Page || 1,
          pageSize: resp.pageSize || resp.PageSize || size,
          // Top-N: cap at N so the pager shows "1–N of N" instead of "of <all records>".
          total: aiParams && aiParams.pageSize ? Math.min(rawTotal, aiParams.pageSize) : rawTotal,
          items: analytics.itemsOf(resp).map(analytics.toGridRow)
        };
        $ctrl.page = $ctrl.results.page;
        $ctrl.payloadsHiddenReason = resp.payloadsHidden ? (resp.payloadsHiddenReason || 'Payloads are hidden.') : null;
        loadPdfStatus();
        $ctrl.selectedIds = {};
        $ctrl.refinement = searchRefinement.analyzeSearchQuery(
          without(aiParams || {}, ['pageSize']),
          aiParams ? (aiParams.freeText || '') : $ctrl.freeText);
        $ctrl.loading = false;

        if (skipAnalytics) return null;
        $ctrl.aggregates = null;
        $ctrl.dataLoaded = false;
        $ctrl.aiInsights = [];
        return fetchAllForAI(body, $ctrl.results.total, ++fetchId, !!(aiParams && aiParams.pageSize));
      }, function (err) {
        if (seq !== searchSeq) return null;
        $log.error('search error', err);
        $ctrl.error = errorRecovery.classifyError(err);
        $ctrl.results = null;
        return null;
      }).finally(function () {
        if (seq === searchSeq) $ctrl.loading = false;
      });
    }

    /**
     * The search box: field:value filters (status:Failed, from:2026-11-01, …) move into the grid's column filters
     * (shown as chips, and in the column menus); what's left is searched as text, several terms meaning any of them.
     */
    $ctrl.search = function () {
      var parsed = searchQuery.parseSearchBox($ctrl.freeText);
      $ctrl.searchError = parsed.errors.length ? parsed.errors.join(' ') : null;
      if ($ctrl.searchError) return;
      parsed.filters.forEach(function (f) {
        $ctrl.filters = gridFilter.setForField($ctrl.filters, f.field, f.entry);
      });
      $ctrl.freeText = parsed.text;
      doSearch(1, $ctrl.pageSize);
    };

    /** A pasted column of IDs (e.g. from Excel) keeps one term per line: an input would run the lines together. */
    $ctrl.onSearchPaste = function (e) {
      var clip = e && (e.clipboardData || (e.originalEvent && e.originalEvent.clipboardData));
      var text = clip && clip.getData('text');
      if (!text || !/[\r\n\t]/.test(text.trim())) return;
      e.preventDefault();
      var list = text.split(/[\r\n\t]+/).map(function (t) { return t.trim(); }).filter(Boolean).join(', ');
      var input = e.target;
      var value = $ctrl.freeText || '';
      var start = typeof input.selectionStart === 'number' ? input.selectionStart : value.length;
      var end = typeof input.selectionEnd === 'number' ? input.selectionEnd : value.length;
      var before = value.slice(0, start).replace(/[\s,]*$/, '');
      var after = value.slice(end).replace(/^[\s,]*/, '');
      $scope.$applyAsync(function () {
        $ctrl.freeText = (before ? before + ', ' : '') + list + (after ? ', ' + after : '');
        $ctrl.searchError = null;
      });
    };

    $ctrl.searchTipsOpen = false;
    $ctrl.useTip = function (example) {
      $ctrl.freeText = (($ctrl.freeText || '').trim() + ' ' + example).trim();
      $ctrl.searchTipsOpen = false;
    };
    $ctrl.termCount = function () { return searchQuery.splitTerms($ctrl.freeText).length; };

    $ctrl.onSearchKeydown = function (e) {
      if (e.key === 'Enter') {
        e.preventDefault();
        $ctrl.search();
      }
    };

    function resetToUnfiltered() {
      $ctrl.freeText = '';
      $ctrl.searchError = null;
      $ctrl.appliedFreeText = '';
      $ctrl.error = null;
      $ctrl.aggregates = null;
      $ctrl.filterLabel = null;
      $ctrl.activeAiParams = null;
      $ctrl.filters = gridFilter.emptyFilter();
      if (PAGE_SIZES.indexOf($ctrl.pageSize) === -1) $ctrl.pageSize = preferredPageSize(); // leaving top-N mode
      // An empty params object makes doSearch ignore the search box and load everything.
      return doSearch(1, $ctrl.pageSize, {});
    }

    $ctrl.clearSearch = resetToUnfiltered;

    $ctrl.retrySearch = function () {
      if (!$ctrl.error || !$ctrl.error.isRetryable) return;
      $ctrl.isRetrying = true;
      doSearch($ctrl.page, $ctrl.pageSize, $ctrl.activeAiParams || undefined).finally(function () { $ctrl.isRetrying = false; });
    };

    $ctrl.detectedClaim = function () {
      return searchQuery.detectClaim($ctrl.freeText);
    };

    // ════════ Grid events ════════

    // Memoized: a one-way binding to a freshly built array would never settle the digest.
    var topNSizes = { n: null, sizes: null };
    $ctrl.pageSizeOptions = function () {
      if (!isTopN()) return PAGE_SIZES;
      var n = $ctrl.activeAiParams.pageSize;
      if (n <= 25) return null; // everything fits on one page — no pager
      if (topNSizes.n !== n) topNSizes = { n: n, sizes: PAGE_SIZES.filter(function (s) { return s <= n; }) };
      return topNSizes.sizes;
    };

    /** The account's "rows per page" preference, or the default. */
    function preferredPageSize() {
      var size = accountService.prefs().pageSize;
      return PAGE_SIZES.indexOf(size) !== -1 ? size : DEFAULT_PAGE_SIZE;
    }
    // Changed in Preferences (or arriving with the account after sign-in): reload page 1 at that size.
    var lastPreferredSize = preferredPageSize();
    $scope.$on('prefs:changed', function () {
      var size = preferredPageSize();
      if (size === lastPreferredSize) return; // another preference changed
      lastPreferredSize = size;
      if (size !== $ctrl.pageSize && !isTopN() && $ctrl.results) $ctrl.onPageChange(1, size);
    });

    $ctrl.onPageChange = function (page, size) {
      $ctrl.page = page;
      // Picking a page size in the pager becomes the preference (not in top-N mode, whose sizes are special).
      var sizeChanged = size !== $ctrl.pageSize;
      $ctrl.pageSize = size;
      if (sizeChanged && !isTopN() && PAGE_SIZES.indexOf(size) !== -1) {
        lastPreferredSize = size;
        accountService.set('pageSize', size).catch(angular.noop);
      }
      if (isTopN()) return; // all N rows are in memory — the grid re-slices locally
      var params = $ctrl.activeAiParams ? without($ctrl.activeAiParams, ['pageSize', 'sortBy', 'sortDirection']) : undefined;
      doSearch(page, size, params, $ctrl.sort, true);
    };

    $ctrl.onSortChange = function (sort) {
      $ctrl.sort = { by: sort.by, dir: sort.by ? sort.dir : $ctrl.sort.dir };
      // Top-N: reorder locally — the server would return a different set of N records.
      if (isTopN()) return;
      var params = $ctrl.activeAiParams ? without($ctrl.activeAiParams, ['pageSize', 'sortBy', 'sortDirection']) : undefined;
      doSearch(1, $ctrl.pageSize, params, { by: sort.by, dir: sort.dir }, true);
    };

    /**
     * Links into the grid: #!/?sla=breached (from an SLA alert, in the portal or in Teams / email) switches on
     * "Breached only". The parameter is then dropped, so the filter behaves like any other. True when one applied.
     */
    function applyLinkFilter() {
      var sla = $location.search().sla;
      if (!sla) return false;
      $location.search('sla', null).replace();
      if (String(sla).toLowerCase() !== 'breached') return false;
      $ctrl.filters = gridFilter.setForField($ctrl.filters, 'sla', { field: 'sla', operator: 'eq', value: 'Breached', ignoreCase: true });
      return true;
    }
    // Already on this page when the link is followed (e.g. from the alerts bell).
    $scope.$on('$locationChangeSuccess', function () {
      if (applyLinkFilter()) $ctrl.onFiltersChange($ctrl.filters);
    });

    /** A column filter changed: a new result set, so page 1 and fresh analytics. */
    $ctrl.onFiltersChange = function (filters) {
      $ctrl.filters = filters;
      doSearch(1, $ctrl.pageSize, $ctrl.activeAiParams || undefined, $ctrl.sort);
    };

    $ctrl.clearSelection = function () {
      $ctrl.selectedIds = {};
    };

    $ctrl.toggleSelect = function (id) {
      var next = angular.extend({}, $ctrl.selectedIds);
      if (next[id]) delete next[id];
      else next[id] = true;
      $ctrl.selectedIds = next;
    };

    $ctrl.toggleAll = function (ids, checked) {
      var next = angular.extend({}, $ctrl.selectedIds);
      ids.forEach(function (id) {
        if (checked) next[id] = true;
        else delete next[id];
      });
      $ctrl.selectedIds = next;
    };

    $ctrl.selectedCount = function () {
      return Object.keys($ctrl.selectedIds).length;
    };

    function selectedRows() {
      if (!$ctrl.results) return [];
      return $ctrl.results.items.filter(function (r) { return $ctrl.selectedIds[r.id]; });
    }

    // ════════ AI-driven filters ════════

    /** Applies an AI filter state (null = clear). Shared by search, clear-filters and undo. */
    function applyAiParams(params, opts) {
      if (!opts || opts.pushUndo !== false) {
        undoStack = undoStack.slice(-9).concat([$ctrl.activeAiParams]); // 10 steps back is plenty
      }
      if (params === null) return resetToUnfiltered();

      $ctrl.freeText = searchQuery.searchBoxText(params);
      $ctrl.filterLabel = searchQuery.filterLabel(params);
      $ctrl.activeAiParams = params;

      // The AI's filter replaces any grid column filter on the same field, so the two can't contradict
      // each other (e.g. a column "Status: Failed" left over under an AI "Processing" search).
      AI_PARAM_COLUMNS.forEach(function (m) {
        if (m.keys.some(function (k) { return !searchQuery.isEmpty(pickKey(params, k)); })) {
          $ctrl.filters = gridFilter.setForField($ctrl.filters, m.field, null);
        }
      });

      // Top-N queries use N as the page size; otherwise leave top-N mode for a normal size.
      var size = params.pageSize || (PAGE_SIZES.indexOf($ctrl.pageSize) !== -1 ? $ctrl.pageSize : preferredPageSize());
      $ctrl.pageSize = size;
      // Sync grid sort state so the column header shows the arrow.
      if (params.sortBy) $ctrl.sort = { by: params.sortBy, dir: params.sortDirection || 'desc' };

      return doSearch(1, size, params);
    }

    var AI_PARAM_COLUMNS = [
      { keys: ['status', 'statuses'], field: 'status' },
      { keys: ['customer'], field: 'customer' },
      { keys: ['delivery'], field: 'delivery' },
      { keys: ['claimNumber'], field: 'claimNumber' },
      { keys: ['docId'], field: 'docId' },
      { keys: ['correspondenceId'], field: 'id' },
      { keys: ['fromDate', 'toDate'], field: 'created' }
    ];

    function pickKey(obj, key) {
      var out = {};
      out[key] = obj[key];
      return out;
    }

    function undo() {
      if (!undoStack.length) {
        showToast('danger', 'Nothing to undo', 'Undo');
        return;
      }
      var previous = undoStack[undoStack.length - 1];
      undoStack = undoStack.slice(0, -1);
      applyAiParams(previous, { pushUndo: false });
    }

    $ctrl.clearAiFilter = function () {
      resetToUnfiltered();
    };

    // ════════ Active filter chips ════════

    var columnsByKey = {};
    CORRESPONDENCE_COLUMNS.forEach(function (c) { columnsByKey[c.key] = c; });

    function columnFilterChips() {
      if (!gridFilter.isComposite($ctrl.filters)) return [];
      return $ctrl.filters.filters.filter(gridFilter.isActive).map(function (entry) {
        var field = entry.field || (entry.filters && entry.filters[0] && entry.filters[0].field);
        var column = columnsByKey[field] || { label: field, filterType: 'text' };
        return { kind: 'column', field: field, label: gridFilter.describe(entry, column) };
      });
    }

    // Memoized on its inputs: ng-repeat over a fresh array each digest would never settle.
    var chipCache = { key: null, chips: [] };
    $ctrl.chips = function () {
      var key = angular.toJson([$ctrl.activeAiParams, $ctrl.appliedFreeText, gridFilter.toWire($ctrl.filters)]);
      if (key === chipCache.key) return chipCache.chips;
      var chips = searchQuery.paramChips($ctrl.activeAiParams).map(function (c) {
        return { kind: 'ai', keys: c.keys, label: c.label };
      });
      if (!$ctrl.activeAiParams && $ctrl.appliedFreeText) chips = chips.concat(textChips($ctrl.appliedFreeText));
      chipCache = { key: key, chips: chips.concat(columnFilterChips()) };
      return chipCache.chips;
    };

    /** One chip per search term ("any of" when several); a long pasted list is one chip. */
    var MAX_TERM_CHIPS = 5;
    function textChips(text) {
      var terms = searchQuery.splitTerms(text);
      if (terms.length <= 1) return [{ kind: 'text', label: 'Search: "' + text + '"' }];
      if (terms.length > MAX_TERM_CHIPS) {
        return [{ kind: 'text', label: 'Search: any of ' + terms.length + ' terms (' + terms.slice(0, 3).join(', ') + ', …)', title: terms.join(', ') }];
      }
      return terms.map(function (t, i) {
        return { kind: 'text', term: t, label: (i === 0 ? 'Search: ' : 'or ') + '"' + t + '"' };
      });
    }

    $ctrl.removeChip = function (chip) {
      if (chip.kind === 'column') {
        $ctrl.onFiltersChange(gridFilter.setForField($ctrl.filters, chip.field, null));
      } else if (chip.kind === 'text') {
        $ctrl.freeText = chip.term
          ? searchQuery.splitTerms($ctrl.appliedFreeText).filter(function (t) { return t !== chip.term; }).join(', ')
          : '';
        doSearch(1, $ctrl.pageSize);
      } else {
        var rest = without($ctrl.activeAiParams, chip.keys);
        applyAiParams(searchQuery.isEmpty(rest) ? null : rest);
      }
    };

    $ctrl.applySuggestion = function (suggestion) {
      if (suggestion.query) applyAiParams(without(suggestion.query, ['freeText']));
    };

    $ctrl.applyTypoFix = function (corrected) {
      var original = $ctrl.refinement && $ctrl.refinement.typoCorrection && $ctrl.refinement.typoCorrection.original;
      $ctrl.freeText = original
        ? $ctrl.freeText.replace(new RegExp('\\b' + original + '\\b', 'i'), corrected)
        : corrected;
      doSearch(1, $ctrl.pageSize);
    };

    // ════════ Full-dataset analytics ════════

    /**
     * Fetches every matching record (first page reveals the backend's page-size cap).
     * cap: optional top-N limit. Resolves to { items, total }.
     */
    function fetchAllPages(filterBody, knownTotal, cap) {
      return correspondenceApi.search(angular.extend({}, filterBody, { page: 1, pageSize: 2000 })).then(function (firstResp) {
        var firstBatch = analytics.itemsOf(firstResp);
        var rawTotal = knownTotal !== undefined ? knownTotal : analytics.totalOf(firstResp, firstBatch.length);
        var total = cap ? Math.min(rawTotal, cap) : rawTotal;
        var batchSize = Math.max(firstBatch.length, 1);
        var totalPages = Math.ceil(total / batchSize);
        var all = firstBatch.slice();

        var chain = $q.resolve();
        function fetchGroup(start) {
          var pages = [];
          for (var p = start; p < Math.min(start + PARALLEL, totalPages + 1); p++) pages.push(p);
          return $q.all(pages.map(function (page) {
            return correspondenceApi.search(angular.extend({}, filterBody, { page: page, pageSize: batchSize }));
          })).then(function (responses) {
            responses.forEach(function (resp) { Array.prototype.push.apply(all, analytics.itemsOf(resp)); });
          });
        }
        for (var start = 2; start <= totalPages; start += PARALLEL) {
          chain = chain.then(fetchGroup.bind(null, start));
        }
        return chain.then(function () { return { items: all.slice(0, total), total: total }; });
      });
    }

    function fetchAllForAI(body, total, id, topN) {
      function isStale() { return id !== fetchId; }

      if (total === 0) {
        $ctrl.aiInsights = [];
        $ctrl.dataLoaded = true;
        return $q.resolve(null);
      }

      // Order doesn't affect aggregates — except for top-N, where it decides which N records count.
      var filterBody = without(body, topN ? ['page', 'pageSize'] : ['page', 'pageSize', 'sortBy', 'sortDirection']);

      return fetchAllPages(filterBody, total, topN ? total : undefined).then(function (fetched) {
        // A newer search started while these were in flight — don't clobber its state.
        if (isStale()) return null;
        var agg = analytics.computeAggregates(fetched.items.map(analytics.normalizeForAggregates), total, filterBody);
        $ctrl.aggregates = agg;

        var insights = analytics.buildInsights(agg);
        var sig = analytics.snapshotSignature(body);
        if (sig) {
          var snapshots = storage.getJson(SNAPSHOT_KEY, {});
          var failedCount = agg.statusCounts.Failed || 0;
          insights = insights.concat(analytics.compareWithSnapshot(snapshots[sig], total, failedCount));
          snapshots[sig] = { total: total, failedCount: failedCount, capturedAt: new Date().toISOString() };
          storage.setJson(SNAPSHOT_KEY, snapshots);
        }
        $ctrl.aiInsights = insights.slice(0, 5);
        return agg;
      }).catch(function () {
        if (isStale()) return null;
        $ctrl.aggregates = null;
        $ctrl.aiInsights = [];
        return null;
      }).finally(function () {
        if (!isStale()) $ctrl.dataLoaded = true;
      });
    }

    /** Open server-side alerts feed the assistant's insights (the checks themselves run in the API). */
    function loadServerAlerts() {
      if (!$ctrl.can('alerts.read')) return;
      adminApi.alerts(true, 3).then(function (list) {
        $ctrl.backgroundAlerts = list.map(function (a) { return '🚨 ' + a.title + ' — ' + a.message; });
      }).catch(angular.noop);
    }

    // Superseded by the server-side alert job (ProcessRequestEndPoint/Alerts); kept for reference, no longer scheduled.
    function backgroundScan() {
      var since = new Date(Date.now() - DAY_MS).toISOString();
      correspondenceApi.search({ fromDate: since, page: 1, pageSize: 1000 }).then(function (resp) {
        var items = analytics.itemsOf(resp);
        if (!analytics.totalOf(resp, items.length)) return;
        var history = storage.getJson(SCAN_KEY, []);
        var result = analytics.evaluateBackgroundScan(items, history);
        if (result.alert && $ctrl.backgroundAlerts.indexOf(result.alert) === -1) {
          $ctrl.backgroundAlerts = [result.alert].concat($ctrl.backgroundAlerts).slice(0, 3);
          try {
            if ('Notification' in $window && $window.Notification.permission === 'granted') {
              new $window.Notification('ECCO Correspondence Manager', { body: result.alert });
            }
          } catch (e) {
            // best-effort only
          }
        }
        storage.setJson(SCAN_KEY, history.concat([{ failedRate: result.failedRate, total: items.length, scannedAt: new Date().toISOString() }]).slice(-10));
      }).catch(angular.noop); // never surface a background check as a user-facing error
    }

    $ctrl.toggleAnalytics = function () {
      $ctrl.showAnalytics = !$ctrl.showAnalytics;
    };

    $ctrl.analyticsLoading = function () {
      return $ctrl.results !== null && !$ctrl.dataLoaded;
    };

    // ════════ Export ════════

    function exportDenied() {
      if ($ctrl.can('correspondence.export')) return false;
      showToast('danger', 'You don\'t have permission to export.', 'Export');
      return true;
    }

    $ctrl.exportPage = function (columns) {
      if (!$ctrl.results || exportDenied()) return;
      excelExport.download($ctrl.results.items, 'export-page.xls', columns);
      $ctrl.exportMenuOpen = false;
    };

    $ctrl.exportSelected = function (columns) {
      if (exportDenied()) return;
      var rows = selectedRows();
      if (!rows.length) return;
      excelExport.download(rows, 'export-selected.xls', columns);
      $ctrl.exportMenuOpen = false;
    };

    $ctrl.exportAll = function (columns) {
      if (!$ctrl.results || exportDenied()) return;
      $ctrl.exportMenuOpen = false;
      $ctrl.exporting = true;
      var cap = $ctrl.activeAiParams && $ctrl.activeAiParams.pageSize;
      var filterBody = angular.extend(searchQuery.buildFilterRequest($ctrl.activeAiParams, $ctrl.activeAiParams ? '' : $ctrl.appliedFreeText),
        { includePayload: true, purpose: 'export' });
      var wireFilter = gridFilter.toWire($ctrl.filters);
      if (wireFilter) filterBody.filter = wireFilter;
      fetchAllPages(filterBody, undefined, cap)
        .then(function (fetched) { excelExport.download(fetched.items, 'export-all.xls', columns); })
        .catch(function (err) {
          if (err && err.status !== 403) $log.error('export all failed', err);
          showToast('danger', errorRecovery.messageOf(err, 'Export failed'), 'Export Failed');
        })
        .finally(function () { $ctrl.exporting = false; });
    };

    function aiExport(params) {
      if (params.scope === 'all') $ctrl.exportAll(params.columns);
      else if (params.scope === 'selected') $ctrl.exportSelected(params.columns);
      else $ctrl.exportPage(params.columns);
    }

    // ════════ Bulk resubmit ════════
    // Re-POSTs each checked row's ORIGINAL stored payload. A real write action, so it always needs
    // manually selected rows AND an explicit confirmation — the AI can find candidates but never
    // triggers this itself. The API checks each record against the access policies (e.g. only
    // Failed records, only customers in the user's scope) and the reasons for any refusals are shown.

    $ctrl.bulkResubmit = function () {
      var withPayload = selectedRows().filter(function (r) { return !!r.payload; });
      if (!withPayload.length) {
        showToast('danger', $ctrl.payloadsHiddenReason
          ? 'Resubmitting needs the original payloads, which you can\'t view. ' + $ctrl.payloadsHiddenReason
          : 'None of the selected records have a stored payload to resubmit.', 'Resubmit');
        return;
      }
      $ctrl.resubmitConfirm = withPayload;
    };

    $ctrl.resubmitMessage = function () {
      var n = ($ctrl.resubmitConfirm || []).length;
      return 'Resubmit ' + n + ' record' + (n === 1 ? '' : 's') + '? Each one\'s original request payload will be re-sent as a new correspondence request.';
    };

    $ctrl.executeResubmit = function () {
      var rows = $ctrl.resubmitConfirm || [];
      $ctrl.resubmitConfirm = null;
      if (!rows.length) return;
      $ctrl.resubmitting = true;
      var succeeded = 0;
      var failed = 0;
      var reasons = {}; // refusal message -> count
      var chain = $q.resolve();
      rows.forEach(function (r) {
        chain = chain.then(function () {
          var parsed;
          try { parsed = JSON.parse(r.payload); } catch (e) { failed++; return null; }
          return correspondenceApi.resubmit(r.id, parsed).then(function () { succeeded++; }, function (err) {
            failed++;
            var reason = errorRecovery.messageOf(err, 'Request failed');
            reasons[reason] = (reasons[reason] || 0) + 1;
          });
        });
      });
      chain.then(function () {
        $ctrl.resubmitting = false;
        var detail = Object.keys(reasons).map(function (k) { return reasons[k] + ' × ' + k; }).join(' ');
        showToast(failed === 0 ? 'success' : 'danger',
          'Resubmitted ' + succeeded + ' record' + (succeeded === 1 ? '' : 's') + (failed ? '; ' + failed + ' not resubmitted. ' + detail : '.'),
          'Resubmit');
        if (succeeded > 0) $timeout(function () { doSearch($ctrl.page, $ctrl.pageSize, $ctrl.activeAiParams || undefined); }, 1500);
      });
    };

    // ════════ PDFs (rendered from each request's payload, stored in Blob Storage) ════════

    var PDF_POLL_MS = 10000;
    function realtimeLive() { return $injector.has('realtimeService') && $injector.get('realtimeService').isLive(); }
    var pdfPoll = null;
    $ctrl.pdfStatus = { storageConfigured: false, items: {} };

    /** PDF states for the rows on screen; re-checks while any are still being generated. */
    function loadPdfStatus() {
      if (pdfPoll) { $timeout.cancel(pdfPoll); pdfPoll = null; }
      var ids = (($ctrl.results && $ctrl.results.items) || []).map(function (r) { return r.id; });
      if (!ids.length) return;
      correspondenceApi.pdfStatus(ids).then(function (resp) {
        var items = {};
        (resp.items || []).forEach(function (s) { items[s.correspondenceId] = s; });
        $ctrl.pdfStatus = { storageConfigured: !!resp.storageConfigured, items: items };
        var busy = Object.keys(items).some(function (k) { return items[k].status === 'Pending' || items[k].status === 'Processing'; });
        if (busy && !realtimeLive()) pdfPoll = $timeout(loadPdfStatus, PDF_POLL_MS);
      }).catch(function () {
        $ctrl.pdfStatus = { storageConfigured: false, items: {} }; // PDFs aren't available on this server
      });
    }

    function setPdfState(id, state) {
      var items = angular.extend({}, $ctrl.pdfStatus.items);
      items[id] = angular.extend({ correspondenceId: id }, state);
      $ctrl.pdfStatus = { storageConfigured: $ctrl.pdfStatus.storageConfigured, items: items };
    }

    function generatePdf(row) {
      setPdfState(row.id, { status: 'Processing' });
      return correspondenceApi.generatePdf(row.id).then(function (result) {
        setPdfState(row.id, result);
        return result;
      }, function (err) {
        setPdfState(row.id, { status: 'Failed', lastError: errorRecovery.messageOf(err) });
        return $q.reject(err);
      });
    }

    /**
     * The row's PDF icon: opens the PDF in the portal's viewer when it exists; otherwise generates it first
     * (and then opens it, if the user may view payloads).
     */
    $ctrl.onPdf = function (row) {
      var s = $ctrl.pdfStatus.items[row.id] || { status: 'None' };
      if (s.status === 'Generated') {
        $ctrl.pdfViewer = row;
        return;
      }
      generatePdf(row).then(function () {
        if ($ctrl.can('correspondence.payload.read')) $ctrl.pdfViewer = row;
        else showToast('success', 'PDF generated for ' + (row.docId || row.id) + '.', 'PDF');
      }).catch(function (err) { showToast('danger', errorRecovery.messageOf(err, 'The PDF request failed'), 'PDF'); });
    };

    $ctrl.closePdfViewer = function () { $ctrl.pdfViewer = null; };

    /** Regenerated from inside the viewer: keep the row's icon in step. */
    $ctrl.pdfRegenerated = function (status) {
      if ($ctrl.pdfViewer) setPdfState($ctrl.pdfViewer.id, status);
    };

    $ctrl.generateSelectedPdfs = function () {
      var rows = selectedRows();
      if (!rows.length) return;
      $ctrl.generatingPdfs = true;
      var ok = 0;
      var reasons = {};
      var chain = $q.resolve();
      rows.forEach(function (r) {
        chain = chain.then(function () {
          return generatePdf(r).then(function () { ok++; }, function (err) {
            var reason = errorRecovery.messageOf(err, 'Request failed');
            reasons[reason] = (reasons[reason] || 0) + 1;
          });
        });
      });
      chain.then(function () {
        var failed = rows.length - ok;
        var detail = Object.keys(reasons).map(function (k) { return reasons[k] + ' × ' + k; }).join(' ');
        showToast(failed ? 'danger' : 'success',
          'Generated ' + ok + ' PDF' + (ok === 1 ? '' : 's') + (failed ? '; ' + failed + ' failed. ' + detail : '.'), 'PDF');
      }).finally(function () { $ctrl.generatingPdfs = false; });
    };

    // ════════ Record details panel ════════

    $ctrl.openDetails = function (row) { $ctrl.detailsId = row.id; };

    /** The panel's PDF button: open it when it exists, otherwise generate it first. */
    $ctrl.openPdfFromPanel = function (row) {
      correspondenceApi.pdfStatus([row.id]).then(function (resp) {
        var s = (resp.items || [])[0];
        if (s) setPdfState(row.id, s);
        $ctrl.onPdf(row);
      }).catch(function (err) { showToast('danger', errorRecovery.messageOf(err), 'PDF'); });
    };

    // ════════ Live updates (realtimeService) ════════

    $ctrl.live = {};
    $scope.$on('portal:recordsChanged', function (e, data) {
      var onPage = (($ctrl.results && $ctrl.results.items) || []).map(function (r) { return r.id; });
      var changed = (data.changedIds || []).filter(function (id) { return onPage.indexOf(id) !== -1; }).length;
      if (!data.newRecords && !changed) return;
      $ctrl.live = { newRecords: ($ctrl.live.newRecords || 0) + (data.newRecords || 0), changed: Math.max($ctrl.live.changed || 0, changed) };
    });
    $scope.$on('portal:pdfStatusChanged', function (e, data) {
      if ($ctrl.pdfStatus.items[data.correspondenceId] || (($ctrl.results && $ctrl.results.items) || []).some(function (r) { return r.id === data.correspondenceId; })) {
        setPdfState(data.correspondenceId, angular.extend({}, $ctrl.pdfStatus.items[data.correspondenceId], { status: data.status }));
      }
    });
    $scope.$on('portal:alertRaised', function () { loadServerAlerts(); });

    $ctrl.applyLiveUpdates = function () {
      $ctrl.live = {};
      doSearch($ctrl.page, $ctrl.pageSize, $ctrl.activeAiParams || undefined, $ctrl.sort);
    };

    // ════════ Letter preview (Insert form) ════════

    $ctrl.previewLetter = function (payloadText) {
      return correspondenceApi.previewLetter({ payload: payloadText }).then(function (result) {
        $ctrl.letterPreview = {
          blob: result.blob, fileName: 'letter-preview.pdf', title: 'Letter preview',
          note: (result.template ? 'Template: ' + result.template + '. ' : '') +
                (result.missing.length ? 'Missing values shown as [Name]: ' + result.missing.join(', ') + '.' : 'All placeholders filled.')
        };
      }).catch(function (err) {
        showToast('danger', errorRecovery.messageOf(err, 'The letter preview failed.'), 'Letter preview');
      });
    };

    // ════════ Delete (to the recycle bin) ════════
    // Removes the records and their status history from the database. Always needs an explicit
    // confirmation; the API checks each record against the delete policies (e.g. customer scope).

    $ctrl.askDeleteSelected = function () {
      $ctrl.askDeleteRows(selectedRows());
    };

    $ctrl.askDeleteRows = function (rows) {
      if (!$ctrl.can('correspondence.delete') || !rows.length) return;
      $ctrl.deleteConfirm = rows;
    };

    $ctrl.deleteMessage = function () {
      var rows = $ctrl.deleteConfirm || [];
      if (rows.length === 1) {
        var r = rows[0];
        return 'Delete record ' + (r.docId || r.id) + (r.claimNumber ? ' (claim ' + r.claimNumber + ')' : '') +
          '? It moves to the recycle bin with its status history and PDF, and can be restored from Administration → Recycle bin for 30 days.';
      }
      return 'Delete ' + rows.length + ' records? They move to the recycle bin with their status history and PDFs, and can be restored for 30 days.';
    };

    $ctrl.executeDelete = function () {
      var rows = $ctrl.deleteConfirm || [];
      $ctrl.deleteConfirm = null;
      if (!rows.length) return;
      $ctrl.deleting = true;
      correspondenceApi.deleteRecords(rows.map(function (r) { return r.id; }))
        .then(function (result) {
          var deleted = (result.deleted || []).length;
          var denied = result.denied || [];
          var reasons = {};
          denied.forEach(function (d) { reasons[d.message] = (reasons[d.message] || 0) + 1; });
          var detail = Object.keys(reasons).map(function (k) { return reasons[k] + ' × ' + k; }).join(' ');
          var missing = (result.notFound || []).length;
          showToast(denied.length || missing ? 'danger' : 'success',
            'Moved ' + deleted + ' record' + (deleted === 1 ? '' : 's') + ' to the recycle bin' +
            (result.recycleBinDays ? ' (restorable for ' + result.recycleBinDays + ' days).' : '.') +
            (denied.length ? ' ' + denied.length + ' not deleted. ' + detail : '') +
            (missing ? ' ' + missing + ' no longer existed.' : ''), 'Delete');
          $ctrl.selectedIds = {};
          // Same view, fresh data and analytics; step back a page if this one is now empty.
          var lastPage = Math.max(1, Math.ceil(Math.max(0, ($ctrl.results ? $ctrl.results.total : 0) - deleted) / $ctrl.pageSize));
          return doSearch(Math.min($ctrl.page, lastPage), $ctrl.pageSize, $ctrl.activeAiParams || undefined, $ctrl.sort);
        })
        .catch(function (err) {
          showToast('danger', errorRecovery.messageOf(err, 'Delete failed'), 'Delete');
        })
        .finally(function () { $ctrl.deleting = false; });
    };

    // ════════ Insert / AI drafts ════════

    $ctrl.openInsert = function () {
      $ctrl.showInsert = true;
    };

    function closeInsert() {
      $ctrl.showInsert = false;
      $ctrl.aiPayload = null;
      $ctrl.aiContext = null;
      $ctrl.draftParams = null;
    }

    $ctrl.onInsertHide = closeInsert;

    $ctrl.onInsertSuccess = function (message) {
      closeInsert();
      showToast('success', message, 'Correspondence Inserted');
      // Inserts are processed asynchronously — the record isn't in the DB right after the 202,
      // so wait briefly, then show page 1 (newest first by default).
      $timeout(function () { doSearch(1, $ctrl.pageSize); }, 3000);
    };

    $ctrl.onInsertError = function (message) {
      showToast('danger', message, 'Insert Failed');
    };

    /** Another request for the same claim/policy in the last 7 days? Best-effort — never blocks. */
    function checkForDuplicate(params) {
      if (!params.claimNumber && !params.policyNumber) return $q.resolve(null);
      var sevenDaysAgo = new Date(Date.now() - 7 * DAY_MS).toISOString().slice(0, 10);
      return correspondenceApi.search({
        claimNumber: params.claimNumber || undefined,
        policyNumber: params.claimNumber ? undefined : (params.policyNumber || undefined),
        fromDate: sevenDaysAgo,
        page: 1,
        pageSize: 1
      }).then(function (resp) {
        var total = analytics.totalOf(resp);
        if (!total) return null;
        var target = params.claimNumber ? 'claim ' + params.claimNumber : 'policy ' + params.policyNumber;
        return '⚠️ Possible duplicate: ' + total + ' existing request' + (total === 1 ? '' : 's') + ' for ' + target + ' already submitted in the last 7 days.';
      }).catch(function () { return null; });
    }

    function openDraft(params, extraContext) {
      var built = correspondencePayload.build(params, extraContext);
      $ctrl.draftParams = params;
      $ctrl.aiPayload = JSON.stringify(built.payload, null, 2);
      $ctrl.aiContext = built.contextWithValidation;
      $ctrl.showInsert = true;
      return built.validation;
    }

    function needsAttention(count) {
      return count + ' field' + (count === 1 ? '' : 's') + ' need' + (count === 1 ? 's' : '');
    }

    function aiSendCorrespondence(params) {
      if (!$ctrl.can('correspondence.create')) return $q.reject('You don\'t have permission to create correspondence.');
      return checkForDuplicate(params).then(function (dupWarning) {
        var validation = openDraft(params, dupWarning ? [dupWarning] : []);
        var dupNote = dupWarning ? ' ' + dupWarning : '';
        if (validation.errors.length) {
          return 'Correspondence payload generated, but ' + needsAttention(validation.errors.length) +
            ' attention before you can submit — see the highlighted issues in the form.' + dupNote;
        }
        return 'Correspondence payload generated. Review and modify the details in the form, then submit.' + dupNote;
      });
    }

    /** Patches the open draft with just the corrected fields instead of re-describing it all. */
    function aiUpdateCorrespondence(partial) {
      var merged = angular.extend({}, $ctrl.draftParams || {}, partial);
      var validation = openDraft(merged, []);
      var changed = Object.keys(partial).join(', ') || 'the draft';
      if (validation.errors.length) {
        return $q.resolve('Updated ' + changed + '. ' + needsAttention(validation.errors.length) + ' attention before you can submit.');
      }
      return $q.resolve('Updated ' + changed + '. Review the changes in the form, then submit.');
    }

    // ════════ Payload viewer & Explain ════════

    $ctrl.viewPayload = function (row) {
      var payload = row.payload || '';
      // The API decrypts stored payloads (PGP or AES-GCM) when includePayload is set. Anything still
      // armored PGP or bare Base64 here is ciphertext it couldn't decrypt (e.g. another environment's key).
      var trimmed = payload.trim();
      var isPgp = trimmed.indexOf('-----BEGIN PGP MESSAGE-----') === 0;
      var isBase64 = !!trimmed && trimmed.charAt(0) !== '{' && trimmed.charAt(0) !== '[' && /^[A-Za-z0-9+/=\s]+$/.test(trimmed);
      $ctrl.viewer = {
        payload: payload,
        isEncrypted: !!(row.payloadEncrypted || row.PayloadEncrypted) || isPgp || isBase64
      };
    };

    $ctrl.closePayload = function () {
      $ctrl.viewer = null;
    };

    $ctrl.payloadCopied = function () {
      showToast('success', 'Payload copied to clipboard', 'Copied');
    };

    /**
     * Hands the record's key fields to the assistant as a ready-made question. For Failed
     * records, runs the pre-submit validation over the stored payload so the AI can cite a
     * concrete likely cause instead of guessing.
     */
    $ctrl.explainRecord = function (row) {
      var summary = [
        row.id ? 'Correspondence ID: ' + row.id : null,
        row.docId ? 'Doc ID: ' + row.docId : null,
        row.claimNumber ? 'Claim: ' + row.claimNumber : null,
        row.customer ? 'Customer: ' + row.customer : null,
        row.delivery ? 'Delivery: ' + row.delivery : null,
        row.created ? 'Created: ' + row.created : null,
        row.status ? 'Status: ' + row.status : null
      ].filter(Boolean).join(', ');

      var rootCauseHint = '';
      if (String(row.status || '').toLowerCase() === 'failed' && row.payload) {
        try {
          var validation = payloadValidation.validatePayload(JSON.parse(row.payload));
          var issues = validation.errors.concat(validation.warnings).map(function (v) { return v.message; });
          if (issues.length) {
            rootCauseHint = ' Automated validation of this record\'s payload found these issues, which are the likely cause(s) of failure: ' + issues.join('; ') + '.';
          }
        } catch (e) {
          // Not parseable JSON (e.g. still PGP-encrypted) — explain without a root-cause hint.
        }
      }

      assistantBridge.openAndAsk('Explain this correspondence record in plain English: ' + summary + '.' + rootCauseHint +
        ' What does its status mean, and is there anything about it that looks unusual?');
    };

    // ════════ Toast ════════

    function showToast(variant, message, title) {
      $ctrl.toast = { variant: variant, message: message, title: title };
    }

    $ctrl.closeToast = function () {
      $ctrl.toast = null;
    };
  }
})();
