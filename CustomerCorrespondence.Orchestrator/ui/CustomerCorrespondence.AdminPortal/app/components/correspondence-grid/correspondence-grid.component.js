(function () {
  'use strict';

  /**
   * <correspondence-grid rows="…" total="…" page="…" page-size="…"
   *     page-size-options="[10, 25, 50, 100] | null"  (null hides the pager)
   *     client-paging="bool"     (top-N mode: every row is in memory — sort/slice locally)
   *     sort="{ by, dir }" filters="CompositeFilterDescriptor" status-options="[…]"
   *     server-filtering="bool"  (the parent applies filters through the API; otherwise they narrow the rows here)
   *     value-options="{ customer: […], delivery: […] }"  (checkbox-filter values; defaults to the loaded rows)
   *     selected-ids="{ id: true }" can-view-payload="bool" can-explain="bool" can-delete="bool"
   *     on-sort-change="…(sort)" on-page-change="…(page, size)" on-filters-change="…(filters)"
   *     on-toggle-select="…(id)" on-toggle-all="…(ids, checked)"
   *     on-view-payload="…(row)" on-explain="…(row)" on-delete="…(row)">
   *
   * Server-side sort and paging. Filtering follows KendoReact's filter row and column menus:
   * each cell has an operator menu and a clear button, and the state is a Kendo-style composite
   * descriptor (see gridFilter). A field's entry may itself be a composite (e.g. "Any of 2").
   *
   * View preferences — visible columns and row density — are remembered per browser.
   */
  angular.module('adminPortal').constant('CORRESPONDENCE_COLUMNS', [
    // filterType picks the filter-row editor and operator list (text | date | select).
    // menu picks the column-menu filter: 'conditions' (two conditions, And/Or) or 'checkbox' (pick values).
    // priority: when the grid is too narrow, columns are hidden from the highest number down (0 never hides).
    // Every hidden value is still in the record's details panel.
    // sortable / filterable: false for computed columns the API can't sort or filter on.
    // options: a fixed value list for a select / checkbox filter (instead of the status list or the loaded rows).
    { key: 'id', label: 'Correspondence Id', filterType: 'text', menu: 'conditions', priority: 7 },
    { key: 'docId', label: 'Doc Id', filterType: 'text', menu: 'conditions', priority: 0 },
    { key: 'claimNumber', label: 'Claim Number', filterType: 'text', menu: 'conditions', priority: 5 },
    { key: 'customer', label: 'Customer', filterType: 'text', menu: 'checkbox', priority: 4 },
    { key: 'delivery', label: 'Delivery', filterType: 'text', menu: 'checkbox', priority: 6 },
    { key: 'created', label: 'Created', type: 'date', filterType: 'date', menu: 'conditions', priority: 3 },
    { key: 'status', label: 'Status', filterType: 'select', menu: 'checkbox', priority: 1 },
    { key: 'sla', label: 'Age / SLA', filterType: 'select', menu: 'checkbox', sortable: false, priority: 2,
      options: ['Breached', 'At risk', 'On track', 'Finished'], operatorKeys: ['eq', 'neq'] }
  ]);

  /** Row actions fold into a "⋯" menu once columns of this priority or more important would otherwise be hidden. */
  var COLLAPSE_ACTIONS_BEFORE_PRIORITY = 6;

  angular.module('adminPortal').component('correspondenceGrid', {
    templateUrl: 'app/components/correspondence-grid/correspondence-grid.html',
    bindings: {
      rows: '<',
      total: '<',
      page: '<',
      pageSize: '<',
      pageSizeOptions: '<',
      clientPaging: '<',
      sort: '<',
      filters: '<',
      statusOptions: '<',
      serverFiltering: '<',
      valueOptions: '<',
      selectedIds: '<',
      canViewPayload: '<',
      canExplain: '<',
      canDelete: '<',
      pdfStatus: '<',
      canDownloadPdf: '<',
      canGeneratePdf: '<',
      onSortChange: '&',
      onPageChange: '&',
      onFiltersChange: '&',
      onToggleSelect: '&',
      onToggleAll: '&',
      onViewPayload: '&',
      onExplain: '&',
      onDelete: '&',
      onPdf: '&',
      onDetails: '&'
    },
    controller: CorrespondenceGridController
  });

  var PREFS_KEY = 'ecco.grid.prefs';

  CorrespondenceGridController.$inject = ['gridService', 'gridFilter', '$filter', '$timeout', 'storage', 'CORRESPONDENCE_COLUMNS',
    '$element', '$scope', '$window', 'accountService'];

  function CorrespondenceGridController(gridService, gridFilter, $filter, $timeout, storage, CORRESPONDENCE_COLUMNS,
    $element, $scope, $window, accountService) {
    var $ctrl = this;

    $ctrl.columns = angular.copy(CORRESPONDENCE_COLUMNS);

    var DATE_FIELDS = $ctrl.columns.filter(function (c) { return c.filterType === 'date'; }).map(function (c) { return c.key; });
    var SELECT_OPERATORS = ['eq', 'neq', 'isnull', 'isnotnull'];

    $ctrl.columns.forEach(function (col, i) {
      col.operators = col.filterType === 'select'
        ? gridFilter.OPERATORS.text.filter(function (o) { return (col.operatorKeys || SELECT_OPERATORS).indexOf(o.operator) !== -1; })
        : gridFilter.operatorsFor(col.filterType);
      col.defaultOperator = col.operators[0].operator;
    });

    // ---- View preferences: visible columns + density (remembered per browser) ----
    // hidden = columns the user switched off; pinned = columns the user switched on although they don't fit
    // (they then outrank every other column); autoHidden = columns dropped to fit the grid's width (not saved).
    var prefs = storage.getJson(PREFS_KEY, {}) || {};
    $ctrl.hidden = prefs.hidden || {};
    $ctrl.pinned = prefs.pinned || {};
    $ctrl.autoHidden = {};
    $ctrl.compactActions = false;
    // Density: the account preference (Preferences dialog) wins over this browser's last choice.
    var accountDensity = accountService.prefs().density;
    $ctrl.density = (accountDensity || prefs.density) === 'compact' ? 'compact' : 'comfortable';
    $scope.$on('prefs:changed', function (e, p) {
      if (p.density && p.density !== $ctrl.density) { $ctrl.density = p.density; savePrefs(); scheduleFit(); }
    });

    function savePrefs() {
      storage.setJson(PREFS_KEY, { hidden: $ctrl.hidden, pinned: $ctrl.pinned, density: $ctrl.density });
    }

    function updateVisibleColumns() {
      var chosen = $ctrl.columns.filter(function (c) { return !$ctrl.hidden[c.key]; });
      if (!chosen.length) { $ctrl.hidden = {}; chosen = $ctrl.columns.slice(); } // never hide everything
      var visible = chosen.filter(function (c) { return !$ctrl.autoHidden[c.key]; });
      visible.forEach(function (col, i) { col.menuAlignEnd = i >= visible.length - 3; }); // open menus inward near the right edge
      $ctrl.visibleColumns = visible;
      $ctrl.autoHiddenColumns = chosen.filter(function (c) { return $ctrl.autoHidden[c.key]; });
    }
    updateVisibleColumns();

    $ctrl.isVisible = function (col) { return !$ctrl.hidden[col.key] && !$ctrl.autoHidden[col.key]; };

    $ctrl.toggleColumn = function (col) {
      var hidden = angular.extend({}, $ctrl.hidden);
      var pinned = angular.extend({}, $ctrl.pinned);
      if ($ctrl.isVisible(col)) {
        hidden[col.key] = true;
        delete pinned[col.key];
        if ($ctrl.columns.every(function (c) { return hidden[c.key]; })) return; // keep at least one
      } else {
        // Switched on: show it even if it doesn't fit; other columns make room for it.
        delete hidden[col.key];
        if ($ctrl.autoHidden[col.key] || col.priority > 0) pinned[col.key] = true;
      }
      $ctrl.hidden = hidden;
      $ctrl.pinned = pinned;
      updateVisibleColumns();
      savePrefs();
      scheduleFit();
    };

    $ctrl.isOnlyVisible = function (col) {
      return $ctrl.isVisible(col) && $ctrl.visibleColumns.length === 1;
    };

    $ctrl.resetColumns = function () {
      $ctrl.hidden = {};
      $ctrl.pinned = {};
      updateVisibleColumns();
      savePrefs();
      scheduleFit();
    };

    // ---- Fit the columns to the grid's width (no horizontal scroll bar) ----
    // Natural column widths are measured with the table at its content width, remembered per column, and the
    // lowest-priority columns are dropped until the rest fit. Columns never shown yet use an estimate.
    var naturalWidth = {};
    var MAX_NATURAL_WIDTH = 320; // text cells truncate at 240px, so a wider reading is a layout artefact
    var ESTIMATE = { id: 150, docId: 150, claimNumber: 130, customer: 190, delivery: 120, created: 160, status: 150, sla: 130 };
    var fitTimer = null;

    function scheduleFit() {
      if (fitTimer) $timeout.cancel(fitTimer);
      fitTimer = $timeout(fit, 0);
    }

    function measure(scroller) {
      var table = scroller.querySelector('.grid__table');
      if (!table) return null;
      var previous = table.style.width;
      table.style.width = 'auto'; // natural widths, not stretched to fill
      var sizes = { select: 0, actions: 0 };
      angular.forEach(table.querySelectorAll('thead tr:first-child th'), function (th) {
        var key = th.getAttribute('data-col');
        var w = th.getBoundingClientRect().width;
        if (key === '_select') sizes.select = w;
        else if (key === '_actions') sizes.actions = w;
        else if (key) naturalWidth[key] = Math.min(w, MAX_NATURAL_WIDTH); // capped: one bad reading must not hide a column for good
      });
      table.style.width = previous;
      return sizes;
    }

    function fit() {
      fitTimer = null;
      var scroller = $element[0].querySelector('.grid__scroll');
      if (!scroller || !scroller.clientWidth) return;
      var sizes = measure(scroller);
      if (!sizes) return;
      if (!$ctrl.compactActions) fullActionsWidth = sizes.actions;
      else compactActionsWidth = sizes.actions;

      var available = scroller.clientWidth - sizes.select - 2;
      var chosen = $ctrl.columns.filter(function (c) { return !$ctrl.hidden[c.key]; });
      // Most important first: pinned columns, then by priority.
      var ranked = chosen.slice().sort(function (a, b) {
        return rank(a) - rank(b);
      });

      function plan(actionsWidth) {
        var room = available - actionsWidth, used = 0, drop = {}, full = false;
        ranked.forEach(function (col) {
          var w = naturalWidth[col.key] || ESTIMATE[col.key] || 140;
          if (rank(col) <= 0) { used += w; return; }          // pinned and the record's key column: always shown
          if (!full && used + w <= room) { used += w; return; }
          full = true;                                         // hide strictly by priority, never a more important column first
          drop[col.key] = true;
        });
        return drop;
      }

      var drop = plan(fullActionsWidth || sizes.actions);
      var important = ranked.some(function (c) { return drop[c.key] && c.priority < COLLAPSE_ACTIONS_BEFORE_PRIORITY; });
      var compact = important;
      if (compact) drop = plan(compactActionsWidth || 76);

      var changed = compact !== $ctrl.compactActions ||
        Object.keys(drop).sort().join() !== Object.keys($ctrl.autoHidden).sort().join();
      if (!changed) { refits = 0; return; }
      $ctrl.compactActions = compact;
      $ctrl.autoHidden = drop;
      updateVisibleColumns();
      // Re-measure once the new layout is drawn (e.g. the compact actions column's real width); bounded, so it can't loop.
      if (++refits < 4) scheduleFit(); else refits = 0;
    }
    var refits = 0;
    var fullActionsWidth = 0, compactActionsWidth = 0;

    function rank(col) { return $ctrl.pinned[col.key] ? -1 : col.priority; }

    var resizeObserver = null;
    $ctrl.$postLink = function () {
      var scroller = $element[0].querySelector('.grid__scroll');
      if ($window.ResizeObserver && scroller) {
        var last = 0;
        resizeObserver = new $window.ResizeObserver(function (entries) {
          var w = Math.round(entries[0].contentRect.width);
          if (w === last) return; // a scroll bar appearing changes the height only
          last = w;
          $scope.$applyAsync(scheduleFit);
        });
        resizeObserver.observe(scroller);
      } else {
        angular.element($window).on('resize', onWindowResize);
      }
      if (scroller) scroller.addEventListener('scroll', $ctrl.onGridScroll, { passive: true });
      scheduleFit();
    };
    function onWindowResize() { $scope.$applyAsync(scheduleFit); }

    $ctrl.$onDestroy = function () {
      if (resizeObserver) resizeObserver.disconnect();
      angular.element($window).off('resize', onWindowResize);
      angular.element($window).off('resize', closeRowMenuAsync);
      if (fitTimer) $timeout.cancel(fitTimer);
    };

    $ctrl.showHiddenColumns = function () { $ctrl.columnsOpen = true; };
    $ctrl.hiddenNames = function () {
      return ($ctrl.autoHiddenColumns || []).map(function (c) { return c.label; }).join(', ');
    };

    // ---- Row "⋯" menu (narrow grids). Fixed-positioned, so the scrolling grid body can't clip it. ----
    $ctrl.rowMenu = null;
    $ctrl.openRowMenu = function (row, $event) {
      if ($event) $event.stopPropagation();
      if ($ctrl.rowMenu && $ctrl.rowMenu.row === row) { $ctrl.rowMenu = null; return; }
      var rect = $event.currentTarget.getBoundingClientRect();
      var menuHeight = 190, menuWidth = 220;
      var below = $window.innerHeight - rect.bottom > menuHeight + 8;
      $ctrl.rowMenu = {
        row: row,
        style: {
          top: (below ? rect.bottom + 4 : Math.max(8, rect.top - menuHeight - 4)) + 'px',
          left: Math.max(8, Math.min(rect.right - menuWidth, $window.innerWidth - menuWidth - 8)) + 'px'
        }
      };
      angular.element($window).one('resize', closeRowMenuAsync);
      // Once drawn, use the menu's real size: flip above the button (or clamp) when it would run off the screen.
      $timeout(function () {
        var el = $element[0].querySelector('.row-menu');
        if (!el || !$ctrl.rowMenu) return;
        var h = el.offsetHeight;
        var top = rect.bottom + 4;
        if (top + h > $window.innerHeight - 8) top = rect.top - h - 4;
        $ctrl.rowMenu.style.top = Math.max(8, Math.min(top, $window.innerHeight - h - 8)) + 'px';
      });
    };
    $ctrl.closeRowMenu = function () { $ctrl.rowMenu = null; };
    function closeRowMenuAsync() { $scope.$applyAsync($ctrl.closeRowMenu); }
    $ctrl.onGridScroll = function () { if ($ctrl.rowMenu) closeRowMenuAsync(); };
    $ctrl.rowAction = function (action) {
      var row = $ctrl.rowMenu && $ctrl.rowMenu.row;
      $ctrl.rowMenu = null;
      if (row) action({ row: row });
    };

    $ctrl.setDensity = function (density) {
      $ctrl.density = density;
      savePrefs();
      accountService.set('density', density).catch(angular.noop); // follows the account; still applies here if saving fails
    };

    /** The Actions column only appears when the user can do something there. */
    $ctrl.showPdf = function () {
      return !!($ctrl.pdfStatus && $ctrl.pdfStatus.storageConfigured && ($ctrl.canDownloadPdf || $ctrl.canGeneratePdf));
    };

    $ctrl.hasActions = function () { return true; }; // Details is always available

    /**
     * The PDF icon's state for a row: { css, title, enabled }. Generated → download; missing or failed →
     * generate (for users who may); being generated → busy.
     */
    $ctrl.pdfIcon = function (row) {
      var s = ($ctrl.pdfStatus && $ctrl.pdfStatus.items[row.id]) || { status: 'None' };
      switch (s.status) {
        case 'Generated':
          return $ctrl.canDownloadPdf
            ? { css: 'ready', enabled: true, title: 'View PDF' }
            : { css: 'ready', enabled: false, title: 'PDF available — viewing it needs payload access' };
        case 'Pending':
        case 'Processing':
          return { css: 'busy', enabled: false, title: 'PDF is being generated…' };
        case 'Failed':
          return $ctrl.canGeneratePdf
            ? { css: 'failed', enabled: true, title: 'PDF generation failed' + (s.lastError ? ': ' + s.lastError : '') + ' — click to try again' }
            : { css: 'failed', enabled: false, title: 'PDF generation failed' };
        default:
          return $ctrl.canGeneratePdf
            ? { css: 'missing', enabled: true, title: 'Generate PDF' }
            : { css: 'missing', enabled: false, title: 'No PDF yet' };
      }
    };

    $ctrl.colspan = function () { return $ctrl.visibleColumns.length + 1 + ($ctrl.hasActions() ? 1 : 0); };

    // ---- Correspondence ids: shortened in the grid, copied whole ----
    $ctrl.shortId = function (id) {
      var s = String(id || '');
      return s.length > 13 ? s.slice(0, 8) + '…' : s;
    };

    $ctrl.copyId = function (row) {
      if (!navigator.clipboard) return;
      navigator.clipboard.writeText(String(row.id)).then(function () {
        $timeout(function () { $ctrl.copiedId = row.id; });
        $timeout(function () { if ($ctrl.copiedId === row.id) $ctrl.copiedId = null; }, 1500);
      }).catch(angular.noop);
    };

    // Per-column editor state: { operator, value, group } where group is a nested composite set from outside.
    $ctrl.cells = {};
    $ctrl.openMenu = null;

    $ctrl.$onChanges = function (changes) {
      if (changes.filters) syncCells();
      refresh();
      if (changes.rows || changes.pdfStatus) scheduleFit(); // new content can change the columns' natural widths
    };

    function syncCells() {
      var prev = $ctrl.cells;
      var next = {};
      $ctrl.columns.forEach(function (col) {
        var entry = gridFilter.findForField($ctrl.filters, col.key);
        if (entry && gridFilter.isComposite(entry)) {
          next[col.key] = { operator: col.defaultOperator, value: null, group: entry };
        } else if (entry) {
          next[col.key] = { operator: entry.operator, value: entry.value };
        } else {
          // No filter yet: keep an operator picked before any value was typed (Kendo does the same).
          var p = prev[col.key];
          var pending = p && !p.group && !hasValue(p.value) && gridFilter.needsValue(p.operator);
          next[col.key] = { operator: pending ? p.operator : col.defaultOperator, value: null };
        }
      });
      $ctrl.cells = next;
    }

    function hasValue(v) {
      return v !== null && v !== undefined && !(typeof v === 'string' && v.trim() === '');
    }

    /** The SLA filter's value for a row's SLA position (Met and Missed are both "Finished"). */
    function slaGroup(sla) {
      if (!sla) return null;
      return { Breached: 'Breached', AtRisk: 'At risk', OnTrack: 'On track', Met: 'Finished', Missed: 'Finished' }[sla.state] || null;
    }

    function applyFilters(rows, filter) {
      if (!gridFilter.findForField(filter, 'sla')) return gridFilter.filterBy(rows, filter, { dateFields: DATE_FIELDS });
      // Filter on each row's SLA group, then hand back the original rows.
      var views = rows.map(function (r) { var v = Object.create(r); v.sla = slaGroup(r.sla); return v; });
      return gridFilter.filterBy(views, filter, { dateFields: DATE_FIELDS }).map(function (v) { return Object.getPrototypeOf(v); });
    }

    // ---- "Breached only" quick filter (the SLA column's filter, set to Breached) ----
    var BREACHED = { field: 'sla', operator: 'eq', value: 'Breached', ignoreCase: true };

    $ctrl.breachedOnly = function () {
      var entry = gridFilter.findForField($ctrl.filters, 'sla');
      return !!entry && entry.operator === 'eq' && String(entry.value).toLowerCase() === 'breached';
    };

    $ctrl.toggleBreachedOnly = function () {
      $ctrl.cells.sla = { operator: 'eq', value: null };
      $ctrl.onFiltersChange({ filters: gridFilter.setForField($ctrl.filters, 'sla', $ctrl.breachedOnly() ? null : angular.copy(BREACHED)) });
    };

    // The AI sorts by the backend field name; map it onto the grid column that shows it.
    var SORT_ALIASES = { requestCreationTS: 'created' };

    function sortKey() {
      var by = $ctrl.sort && $ctrl.sort.by;
      return SORT_ALIASES[by] || by;
    }

    function refresh() {
      // With server filtering the API already applied the filters to these rows.
      var rows = $ctrl.serverFiltering ? ($ctrl.rows || []) : applyFilters($ctrl.rows || [], $ctrl.filters);
      if ($ctrl.clientPaging) {
        var key = sortKey();
        rows = gridService.sort(rows, { key: key, dir: key ? $ctrl.sort.dir : null }, $ctrl.columns);
        var size = $ctrl.pageSize || 25;
        rows = rows.slice(($ctrl.page - 1) * size, $ctrl.page * size);
      }
      $ctrl.viewRows = rows;
      $ctrl.activeFilterCount = gridFilter.activeCount($ctrl.filters);
    }

    // ---- Sorting: click cycles asc -> desc -> none ----
    $ctrl.sortDir = function (col) {
      return sortKey() === col.key ? $ctrl.sort.dir : null;
    };

    $ctrl.toggleSort = function (col) {
      if (col.sortable === false) return;
      var dir = $ctrl.sortDir(col);
      var next = !dir ? { by: col.key, dir: 'asc' } : dir === 'asc' ? { by: col.key, dir: 'desc' } : { by: null, dir: 'asc' };
      $ctrl.onSortChange({ sort: next });
    };

    $ctrl.ariaSort = function (col) {
      var dir = $ctrl.sortDir(col);
      return dir === 'asc' ? 'ascending' : dir === 'desc' ? 'descending' : 'none';
    };

    // ---- Filter row (KendoReact-style: value editor + operator menu + clear button) ----

    function emit(col, entry) {
      $ctrl.onFiltersChange({ filters: gridFilter.setForField($ctrl.filters, col.key, entry) });
    }

    /** The cell's descriptor, or null while it has no value (value-less operators apply on their own). */
    function cellDescriptor(col) {
      var cell = $ctrl.cells[col.key];
      if (gridFilter.needsValue(cell.operator) && !hasValue(cell.value)) return null;
      var d = { field: col.key, operator: cell.operator, value: gridFilter.needsValue(cell.operator) ? cell.value : null };
      if (col.filterType !== 'date') d.ignoreCase = true;
      return d;
    }

    $ctrl.valueChanged = function (col) {
      emit(col, cellDescriptor(col));
    };

    $ctrl.needsValue = function (col) {
      return gridFilter.needsValue($ctrl.cells[col.key].operator);
    };

    $ctrl.operatorLabel = function (col) {
      var op = $ctrl.cells[col.key].operator;
      var match = col.operators.filter(function (o) { return o.operator === op; })[0];
      return match ? match.text : op;
    };

    $ctrl.toggleMenu = function (col) {
      $ctrl.openMenu = $ctrl.openMenu === col.key ? null : col.key;
      $ctrl.colMenu = null;
    };

    $ctrl.closeMenu = function (col) {
      if ($ctrl.openMenu === col.key) $ctrl.openMenu = null;
    };

    $ctrl.chooseOperator = function (col, operator) {
      var cell = $ctrl.cells[col.key];
      $ctrl.openMenu = null;
      if (cell.group) cell.group = null;
      cell.operator = operator;
      if (!gridFilter.needsValue(operator)) cell.value = null;
      // Applies at once for value-less operators or when a value is already there; otherwise waits for one.
      var d = cellDescriptor(col);
      if (d || gridFilter.findForField($ctrl.filters, col.key)) emit(col, d);
    };

    $ctrl.cellActive = function (col) {
      return !!gridFilter.findForField($ctrl.filters, col.key);
    };

    $ctrl.clearCell = function (col) {
      $ctrl.cells[col.key] = { operator: col.defaultOperator, value: null };
      $ctrl.openMenu = null;
      emit(col, null);
    };

    $ctrl.clearAllFilters = function () {
      $ctrl.openMenu = null;
      $ctrl.onFiltersChange({ filters: gridFilter.emptyFilter() });
    };

    /** "Any of 3" for an OR of equals (the AI's multi-status filter), else "N conditions". */
    $ctrl.groupLabel = function (col) {
      var group = $ctrl.cells[col.key].group;
      var anyOf = group.logic === 'or' && group.filters.every(function (f) { return f.operator === 'eq'; });
      return anyOf ? 'Any of ' + group.filters.length : group.filters.length + ' conditions';
    };

    $ctrl.groupTitle = function (col) {
      var group = $ctrl.cells[col.key].group;
      if (gridFilter.selectedValues(group)) return group.filters.map(function (f) { return f.value; }).join(', ');
      return group.filters.map(function (f) { return describe(col, f); }).join(group.logic === 'or' ? ' or ' : ' and ');
    };

    function describe(col, d) {
      if (gridFilter.isComposite(d)) return '(' + d.filters.map(function (f) { return describe(col, f); }).join(' ' + d.logic + ' ') + ')';
      var op = col.operators.filter(function (o) { return o.operator === d.operator; })[0];
      var text = op ? op.text : d.operator;
      if (!gridFilter.needsValue(d.operator)) return text;
      var value = d.value instanceof Date ? $filter('date')(d.value, 'MM/dd/yyyy') : '"' + d.value + '"';
      return text + ' ' + value;
    }

    // ---- Column menu (KendoReact GridColumnMenuSort + GridColumnMenuFilter / GridColumnMenuCheckboxFilter) ----

    $ctrl.colMenu = null;
    $ctrl.logicOptions = [{ value: 'and', text: 'And' }, { value: 'or', text: 'Or' }];

    $ctrl.toggleColumnMenu = function (col) {
      $ctrl.openMenu = null;
      if ($ctrl.colMenu && $ctrl.colMenu.key === col.key) { $ctrl.colMenu = null; return; }
      var entry = gridFilter.findForField($ctrl.filters, col.key);
      var menu = { key: col.key, search: '' };
      if (col.menu === 'checkbox') {
        var picked = (gridFilter.selectedValues(entry) || []).map(function (v) { return String(v).toLowerCase(); });
        var extra = col.options || (col.key === 'status' ? $ctrl.statusOptions : (($ctrl.valueOptions || {})[col.key] || []));
        // Server filtering: list every value the user may see (counts from one page would mislead).
        var source = $ctrl.serverFiltering || col.options ? [] : $ctrl.rows;
        menu.values = gridFilter.distinctValues(source, col.key, extra.concat(picked.length ? gridFilter.selectedValues(entry) : [])).map(function (v) {
          return { value: v.value, count: v.count, checked: picked.indexOf(String(v.value).toLowerCase()) !== -1 };
        });
        // An existing filter the checkbox list can't show (e.g. "Does not contain") is replaced only on Filter.
        menu.otherFilter = !!entry && !gridFilter.selectedValues(entry);
      } else {
        var c = gridFilter.toConditions(entry, col.defaultOperator);
        menu.logic = c.logic;
        menu.first = c.first;
        menu.second = c.second;
      }
      $ctrl.colMenu = menu;
    };

    $ctrl.closeColumnMenu = function (col) {
      if ($ctrl.colMenu && $ctrl.colMenu.key === col.key) $ctrl.colMenu = null;
    };

    $ctrl.menuSort = function (col, dir) {
      $ctrl.colMenu = null;
      var same = $ctrl.sortDir(col) === dir;
      $ctrl.onSortChange({ sort: same ? { by: null, dir: 'asc' } : { by: col.key, dir: dir } });
    };

    $ctrl.conditionNeedsValue = function (condition) {
      return gridFilter.needsValue(condition.operator);
    };

    $ctrl.visibleValues = function () {
      var q = ($ctrl.colMenu.search || '').trim().toLowerCase();
      return $ctrl.colMenu.values.filter(function (v) { return !q || String(v.value).toLowerCase().indexOf(q) !== -1; });
    };

    $ctrl.checkedCount = function () {
      return $ctrl.colMenu.values.filter(function (v) { return v.checked; }).length;
    };

    $ctrl.allVisibleChecked = function () {
      var list = $ctrl.visibleValues();
      return list.length > 0 && list.every(function (v) { return v.checked; });
    };

    $ctrl.someVisibleChecked = function () {
      var list = $ctrl.visibleValues();
      var n = list.filter(function (v) { return v.checked; }).length;
      return n > 0 && n < list.length;
    };

    $ctrl.toggleAllValues = function () {
      var check = !$ctrl.allVisibleChecked();
      $ctrl.visibleValues().forEach(function (v) { v.checked = check; });
    };

    $ctrl.applyColumnMenu = function (col) {
      var menu = $ctrl.colMenu;
      var entry = col.menu === 'checkbox'
        ? gridFilter.anyOf(col.key, menu.values.filter(function (v) { return v.checked; }).map(function (v) { return v.value; }))
        : gridFilter.fromConditions(col.key, menu.logic, [menu.first, menu.second], { ignoreCase: col.filterType !== 'date' });
      $ctrl.colMenu = null;
      // Dropping a cell's state lets syncCells rebuild it from the new entry.
      $ctrl.cells[col.key] = { operator: col.defaultOperator, value: null };
      emit(col, entry);
    };

    $ctrl.clearColumnMenu = function (col) {
      $ctrl.colMenu = null;
      $ctrl.clearCell(col);
    };

    // ---- Rows ----
    // ---- Time lapse / SLA (computed by the API from Administration > SLA) ----
    var SLA_LABEL = { OnTrack: 'On track', AtRisk: 'At risk', Breached: 'Breached', Met: 'Met', Missed: 'Missed' };

    /** "45m", "6h 10m", "3d 4h" */
    $ctrl.formatAge = function (seconds) {
      if (seconds === null || seconds === undefined) return '—';
      var m = Math.max(0, Math.floor(seconds / 60));
      if (m < 60) return m + 'm';
      var h = Math.floor(m / 60);
      if (h < 24) return h + 'h ' + (m % 60) + 'm';
      return Math.floor(h / 24) + 'd ' + (h % 24) + 'h';
    };
    $ctrl.slaLabel = function (sla) { return (sla && SLA_LABEL[sla.state]) || ''; };
    $ctrl.slaTitle = function (sla) {
      if (!sla) return 'No SLA information';
      var basis = sla.targetDays + ' ' + (sla.businessDays ? 'business' : 'calendar') + '-day SLA';
      var due = $filter('ccDateTime')(sla.dueUtc);
      if (sla.state === 'Met' || sla.state === 'Missed') {
        return SLA_LABEL[sla.state] + ': finished in ' + $ctrl.formatAge(sla.elapsedSeconds) + ' (' + basis + ', due ' + due + ')';
      }
      return SLA_LABEL[sla.state] + ': ' + sla.percentUsed + '% of the ' + basis + ' used · due ' + due;
    };

    // Every row gets a left edge in its status's colour family, matching its badge (see ccStatusTone).
    $ctrl.rowClass = function (row) { return 'row--' + $filter('ccStatusTone')(row.status); };

    $ctrl.badgeClass = function (row) { return 'status-badge--' + $filter('ccStatusTone')(row.status); };

    $ctrl.isSelected = function (row) {
      return !!($ctrl.selectedIds && $ctrl.selectedIds[row.id]);
    };

    $ctrl.allSelected = function () {
      var rows = $ctrl.rows || [];
      return rows.length > 0 && rows.every(function (r) { return $ctrl.selectedIds && $ctrl.selectedIds[r.id]; });
    };

    $ctrl.toggleAll = function () {
      var ids = ($ctrl.rows || []).map(function (r) { return r.id; });
      $ctrl.onToggleAll({ ids: ids, checked: !$ctrl.allSelected() });
    };

    $ctrl.trackRow = function (row, index) {
      return row.id !== undefined && row.id !== null ? row.id : '$' + index;
    };

    // ---- Paging ----
    $ctrl.pageChange = function (page) {
      $ctrl.onPageChange({ page: page, size: $ctrl.pageSize });
    };

    // ---- Keyboard shortcuts (from the shell): b = Breached only, [ / ] = previous / next page ----
    $scope.$on('shortcut:breachedOnly', function () { $ctrl.toggleBreachedOnly(); });
    $scope.$on('shortcut:previousPage', function () { if ($ctrl.pageSizeOptions && $ctrl.page > 1) $ctrl.pageChange($ctrl.page - 1); });
    $scope.$on('shortcut:nextPage', function () {
      var last = Math.max(1, Math.ceil(($ctrl.total || 0) / ($ctrl.pageSize || 1)));
      if ($ctrl.pageSizeOptions && $ctrl.page < last) $ctrl.pageChange($ctrl.page + 1);
    });

    $ctrl.pageSizeChange = function (size) {
      $ctrl.onPageChange({ page: 1, size: size });
    };
  }
})();
