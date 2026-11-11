/* Unit tests for the Kendo-style pager's page blocks. */
'use strict';

var assert = require('assert');
var h = require('./harness');

h.load('app/components/pagination/pagination.component.js');

h.suite('pagination');

h.test('pages come in blocks with "..." jumping to the neighbouring block', function () {
  var build = h.componentController('pagination').buildItems;
  var show = function (items) { return items.map(function (i) { return i.gap ? '...' + i.page : i.page; }).join(' '); };
  assert.strictEqual(show(build(1, 8, 4)), '1 2 3 4 ...5');
  assert.strictEqual(show(build(4, 8, 4)), '1 2 3 4 ...5');
  assert.strictEqual(show(build(6, 8, 4)), '...4 5 6 7 8');
  assert.strictEqual(show(build(6, 13, 4)), '...4 5 6 7 8 ...9');
  assert.strictEqual(show(build(1, 3, 4)), '1 2 3');
  assert.strictEqual(show(build(1, 1, 4)), '1');
});
