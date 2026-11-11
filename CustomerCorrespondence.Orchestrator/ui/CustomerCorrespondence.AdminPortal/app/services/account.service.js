(function () {
  'use strict';

  /**
   * The signed-in user's account menu data and display preferences.
   *
   * Preferences (theme, time zone for dates, default page size, row density, and the appearance: font, text size,
   * header and text colour — see the appearance service) are saved with the account
   * (PUT api/account/preferences), so they follow the user to any browser, and cached in this browser so they
   * apply before the API answers. A change broadcasts 'prefs:changed' with the new preferences.
   *
   * Account info (GET api/account): data scope, previous sign-in, support links, version. Fetched when the
   * account menu opens and kept for a minute.
   */
  angular.module('adminPortal').factory('accountService', accountService);

  accountService.$inject = ['$http', '$q', '$rootScope', 'APP_CONFIG', 'authService', 'themeService', 'storage', 'displayTime', 'appearance'];

  var CACHE_KEY = 'ecco.prefs';
  var PENDING_KEY = 'ecco.prefs.pending'; // preferences changed here that the account hasn't stored yet
  var INFO_TTL_MS = 60000;
  var FIELDS = ['theme', 'timeZone', 'pageSize', 'density', 'font', 'textSize', 'headerColor', 'textColor'];

  function accountService($http, $q, $rootScope, APP_CONFIG, authService, themeService, storage, displayTime, appearance) {
    storage.set('ecco.ai.appearance', ''); // the assistant-only appearance settings, now portal-wide preferences
    var base = APP_CONFIG.apiBaseUrl + '/account';
    var prefs = clean(storage.getJson(CACHE_KEY, {}) || {});
    var pending = (storage.getJson(PENDING_KEY, []) || []).filter(function (k) { return FIELDS.indexOf(k) !== -1; });
    var info = null;
    var infoAt = 0;
    var infoPromise = null;

    function clean(p) {
      var out = {};
      FIELDS.forEach(function (k) { if (p && p[k] !== undefined && p[k] !== null && p[k] !== '') out[k] = p[k]; });
      return out;
    }

    /** Applies the preferences that act on the whole page: theme, time zone and appearance. */
    function apply() {
      if (prefs.theme) themeService.set(prefs.theme);
      appearance.apply(prefs);
      displayTime.utc = prefs.timeZone === 'utc';
      $rootScope.$broadcast('prefs:changed', angular.copy(prefs));
    }

    function setPending(keys) {
      pending = keys;
      storage.setJson(PENDING_KEY, pending);
    }

    /**
     * The account's saved preferences replace this browser's, except ones changed here that the account hasn't
     * stored yet (the save failed, or the API dropped a preference it doesn't know): those are kept and sent again.
     */
    function adoptServerPrefs(server) {
      var p = clean(server);
      pending.forEach(function (k) {
        if (prefs[k] !== undefined) p[k] = prefs[k];
        else delete p[k];
      });
      if (!Object.keys(p).length && !pending.length) return; // nothing saved with the account yet: keep this browser's
      prefs = p;
      storage.setJson(CACHE_KEY, prefs);
      apply();
      if (pending.length) save().catch(angular.noop);
    }

    /**
     * Saves every preference with the account, then checks the reply (what the account actually stored): a
     * preference it didn't keep stays pending, and the promise rejects saying which.
     */
    function save() {
      var sent = angular.copy(prefs);
      return $http.put(base + '/preferences', sent).then(function (res) {
        var stored = clean(res.data || {});
        var lost = Object.keys(sent).filter(function (k) { return !angular.equals(stored[k], sent[k]); });
        setPending(lost);
        if (lost.length) {
          return $q.reject({ message: 'Applied in this browser, but your account didn\'t keep ' + lost.map(label).join(', ') +
            '. The API may need restarting or updating; it will be saved again automatically.' });
        }
        return prefs;
      }, function (err) {
        setPending(Object.keys(sent));
        return $q.reject({ message: (err.data && err.data.message) || 'Your preference applies in this browser, but could not be saved to your account.' });
      });
    }
    var LABELS = { theme: 'theme', timeZone: 'time zone', pageSize: 'rows per page', density: 'row density',
      font: 'font', textSize: 'text size', headerColor: 'header colour', textColor: 'text colour' };
    function label(k) { return LABELS[k] || k; }

    function loadInfo(force) {
      if (!authService.isAuthenticated()) return $q.reject({ message: 'Not signed in.' });
      if (!force && info && Date.now() - infoAt < INFO_TTL_MS) return $q.resolve(info);
      if (infoPromise) return infoPromise;
      infoPromise = $http.get(base).then(function (res) {
        info = res.data || {};
        infoAt = Date.now();
        adoptServerPrefs(info.preferences);
        return info;
      }).finally(function () { infoPromise = null; });
      return infoPromise;
    }

    // Signing in (or out, or as someone else) re-reads the account; its saved preferences then apply.
    $rootScope.$on('auth:changed', function (e, user) {
      info = null;
      infoAt = 0;
      if (!user) setPending([]); // signed out: unsaved changes belonged to that account
      if (user && !user.mustChangePassword) loadInfo(true).catch(angular.noop);
    });

    apply();

    return {
      /** The current preferences (a copy). Unset values are absent. */
      prefs: function () { return angular.copy(prefs); },

      /** Sets one preference, applies it now, and saves it with the account. Resolves when saved. */
      set: function (key, value) {
        var changes = {};
        changes[key] = value;
        return this.setAll(changes);
      },

      /** Sets several preferences at once ({ key: value }; null clears one) with a single save. */
      setAll: function (changes) {
        var keys = Object.keys(changes || {});
        if (keys.some(function (k) { return FIELDS.indexOf(k) === -1; })) return $q.reject({ message: 'Unknown preference.' });
        var next = angular.copy(prefs);
        keys.forEach(function (k) {
          var value = changes[k];
          if (value === null || value === undefined || value === '') delete next[k];
          else next[k] = value;
        });
        if (angular.equals(next, prefs)) return $q.resolve(prefs);
        prefs = next;
        storage.setJson(CACHE_KEY, prefs);
        setPending(pending.concat(keys.filter(function (k) { return pending.indexOf(k) === -1; })));
        apply();
        if (!authService.isAuthenticated()) return $q.resolve(prefs);
        return save();
      },

      /** Account info for the menu; cached for a minute. */
      info: function (force) { return loadInfo(force); },
      cachedInfo: function () { return info; },

      /** Ends every other session; this browser carries on with the token the API returns. */
      signOutOthers: function () {
        return $http.post(base + '/sign-out-others').then(function (res) {
          authService.adopt(res.data);
          return res.data;
        }).catch(function (err) {
          return $q.reject({ message: (err.data && err.data.message) || 'Could not sign out your other sessions. Try again.' });
        });
      },

      /** "UTC" or the browser's zone with its offset, e.g. "Europe/London (UTC+00:00)". */
      timeZoneLabel: function (mode) {
        if ((mode || prefs.timeZone) === 'utc') return 'UTC';
        var zone = '';
        try { zone = Intl.DateTimeFormat().resolvedOptions().timeZone || ''; } catch (e) { zone = ''; }
        var offset = -new Date().getTimezoneOffset();
        var sign = offset >= 0 ? '+' : '−';
        var abs = Math.abs(offset);
        var hhmm = (abs / 60 < 10 ? '0' : '') + Math.floor(abs / 60) + ':' + (abs % 60 < 10 ? '0' : '') + (abs % 60);
        return (zone ? zone.replace(/_/g, ' ') + ' ' : '') + '(UTC' + sign + hhmm + ')';
      }
    };
  }
})();
