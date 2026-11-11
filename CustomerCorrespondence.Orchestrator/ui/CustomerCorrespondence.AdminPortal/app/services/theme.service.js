(function () {
  'use strict';

  /** Applies the light or dark theme defined in css/styles.css via <html data-theme>. */
  angular.module('adminPortal').factory('themeService', themeService);

  themeService.$inject = ['storage'];

  function themeService(storage) {
    var KEY = 'ecco-theme';
    var THEMES = [
      { id: 'dark', label: 'Dark', emoji: '🌙' },
      { id: 'light', label: 'Light', emoji: '☀️' },
      { id: 'system', label: 'System', emoji: '🖥️' } // follows the operating system's light / dark setting
    ];
    var current = 'dark';
    var systemQuery = window.matchMedia ? window.matchMedia('(prefers-color-scheme: light)') : null;

    /** The theme actually shown: 'system' becomes light or dark. */
    function resolve(id) {
      if (id !== 'system') return id;
      return systemQuery && systemQuery.matches ? 'light' : 'dark';
    }
    if (systemQuery) {
      var onSystemChange = function () { if (current === 'system') apply('system'); };
      if (systemQuery.addEventListener) systemQuery.addEventListener('change', onSystemChange);
      else if (systemQuery.addListener) systemQuery.addListener(onSystemChange);
    }

    function find(id) {
      return THEMES.filter(function (t) { return t.id === id; })[0];
    }

    function apply(id) {
      var shown = resolve(id);
      var root = document.documentElement;
      root.setAttribute('data-theme', shown);
      root.style.colorScheme = shown === 'light' ? 'light' : 'dark';
    }

    return {
      themes: THEMES,
      init: function () {
        var saved = storage.get(KEY);
        // A theme saved before the others were retired falls back to dark.
        current = find(saved) ? saved : 'dark';
        apply(current);
      },
      current: function () {
        return find(current);
      },
      /** The theme shown right now ('light' or 'dark'), whatever the setting. */
      shown: function () { return resolve(current); },
      /** Switches between light and dark (from System, to the opposite of what's shown). */
      toggle: function () {
        this.set(resolve(current) === 'dark' ? 'light' : 'dark');
      },
      set: function (id) {
        if (!find(id)) return;
        current = id;
        apply(id);
        storage.set(KEY, id);
      }
    };
  }
})();
