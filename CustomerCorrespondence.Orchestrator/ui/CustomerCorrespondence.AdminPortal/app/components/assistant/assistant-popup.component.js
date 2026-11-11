(function () {
  'use strict';

  /**
   * <assistant-popup>
   * Floating launcher + window for the assistant panel. The window can be dragged,
   * resized from its corners/side edges, or docked to the right edge (the page
   * reserves the docked width via assistantBridge.dockWidth).
   *
   * The panel stays mounted while the window is closed, so the conversation
   * survives closing and reopening.
   */
  angular.module('adminPortal').component('assistantPopup', {
    templateUrl: 'app/components/assistant/assistant-popup.html',
    controller: AssistantPopupController
  });

  var DEFAULT_WIDTH = 420;
  var DEFAULT_HEIGHT = 640;
  var MIN_WIDTH = 320;
  var MIN_HEIGHT = 360;
  var EDGE_MARGIN = 24;
  var DEFAULT_DOCK_WIDTH = 380;
  var MIN_DOCK_WIDTH = 300;
  var MAX_DOCK_WIDTH = 640;
  var SCREEN_GAP = 8;          // the window always keeps this much of the screen around it
  var COMPACT_MAX_WIDTH = 640; // narrower than this: the assistant fills the screen instead of floating
  var MIN_DOCK_VIEWPORT = 900; // narrower than this: docking would leave too little room for the page

  // Which axis each resize handle drags along — null leaves that axis untouched.
  var HANDLE_AXES = {
    nw: { x: 'w', y: 'n' },
    ne: { x: 'e', y: 'n' },
    sw: { x: 'w', y: 's' },
    se: { x: 'e', y: 's' },
    w: { x: 'w', y: null },
    e: { x: 'e', y: null }
  };

  function clamp(value, min, max) {
    return Math.max(min, Math.min(value, max));
  }

  AssistantPopupController.$inject = ['$scope', '$document', '$window', 'assistantBridge'];

  function AssistantPopupController($scope, $document, $window, assistantBridge) {
    var $ctrl = this;
    var cleanupDrag = null;

    $ctrl.handles = Object.keys(HANDLE_AXES);

    $ctrl.$onInit = function () {
      $ctrl.open = false;
      $ctrl.docked = false;
      $ctrl.dockWidth = DEFAULT_DOCK_WIDTH;
      $ctrl.box = defaultBox();
      fitToScreen();
      assistantBridge.registerOpener(function () {
        $ctrl.open = true;
        fitToScreen();
      });
      angular.element($window).on('resize', onResize);
    };

    $ctrl.$onDestroy = function () {
      assistantBridge.registerOpener(null);
      assistantBridge.dockWidth = 0; // e.g. navigating to Administration while docked
      angular.element($window).off('resize', onResize);
      if (cleanupDrag) cleanupDrag();
    };

    // The browser window changed size: keep the assistant fully on screen.
    var resizeQueued = false;
    function onResize() {
      if (resizeQueued) return;
      resizeQueued = true;
      $scope.$applyAsync(function () { resizeQueued = false; fitToScreen(); });
    }

    /**
     * Keeps the floating window inside the screen (shrinking it if needed), switches to the full-screen layout on
     * small screens, and undocks when the screen is too narrow to share with the page.
     */
    function fitToScreen() {
      var w = $window.innerWidth;
      var h = $window.innerHeight;
      $ctrl.compact = w < COMPACT_MAX_WIDTH;
      if ($ctrl.docked && !$ctrl.canDock()) { $ctrl.docked = false; $ctrl.userPlaced = false; }
      // Not moved or resized by the user: stay anchored above the launcher at the default size for this screen.
      if (!$ctrl.userPlaced) $ctrl.box = defaultBox();
      if ($ctrl.docked) $ctrl.dockWidth = clamp($ctrl.dockWidth, MIN_DOCK_WIDTH, Math.min(MAX_DOCK_WIDTH, Math.floor(w / 2)));
      var b = $ctrl.box;
      var width = clamp(b.width, Math.min(MIN_WIDTH, w - SCREEN_GAP * 2), w - SCREEN_GAP * 2);
      var height = clamp(b.height, Math.min(MIN_HEIGHT, h - SCREEN_GAP * 2), h - SCREEN_GAP * 2);
      $ctrl.box = {
        width: width,
        height: height,
        left: clamp(b.left, SCREEN_GAP, w - width - SCREEN_GAP),
        top: clamp(b.top, SCREEN_GAP, h - height - SCREEN_GAP)
      };
      publishDock();
    }

    $ctrl.canDock = function () { return $window.innerWidth >= MIN_DOCK_VIEWPORT; };

    function defaultBox() {
      var width = Math.min(DEFAULT_WIDTH, $window.innerWidth - EDGE_MARGIN * 2);
      var height = Math.min(DEFAULT_HEIGHT, $window.innerHeight - EDGE_MARGIN * 2);
      return {
        width: width,
        height: height,
        left: $window.innerWidth - EDGE_MARGIN - width,
        top: Math.max(0, $window.innerHeight - 92 - height)
      };
    }

    function publishDock() {
      assistantBridge.dockWidth = $ctrl.open && $ctrl.docked ? $ctrl.dockWidth : 0;
    }

    $ctrl.windowStyle = function () {
      if ($ctrl.docked) return { top: 0, right: 0, width: $ctrl.dockWidth + 'px', height: '100vh' };
      if ($ctrl.compact) return { top: SCREEN_GAP + 'px', left: SCREEN_GAP + 'px', width: 'calc(100vw - ' + SCREEN_GAP * 2 + 'px)', height: 'calc(100vh - ' + SCREEN_GAP * 2 + 'px)' };
      return { top: $ctrl.box.top + 'px', left: $ctrl.box.left + 'px', width: $ctrl.box.width + 'px', height: $ctrl.box.height + 'px' };
    };

    $ctrl.launcherStyle = function () {
      return { right: ($ctrl.docked && $ctrl.open ? $ctrl.dockWidth + 24 : 24) + 'px' };
    };

    $ctrl.toggleOpen = function () {
      if ($ctrl.open) {
        $ctrl.open = false;
        $ctrl.docked = false;
      } else {
        $ctrl.open = true;
        fitToScreen(); // the screen may have changed size since it was last open
      }
      publishDock();
    };

    // Undocking always drops back to the default floating position/size.
    $ctrl.toggleDock = function () {
      if (!$ctrl.docked && !$ctrl.canDock()) return;
      $ctrl.docked = !$ctrl.docked;
      if (!$ctrl.docked) { $ctrl.box = defaultBox(); $ctrl.userPlaced = false; }
      fitToScreen();
    };

    // Keyboard shortcut "i" (from the shell).
    $scope.$on('shortcut:assistant', function () { $ctrl.toggleOpen(); });

    $ctrl.restorePosition = function () {
      $ctrl.userPlaced = false;
      $ctrl.box = defaultBox();
      fitToScreen();
    };

    /** Shared mousemove/mouseup plumbing; `onMove(dx, dy)` mutates state. */
    function track(event, onMove) {
      event.preventDefault();
      event.stopPropagation();
      var startX = event.clientX;
      var startY = event.clientY;
      $ctrl.interacting = true;

      function move(ev) {
        onMove(ev.clientX - startX, ev.clientY - startY);
        $scope.$applyAsync();
      }
      function up() {
        cleanupDrag();
        $scope.$applyAsync(function () { $ctrl.interacting = false; });
      }
      cleanupDrag = function () {
        $document.off('mousemove', move);
        $document.off('mouseup', up);
        cleanupDrag = null;
      };
      $document.on('mousemove', move);
      $document.on('mouseup', up);
    }

    $ctrl.startMove = function (event) {
      if ($ctrl.docked || $ctrl.compact) return;
      $ctrl.userPlaced = true;
      var start = angular.copy($ctrl.box);
      track(event, function (dx, dy) {
        $ctrl.box.left = clamp(start.left + dx, 0, $window.innerWidth - start.width);
        $ctrl.box.top = clamp(start.top + dy, 0, $window.innerHeight - start.height);
      });
    };

    $ctrl.startResize = function (handle, event) {
      $ctrl.userPlaced = true;
      var start = angular.copy($ctrl.box);
      var axes = HANDLE_AXES[handle];
      track(event, function (dx, dy) {
        var maxWidth = $window.innerWidth - EDGE_MARGIN;
        var maxHeight = $window.innerHeight - EDGE_MARGIN;
        var width = start.width;
        var left = start.left;
        var height = start.height;
        var top = start.top;

        if (axes.x === 'e') {
          width = clamp(start.width + dx, MIN_WIDTH, maxWidth);
        } else if (axes.x === 'w') {
          width = clamp(start.width - dx, MIN_WIDTH, maxWidth);
          left = start.left + start.width - width;
        }
        if (axes.y === 's') {
          height = clamp(start.height + dy, MIN_HEIGHT, maxHeight);
        } else if (axes.y === 'n') {
          height = clamp(start.height - dy, MIN_HEIGHT, maxHeight);
          top = start.top + start.height - height;
        }

        $ctrl.box = {
          width: width,
          height: height,
          left: clamp(left, 0, $window.innerWidth - width),
          top: clamp(top, 0, $window.innerHeight - height)
        };
      });
    };

    $ctrl.startDockResize = function (event) {
      var startWidth = $ctrl.dockWidth;
      track(event, function (dx) {
        // Dragging left grows the panel — its right edge is pinned to the screen edge.
        $ctrl.dockWidth = clamp(startWidth - dx, MIN_DOCK_WIDTH, Math.min(MAX_DOCK_WIDTH, $window.innerWidth - EDGE_MARGIN));
        publishDock();
      });
    };
  }
})();
