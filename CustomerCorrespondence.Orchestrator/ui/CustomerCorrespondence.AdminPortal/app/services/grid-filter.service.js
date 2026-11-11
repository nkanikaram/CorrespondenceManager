(function () {
  'use strict';

  /**
   * Client-side filtering modelled on KendoReact's Grid filtering (kendo-data-query):
   *
   *   CompositeFilterDescriptor  { logic: 'and' | 'or', filters: [descriptor, …] }
   *   FilterDescriptor           { field, operator, value, ignoreCase }
   *
   * Composites nest, so "status is Failed or Processing" inside an AND is
   * { logic: 'and', filters: [{ logic: 'or', filters: [{ field: 'status', operator: 'eq', value: 'Failed' }, …] }] }.
   *
   * Operators and labels match Kendo's filter-row defaults; the first operator of each
   * type is that type's default (text: contains, numeric/date/boolean: eq).
   *
   * One deliberate difference: fields listed in `options.dateFields` compare by calendar
   * day, so "Created is equal to 05/14/2025" matches every record created that day.
   * (Kendo compares exact timestamps, which a date-only picker can never match.)
   */
  angular.module('adminPortal').factory('gridFilter', function () {
    var OPERATORS = {
      text: [
        { operator: 'contains', text: 'Contains' },
        { operator: 'doesnotcontain', text: 'Does not contain' },
        { operator: 'eq', text: 'Is equal to' },
        { operator: 'neq', text: 'Is not equal to' },
        { operator: 'startswith', text: 'Starts with' },
        { operator: 'endswith', text: 'Ends with' },
        { operator: 'isnull', text: 'Is null' },
        { operator: 'isnotnull', text: 'Is not null' },
        { operator: 'isempty', text: 'Is empty' },
        { operator: 'isnotempty', text: 'Is not empty' }
      ],
      numeric: [
        { operator: 'eq', text: 'Is equal to' },
        { operator: 'neq', text: 'Is not equal to' },
        { operator: 'gte', text: 'Is greater than or equal to' },
        { operator: 'gt', text: 'Is greater than' },
        { operator: 'lte', text: 'Is less than or equal to' },
        { operator: 'lt', text: 'Is less than' },
        { operator: 'isnull', text: 'Is null' },
        { operator: 'isnotnull', text: 'Is not null' }
      ],
      date: [
        { operator: 'eq', text: 'Is equal to' },
        { operator: 'neq', text: 'Is not equal to' },
        { operator: 'gte', text: 'Is after or equal to' },
        { operator: 'gt', text: 'Is after' },
        { operator: 'lte', text: 'Is before or equal to' },
        { operator: 'lt', text: 'Is before' },
        { operator: 'isnull', text: 'Is null' },
        { operator: 'isnotnull', text: 'Is not null' }
      ],
      boolean: [
        { operator: 'eq', text: 'Is equal to' }
      ]
    };

    var NO_VALUE = { isnull: true, isnotnull: true, isempty: true, isnotempty: true };

    function operatorsFor(type) {
      return OPERATORS[type] || OPERATORS.text;
    }

    function defaultOperator(type) {
      return operatorsFor(type)[0].operator;
    }

    /** isnull / isnotnull / isempty / isnotempty apply without a value. */
    function needsValue(operator) {
      return !NO_VALUE[operator];
    }

    function isComposite(f) {
      return !!f && Array.isArray(f.filters);
    }

    function isNil(v) {
      return v === null || v === undefined;
    }

    function hasValue(v) {
      return !isNil(v) && !(typeof v === 'string' && v.trim() === '') && !(typeof v === 'number' && isNaN(v));
    }

    /** A descriptor that would filter something (value-less operators always count). */
    function isActive(d) {
      if (isComposite(d)) return d.filters.some(isActive);
      return !!d && !!d.operator && (!needsValue(d.operator) || hasValue(d.value));
    }

    function toDate(v) {
      if (isNil(v) || v === '') return null;
      var d = v instanceof Date ? v : new Date(v);
      return isNaN(d.getTime()) ? null : d;
    }

    function startOfDay(d) {
      return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
    }

    function getField(item, field) {
      if (!field) return item;
      return String(field).split('.').reduce(function (o, k) { return isNil(o) ? undefined : o[k]; }, item);
    }

    function compileDescriptor(d, dateFields) {
      var op = d.operator;
      var byDay = !!dateFields[d.field];
      var wanted = d.value;
      var ignoreCase = d.ignoreCase !== false;
      var isDate = byDay || wanted instanceof Date;

      if (op === 'isnull') return function (item) { return isNil(getField(item, d.field)); };
      if (op === 'isnotnull') return function (item) { return !isNil(getField(item, d.field)); };
      if (op === 'isempty') return function (item) { return getField(item, d.field) === ''; };
      if (op === 'isnotempty') return function (item) { return getField(item, d.field) !== ''; };

      if (isDate) {
        var w = toDate(wanted);
        if (!w) return function () { return true; };
        var target = byDay ? startOfDay(w) : w.getTime();
        return function (item) {
          var v = toDate(getField(item, d.field));
          if (!v) return op === 'neq';
          var t = byDay ? startOfDay(v) : v.getTime();
          switch (op) {
            case 'eq': return t === target;
            case 'neq': return t !== target;
            case 'gt': return t > target;
            case 'gte': return t >= target;
            case 'lt': return t < target;
            case 'lte': return t <= target;
            default: return true;
          }
        };
      }

      if (typeof wanted === 'number' || typeof wanted === 'boolean') {
        return function (item) {
          var v = getField(item, d.field);
          switch (op) {
            case 'eq': return v === wanted;
            case 'neq': return v !== wanted;
            case 'gt': return !isNil(v) && v > wanted;
            case 'gte': return !isNil(v) && v >= wanted;
            case 'lt': return !isNil(v) && v < wanted;
            case 'lte': return !isNil(v) && v <= wanted;
            default: return true;
          }
        };
      }

      var needle = isNil(wanted) ? '' : String(wanted);
      if (ignoreCase) needle = needle.toLowerCase();
      return function (item) {
        var raw = getField(item, d.field);
        var v = isNil(raw) ? '' : String(raw);
        if (ignoreCase) v = v.toLowerCase();
        switch (op) {
          case 'contains': return v.indexOf(needle) !== -1;
          case 'doesnotcontain': return v.indexOf(needle) === -1;
          case 'eq': return !isNil(raw) && v === needle;
          case 'neq': return isNil(raw) || v !== needle;
          case 'startswith': return v.indexOf(needle) === 0;
          case 'endswith': return needle.length <= v.length && v.lastIndexOf(needle) === v.length - needle.length;
          case 'gt': return v > needle;
          case 'gte': return v >= needle;
          case 'lt': return v < needle;
          case 'lte': return v <= needle;
          default: return true;
        }
      };
    }

    /** Builds a predicate; inactive descriptors (no value yet) are skipped, as in Kendo's filter row. */
    function compile(filter, dateFields) {
      if (!isActive(filter)) return function () { return true; };
      if (!isComposite(filter)) return compileDescriptor(filter, dateFields);
      var parts = filter.filters.filter(isActive).map(function (f) { return compile(f, dateFields); });
      if (filter.logic === 'or') return function (item) { return parts.some(function (p) { return p(item); }); };
      return function (item) { return parts.every(function (p) { return p(item); }); };
    }

    /** Kendo's filterBy(data, descriptor). options.dateFields: ['created', …] compares those fields by day. */
    function filterBy(data, filter, options) {
      if (!data || !isActive(filter)) return data || [];
      var dateFields = {};
      ((options && options.dateFields) || []).forEach(function (f) { dateFields[f] = true; });
      return data.filter(compile(filter, dateFields));
    }

    // ---- Helpers for filter-row cells: one top-level entry per field ----

    function emptyFilter() {
      return { logic: 'and', filters: [] };
    }

    /** True when `entry` filters only on `field` (a descriptor, or a composite of descriptors on it). */
    function isForField(entry, field) {
      if (isComposite(entry)) return entry.filters.length > 0 && entry.filters.every(function (f) { return isForField(f, field); });
      return !!entry && entry.field === field;
    }

    /** The top-level entry for `field`, or null. */
    function findForField(filter, field) {
      if (!isComposite(filter)) return null;
      for (var i = 0; i < filter.filters.length; i++) {
        if (isForField(filter.filters[i], field)) return filter.filters[i];
      }
      return null;
    }

    /** A new composite with `field`'s entry replaced by `entry` (null removes it). Never mutates. */
    function setForField(filter, field, entry) {
      var base = isComposite(filter) ? filter : emptyFilter();
      var rest = base.filters.filter(function (f) { return !isForField(f, field); });
      if (entry && isActive(entry)) rest.push(entry);
      return { logic: base.logic || 'and', filters: rest };
    }

    /** "status is Failed or Processing" as an OR composite (a single value stays a plain descriptor). */
    function anyOf(field, values) {
      var list = (values || []).filter(hasValue);
      if (!list.length) return null;
      if (list.length === 1) return { field: field, operator: 'eq', value: list[0], ignoreCase: true };
      return {
        logic: 'or',
        filters: list.map(function (v) { return { field: field, operator: 'eq', value: v, ignoreCase: true }; })
      };
    }

    // ---- Helpers for the column-menu filters (KendoReact GridColumnMenuFilter / CheckboxFilter) ----

    /**
     * A field's entry as up to two conditions joined by and/or, the shape of Kendo's filter menu.
     * A nested group with more than two conditions keeps only the first two.
     */
    function toConditions(entry, defaultOp) {
      var blank = function () { return { operator: defaultOp, value: null }; };
      if (!entry) return { logic: 'and', first: blank(), second: blank() };
      var list = isComposite(entry) ? entry.filters : [entry];
      var pick = function (d) { return d && !isComposite(d) ? { operator: d.operator, value: d.value } : blank(); };
      return { logic: isComposite(entry) ? (entry.logic || 'and') : 'and', first: pick(list[0]), second: pick(list[1]) };
    }

    /** Back from the menu's two conditions: null (nothing set), one descriptor, or an and/or group. */
    function fromConditions(field, logic, conditions, options) {
      var ignoreCase = !(options && options.ignoreCase === false);
      var list = conditions
        .map(function (c) {
          var d = { field: field, operator: c.operator, value: needsValue(c.operator) ? c.value : null };
          if (ignoreCase) d.ignoreCase = true;
          return d;
        })
        .filter(isActive);
      if (!list.length) return null;
      if (list.length === 1) return list[0];
      return { logic: logic === 'or' ? 'or' : 'and', filters: list };
    }

    /** The values an entry selects when it is a checkbox-style "any of" (eq / OR of eqs), else null. */
    function selectedValues(entry) {
      if (!entry) return null;
      var list = isComposite(entry) ? (entry.logic === 'or' ? entry.filters : null) : [entry];
      if (!list || !list.every(function (d) { return !isComposite(d) && d.operator === 'eq'; })) return null;
      return list.map(function (d) { return d.value; });
    }

    /**
     * Distinct values of `field` for the checkbox list, merged case-insensitively ("Mail" and "MAIL"
     * are one entry, as an ignoreCase "eq" would match both). Blanks are left out. Sorted by text.
     */
    function distinctValues(data, field, extra) {
      var seen = {};
      var out = [];
      function add(v, count) {
        if (!hasValue(v)) return;
        var key = String(v).toLowerCase();
        if (!seen[key]) { seen[key] = { value: v, count: 0 }; out.push(seen[key]); }
        seen[key].count += count;
      }
      (extra || []).forEach(function (v) { add(v, 0); });
      (data || []).forEach(function (item) { add(getField(item, field), 1); });
      return out.sort(function (a, b) { return String(a.value).localeCompare(String(b.value)); });
    }

    // ---- Server round-trip and display ----

    function pad2(n) { return (n < 10 ? '0' : '') + n; }

    /**
     * The filter as sent to the API: only active conditions, and Date values as local 'yyyy-MM-dd'
     * (the API compares dates by calendar day). Returns null when nothing is active.
     */
    function toWire(filter) {
      function walk(node) {
        if (isComposite(node)) {
          var parts = node.filters.filter(isActive).map(walk);
          return parts.length ? { logic: node.logic === 'or' ? 'or' : 'and', filters: parts } : null;
        }
        var d = { field: node.field, operator: node.operator };
        if (needsValue(node.operator)) {
          d.value = node.value instanceof Date
            ? node.value.getFullYear() + '-' + pad2(node.value.getMonth() + 1) + '-' + pad2(node.value.getDate())
            : node.value;
        }
        if (node.ignoreCase !== undefined) d.ignoreCase = node.ignoreCase;
        return d;
      }
      return isActive(filter) ? walk(filter) : null;
    }

    function operatorText(type, operator) {
      var all = operatorsFor(type).concat(OPERATORS.text);
      var match = all.filter(function (o) { return o.operator === operator; })[0];
      return match ? match.text : operator;
    }

    function valueText(value) {
      if (value instanceof Date) return pad2(value.getMonth() + 1) + '/' + pad2(value.getDate()) + '/' + value.getFullYear();
      return '"' + value + '"';
    }

    /**
     * One-line description of a field's entry, e.g. 'Customer contains "life"',
     * 'Status: Failed, Processing', 'Created is after 11/03/2026 or is null'.
     * column: { label, filterType ('text' | 'date' | 'select') }.
     */
    function describe(entry, column) {
      var type = column.filterType === 'select' ? 'text' : column.filterType;
      var picked = selectedValues(entry);
      if (picked) return column.label + ': ' + picked.join(', ');
      function one(d) {
        if (isComposite(d)) return '(' + d.filters.map(one).join(d.logic === 'or' ? ' or ' : ' and ') + ')';
        var text = operatorText(type, d.operator).toLowerCase();
        return needsValue(d.operator) ? text + ' ' + valueText(d.value) : text;
      }
      var list = isComposite(entry) ? entry.filters : [entry];
      return column.label + ' ' + list.map(one).join(isComposite(entry) && entry.logic === 'or' ? ' or ' : ' and ');
    }

    function activeCount(filter) {
      return isComposite(filter) ? filter.filters.filter(isActive).length : (isActive(filter) ? 1 : 0);
    }

    return {
      OPERATORS: OPERATORS,
      operatorsFor: operatorsFor,
      defaultOperator: defaultOperator,
      needsValue: needsValue,
      isComposite: isComposite,
      isActive: isActive,
      filterBy: filterBy,
      emptyFilter: emptyFilter,
      findForField: findForField,
      setForField: setForField,
      anyOf: anyOf,
      toConditions: toConditions,
      fromConditions: fromConditions,
      selectedValues: selectedValues,
      distinctValues: distinctValues,
      toWire: toWire,
      describe: describe,
      activeCount: activeCount
    };
  });
})();
