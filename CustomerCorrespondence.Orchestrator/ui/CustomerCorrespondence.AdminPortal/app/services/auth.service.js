(function () {
  'use strict';

  /**
   * Portal sign-in state, the $http interceptor that attaches the access token, and the
   * administration API.
   *
   * The token lives in sessionStorage: it survives a reload but not closing the tab, and isn't
   * shared across tabs. Permissions come from the API (roles → permissions); the UI only uses
   * them to hide what the user can't do — the API enforces every rule, including the
   * attribute-based ones the UI never sees (scope, PII clearance, record status...).
   */
  angular.module('adminPortal')
    .factory('authService', authService)
    .factory('authInterceptor', authInterceptor)
    .factory('adminApi', adminApi)
    .config(['$httpProvider', function ($httpProvider) {
      $httpProvider.interceptors.push('authInterceptor');
    }]);

  var SESSION_KEY = 'ecco.auth.session';

  function readSession() {
    try {
      var raw = window.sessionStorage.getItem(SESSION_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch (e) {
      return null;
    }
  }

  function writeSession(session) {
    try {
      if (session) window.sessionStorage.setItem(SESSION_KEY, JSON.stringify(session));
      else window.sessionStorage.removeItem(SESSION_KEY);
    } catch (e) {
      // Private mode / storage disabled: the session just won't survive a reload.
    }
  }

  authService.$inject = ['$http', '$q', '$rootScope', '$timeout', 'APP_CONFIG', 'errorRecovery'];

  function authService($http, $q, $rootScope, $timeout, APP_CONFIG, errorRecovery) {
    var base = APP_CONFIG.apiBaseUrl + '/auth';
    var session = null;   // { token, expiresAtUtc, user }
    var notice = null;    // message for the sign-in page ("Your session has ended...")
    var expiryTimer = null;

    function changed() {
      $rootScope.$broadcast('auth:changed', session && session.user);
    }

    function scheduleExpiry() {
      if (expiryTimer) $timeout.cancel(expiryTimer);
      expiryTimer = null;
      if (!session) return;
      var ms = new Date(session.expiresAtUtc).getTime() - Date.now();
      // $timeout's delay is a 32-bit int; tokens are short-lived, but guard anyway.
      expiryTimer = $timeout(function () { expire('Your session timed out. Sign in again to continue.'); }, Math.max(0, Math.min(ms, 2147483647)));
    }

    function setSession(data) {
      session = { token: data.accessToken, expiresAtUtc: data.expiresAtUtc, user: data.user };
      notice = null;
      writeSession(session);
      scheduleExpiry();
      changed();
      return session.user;
    }

    function clear(message) {
      session = null;
      notice = message || null;
      writeSession(null);
      scheduleExpiry();
      changed();
    }

    function expire(message) {
      if (!session) return;
      clear(message || 'Your session has ended. Sign in again to continue.');
    }

    function messageOf(err, fallback) {
      return errorRecovery.messageOf(err, fallback);
    }

    function resetError(err) {
      return {
        message: err.status <= 0 ? 'Can\'t reach the server. Check your connection and try again.' : messageOf(err, 'Could not reset the password.'),
        linkInvalid: !!(err.data && err.data.linkInvalid),
        problems: (err.data && err.data.problems) || []
      };
    }

    var service = {
      /** Restores a saved session (if unexpired) and re-validates it against the API. */
      init: function () {
        var saved = readSession();
        if (saved && saved.token && new Date(saved.expiresAtUtc).getTime() > Date.now() + 5000) {
          session = saved;
          scheduleExpiry();
          service.refresh();
        } else if (saved) {
          writeSession(null);
        }
      },

      login: function (userName, password) {
        return $http.post(base + '/login', { userName: userName, password: password })
          .then(function (res) { return setSession(res.data); })
          .catch(function (err) {
            return $q.reject({
              message: err.status === 401 || err.status === 400 ? messageOf(err, 'Incorrect user name or password.')
                : err.status <= 0 ? 'Can\'t reach the server. Check your connection and try again.'
                  : messageOf(err, 'Sign-in failed. Try again.'),
              locked: !!(err.data && err.data.locked)
            });
          });
      },

      logout: function () {
        var token = session && session.token;
        clear('You have signed out.');
        if (token) {
          // Recorded in the audit log; the token itself simply stops being used.
          $http.post(base + '/logout', null, { headers: { Authorization: 'Bearer ' + token }, skipAuth: true }).catch(angular.noop);
        }
      },

      /** Re-reads the profile (roles, permissions, attributes) from the API. */
      refresh: function () {
        if (!session) return $q.resolve(null);
        return $http.get(base + '/me').then(function (res) {
          if (!session) return null;
          session.user = res.data;
          writeSession(session);
          changed();
          return session.user;
        }).catch(function () { return null; }); // 401s are handled by the interceptor
      },

      changePassword: function (currentPassword, newPassword) {
        return $http.post(base + '/change-password', { currentPassword: currentPassword, newPassword: newPassword })
          .then(function (res) { return setSession(res.data); })
          .catch(function (err) {
            return $q.reject({ message: messageOf(err, 'Could not change the password.'), problems: (err.data && err.data.problems) || [] });
          });
      },

      passwordPolicy: function () {
        return $http.get(base + '/password-policy').then(function (res) { return res.data; });
      },

      // ── Forgot password (signed out; skipAuth so a stale session neither rides along nor gets ended) ──

      /** Asks for a reset link. Resolves to the API's message, which is the same whether or not the account exists. */
      forgotPassword: function (userNameOrEmail) {
        return $http.post(base + '/forgot-password', { userNameOrEmail: userNameOrEmail }, { skipAuth: true })
          .then(function (res) { return res.data && res.data.message; })
          .catch(function (err) {
            return $q.reject({
              message: err.status <= 0 ? 'Can\'t reach the server. Check your connection and try again.'
                : messageOf(err, 'Could not send the reset email. Try again later.')
            });
          });
      },

      /** Resolves to { userName, displayName } for a working reset link; rejects with linkInvalid otherwise. */
      checkResetLink: function (token) {
        return $http.post(base + '/reset-password/check', { token: token }, { skipAuth: true })
          .then(function (res) { return res.data; })
          .catch(function (err) { return $q.reject(resetError(err)); });
      },

      resetPassword: function (token, newPassword) {
        return $http.post(base + '/reset-password', { token: token, newPassword: newPassword }, { skipAuth: true })
          .then(function (res) { return res.data; })
          .catch(function (err) { return $q.reject(resetError(err)); });
      },

      /** Adopts a session the API re-issued (e.g. after you edit your own account, which rotates your stamp). */
      adopt: function (data) {
        return data && data.accessToken ? setSession(data) : null;
      },

      /** "Entra" for Microsoft sign-in accounts, "Local" for portal passwords. */
      signInMethod: function () {
        var u = session && session.user;
        return (u && u.signInMethod) || 'Local';
      },

      expire: expire,
      token: function () { return session && session.token; },
      user: function () { return session && session.user; },
      isAuthenticated: function () { return !!session; },
      /**
       * Asks the API whether this session's token still works; signs out with `message` when it doesn't
       * (e.g. "Sign out other sessions" was used in another browser). Resolves true while signed in.
       */
      verifySession: function (message) {
        if (!session) return $q.resolve(false);
        var token = session.token;
        return $http.get(base + '/me', { handles401: true }).then(function () { return true; }).catch(function (err) {
          // Only end the session that was checked: a newer token (adopted meanwhile) is still good.
          if (err.status === 401 && session && session.token === token) expire(message);
          return !!session;
        });
      },

      /** When this session's token expires (ISO, UTC), or null. */
      sessionExpiresUtc: function () { return session ? session.expiresAtUtc : null; },
      notice: function () { return notice; },
      clearNotice: function () { notice = null; },

      /** True when the user's roles grant the permission (and no forced password change is pending). */
      can: function (permission) {
        var u = session && session.user;
        return !!u && !u.mustChangePassword && (u.permissions || []).indexOf(permission) !== -1;
      },

      /** Values of an ABAC subject attribute, e.g. attribute('scope.customer') → ['Life Services']. */
      attribute: function (key) {
        var u = session && session.user;
        return (u && u.attributes && u.attributes[key]) || [];
      }
    };
    return service;
  }

  authInterceptor.$inject = ['$q', '$injector', 'APP_CONFIG'];

  /** Adds the bearer token to API calls; a 401 from any API call (other than sign-in itself) ends the session. */
  function authInterceptor($q, $injector, APP_CONFIG) {
    var auth; // resolved lazily: authService depends on $http, which depends on this interceptor

    function isApi(url) {
      return typeof url === 'string' && url.indexOf(APP_CONFIG.apiBaseUrl + '/') === 0;
    }

    return {
      request: function (config) {
        if (!isApi(config.url) || config.skipAuth) return config;
        auth = auth || $injector.get('authService');
        var token = auth.token();
        if (token) {
          config.headers = config.headers || {};
          config.headers.Authorization = 'Bearer ' + token;
        }
        return config;
      },
      responseError: function (rejection) {
        var url = rejection && rejection.config && rejection.config.url;
        if (rejection.status === 401 && isApi(url) && !/\/auth\/(login|change-password)$/.test(url) && !rejection.config.skipAuth && !rejection.config.handles401) {
          auth = auth || $injector.get('authService');
          auth.expire();
        }
        return $q.reject(rejection);
      }
    };
  }

  adminApi.$inject = ['$http', '$q', 'APP_CONFIG', 'errorRecovery'];

  function adminApi($http, $q, APP_CONFIG, errorRecovery) {
    var base = APP_CONFIG.apiBaseUrl + '/admin';

    function unwrap(promise) {
      return promise.then(
        function (res) { return res.data; },
        function (err) {
          // 405, or a 404 with no explanation, means the API has no such endpoint: usually an older API build that
          // hasn't been restarted since an update.
          var missingEndpoint = err.status === 405 || (err.status === 404 && !(err.data && err.data.message));
          return $q.reject({
            status: err.status,
            message: missingEndpoint
              ? "The API doesn't support this action yet (HTTP " + err.status + '). It is probably running an older build; restart or update the API, then try again.'
              : errorRecovery.messageOf(err, 'The request failed (HTTP ' + (err.status || 'no response') + ').'),
            problems: (err.data && err.data.problems) || []
          });
        }
      );
    }

    return {
      users: function () { return unwrap($http.get(base + '/users')); },
      createUser: function (user) { return unwrap($http.post(base + '/users', user)); },
      updateUser: function (id, user) { return unwrap($http.put(base + '/users/' + encodeURIComponent(id), user)); },
      resetPassword: function (id, newPassword) {
        return unwrap($http.post(base + '/users/' + encodeURIComponent(id) + '/reset-password', { newPassword: newPassword }));
      },
      deleteUser: function (id) { return unwrap($http.delete(base + '/users/' + encodeURIComponent(id))); },
      unlock: function (id) { return unwrap($http.post(base + '/users/' + encodeURIComponent(id) + '/unlock')); },
      roles: function () { return unwrap($http.get(base + '/roles')); },
      permissions: function () { return unwrap($http.get(base + '/permissions')); },
      setRolePermissions: function (roleId, permissions) {
        return unwrap($http.put(base + '/roles/' + roleId + '/permissions', { permissions: permissions }));
      },
      attributes: function () { return unwrap($http.get(base + '/attributes')); },
      policies: function () { return unwrap($http.get(base + '/policies')); },
      updatePolicy: function (id, change) { return unwrap($http.put(base + '/policies/' + id, change)); },
      audit: function (top, userName) {
        return unwrap($http.get(base + '/audit', { params: { top: top || 200, userName: userName || undefined } }));
      },
      lookup: function (field) { return unwrap($http.get(base + '/lookups/' + encodeURIComponent(field))); },

      // Entra group / app-role mappings (single sign-on)
      groupMappings: function () { return unwrap($http.get(base + '/group-mappings')); },
      addGroupMapping: function (m) { return unwrap($http.post(base + '/group-mappings', m)); },
      deleteGroupMapping: function (id) { return unwrap($http.delete(base + '/group-mappings/' + id)); },

      // Recycle bin, letters, alerts (portal endpoints)
      recycleBin: function (search) { return unwrap($http.get(APP_CONFIG.apiBaseUrl + '/portal/recycle-bin', { params: { search: search || undefined } })); },
      /** Resolves to { restored: [ids], failed: [{ correspondenceId, docId, message }], notFound: [ids] }. */
      restoreMany: function (ids) { return unwrap($http.post(APP_CONFIG.apiBaseUrl + '/portal/recycle-bin/restore', { correspondenceIds: ids })); },
      restore: function (id) { return unwrap($http.post(APP_CONFIG.apiBaseUrl + '/portal/recycle-bin/' + encodeURIComponent(id) + '/restore')); },
      purge: function (id) { return unwrap($http.delete(APP_CONFIG.apiBaseUrl + '/portal/recycle-bin/' + encodeURIComponent(id))); },
      templates: function () { return unwrap($http.get(APP_CONFIG.apiBaseUrl + '/portal/letters/templates')); },
      placeholders: function () { return unwrap($http.get(APP_CONFIG.apiBaseUrl + '/portal/letters/placeholders')); },
      createTemplate: function (t) { return unwrap($http.post(APP_CONFIG.apiBaseUrl + '/portal/letters/templates', t)); },
      updateTemplate: function (id, t) { return unwrap($http.put(APP_CONFIG.apiBaseUrl + '/portal/letters/templates/' + id, t)); },
      publishTemplate: function (id) { return unwrap($http.post(APP_CONFIG.apiBaseUrl + '/portal/letters/templates/' + id + '/publish')); },
      retireTemplate: function (id) { return unwrap($http.post(APP_CONFIG.apiBaseUrl + '/portal/letters/templates/' + id + '/retire')); },
      alerts: function (open, top) { return unwrap($http.get(APP_CONFIG.apiBaseUrl + '/portal/alerts', { params: { open: !!open, top: top || 50 } })); },
      acknowledgeAlert: function (id) { return unwrap($http.post(APP_CONFIG.apiBaseUrl + '/portal/alerts/' + id + '/acknowledge')); },
      sla: function () { return unwrap($http.get(APP_CONFIG.apiBaseUrl + '/portal/sla')); },
      saveSla: function (config) { return unwrap($http.put(APP_CONFIG.apiBaseUrl + '/portal/sla', config)); },
      alertRules: function () { return unwrap($http.get(APP_CONFIG.apiBaseUrl + '/portal/alerts/rules')); }
    };
  }
})();
