/* Unit tests for gridFilter (KendoReact-style filter descriptors and filterBy). */
'use strict';

var assert = require('assert');
var h = require('./harness');

h.load('app/services/grid-filter.service.js');
var gf = h.get('gridFilter');

var rows = [
  { id: 1, name: 'Alice Smith', status: 'Completed', amount: 10, created: '2025-05-14T08:30:00' },
  { id: 2, name: 'bob jones', status: 'Failed', amount: 25, created: '2025-05-14T23:10:00' },
  { id: 3, name: 'Carol', status: 'Processing', amount: null, created: '2025-05-15T00:05:00' },
  { id: 4, name: '', status: 'failed', amount: 5, created: null },
  { id: 5, name: null, status: 'Submitted', amount: 40, created: '2025-04-01T12:00:00' }
];
var ids = function (list) { return list.map(function (r) { return r.id; }); };
var by = function (d, opts) { return ids(gf.filterBy(rows, { logic: 'and', filters: [d] }, opts)); };

h.suite('gridFilter');

h.test('operator lists and defaults match the Kendo filter row', function () {
  assert.strictEqual(gf.defaultOperator('text'), 'contains');
  assert.strictEqual(gf.defaultOperator('numeric'), 'eq');
  assert.strictEqual(gf.defaultOperator('date'), 'eq');
  assert.strictEqual(gf.defaultOperator('boolean'), 'eq');
  assert.deepStrictEqual(gf.operatorsFor('date').map(function (o) { return o.text; }).slice(2, 6),
    ['Is after or equal to', 'Is after', 'Is before or equal to', 'Is before']);
  assert.strictEqual(gf.needsValue('contains'), true);
  assert.strictEqual(gf.needsValue('isempty'), false);
});

h.test('text operators are case-insensitive by default', function () {
  assert.deepStrictEqual(by({ field: 'name', operator: 'contains', value: 'SMITH' }), [1]);
  assert.deepStrictEqual(by({ field: 'name', operator: 'doesnotcontain', value: 'o' }), [1, 4, 5]);
  assert.deepStrictEqual(by({ field: 'status', operator: 'eq', value: 'FAILED' }), [2, 4]);
  assert.deepStrictEqual(by({ field: 'status', operator: 'neq', value: 'failed' }), [1, 3, 5]);
  assert.deepStrictEqual(by({ field: 'name', operator: 'startswith', value: 'b' }), [2]);
  assert.deepStrictEqual(by({ field: 'name', operator: 'endswith', value: 'ROL' }), [3]);
});

h.test('ignoreCase: false matches case exactly', function () {
  assert.deepStrictEqual(by({ field: 'status', operator: 'eq', value: 'Failed', ignoreCase: false }), [2]);
});

h.test('null / empty operators need no value', function () {
  assert.deepStrictEqual(by({ field: 'name', operator: 'isnull' }), [5]);
  assert.deepStrictEqual(by({ field: 'name', operator: 'isnotnull' }), [1, 2, 3, 4]);
  assert.deepStrictEqual(by({ field: 'name', operator: 'isempty' }), [4]);
  assert.deepStrictEqual(by({ field: 'name', operator: 'isnotempty' }), [1, 2, 3, 5]);
});

h.test('numeric comparisons skip nulls', function () {
  assert.deepStrictEqual(by({ field: 'amount', operator: 'gte', value: 10 }), [1, 2, 5]);
  assert.deepStrictEqual(by({ field: 'amount', operator: 'lt', value: 10 }), [4]);
  assert.deepStrictEqual(by({ field: 'amount', operator: 'neq', value: 10 }), [2, 3, 4, 5]);
});

h.test('date fields compare by calendar day', function () {
  var day = new Date(2025, 4, 14);
  var opts = { dateFields: ['created'] };
  assert.deepStrictEqual(by({ field: 'created', operator: 'eq', value: day }, opts), [1, 2]);
  assert.deepStrictEqual(by({ field: 'created', operator: 'gt', value: day }, opts), [3]);
  assert.deepStrictEqual(by({ field: 'created', operator: 'gte', value: day }, opts), [1, 2, 3]);
  assert.deepStrictEqual(by({ field: 'created', operator: 'lt', value: day }, opts), [5]);
  assert.deepStrictEqual(by({ field: 'created', operator: 'lte', value: day }, opts), [1, 2, 5]);
  assert.deepStrictEqual(by({ field: 'created', operator: 'neq', value: day }, opts), [3, 4, 5]);
  assert.deepStrictEqual(by({ field: 'created', operator: 'isnull' }, opts), [4]);
});

h.test('without dateFields a Date value compares exact timestamps (Kendo default)', function () {
  assert.deepStrictEqual(by({ field: 'created', operator: 'eq', value: new Date(2025, 4, 14) }), []);
  assert.deepStrictEqual(by({ field: 'created', operator: 'eq', value: new Date(2025, 4, 14, 8, 30) }), [1]);
});

h.test('composites nest with and / or logic', function () {
  var f = {
    logic: 'and',
    filters: [
      { logic: 'or', filters: [{ field: 'status', operator: 'eq', value: 'failed' }, { field: 'status', operator: 'eq', value: 'processing' }] },
      { field: 'amount', operator: 'isnotnull' }
    ]
  };
  assert.deepStrictEqual(ids(gf.filterBy(rows, f)), [2, 4]);
});

h.test('descriptors without a value are ignored, like an empty filter cell', function () {
  assert.strictEqual(gf.filterBy(rows, { logic: 'and', filters: [{ field: 'name', operator: 'contains', value: '  ' }] }), rows);
  assert.strictEqual(gf.filterBy(rows, null), rows);
  assert.strictEqual(gf.isActive({ logic: 'and', filters: [] }), false);
});

h.test('setForField / findForField manage one entry per column without mutating', function () {
  var base = gf.emptyFilter();
  var a = gf.setForField(base, 'name', { field: 'name', operator: 'contains', value: 'a' });
  var b = gf.setForField(a, 'status', gf.anyOf('status', ['Failed', 'Processing']));
  assert.deepStrictEqual(base.filters, []);
  assert.strictEqual(b.filters.length, 2);
  assert.strictEqual(gf.findForField(b, 'status').logic, 'or');
  assert.strictEqual(gf.findForField(b, 'name').value, 'a');
  var c = gf.setForField(b, 'name', { field: 'name', operator: 'eq', value: 'Carol' });
  assert.strictEqual(c.filters.length, 2);
  assert.strictEqual(gf.findForField(c, 'name').operator, 'eq');
  var d = gf.setForField(c, 'status', null);
  assert.strictEqual(gf.findForField(d, 'status'), null);
  assert.strictEqual(gf.activeCount(d), 1);
});

h.test('anyOf: one value is a plain eq, several are an OR group', function () {
  assert.deepStrictEqual(gf.anyOf('status', ['Failed']), { field: 'status', operator: 'eq', value: 'Failed', ignoreCase: true });
  assert.strictEqual(gf.anyOf('status', []), null);
  assert.strictEqual(gf.anyOf('status', ['A', 'B']).filters.length, 2);
});

h.test('toConditions / fromConditions round-trip the two-condition column menu', function () {
  var empty = gf.toConditions(null, 'contains');
  assert.deepStrictEqual(empty, { logic: 'and', first: { operator: 'contains', value: null }, second: { operator: 'contains', value: null } });

  var one = gf.fromConditions('name', 'or', [{ operator: 'startswith', value: 'b' }, { operator: 'contains', value: '' }]);
  assert.deepStrictEqual(one, { field: 'name', operator: 'startswith', value: 'b', ignoreCase: true }, 'blank second condition dropped');

  var two = gf.fromConditions('name', 'or', [{ operator: 'startswith', value: 'b' }, { operator: 'isnull', value: 'ignored' }]);
  assert.strictEqual(two.logic, 'or');
  assert.deepStrictEqual(two.filters[1], { field: 'name', operator: 'isnull', value: null, ignoreCase: true });
  assert.deepStrictEqual(ids(gf.filterBy(rows, gf.setForField(null, 'name', two))), [2, 5]);

  var back = gf.toConditions(two, 'contains');
  assert.strictEqual(back.logic, 'or');
  assert.strictEqual(back.first.operator, 'startswith');
  assert.strictEqual(back.second.operator, 'isnull');
  assert.strictEqual(gf.fromConditions('name', 'and', [{ operator: 'contains', value: ' ' }, { operator: 'eq', value: null }]), null);
});

h.test('selectedValues reads checkbox-style filters only', function () {
  assert.deepStrictEqual(gf.selectedValues(gf.anyOf('status', ['A', 'B'])), ['A', 'B']);
  assert.deepStrictEqual(gf.selectedValues({ field: 'status', operator: 'eq', value: 'A' }), ['A']);
  assert.strictEqual(gf.selectedValues({ field: 'status', operator: 'neq', value: 'A' }), null);
  assert.strictEqual(gf.selectedValues({ logic: 'and', filters: [{ field: 's', operator: 'eq', value: 'A' }, { field: 's', operator: 'eq', value: 'B' }] }), null);
  assert.strictEqual(gf.selectedValues(null), null);
});

h.test('distinctValues merges case, skips blanks, counts and sorts', function () {
  var list = gf.distinctValues(rows, 'status', ['Awaiting']);
  assert.deepStrictEqual(list.map(function (v) { return [v.value, v.count]; }),
    [['Awaiting', 0], ['Completed', 1], ['Failed', 2], ['Processing', 1], ['Submitted', 1]]);
  assert.deepStrictEqual(gf.distinctValues(rows, 'name').length, 3);
});
