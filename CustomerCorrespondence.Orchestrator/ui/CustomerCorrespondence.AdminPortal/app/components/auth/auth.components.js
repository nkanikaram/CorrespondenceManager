(function () {
  'use strict';

  var app = angular.module('adminPortal');

  /** <login-page> — the sign-in screen. */
  app.component('loginPage', {
    templateUrl: 'app/components/auth/login-page.html',
    controller: LoginPageController
  });

  LoginPageController.$inject = ['$scope', '$location', 'authService', 'ssoService'];

  /**
   * Views: 'signin' (default), 'forgot' (ask for a reset link), 'sent' (check your inbox),
   * 'reset' (choose a new password from an emailed #!/reset-password?token=… link), 'linkInvalid'.
   */
  function LoginPageController($scope, $location, authService, ssoService) {
    var $ctrl = this;
    var resetToken = null;

    $ctrl.$onInit = function () {
      $ctrl.form = { userName: '', password: '' };
      $ctrl.notice = authService.notice();
      $ctrl.showPassword = false;
      $ctrl.view = 'signin';
      $ctrl.passwordReset = false;
      $ctrl.minLength = 12;
      openResetLink();
      $scope.$on('$locationChangeSuccess', openResetLink);
      // Labels non-production hosts so nobody mistakes a test portal for the real one.
      var host = window.location.hostname;
      $ctrl.environment = host === 'localhost' || host === '127.0.0.1' ? 'Development'
        : /(^|[.-])(dev|test|uat|qa|stage|staging)([.-]|$)/i.test(host) ? 'Test' : null;
      $ctrl.loadingConfig = true;
      ssoService.loadConfig().then(function (config) {
        $ctrl.sso = config.entra.enabled;
        $ctrl.mockSso = !!config.entra.mock;
        $ctrl.mockAccounts = config.entra.mockAccounts || [];
        $ctrl.localLogin = config.localLogin.enabled;
        $ctrl.passwordReset = !!(config.passwordReset && config.passwordReset.enabled);
        $ctrl.linkMinutes = (config.passwordReset && config.passwordReset.linkLifetimeMinutes) || 30;
        // With SSO on, the password form is the exception (break-glass), so it starts folded away.
        $ctrl.showLocal = !$ctrl.sso;
      }).finally(function () { $ctrl.loadingConfig = false; });
    };

    // ── Forgot password ──

    $ctrl.showForgot = function () {
      $ctrl.error = null;
      $ctrl.notice = null;
      $ctrl.forgot = { value: ($ctrl.form.userName || '').trim(), submitted: false };
      $ctrl.view = 'forgot';
    };

    $ctrl.backToSignIn = function (notice) {
      $ctrl.error = null;
      $ctrl.notice = notice || null;
      $ctrl.view = 'signin';
      $ctrl.showLocal = true;
      if ($location.path() === '/reset-password') $location.path('/').search({}).replace();
    };

    $ctrl.sendResetLink = function (form) {
      $ctrl.error = null;
      if (form.$invalid) { $ctrl.forgot.submitted = true; return; }
      $ctrl.busy = true;
      authService.forgotPassword($ctrl.forgot.value.trim())
        .then(function (message) {
          $ctrl.sentMessage = message;
          $ctrl.view = 'sent';
        })
        .catch(function (err) { $ctrl.error = err.message; })
        .finally(function () { $ctrl.busy = false; });
    };

    /** Picks up an emailed link, then drops the token from the address bar (and so from browser history). */
    function openResetLink() {
      if ($location.path() !== '/reset-password') return;
      var token = $location.search().token;
      if (!token) { if (!resetToken) $ctrl.view = 'linkInvalid'; return; }
      resetToken = token;
      $location.search('token', null).replace();
      $ctrl.error = null;
      $ctrl.notice = null;
      $ctrl.reset = { next: '', confirm: '', userName: '', checking: true };
      $ctrl.view = 'reset';
      authService.passwordPolicy().then(function (policy) { $ctrl.minLength = policy.minLength || 12; }).catch(angular.noop);
      authService.checkResetLink(token)
        .then(function (info) { $ctrl.reset.userName = info.userName; $ctrl.reset.displayName = info.displayName; })
        .catch(function (err) {
          if (err.linkInvalid) { resetToken = null; $ctrl.view = 'linkInvalid'; } else { $ctrl.error = err.message; }
        })
        .finally(function () { $ctrl.reset.checking = false; });
    }

    var rulesCache = { key: null, rules: [] };
    /** Live checklist mirroring the API's rules (memoized: fresh objects every digest would never settle). */
    $ctrl.resetRules = function () {
      var p = ($ctrl.reset && $ctrl.reset.next) || '';
      var user = ($ctrl.reset && $ctrl.reset.userName) || '';
      var key = [p, user, $ctrl.minLength].join('\u0000');
      if (key === rulesCache.key) return rulesCache.rules;
      var classes = [/[A-Z]/, /[a-z]/, /[0-9]/, /[^A-Za-z0-9]/].filter(function (re) { return re.test(p); }).length;
      rulesCache.key = key;
      return (rulesCache.rules = [
        { text: 'At least ' + $ctrl.minLength + ' characters', ok: p.length >= $ctrl.minLength },
        { text: 'Three of: upper case, lower case, digits, symbols', ok: classes >= 3 },
        { text: 'Doesn\'t contain your user name', ok: !!p && (!user || p.toLowerCase().indexOf(user.toLowerCase()) === -1) }
      ]);
    };

    $ctrl.canReset = function () {
      return !!$ctrl.reset && !$ctrl.reset.checking && !!$ctrl.reset.userName && $ctrl.reset.next === $ctrl.reset.confirm &&
        $ctrl.resetRules().every(function (r) { return r.ok; });
    };

    $ctrl.submitReset = function () {
      if (!$ctrl.canReset()) return;
      $ctrl.busy = true;
      $ctrl.error = null;
      $ctrl.problems = [];
      authService.resetPassword(resetToken, $ctrl.reset.next)
        .then(function (res) {
          resetToken = null;
          $ctrl.form = { userName: res.userName || $ctrl.reset.userName, password: '' };
          $ctrl.backToSignIn('Your password has been changed. Sign in with your new password.');
          $ctrl.focusPassword = ($ctrl.focusPassword || 0) + 1;
        })
        .catch(function (err) {
          if (err.linkInvalid) { resetToken = null; $ctrl.view = 'linkInvalid'; return; }
          $ctrl.error = err.message;
          $ctrl.problems = err.problems;
        })
        .finally(function () { $ctrl.busy = false; });
    };

    /** Microsoft sign-in: a full-page redirect (or the mock server's account picker). */
    $ctrl.signInWithMicrosoft = function (mockAccount) {
      if ($ctrl.mockSso && !mockAccount) { $ctrl.pickAccount = true; return; }
      $ctrl.error = null;
      $ctrl.ssoBusy = true;
      $ctrl.pickAccount = false;
      ssoService.signIn(mockAccount).catch(function (err) {
        $ctrl.error = err.message;
        $ctrl.ssoBusy = false;
      });
    };

    $ctrl.submit = function (form) {
      $ctrl.error = null;
      if (form.$invalid) {
        $ctrl.submitted = true;
        return;
      }
      $ctrl.busy = true;
      authService.login($ctrl.form.userName.trim(), $ctrl.form.password)
        .catch(function (err) {
          $ctrl.error = err.message;
          $ctrl.locked = err.locked;
          $ctrl.form.password = '';
          $ctrl.focusPassword = ($ctrl.focusPassword || 0) + 1;
        })
        .finally(function () { $ctrl.busy = false; });
    };

    /** Caps Lock is a common cause of "wrong password" — say so before it happens. */
    $ctrl.checkCaps = function (event) {
      if (event && typeof event.getModifierState === 'function') $ctrl.capsLock = event.getModifierState('CapsLock');
    };
  }

  /**
   * <change-password-form forced="bool" on-done="…()" on-cancel="…()">
   * Self-service password change. "forced" is the first sign-in with a temporary password:
   * there the cancel action signs out instead of closing.
   */
  app.component('changePasswordForm', {
    bindings: { forced: '<?', onDone: '&?', onCancel: '&?' },
    templateUrl: 'app/components/auth/change-password-form.html',
    controller: ChangePasswordFormController
  });

  ChangePasswordFormController.$inject = ['authService'];

  function ChangePasswordFormController(authService) {
    var $ctrl = this;

    $ctrl.$onInit = function () {
      $ctrl.form = { current: '', next: '', confirm: '' };
      $ctrl.minLength = 12;
      authService.passwordPolicy().then(function (policy) { $ctrl.minLength = policy.minLength || 12; }).catch(angular.noop);
    };

    /**
     * Live checklist mirroring the API's rules (the API has the final say). Memoized on its inputs:
     * ng-repeat over freshly built objects every digest would never settle.
     */
    var rulesCache = { key: null, rules: [] };
    $ctrl.rules = function () {
      var p = $ctrl.form.next || '';
      var user = (authService.user() || {}).userName || '';
      var key = [p, $ctrl.form.current, user, $ctrl.minLength].join('\u0000');
      if (key === rulesCache.key) return rulesCache.rules;
      var classes = [/[A-Z]/, /[a-z]/, /[0-9]/, /[^A-Za-z0-9]/].filter(function (re) { return re.test(p); }).length;
      rulesCache.key = key;
      return (rulesCache.rules = [
        { text: 'At least ' + $ctrl.minLength + ' characters', ok: p.length >= $ctrl.minLength },
        { text: 'Three of: upper case, lower case, digits, symbols', ok: classes >= 3 },
        { text: 'Doesn\'t contain your user name', ok: !!p && (!user || p.toLowerCase().indexOf(user.toLowerCase()) === -1) },
        { text: 'Different from your current password', ok: !!p && p !== $ctrl.form.current }
      ]);
    };

    $ctrl.canSubmit = function () {
      return !!$ctrl.form.current && $ctrl.form.next === $ctrl.form.confirm && $ctrl.rules().every(function (r) { return r.ok; });
    };

    $ctrl.submit = function () {
      if (!$ctrl.canSubmit()) return;
      $ctrl.busy = true;
      $ctrl.error = null;
      $ctrl.problems = [];
      authService.changePassword($ctrl.form.current, $ctrl.form.next)
        .then(function () {
          $ctrl.done = true;
          if ($ctrl.onDone) $ctrl.onDone();
        })
        .catch(function (err) {
          $ctrl.error = err.message;
          $ctrl.problems = err.problems || [];
        })
        .finally(function () { $ctrl.busy = false; });
    };
  }
})();
