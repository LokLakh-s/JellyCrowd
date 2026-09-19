/*
 * Jelly Crowd user guide — rendered inside the plugin's overlay panel (guide.html), no iframe.
 * Everything is looked up under .jcGuide so the guide never reaches into the Jellyfin UI around it.
 */
(function () {
  'use strict';
  const root = document.querySelector('.jcGuide');
  if (!root || root.dataset.jcGuideReady === '1') { return; }
  root.dataset.jcGuideReady = '1';
  const langButtons = () => Array.prototype.slice.call(root.querySelectorAll('.seg button'));

  // The guide renders inside the Jellyfin client, served from /web/ — so a relative path resolves against
  // THAT, not against the plugin, and 404s. (It worked while the guide lived in an iframe whose own URL
  // was the plugin's asset route.) Ask the client for the real URL, as guide.html does for this script.
  const apiUrl = p => (window.ApiClient && typeof window.ApiClient.getUrl === 'function')
    ? window.ApiClient.getUrl(p)
    : '/' + p;

  let IMG = {};   // filled from the served content
  let C = {};     // idem: the guide text, per language

  function esc(){}
  // A figure is skipped when the content names no file for it: the guide shipped with the plugin carries
  // no screenshots of anyone's library, and an instance adds its own through the content it serves.
  function fig(lang, key, cap){
    if (!IMG[key]) { return ''; }
    const narrow = key === 'notif' ? ' narrow' : '';
    return '<figure><div class="shot'+narrow+'"><img loading="lazy" alt="" src="'+apiUrl('JellyCrowd/Guide/Image/'+IMG[key])+'"></div><figcaption>'+cap+'</figcaption></figure>';
  }
  function render(lang){
    const t = C[lang];
    let h = '';
    h += '<header class="hero"><div class="wrap">'
      + '<span class="kicker">'+t.kicker+'</span>'
      + '<h1>'+t.h1+'</h1>'
      + '<p class="lede">'+t.lede+'</p>'
      + '<p class="where">'+t.where+'</p>'
      + '</div></header>';
    h += '<main class="wrap">';
    for (const s of t.steps){
      h += '<section class="step"><div class="step-head"><div class="num">'+s.n+'</div><div>'
        + '<h2>'+s.title+'</h2><p>'+s.intro+'</p></div></div><div class="body">';
      if (s.bullets && s.bullets.length){
        h += '<ul class="clean">'+s.bullets.map(b=>'<li>'+b+'</li>').join('')+'</ul>';
      }
      for (const [k,cap] of s.figs){ h += fig(lang,k,cap); }
      if (s.callout){ h += '<div class="callout"><span class="ic">'+s.callout.icon+'</span><p>'+s.callout.html+'</p></div>'; }
      h += '</div></section>';
    }
    // legend
    h += '<section class="legend-sec"><h2 class="sec">'+t.legend.title+'</h2><p class="sec-intro">'+t.legend.intro+'</p><div class="legend">'
      + t.legend.items.map(([lab,cls,desc])=>'<div class="lg"><span class="pill '+cls+'">'+lab+'</span><p>'+desc+'</p></div>').join('')
      + '</div></section>';
    // quota
    h += '<section class="quota-sec"><h2 class="sec">'+t.quota.title+'</h2>'
      + '<p class="sec-intro" style="margin-bottom:0">'+t.quota.body+'</p>'
      + '<div class="qbar-demo"><div class="qbar-label"><span>'+t.quota.barLabel+'</span><span>0 B / 30 GiB</span></div><div class="qbar"><span></span></div></div>'
      + '<ul class="clean">'+t.quota.bullets.map(b=>'<li>'+b+'</li>').join('')+'</ul></section>';
    // my library (mock rows rather than a screenshot: the expiry chip and the disabled renew are states
    // a capture would rarely show, and this one cannot go stale with a restyle)
    h += '<section class="lib-sec"><h2 class="sec">'+t.library.title+'</h2>'
      + '<p class="sec-intro" style="margin-bottom:0">'+t.library.body+'</p>'
      + '<div class="lib-demo">'
      + t.library.rows.map(r=>'<div class="lib-row"><span class="t">'+r.t+'</span><span class="sz">'+r.sz+'</span>'
        + (r.chip ? '<span class="pill p-purple">'+r.chip+'</span>' : '')
        + r.btns.map(b=>'<span class="btn">'+b+'</span>').join('')
        + '</div>').join('')
      + '</div>'
      + '<ul class="clean">'+t.library.bullets.map(b=>'<li>'+b+'</li>').join('')+'</ul></section>';
    // faq
    h += '<section class="faq-sec"><h2 class="sec">'+t.faqTitle+'</h2>'
      + t.faq.map(([q,a])=>'<details><summary>'+q+'</summary><p>'+a+'</p></details>').join('')
      + '</section>';
    h += '</main><footer><div class="wrap">'+t.footer+'</div></footer>';
    root.querySelector('.jcGuideApp').innerHTML = h;
    root.setAttribute('lang', lang);
  }
  function setLang(lang){
    render(lang);
    langButtons().forEach(b=>b.setAttribute('aria-pressed', String(b.dataset.lang===lang)));
  }

  // A language the served content does not carry falls back rather than rendering an empty guide.
  function resolveLang(){
    const wanted = (navigator.language||'fr').toLowerCase().startsWith('en') ? 'en' : 'fr';
    if (C[wanted]) { return wanted; }
    return C.en ? 'en' : Object.keys(C)[0];
  }

  fetch(apiUrl('JellyCrowd/Guide/Content'))
    .then(r => r.ok ? r.json() : Promise.reject(new Error('HTTP ' + r.status)))
    .then(data => {
      IMG = (data && data.images) || {};
      C = (data && data.languages) || {};
      if (!Object.keys(C).length) { throw new Error('no content'); }
      // Only offer a language the content actually has.
      langButtons().forEach(b => {
        if (!C[b.dataset.lang]) { b.style.display = 'none'; return; }
        b.addEventListener('click', () => setLang(b.dataset.lang));
      });
      setLang(resolveLang());
    })
    .catch(() => {
      root.dataset.jcGuideReady = '';   // let a later open try again
      root.querySelector('.jcGuideApp').textContent = '';
    });
})();
