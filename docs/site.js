/* NJ-Flush site script (shared by / and /ar/). Everything works without it: it only adds reveal, clip playback, tabs and the widget replica. */
(function () {
  'use strict';
  var reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };

  // reduced motion: no autoplay, the still stands in for the hero video
  var hero = $('#hero-video');
  if (hero && reduced) { hero.pause(); hero.remove(); }

  // reveal on scroll (IntersectionObserver, no scroll listener); content stays visible if this script never runs
  if ('IntersectionObserver' in window) {
    var io = new IntersectionObserver(function (es) {
      es.forEach(function (e) { if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); } });
    }, { threshold: .15 });
    document.documentElement.classList.add('reveal-on');
    $$('.reveal, .gauge').forEach(function (el) { io.observe(el); });
  }

  // flush clips: load and play only while on screen; reduced motion keeps the poster
  var clips = $$('video.clip');
  if (clips.length && 'IntersectionObserver' in window && !reduced) {
    var vo = new IntersectionObserver(function (es) {
      es.forEach(function (e) {
        var v = e.target;
        if (e.isIntersecting) { v.preload = 'auto'; var p = v.play(); if (p && p.catch) p.catch(function () {}); } else { v.pause(); }
      });
    }, { threshold: .3 });
    clips.forEach(function (v) { vo.observe(v); });
  }

  // panel tabs: one screenshot at a time, arrow keys move between them
  var list = $('[role="tablist"]');
  if (list) {
    var tabs = $$('[role="tab"]', list);
    var show = function (tab, focus) {
      tabs.forEach(function (t) {
        var on = t === tab;
        t.setAttribute('aria-selected', on ? 'true' : 'false');
        t.tabIndex = on ? 0 : -1;
        var panel = document.getElementById(t.getAttribute('aria-controls'));
        if (panel) panel.hidden = !on;
      });
      if (focus) tab.focus();
    };
    tabs.forEach(function (t, i) {
      t.addEventListener('click', function () { show(t, false); });
      t.addEventListener('keydown', function (e) {
        var rtl = document.documentElement.dir === 'rtl';
        var next = e.key === 'ArrowRight' ? (rtl ? -1 : 1) : e.key === 'ArrowLeft' ? (rtl ? 1 : -1) : 0;
        if (e.key === 'Home') { e.preventDefault(); show(tabs[0], true); }
        else if (e.key === 'End') { e.preventDefault(); show(tabs[tabs.length - 1], true); }
        else if (next) { e.preventDefault(); show(tabs[(i + next + tabs.length) % tabs.length], true); }
      });
    });
    show(tabs[0], false);
  }

  // widget replica: same colour rule as the app (cyan < 80, yellow < 90, orange < 96, red); example figures, not read from the PC
  var ram = $('#ram');
  if (ram) {
    var pct = $('#w-pct'), gb = $('#w-gb'), fill = $('#w-fill');
    var tint = function (p) { return p >= 96 ? '#f87171' : p >= 90 ? '#fb923c' : p >= 80 ? '#facc15' : '#22d3ee'; };
    var draw = function () {
      var p = +ram.value;
      pct.textContent = p + '%';
      gb.textContent = (15.85 * p / 100).toFixed(2) + '/15.85 GB';
      fill.style.width = p + '%';
      fill.style.background = tint(p);
    };
    ram.addEventListener('input', draw);
    draw();
  }
})();
