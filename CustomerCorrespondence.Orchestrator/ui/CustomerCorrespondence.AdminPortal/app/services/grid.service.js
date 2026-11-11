(function () {
  'use strict';

  /**
   * Pure data operations used by the data grid: global search, per-column
   * filters, sorting and pagination. Kept free of DOM/scope concerns so the
   * same pipeline could be moved server-side later.
   */
  angular.module('adminPortal').factory('gridService', gridService);

  function gridService() {
    return {
      search: search,
      filter: filter,
      sort: sort,
      paginate: paginate,
      process: process,
      distinctValues: distinctValues
    };

    function normalize(value) {
      return value === null || value === undefined ? '' : String(value).toLowerCase();
    }

    /** Keeps rows where any searchable column contains the term. */
    function search(rows, term, columns) {
      var needle = normalize(term).trim();
      if (!needle) return rows;

      var keys = columns
        .filter(function (col) { return col.searchable !== false; })
        .map(function (col) { return col.key; });

      return rows.filter(function (row) {
        return keys.some(function (key) {
          return normalize(row[key]).indexOf(needle) !== -1;
        });
      });
    }

    /**
     * Applies column filters. `filters` maps column key -> filter value.
     * Text filters match by "contains"; select filters match exactly.
     */
    function filter(rows, filters, columns) {
      var active = columns.filter(function (col) {
        var value = filters[col.key];
        return col.filterable && value !== undefined && value !== null && value !== '';
      });
      if (!active.length) return rows;

      return rows.filter(function (row) {
        return active.every(function (col) {
          var wanted = filters[col.key];
          if (col.filterType === 'select') {
            return String(row[col.key]) === String(wanted);
          }
          return normalize(row[col.key]).indexOf(normalize(wanted).trim()) !== -1;
        });
      });
    }

    function compare(a, b, type) {
      var aEmpty = a === null || a === undefined || a === '';
      var bEmpty = b === null || b === undefined || b === '';
      if (aEmpty || bEmpty) return aEmpty === bEmpty ? 0 : (aEmpty ? 1 : -1);

      if (type === 'number') return Number(a) - Number(b);
      if (type === 'date') return new Date(a).getTime() - new Date(b).getTime();
      return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' });
    }

    /** Stable sort by `sortState = { key, dir: 'asc' | 'desc' }`. Empty values always sort last. */
    function sort(rows, sortState, columns) {
      if (!sortState || !sortState.key || !sortState.dir) return rows;

      var column = columns.filter(function (col) { return col.key === sortState.key; })[0] || {};
      var direction = sortState.dir === 'desc' ? -1 : 1;

      return rows
        .map(function (row, index) { return { row: row, index: index }; })
        .sort(function (x, y) {
          var a = x.row[sortState.key];
          var b = y.row[sortState.key];
          var aEmpty = a === null || a === undefined || a === '';
          var bEmpty = b === null || b === undefined || b === '';
          // Keep empties at the bottom regardless of direction.
          if (aEmpty || bEmpty) return aEmpty === bEmpty ? x.index - y.index : (aEmpty ? 1 : -1);
          return (compare(a, b, column.type) * direction) || (x.index - y.index);
        })
        .map(function (item) { return item.row; });
    }

    /** Returns the requested page, clamped into range. */
    function paginate(rows, page, pageSize) {
      var totalPages = Math.max(1, Math.ceil(rows.length / pageSize));
      var current = Math.min(Math.max(1, page), totalPages);
      var start = (current - 1) * pageSize;
      return {
        rows: rows.slice(start, start + pageSize),
        page: current,
        totalPages: totalPages
      };
    }

    /** Full pipeline: search -> column filters -> sort -> paginate. */
    function process(rows, options) {
      var columns = options.columns || [];
      var result = search(rows, options.searchText, columns);
      result = filter(result, options.filters || {}, columns);
      result = sort(result, options.sortState, columns);

      var page = paginate(result, options.page || 1, options.pageSize || 10);
      return {
        rows: page.rows,
        page: page.page,
        totalPages: page.totalPages,
        total: result.length
      };
    }

    /** Sorted unique values of a column, used to populate select filters. */
    function distinctValues(rows, key) {
      var seen = {};
      rows.forEach(function (row) {
        var value = row[key];
        if (value !== null && value !== undefined && value !== '') seen[value] = true;
      });
      return Object.keys(seen).sort(function (a, b) {
        return a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' });
      });
    }
  }
})();
