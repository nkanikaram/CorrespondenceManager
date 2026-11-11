(function () {
  'use strict';

  /**
   * Runtime settings. `apiBaseUrl` is relative so requests go through the dev
   * server's proxy (see `npm start`); point it at an absolute URL to call the
   * backend directly (the backend must then allow CORS from this origin).
   */
  /** The portal's version (keep in step with package.json); shown in the account menu. */
  angular.module('adminPortal').constant('APP_VERSION', '1.0.0');

  angular.module('adminPortal').constant('APP_CONFIG', {
    apiBaseUrl: '/api',
    searchTimeoutMs: 30000,
    chatHistoryTimeoutMs: 15000,
    aiModel: 'claude-haiku-4-5',
    // A turn with several tool calls (e.g. one search_grid + multiple show_chart) needs headroom
    // beyond a single action — 1024 was tight enough that a narrated multi-step plan could exhaust
    // it before the tool_use blocks were emitted, silently dropping every action.
    aiMaxTokens: 4096
  });
})();
