/* Tests for server-bound grid filters, filter chips, and the mock API's access rules (parity with the C# evaluator). */
'use strict';

var assert = require('assert');
var h = require('./harness');

h.load('app/services/grid-filter.service.js');
h.load('app/services/search-query.service.js');
var gf = h.get('gridFilter');
var searchQuery = h.get('searchQuery');
var auth = require('../tools/mock-auth');

h.suite('server filters & chips');

h.test('toWire sends only active conditions, with dates as local yyyy-MM-dd', function () {
  var filter = {
    logic: 'and',
    filters: [
      { field: 'created', operator: 'gte', value: new Date(2026, 10, 3) },
      { field: 'docId', operator: 'contains', value: '  ' },
      { field: 'claimNumber', operator: 'isnull', value: 'ignored' },
      { logic: 'or', filters: [{ field: 'status', operator: 'eq', value: 'Failed', ignoreCase: true }] }
    ]
  };
  assert.deepStrictEqual(gf.toWire(filter), {
    logic: 'and',
    filters: [
      { field: 'created', operator: 'gte', value: '2026-11-03' },
      { field: 'claimNumber', operator: 'isnull' },
      { logic: 'or', filters: [{ field: 'status', operator: 'eq', value: 'Failed', ignoreCase: true }] }
    ]
  });
  assert.strictEqual(gf.toWire(gf.emptyFilter()), null);
  assert.strictEqual(gf.toWire(null), null);
});

h.test('describe renders a column entry as one readable line', function () {
  var customer = { label: 'Customer', filterType: 'text' };
  var created = { label: 'Created', filterType: 'date' };
  var status = { label: 'Status', filterType: 'select' };
  assert.strictEqual(gf.describe({ field: 'customer', operator: 'contains', value: 'life' }, customer), 'Customer contains "life"');
  assert.strictEqual(gf.describe(gf.anyOf('status', ['Failed', 'Processing']), status), 'Status: Failed, Processing');
  assert.strictEqual(gf.describe({
    logic: 'or',
    filters: [{ field: 'created', operator: 'gt', value: new Date(2026, 10, 3) }, { field: 'created', operator: 'isnull' }]
  }, created), 'Created is after 11/03/2026 or is null');
});

h.test('paramChips groups related AI params and maps each chip to the keys it removes', function () {
  var chips = searchQuery.paramChips({
    statuses: ['Failed', 'New'], customer: 'Life Services', fromDate: '2026-10-01', toDate: '2026-10-31',
    pageSize: 5, sortBy: 'requestCreationTS', sortDirection: 'desc'
  });
  assert.deepStrictEqual(chips.map(function (c) { return c.label; }), [
    'Top 5 by requestCreationTS desc', 'Status: Failed, New', 'Customer: Life Services', 'Created 2026-10-01 to 2026-10-31'
  ]);
  assert.deepStrictEqual(chips[0].keys, ['pageSize', 'sortBy', 'sortDirection']);
  assert.deepStrictEqual(chips[3].keys, ['fromDate', 'toDate']);
  assert.deepStrictEqual(searchQuery.paramChips(null), []);
});

h.suite('mock access rules (same cases as AccessPolicyEvaluatorTests.cs)');

function user(roles, attributes) {
  return { isActive: true, roles: roles, attributes: attributes || {} };
}

h.test('RBAC: a permission must come from a role', function () {
  var viewer = user(['Viewer']);
  assert.strictEqual(auth.check(viewer, 'correspondence.read', {}).allowed, true);
  assert.strictEqual(auth.check(viewer, 'correspondence.export', {}).allowed, false);
  assert.strictEqual(auth.check(user(['Operator'], {}), 'admin.users.manage', {}).allowed, false);
});

h.test('seeded ABAC policies: status, customer scope, PII clearance, export limit', function () {
  var op = user(['Operator'], { 'scope.customer': ['Life Services'], piiClearance: ['true'], maxExportRows: ['100'] });
  assert.strictEqual(auth.check(op, 'correspondence.resubmit', { status: 'failed', customer: 'life services' }).allowed, true);
  var notFailed = auth.check(op, 'correspondence.resubmit', { status: 'Completed', customer: 'Life Services' });
  assert.strictEqual(notFailed.allowed, false);
  assert.strictEqual(notFailed.policy, 'Resubmit failed records only');
  assert.strictEqual(auth.check(op, 'correspondence.resubmit', { status: 'Failed', customer: 'Claims System' }).policy, 'Resubmit within customer scope');
  assert.strictEqual(auth.check(op, 'correspondence.create', {}).allowed, false, 'missing customer fails closed');
  assert.strictEqual(auth.check(op, 'correspondence.payload.read', {}).allowed, true);
  assert.strictEqual(auth.check(user(['Operator'], {}), 'correspondence.payload.read', {}).allowed, false);
  assert.strictEqual(auth.check(op, 'correspondence.export', { rowCount: '100' }).allowed, true);
  assert.strictEqual(auth.check(op, 'correspondence.export', { rowCount: '101' }).allowed, false);
  assert.strictEqual(auth.check(user(['Operator'], {}), 'correspondence.export', { rowCount: '99999' }).allowed, true, 'no limit set');
});

h.test('scopeValues: * is unrestricted, missing is nothing', function () {
  assert.strictEqual(auth.scopeValues(user([], { 'scope.customer': ['*'] }), 'scope.customer'), null);
  assert.deepStrictEqual(auth.scopeValues(user([], {}), 'scope.customer'), []);
  assert.deepStrictEqual(auth.scopeValues(user([], { 'scope.customer': ['A'] }), 'scope.customer'), ['A']);
});

h.test('delete is Administrator-only and limited to the customer scope', function () {
  assert.strictEqual(auth.check(user(['Operator'], { 'scope.customer': ['*'] }), 'correspondence.delete', { customer: 'X' }).allowed, false, 'no role grants it');
  var scopedAdmin = user(['Administrator'], { 'scope.customer': ['Life Services'] });
  assert.strictEqual(auth.check(scopedAdmin, 'correspondence.delete', { customer: 'Life Services' }).allowed, true);
  assert.strictEqual(auth.check(scopedAdmin, 'correspondence.delete', { customer: 'Claims System' }).policy, 'Delete within customer scope');
});
