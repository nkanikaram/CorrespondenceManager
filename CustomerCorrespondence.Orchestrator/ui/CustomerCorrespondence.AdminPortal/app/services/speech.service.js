(function () {
  'use strict';

  /**
   * Web Speech API wrappers: dictation (SpeechRecognition) and read-aloud
   * (speechSynthesis) with a preference for natural-sounding voices.
   */
  angular.module('adminPortal').factory('speech', speech);

  // The Web Speech API doesn't expose a voice's gender, so match against the well-known names
  // each platform ships (Windows/Edge "Microsoft ...", Chrome's "Google ...", macOS/iOS voices).
  var FEMALE_VOICE_HINTS = [
    'female', 'zira', 'susan', 'samantha', 'victoria', 'aria', 'jenny', 'emma', 'ava', 'allison',
    'karen', 'moira', 'tessa', 'fiona', 'kate', 'serena', 'salli', 'joanna', 'kendra', 'ivy',
    'google us english', 'google uk english female', 'hazel', 'catherine', 'michelle'
  ];
  var MALE_VOICE_HINTS = [
    'male', 'david', 'mark', 'guy', 'daniel', 'alex', 'fred', 'ryan', 'christopher', 'george',
    'james', 'eric', 'matthew', 'brian', 'google uk english male', 'tom', 'oliver'
  ];
  var NATURAL = /natural|neural/i;

  function matchesGender(name, gender) {
    var lower = name.toLowerCase();
    var hints = gender === 'female' ? FEMALE_VOICE_HINTS : MALE_VOICE_HINTS;
    // "female" contains "male" — don't let a female voice count as male.
    if (gender === 'male' && lower.indexOf('female') !== -1) return false;
    return hints.some(function (h) { return lower.indexOf(h) !== -1; });
  }

  /**
   * Prefers a "Natural"/"Neural" English voice of the requested gender, then any
   * voice of that gender, then any natural voice, then a local en-US voice.
   */
  function pickVoice(voices, gender) {
    if (!voices || !voices.length) return null;
    var en = voices.filter(function (v) { return v.lang && v.lang.toLowerCase().indexOf('en') === 0; });
    var pool = en.length ? en : voices;
    var gendered = pool.filter(function (v) { return matchesGender(v.name, gender); });
    return gendered.filter(function (v) { return NATURAL.test(v.name); })[0] ||
      gendered[0] ||
      pool.filter(function (v) { return NATURAL.test(v.name); })[0] ||
      pool.filter(function (v) { return v.lang && v.lang.toLowerCase() === 'en-us' && v.localService; })[0] ||
      pool[0] ||
      null;
  }

  function speech() {
    var cachedVoices = null;
    var synth = typeof window !== 'undefined' ? window.speechSynthesis : null;

    if (synth && synth.addEventListener) {
      // The voice list often loads asynchronously — warm the cache so the first read-aloud
      // click can already pick a natural-sounding voice.
      var refresh = function () { cachedVoices = synth.getVoices(); };
      refresh();
      synth.addEventListener('voiceschanged', refresh);
    }

    function recognitionCtor() {
      return typeof window !== 'undefined' ? (window.SpeechRecognition || window.webkitSpeechRecognition || null) : null;
    }

    return {
      pickVoice: pickVoice,
      canListen: function () { return !!recognitionCtor(); },
      canSpeak: function () { return !!synth; },

      /** Starts one-shot dictation. Returns the recognition object (call .stop()) or null. */
      listen: function (onTranscript, onEnd) {
        var Ctor = recognitionCtor();
        if (!Ctor) return null;
        var recognition = new Ctor();
        recognition.lang = 'en-US';
        recognition.continuous = false;
        recognition.interimResults = false;
        recognition.onresult = function (e) {
          var last = e.results[e.results.length - 1];
          var transcript = last && last[0] ? last[0].transcript : '';
          if (transcript.trim()) onTranscript(transcript.trim());
        };
        recognition.onerror = onEnd;
        recognition.onend = onEnd;
        recognition.start();
        return recognition;
      },

      /** Reads text aloud; onDone fires on end or error. */
      speak: function (text, gender, onDone) {
        if (!synth) return false;
        try {
          synth.cancel();
          var utter = new SpeechSynthesisUtterance(text);
          var voice = pickVoice(cachedVoices || synth.getVoices(), gender);
          if (voice) utter.voice = voice;
          // Slightly slower and warmer than the default — reads like someone explaining
          // something rather than a screen reader rushing through text.
          utter.rate = 0.95;
          utter.pitch = 1.03;
          utter.onend = onDone;
          utter.onerror = onDone;
          synth.speak(utter);
          return true;
        } catch (e) {
          return false;
        }
      },

      stopSpeaking: function () {
        try { if (synth) synth.cancel(); } catch (e) { /* ignore */ }
      }
    };
  }
})();
