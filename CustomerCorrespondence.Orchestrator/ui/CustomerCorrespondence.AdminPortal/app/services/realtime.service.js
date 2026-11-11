(function () {
  'use strict';

  /**
   * Live updates from the API: a SignalR connection to /hubs/portal (falls back to Server-Sent Events / long
   * polling inside SignalR), and — when no hub is available at all — polling GET /api/portal/changes every 30 s.
   *
   * Broadcasts on $rootScope (ids and counts only; pages re-fetch through the normal endpoints):
   *   'portal:recordsChanged'   { newRecords, changedIds }
   *   'portal:pdfStatusChanged' { correspondenceId, status }
   *   'portal:alertRaised'      { alertId, ruleKey, severity, title }   (users with alerts.read)
   */
  angular.module('adminPortal').factory('realtimeService', realtimeService);

  realtimeService.$inject = ['$rootScope', '$http', '$timeout', '$window', '$log', 'APP_CONFIG', 'authService'];

  function realtimeService($rootScope, $http, $timeout, $window, $log, APP_CONFIG, authService) {
    var POLL_MS = 30000;
    // A WebSocket that never completes its handshake (e.g. a proxy that can't forward WebSockets) neither
    // connects nor fails; after this long, fall back to polling instead of waiting forever.
    var CONNECT_TIMEOUT_MS = 10000;
    var connectTimer = null;
    var connection = null;
    var pollTimer = null;
    var since = null;
    var state = 'stopped'; // stopped | connecting | live | polling

    function emit(name, data) {
      $rootScope.$applyAsync(function () { $rootScope.$broadcast(name, data); });
    }

    function poll() {
      pollTimer = null;
      if (state !== 'polling' || !authService.isAuthenticated()) return;
      $http.get(APP_CONFIG.apiBaseUrl + '/portal/changes', { params: { since: since }, handles401: true }).then(function (res) {
        var c = res.data || {};
        since = c.asOfUtc || since;
        if (c.newRecords || (c.changedIds && c.changedIds.length)) emit('portal:recordsChanged', { newRecords: c.newRecords, changedIds: c.changedIds || [] });
        (c.pdfChanges || []).forEach(function (p) { emit('portal:pdfStatusChanged', p); });
        if (c.newAlerts) emit('portal:alertRaised', { count: c.newAlerts });
      }).catch(function (err) {
        // This token stopped working: say why (it was signed out elsewhere, or the account changed).
        if (err && err.status === 401) checkSession();
      }).finally(function () {
        if (state === 'polling') pollTimer = $timeout(poll, POLL_MS);
      });
    }

    var SIGNED_OUT_ELSEWHERE = 'You were signed out because your account was signed out on another browser or device, ' +
      'your password changed, or an administrator changed your account. Sign in again to continue.';
    var lastCheck = 0;
    function checkSession() {
      lastCheck = Date.now();
      authService.verifySession(SIGNED_OUT_ELSEWHERE);
    }

    // Coming back to the tab (or waking the laptop): re-check, at most once every 30 seconds. Covers missed
    // pushes (live connection down, polling, or the API restarted).
    $window.document.addEventListener('visibilitychange', function () {
      if ($window.document.visibilityState === 'visible' && authService.isAuthenticated() && Date.now() - lastCheck > 30000) {
        $rootScope.$applyAsync(checkSession);
      }
    });

    function startPolling() {
      state = 'polling';
      since = since || new Date().toISOString();
      if (!pollTimer) pollTimer = $timeout(poll, POLL_MS);
    }

    function start() {
      if (state !== 'stopped') return;
      state = 'connecting';
      since = new Date().toISOString();
      var signalR = $window.signalR;
      if (!signalR) { startPolling(); return; }
      connection = new signalR.HubConnectionBuilder()
        .withUrl(APP_CONFIG.apiBaseUrl.replace(/\/api$/, '') + '/hubs/portal', { accessTokenFactory: function () { return authService.token(); } })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.None)
        .build();
      connection.on('recordsChanged', function (d) { emit('portal:recordsChanged', d); });
      connection.on('pdfStatusChanged', function (d) { emit('portal:pdfStatusChanged', d); });
      connection.on('alertRaised', function (d) { emit('portal:alertRaised', d); });
      // This user's sessions were ended (another browser used "Sign out other sessions", the password changed, or an
      // administrator changed the account). Re-check this token after a moment, so the browser that started it has
      // adopted its new token first; every other browser signs out now rather than at its next request.
      connection.on('sessionsEnded', function () { $timeout(checkSession, 2000); });
      connection.onreconnected(function () { $rootScope.$applyAsync(function () { state = 'live'; }); });
      connection.onclose(function () {
        $rootScope.$applyAsync(function () { if (state !== 'stopped') startPolling(); });
      });
      var starting = connection;
      connectTimer = $timeout(function () {
        connectTimer = null;
        if (state !== 'connecting' || connection !== starting) return;
        $log.debug('Live updates did not connect in time, polling instead');
        connection = null;
        starting.stop().catch(angular.noop);
        startPolling();
      }, CONNECT_TIMEOUT_MS);
      connection.start().then(function () {
        if (connectTimer) { $timeout.cancel(connectTimer); connectTimer = null; }
        $rootScope.$applyAsync(function () { if (state === 'connecting') state = 'live'; });
      }).catch(function (err) {
        if (connectTimer) { $timeout.cancel(connectTimer); connectTimer = null; }
        $log.debug('Live updates unavailable, polling instead', err);
        connection = null;
        $rootScope.$applyAsync(function () { if (state === 'connecting') startPolling(); });
      });
    }

    function stop() {
      state = 'stopped';
      if (connectTimer) { $timeout.cancel(connectTimer); connectTimer = null; }
      if (pollTimer) { $timeout.cancel(pollTimer); pollTimer = null; }
      if (connection) { connection.stop().catch(angular.noop); connection = null; }
    }

    // Follow the session: connect while signed in (with a usable password), disconnect on sign-out.
    $rootScope.$on('auth:changed', function (e, user) {
      if (user && !user.mustChangePassword) start();
      else stop();
    });

    return {
      start: start,
      stop: stop,
      state: function () { return state; },
      isLive: function () { return state === 'live'; }
    };
  }
})();
