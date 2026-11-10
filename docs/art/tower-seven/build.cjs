const fs=require('node:fs'),path=require('node:path');
const kit=require('./kit.cjs');
kit.copyAssets();
const partial=process.argv.includes('--partial');
const candidates=[];
const baseline='baseline.png';
fs.copyFileSync(path.join(kit.root,'docs/art/ui-styleguide/2026-11-10-tower-reference/integrated-after/reference-720-selected-focus.png'),path.join(__dirname,baseline));
candidates.push({id:'baseline',name:'Текущий экран · runtime',kind:'image',source:baseline,baseline:true,concept:'Текущая проверенная реализация — ориентир, не один из семи новых вариантов.'});
for(let i=1;i<=7;i++){
  const id='v'+String(i).padStart(2,'0'),file=path.join(__dirname,id+'.cjs');
  if(!fs.existsSync(file)){if(partial)continue;throw new Error('Missing variant '+id);}
  delete require.cache[require.resolve(file)];
  const v=require(file);
  if(v.id!==id||typeof v.body!=='function'||!v.css||!v.name)throw new Error('Invalid variant module '+id);
  fs.writeFileSync(path.join(__dirname,id+'.html'),kit.html(v));
  candidates.push({id,name:v.name,source:id+'.html',concept:v.concept,typography:'Segoe UI · общая иерархия Dialoid',palette:['#0C1114','#162126','#D8D5C6','#91B3BE'],traits:[...(v.traits||[]),'Сильная сторона: '+v.pros,'Компромисс: '+v.compromise]});
}
fs.writeFileSync(path.join(__dirname,'manifest.json'),JSON.stringify({schemaVersion:1,lang:'en',round:'Dialoid Tower / 7 layouts / 2026-11-10',nativeViewport:'1280x720',candidates},null,2));
console.log('Static candidates built: '+(candidates.length-1)+' + runtime baseline');
