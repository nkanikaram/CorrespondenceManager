(function () {
  'use strict';

  /**
   * Microsoft Entra ID sign-in for the portal (MSAL.js, authorization code flow with PKCE, full-page redirect).
   *
   * The API says whether SSO is on and which app registration to use (GET /api/auth/config), so the same build
   * works in every environment. After Microsoft redirects back, the ID token is exchanged for a portal session
   * (POST /api/auth/entra), where the API checks the tenant, MFA and the user's groups.
   *
   * The mock server (npm run start:mock) answers config with { mock: true }: the button then opens a small
   * account picker instead of Microsoft, so the rest of the flow can be exercised without a tenant.
   */
  angular.module('adminPortal').factory('ssoService', ssoService);

  ssoService.$inject = ['$http', '$q', '$window', 'APP_CONFIG', 'authService', 'errorRecovery'];

  function ssoService($http, $q, $window, APP_CONFIG, authService, errorRecovery) {
    var config = null;     // { entra: { enabled, clientId, tenantId, authority, mock }, localLogin: { enabled } }
    var msal = null;
    var configPromise = null;

    function redirectUri() {
      return $window.location.origin + $window.location.pathname;
    }

    function loadConfig() {
      if (!configPromise) {
        configPromise = $http.get(APP_CONFIG.apiBaseUrl + '/auth/config', { skipAuth: true })
          .then(function (res) { config = res.data; return config; })
          .catch(function () {
            config = { entra: { enabled: false }, localLogin: { enabled: true } };
            return config;
          });
      }
      return configPromise;
    }

    function client() {
      if (msal || !config || !config.entra.enabled || config.entra.mock) return $q.resolve(msal);
      if (!$window.msal || !$window.msal.PublicClientApplication) return $q.reject({ message: 'The Microsoft sign-in library failed to load.' });
      msal = new $window.msal.PublicClientApplication({
        auth: { clientId: config.entra.clientId, authority: config.entra.authority, redirectUri: redirectUri(), postLogoutRedirectUri: redirectUri() },
        // sessionStorage: nothing about the Microsoft account outlives the browser tab.
        cache: { cacheLocation: 'sessionStorage' }
      });
      return $q.when(msal.initialize()).then(function () { return msal; });
    }

    function exchange(idToken) {
      return $http.post(APP_CONFIG.apiBaseUrl + '/auth/entra', { idToken: idToken }, { skipAuth: true })
        .then(function (res) { return authService.adopt(res.data); })
        .catch(function (err) { return $q.reject({ message: errorRecovery.messageOf(err, 'Microsoft sign-in failed.') }); });
    }

    return {
      loadConfig: loadConfig,
      config: function () { return config; },

      /** Call once on start-up: completes a sign-in when Microsoft has just redirected back here. */
      completeRedirect: function () {
        return loadConfig().then(function () {
          if (!config.entra.enabled || config.entra.mock) return null;
          return client().then(function (app) {
            return $q.when(app.handleRedirectPromise()).then(function (result) {
              return result && result.idToken ? exchange(result.idToken) : null;
            });
          });
        });
      },

      /** Sends the browser to Microsoft (or opens the mock account picker). */
      signIn: function (mockAccount) {
        if (config && config.entra.mock) return exchange('mock:' + (mockAccount || ''));
        return client().then(function (app) {
          return app.loginRedirect({ scopes: ['openid', 'profile', 'email'], prompt: 'select_account' });
        });
      },

      /** Forgets the Microsoft account in this tab (the portal session itself is ended by authService.logout). */
      forget: function () {
        if (!msal) return;
        try { if (typeof msal.clearCache === 'function') msal.clearCache(); } catch (e) { /* best effort */ }
      }
    };
  }
})();
