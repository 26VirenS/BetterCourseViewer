// (Mac 1.3.114, Windows 0.3.32) A stand-in for a Macmillan Learning assignment, laid out as Macmillan's own is — its
// Integration Hub page with the assessment player (SAC) in a frame — (Mac 1.3.197, Windows 0.3.92) drawn from the page
// code of a real one (a recording of a student's homework): the same ids, classes and data-test-id attributes Simpl's
// look reads (extension/tools/macmillan/look.js), and Macmillan's own colours and rules for the look turned off. The cover
// with Open Assignment and the grading settings; then the side list of questions, the score, Resources, AI Tutor,
// Solution, the main button (Submit Answer, Next Question), the previous and next arrows, the attempts menu, the verdict
// along the foot, and the question's parts — text, a picture, numeric entries with units, multiple choice, multiple
// select. The questions are this mock's own. ?state=open starts on the first question, ?state=answered with it solved
// (the hub passes its own ?state= on to the player).

export const MACMILLAN_HUB_HTML = `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>Integration Hub</title><style>
html,body{height:100%}body{margin:0;font-family:"Source Sans Pro",Helvetica,Arial,sans-serif;background:#fff}.skip{position:absolute;left:-999px}
.hub_molecule_app_header{display:flex;align-items:center;gap:4px;height:56px;padding:0 8px;background:#000;color:#fff}.hub_molecule_app_header>button{display:inline-flex;align-items:center;justify-content:center;width:40px;height:40px;background:none;border:0;border-radius:50%;color:#fff;cursor:pointer}.hub_molecule_app_header svg{fill:currentColor}
.app_header_title_txt{flex:1;margin:0 0 0 6px;font-size:18px;font-weight:400;white-space:nowrap}[data-test-id=assign-due-div]{display:flex;align-items:center;gap:8px;height:56px;padding:0 16px;background:#00758e;color:#fff;font-size:14px}[data-test-id=assign-due-div] svg{fill:#fff}
#main-content{position:absolute;top:56px;left:0;right:0;bottom:0}#main-content>div,.hub_molecule_iframe,.hub_molecule_iframe iframe{display:block;width:100%;height:100%;border:0}
#rc{position:fixed;left:16px;bottom:16px;width:36px;height:36px;border-radius:50%;border:0;background:#1565c0;color:#fff;font-weight:700}
</style></head><body><div id="__next"><div data-test-id="atom-hubbox"><a class="skip" data-test-id="skip-to-main-content" href="#main-content">Skip to main content</a>
<div data-test-id="atom-hubbox" class="hub_molecule_app_header"><button data-test-id="hubButton" aria-label="Back to My Course" class="home_btn"><svg width="22" height="22" viewBox="0 0 24 24"><path d="M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z"/></svg></button><button data-test-id="molecule-back-arrow-link" aria-label="Go back" class="back_btn"><svg width="22" height="22" viewBox="0 0 24 24"><path d="M20 11H7.8l5.6-5.6L12 4l-8 8 8 8 1.4-1.4L7.8 13H20z"/></svg></button>
<h1 data-testid="Assessment" aria-label="Assessment" class="app_header_title_txt">Assessment</h1><div data-test-id="assign-due-div"><span data-test-id="assign-calendar-icon"><svg width="18" height="18" viewBox="0 0 24 24"><path d="M19 4h-1V2h-2v2H8V2H6v2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2zm0 16H5V9h14z"/></svg></span><span data-test-id="assign-due-msg"><strong>Due:</strong> Mon, Oct 26</span></div></div>
<div id="main-content" role="region" aria-label="Main content"><div><div data-test-id="atom-hubbox" class="hub_molecule_iframe"><iframe title="W8 Homework: Forces in Motion" name="launch-frame" data-test-id="molecule-iframe"></iframe></div></div></div></div></div>
<button id="rc" class="_pendo-badge _pendo-resource-center-badge-container" aria-label="Open Resource Center">?</button>
<form id="launch" target="launch-frame" method="get" action="/mock-macmillan/launch"><input type="hidden" name="folded"><input type="hidden" name="state"></form>
<script>(function(){var f=document.getElementById('launch');f.folded.value=/folded=1/.test(location.search)?'1':'';var m=/[?&]state=([a-z]+)/.exec(location.search);f.state.value=m?m[1]:'';f.submit();})();</script></body></html>`;

const QUESTIONS = [
  { layout: 'two_col_single_right', parts: [
    ['text', 'A cart of mass <i>m</i> = 4.00 kg is pushed across a level, frictionless track by a steady force of 10.0 N.'],
    ['picture', 'A cart on a level track, pushed to the right by a horizontal arrow labelled F.'],
    ['numeric', 'What is the cart’s acceleration <i>a</i>?', '<i>a</i> =', 'm/s<sup>2</sup>', '2.5'],
    ['numeric', 'How far does it travel from rest in 2.00 s?', '<i>d</i> =', 'm', '5'],
  ] },
  { parts: [
    ['text', 'A 2 kg book rests on a table.'],
    ['picture', 'A book resting on a table, its weight drawn down and the table’s push drawn up.'],
    ['choice', 'What is the net force on the book?', ['0 N', '2 N', '9.8 N', '19.6 N'], 0],
  ] },
  { parts: [
    ['text', 'A ball swings in a vertical circle on a string.'],
    ['labeling', 'Answer Bank', ['tension', 'weight', 'normal force']],
    ['select', 'Which forces act on the ball at the top of the circle? Select all that apply.', ['the tension in the string', 'the force of gravity (weight)', 'the normal force'], [0, 1]],
  ] },
  { layout: 'two_col_all', parts: [
    ['text', 'An elevator of mass 800 kg rises at a constant speed.'],
    ['numeric', 'What is the tension <i>T</i> in its cable? Use <i>g</i> = 9.8 m/s<sup>2</sup>.', '<i>T</i> =', 'N', '7840'],
  ] },
];

export const MACMILLAN_SAC_HTML = `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>Assessment</title><style>
/* (Mac 1.3.197, Windows 0.3.92) Macmillan's own rules, as its style sheet has them (sac/static/css/41.*.chunk.css) and its page draws them: the side panel 300 px (54 px folded), the bar 54 px with its tools between rules, the 960-px sheet set 25 px in, the verdict bar along the foot */
html,body{height:100%}body{margin:0;font-family:"Source Sans Pro",Arial,Helvetica,sans-serif;color:#242424;font-size:15px}.visuallyhidden{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)}
#root,#sac{height:100%}#sac,#main{display:flex}
#side-nav{display:flex;flex-direction:column;box-sizing:border-box;height:100%;min-width:300px;width:300px;background:#fff;border-right:1px solid #ddd;z-index:12}#side-nav.closed{min-width:54px;width:54px}
.side-nav-item-container{display:flex;flex-direction:column;height:100%;min-height:0}#side-nav .toggle{flex:none;height:54px;border-bottom:1px solid #ddd}#side-nav .toggle>button{display:flex;align-items:center;width:100%;height:100%;padding:0 14px;background:none;border:0;color:#00758e;font:inherit;cursor:pointer}
#side-nav .flex-span{display:flex;align-items:center;gap:10px;width:100%}#side-nav svg{flex:none;fill:#00758e}#side-nav .panel-label{display:flex;justify-content:space-between;align-items:center;flex:1;color:#000;font-size:16px;font-weight:600;text-align:left}#side-nav.closed .panel-label{display:none}
.mm-policies{flex:none;padding:14px;border-bottom:1px solid #ddd}.mm-policies>div{display:flex;justify-content:space-between;align-items:center}.mm-policies h4{margin:0;font-size:16px;font-weight:600}.mm-policies button{background:none;border:0;color:#00758e;font:inherit;font-size:13px;font-weight:600;text-transform:uppercase;cursor:pointer}.mm-policies ul{margin:10px 0 0;padding-left:18px;font-size:14px}.mm-policies p{margin:4px 0}
#side-nav .content{flex:1;min-height:0;overflow-y:auto;background:#fff}#question-nav{list-style:none;margin:0;padding:0;color:#000}
#question-nav>li{display:flex;justify-content:space-between;align-items:center;padding:13px 14px;border-bottom:1px solid #ddd;background:#fff;cursor:pointer}#question-nav>li.mm-on{background:#cff3f9}#question-nav [data-test-id$="-number"]{font-size:15px;font-weight:600;line-height:22px}#question-nav .mm-sub{font-size:12px;color:#686868}
.successPill{display:flex;align-items:center;gap:4px;padding:1px 8px;border:1px solid #3b822e;border-radius:4px;color:#3b822e;fill:#3b822e;font-size:13px}
#main{flex-direction:column;height:100%;min-width:0;width:calc(100vw - 300px);overflow-x:auto;overflow-y:hidden;background:#f3f3f3}#side-nav.closed:not(.menu-selected)~#main{width:calc(100vw - 54px)}
#check-bar{display:flex;align-items:center;flex:none;height:54px;background:#fff;border-bottom:1px solid #ddd}#check-bar .header-actions-container{display:flex;align-items:center;flex-grow:1;gap:24px;padding-left:16px}
.mm-pts{font-size:20px;font-weight:600}.mm-ptsl{margin-left:3px;font-size:12px}.mm-tot{font-size:12px;color:#686868}[data-test-id=switch-to-new-layout]{background:none;border:0;color:#00758e;font:inherit;font-size:14px;cursor:pointer}.mm-beta{margin-left:4px;padding:1px 4px;border-radius:3px;background:#00758e;color:#fff;font-size:10px}
.check-bar-actions{display:flex;align-items:center;height:100%}#tools{display:flex;height:100%;margin:0;padding:0;list-style:none;color:#00758e}#tools li{height:100%;min-width:120px;border-right:1px solid #ddd}#tools li:first-child{border-left:1px solid #ddd}
#tools button{display:flex;align-items:center;justify-content:center;gap:6px;width:100%;height:100%;padding:0 14px;background:none;border:0;color:#00758e;font:inherit;font-size:15px;font-weight:600;cursor:pointer}#tools svg{fill:#00758e}
[data-test-id=btn-sac-main]{margin:0 14px;padding:9px 22px;background:#00758e;color:#fff;border:0;border-radius:3px;font:inherit;font-size:15px;font-weight:600;white-space:nowrap;cursor:pointer}
.fullscreen-button{width:56px;height:100%;border-left:1px solid #ddd}.fullscreen-button button{width:100%;height:100%;background:none;border:0;cursor:pointer}.fullscreen-button svg{fill:#00758e}
.raptor-container{position:relative;display:flex;flex-direction:column;flex-grow:1;min-height:0;margin-top:1px;background:#f3f3f3}
#attempts-holder{position:relative;z-index:11;display:flex;height:40px;min-height:40px;background:#f3f3f3}#question-name{position:relative;width:186px;height:40px;line-height:40px;font-size:14px;text-align:center}
#question-name button{position:absolute;top:0;height:40px;background:transparent;border:0;color:#00758e;cursor:pointer}#question-name .left{left:0}#question-name .right{right:0}#question-name svg{fill:#00758e}#nav-question-num{display:inline-block;width:130px;margin:0;color:#00758e;font-size:15px;font-weight:600}
#attempts-holder.dropdown #attempts-button{position:absolute;right:73px;top:5px;padding:5px 12px;background:#fff;border:1px solid #c8c8c8;border-radius:3px;color:#00758e;font:inherit;font-size:14px;cursor:pointer}#attempts-button svg{fill:#00758e;vertical-align:middle}
#attempts-holder.dropdown #attempts{position:absolute;right:73px;top:38px;display:none;margin:0;padding:0;list-style:none;background:#fff;border:1px solid #c8c8c8}#attempts-holder.dropdown #attempts.open{display:block}#attempts button{display:block;width:100%;padding:8px 14px;background:none;border:0;font:inherit;text-align:left;cursor:pointer}
#raptor-content-container{position:relative;flex:1;min-height:0;width:100%;overflow-y:auto;background:#f3f3f3;color:#000}#raptor-content-container .content-area{position:absolute;left:0;right:0;width:960px;margin:10px auto 0 25px}
.authoring-area{margin-bottom:30px}.authoring-area>.parent-module-container{display:block;position:relative;box-sizing:border-box;min-height:calc(100vh - 135px);padding:10px 0;background:#fff;box-shadow:0 1px 2px rgba(0,0,0,.3)}
.flowable-layout .module-container{box-sizing:border-box;padding:10px 10px 10px 24px;position:relative;vertical-align:top}.authoring-area .module-container:last-of-type{min-height:170px}
.flowable-layout.two_col_single_right .module-container{display:inline-block;width:50%}.flowable-layout.two_col_single_right .module-container:nth-of-type(2){float:right}.flowable-layout.two_col_single_right .module-container:nth-of-type(n+3){display:block}
.raw-html img{display:block;max-width:100%}.raw-html div[style*="width:900px"]{max-width:100%}[copyrightmessagestyle]{position:absolute;transform:rotate(270deg);z-index:2;left:-55px;top:81px}
.numeric-entry-table{display:table}.graded-equations-td{display:table-cell;vertical-align:middle;padding:.8em .75em 0 0;position:relative}.numeric-entry-border{background:#fff;border:2px solid #888;border-radius:5px;height:3.1em;padding:.35em .25em 0;white-space:nowrap;position:relative}.numeric-entry-border.focused{border-color:#51647a}.numeric-entry-border.disabled{background:#f3f3f3;border-color:#ddd}.numeric-entry-input{background:none;border:none;font-size:18px;width:100%}
.numeric-entry-toolmenu{background:#fff;border:1px solid #cbcbcb;padding:4px;position:absolute;width:210px;z-index:9}.numeric-entry-toolmenu>span{background:#f3f4f8;display:inline-block;height:2.2em;width:100%;text-transform:uppercase}.numeric-entry-toolmenu>button{background:#fff;border:1px solid #0600b4;color:#0600b4;width:100%}
.flowable-layout.two_col_all .module-container{display:inline-block;width:50%}.grading-status{display:inline;border-radius:20px;font-weight:600;padding:3px 8px;color:#255719;background:#daf4d4}.module-graded-incorrect .grading-status{color:#8a1c1c;background:#ffe7e5}.grading-status{font-weight:700}.cover{max-width:640px;margin:40px auto}
#results{position:absolute;left:0;right:0;bottom:0;z-index:21;display:flex;align-items:center;box-sizing:border-box;height:50px;padding-left:20px}#results.correct{background:#daf4d4;border:1px solid #3b822e}#results.correct svg{fill:#3b822e}#results.incorrect{background:#ffe7e5;border:1px solid #da1b2c}#results.incorrect svg{fill:#da1b2c}.results-message{display:flex;align-items:center;gap:5px;font-weight:600}
</style></head><body><div id="root"><main id="sac"></main></div><iframe title="widget" src="/mock-macmillan/sac/static/widget.html" style="position:fixed;left:0;bottom:0;width:2px;height:2px;border:0"></iframe><script>
var Q=${JSON.stringify(QUESTIONS)};
var ST=(/[?&]state=([a-z]+)/.exec(location.search)||[])[1]||'';
var S={open:ST==='open'||ST==='answered',at:0,done:{},pts:{},answers:{},attempts:{},view:'question',folded:/folded=1/.test(location.search),policies:false};
if(ST==='answered'){S.done[0]=true;S.pts[0]=100;S.attempts[0]=1;S.answers[0]={2:'2.5',3:'5'};S.view='answer'}
var TOTAL=Q.length*100;
var I={list:'<svg width="22" height="22" viewBox="0 0 24 24"><path d="M3 6h18v2H3zm0 5h18v2H3zm0 5h18v2H3z"/></svg>',left:'<svg width="18" height="18" viewBox="0 0 24 24"><path d="M15.4 7.4 14 6l-6 6 6 6 1.4-1.4-4.6-4.6z"/></svg>',right:'<svg width="18" height="18" viewBox="0 0 24 24"><path d="M8.6 16.6 10 18l6-6-6-6-1.4 1.4 4.6 4.6z"/></svg>',
 check:'<svg width="14" height="14" viewBox="0 0 24 24"><path d="M9 16.2 4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4z"/></svg>',solved:'<svg width="20" height="20" viewBox="0 0 24 24"><path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm-2 15-5-5 1.4-1.4 3.6 3.6 7.6-7.6L19 8z"/></svg>',wrong:'<svg width="20" height="20" viewBox="0 0 24 24"><path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm5 13.6L15.6 17 12 13.4 8.4 17 7 15.6 10.6 12 7 8.4 8.4 7 12 10.6 15.6 7 17 8.4 13.4 12z"/></svg>',
 book:'<svg width="18" height="18" viewBox="0 0 24 24"><path d="M18 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V4a2 2 0 0 0-2-2zM6 4h5v8l-2.5-1.5L6 12z"/></svg>',tutor:'<svg width="18" height="18" viewBox="0 0 24 24"><path d="M12 2 9.5 8.5 3 11l6.5 2.5L12 20l2.5-6.5L21 11l-6.5-2.5z"/></svg>',bulb:'<svg width="18" height="18" viewBox="0 0 24 24"><path d="M9 21a1 1 0 0 0 1 1h4a1 1 0 0 0 1-1v-1H9zm3-19a7 7 0 0 0-4 12.7V17a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1v-2.3A7 7 0 0 0 12 2z"/></svg>',
 full:'<svg width="20" height="20" viewBox="0 0 24 24"><path d="M7 14H5v5h5v-2H7zm-2-4h2V7h3V5H5zm12 7h-3v2h5v-5h-2zM14 5v2h3v3h2V5z"/></svg>',down:'<svg width="14" height="14" viewBox="0 0 24 24"><path d="M7 10l5 5 5-5z"/></svg>'};
function esc(s){return String(s)}
function score(){var t=0;for(var k in S.pts)t+=S.pts[k];return t}
function completed(){return Object.keys(S.done).length}
var POLICIES_LIST='<ul>'+
 '<li data-test-id="policy-unlimited-attemps"><p><b>Unlimited attempts</b> per question.</p></li>'+
 '<li data-test-id="policy-partial-credit"><p><b>-5%</b> partial credit for incorrect attempts.</p></li>'+
 '<li data-test-id="policy-late"><p>Submit up to <b>3 days late</b> for a daily penalty of <b>10%</b> off the total score.</p></li>'+
 '<li data-test-id="policy-solutions"><p>View <b>solution explanations</b> after completing each question.</p></li>'+
 '<li data-test-id="policy-ai-tutor"><p>Access to <b>AI Tutor</b> assistance.</p></li></ul>';
var POLICIES='<h4>Grading Settings</h4><button data-test-id="show-policy-details-button" aria-expanded="true">Hide</button>'+POLICIES_LIST;
function policies(){return '<div class=".no-css mm-policies"><div><h4>Grading Settings</h4><button data-test-id="show-policy-details-button" aria-expanded="'+S.policies+'">'+(S.policies?'Hide':'Show')+'</button></div>'+(S.policies?POLICIES_LIST:'')+'</div>'}
function cover(){
 document.getElementById('sac').innerHTML='<div class="cover"><h1>W8 Homework: Forces in Motion</h1><p>Due Mon, Oct 26, 11:59 pm</p><p>'+Q.length+' questions</p><button id="open">Open Assignment</button>'+POLICIES+'</div>';
 document.getElementById('open').onclick=function(){S.open=true;draw()};
}
function part(p,i,n){
 var done=S.done[n],a=(S.answers[n]||{});
 if(p[0]==='text')return '<article class="module-container" role="group" aria-label="Ungraded Text"><div data-module-type="static_textfield" class="sl-module"><div class="raw-html"><p>'+p[1]+'</p></div></div></article>';
 if(p[0]==='picture')return '<article class="module-container" role="group" aria-label="Ungraded Text"><div data-module-type="static_textfield" class="sl-module"><div class="raw-html"><p><md-never><div style="width:900px"><img alt="'+p[1]+'" width="900" style="width:900px" src="data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="320" height="240"><rect width="320" height="240" fill="#fff"/><rect x="100" y="150" width="90" height="40" fill="#c33"/><line x1="20" y1="195" x2="300" y2="195" stroke="#333"/><line x1="30" y1="170" x2="95" y2="170" stroke="#06c" stroke-width="4"/></svg>')+'"></div></md-never></p></div></div></article>';
 if(p[0]==='numeric'){var v=a[i]||'';var ok=done?'Correct':(S.attempts[n]&&a['w'+i]?'Incorrect':'');
  // (Mac 1.3.117) as Macmillan's number entry is: a table of the prefix, the field in its frame (its tools menu, ×10ʸ, shown while it is focused), the unit
  return '<article class="module-container" role="group" aria-label="Ungraded Text"><div class="sl-module"><span class="stix-font"><div class="raw-html"><p>'+p[1]+'</p></div></span></div></article><article class="module-container" data-module-id="'+n+i+'" role="group" aria-label="Numeric Entry"><div data-module-type="numeric_entry" class="ne-take-mode sl-module"><div class="numeric-entry-table" style="width:100%"><div class="graded-equations-td"><label><span data-test-id="equation-prefix" class="stix-font">'+p[2]+'</span></label></div><div class="graded-equations-td not-considered-in-grading-module-frame" style="width:100%"><div class="'+(ok?'module-graded-'+(ok==='Correct'?'correct':'incorrect'):'')+'">'+(ok?'<div data-test-id="result-frame-'+n+i+'" aria-label="'+ok+'"></div>':'')+'<div class="numeric-entry-border'+(done?' disabled':'')+'"><input class="numeric-entry-input editable" data-i="'+i+'" value="'+v+'"'+(done?' disabled':'')+'></div>'+(ok?'<div class="grading-status">'+ok+'</div>':'')+'</div><div class="numeric-entry-toolmenu" style="display:none" role="toolbar"><span class="tools">tools</span><button data-test-id="sci-not-button" aria-label="Change answer format to or from scientific notation"><span>×10<sup>y</sup></span></button></div></div><div class="graded-equations-td"><span class="stix-font">'+p[3]+'</span></div></div></div></article>';}
 if(p[0]==='labeling')return '<article class="module-container" role="group" aria-label="Labeling"><div data-module-type="labeling" class="sl-module"><div class="react-resizable" style="height:180px"><div class="sl-labeling" style="width:880px;position:relative"><div style="background:#fff;border:1px solid #ccc;width:300px;position:absolute;right:0;top:0"><div class="mm-bank-head" style="background:#51647a;color:#fff;padding:6px;text-align:center">'+p[1]+'</div>'+p[2].map(function(w){return '<span class="mm-chip" style="display:inline-block;margin:6px;padding:6px 10px;border:1px solid #999;color:#222">'+w+'</span>'}).join('')+'</div></div></div></div></article>';
 if(p[0]==='choice'||p[0]==='select'){var t=p[0]==='choice'?'radio':'checkbox';var h='<article class="module-container" role="group" aria-label="'+(t==='radio'?'Multiple Choice':'Multiple Select')+'"><div data-module-type="'+(t==='radio'?'multiple_choice':'multiple_select')+'"><p>'+p[1]+'</p>';
  p[2].forEach(function(o,k){var on=(a[i]||[]).indexOf(k)>=0;h+='<label><input type="'+t+'" name="q'+n+'p'+i+'" data-i="'+i+'" data-k="'+k+'"'+(on?' checked':'')+(done?' disabled':'')+'> '+o+'</label><br>'});
  return h+'</div></article>';}
 return '';
}
function draw(){
 if(!S.open)return cover();
 var n=S.at,q=Q[n],done=S.done[n];
 var title='Completed '+completed()+' of '+Q.length+' Questions';
 document.title='Question '+(n+1)+' of '+Q.length+' - W8 Homework: Forces in Motion';
 var rows=Q.map(function(_,k){var d=S.done[k],t=S.attempts[k]||0;
  return '<li data-test-id="item-'+(k+1)+'-'+(d?'success':(t?'error':'default'))+'-row" role="button" tabindex="0" class="no-style'+(k===n?' mm-on':'')+'"><div><div data-test-id="item-'+(k+1)+'-number">Q'+(k+1)+'</div><div class="mm-sub">'+(d?'Solved in <span>'+t+'</span> attempt'+(t===1?'':'s'):(t?t+' attempt'+(t===1?'':'s'):'Unlimited attempts'))+'</div></div><div><div>'+(d?'<div data-test-id="item-'+(k+1)+'-success" class="no-style successPill">'+I.check+'<div data-test-id="item-'+(k+1)+'-points" class="no-style">+'+S.pts[k]+'</div></div>':'')+'</div></div></li>'}).join('');
 var side='<nav id="side-nav" data-test-id="side-nav" class="'+(S.folded?'closed':'menu-selected')+'"><div class="side-nav-item-container"><div data-test-id="nav-toggle-item" class="toggle nav-list secondary'+(S.folded?'':' selected')+'"><button data-test-id="side-nav-'+title+'-menu-button" aria-expanded="'+!S.folded+'" aria-label="'+(S.folded?'Expand':'Collapse')+' assignment menu" aria-description="'+title+'"><span class="flex-span">'+I.list+'<div class="panel-label"><span data-test-id="side-nav-button-title">'+title+'</span><div data-test-id="side-nav-button-collapse" class="menu-chevron">'+I.left+'</div></div></span></button></div>'+
  (S.folded?'':policies()+'<div class="content nav-questions"><span data-test-id="aria-wrapper" role="navigation" aria-label="Non-Sequential Question"><ul id="question-nav" data-test-id="items" class="question-nav-item-container">'+rows+'</ul></span></div>')+'</div></nav>';
 var tool=function(id,label,icon){return '<li id="tool-'+id+'"><button data-test-id="checkbar-'+id+'-button" aria-label="Open '+label+'">'+icon+'<div><div data-test-id="'+id+'-button-text">'+label+'</div></div></button></li>'};
 var bar='<nav id="check-bar" aria-label="Assignment Tools"><h2 class="visuallyhidden">Assignment Tools</h2><div data-test-id="header-actions-container" class="header-actions-container"><div><div data-test-id="final-score-fraction" aria-label="Assignment Score: '+score()+' out of '+TOTAL+'"><div><div><span class="mm-pts">+'+score()+'</span><span class="mm-ptsl">pts</span></div><div class="mm-tot">/'+TOTAL+'</div></div></div></div><div><button data-test-id="switch-to-new-layout">Switch to New Layout<span class="mm-beta">Beta</span></button></div></div>'+
  '<div data-test-id="checkbar-actions" class="check-bar-actions"><ul id="tools" data-test-id="tools">'+tool('resources','Resources',I.book)+(done?tool('solution','Solution',I.bulb):tool('mat','AI Tutor',I.tutor))+'</ul>'+
  '<div><button data-test-id="btn-sac-main" aria-label="'+(done?'Next Question':'Submit Answer')+'" type="button">'+(done?'Next Question':'Submit Answer')+'</button></div><div id="toggle-fullscreen" data-test-id="toggle-fullscreen" aria-label="Expand to Full Screen Mode" class="fullscreen-button"><div><button aria-label="Expand to Full Screen Mode">'+I.full+'</button></div></div></div></nav>';
 var head='<div id="attempts-holder" data-test-id="attempts-holder-container" class="dropdown"><div id="question-name" aria-label="Sequential Question" class="heading2"><button data-test-id="nav-item-back" aria-label="previous question" class="left"><span class="flex-span">'+I.left+'</span></button><h2 id="nav-question-num" data-test-id="nav-question-header">Question '+(n+1)+' of '+Q.length+'</h2><button data-test-id="nav-item-forward" aria-label="next question" class="right"><span class="flex-span">'+I.right+'</span></button></div>'+
  (S.attempts[n]?'<div aria-label="Attempts Dropdown Menu "><button id="attempts-button" data-test-id="attempts-dropdown-button" class="cdl-button" aria-label="Viewing '+(S.view==='question'?'the question':'my answer')+'.">'+(S.view==='question'?'Question':'My Answer')+' '+I.down+'</button><ul id="attempts" data-test-id="attempts"><li><button data-test-id="attempt-nav-question-button">Question</button></li><li><button data-test-id="attempt-1-nav-button">My Answer</button></li></ul></div>':'')+'</div>';
 var body='<div id="raptor-content-container" aria-label="Question content"><div><section class="content-area take-mode"><div class="authoring-area '+(q.layout||'')+' flowable-layout"><article class="parent-module-container" role="presentation"><div copyrightmessagestyle="brandName"><p data-test-id="copyright-box">© Macmillan Learning</p></div>'+q.parts.map(function(p,i){return part(p,i,n)}).join('')+'</article></div></section></div></div>';
 var res=done?'<footer id="results" class="correct"><span data-test-id="results-bar-message" class="results-message">'+I.solved+'Solved</span></footer>':(S.attempts[n]?'<footer id="results" class="incorrect"><span data-test-id="results-bar-message" class="results-message">'+I.wrong+'Incorrect. Try again.</span></footer>':'');
 document.getElementById('sac').innerHTML='<h2 class="visuallyhidden">'+document.title+'</h2>'+side+'<section id="main">'+bar+'<div class="raptor-container">'+head+body+res+'</div></section>';
 wire();
}
function wire(){
 var n=S.at;
 document.querySelectorAll('#question-nav > li').forEach(function(li,k){li.onclick=function(){S.at=k;S.view='question';draw()}});
 document.querySelector('#side-nav [data-test-id=nav-toggle-item] > button').onclick=function(){S.folded=!S.folded;draw()};
 var pol=document.querySelector('#side-nav [data-test-id=show-policy-details-button]');if(pol)pol.onclick=function(){S.policies=!S.policies;draw()};
 document.querySelector('[data-test-id=nav-item-back]').onclick=function(){if(S.at>0){S.at--;draw()}};
 document.querySelector('[data-test-id=nav-item-forward]').onclick=function(){if(S.at<Q.length-1){S.at++;draw()}};
 document.querySelectorAll('.numeric-entry-input').forEach(function(el){var b=el.closest('.numeric-entry-border'),m=el.closest('.graded-equations-td').querySelector('.numeric-entry-toolmenu');el.addEventListener('focus',function(){b.classList.add('focused');if(m)m.style.display=''});});
 document.querySelectorAll('input[data-i]').forEach(function(el){el.oninput=el.onchange=function(){var a=S.answers[n]=S.answers[n]||{};var i=el.getAttribute('data-i');
  if(el.type==='text')a[i]=el.value;else{var all=[];document.querySelectorAll('input[name=q'+n+'p'+i+']').forEach(function(x,k){if(x.checked)all.push(k)});a[i]=all}}});
 document.querySelector('[data-test-id=btn-sac-main]').onclick=function(){
  if(S.done[n]){if(S.at<Q.length-1)S.at++;draw();return}
  var a=S.answers[n]||{},right=true;S.attempts[n]=(S.attempts[n]||0)+1;
  Q[n].parts.forEach(function(p,i){if(p[0]==='numeric'){var ok=Math.abs(parseFloat(a[i])-parseFloat(p[4]))<=Math.abs(parseFloat(p[4]))*0.02;a['w'+i]=!ok;if(!ok)right=false}
   if(p[0]==='choice'&&(a[i]||[])[0]!==p[3])right=false;if(p[0]==='select'&&JSON.stringify((a[i]||[]).slice().sort())!==JSON.stringify(p[3]))right=false});
  if(right){S.done[n]=true;S.pts[n]=Math.max(0,100-5*(S.attempts[n]-1))}
  draw();
 };
 var ab=document.getElementById('attempts-button');if(ab)ab.onclick=function(){document.getElementById('attempts').classList.toggle('open')};
 var dd=document.querySelector('[data-test-id=attempt-nav-question-button]');if(dd)dd.onclick=function(){S.view='question';draw()};
 var my=document.querySelector('[data-test-id=attempt-1-nav-button]');if(my)my.onclick=function(){S.view='answer';draw()};
}
draw();
</script></body></html>`;
