(function () {
  'use strict';

  /**
   * <app-shell> — the root view. Signed out: the sign-in page. Temporary password: the
   * forced password change. Otherwise: header (navigation, theme, user menu) and the
   * current page, chosen by the URL (#!/ = correspondence, #!/admin = administration).
   *
   * Pages are created with ng-if, so signing out destroys them and everything they loaded.
   */
  angular.module('adminPortal').component('appShell', {
    templateUrl: 'app/shell/app-shell.html',
    controller: AppShellController
  });

  AppShellController.$inject = ['$scope', '$location', '$document', '$timeout', '$window', '$filter', 'appearance', 'authService', 'assistantBridge', 'ssoService', 'adminApi',
    'accountService', 'themeService', 'APP_VERSION'];

  function AppShellController($scope, $location, $document, $timeout, $window, $filter, appearance, authService, assistantBridge, ssoService, adminApi,
    accountService, themeService, APP_VERSION) {
    var $ctrl = this;
    $ctrl.auth = authService;
    $ctrl.bridge = assistantBridge;
    $ctrl.year = new Date().getFullYear();

    function canAdminister() {
      return ['admin.users.manage', 'admin.audit.read', 'letters.templates.manage', 'correspondence.recycle', 'alerts.read', 'sla.manage']
        .some(function (p) { return authService.can(p); });
    }

    function route() {
      var path = $location.path();
      if (path === '/reset-password' && authService.isAuthenticated()) {
        // An emailed reset link opened in a signed-in tab: the reset ends every session anyway, so sign out and show it.
        ssoService.forget();
        authService.logout();
        return;
      }
      if (path === '/admin' && authService.isAuthenticated() && !canAdminister()) {
        $location.path('/').replace(); // no permission: back to the main page
        return;
      }
      $ctrl.view = path === '/admin' ? 'admin' : 'search';
    }

    $ctrl.$onInit = function () { route(); loadAlerts(); };
    $scope.$on('$locationChangeSuccess', route);
    $scope.$on('auth:changed', function () {
      $ctrl.userMenuOpen = false;
      $ctrl.showChangePassword = false;
      route();
      loadAlerts();
    });

    // ── Alerts bell: open alerts for users who may see them; refreshed when one is raised (live) ──
    $ctrl.openAlerts = [];
    function loadAlerts() {
      if (!$ctrl.signedIn() || !authService.can('alerts.read')) { $ctrl.openAlerts = []; return; }
      adminApi.alerts(true, 10).then(function (list) { $ctrl.openAlerts = list; }).catch(function () { $ctrl.openAlerts = []; });
    }
    $scope.$on('portal:alertRaised', loadAlerts);
    $scope.$on('portal:alertsChanged', loadAlerts); // acknowledged on the Alerts tab
    $ctrl.canSeeAlerts = function () { return authService.can('alerts.read'); };
    $ctrl.acknowledge = function (alert, $event) {
      if ($event) $event.stopPropagation();
      adminApi.acknowledgeAlert(alert.alertId).then(loadAlerts).catch(loadAlerts);
    };
    /** SLA alerts open the grid filtered to breached requests. */
    $ctrl.showBreached = function (alert) {
      $ctrl.alertsOpen = false;
      $location.path('/').search({ sla: 'breached' });
    };
    $ctrl.viewAllAlerts = function () {
      $ctrl.alertsOpen = false;
      $location.path('/admin').search({ tab: 'alerts' });
    };
    $ctrl.isSso = function () { return authService.signInMethod() === 'Entra'; };

    // ── Account menu: access, security, preferences, help, version ──
    $ctrl.version = APP_VERSION;
    $ctrl.account = function () { return accountService.cachedInfo(); };

    $ctrl.toggleUserMenu = function () {
      $ctrl.userMenuOpen = !$ctrl.userMenuOpen;
      $ctrl.confirmSignOutOthers = false;
      $ctrl.accountError = null;
      $ctrl.menuOpenedAt = new Date().toISOString(); // fixed while open (a changing href would never settle)
      var header = $document[0].querySelector('.app-header');
      $ctrl.menuTop = header ? Math.round(header.getBoundingClientRect().bottom) + 4 : 60; // phones: the menu sits under the header
      // Always re-read (what's cached shows meanwhile), so the security details are current.
      if ($ctrl.userMenuOpen) accountService.info(true).catch(function (e) { $ctrl.accountError = e.message || 'Could not load your account details.'; });
    };
    $ctrl.closeUserMenu = function () { $ctrl.userMenuOpen = false; $ctrl.confirmSignOutOthers = false; };

    /** A data-scope list for the menu: null = every value, [] = none. */
    $ctrl.scopeText = function (values, all) {
      if (values === null || values === undefined) return all;
      if (!values.length) return 'None';
      return values.length <= 3 ? values.join(', ') : values.slice(0, 2).join(', ') + ' and ' + (values.length - 2) + ' more';
    };
    $ctrl.scopeTitle = function (values) { return values && values.length > 3 ? values.join(', ') : ''; };
    $ctrl.sessionEnds = function () { return authService.sessionExpiresUtc(); };

    /** "in 42 min" / "in 1 h 5 min" until the session ends. */
    $ctrl.minutesLeft = function () {
      var ends = authService.sessionExpiresUtc();
      var min = ends ? Math.max(0, Math.round((new Date(ends).getTime() - Date.now()) / 60000)) : 0;
      return min >= 60 ? 'in ' + Math.floor(min / 60) + ' h' + (min % 60 ? ' ' + (min % 60) + ' min' : '') : 'in ' + min + ' min';
    };

    /** "Today 13:20" / "Yesterday 09:05" / "Nov 2 09:05" (in the user's time zone preference). */
    $ctrl.friendlyTime = function (utc) {
      if (!utc) return '';
      var date = $filter('date');
      var day = function (d) { return date(d, 'yyyy-MM-dd'); };
      var now = new Date();
      var yesterday = new Date(now.getTime() - 86400000);
      var label = day(utc) === day(now.toISOString()) ? 'Today' : day(utc) === day(yesterday.toISOString()) ? 'Yesterday' : date(utc, 'MMM d');
      return label + ' ' + date(utc, 'HH:mm');
    };

    /** The sign-in's address; the loopback address reads as "this computer". */
    $ctrl.ipLabel = function (ip) {
      if (!ip) return 'unknown address';
      return /^(::1|127\.0\.0\.1|::ffff:127\.0\.0\.1)$/.test(ip) ? 'this computer' : ip;
    };

    /** Environment badge colour: production green, anything else amber (so a test system is noticeable). */
    $ctrl.envTone = function (env) { return /^prod/i.test(env || '') ? 'prod' : 'nonprod'; };

    $ctrl.signOutOthers = function () {
      $ctrl.signingOutOthers = true;
      accountService.signOutOthers().then(function () {
        // The new token closes the menu (auth:changed), so confirm with a toast.
        $ctrl.closeUserMenu();
        $ctrl.toast = { variant: 'success', title: 'Other sessions signed out', message: 'Every other browser and device is signed out. You stay signed in here.' };
        $timeout(function () { $ctrl.toast = null; }, 6000);
      }).catch(function (e) { $ctrl.accountError = e.message; })
        .finally(function () { $ctrl.signingOutOthers = false; });
    };

    // Preferences dialog: changes apply at once and save with the account.
    $ctrl.openPreferences = function () {
      $ctrl.closeUserMenu();
      $ctrl.prefs = accountService.prefs();
      $ctrl.prefsError = null;
      if ($ctrl.prefs.headerColor) $ctrl.pick.header = $ctrl.prefs.headerColor;
      if ($ctrl.isCustomText()) $ctrl.pick.text = $ctrl.prefs.textColor;
      $ctrl.lookCheck = readability();
      $ctrl.showPreferences = true;
    };
    // From elsewhere, e.g. the AI Assistant's tools panel.
    $scope.$on('open:preferences', function () { $ctrl.openPreferences(); });
    $ctrl.currentTheme = function () { return $ctrl.prefs.theme || ((themeService.current() || {}).id) || 'dark'; };
    $ctrl.setPref = function (key, value) {
      var changes = {};
      changes[key] = value;
      savePrefs(changes);
    };
    function savePrefs(changes) {
      angular.forEach(changes, function (v, k) { if (v === null) delete $ctrl.prefs[k]; else $ctrl.prefs[k] = v; });
      $ctrl.prefsError = null;
      $ctrl.lookCheck = readability();
      accountService.setAll(changes).then(function () {
        $ctrl.prefsSaved = true;
        $timeout(function () { $ctrl.prefsSaved = false; }, 2000);
      }).catch(function (e) { $ctrl.prefsError = e.message; });
    }

    // Appearance: font, text size, header and text colour, for every screen.
    $ctrl.look = { fonts: appearance.FONTS.slice(1), sizes: appearance.SIZES, headers: appearance.HEADERS };
    $ctrl.pick = { header: '#0b6bcb', text: '#1f2937' };
    $ctrl.isPresetHeader = function () { return $ctrl.look.headers.some(function (h) { return h.id && h.id === $ctrl.prefs.headerColor; }); };
    $ctrl.isCustomText = function () { return !!$ctrl.prefs.textColor && $ctrl.prefs.textColor !== 'contrast'; };
    $ctrl.lookIsDefault = function () { return appearance.isDefault($ctrl.prefs); };
    $ctrl.resetAppearance = function () {
      var changes = {};
      appearance.KEYS.forEach(function (k) { changes[k] = null; });
      savePrefs(changes);
    };
    /** A custom text colour's contrast with the page, so an unreadable choice is flagged. */
    function readability() {
      if (!$ctrl.prefs || !$ctrl.isCustomText()) return null;
      var bg = $window.getComputedStyle($window.document.documentElement).getPropertyValue('--surface');
      return appearance.readability($ctrl.prefs.textColor, bg);
    }
    $scope.$on('prefs:changed', function () { if ($ctrl.showPreferences) $ctrl.lookCheck = readability(); }); // theme changes too
    $ctrl.timeZoneLabel = accountService.timeZoneLabel;
    $ctrl.pageSizes = [10, 25, 50, 100];

    // Help: user guide, report a problem (an email with what support always asks for), shortcuts.
    $ctrl.reportProblemHref = function () {
      var a = $ctrl.account();
      if (!a || !a.support || !a.support.email) return null;
      var u = authService.user() || {};
      var lines = [
        'What happened:', '', '', 'What I expected:', '', '',
        '— Details for support —',
        'User: ' + (u.userName || '') + (u.displayName ? ' (' + u.displayName + ')' : ''),
        'Page: ' + $window.location.origin + $window.location.pathname + '#!' + $location.path(),
        'Portal ' + APP_VERSION + ' · API ' + (a.version || '?') + ' · ' + (a.environment || ''),
        'Time: ' + $ctrl.menuOpenedAt,
        'Browser: ' + $window.navigator.userAgent
      ];
      return 'mailto:' + encodeURIComponent(a.support.email) + '?subject=' + encodeURIComponent('ECCO portal problem') +
        '&body=' + encodeURIComponent(lines.join('\n'));
    };

    $ctrl.openShortcuts = function () { $ctrl.closeUserMenu(); $ctrl.showShortcuts = true; };
    $ctrl.shortcuts = [
      { keys: ['/'], text: 'Search' },
      { keys: ['b'], text: 'Breached only (switch on or off)' },
      { keys: ['i'], text: 'Open or close the AI Assistant' },
      { keys: ['['], text: 'Previous page' },
      { keys: [']'], text: 'Next page' },
      { keys: ['g', 'c'], text: 'Go to Correspondence' },
      { keys: ['g', 'a'], text: 'Go to Administration' },
      { keys: ['?'], text: 'Show these shortcuts' },
      { keys: ['Esc'], text: 'Close a menu or dialog' }
    ];

    // Keyboard shortcuts: single keys, ignored while typing, with a modifier, or while a dialog is open
    // (except Esc). "g" starts a two-key "go to".
    var pendingG = null;
    function typing(target) {
      var tag = target && target.tagName;
      return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (target && target.isContentEditable);
    }
    function onKeydown(e) {
      if (!$ctrl.signedIn()) return;
      if (e.key === 'Escape') {
        if ($ctrl.showShortcuts || $ctrl.showPreferences || $ctrl.userMenuOpen || $ctrl.alertsOpen) {
          $scope.$apply(function () { $ctrl.showShortcuts = false; $ctrl.showPreferences = false; $ctrl.closeUserMenu(); $ctrl.alertsOpen = false; });
        }
        return;
      }
      if (e.ctrlKey || e.metaKey || e.altKey || typing(e.target) || $ctrl.showChangePassword || $ctrl.showPreferences) return;
      if ($document[0].querySelector('.dialog-backdrop:not(.shell-dialog)')) return; // a page's own dialog is open
      var key = e.key;
      if (pendingG) {
        $timeout.cancel(pendingG);
        pendingG = null;
        if (key === 'c' || key === 'a') {
          e.preventDefault();
          $scope.$apply(function () { if (key === 'a' && canAdminister()) $location.path('/admin').search({}); else if (key === 'c') $location.path('/').search({}); });
        }
        return;
      }
      if (key === 'g') { pendingG = $timeout(function () { pendingG = null; }, 1200, false); return; }
      if (key === '?') { e.preventDefault(); $scope.$apply(function () { $ctrl.showShortcuts = !$ctrl.showShortcuts; }); return; }
      if ($ctrl.showShortcuts) return;
      if (key === '/') {
        var search = $document[0].querySelector('.toolbar__search');
        if (search) { e.preventDefault(); search.focus(); search.select(); }
        return;
      }
      var action = { b: 'breachedOnly', i: 'assistant', '[': 'previousPage', ']': 'nextPage' }[key];
      if (action && $ctrl.view === 'search') { e.preventDefault(); $scope.$apply(function () { $scope.$broadcast('shortcut:' + action); }); }
    }
    $document.on('keydown', onKeydown);
    $scope.$on('$destroy', function () { $document.off('keydown', onKeydown); });

    $ctrl.user = function () { return authService.user(); };
    $ctrl.signedIn = function () { var u = authService.user(); return !!u && !u.mustChangePassword; };
    $ctrl.mustChangePassword = function () { var u = authService.user(); return !!u && u.mustChangePassword; };
    $ctrl.canAdminister = canAdminister;

    $ctrl.initials = function () {
      var u = authService.user();
      var name = (u && (u.displayName || u.userName)) || '?';
      return name.split(/\s+/).filter(Boolean).slice(0, 2).map(function (p) { return p.charAt(0).toUpperCase(); }).join('');
    };

    $ctrl.go = function (path) {
      $ctrl.userMenuOpen = false;
      $location.path(path);
    };

    $ctrl.openChangePassword = function () {
      $ctrl.userMenuOpen = false;
      $ctrl.showChangePassword = true;
    };

    $ctrl.signOut = function () {
      $ctrl.userMenuOpen = false;
      ssoService.forget();
      authService.logout();
      $location.path('/');
    };
  }
})();
