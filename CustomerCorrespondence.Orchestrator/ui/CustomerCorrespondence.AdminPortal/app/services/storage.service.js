(function () {
  'use strict';

  /**
   * localStorage wrapper that never throws — storage can be unavailable or full
   * (private browsing, quota, blocked site data), and every caller treats it as
   * best-effort.
   */
  angular.module('adminPortal').factory('storage', storage);

  function storage() {
    return {
      get: get,
      set: set,
      getJson: getJson,
      setJson: setJson
    };

    function get(key) {
      try {
        return window.localStorage.getItem(key);
      } catch (e) {
        return null;
      }
    }

    function set(key, value) {
      try {
        window.localStorage.setItem(key, value);
      } catch (e) {
        // ignore storage errors (private browsing, quota, etc.)
      }
    }

    function getJson(key, fallback) {
      try {
        var raw = window.localStorage.getItem(key);
        return raw ? JSON.parse(raw) : fallback;
      } catch (e) {
        return fallback;
      }
    }

    function setJson(key, value) {
      set(key, JSON.stringify(value));
    }
  }
})();
