(function () {
  'use strict';

  /**
   * <record-detail-panel record-id="…" on-close="…()" on-open-pdf="…(row)" on-explain="…(row)">
   *
   * Side panel with everything about one correspondence record: its fields, the full status timeline
   * (CorrespondenceRequestStatusHistory, with time spent in each status), the payload (when the user may see
   * it), the PDF and the activity on it (resubmits, PDFs, deletions/restores, refused actions) from the audit log.
   */
  angular.module('adminPortal').component('recordDetailPanel', {
    templateUrl: 'app/components/record/record-detail-panel.html',
    bindings: { recordId: '<', onClose: '&', onOpenPdf: '&', onExplain: '&', canExplain: '<' },
    controller: RecordDetailController
  });

  RecordDetailController.$inject = ['$scope', 'correspondenceApi', 'errorRecovery'];

  var EVENT_LABELS = {
    CorrespondenceCreated: 'Created in the portal', CorrespondenceResubmitted: 'Resubmitted', CorrespondenceDeleted: 'Moved to the recycle bin',
    CorrespondenceRestored: 'Restored from the recycle bin', CorrespondencePurged: 'Permanently deleted', PdfGenerated: 'PDF generated',
    PdfDownloaded: 'PDF opened', AccessDenied: 'Action refused'
  };

  function duration(ms) {
    if (ms == null || ms < 0) return null;
    var m = Math.round(ms / 60000);
    if (m < 60) return m + ' min';
    var h = Math.round(m / 60);
    if (h < 48) return h + ' h';
    return Math.round(h / 24) + ' days';
  }

  function RecordDetailController($scope, correspondenceApi, errorRecovery) {
    var $ctrl = this;

    $ctrl.$onChanges = function (changes) {
      if (changes.recordId && $ctrl.recordId) {
        $ctrl.tab = 'overview';
        load();
      }
    };

    function load() {
      $ctrl.loading = true;
      $ctrl.error = null;
      var id = $ctrl.recordId;
      correspondenceApi.details(id).then(function (d) {
        if (id !== $ctrl.recordId) return;
        $ctrl.details = d;
        // Timeline: oldest first, with how long each status lasted (the last one is "so far").
        var history = d.statusHistory || [];
        $ctrl.timeline = history.map(function (h, i) {
          var start = h.updatedTS ? new Date(h.updatedTS).getTime() : null;
          var next = history[i + 1] && history[i + 1].updatedTS ? new Date(history[i + 1].updatedTS).getTime() : null;
          return angular.extend({}, h, {
            current: i === history.length - 1,
            lasted: start && next ? duration(next - start) : null,
            css: String(h.status || '').toLowerCase()
          });
        });
        $ctrl.activity = (d.activity || []).map(function (a) {
          return angular.extend({}, a, { label: EVENT_LABELS[a.eventType] || a.eventType });
        });
        $ctrl.payloadPretty = null;
        if (d.payload) {
          try { $ctrl.payloadPretty = JSON.stringify(JSON.parse(d.payload), null, 2); } catch (e) { $ctrl.payloadPretty = d.payload; }
        }
      }).catch(function (err) {
        $ctrl.error = errorRecovery.messageOf(err, 'The record could not be loaded.');
        $ctrl.details = null;
      }).finally(function () { $ctrl.loading = false; });
    }

    $ctrl.reload = load;

    /** Activity is re-read when shown, so what was just done here (PDF opened, resubmitted) appears. */
    $ctrl.selectTab = function (tab) {
      $ctrl.tab = tab;
      if (tab === 'activity' && $ctrl.details) load();
    };

    /** A row-like object for the page's PDF viewer / Explain actions. */
    $ctrl.row = function () {
      var r = $ctrl.details && $ctrl.details.record;
      if (!r) return null;
      return {
        id: r.correspondenceId, docId: r.docId, claimNumber: r.claimNumber, customer: r.requestingDomainName,
        delivery: r.deliveryMethod, created: r.requestCreationTS, status: r.latestStatus, payload: $ctrl.details.payload
      };
    };

    // Live: a status change or PDF update for this record refreshes the panel.
    $scope.$on('portal:recordsChanged', function (e, data) {
      if ($ctrl.recordId && (data.changedIds || []).indexOf($ctrl.recordId) !== -1) load();
    });
    $scope.$on('portal:pdfStatusChanged', function (e, data) {
      if ($ctrl.recordId && data.correspondenceId === $ctrl.recordId) load();
    });
  }
})();
