/*
 * Minimal AngularJS stand-in for unit-testing services in plain Node: records
 * factory/constant registrations and resolves dependencies by $inject (or by
 * the array annotation form) on demand.
 */
'use strict';

var path = require('path');

var registry = {};
var instances = {};
var components = {};

function register(name, def) {
  registry[name] = def;
  return moduleApi;
}

var moduleApi = {
  factory: register,
  service: register,
  constant: function (name, value) { instances[name] = value; return moduleApi; },
  component: function (name, def) { components[name] = def; return moduleApi; },
  directive: function () { return moduleApi; },
  filter: function () { return moduleApi; },
  config: function () { return moduleApi; },
  run: function () { return moduleApi; }
};

global.angular = {
  module: function () { return moduleApi; },
  extend: Object.assign,
  copy: function (v) { return v === undefined ? v : JSON.parse(JSON.stringify(v)); },
  noop: function () {}
};

function get(name) {
  if (Object.prototype.hasOwnProperty.call(instances, name)) return instances[name];
  var def = registry[name];
  if (!def) throw new Error('Unknown service: ' + name);
  var fn = Array.isArray(def) ? def[def.length - 1] : def;
  var deps = Array.isArray(def) ? def.slice(0, -1) : (fn.$inject || []);
  instances[name] = fn.apply(null, deps.map(get));
  return instances[name];
}

function load(relativePath) {
  require(path.join(__dirname, '..', relativePath));
}

var passed = 0;
var failed = 0;

function test(name, fn) {
  try {
    fn();
    passed++;
    console.log('  ok - ' + name);
  } catch (e) {
    failed++;
    console.log('  FAIL - ' + name + '\n    ' + (e && e.stack ? e.stack.split('\n').slice(0, 3).join('\n    ') : e));
  }
}

function suite(name) {
  console.log('\n' + name);
}

function summary() {
  console.log('\n' + passed + ' passed, ' + failed + ' failed');
  if (failed) process.exitCode = 1;
}

/** A registered component's controller constructor (for testing its static helpers). */
function componentController(name) {
  if (!components[name]) throw new Error('Unknown component: ' + name);
  return components[name].controller;
}

module.exports = { load: load, get: get, componentController: componentController, test: test, suite: suite, summary: summary };
