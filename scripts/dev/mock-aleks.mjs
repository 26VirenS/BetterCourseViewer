// (Mac 1.3.198, Windows 0.3.93) A stand-in for an ALEKS learning page, laid out as ALEKS's own is in the page code of a
// real session (a recording of a student's homework): the same ids, classes and ARIA the apps' look reads
// (extension/tools/aleks/look.js), and ALEKS's own colours and placing for the look turned off — the teal bar with the
// topic, its progress and the student's menu; the topics' tab and Español; the question on white with the answer box
// and its keys (Undo, Start Over); the tools in a column at the right (Help, Calculator, Videos, eBook, Messages); the
// actions along the foot (Explanation, Check); the legal line under them. The question is this mock's own: it is
// checked (Try Again, or Correct and Next), the calculator opens and closes, Explanation shows and hides.
// ?state=wrong starts with a wrong answer checked.

export const ALEKS_LEARN_HTML = `<!DOCTYPE html><html lang="en" style="height:100%"><head><meta charset="utf-8"><title>ALEKS - Student - Learn</title><style>
body{margin:0;height:100%;background:#c1cfd4;font-family:ProximaNova,"Helvetica Neue",Arial,sans-serif;color:#272a2a}
#welcomePage{position:relative;box-sizing:border-box;width:100%;height:100%;max-width:1280px;min-width:1024px;margin:auto;background:#f4f5f5}
.tooltiptarget_mainview{position:absolute;top:0;left:0;bottom:0;width:100%;box-sizing:border-box}
.tooltiptarget_banner.absolute_print{position:fixed;top:0;width:100%;max-width:1280px;height:0;background:#fff;z-index:1500}
header.aleks-header{position:fixed;top:0;box-sizing:border-box;width:100%;max-width:1280px;height:64px;display:flex;align-items:center;justify-content:space-between;padding:0 16px;background:#008298;color:#fff;z-index:9000}
.left-container{display:flex;align-items:center;gap:16px;flex:1;min-width:0}.right-container{display:flex;align-items:center;margin-left:16px}
.aleks-button{display:inline-flex;align-items:center;justify-content:center;cursor:pointer}.hamburger-menu-button{flex:none;width:40px;height:40px;border:1px solid #fff;border-radius:24px;background:rgba(89,98,98,.3);color:#fff}
.title-container{display:flex;align-items:center;justify-content:space-between;flex:1;min-width:0;gap:16px}.item-info{min-width:0}.item-info>div{white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.slice-name{font-size:12px;font-weight:700}.item-title{font-size:15px;font-weight:700}
.learning-status-container{flex:none;font-size:12px}.random-walk{display:flex;flex-direction:column;align-items:flex-start;gap:3px}.rw-row{display:flex;align-items:center;gap:6px}
.rw-track{position:relative;display:flex;gap:2px;width:200px;height:10px;border-radius:10px;background:#008298}.rw-seg{flex:1;box-sizing:border-box;height:10px;border:1px solid #fff;border-radius:10px;overflow:hidden}.rw-seg>div{height:100%;width:0;background:#396c29}
#userProfile{height:36px;padding:0 14px;border:1px solid #fff;border-radius:4px;background:rgba(89,98,98,.3);color:#fff;font:inherit;font-size:16px;cursor:pointer}
button.tooltiptarget_toggle_carousel{position:absolute;top:64px;left:16px;width:46px;height:30px;padding:0;border:2px solid;border-color:#008298 transparent transparent;border-radius:0 0 10px 10px;background:#c1ebf1;color:#008298;z-index:100}
#smt_LanguageToggleButton_button_input_toggle_spanish{position:absolute;top:68px;right:20px;padding:2px 8px;border:1px solid #6d7979;border-radius:25px;background:#fff;font:inherit;font-size:12px;font-weight:700;z-index:100}
#current-main-view{position:absolute;inset:62px 0 0;background:#fff;z-index:1}
.mainview_item_page{position:relative;padding-bottom:108px;background:#fff;z-index:1}
[id^=itemContainer_]{box-sizing:border-box;width:100%;min-height:400px;padding:30px 100px 162px 40px;background:#fff;font-family:Verdana,Geneva,sans-serif;font-size:14px;color:#000}
#algoPrompt p{margin:0 0 14px;line-height:1.6}
#result_feedback_alert{display:inline-block;position:relative;margin:20px 10px 20px 20px;padding:12px 12px 12px 35px;border:1px solid #b82821;border-radius:10px;font-family:ProximaNova,"Helvetica Neue",Arial,sans-serif}
#result_feedback_alert.right{border-color:#396c29}#result_feedback_alert b{display:block;color:#b82821;font-size:16px}#result_feedback_alert.right b{color:#396c29}#result_feedback_alert span{display:block;padding-top:15px;font-size:14px;font-weight:700}
.fb-icon{position:absolute;left:-20px;top:-1px;width:44px;height:44px;border-radius:50%;background:#b82821;color:#fff;display:flex;align-items:center;justify-content:center;font-size:22px}.right .fb-icon{background:#396c29}
#step_container{margin-top:24px;display:flex;align-items:flex-start;gap:14px;flex-wrap:wrap}
.aleks-editor-content{display:inline-block}#ansed_answer_1{position:relative;display:inline-block;width:252px;height:50px;overflow:hidden;vertical-align:top}
.ansed_root{position:absolute;inset:0;box-sizing:border-box;border:1px solid #000;background:#fff}.ansed_input{box-sizing:border-box;width:100%;height:100%;padding:0 46px 0 10px;border:0;outline:0;background:transparent;font:18px Verdana,Geneva,sans-serif}.ansed_unit{position:absolute;right:10px;top:14px;font-size:16px}
.aleks-editor-menu{display:inline-block}#containerEditButton{display:flex;margin:2.5px;border-radius:4px}#containerEditButton button{width:76px;height:44px;margin:4px;border:0;border-radius:4px;background:#006a7c;color:#fff;font-size:20px;cursor:pointer}
[role=complementary][aria-label=Tools]{position:fixed;right:80px;top:64px;margin-right:20px;margin-top:110px;z-index:1003}
[role=complementary] button.iconButton{width:36px;height:36px;margin:5px;padding:0;border:0;border-radius:50%;background:rgba(210,214,214,.3);color:#46707d;cursor:pointer;display:flex;align-items:center;justify-content:center}[role=complementary] button.iconButton.primary{background:#006a7c;color:#fff}
[role=complementary] svg{width:22px;height:22px;fill:currentColor}
[role=navigation][aria-label=Actions]{position:fixed;bottom:28px;box-sizing:border-box;width:100%;max-width:1280px;height:56px;display:flex;align-items:center;justify-content:space-between;padding:0 20px;background:#f4f5f5;border-top:3px solid #d1dbde;z-index:11}
[role=navigation] button{display:inline-block;height:26px;margin:0 8px 0 0;padding:0 11px;border-radius:25px;font:inherit;font-size:14px;font-weight:700;cursor:pointer}[role=navigation] button.standard{border:1px solid #6d7979;background:rgba(210,214,214,.3);color:#272a2a}[role=navigation] button.primary{border:0;background:#006a7c;color:#fff}
.aleks-legal-footer{position:fixed;bottom:0;box-sizing:border-box;width:100%;max-width:1280px;height:28px;display:flex;align-items:center;justify-content:flex-end;padding:0 16px;background:#596262;color:#fff;font-size:12px;z-index:11}.aleks-legal-footer a{color:#fff;margin-left:12px}
.dialog--mk1{position:fixed;right:160px;top:174px;width:250px;border:1px solid #6d7979;border-radius:10px;background:#fff;z-index:1100;font-family:ProximaNova,"Helvetica Neue",Arial,sans-serif}.dialog--mk1 .dlg-head{display:flex;align-items:center;justify-content:space-between;min-height:50px;padding:0 12px;border-radius:7px 7px 0 0;background:#f4f5f5;font-size:18px;font-weight:700}
.dialog--mk1 .dlg-screen{height:60px;margin:4px;border:1px solid #d1dbde;text-align:right;font-size:20px;padding:6px;box-sizing:border-box}.dialog--mk1 .dlg-keys{display:grid;grid-template-columns:repeat(4,1fr);gap:1px;margin:4px;background:#d1dbde}.dialog--mk1 .dlg-keys span{background:#fff;text-align:center;padding:9px 0;font-weight:700}
#explain{margin-top:24px;padding:14px;border-left:4px solid #008298;background:#f4f5f5;display:none}#explain.on{display:block}
</style></head><body style="margin:0"><div id="welcomePage"><div class="tooltiptarget_mainview">
<a class="hide_print" href="#current-main-view" style="position:absolute;left:-999px">Skip to Main Content</a>
<div role="banner" class="tooltiptarget_banner absolute_print"><button id="smt_LanguageToggleButton_button_input_toggle_spanish" class="standard small hide_print" type="button">Español</button>
<header class="aleks-sm-learning-header tooltiptarget_banner aleks-header aleks-header-element"><div class="left-container">
<button id="_sm-bannerr_b_" data-testid="hamburger_menu_button" aria-label="Main Menu" class="aleks-button hamburger-menu-button medium secondaryReversed icon-only" type="button"><svg width="22" height="22" viewBox="0 0 24 24" fill="currentColor"><path d="M3 6h18v2H3zm0 5h18v2H3zm0 5h18v2H3z"/></svg></button>
<div class="title-container"><div role="heading" class="item-info"><div class="aleks-typography three body primary bold resize slice-name">Exponential and Logarithmic Functions</div><div class="aleks-typography two body primary bold resize item-title">Finding the final amount in a word problem on continuous exponential growth</div></div>
<div class="learning-status-container progress-container"><div class="random-walk"><div class="aleks-typography three body primary resize status" id="mk-said" aria-live="polite"></div><div class="rw-row"><div class="tooltiptarget_randomWalk" aria-label="Topic Progress"><div aria-live="polite" id="mk-live" style="position:absolute;width:1px;height:1px;overflow:hidden">Topic Progress 0 of 5</div><div class="rw-track"><div class="rw-seg"><div style="height:100%;width:0%"></div></div><div class="rw-seg"><div style="height:100%;width:0%"></div></div><div class="rw-seg"><div style="height:100%;width:0%"></div></div><div class="rw-seg"><div style="height:100%;width:0%"></div></div><div class="rw-seg"><div style="height:100%;width:0%"></div></div></div></div><div class="body3" id="mk-count">0/5</div></div></div></div></div></div>
<div class="right-container"><div class="legacy-user-menu"><button id="userProfile" data-testid="aleks_user_menu_button" aria-label="Profile Student" type="button">Student &#9662;</button></div></div></header></div>
<button aria-label="Topics" class="tooltiptarget_toggle_carousel" type="button">&#8964;</button>
<div id="current-main-view" role="main"><div style="z-index: -1; position: absolute; inset: 66px 0px 0px; background-color: rgb(255, 255, 255);"></div><div class="mainview_item_page"><div id="itemContainer_pcalc101" class="removeiframe">
<div id="main_algo_body"><div id="item_feedbacks"></div>
<div id="algoPrompt"><p>A population of <b>250</b> bacteria grows according to a <i>continuous exponential growth model</i>, at a relative rate of <b>8%</b> per hour.</p><p>How many bacteria are there after five hours? Do not round any intermediate computations, and round your answer to the nearest whole number.</p></div>
<div id="step_container"><div class="aleks-editor-content"><div id="ansed_answer_1"><div id="ansed_root_ansed_1_1" class="ansed_root"><input id="ansed_input_ansed_1_1" aria-label="Answer Editor" class="ansed_input" type="text" autocomplete="off"><span class="ansed_unit">bacteria</span></div></div></div>
<div class="aleks-editor-menu aleks-editor-menu--pos-top"><div id="menubar_ansed_1_0_ansed"><div style="padding:2.5px;display:inline-block;background-color:#fff;border-radius:7px;border:1px solid #83a0a8;box-shadow:#e0e7e9 -5px 5px 5px"><div id="containerEditButton" role="list" aria-label="Undo or start over menu"><button id="menubar_ansed_1_0_ansed_undo" aria-label="Undo" type="button">&#8630;</button><button id="menubar_ansed_1_0_ansed_reset" aria-label="Start over" type="button">&#10005;</button></div></div></div></div></div>
<div id="explain"><b>Explanation.</b> Continuous growth: <i>y</i> = <i>y</i><sub>0</sub>e<sup><i>kt</i></sup>. Here <i>y</i> = 250e<sup>0.08·5</sup> = 250e<sup>0.4</sup> ≈ 372.96, so about 373 bacteria.</div>
</div></div></div></div>
<div role="complementary" aria-label="Tools" class="hide_print"><table style="font-size:1em"><tbody>
<tr><td><button id="smt_sidenav_button_input_gototutorial" aria-label="Tools Help" class="secondary large iconButton" type="button"><svg viewBox="0 0 24 24"><path d="M11 18h2v-2h-2zm1-16a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 18a8 8 0 1 1 0-16 8 8 0 0 1 0 16zm0-14a4 4 0 0 0-4 4h2a2 2 0 1 1 4 0c0 2-3 1.75-3 5h2c0-2.25 3-2.5 3-5a4 4 0 0 0-4-4z"/></svg></button></td></tr>
<tr><td><button id="smt_sidenav_button_input_togglecalculator" aria-label="Calculator" class="secondary large iconButton" type="button"><svg viewBox="0 0 24 24"><path d="M7 2h10a2 2 0 0 1 2 2v16a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2zm0 2v4h10V4zm0 6v2h2v-2zm4 0v2h2v-2zm4 0v2h2v-2zm-8 4v2h2v-2zm4 0v2h2v-2zm4 0v6h2v-6zm-8 4v2h2v-2zm4 0v2h2v-2z"/></svg></button></td></tr>
<tr><td><button id="smt_sidenav_button_input_gotovideo" aria-label="Videos" class="secondary large iconButton" type="button"><svg viewBox="0 0 24 24"><path d="M3 5h18v14H3zm2 2v10h14V7zm5 2 5 3-5 3z"/></svg></button></td></tr>
<tr><td><button id="smt_sidenav_button_input_gototextbook" aria-label="eBook" class="secondary large iconButton" type="button"><svg viewBox="0 0 24 24"><path d="M6 2h12a2 2 0 0 1 2 2v16a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2zm0 2v16h12V4zm2 2h8v2H8zm0 4h8v2H8z"/></svg></button></td></tr>
<tr><td><button id="smt_sidenav_button_input_gotomessage" aria-label="Message Center" class="secondary large iconButton" type="button"><svg viewBox="0 0 24 24"><path d="M2 5h20v14H2zm2 2v.5l8 5 8-5V7zm0 2.8V17h16V9.8l-8 5z"/></svg></button></td></tr>
</tbody></table></div>
<div role="navigation" aria-label="Actions" class="tooltiptarget_bottomNavbar hide_print"><div style="justify-self:start;align-items:center"><button id="smt_bottomnav_button_input_requestExplain" class="medium requestExplain standard" type="button">Explanation</button><button id="smt_bottomnav_button_input_checkAnswer" class="medium checkAnswer primary" type="button">Check</button><button id="smt_bottomnav_button_input_nextItem" class="medium nextItem primary" type="button" style="display:none">Next</button></div><div style="justify-self:center;align-items:center"></div><div style="justify-self:end;display:flex;flex-wrap:wrap;align-items:center;justify-content:flex-end"></div></div>
<div class="tooltiptarget_legalFooter aleks-legal-footer aleks-footer-element react-component sm-legal">© 2026 McGraw Hill. <a href="#">Terms of Use</a> <a href="#">Privacy Center</a> <a href="#">Accessibility</a></div>
<div id="mk-calc" class="dialog--mk1 full_page_print" role="dialog" aria-label="Calculator" style="display:none"><div class="dlg-head">Calculator<button type="button" id="mk-calc-close" aria-label="Close" style="border:0;background:#006a7c;color:#fff;border-radius:50%;width:30px;height:30px">&#10005;</button></div><div class="dlg-screen" id="mk-screen">0</div><div class="dlg-keys"><span>7</span><span>8</span><span>9</span><span>÷</span><span>4</span><span>5</span><span>6</span><span>×</span><span>1</span><span>2</span><span>3</span><span>−</span><span>0</span><span>.</span><span>=</span><span>+</span></div></div>
</div></div><script>
(function(){
 var done=0,tries=0;
 var input=document.getElementById('ansed_input_ansed_1_1'),fb=document.getElementById('item_feedbacks'),check=document.getElementById('smt_bottomnav_button_input_checkAnswer'),next=document.getElementById('smt_bottomnav_button_input_nextItem');
 function said(right){
  fb.innerHTML=right?'<div id="result_feedback_alert" class="right"><div class="fb-icon">&#10003;</div><b>Correct</b><span>Your answer is correct.</span></div>':'<div id="result_feedback_alert"><div class="fb-icon">!</div><b>Try Again</b><span>Your answer is incorrect.</span></div>';
  document.getElementById('mk-said').textContent=right?'':'Try again...';
  if(right){done=1;document.querySelector('.rw-seg>div').style.width='100%';document.getElementById('mk-count').textContent='1/5';document.getElementById('mk-live').textContent='Topic Progress 1 of 5';check.style.display='none';next.style.display='';input.disabled=true}
  else{check.textContent='Recheck'}
 }
 check.onclick=function(){tries++;said(Math.round(parseFloat(input.value))===373)};
 next.onclick=function(){location.reload()};
 document.getElementById('smt_bottomnav_button_input_requestExplain').onclick=function(){document.getElementById('explain').classList.toggle('on')};
 document.getElementById('menubar_ansed_1_0_ansed_reset').onclick=function(){input.value='';input.focus()};
 document.getElementById('menubar_ansed_1_0_ansed_undo').onclick=function(){input.value=input.value.slice(0,-1);input.focus()};
 var calc=document.getElementById('mk-calc'),cb=document.getElementById('smt_sidenav_button_input_togglecalculator');
 function calcOn(on){calc.style.display=on?'':'none';cb.className=(on?'primary':'secondary')+' large iconButton'}
 cb.onclick=function(){calcOn(calc.style.display==='none')};
 document.getElementById('mk-calc-close').onclick=function(){calcOn(false)};
 if(/[?&]state=wrong/.test(location.search)){input.value='37.3';said(false)}
})();
</script></body></html>`;
