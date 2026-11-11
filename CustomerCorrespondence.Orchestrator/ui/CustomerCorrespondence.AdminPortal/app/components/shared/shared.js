(function () {
  'use strict';

  var app = angular.module('adminPortal');

  /** <div cc-click-outside="$ctrl.open = false"> — runs the expression on a click outside the element. */
  app.directive('ccClickOutside', ['$document', function ($document) {
    return {
      restrict: 'A',
      link: function (scope, element, attrs) {
        function onClick(event) {
          if (element[0].contains(event.target)) return;
          scope.$applyAsync(attrs.ccClickOutside);
        }
        $document.on('mousedown', onClick);
        scope.$on('$destroy', function () { $document.off('mousedown', onClick); });
      }
    };
  }]);

  /** <input cc-focus-when="$ctrl.show"> — focuses (and selects) the element when the expression turns truthy. */
  app.directive('ccFocusWhen', ['$timeout', function ($timeout) {
    return {
      restrict: 'A',
      link: function (scope, element, attrs) {
        scope.$watch(attrs.ccFocusWhen, function (value) {
          if (value) $timeout(function () { element[0].focus(); if (element[0].select) element[0].select(); });
        });
      }
    };
  }]);

  /** MM/dd/yyyy HH:mm:ss in local time; returns the raw value if it isn't a parseable date, '—' if empty. */
  /**
   * {{ status | ccStatusTone }} — the colour family of a correspondence status, shared by the grid's row edge and every
   * status badge: completed (green, finished well) · processing (amber, in progress / waiting on a vendor) ·
   * submitted (blue, received, not started) · failed (red, needs action) · unknown (grey). Names are matched
   * without case or punctuation, and unlisted ones by their wording (…Confirmed, Awaiting…, …Error).
   */
  var STATUS_TONES = {
    completed: 'completed', delivered: 'completed', edeliveryconfirmed: 'completed', imagingconfirmed: 'completed',
    archived: 'completed', printed: 'completed', mailed: 'completed', sent: 'completed',
    processing: 'processing', inprogress: 'processing', pending: 'processing', retrying: 'processing',
    awaitingimaging: 'processing', awaitingedelivery: 'processing', sentforprint: 'processing', sentforimaging: 'processing',
    submitted: 'submitted', new: 'submitted', received: 'submitted', queued: 'submitted',
    failed: 'failed', error: 'failed', rejected: 'failed', returned: 'failed', undeliverable: 'failed'
  };
  function statusTone(status) {
    var key = String(status || '').toLowerCase().replace(/[^a-z]/g, '');
    if (!key) return 'unknown';
    if (STATUS_TONES[key]) return STATUS_TONES[key];
    if (/fail|error|reject|undeliver/.test(key)) return 'failed';
    if (/^awaiting|^sentfor|progress|pending|retry/.test(key)) return 'processing';
    if (/confirm|delivered|complete|archiv/.test(key)) return 'completed';
    if (/submit|received|queued/.test(key)) return 'submitted';
    return 'unknown';
  }
  app.constant('statusTone', statusTone);
  app.filter('ccStatusTone', function () { return statusTone; });

  /**
   * Time zone for dates: { utc: false } shows the browser's local time (the default); true shows UTC.
   * Set from the user's preference by accountService.
   *
   * The API's timestamps are UTC. SQL datetime values arrive without a zone ("2026-11-04T11:53:17"), which
   * browsers would read as local time, so a timestamp with no zone is read as UTC everywhere here.
   */
  app.value('displayTime', { utc: false });

  var HAS_TIME = /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/;
  var HAS_ZONE = /(Z|[+-]\d{2}:?\d{2})$/i;
  function asUtcTimestamp(raw) {
    return typeof raw === 'string' && HAS_TIME.test(raw) && !HAS_ZONE.test(raw) ? raw.replace(' ', 'T') + 'Z' : raw;
  }
  app.constant('asUtcTimestamp', asUtcTimestamp);

  // Angular's date filter: same timestamp rule, and the user's time zone. (Stateful: the zone can change.)
  app.config(['$provide', function ($provide) {
    $provide.decorator('dateFilter', ['$delegate', 'displayTime', function ($delegate, displayTime) {
      function dateFilter(input, format, timezone) {
        var timestamp = typeof input === 'string' && HAS_TIME.test(input);
        input = asUtcTimestamp(input);
        if (!timezone && displayTime.utc && (timestamp || typeof input === 'number')) timezone = 'UTC';
        return $delegate(input, format, timezone);
      }
      dateFilter.$stateful = true;
      return dateFilter;
    }]);
  }]);

  app.filter('ccDateTime', ['displayTime', function (displayTime) {
    function pad(n) { return (n < 10 ? '0' : '') + n; }
    function ccDateTime(raw) {
      if (!raw) return '—';
      var d = new Date(asUtcTimestamp(raw));
      if (isNaN(d.getTime())) return raw;
      if (displayTime.utc) {
        return pad(d.getUTCMonth() + 1) + '/' + pad(d.getUTCDate()) + '/' + d.getUTCFullYear() + ' ' +
          pad(d.getUTCHours()) + ':' + pad(d.getUTCMinutes()) + ':' + pad(d.getUTCSeconds());
      }
      return pad(d.getMonth() + 1) + '/' + pad(d.getDate()) + '/' + d.getFullYear() + ' ' +
        pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
    }
    ccDateTime.$stateful = true;
    return ccDateTime;
  }]);

  /** 1234 → "1.2k" */
  app.filter('ccTokens', function () {
    return function (n) {
      n = Number(n) || 0;
      return n >= 1000 ? (n / 1000).toFixed(1) + 'k' : String(n);
    };
  });

  /** "1 record" / "3 records" — usage: {{ n | ccPlural:'record' }} */
  app.filter('ccPlural', function () {
    return function (n, word, plural) {
      return n + ' ' + (n === 1 ? word : (plural || word + 's'));
    };
  });

  /**
   * <smart-error-alert error="…" is-retrying="…" on-retry="…()" on-dismiss="…()">
   * Error message + recovery suggestion, with Retry when the error is retryable.
   */
  app.component('smartErrorAlert', {
    bindings: { error: '<', isRetrying: '<?', onRetry: '&?', onDismiss: '&?' },
    template:
      '<div class="alert alert--danger smart-error" ng-if="$ctrl.error && !$ctrl.dismissed" role="alert">' +
      '  <span class="smart-error__icon" aria-hidden="true">{{ $ctrl.icon() }}</span>' +
      '  <div class="smart-error__body">' +
      '    <strong>{{ $ctrl.message() }}</strong>' +
      '    <div class="smart-error__hint" ng-if="$ctrl.error.suggestion">💡 {{ $ctrl.error.suggestion }}</div>' +
      '  </div>' +
      '  <div class="smart-error__actions">' +
      '    <button type="button" class="btn btn--xs btn--outline" ng-if="$ctrl.canRetry()" ng-disabled="$ctrl.isRetrying" ng-click="$ctrl.retry()">' +
      '      <span class="spinner spinner--sm" ng-if="$ctrl.isRetrying"></span>{{ $ctrl.isRetrying ? \'Retrying...\' : \'🔄 Retry\' }}' +
      '    </button>' +
      '    <button type="button" class="btn btn--xs btn--outline" ng-disabled="$ctrl.isRetrying" ng-click="$ctrl.dismiss()" aria-label="Dismiss">✕</button>' +
      '  </div>' +
      '</div>',
    controller: function () {
      var $ctrl = this;
      var ICONS = { network: '📡', validation: '⚠️', server: '🔧', unknown: '❌' };
      $ctrl.$onChanges = function (changes) {
        if (changes.error) $ctrl.dismissed = false;
      };
      $ctrl.icon = function () { return ICONS[$ctrl.error.category] || '❌'; };
      $ctrl.message = function () {
        return typeof $ctrl.error === 'string' ? $ctrl.error : ($ctrl.error.message || 'An error occurred');
      };
      $ctrl.canRetry = function () { return $ctrl.error.isRetryable !== false && !!$ctrl.onRetry; };
      $ctrl.retry = function () { $ctrl.onRetry(); };
      $ctrl.dismiss = function () {
        $ctrl.dismissed = true;
        if ($ctrl.onDismiss) $ctrl.onDismiss();
      };
    }
  });

  /**
   * <confirm-dialog show="…" title="…" message="'…'" confirm-label="…" danger="true"
   *                 on-confirm="…()" on-cancel="…()">
   * Themed stand-in for window.confirm().
   */
  app.component('confirmDialog', {
    bindings: {
      show: '<', title: '@?', message: '<', confirmLabel: '@?', cancelLabel: '@?', danger: '<?',
      onConfirm: '&', onCancel: '&'
    },
    template:
      '<div class="dialog-backdrop" ng-if="$ctrl.show" ng-click="$ctrl.onCancel()">' +
      '  <div class="dialog dialog--sm" role="dialog" aria-modal="true" ng-click="$event.stopPropagation()">' +
      '    <div class="dialog__header"><span>{{ $ctrl.title || \'Confirm\' }}</span>' +
      '      <button type="button" class="dialog__close" aria-label="Close" ng-click="$ctrl.onCancel()">×</button></div>' +
      '    <div class="dialog__body">{{ $ctrl.message }}</div>' +
      '    <div class="dialog__footer">' +
      '      <button type="button" class="btn btn--secondary" ng-click="$ctrl.onCancel()">{{ $ctrl.cancelLabel || \'Cancel\' }}</button>' +
      '      <button type="button" class="btn" ng-class="$ctrl.danger ? \'btn--danger\' : \'btn--primary\'" ng-click="$ctrl.onConfirm()">{{ $ctrl.confirmLabel || \'Confirm\' }}</button>' +
      '    </div>' +
      '  </div>' +
      '</div>'
  });

  /**
   * <prompt-dialog show="…" title="…" message="…" default-value="…" on-submit="…(value)" on-cancel="…()">
   * Themed stand-in for window.prompt().
   */
  app.component('promptDialog', {
    bindings: {
      show: '<', title: '@?', message: '@?', defaultValue: '<?', confirmLabel: '@?', cancelLabel: '@?',
      onSubmit: '&', onCancel: '&'
    },
    template:
      '<div class="dialog-backdrop" ng-if="$ctrl.show" ng-click="$ctrl.onCancel()">' +
      '  <div class="dialog dialog--sm" role="dialog" aria-modal="true" ng-click="$event.stopPropagation()">' +
      '    <div class="dialog__header"><span>{{ $ctrl.title || \'Enter a value\' }}</span>' +
      '      <button type="button" class="dialog__close" aria-label="Close" ng-click="$ctrl.onCancel()">×</button></div>' +
      '    <div class="dialog__body">' +
      '      <div class="dialog__message" ng-if="$ctrl.message">{{ $ctrl.message }}</div>' +
      '      <input type="text" class="input" ng-model="$ctrl.value" cc-focus-when="$ctrl.show"' +
      '             ng-keydown="$event.key === \'Enter\' && $ctrl.submit()">' +
      '    </div>' +
      '    <div class="dialog__footer">' +
      '      <button type="button" class="btn btn--secondary" ng-click="$ctrl.onCancel()">{{ $ctrl.cancelLabel || \'Cancel\' }}</button>' +
      '      <button type="button" class="btn btn--primary" ng-disabled="!$ctrl.trimmed()" ng-click="$ctrl.submit()">{{ $ctrl.confirmLabel || \'Save\' }}</button>' +
      '    </div>' +
      '  </div>' +
      '</div>',
    controller: function () {
      var $ctrl = this;
      $ctrl.$onChanges = function (changes) {
        if ((changes.show && $ctrl.show) || changes.defaultValue) $ctrl.value = $ctrl.defaultValue || '';
      };
      $ctrl.trimmed = function () { return ($ctrl.value || '').trim(); };
      $ctrl.submit = function () {
        if ($ctrl.trimmed()) $ctrl.onSubmit({ value: $ctrl.trimmed() });
      };
    }
  });

  /**
   * <toast-message toast="{ variant: 'success'|'danger', title, message }" on-close="…()">
   * Auto-hides after 6 seconds.
   */
  app.component('toastMessage', {
    bindings: { toast: '<', onClose: '&' },
    template:
      '<div class="toast" ng-if="$ctrl.toast" ng-class="\'toast--\' + $ctrl.toast.variant" role="status">' +
      '  <div class="toast__header"><strong>{{ $ctrl.toast.title || ($ctrl.toast.variant === \'success\' ? \'Success\' : \'Error\') }}</strong>' +
      '    <button type="button" class="dialog__close" aria-label="Close" ng-click="$ctrl.onClose()">×</button></div>' +
      '  <div class="toast__body">{{ $ctrl.toast.message }}</div>' +
      '</div>',
    controller: ['$timeout', function ($timeout) {
      var $ctrl = this;
      var timer = null;
      $ctrl.$onChanges = function () {
        if (timer) $timeout.cancel(timer);
        if ($ctrl.toast) timer = $timeout(function () { $ctrl.onClose(); }, 6000);
      };
      $ctrl.$onDestroy = function () { if (timer) $timeout.cancel(timer); };
    }]
  });

  /**
   * <collapsible-card title="…" count="n" default-open="false" tone="insight|tip">
   *   <card-extra>…header control…</card-extra>
   *   …body…
   * </collapsible-card>
   * One consistent card shape for every tile in the assistant's Ask tab.
   */
  app.component('collapsibleCard', {
    bindings: { title: '@', count: '<?', defaultOpen: '<?', tone: '@?' },
    transclude: { extra: '?cardExtra' },
    template:
      '<div class="card-tile" ng-class="$ctrl.tone ? \'card-tile--\' + $ctrl.tone : \'\'">' +
      '  <div class="card-tile__header">' +
      '    <button type="button" class="card-tile__toggle" ng-click="$ctrl.open = !$ctrl.open" aria-expanded="{{ $ctrl.open }}">' +
      '      <span class="card-tile__chevron" ng-class="{ \'is-open\': $ctrl.open }" aria-hidden="true">▸</span>' +
      '      <span class="card-tile__title">{{ $ctrl.title }}<span ng-if="$ctrl.count !== undefined && $ctrl.count !== null"> ({{ $ctrl.count }})</span></span>' +
      '    </button>' +
      '    <span ng-transclude="extra"></span>' +
      '  </div>' +
      '  <div class="card-tile__body" ng-show="$ctrl.open" ng-transclude></div>' +
      '</div>',
    controller: function () {
      var $ctrl = this;
      $ctrl.$onInit = function () { $ctrl.open = $ctrl.defaultOpen !== false; };
    }
  });

  /** Light/dark switch for the header. */
  /**
   * <theme-toggle> — Light / Dark / System as a three-button switch (the sign-in page; signed-in users have Preferences).
   * The highlighted button is the current setting, so it reads as a state, not as an action.
   */
  var THEME_ICONS = {
    light: '<circle cx="12" cy="12" r="4"/><path d="M12 2.5v2M12 19.5v2M4.6 4.6l1.4 1.4M18 18l1.4 1.4M2.5 12h2M19.5 12h2M4.6 19.4 6 18M18 6l1.4-1.4"/>',
    dark: '<path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z"/>',
    system: '<rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/>'
  };
  app.component('themeToggle', {
    template:
      '<div class="theme-switch" role="radiogroup" aria-label="Theme">' +
      ['light', 'dark', 'system'].map(function (id) {
        var label = { light: 'Light', dark: 'Dark', system: 'Match system' }[id];
        return '<button type="button" class="theme-switch__btn" role="radio" ng-class="{ \'is-active\': $ctrl.is(\'' + id + '\') }"' +
          ' aria-checked="{{ $ctrl.is(\'' + id + '\') }}" title="' + label + ' theme" aria-label="' + label + ' theme" ng-click="$ctrl.set(\'' + id + '\')">' +
          '<svg viewBox="0 0 24 24" aria-hidden="true">' + THEME_ICONS[id] + '</svg></button>';
      }).join('') +
      '</div>',
    controller: ['themeService', function (themeService) {
      var $ctrl = this;
      $ctrl.is = function (id) { return (themeService.current() || {}).id === id; };
      $ctrl.set = function (id) { themeService.set(id); };
    }]
  });
})();
