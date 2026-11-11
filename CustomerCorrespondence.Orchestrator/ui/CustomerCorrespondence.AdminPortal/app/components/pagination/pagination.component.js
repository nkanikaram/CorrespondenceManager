(function () {
  'use strict';

  /**
   * <pagination current-page="…" page-size="…" total-items="…"
   *             page-size-options="[10, 25, 50]" button-count="4"
   *             on-page-change="…(page)" on-page-size-change="…(size)"></pagination>
   *
   * Stateless pager styled after the KendoReact Pager: first/prev, a block of
   * `buttonCount` page numbers with "..." to jump to the previous/next block,
   * next/last, an "items per page" select, and "1 - 10 of 77 items" on the right.
   */
  angular.module('adminPortal').component('pagination', {
    templateUrl: 'app/components/pagination/pagination.html',
    bindings: {
      currentPage: '<',
      pageSize: '<',
      totalItems: '<',
      pageSizeOptions: '<',
      buttonCount: '<',
      onPageChange: '&',
      onPageSizeChange: '&'
    },
    controller: PaginationController
  });

  var DEFAULT_BUTTON_COUNT = 4;

  function PaginationController() {
    var $ctrl = this;

    $ctrl.$onChanges = function () {
      var total = $ctrl.totalItems || 0;
      var size = $ctrl.pageSize || 10;

      $ctrl.totalPages = Math.max(1, Math.ceil(total / size));
      $ctrl.rangeStart = total ? ($ctrl.currentPage - 1) * size + 1 : 0;
      $ctrl.rangeEnd = Math.min($ctrl.currentPage * size, total);
      $ctrl.items = buildItems($ctrl.currentPage, $ctrl.totalPages, $ctrl.buttonCount || DEFAULT_BUTTON_COUNT);
      $ctrl.allPages = range(1, $ctrl.totalPages);
      $ctrl.selectedPage = $ctrl.currentPage;
      $ctrl.selectedSize = size;
    };

    $ctrl.goTo = function (page) {
      page = Number(page);
      if (!page || page < 1 || page > $ctrl.totalPages || page === $ctrl.currentPage) return;
      $ctrl.onPageChange({ page: page });
    };

    $ctrl.changeSize = function () {
      $ctrl.onPageSizeChange({ size: Number($ctrl.selectedSize) });
    };
  }

  function range(from, to) {
    var out = [];
    for (var i = from; i <= to; i++) out.push(i);
    return out;
  }

  /**
   * Kendo's numeric pager: pages come in blocks of `count`. A "..." before the block jumps to
   * the previous block's last page; one after it jumps to the next block's first page.
   * e.g. total=8, count=4: current=2 -> 1 2 3 4 ...   current=6 -> ... 5 6 7 8
   */
  function buildItems(current, total, count) {
    var start = Math.floor((current - 1) / count) * count + 1;
    var end = Math.min(start + count - 1, total);
    var items = [];
    if (start > 1) items.push({ page: start - 1, gap: true });
    range(start, end).forEach(function (p) { items.push({ page: p }); });
    if (end < total) items.push({ page: end + 1, gap: true });
    return items;
  }

  PaginationController.buildItems = buildItems; // exposed for unit tests
})();
