// (Mac 1.3.114, Windows 0.3.32) A stand-in for a Macmillan Learning assignment, laid out as Macmillan's own is — its
// Integration Hub page with the assessment player (SAC) in a frame — down to the data-test-id attributes the app's skin
// reads (mac/Simpl/Engine/MacmillanHook.swift, windows/SimplFluent/Screens/Alta/MacmillanHook.cs): the cover with Open
// Assignment and the grading settings; then the side list of questions, the score, Resources, AI Tutor, Solution, the
// main button (Submit Answer, Next Question), the previous and next arrows, the attempts menu, and the question's parts —
// text, a picture, numeric entries with units, multiple choice, multiple select. The questions are this mock's own.

export const MACMILLAN_HUB_HTML = `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>Integration Hub</title><style>
body{margin:0;font-family:Helvetica,Arial,sans-serif;background:#f4f5f7}header{height:64px;display:flex;align-items:center;gap:12px;padding:0 20px;background:#fff;border-bottom:1px solid #ddd}
header h1{font-size:20px;margin:0}main{position:absolute;top:65px;left:0;right:0;bottom:0}iframe{width:100%;height:100%;border:0}#rc{position:fixed;right:16px;bottom:16px}
</style></head><body><a href="#main">Skip to main content</a><header><button aria-label="Back to My Course">&#8592;</button><button aria-label="Go back">&#8592;</button>
<h1 aria-label="Assessment" data-testid="Assessment">Assessment</h1><span>Due: Mon, Oct 26</span></header>
<main aria-label="Main content"><iframe title="Assessment" src="/mock-macmillan/sac/12778033"></iframe></main><button id="rc" aria-label="Open Resource Center">?</button></body></html>`;

const QUESTIONS = [
  { parts: [
    ['text', 'A cart of mass <i>m</i> = 4.00 kg is pushed across a level, frictionless track by a steady force of 10.0 N.'],
    ['picture', 'A cart on a level track, pushed to the right by a horizontal arrow labelled F.'],
    ['numeric', 'What is the cart’s acceleration <i>a</i>?', '<i>a</i> =', 'm/s<sup>2</sup>', '2.5'],
    ['numeric', 'How far does it travel from rest in 2.00 s?', '<i>d</i> =', 'm', '5'],
  ] },
  { parts: [
    ['text', 'A 2 kg book rests on a table.'],
    ['choice', 'What is the net force on the book?', ['0 N', '2 N', '9.8 N', '19.6 N'], 0],
  ] },
  { parts: [
    ['text', 'A ball swings in a vertical circle on a string.'],
    ['select', 'Which forces act on the ball at the top of the circle? Select all that apply.', ['the tension in the string', 'the force of gravity (weight)', 'the normal force'], [0, 1]],
  ] },
  { parts: [
    ['text', 'An elevator of mass 800 kg rises at a constant speed.'],
    ['numeric', 'What is the tension <i>T</i> in its cable? Use <i>g</i> = 9.8 m/s<sup>2</sup>.', '<i>T</i> =', 'N', '7840'],
  ] },
];

export const MACMILLAN_SAC_HTML = `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>Assessment</title><style>
body{margin:0;font-family:Helvetica,Arial,sans-serif;color:#222;background:#fff}#app{display:flex;height:100vh}
#side{width:300px;border-right:1px solid #ddd;overflow:auto;background:#fafafa}#main{flex:1;display:flex;flex-direction:column;min-width:0}
#check-bar{display:flex;align-items:center;gap:12px;height:54px;padding:0 16px;border-bottom:1px solid #ddd}#check-bar ul{display:flex;gap:6px;list-style:none;margin:0;padding:0}
[data-test-id=btn-sac-main]{background:#1565c0;color:#fff;border:0;border-radius:4px;padding:8px 14px}.raptor-container{flex:1;overflow:auto;padding:12px 20px}
#question-nav li{padding:10px 14px;border-top:1px solid #eee;cursor:pointer}#question-nav li.on{background:#e8f0fe}.module-container{margin:12px 0}
.numeric-entry-input{width:120px}.grading-status{font-weight:700}.cover{max-width:640px;margin:40px auto}
</style></head><body><div id="app"></div><script>
var Q=${JSON.stringify(QUESTIONS)};
var S={open:false,at:0,done:{},pts:{},answers:{},attempts:{},view:'question'};
var TOTAL=Q.length*100;
function esc(s){return String(s)}
function score(){var t=0;for(var k in S.pts)t+=S.pts[k];return t}
function completed(){return Object.keys(S.done).length}
var POLICIES='<h4>Grading Settings</h4><button data-test-id="show-policy-details-button">Hide</button><ul>'+
 '<li data-test-id="policy-unlimited-attemps"><p><b>Unlimited attempts</b> per question.</p></li>'+
 '<li data-test-id="policy-partial-credit"><p><b>-5%</b> partial credit for incorrect attempts.</p></li>'+
 '<li data-test-id="policy-late"><p>Submit up to <b>3 days late</b> for a daily penalty of <b>10%</b> off the total score.</p></li>'+
 '<li data-test-id="policy-solutions"><p>View <b>solution explanations</b> after completing each question.</p></li>'+
 '<li data-test-id="policy-ai-tutor"><p>Access to <b>AI Tutor</b> assistance.</p></li></ul>';
function cover(){
 document.getElementById('app').innerHTML='<div class="cover"><h1>W8 Homework: Forces in Motion</h1><p>Due Mon, Oct 26, 11:59 pm</p><p>'+Q.length+' questions</p><button id="open">Open Assignment</button>'+POLICIES+'</div>';
 document.getElementById('open').onclick=function(){S.open=true;draw()};
}
function part(p,i,n){
 var done=S.done[n],a=(S.answers[n]||{});
 if(p[0]==='text')return '<article class="module-container" role="group" aria-label="Ungraded Text"><div data-module-type="static_textfield" class="sl-module"><div class="raw-html"><p>'+p[1]+'</p></div></div></article>';
 if(p[0]==='picture')return '<article class="module-container" role="group" aria-label="Ungraded Text"><div data-module-type="static_textfield"><img alt="'+p[1]+'" width="320" height="120" src="data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="320" height="120"><rect x="100" y="50" width="90" height="40" fill="#c33"/><line x1="20" y1="95" x2="300" y2="95" stroke="#333"/><line x1="30" y1="70" x2="95" y2="70" stroke="#06c" stroke-width="4"/></svg>')+'"></div></article>';
 if(p[0]==='numeric'){var v=a[i]||'';var ok=done?'Correct':(S.attempts[n]&&a['w'+i]?'Incorrect':'');
  return '<article class="module-container" role="group" aria-label="Ungraded Text"><div class="raw-html"><p>'+p[1]+'</p></div></article><article class="module-container" data-module-id="'+n+i+'" role="group" aria-label="Numeric Entry"><div data-module-type="numeric_entry" class="sl-module"><label><span data-test-id="equation-prefix">'+p[2]+'</span></label>'+(ok?'<div data-test-id="result-frame-'+n+i+'" aria-label="'+ok+'"></div>':'')+'<input class="numeric-entry-input" data-i="'+i+'" value="'+v+'"'+(done?' disabled':'')+'> '+p[3]+(ok?'<div class="grading-status">'+ok+'</div>':'')+'</div></article>';}
 if(p[0]==='choice'||p[0]==='select'){var t=p[0]==='choice'?'radio':'checkbox';var h='<article class="module-container" role="group" aria-label="'+(t==='radio'?'Multiple Choice':'Multiple Select')+'"><div data-module-type="'+(t==='radio'?'multiple_choice':'multiple_select')+'"><p>'+p[1]+'</p>';
  p[2].forEach(function(o,k){var on=(a[i]||[]).indexOf(k)>=0;h+='<label><input type="'+t+'" name="q'+n+'p'+i+'" data-i="'+i+'" data-k="'+k+'"'+(on?' checked':'')+(done?' disabled':'')+'> '+o+'</label><br>'});
  return h+'</div></article>';}
 return '';
}
function draw(){
 if(!S.open)return cover();
 var n=S.at,q=Q[n],done=S.done[n];
 var side='<nav id="side"><button data-test-id="side-nav-menu-button" aria-label="Collapse assignment menu"><span data-test-id="side-nav-button-title">Completed '+completed()+' of '+Q.length+' Questions</span></button>'+POLICIES+
  '<ul id="question-nav" data-test-id="items">'+Q.map(function(_,k){var d=S.done[k];return '<li role="button" class="'+(k===n?'on':'')+'" data-test-id="item-'+(k+1)+'-'+(d?'success':(S.attempts[k]?'error':'default'))+'-row"><div data-test-id="item-'+(k+1)+'-number">Q'+(k+1)+'</div><div>'+(d?'Solved in '+S.attempts[k]+' attempt'+(S.attempts[k]===1?'':'s'):(S.attempts[k]?S.attempts[k]+' attempt'+(S.attempts[k]===1?'':'s'):'Unlimited attempts'))+'</div>'+(d?'<div data-test-id="item-'+(k+1)+'-success"><div data-test-id="item-'+(k+1)+'-points">+'+S.pts[k]+'</div></div>':'')+'</li>'}).join('')+'</ul></nav>';
 var bar='<nav id="check-bar" aria-label="Assignment Tools"><div data-test-id="final-score-fraction" aria-label="Assignment Score: '+score()+' out of '+TOTAL+'"><span>+'+score()+'</span> pts <span>/'+TOTAL+'</span></div><button data-test-id="switch-to-new-layout">Switch to New Layout</button><ul>'+
  '<li><button aria-label="Open Resources">Resources</button></li>'+(done?'<li><button data-test-id="checkbar-solution-button" aria-label="Open Solution">Solution</button></li>':'<li><button aria-label="Open AI Tutor">AI Tutor</button></li>')+
  '</ul><button data-test-id="btn-sac-main" aria-label="'+(done?'Next Question':'Submit Answer')+'">'+(done?'Next Question':'Submit Answer')+'</button><div id="toggle-fullscreen" aria-label="Expand to Full Screen Mode"></div></nav>';
 var head='<div id="attempts-holder" data-test-id="attempts-holder-container"><div id="question-name" aria-label="Sequential Question"><button data-test-id="nav-item-back" aria-label="previous question">&lsaquo;</button><h2 data-test-id="nav-question-header">Question '+(n+1)+' of '+Q.length+'</h2><button data-test-id="nav-item-forward" aria-label="next question">&rsaquo;</button></div>'+
  (S.attempts[n]?'<button data-test-id="attempts-dropdown-button" aria-label="Viewing '+(S.view==='question'?'the question':'my answer')+'.">'+(S.view==='question'?'Question':'My Answer')+'</button><ul data-test-id="attempts"><li><button data-test-id="attempt-nav-question-button">Question</button></li><li><button data-test-id="attempt-1-nav-button">My Answer</button></li></ul>':'')+'</div>';
 var body='<div id="raptor-content-container" aria-label="Question content"><p data-test-id="copyright-box">© Macmillan Learning</p>'+q.parts.map(function(p,i){return part(p,i,n)}).join('')+'</div>';
 document.getElementById('app').innerHTML=side+'<section id="main">'+bar+'<div class="raptor-container">'+head+body+'</div></section>';
 document.title='Question '+(n+1)+' of '+Q.length+' - W8 Homework: Forces in Motion';
 wire();
}
function wire(){
 var n=S.at;
 document.querySelectorAll('#question-nav li').forEach(function(li,k){li.onclick=function(){S.at=k;S.view='question';draw()}});
 document.querySelector('[data-test-id=nav-item-back]').onclick=function(){if(S.at>0){S.at--;draw()}};
 document.querySelector('[data-test-id=nav-item-forward]').onclick=function(){if(S.at<Q.length-1){S.at++;draw()}};
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
 var dd=document.querySelector('[data-test-id=attempt-nav-question-button]');if(dd)dd.onclick=function(){S.view='question';draw()};
 var my=document.querySelector('[data-test-id=attempt-1-nav-button]');if(my)my.onclick=function(){S.view='answer';draw()};
}
draw();
</script></body></html>`;
