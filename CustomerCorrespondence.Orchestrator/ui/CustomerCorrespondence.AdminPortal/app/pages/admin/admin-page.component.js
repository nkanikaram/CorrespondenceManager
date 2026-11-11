(function () {
  'use strict';

  /**
   * <admin-page> — portal access administration.
   *   Users            accounts, roles (RBAC) and data-access attributes (ABAC)
   *   Roles            which permissions each role grants
   *   Access policies  attribute-based deny rules, evaluated after the role check
   *   Audit log        sign-ins, denials and administrative changes
   *   Microsoft sign-in  Entra ID group / app-role → role and attribute mappings
   *   Recycle bin      deleted records (restorable until purged)
   *   Letter templates branded, versioned customer letters per document type
   *   Alerts           server-side alerts and the rules that raise them
   * Every change is validated and audited by the API; this page only collects input.
   */
  angular.module('adminPortal').component('adminPage', {
    templateUrl: 'app/pages/admin/admin-page.html',
    controller: AdminPageController
  });

  var ALL = '*';
  var ATTR = {
    customer: 'scope.customer',
    delivery: 'scope.deliveryMethod',
    pii: 'piiClearance',
    exportLimit: 'maxExportRows'
  };

  /** Readable form of an access-policy condition, e.g. 'status is not one of "Failed"'. */
  function describeCondition(json) {
    var OPS = {
      eq: 'is', neq: 'is not', 'in': 'is one of', notIn: 'is not one of', exists: 'is set', notExists: 'is not set',
      gt: 'is more than', gte: 'is at least', lt: 'is less than', lte: 'is at most'
    };
    function name(attr) {
      var map = {
        'resource.status': 'the record status', 'resource.customer': 'the customer', 'resource.deliveryMethod': 'the delivery method',
        'resource.rowCount': 'the number of rows', 'subject.piiClearance': 'the user\'s PII clearance',
        'subject.scope.customer': 'the user\'s customer scope', 'subject.maxExportRows': 'the user\'s export row limit',
        'env.hourUtc': 'the hour (UTC)', 'env.dayOfWeek': 'the day of the week'
      };
      return map[attr] || attr;
    }
    function node(n) {
      if (n.all || n.any) return n[n.all ? 'all' : 'any'].map(node).join(n.all ? ' and ' : ' or ');
      var values = n.values ? n.values.map(function (v) { return '"' + v + '"'; }).join(', ') : n.valuesFrom ? name(n.valuesFrom) : '';
      return name(n.attr) + ' ' + (OPS[n.op] || n.op) + (values ? ' ' + values : '');
    }
    try { return node(JSON.parse(json)); } catch (e) { return 'Unreadable condition'; }
  }

  function randomPassword() {
    var sets = ['ABCDEFGHJKLMNPQRSTUVWXYZ', 'abcdefghijkmnopqrstuvwxyz', '23456789', '!@#$%*-_+?'];
    var all = sets.join('');
    var bytes = new Uint32Array(16);
    window.crypto.getRandomValues(bytes);
    var chars = sets.map(function (set, i) { return set.charAt(bytes[i] % set.length); }); // one of each class
    for (var i = sets.length; i < bytes.length; i++) chars.push(all.charAt(bytes[i] % all.length));
    for (var j = chars.length - 1; j > 0; j--) { // shuffle so the classes aren't in a fixed order
      var k = bytes[j] % (j + 1);
      var t = chars[j]; chars[j] = chars[k]; chars[k] = t;
    }
    return chars.join('');
  }

  AdminPageController.$inject = ['$q', '$scope', '$location', 'adminApi', 'authService', 'correspondenceApi'];

  var TAB_PERMISSIONS = {
    users: 'admin.users.manage', roles: 'admin.users.manage', policies: 'admin.users.manage', sso: 'admin.users.manage',
    audit: 'admin.audit.read', recycle: 'correspondence.recycle', letters: 'letters.templates.manage', alerts: 'alerts.read',
    sla: 'sla.manage'
  };
  var TAB_ORDER = ['users', 'roles', 'policies', 'sso', 'recycle', 'letters', 'alerts', 'sla', 'audit'];

  function AdminPageController($q, $scope, $location, adminApi, authService, correspondenceApi) {
    var $ctrl = this;

    /** SLA alerts: open the grid with "Breached only" on. */
    $ctrl.showBreached = function () { $location.path('/').search({ sla: 'breached' }); };
    $ctrl.can = authService.can;
    $ctrl.describeCondition = describeCondition;

    $ctrl.$onInit = function () {
      $ctrl.userFilter = '';
      $ctrl.audit = { rows: [], top: 200, userName: '' };
      $ctrl.recycle = { search: '', items: [], selected: {} };
      $ctrl.letters = { groups: [], placeholders: [] };
      $ctrl.alertsTab = { items: [], openOnly: false };
      $ctrl.sso = { mappings: [], form: { claimType: 'group', grant: 'role' } };
      if (authService.can('admin.users.manage')) loadAll();
      $ctrl.setTab(requestedTab() || TAB_ORDER.filter($ctrl.canTab)[0]);
    };

    $ctrl.canTab = function (tab) { return authService.can(TAB_PERMISSIONS[tab]); };

    function requestedTab() {
      var t = $location.search().tab;
      return t && TAB_PERMISSIONS[t] && $ctrl.canTab(t) ? t : null;
    }
    // "View all alerts" in the header while this page is already open.
    $scope.$on('$locationChangeSuccess', function () {
      var t = requestedTab();
      if (t && t !== $ctrl.tab) $ctrl.setTab(t);
    });

    function fail(err) {
      $ctrl.loadError = (err && err.message) || 'Could not load administration data.';
    }

    function loadAll() {
      $ctrl.loading = true;
      $ctrl.loadError = null;
      return $q.all({
        users: adminApi.users(),
        roles: adminApi.roles(),
        permissions: adminApi.permissions(),
        attributes: adminApi.attributes(),
        policies: adminApi.policies(),
        customers: adminApi.lookup('customer').catch(function () { return []; }),
        deliveries: adminApi.lookup('delivery').catch(function () { return []; })
      }).then(function (d) {
        $ctrl.users = d.users;
        $ctrl.roles = d.roles;
        $ctrl.permissions = d.permissions;
        $ctrl.attributeDefs = d.attributes;
        $ctrl.policies = d.policies.map(function (p) { return angular.extend({}, p, { summary: describeCondition(p.conditionJson) }); });
        $ctrl.customers = d.customers;
        $ctrl.deliveries = d.deliveries;
        buildMatrix();
      }).catch(fail).finally(function () { $ctrl.loading = false; });
    }

    // ── SLA: targets in days (default + per customer), business/calendar days, "at risk" threshold, finishing statuses ──
    $ctrl.slaTab = { loading: false };
    $ctrl.loadSla = function () {
      var t = $ctrl.slaTab;
      t.loading = true; t.error = null; t.problems = []; t.flash = null;
      $q.all({
        config: adminApi.sla(),
        statuses: correspondenceApi.getStatusValues().catch(function () { return []; }),
        customers: correspondenceApi.getFieldValues('customer').catch(function () { return []; })
      }).then(function (d) {
        setSlaForm(d.config);
        var known = (d.statuses || []).map(valueOf).filter(Boolean);
        // Keep any finishing status already saved, even if no record has it at the moment.
        t.statuses = known.concat(t.form.stopStatuses.filter(function (s) { return known.indexOf(s) === -1; })).sort();
        t.customers = (d.customers || []).map(valueOf).filter(Boolean);
      }).catch(function (err) { t.error = err.message; })
        .finally(function () { t.loading = false; });
    };
    function valueOf(v) { return typeof v === 'string' ? v : v && (v.value || v.name || v.status); }
    function setSlaForm(c) {
      $ctrl.slaTab.saved = c;
      $ctrl.slaTab.form = {
        defaultDays: Number(c.defaultDays), businessDays: !!c.businessDays, atRiskPercent: Number(c.atRiskPercent),
        stopStatuses: (c.stopStatuses || []).slice(),
        customers: (c.customers || []).map(function (x) { return { customerName: x.customerName, targetDays: Number(x.targetDays) }; })
      };
      $ctrl.slaTab.newCustomer = null;
      $ctrl.slaTab.problems = [];
    }
    $ctrl.slaIsStop = function (s) { return $ctrl.slaTab.form.stopStatuses.indexOf(s) !== -1; };
    $ctrl.slaToggleStop = function (s) {
      var list = $ctrl.slaTab.form.stopStatuses, i = list.indexOf(s);
      if (i === -1) list.push(s); else list.splice(i, 1);
    };
    var availableCache = { key: null, list: [] };
    $ctrl.slaAvailableCustomers = function () {
      var used = $ctrl.slaTab.form.customers.map(function (c) { return c.customerName.toLowerCase(); });
      var key = used.join('|') + '#' + ($ctrl.slaTab.customers || []).length;
      if (key !== availableCache.key) {
        availableCache = { key: key, list: ($ctrl.slaTab.customers || []).filter(function (c) { return used.indexOf(c.toLowerCase()) === -1; }) };
      }
      return availableCache.list;
    };
    $ctrl.slaAddCustomer = function () {
      var name = $ctrl.slaTab.newCustomer;
      if (!name) return;
      $ctrl.slaTab.form.customers.push({ customerName: name, targetDays: $ctrl.slaTab.form.defaultDays });
      $ctrl.slaTab.newCustomer = null;
    };
    $ctrl.slaRemoveCustomer = function (i) { $ctrl.slaTab.form.customers.splice(i, 1); };
    function slaKey(c) {
      return JSON.stringify([Number(c.defaultDays), !!c.businessDays, Number(c.atRiskPercent), (c.stopStatuses || []).slice().sort(),
        (c.customers || []).map(function (x) { return [x.customerName, Number(x.targetDays)]; })]);
    }
    $ctrl.slaDirty = function () {
      var t = $ctrl.slaTab;
      return !!(t.form && t.saved) && slaKey(t.form) !== slaKey(t.saved);
    };
    $ctrl.slaExample = function () {
      var f = $ctrl.slaTab.form;
      if (!f || !(f.defaultDays > 0) || !(f.atRiskPercent > 0)) return '';
      var unit = f.businessDays ? ' business days' : ' days';
      var risk = Math.round(f.defaultDays * f.atRiskPercent) / 100;
      return 'Example: a request created on Monday at 09:00 is due ' + f.defaultDays + unit + ' later' +
        (f.businessDays ? ' (weekends don\'t count)' : '') + ', and shows "At risk" after ' + risk + unit + '.';
    };
    $ctrl.saveSla = function () {
      var t = $ctrl.slaTab;
      t.saving = true; t.error = null; t.problems = []; t.flash = null;
      adminApi.saveSla(t.form).then(function (saved) {
        setSlaForm(saved);
        t.flash = 'SLA settings saved. Searches use them within 30 seconds.';
      }).catch(function (err) { t.error = err.message; t.problems = err.problems || []; })
        .finally(function () { t.saving = false; });
    };
    $ctrl.resetSla = function () { setSlaForm($ctrl.slaTab.saved); $ctrl.slaTab.error = null; };

    $ctrl.setTab = function (tab) {
      $ctrl.tab = tab;
      $ctrl.flash = null;
      $ctrl.createdPassword = null; // don't leave a password on screen
      if (tab === 'audit') $ctrl.loadAudit(); // show what just happened
      if (tab === 'sso') $ctrl.loadMappings();
      if (tab === 'recycle') $ctrl.loadRecycle();
      if (tab === 'letters') $ctrl.loadLetters();
      if (tab === 'alerts') $ctrl.loadAlerts();
      if (tab === 'sla') $ctrl.loadSla();
      if ($location.search().tab && $location.search().tab !== tab) $location.search('tab', null);
    };

    function flash(message) { $ctrl.flash = message; }

    // ════════ Users ════════

    $ctrl.visibleUsers = function () {
      var q = ($ctrl.userFilter || '').trim().toLowerCase();
      return ($ctrl.users || []).filter(function (u) {
        return !q || [u.userName, u.displayName, u.email].concat(u.roles).join(' ').toLowerCase().indexOf(q) !== -1;
      });
    };

    function attr(user, key) { return (user.attributes && user.attributes[key]) || []; }

    $ctrl.scopeText = function (user, key) {
      var values = attr(user, key);
      if (values.indexOf(ALL) !== -1) return 'All';
      if (!values.length) return 'None';
      return values.length === 1 ? values[0] : values.length + ' selected';
    };
    $ctrl.scopeTitle = function (user, key) {
      var values = attr(user, key);
      return values.indexOf(ALL) !== -1 ? 'All' : values.join(', ') || 'No access';
    };
    $ctrl.hasPii = function (user) { return attr(user, ATTR.pii)[0] === 'true'; };
    $ctrl.exportLimit = function (user) { return attr(user, ATTR.exportLimit)[0] || null; };

    $ctrl.isSso = function (user) { return !!(user && user.externalProvider); };
    /** Roles that come from the user's Entra groups (read-only here; change the group or the mapping). */
    $ctrl.groupRoles = function (user) { return (user && user.groupRoles) || []; };

    $ctrl.status = function (user) {
      if (!user.isActive) return { text: 'Inactive', css: 'muted' };
      if (user.isLockedOut) return { text: 'Locked', css: 'danger' };
      if (user.mustChangePassword) return { text: 'Password change due', css: 'warning' };
      return { text: 'Active', css: 'success' };
    };

    $ctrl.isSelf = function (user) {
      var me = authService.user();
      return !!me && !!user && me.userId === user.userId;
    };

    function scopeEditor(values, options) {
      var all = values.indexOf(ALL) !== -1;
      var picked = {};
      values.forEach(function (v) { if (v !== ALL) picked[v] = true; });
      // Keep values assigned earlier even if no records currently use them.
      var list = options.slice();
      Object.keys(picked).forEach(function (v) { if (list.indexOf(v) === -1) list.push(v); });
      return { mode: all ? 'all' : 'some', picked: picked, options: list.sort() };
    }

    $ctrl.openEditor = function (user) {
      var isNew = !user;
      user = user || { roles: [], attributes: {}, isActive: true };
      $ctrl.editorError = null;
      $ctrl.editorProblems = [];
      $ctrl.editor = {
        isNew: isNew,
        userId: user.userId,
        self: $ctrl.isSelf(user),
        sso: $ctrl.isSso(user),
        groupRoles: $ctrl.groupRoles(user),
        groupAttributes: user.groupAttributes || {},
        userName: user.userName || '',
        displayName: user.displayName || '',
        email: user.email || '',
        isActive: user.isActive !== false,
        password: isNew ? randomPassword() : '',
        showPassword: isNew,
        roles: (function () {
          var m = {};
          (user.roles || []).forEach(function (r) { if ((user.groupRoles || []).indexOf(r) === -1) m[r] = true; });
          return m;
        })(),
        customer: scopeEditor(attr(user, ATTR.customer), $ctrl.customers || []),
        delivery: scopeEditor(attr(user, ATTR.delivery), $ctrl.deliveries || []),
        pii: attr(user, ATTR.pii)[0] === 'true',
        exportLimit: attr(user, ATTR.exportLimit)[0] ? Number(attr(user, ATTR.exportLimit)[0]) : null
      };
      if (isNew) { $ctrl.editor.customer.mode = 'some'; $ctrl.editor.delivery.mode = 'all'; }
    };

    $ctrl.closeEditor = function () { $ctrl.editor = null; };
    $ctrl.newPassword = function () { $ctrl.editor.password = randomPassword(); $ctrl.editor.showPassword = true; };
    $ctrl.newPasswordValue = randomPassword;

    function scopeValues(scope) {
      if (scope.mode === 'all') return [ALL];
      return Object.keys(scope.picked).filter(function (k) { return scope.picked[k]; });
    }

    $ctrl.scopeEmpty = function (scope) { return scope.mode === 'some' && scopeValues(scope).length === 0; };

    /** Permissions the selected roles would grant (preview in the editor). */
    $ctrl.effectivePermissions = function () {
      if (!$ctrl.editor) return [];
      var set = {};
      ($ctrl.roles || []).forEach(function (r) {
        if ($ctrl.editor.roles[r.roleName] || $ctrl.editor.groupRoles.indexOf(r.roleName) !== -1) r.permissions.forEach(function (p) { set[p] = true; });
      });
      return Object.keys(set).sort();
    };

    $ctrl.saveUser = function () {
      var e = $ctrl.editor;
      var attributes = {};
      attributes[ATTR.customer] = scopeValues(e.customer);
      attributes[ATTR.delivery] = scopeValues(e.delivery);
      attributes[ATTR.pii] = e.pii ? ['true'] : [];
      attributes[ATTR.exportLimit] = e.exportLimit ? [String(Math.floor(e.exportLimit))] : [];
      var body = {
        userName: e.userName.trim(),
        displayName: e.displayName.trim(),
        email: e.email.trim(),
        isActive: e.isActive,
        roles: Object.keys(e.roles).filter(function (r) { return e.roles[r]; }),
        attributes: attributes
      };
      if (e.isNew) body.password = e.password;
      body.roles = body.roles.filter(function (r) { return e.groupRoles.indexOf(r) === -1; }); // group roles aren't manual grants

      e.saving = true;
      $ctrl.editorError = null;
      $ctrl.editorProblems = [];
      (e.isNew ? adminApi.createUser(body) : adminApi.updateUser(e.userId, body))
        .then(function (result) {
          // Saving your own account returns a fresh session (the old one ended with the change).
          if (e.self) authService.adopt(result);
          flash(e.isNew
            ? 'Created ' + body.userName + '. Give them the temporary password; they must change it when they first sign in.'
            : e.self ? 'Saved your account. Your new access applies now.'
              : 'Saved ' + body.userName + '. Their open sessions end and they sign in again with the new access.');
          if (e.isNew) $ctrl.createdPassword = { userName: body.userName, password: e.password };
          $ctrl.editor = null;
          return $q.all([adminApi.users(), adminApi.roles()]).then(function (r) {
            $ctrl.users = r[0];
            $ctrl.roles = r[1]; // member counts changed
            buildMatrix();
          });
        })
        .catch(function (err) {
          $ctrl.editorError = err.message;
          $ctrl.editorProblems = err.problems || [];
        })
        .finally(function () { e.saving = false; });
    };

    $ctrl.openReset = function (user) {
      $ctrl.reset = { user: user, password: randomPassword(), show: true };
      $ctrl.resetError = null;
    };

    $ctrl.saveReset = function () {
      var r = $ctrl.reset;
      r.saving = true;
      adminApi.resetPassword(r.user.userId, r.password)
        .then(function () {
          $ctrl.createdPassword = { userName: r.user.userName, password: r.password };
          flash('Reset the password for ' + r.user.userName + '. They must choose a new one when they sign in.');
          $ctrl.reset = null;
          return adminApi.users().then(function (users) { $ctrl.users = users; });
        })
        .catch(function (err) { $ctrl.resetError = err.message + (err.problems.length ? ' ' + err.problems.join(' ') : ''); })
        .finally(function () { r.saving = false; });
    };

    /** Permanent; deactivating is the reversible alternative (offered in the confirmation). */
    $ctrl.askDelete = function (user) {
      $ctrl.deleteError = null;
      $ctrl.pendingDelete = user;
    };

    $ctrl.deleteMessage = function () {
      var u = $ctrl.pendingDelete;
      return u ? 'Permanently delete ' + u.displayName + ' (' + u.userName + ')? Their roles and data-access settings are removed and ' +
        'they are signed out. Audit entries are kept. To keep the account but block sign-in, edit it and untick "Account active" instead.' : '';
    };

    $ctrl.confirmDelete = function () {
      var u = $ctrl.pendingDelete;
      $ctrl.pendingDelete = null;
      if (!u) return;
      adminApi.deleteUser(u.userId)
        .then(function () {
          flash('Deleted ' + u.userName + '.');
          return $q.all([adminApi.users(), adminApi.roles()]);
        })
        .then(function (r) { $ctrl.users = r[0]; $ctrl.roles = r[1]; buildMatrix(); })
        .catch(function (err) { $ctrl.loadError = err.message; });
    };

    $ctrl.unlock = function (user) {
      adminApi.unlock(user.userId)
        .then(function () { flash('Unlocked ' + user.userName + '.'); return adminApi.users(); })
        .then(function (users) { $ctrl.users = users; })
        .catch(function (err) { flash(err.message); });
    };

    $ctrl.copy = function (text) {
      if (navigator.clipboard) navigator.clipboard.writeText(text).catch(angular.noop);
    };

    // ════════ Roles × permissions ════════

    function buildMatrix() {
      var byCategory = {};
      $ctrl.permissions.forEach(function (p) { (byCategory[p.category] = byCategory[p.category] || []).push(p); });
      $ctrl.permissionGroups = Object.keys(byCategory).map(function (c) { return { category: c, permissions: byCategory[c] }; });
      $ctrl.matrix = {};
      $ctrl.roles.forEach(function (r) {
        $ctrl.matrix[r.roleId] = {};
        r.permissions.forEach(function (p) { $ctrl.matrix[r.roleId][p] = true; });
      });
      $ctrl.matrixSaved = angular.copy($ctrl.matrix);
    }

    $ctrl.isLockedCell = function (role, permission) {
      return role.roleName === 'Administrator' && permission.permissionKey === 'admin.users.manage';
    };

    $ctrl.roleDirty = function (role) {
      return !angular.equals(trueKeys($ctrl.matrix[role.roleId]), trueKeys($ctrl.matrixSaved[role.roleId]));
    };

    $ctrl.anyRoleDirty = function () { return ($ctrl.roles || []).some($ctrl.roleDirty); };

    function trueKeys(map) { return Object.keys(map || {}).filter(function (k) { return map[k]; }).sort(); }

    $ctrl.saveRoles = function () {
      var dirty = $ctrl.roles.filter($ctrl.roleDirty);
      $ctrl.rolesSaving = true;
      $ctrl.rolesError = null;
      $q.all(dirty.map(function (r) { return adminApi.setRolePermissions(r.roleId, trueKeys($ctrl.matrix[r.roleId])); }))
        .then(function () {
          flash('Saved permissions for ' + dirty.map(function (r) { return r.roleName; }).join(', ') + '. Members get them on their next request.');
          return adminApi.roles();
        })
        .then(function (roles) { $ctrl.roles = roles; buildMatrix(); })
        .catch(function (err) { $ctrl.rolesError = err.message; })
        .finally(function () { $ctrl.rolesSaving = false; });
    };

    $ctrl.discardRoles = function () { $ctrl.matrix = angular.copy($ctrl.matrixSaved); };

    // ════════ Access policies ════════

    $ctrl.togglePolicy = function (policy) {
      var next = !policy.isActive;
      policy.saving = true;
      adminApi.updatePolicy(policy.policyId, { isActive: next, conditionJson: policy.conditionJson, denyMessage: policy.denyMessage })
        .then(function () {
          policy.isActive = next;
          flash((next ? 'Turned on ' : 'Turned off ') + '"' + policy.policyName + '".');
        })
        .catch(function (err) { flash(err.message); })
        .finally(function () { policy.saving = false; });
    };

    $ctrl.editPolicy = function (policy) {
      var pretty;
      try { pretty = JSON.stringify(JSON.parse(policy.conditionJson), null, 2); } catch (e) { pretty = policy.conditionJson; }
      policy.edit = { conditionJson: pretty, denyMessage: policy.denyMessage || '' };
      policy.error = null;
    };

    $ctrl.cancelPolicy = function (policy) { policy.edit = null; policy.error = null; };

    $ctrl.savePolicy = function (policy) {
      var condition;
      try { condition = JSON.stringify(JSON.parse(policy.edit.conditionJson)); } catch (e) {
        policy.error = 'The condition is not valid JSON: ' + e.message;
        return;
      }
      policy.saving = true;
      adminApi.updatePolicy(policy.policyId, { isActive: policy.isActive, conditionJson: condition, denyMessage: policy.edit.denyMessage })
        .then(function () {
          policy.conditionJson = condition;
          policy.denyMessage = policy.edit.denyMessage;
          policy.summary = describeCondition(condition);
          policy.edit = null;
          flash('Saved "' + policy.policyName + '".');
        })
        .catch(function (err) { policy.error = err.message; })
        .finally(function () { policy.saving = false; });
    };

    // ════════ Microsoft sign-in (Entra group mappings) ════════

    $ctrl.loadMappings = function () {
      $ctrl.sso.loading = true;
      $ctrl.sso.error = null;
      adminApi.groupMappings()
        .then(function (list) { $ctrl.sso.mappings = list; })
        .catch(function (err) { $ctrl.sso.error = err.message; })
        .finally(function () { $ctrl.sso.loading = false; });
    };

    $ctrl.mappingGrant = function (m) {
      var parts = [];
      if (m.roleName) parts.push('Role ' + m.roleName);
      if (m.attributeKey) parts.push(m.attributeKey + ' = ' + m.attributeValue);
      return parts.join(', ');
    };

    $ctrl.addMapping = function () {
      var f = $ctrl.sso.form;
      var body = { claimType: f.claimType, claimValue: (f.claimValue || '').trim(), displayName: (f.displayName || '').trim() || null };
      if (f.grant === 'attribute') { body.attributeKey = f.attributeKey; body.attributeValue = (f.attributeValue || '').trim(); }
      else body.roleName = f.roleName;
      $ctrl.sso.saving = true;
      $ctrl.sso.error = null;
      adminApi.addGroupMapping(body)
        .then(function () {
          flash('Mapped ' + (body.displayName || body.claimValue) + '. Members get it the next time they sign in.');
          $ctrl.sso.form = { claimType: f.claimType, grant: f.grant };
          $ctrl.loadMappings();
        })
        .catch(function (err) { $ctrl.sso.error = err.message + (err.problems && err.problems.length ? ' ' + err.problems.join(' ') : ''); })
        .finally(function () { $ctrl.sso.saving = false; });
    };

    $ctrl.removeMapping = function (m) {
      adminApi.deleteGroupMapping(m.mappingId)
        .then(function () { flash('Removed the mapping for ' + (m.displayName || m.claimValue) + '. It is taken away at their next sign-in.'); $ctrl.loadMappings(); })
        .catch(function (err) { $ctrl.sso.error = err.message; });
    };

    // ════════ Recycle bin ════════

    $ctrl.loadRecycle = function () {
      $ctrl.recycle.loading = true;
      $ctrl.recycle.error = null;
      adminApi.recycleBin(($ctrl.recycle.search || '').trim())
        .then(function (r) {
          $ctrl.recycle.items = r.items;
          $ctrl.recycle.retentionDays = r.retentionDays;
          // Keep ticks only for records still listed.
          var listed = {};
          r.items.forEach(function (i) { listed[i.correspondenceId] = true; });
          Object.keys($ctrl.recycle.selected).forEach(function (id) { if (!listed[id]) delete $ctrl.recycle.selected[id]; });
        })
        .catch(function (err) { $ctrl.recycle.error = err.message; })
        .finally(function () { $ctrl.recycle.loading = false; });
    };

    $ctrl.daysLeft = function (item) {
      return Math.max(0, Math.ceil((new Date(item.purgeAfterUtc).getTime() - Date.now()) / 86400000));
    };

    $ctrl.restoreRecord = function (item) {
      item.busy = true;
      $ctrl.recycle.error = null;
      $ctrl.recycle.failures = null;
      adminApi.restore(item.correspondenceId)
        .then(function () { flash('Restored ' + (item.docId || item.correspondenceId) + ' with its status history and PDF.'); $ctrl.loadRecycle(); })
        .catch(function (err) { $ctrl.recycle.error = err.message; item.busy = false; });
    };

    // Selection for "Restore selected"
    $ctrl.selectedIds = function () {
      var sel = $ctrl.recycle.selected;
      return ($ctrl.recycle.items || []).filter(function (i) { return sel[i.correspondenceId]; }).map(function (i) { return i.correspondenceId; });
    };
    $ctrl.allSelected = function () {
      var items = $ctrl.recycle.items || [];
      return items.length > 0 && $ctrl.selectedIds().length === items.length;
    };
    $ctrl.toggleAll = function () {
      var on = !$ctrl.allSelected();
      $ctrl.recycle.selected = {};
      if (on) $ctrl.recycle.items.forEach(function (i) { $ctrl.recycle.selected[i.correspondenceId] = true; });
    };

    $ctrl.restoreSelected = function () {
      var ids = $ctrl.selectedIds();
      if (!ids.length) return;
      $ctrl.recycle.restoring = true;
      $ctrl.recycle.error = null;
      $ctrl.recycle.failures = null;
      adminApi.restoreMany(ids)
        .then(function (r) {
          var n = r.restored.length;
          if (n) flash('Restored ' + n + ' record' + (n === 1 ? '' : 's') + ' with their status history and PDFs.');
          if (r.failed.length || r.notFound.length) {
            $ctrl.recycle.failures = r.failed.map(function (f) { return (f.docId || f.correspondenceId) + ': ' + f.message; })
              .concat(r.notFound.map(function (id) { return id + ': no longer in the recycle bin.'; }));
          }
          r.restored.forEach(function (id) { delete $ctrl.recycle.selected[id]; });
          $ctrl.loadRecycle();
        })
        .catch(function (err) { $ctrl.recycle.error = err.message; })
        .finally(function () { $ctrl.recycle.restoring = false; });
    };

    $ctrl.askPurge = function (item) { $ctrl.pendingPurge = item; };
    $ctrl.purgeMessage = function () {
      var i = $ctrl.pendingPurge;
      return i ? 'Permanently delete ' + (i.docId || i.correspondenceId) + ' now, with its status history and PDF? This can\'t be undone.' : '';
    };
    $ctrl.confirmPurge = function () {
      var item = $ctrl.pendingPurge;
      $ctrl.pendingPurge = null;
      if (!item) return;
      adminApi.purge(item.correspondenceId)
        .then(function () { flash('Permanently deleted ' + (item.docId || item.correspondenceId) + '.'); $ctrl.loadRecycle(); })
        .catch(function (err) { $ctrl.recycle.error = err.message; });
    };

    // ════════ Letter templates ════════

    var TEMPLATE_FIELDS = ['documentType', 'name', 'subject', 'salutation', 'body', 'closing', 'signatureName', 'signatureTitle'];

    $ctrl.loadLetters = function () {
      $ctrl.letters.loading = true;
      $ctrl.letters.error = null;
      $q.all([adminApi.templates(), $ctrl.letters.placeholders.length ? $q.when($ctrl.letters.placeholders) : adminApi.placeholders()])
        .then(function (r) {
          $ctrl.letters.placeholders = r[1];
          var byType = {};
          r[0].forEach(function (t) { (byType[t.documentType] = byType[t.documentType] || []).push(t); });
          $ctrl.letters.groups = Object.keys(byType).sort().map(function (type) {
            var versions = byType[type].sort(function (a, b) { return b.version - a.version; });
            return { documentType: type, versions: versions, published: versions.filter(function (v) { return v.status === 'Published'; })[0] || null };
          });
        })
        .catch(function (err) { $ctrl.letters.error = err.message; })
        .finally(function () { $ctrl.letters.loading = false; });
    };

    /** Opens a version: drafts are editable, others read-only; asNewVersion starts a new draft from it. */
    $ctrl.openTemplate = function (t, asNewVersion) {
      var draft = {};
      TEMPLATE_FIELDS.forEach(function (f) { draft[f] = t ? t[f] || '' : ''; });
      if (!t) { draft.salutation = 'Dear {{RecipientName}},'; draft.closing = 'Sincerely,'; }
      $ctrl.tpl = {
        id: t && !asNewVersion ? t.templateId : null,
        isNewType: !t,
        readOnly: !!t && !asNewVersion && t.status !== 'Draft',
        version: t && !asNewVersion ? t.version : null,
        status: t && !asNewVersion ? t.status : 'Draft',
        draft: draft,
        error: null
      };
      $ctrl.tplFocus = 'body';
    };
    $ctrl.closeTemplate = function () { $ctrl.tpl = null; };
    $ctrl.newVersionFromOpen = function () { $ctrl.openTemplate(angular.extend({}, $ctrl.tpl.draft), true); };

    $ctrl.insertPlaceholder = function (name) {
      if (!$ctrl.tpl || $ctrl.tpl.readOnly) return;
      var field = $ctrl.tplFocus || 'body';
      var el = document.getElementById('tpl-' + field);
      var token = '{{' + name + '}}';
      var value = $ctrl.tpl.draft[field] || '';
      if (el && typeof el.selectionStart === 'number' && el.value === value) {
        $ctrl.tpl.draft[field] = value.slice(0, el.selectionStart) + token + value.slice(el.selectionEnd);
      } else {
        $ctrl.tpl.draft[field] = value + token;
      }
    };

    function templateBody() {
      var body = {};
      TEMPLATE_FIELDS.forEach(function (f) { body[f] = ($ctrl.tpl.draft[f] || '').trim() || null; });
      return body;
    }

    $ctrl.saveTemplate = function () {
      var t = $ctrl.tpl;
      t.saving = true;
      t.error = null;
      return (t.id ? adminApi.updateTemplate(t.id, templateBody()) : adminApi.createTemplate(templateBody()))
        .then(function (saved) {
          t.id = saved.templateId;
          t.version = saved.version;
          t.isNewType = false;
          flash('Saved draft v' + saved.version + ' of ' + saved.documentType + '.');
          $ctrl.loadLetters();
          return saved;
        })
        .catch(function (err) { t.error = err.message; return $q.reject(err); })
        .finally(function () { t.saving = false; });
    };

    $ctrl.publishTemplate = function () {
      var t = $ctrl.tpl;
      $ctrl.saveTemplate().then(function (saved) {
        t.saving = true;
        return adminApi.publishTemplate(saved.templateId).then(function (p) {
          flash('Published ' + p.documentType + ' v' + p.version + '. New PDFs of this type use it; the previous version is retired.');
          $ctrl.tpl = null;
          $ctrl.loadLetters();
        });
      }).catch(function (err) { t.error = err.message; })
        .finally(function () { t.saving = false; });
    };

    $ctrl.retireTemplate = function (v) {
      adminApi.retireTemplate(v.templateId)
        .then(function () {
          flash('Retired ' + v.documentType + ' v' + v.version + '.' +
            (v.status === 'Published' ? ' PDFs of this type use the plain data sheet until another version is published.' : ''));
          $ctrl.loadLetters();
        })
        .catch(function (err) { $ctrl.letters.error = err.message; });
    };

    /** Renders the editor's current text with sample data (nothing is saved). */
    $ctrl.previewTemplate = function () {
      var t = $ctrl.tpl;
      t.previewing = true;
      t.error = null;
      correspondenceApi.previewLetter({ template: templateBody() })
        .then(function (result) {
          $ctrl.letterPreview = {
            blob: result.blob, fileName: 'letter-preview.pdf',
            title: 'Preview — ' + (t.draft.documentType || 'letter') + (t.version ? ' v' + t.version : ''),
            note: 'Sample data. ' + (result.missing.length ? 'No value for: ' + result.missing.join(', ') + '.' : 'All placeholders filled.')
          };
        })
        .catch(function (err) { t.error = err.message; })
        .finally(function () { t.previewing = false; });
    };

    // ════════ Alerts ════════

    $ctrl.loadAlerts = function () {
      var a = $ctrl.alertsTab;
      a.loading = true;
      a.error = null;
      $q.all([adminApi.alerts(a.openOnly, 100), a.rules ? $q.when(a.rules) : adminApi.alertRules()])
        .then(function (r) { a.items = r[0]; a.rules = r[1]; })
        .catch(function (err) { a.error = err.message; })
        .finally(function () { a.loading = false; });
    };

    $ctrl.acknowledge = function (alert) {
      alert.busy = true;
      adminApi.acknowledgeAlert(alert.alertId)
        .then(function () { $ctrl.loadAlerts(); $scope.$emit('portal:alertsChanged'); })
        .catch(function (err) { $ctrl.alertsTab.error = err.message; alert.busy = false; });
    };

    $ctrl.sentTo = function (a) {
      return [a.notifiedTeams ? 'Teams' : null, a.notifiedEmail ? 'Email' : null].filter(Boolean).join(', ') || 'Portal only';
    };

    $ctrl.severityCss = function (s) { return s === 'Critical' ? 'danger' : s === 'Warning' ? 'warning' : 'muted'; };

    $scope.$on('portal:alertRaised', function () { if ($ctrl.tab === 'alerts') $ctrl.loadAlerts(); });

    // ════════ Audit log ════════

    $ctrl.loadAudit = function () {
      $ctrl.audit.loading = true;
      $ctrl.audit.error = null;
      adminApi.audit($ctrl.audit.top, $ctrl.audit.userName.trim())
        .then(function (rows) { $ctrl.audit.rows = rows; })
        .catch(function (err) { $ctrl.audit.error = err.message; })
        .finally(function () { $ctrl.audit.loading = false; });
    };

    $ctrl.outcomeCss = function (outcome) {
      return outcome === 'Success' ? 'success' : outcome === 'Denied' ? 'warning' : 'danger';
    };
  }
})();
