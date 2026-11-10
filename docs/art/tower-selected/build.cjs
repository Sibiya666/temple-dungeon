const fs = require('node:fs');
const path = require('node:path');
const { acts, money } = require('../tower-seven/kit.cjs');
const assetDir = path.resolve(__dirname, '../tower-seven/assets');
function uri(file) {
  const mime = file.endsWith('.svg') ? 'image/svg+xml' : 'image/png';
  return 'data:' + mime + ';base64,' + fs.readFileSync(path.join(assetDir, file)).toString('base64');
}
const activityDescriptions = {
  timed_run: 'Завершите встречу до истечения времени.', ritual: 'Проведите ритуал и защитите его от монстров.',
  clear: 'Уничтожьте монстров на этаже.', waves: 'Отбейте все волны монстров, прорывающихся из разлома.',
  bosses: 'Сразитесь с боссами этажа.', escort: 'Сопроводите цель через этаж.',
  hold: 'Удерживайте позицию под натиском монстров.', levers: 'Активируйте переключатели на этаже.'
};
const data = acts.map(([id, name]) => ({ id, name, description: activityDescriptions[id], image: uri('activities-' + id + '.png') }));
const currencies = money.map(([id, name]) => ({ id, name, image: uri('currencies-' + id + '.svg') }));
const focus = uri('cold-focus.svg');
const architecture = uri('architecture.png');
const css = String.raw`
:root{color-scheme:dark;--ink:#dedccf;--muted:#a7b9bd;--quiet:#879fa7;--line:#354a54;--panel:#111d23;--accent:#b7cdd4}*{box-sizing:border-box}html{min-width:0;background:#0c1114}body{margin:0;font:14px/1.4 'Segoe UI',Arial,sans-serif;color:var(--ink)}button{font:inherit;color:inherit;cursor:pointer}button:disabled{cursor:default;color:#8a9ba0;border-color:#35434a;background:#142027}button:focus-visible{outline:2px solid #e8e4bf;outline-offset:4px}h1,h2,h3,p{margin:0}h2,.eyebrow{text-transform:uppercase;font-size:12px;font-weight:500;letter-spacing:.09em;color:var(--muted)}h3{font-size:23px;font-weight:500;line-height:1.2}img{object-fit:contain}button{touch-action:manipulation}.prototype{padding:9px 24px;display:flex;justify-content:space-between;gap:18px;align-items:center;font-size:12px;color:#b6c7cb;background:#0b1013;border-bottom:1px solid #30414a}.prototype strong{font-weight:500;color:#e5e2d6}.canvas{margin:8px auto;width:calc(100% - 32px);max-width:1536px;min-height:640px;padding:15px 23px 10px;border:1px solid #35474e;background:linear-gradient(90deg,#0c1114f5,#0c1114d5 55%,#0c1114ef),url('${architecture}') center 38%/cover}.top{display:flex;align-items:center;gap:24px;padding-bottom:11px;border-bottom:1px solid #30414a}.brand{font:20px Georgia,serif;letter-spacing:.1em}.top h1{font-size:15px;font-weight:400;margin-right:auto;color:#bdc9c9}.rank{font-size:14px}.stage{display:grid;grid-template-columns:176px minmax(0,1fr) 260px;gap:24px;padding-top:14px}.label-row{display:flex;justify-content:space-between;align-items:center;gap:8px;margin-bottom:13px}.micro,.quiet{font-size:12px;color:var(--muted)}.floor-list{display:grid;gap:8px}.floor{position:relative;display:grid;grid-template-columns:26px 30px minmax(0,1fr);gap:6px;align-items:center;width:100%;min-height:82px;text-align:left;padding:10px 8px;border:0;border-bottom:1px solid var(--line);background:#101b20c4}.floor[aria-current=true]{background:#23343de8;box-shadow:inset 2px 0 #b7cdd4}.floor:hover{background-color:#21323b}.floor-number{font:21px Georgia,serif;color:#a0bac4}.floor-copy{min-width:0;display:flex;flex-direction:column;gap:3px}.floor-copy strong{font-size:13px;font-weight:500}.floor-copy small{font-size:12px;color:#a5b8bd;line-height:1.2}.floor-meta{grid-column:2/-1;font-size:12px;color:#9cb0b7;display:flex;justify-content:space-between}.work{min-width:0;display:grid;grid-template-rows:minmax(310px,1fr) auto auto;gap:12px}.editor{min-width:0;text-align:center;padding:13px 16px 14px;background:linear-gradient(180deg,#15232b8c,#0d171bc9);border-top:1px solid #3b5059;border-bottom:1px solid #3b5059}.editor h2{font:32px Georgia,serif;color:var(--ink);letter-spacing:0;text-transform:none;margin:3px 0}.editor-identity{display:flex;align-items:center;justify-content:center;gap:15px;margin:12px auto;min-height:76px}.editor-identity h3{min-width:0;overflow-wrap:anywhere;font-size:18px;max-width:260px;text-align:left}.editor-meta{display:flex;justify-content:center;gap:22px;font-size:12px;color:#afc0c5}.slots{font-size:15px;color:#728d99;letter-spacing:10px;margin-top:7px}.action{max-width:410px;margin:15px auto 0;display:grid;gap:7px;text-align:left}.source-target{font-size:12px;color:#c6d4d5;line-height:1.5}.primary{display:flex;justify-content:space-between;gap:8px;align-items:center;min-height:42px;padding:9px 13px;border:1px solid #91aeb8;background:#2b4552;color:#efeee3;font-size:14px}.primary:not(:disabled):hover{background:#385766}.secondary,.reset{padding:7px 10px;background:transparent;border:1px solid #3b5059;font-size:12px}.secondary:not(:disabled):hover,.reset:hover{background:#21343e}.reason{min-height:17px;font-size:12px;color:#aebec2}.inventory-grid{display:grid;grid-template-columns:repeat(8,minmax(0,1fr));gap:8px}.activity{position:relative;padding:6px 2px 4px;background:transparent;border:1px solid transparent;display:flex;flex-direction:column;align-items:center;gap:9px;min-width:0}.activity:hover{background:#1b2b33}.activity[aria-pressed=true]{background:#1c303a77}.activity-name{font-size:12px;line-height:1.2;text-align:center;min-height:29px;color:#b6c7ca;overflow-wrap:normal}.activity[aria-pressed=true] .activity-name{color:#f0edde}.socket-wrap{position:relative;display:inline-block;width:var(--size,48px);height:var(--size,48px);flex:none}.socket{position:absolute;inset:0;display:block;background:#41555e;clip-path:polygon(18% 0,82% 0,100% 18%,100% 82%,82% 100%,18% 100%,0 82%,0 18%)}.glass{position:absolute;inset:1px;background:linear-gradient(145deg,#1b2b34,#0b1216);clip-path:inherit}.socket img{position:absolute;inset:12%;width:76%;height:76%}.activity[aria-pressed=true] .socket{background:#b8d0d6}.activity:focus-visible,.currency:focus-visible{outline:0}.activity:focus-visible:after,.currency:focus-visible:after{content:'';position:absolute;inset:-3px;background:url('${focus}') center/100% 100%;pointer-events:none}.count{position:absolute;right:1px;bottom:0;min-width:18px;padding:0 3px;font-size:12px;color:#edebdf;background:#0b1317;border:1px solid #41515a;line-height:16px;font-weight:400}.editor .socket-wrap{--size:76px}.floor .socket-wrap{--size:28px}.empty-symbol{position:absolute;inset:0;display:grid;place-items:center;font-size:20px;color:#536e7a}.commands{display:flex;align-items:center;gap:12px;justify-content:space-between;min-height:32px}.commands p{font-size:12px;color:#9db3bb}.sidebar{border-left:1px solid #30434c;padding-left:20px;min-width:0}.identity{display:flex;gap:13px;align-items:center;margin:12px 0}.identity .socket-wrap{--size:64px}.identity .micro{margin-top:8px}.description{font-size:14px;line-height:1.5;color:#b5c7cc;min-height:42px}.property-line{display:flex;justify-content:space-between;margin-top:17px;padding-top:11px;border-top:1px solid #30414a;font-size:12px;color:#b3c4c8}.property-line b{font-weight:400;color:var(--ink)}.detail>.quiet{margin-top:7px}.currencies{margin-top:18px;border-top:1px solid #30414a;padding-top:16px}.currency-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:5px}.currency{position:relative;display:flex;gap:4px;align-items:center;min-height:43px;min-width:0;padding:7px;background:#0d171ccc;border:1px solid #30414a}.currency img{width:25px;height:25px}.currency span{font-size:12px;margin-left:auto;color:#c5cecb}.currency:hover{background:#23343e}.currency[aria-pressed=true]{border-color:#bacdd2;background:#21343e}.currency-description{font-size:12px;color:#adbec4;min-height:34px;margin-top:9px}.bottom{display:flex;justify-content:space-between;gap:16px;border-top:1px solid #293e48;margin-top:12px;padding-top:8px;font-size:12px;color:#a5bac1}.result{min-height:34px;font-size:12px;color:#c4d3d7;line-height:1.4}.route,.inventory,.detail{min-width:0}@media(min-width:1450px) and (min-height:800px){.canvas{margin-top:24px;padding:25px 30px 18px;min-height:755px}.stage{grid-template-columns:196px minmax(0,1fr) 290px;gap:30px;padding-top:24px}.floor{min-height:111px}.work{grid-template-rows:minmax(420px,1fr) auto auto;gap:22px}.editor{padding-top:24px}.editor-identity{margin:25px auto;min-height:90px}.editor .socket-wrap{--size:90px}.action{margin-top:20px}.inventory-grid{gap:12px}.activity .socket-wrap{--size:56px}.currencies{margin-top:42px}.currency{min-height:51px}.bottom{margin-top:23px}}@media(max-width:1100px){.stage{grid-template-columns:155px minmax(0,1fr) 228px;gap:16px}.canvas{padding:16px}.sidebar{padding-left:14px}.inventory-grid{gap:3px}.activity .socket-wrap{--size:40px}.activity-name{font-size:12px}.floor{padding-left:6px;padding-right:6px;grid-template-columns:22px 25px minmax(0,1fr);gap:4px}.floor .socket-wrap{--size:24px}.editor{padding-left:10px;padding-right:10px}.identity{gap:8px}.identity h3{font-size:21px}}@media(max-width:800px){.prototype{flex-direction:column;align-items:flex-start;gap:4px;padding:10px 16px}.canvas{width:calc(100% - 20px);padding:16px 12px}.top{gap:12px;flex-wrap:wrap}.top h1{font-size:14px}.rank{margin-left:auto}.stage{grid-template-columns:1fr;gap:22px}.floor-list{grid-template-columns:repeat(5,minmax(0,1fr));gap:5px}.floor{grid-template-columns:1fr;justify-items:center;text-align:center;min-height:140px;padding:8px 3px;gap:5px}.floor-copy strong{font-size:12px}.floor-copy small{font-size:12px;overflow-wrap:anywhere}.floor-meta{grid-column:1;font-size:12px;flex-direction:column;gap:3px}.floor-number{font-size:20px}.floor .socket-wrap{--size:28px}.work{grid-template-rows:auto auto auto}.editor{padding:16px 12px}.editor h2{font-size:30px}.editor-identity{margin:16px 0}.inventory-grid{grid-template-columns:repeat(4,minmax(0,1fr));gap:10px 6px}.activity .socket-wrap{--size:48px}.sidebar{border-left:0;border-top:1px solid #30434c;padding:18px 0 0;display:grid;grid-template-columns:1fr 1fr;gap:20px}.currencies{margin-top:0;padding-top:0;border-top:0}.description{min-height:0}.commands{flex-wrap:wrap}.bottom{flex-direction:column;gap:5px}.source-target{overflow-wrap:anywhere}.identity{flex-wrap:wrap}.editor-meta{gap:12px}}@media(max-width:480px){.sidebar{grid-template-columns:1fr}.identity{flex-wrap:nowrap}.currencies{padding-top:16px;border-top:1px solid #30414a}.currency-grid{grid-template-columns:repeat(4,minmax(0,1fr))}.floor-list{gap:3px}.floor-copy small{font-size:12px}.floor-meta{font-size:12px}.editor-identity h3{font-size:17px}.editor .socket-wrap{--size:64px}.brand{font-size:18px}.top h1{order:3;width:100%}.currency-description{min-height:34px}}@media(prefers-reduced-motion:reduce){*,*:before,*:after{animation:none!important;transition:none!important;scroll-behavior:auto!important}}
`;
const script = String.raw`
const activities = DATA;
const currencies = CURRENCIES;
const byId = Object.fromEntries(activities.map(a => [a.id,a]));
let floors, counts, selectedFloor, selectedActivity, selectedCurrency;
const $ = s => document.querySelector(s);
function socket(id, size, count) { return '<span class="socket-wrap" style="--size:'+size+'px"><span class="socket"><span class="glass"></span>'+(id?'<img alt="" src="'+byId[id].image+'">':'<span class="empty-symbol" aria-hidden="true">◇</span>')+'</span>'+(count===undefined?'':'<b class="count" data-count="'+id+'">'+count+'</b>')+'</span>'; }
function announce(message){$('#result').textContent=message;}
function render(){
 const installed=floors[selectedFloor-1], chosen=byId[selectedActivity], complete=floors.filter(Boolean).length;
 document.querySelectorAll('[data-floor]').forEach(btn=>{
  const n=Number(btn.dataset.floor),id=floors[n-1];
  btn.dataset.installed=id||'';btn.setAttribute('aria-current',String(n===selectedFloor));
  btn.setAttribute('aria-label','Этаж '+n+', '+(id?byId[id].name:'пустой')+', Склеп, свойства 0 из 5');
  btn.innerHTML='<span class="floor-number">'+String(n).padStart(2,'0')+'</span>'+socket(id,28)+'<span class="floor-copy"><strong>Этаж '+n+'</strong><small>'+(id?byId[id].name:'Пустой этаж')+'</small></span><span class="floor-meta"><span>Склеп</span><span>0 / 5</span></span>';
 });
 document.querySelectorAll('[data-activity]').forEach(btn=>{
  const a=byId[btn.dataset.activity];btn.setAttribute('aria-pressed',String(a.id===selectedActivity));
  btn.setAttribute('aria-label',a.name+', в каталоге '+counts[a.id]+' штук');
  btn.innerHTML=socket(a.id,48,counts[a.id])+'<span class="activity-name">'+a.name+'</span>';
 });
 $('#floor-title').textContent='Этаж '+selectedFloor;
 $('#floor-identity').innerHTML=socket(installed,76)+'<h3>'+(installed?byId[installed].name:'Активность не установлена')+'</h3>';
 $('#selected-portrait').innerHTML=socket(selectedActivity,64);
 $('#activity-title').textContent=chosen.name;
 $('#activity-meta').textContent='Ранг 8 · '+counts[selectedActivity]+' шт. в каталоге';
 $('#activity-description').textContent=chosen.description;
 $('#source-target').textContent='Источник: '+chosen.name+' · '+counts[selectedActivity]+' шт. → Этаж '+selectedFloor+' · '+(installed?byId[installed].name:'пустой');
 const same=installed===selectedActivity, unavailable=counts[selectedActivity]===0;
 const install=$('[data-action="install"]');install.disabled=same||unavailable;
 install.innerHTML=(installed?'Заменить на этаже ':'Установить на этаж ')+selectedFloor+'<span aria-hidden="true">↑</span>';
 $('#reason').textContent=same?'Эта активность уже установлена на выбранном этаже.':unavailable?'В каталоге не осталось экземпляров этой активности.':installed?'Предыдущая активность вернётся в каталог.':'Один экземпляр из каталога займёт выбранный этаж.';
 $('[data-action="remove"]').disabled=!installed;
 $('[data-action="remove"]').textContent=installed?'Снять с этажа '+selectedFloor:'Снять с этажа · этаж пуст';
 $('#route-count').textContent=complete+' / 5';
 $('#summary').textContent='Ранг 8 · Маршрут '+complete+' / 5 · Биом: Склеп';
 document.querySelectorAll('[data-currency]').forEach(btn=>btn.setAttribute('aria-pressed',String(btn.dataset.currency===selectedCurrency)));
 $('#currency-description').textContent=selectedCurrency?currencies.find(c=>c.id===selectedCurrency).name+' · 20 шт.':'Выберите расходник, чтобы увидеть название.';
}
function reset(){floors=Array(5).fill(null);counts=Object.fromEntries(activities.map(a=>[a.id,5]));selectedFloor=2;selectedActivity='waves';selectedCurrency=null;render();}
$('#floors').innerHTML=Array.from({length:5},(_,i)=>'<button class="floor" type="button" data-floor="'+(i+1)+'"></button>').join('');
$('#activities').innerHTML=activities.map(a=>'<button class="activity" type="button" data-activity="'+a.id+'"></button>').join('');
$('#currencies').innerHTML=currencies.map(c=>'<button class="currency" type="button" data-currency="'+c.id+'" title="'+c.name+'" aria-label="'+c.name+', 20 штук"><img alt="" src="'+c.image+'"><span>20</span></button>').join('');
document.addEventListener('click',e=>{
 const floor=e.target.closest('[data-floor]'),activity=e.target.closest('[data-activity]'),currency=e.target.closest('[data-currency]'),action=e.target.closest('[data-action]');
 if(floor){selectedFloor=Number(floor.dataset.floor);render();}
 if(activity){selectedActivity=activity.dataset.activity;render();}
 if(currency){selectedCurrency=currency.dataset.currency;render();}
 if(!action||action.disabled)return;
 if(action.dataset.action==='reset'){reset();announce('Демосостояние сброшено. Все этажи пусты, каждой активности по 5.');return;}
 const index=selectedFloor-1,old=floors[index];
 if(action.dataset.action==='install'&&counts[selectedActivity]>0&&old!==selectedActivity){
  if(old)counts[old]++;counts[selectedActivity]--;floors[index]=selectedActivity;
  render();announce('Активность «'+byId[selectedActivity].name+'» установлена на этаж '+selectedFloor+'.'+(old?' Активность «'+byId[old].name+'» возвращена в каталог.':''));
 }
 if(action.dataset.action==='remove'&&old){counts[old]++;floors[index]=null;render();announce('Снята активность «'+byId[old].name+'» с этажа '+selectedFloor+' и возвращена в каталог.');}
});
function arrowGroup(selector, e, columns){
 const item=e.target.closest(selector);if(!item)return;
 const items=[...document.querySelectorAll(selector)],index=items.indexOf(item);
 let next=index;
 if(e.key==='ArrowRight')next=(index+1)%items.length;
 else if(e.key==='ArrowLeft')next=(index-1+items.length)%items.length;
 else if(e.key==='ArrowDown')next=(index+columns)%items.length;
 else if(e.key==='ArrowUp')next=(index-columns+items.length)%items.length;
 else if(e.key==='Home')next=0;
 else if(e.key==='End')next=items.length-1;
 else return;
 e.preventDefault();items[next].focus();items[next].click();
}
document.addEventListener('keydown',e=>{arrowGroup('[data-floor]',e,1);arrowGroup('[data-activity]',e,matchMedia('(max-width:800px)').matches?4:8);arrowGroup('[data-currency]',e,matchMedia('(max-width:480px)').matches?4:3);});
reset();
`;
const html = `<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Башня — Монтажный пролёт · HTML-прототип</title><style>${css}</style></head><body>
<div class="prototype"><strong>HTML-прототип · не меняет игру</strong><span>Применение расходников пока недоступно. Состояние сбрасывается при перезагрузке.</span></div>
<div class="canvas"><header class="top"><span class="brand">DIALOID</span><h1>Башня / Сборка маршрута</h1><span class="rank">Ранг 8</span></header>
<main class="stage"><section class="route" aria-labelledby="route-title"><div class="label-row"><h2 id="route-title">Маршрут</h2><span class="micro" id="route-count">0 / 5</span></div><div class="floor-list" id="floors" aria-label="Выбор этажа"></div></section>
<div class="work"><section class="editor" aria-labelledby="floor-title"><span class="eyebrow">Выбранный этаж · цель установки</span><h2 id="floor-title">Этаж 2</h2><div class="editor-identity" id="floor-identity"></div><div class="editor-meta"><span>Биом · Склеп</span><span>Свойства этажа · 0 / 5</span></div><p class="slots" aria-hidden="true">◇ ◇ ◇ ◇ ◇</p><div class="action"><p class="source-target" id="source-target"></p><button class="primary" type="button" data-action="install" aria-describedby="source-target reason"></button><p class="reason" id="reason"></p><button class="secondary" type="button" data-action="remove"></button><p id="result" class="result" role="status" aria-live="polite" aria-atomic="true">Выберите активность и установите её на выбранный этаж.</p></div></section>
<section class="inventory" aria-labelledby="inventory-title"><div class="label-row"><h2 id="inventory-title">Каталог активностей</h2><span class="micro">Ранг 8 · 8 видов</span></div><div class="inventory-grid" id="activities" aria-label="Выбор активности"></div></section><div class="commands"><p>Выберите этаж → активность → установите</p><button class="reset" type="button" data-action="reset">Сбросить демо</button></div></div>
<aside class="sidebar"><section class="detail" aria-labelledby="activity-heading"><div class="label-row"><h2 id="activity-heading">Выбрано в каталоге</h2></div><div class="identity"><span id="selected-portrait"></span><div><h3 id="activity-title"></h3><p class="micro" id="activity-meta"></p></div></div><p class="description" id="activity-description"></p><div class="property-line"><span>Свойства активности</span><b>0 / 5</b></div><p class="quiet">Свойств пока нет</p></section><section class="currencies" aria-labelledby="currency-title"><div class="label-row"><h2 id="currency-title">Расходники</h2><span class="micro">12 видов</span></div><div class="currency-grid" id="currencies" aria-label="Описание расходников"></div><p class="currency-description" id="currency-description"></p></section></aside></main>
<footer class="bottom"><p>Стрелки · Home / End — выбор</p><p id="summary"></p></footer></div><script>${script.replace('DATA',JSON.stringify(data)).replace('CURRENCIES',JSON.stringify(currencies))}</script></body></html>`;
fs.writeFileSync(path.join(__dirname,'index.html'),html);
console.log('Built standalone index.html (' + Buffer.byteLength(html) + ' bytes)');


