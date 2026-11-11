(function () {
  'use strict';

  /**
   * The portal's appearance, for every screen: font, text size, header colour and text colour. They are display
   * preferences (Preferences in the user menu, saved with the account by accountService); this service knows the
   * choices and applies them to <html> as CSS variables and data attributes (see "Appearance" in css/styles.css).
   *
   *   font:        default | calibri | arial | verdana | georgia | mono
   *   textSize:    sm | md | lg | xl           (scales the root font size, so all rem-based text follows)
   *   headerColor: unset (theme) | '#rrggbb'   (top bar, dialog and popup headers, the assistant's title bar,
   *                                             a tint on grid column headers)
   *   textColor:   unset (theme) | 'contrast' | '#rrggbb'
   */
  angular.module('adminPortal').factory('appearance', appearance);

  var HEX = /^#[0-9a-f]{6}$/i;

  var FONTS = [
    { id: 'default', label: 'Segoe UI (default)', stack: '' },
    { id: 'calibri', label: 'Calibri', stack: 'Calibri, Carlito, "Segoe UI", sans-serif' },
    { id: 'arial', label: 'Arial', stack: 'Arial, "Helvetica Neue", Helvetica, sans-serif' },
    { id: 'verdana', label: 'Verdana (wide, easy to read)', stack: 'Verdana, Geneva, sans-serif' },
    { id: 'georgia', label: 'Georgia (serif)', stack: 'Georgia, Cambria, "Times New Roman", serif' },
    { id: 'mono', label: 'Consolas (monospace)', stack: 'Consolas, "Cascadia Mono", Menlo, monospace' }
  ];
  var SIZES = [
    { id: 'sm', label: 'S', title: 'Small', scale: 0.9 },
    { id: 'md', label: 'M', title: 'Medium (default)', scale: 1 },
    { id: 'lg', label: 'L', title: 'Large', scale: 1.1 },
    { id: 'xl', label: 'XL', title: 'Extra large', scale: 1.2 }
  ];
  var HEADERS = [
    { id: null, label: 'Theme default' },
    { id: '#0b6bcb', label: 'Ocean blue' },
    { id: '#0f766e', label: 'Teal' },
    { id: '#4338ca', label: 'Indigo' },
    { id: '#7e22ce', label: 'Plum' },
    { id: '#b91c1c', label: 'Crimson' },
    { id: '#166534', label: 'Forest' },
    { id: '#334155', label: 'Slate' }
  ];
  var KEYS = ['font', 'textSize', 'headerColor', 'textColor'];

  function hexToRgb(hex) {
    var n = parseInt(hex.slice(1), 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
  }
  function cssColorToRgb(css) {
    css = String(css || '').trim();
    if (HEX.test(css)) return hexToRgb(css);
    if (/^#[0-9a-f]{3}$/i.test(css)) return hexToRgb('#' + css.slice(1).split('').map(function (c) { return c + c; }).join(''));
    var m = /rgba?\(\s*(\d+)[\s,]+(\d+)[\s,]+(\d+)/.exec(css);
    return m ? [+m[1], +m[2], +m[3]] : null;
  }
  /** WCAG relative luminance. */
  function luminance(rgb) {
    var c = rgb.map(function (v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); });
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
  }
  /** WCAG contrast ratio between two RGB colours (1–21). */
  function contrast(a, b) {
    var la = luminance(a), lb = luminance(b);
    return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
  }
  function find(list, id) { return list.filter(function (x) { return x.id === id; })[0]; }

  function appearance() {
    /** Only the valid appearance values of a preferences object. */
    function pick(prefs) {
      prefs = prefs || {};
      var out = {};
      if (find(FONTS, prefs.font) && prefs.font !== 'default') out.font = prefs.font;
      if (find(SIZES, prefs.textSize) && prefs.textSize !== 'md') out.textSize = prefs.textSize;
      if (HEX.test(prefs.headerColor || '')) out.headerColor = prefs.headerColor.toLowerCase();
      if (prefs.textColor === 'contrast' || HEX.test(prefs.textColor || '')) out.textColor = prefs.textColor;
      return out;
    }

    /** The CSS variables and <html> data attributes for these preferences. */
    function styleOf(prefs) {
      var p = pick(prefs);
      var vars = {};
      var attrs = { 'data-header': null, 'data-text': null };
      var font = find(FONTS, p.font);
      if (font && font.stack) vars['--app-font'] = font.stack;
      var size = find(SIZES, p.textSize);
      if (size) vars['--text-scale'] = String(size.scale);
      if (p.headerColor) {
        vars['--hdr'] = p.headerColor;
        // Black or white on the header, whichever reads better.
        var rgb = hexToRgb(p.headerColor);
        vars['--hdr-ink'] = contrast(rgb, [255, 255, 255]) >= contrast(rgb, [17, 17, 17]) ? '#ffffff' : '#111111';
        attrs['data-header'] = 'custom';
      }
      if (p.textColor === 'contrast') attrs['data-text'] = 'contrast';
      else if (p.textColor) { vars['--user-text'] = p.textColor; attrs['data-text'] = 'custom'; }
      return { vars: vars, attrs: attrs };
    }

    var VARS = ['--app-font', '--text-scale', '--hdr', '--hdr-ink', '--user-text'];

    return {
      FONTS: FONTS,
      SIZES: SIZES,
      HEADERS: HEADERS,
      KEYS: KEYS,
      pick: pick,
      styleOf: styleOf,

      /** Applies the appearance preferences to the whole page. */
      apply: function (prefs, root) {
        root = root || document.documentElement;
        var s = styleOf(prefs);
        VARS.forEach(function (name) {
          if (s.vars[name]) root.style.setProperty(name, s.vars[name]);
          else root.style.removeProperty(name);
        });
        Object.keys(s.attrs).forEach(function (a) {
          if (s.attrs[a]) root.setAttribute(a, s.attrs[a]);
          else root.removeAttribute(a);
        });
      },

      isDefault: function (prefs) { return !Object.keys(pick(prefs)).length; },

      /**
       * How readable a text colour is on a background (`backgroundCss` from getComputedStyle):
       * { ratio, ok } where ok means at least WCAG AA for normal text (4.5:1).
       */
      readability: function (hex, backgroundCss) {
        var bg = cssColorToRgb(backgroundCss);
        if (!HEX.test(hex || '') || !bg) return null;
        var ratio = contrast(hexToRgb(hex), bg);
        return { ratio: Math.round(ratio * 10) / 10, ok: ratio >= 4.5 };
      }
    };
  }
})();
