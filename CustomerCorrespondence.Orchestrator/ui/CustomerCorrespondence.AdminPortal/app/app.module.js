(function () {
  'use strict';

  angular.module('adminPortal', [])
    .config(['$httpProvider', '$compileProvider', function ($httpProvider, $compileProvider) {
      // Batch digests for the many parallel page fetches used by analytics/export.
      $httpProvider.useApplyAsync(true);
      $compileProvider.debugInfoEnabled(false);
    }])
    .run(['themeService', 'authService', 'realtimeService', 'ssoService', 'accountService', function (themeService, authService, realtimeService, ssoService, accountService) {
      themeService.init();
      accountService.prefs(); // created now, so saved theme / time zone preferences apply before anything renders
      authService.init();          // restores a session; realtimeService connects when it's confirmed
      ssoService.completeRedirect(); // finishes a Microsoft sign-in when we've just been redirected back
    }]);
})();
