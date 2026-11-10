const fs=require('node:fs'),path=require('node:path'),{pathToFileURL}=require('node:url');
const {chromium}=require('C:/Users/sibiya666/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const ids=['timed_run','ritual','clear','waves','bosses','escort','hold','levers'];
async function main(){
  const out=path.join(__dirname,'qa');fs.mkdirSync(out,{recursive:true});
  const report={checks:[],errors:[],external:[]};
  const check=(ok,name)=>{report.checks.push({name,passed:!!ok});if(!ok)throw new Error(name)};
  const browser=await chromium.launch({executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',headless:true,timeout:25000});
  try{
    const page=await browser.newPage({viewport:{width:1280,height:720},reducedMotion:'reduce'});
    page.on('pageerror',e=>report.errors.push(e.message));
    page.on('console',m=>{if(m.type()==='error')report.errors.push(m.text())});
    await page.route(/^https?:/,r=>{report.external.push(r.request().url());return r.abort()});
    await page.goto(pathToFileURL(path.join(__dirname,'index.html')).href);
    await page.evaluate(()=>document.fonts.ready);
    const floor=n=>page.locator(`[data-floor="${n}"]`);
    const activity=id=>page.locator(`[data-activity="${id}"]`);
    const install=page.locator('[data-action="install"]'),remove=page.locator('[data-action="remove"]'),reset=page.locator('[data-action="reset"]');
    async function tabTo(action){for(let i=0;i<60;i++){if(await page.evaluate(value=>document.activeElement?.getAttribute('data-action')===value,action))return;await page.keyboard.press('Tab')}throw new Error('Keyboard Tab cannot reach '+action)}
    const snapshot=()=>page.evaluate(()=>({counts:Object.fromEntries([...document.querySelectorAll('[data-count]')].map(e=>[e.dataset.count,parseInt(e.textContent,10)])),floors:[...document.querySelectorAll('[data-floor]')].map(e=>e.dataset.installed||'')}));
    async function conserved(name){const s=await snapshot();check(ids.every(id=>s.counts[id]+s.floors.filter(f=>f===id).length===5),name);return s}
    check(await page.locator('[data-floor]').count()===5,'All five floor tabs exist');
    check(await page.locator('[data-activity]').count()===8,'All eight activities exist');
    check(await page.locator('[data-currency]').count()===12,'All twelve currencies exist');
    let s=await conserved('Initial totals are five per type');
    check(s.floors.every(f=>!f)&&ids.every(id=>s.counts[id]===5),'Initial route is empty');
    check(await remove.isDisabled(),'Remove is disabled for an empty floor');
    check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'No desktop horizontal overflow');
    const outside=await page.locator('[data-floor],[data-activity],[data-currency],[data-action="install"]').evaluateAll(elements=>elements.filter(e=>{const r=e.getBoundingClientRect();return r.width<1||r.height<1||r.left<0||r.top<0||r.right>innerWidth+1||r.bottom>innerHeight+1}).map(e=>e.textContent.trim()));
    check(outside.length===0,'All primary objects visible at 1280x720');
    await page.screenshot({path:path.join(out,'initial-720.png')});
    const beforeHover=JSON.stringify(await snapshot());await activity('clear').hover();
    check(JSON.stringify(await snapshot())===beforeHover,'Hover does not change inventory or floors');
    await install.click();s=await conserved('Install conserves counts');
    check(s.counts.waves===4&&s.floors[1]==='waves','Install targets floor two once');
    check(await page.locator('#result').evaluate(e=>{const r=e.getBoundingClientRect();return r.width>0&&r.height>0&&r.top>=0&&r.bottom<=innerHeight+1}),'Action result is visible at 720p');
    check(await install.isDisabled(),'Identical installation cannot spend twice');
    await activity('clear').click();await install.click();s=await conserved('Replace conserves both types');
    check(s.counts.waves===5&&s.counts.clear===4&&s.floors[1]==='clear','Replace returns previous activity once');
    await remove.click();s=await conserved('Remove conserves counts');
    check(!s.floors[1]&&s.counts.clear===5,'Remove returns to inventory');
    await floor(5).click();await activity('hold').click();await install.click();
    await floor(2).click();await install.click();s=await conserved('Two floors share a type without duplication');
    check(s.floors[4]==='hold'&&s.floors[1]==='hold'&&s.counts.hold===3,'Changing target preserves other floor');
    await page.screenshot({path:path.join(out,'installed-720.png')});
    await reset.click();s=await snapshot();check(s.floors.every(f=>!f)&&ids.every(id=>s.counts[id]===5),'Reset restores original data');
    for(let n=1;n<=5;n++){await floor(n).click();await install.click()}
    s=await conserved('Exhausted stack still conserves counts');check(s.counts.waves===0&&s.floors.every(f=>f==='waves'),'Five placements exhaust exactly five items');
    await reset.click();await floor(2).focus();await page.keyboard.press('ArrowDown');
    check(await page.evaluate(()=>document.activeElement?.getAttribute('data-floor')==='3'),'ArrowDown moves floor keyboard focus');
    await page.keyboard.press('Enter');await activity('ritual').focus();await page.keyboard.press('Enter');await tabTo('install');await page.keyboard.press('Enter');
    s=await conserved('Keyboard choice conserves counts');check(s.floors[2]==='ritual','Keyboard can select activity and floor');
    await tabTo('remove');await page.keyboard.press('Space');s=await conserved('Keyboard removal conserves counts');check(!s.floors[2]&&s.counts.ritual===5,'Tab and Space can remove activity');
    await reset.click();await page.setViewportSize({width:1600,height:900});
    await page.screenshot({path:path.join(out,'initial-900.png')});
    await page.setViewportSize({width:390,height:844});
    check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'No mobile horizontal overflow');
    check(await page.evaluate(()=>{const h=document.querySelector('.editor h3'),editor=document.querySelector('.editor');return h.getBoundingClientRect().right<=editor.getBoundingClientRect().right+1}),'Mobile editor title stays inside its panel');
    await page.screenshot({path:path.join(out,'mobile.png'),fullPage:true});
    check((await page.locator('img').evaluateAll(imgs=>imgs.filter(i=>i.getAttribute('src')&&(!i.complete||!i.naturalWidth)).length))===0,'All images load');
    check(report.errors.length===0,'No browser errors');check(report.external.length===0,'No external requests');
    console.log(JSON.stringify(report,null,2));
  }finally{fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));await browser.close()}
}
main().catch(e=>{console.error(e);process.exitCode=1});
