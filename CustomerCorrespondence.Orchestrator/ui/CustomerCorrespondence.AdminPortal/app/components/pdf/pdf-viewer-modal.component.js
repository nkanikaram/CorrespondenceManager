(function () {
  'use strict';

  /**
   * <pdf-viewer-modal record="row" pdf="statusItem" can-generate="bool"
   *                   on-close="…()" on-status-change="…(status)">
   *
   * Shows a correspondence request's stored PDF inside the portal (the browser's built-in PDF viewer
   * in an iframe), with Download, Open in new tab and — for users who may — Regenerate. The file is
   * fetched with the user's token and shown from a local blob: URL, revoked on close.
   */
  angular.module('adminPortal').component('pdfViewerModal', {
    templateUrl: 'app/components/pdf/pdf-viewer-modal.html',
    // file: { blob, fileName, title, note } shows that PDF instead of fetching a record's stored one.
    bindings: { record: '<', file: '<', pdf: '<', canGenerate: '<', onClose: '&', onStatusChange: '&' },
    controller: PdfViewerController
  });

  PdfViewerController.$inject = ['$sce', '$timeout', '$window', 'correspondenceApi', 'errorRecovery'];

  function PdfViewerController($sce, $timeout, $window, correspondenceApi, errorRecovery) {
    var $ctrl = this;
    var objectUrl = null;
    var blob = null;
    var fileName = null;

    function release() {
      if (objectUrl) $window.URL.revokeObjectURL(objectUrl);
      objectUrl = null;
      blob = null;
      $ctrl.src = null;
    }

    function load() {
      release();
      $ctrl.loading = true;
      $ctrl.error = null;
      var id = $ctrl.record.id;
      correspondenceApi.downloadPdf(id).then(function (file) {
        if (!$ctrl.record || $ctrl.record.id !== id) return; // closed or switched meanwhile
        blob = file.blob.type === 'application/pdf' ? file.blob : new $window.Blob([file.blob], { type: 'application/pdf' });
        fileName = file.fileName || 'correspondence-' + ($ctrl.record.docId || id) + '.pdf';
        objectUrl = $window.URL.createObjectURL(blob);
        $ctrl.src = $sce.trustAsResourceUrl(objectUrl);
      }).catch(function (err) {
        $ctrl.error = errorRecovery.messageOf(err, 'The PDF could not be loaded.');
      }).finally(function () { $ctrl.loading = false; });
    }

    function showFile() {
      release();
      blob = $ctrl.file.blob;
      fileName = $ctrl.file.fileName || 'document.pdf';
      objectUrl = $window.URL.createObjectURL(blob);
      $ctrl.src = $sce.trustAsResourceUrl(objectUrl);
      $ctrl.error = null;
    }

    $ctrl.$onChanges = function (changes) {
      if (changes.file && $ctrl.file) { showFile(); return; }
      if (changes.record || changes.file) {
        if ($ctrl.record) load();
        else release();
      }
    };

    $ctrl.open = function () { return !!($ctrl.record || $ctrl.file); };
    $ctrl.title = function () {
      return $ctrl.file ? $ctrl.file.title : 'PDF — ' + ($ctrl.record.docId || $ctrl.record.id);
    };

    $ctrl.$onDestroy = release;

    $ctrl.close = function () {
      release();
      $ctrl.onClose();
    };

    $ctrl.download = function () {
      if (!objectUrl) return;
      var a = $window.document.createElement('a');
      a.href = objectUrl;
      a.download = fileName;
      $window.document.body.appendChild(a);
      a.click();
      a.remove();
    };

    $ctrl.openInNewTab = function () {
      if (objectUrl) $window.open(objectUrl, '_blank', 'noopener');
    };

    $ctrl.regenerate = function () {
      if (!$ctrl.record) return;
      $ctrl.regenerating = true;
      $ctrl.error = null;
      correspondenceApi.generatePdf($ctrl.record.id).then(function (status) {
        $ctrl.onStatusChange({ status: status });
        load();
      }).catch(function (err) {
        $ctrl.error = errorRecovery.messageOf(err, 'The PDF could not be regenerated.');
      }).finally(function () { $ctrl.regenerating = false; });
    };

    $ctrl.sizeText = function () {
      var bytes = $ctrl.pdf && $ctrl.pdf.sizeBytes;
      if (!bytes) return null;
      return bytes < 1024 ? bytes + ' B' : (bytes / 1024).toFixed(bytes < 10240 ? 1 : 0) + ' KB';
    };
  }
})();
