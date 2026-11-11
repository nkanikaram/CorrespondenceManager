/* Unit tests for gridService (client-side sort used by the grid's top-N mode). */
'use strict';

var assert = require('assert');
var h = require('./harness');

h.load('app/services/grid.service.js');
var grid = h.get('gridService');

var columns = [
  { key: 'id', type: 'number', filterable: true, filterType: 'text' },
  { key: 'name', filterable: true, filterType: 'text' },
  { key: 'role', filterable: true, filterType: 'select' },
  { key: 'joined', type: 'date', searchable: false }
];
var rows = [
  { id: 3, name: 'Charlie', role: 'Admin', joined: '2024-03-01' },
  { id: 10, name: 'alice', role: 'Viewer', joined: '2023-01-15' },
  { id: 2, name: 'Bob', role: 'Admin', joined: null },
  { id: 25, name: 'Dana', role: 'Editor', joined: '2025-07-20' }
];
var ids = function (list) { return list.map(function (r) { return r.id; }); };

h.suite('gridService');

h.test('search is case-insensitive across searchable columns', function () {
  assert.deepStrictEqual(ids(grid.search(rows, 'ALI', columns)), [10]);
  assert.deepStrictEqual(ids(grid.search(rows, 'admin', columns)), [3, 2]);
  assert.deepStrictEqual(ids(grid.search(rows, '2024', columns)), [], 'non-searchable column ignored');
  assert.strictEqual(grid.search(rows, '  ', columns), rows);
});

h.test('text filters use contains, select filters use exact match, combined with AND', function () {
  assert.deepStrictEqual(ids(grid.filter(rows, { name: 'a' }, columns)), [3, 10, 25]);
  assert.deepStrictEqual(ids(grid.filter(rows, { role: 'Admin' }, columns)), [3, 2]);
  assert.deepStrictEqual(ids(grid.filter(rows, { role: 'Admin', name: 'b' }, columns)), [2]);
  assert.deepStrictEqual(ids(grid.filter(rows, { role: '' }, columns)), [3, 10, 2, 25]);
});

h.test('sort is type-aware and keeps empty values last', function () {
  assert.deepStrictEqual(ids(grid.sort(rows, { key: 'id', dir: 'asc' }, columns)), [2, 3, 10, 25]);
  assert.deepStrictEqual(ids(grid.sort(rows, { key: 'id', dir: 'desc' }, columns)), [25, 10, 3, 2]);
  assert.deepStrictEqual(ids(grid.sort(rows, { key: 'name', dir: 'asc' }, columns)), [10, 2, 3, 25]);
  assert.deepStrictEqual(ids(grid.sort(rows, { key: 'joined', dir: 'asc' }, columns)), [10, 3, 25, 2]);
  assert.deepStrictEqual(ids(grid.sort(rows, { key: 'joined', dir: 'desc' }, columns)), [25, 3, 10, 2]);
  assert.deepStrictEqual(ids(rows), [3, 10, 2, 25], 'input not mutated');
});

h.test('paginate slices and clamps the page', function () {
  var many = Array.from({ length: 23 }, function (_, i) { return { id: i + 1 }; });
  var p = grid.paginate(many, 3, 10);
  assert.deepStrictEqual(ids(p.rows), [21, 22, 23]);
  assert.strictEqual(p.totalPages, 3);
  assert.strictEqual(grid.paginate(many, 99, 10).page, 3);
  assert.strictEqual(grid.paginate([], 2, 10).page, 1);
});

h.test('distinctValues returns sorted unique non-empty values', function () {
  assert.deepStrictEqual(grid.distinctValues(rows, 'role'), ['Admin', 'Editor', 'Viewer']);
});
