import React,{useEffect,useState} from 'react';
import {Shield,RefreshCw,Link2,Unlink,Sparkles,Check,ArrowUpRight,Clock} from 'lucide-react';
import {send} from './bridge';
const count=v=>v==null?'—':v;
function remaining(n){if(n==null)return '정보 없음';const min=Math.ceil(Math.max(0,n)/60);return min===0?'완료':`${Math.floor(min/60)}시간 ${min%60}분`;}
function Meter({value,max,label}){return <div className="progress" role="progressbar" aria-label={label} aria-valuenow={value??0} aria-valuemax={max||100}><i style={{width:Math.max(0,Math.min(100,(value||0)/(max||100)*100))+'%'}}/></div>}
const names={disconnected:'연결되지 않음',restoring:'저장된 연결 확인 중',connected:'연결됨',expired:'다시 로그인 필요',selectRole:'캐릭터 선택',noCharacter:'캐릭터 없음',rateLimit:'요청 제한',network:'연결 오류',private:'노트 설정 확인',verification:'추가 인증 필요',account:'계정 확인 필요',service:'서비스 응답 오류',format:'응답 확인 필요',storage:'저장소 오류'};
export function GenshinAccount({account}){
 const a=account||{state:'disconnected',roles:[]},n=a.notes;
 const [now,setNow]=useState(Date.now());
 useEffect(()=>{const timer=setInterval(()=>setNow(Date.now()),30000);return()=>clearInterval(timer);},[]);
 const elapsed=n?Math.max(0,(now-Date.parse(n.updatedAt))/1000):0;
 const recovery=n?.recoverySeconds==null?null:Math.max(0,n.recoverySeconds-elapsed);
 const complete=n?.expeditions?.filter(e=>e.status==='Finished').length??0;
 const ongoing=n?.expeditions?.filter(e=>e.status==='Ongoing').length??0;
 const ready=a.connected&&!a.busy,selected=a.selected?`${a.selected.uid}/${a.selected.server}`:'';
 return <>
  <section className="card genshin-account" aria-label="원신 계정 연결">
   <div className="section-heading"><div><span className="eyebrow">HOYOLAB / ACCOUNT</span><h2>원신 계정</h2></div><span className={'badge '+(a.state==='connected'?'positive':'')}>{a.busy?'조회 중…':names[a.state]||'연결 확인'}</span></div>
   {a.selected&&<div className="account-identity"><strong>{a.selected.nickname||'여행자'}</strong><span>UID {a.selected.uid} · {a.selected.serverName}</span></div>}
   <p role="status">{a.message||'공식 HoYoLAB 화면에서 로그인하면 UID와 서버를 자동으로 확인해요.'}</p>
   {a.roles?.length>1&&<label className="role-picker">원신 캐릭터 선택<select aria-label="원신 캐릭터 선택" value={selected} disabled={a.busy} onChange={e=>{const [uid,server]=e.target.value.split('/');if(uid&&server)send('genshin.select',{uid,server});}}><option value="">연결할 캐릭터를 선택해 주세요</option>{a.roles.map(r=><option key={r.uid+'/'+r.server} value={r.uid+'/'+r.server}>{r.nickname||'여행자'} · {r.serverName} · UID {r.uid}</option>)}</select></label>}
   <div className="account-actions">
    {(!a.connected||a.state==='verification')&&<button className="button" disabled={a.busy} onClick={()=>send('genshin.connect')}><Link2 size={16}/>{a.state==='disconnected'?'HoYoLAB 계정 연결':'다시 연결'}</button>}
    {a.connected&&<button className="button secondary" disabled={!ready} onClick={()=>send('genshin.refresh')}><RefreshCw size={16}/>새로고침</button>}
    {a.state!=='disconnected'&&<button className="button secondary" onClick={()=>send('genshin.disconnect')}><Unlink size={16}/>연결 해제</button>}
   </div>
   <p className="small muted"><Shield size={13}/> 비공식 읽기 전용 연동 · 비밀번호는 수집하지 않아요. 인증 세션은 이 Windows 계정 전용으로 암호화 저장해요.</p>
  </section>
  {n?<>
   <div className="section-heading"><div><span className="eyebrow">TRAVELER’S NOTES / {a.stale?'LAST KNOWN':'LIVE'}</span><h2>모험 전, 잠깐 확인해요</h2></div><span className={'badge '+(a.stale?'':'positive')}>{a.stale?'이전 데이터 · 갱신 실패':'실제 HoYoLAB 데이터'}</span></div>
   <p className="notes-updated small muted"><Clock size={13}/>마지막 갱신 {new Date(n.updatedAt).toLocaleString('ko-KR')} · 레진 수치는 조회 시점의 실제 값이에요.</p>
   <div className="notes-grid">
    <section className="card resin"><div className="between"><h3>퓨어 레진</h3><Sparkles/></div><div className="value">{count(n.resin)} <span>/ {count(n.resinMax)}</span></div><Meter value={n.resin} max={n.resinMax} label="레진"/><p>완충까지 <strong>{remaining(recovery)}</strong></p><span className="badge">남은 시간은 조회 후 로컬 예상 · 소비 시 다음 갱신에 반영</span></section>
    <section className="card"><div className="tile-icon"><Check/></div><h3>일일 의뢰</h3><div className="value">{count(n.dailyFinished)} <span>/ {count(n.dailyTotal)}</span></div>{n.dailyTotal>0&&<Meter value={n.dailyFinished} max={n.dailyTotal} label="일일 의뢰"/>}<p>추가 보상 {n.rewardReceived==null?'확인 불가':n.rewardReceived?'수령 완료':'미수령'}</p></section>
    <section className="card"><div className="tile-icon"><ArrowUpRight/></div><h3>탐사 파견</h3><div className="value">{count(n.expeditionCount)} <span>/ {count(n.expeditionMax)}</span></div><p>조회 시점 · 완료 {complete} / 진행 중 {ongoing}</p><div className="expedition-list">{n.expeditions?.map((e,i)=><div key={i}><span>파견 {i+1}</span><span>{e.status==='Finished'?'완료':e.remainingSeconds!=null?remaining(e.remainingSeconds)+' 남음':e.status==='Ongoing'?'진행 중':'정보 없음'}</span></div>)}</div><span className="small muted">파견 시간·상태는 서버 조회 시점 기준</span></section>
   </div>
  </>:<section className="card unavailable"><Shield/><h3>{a.busy?'실시간 노트를 확인하고 있어요':'연결 후 실제 모험 정보를 표시해요'}</h3><p>레진·탐사 파견·일일 의뢰는 HoYoLAB에서 실제로 제공하는 값만 보여줘요. 원신 계정 영역에는 DEMO 값을 대신 표시하지 않아요.</p></section>}
 </>;
}
export function GenshinSummary({account}){const a=account||{};return <><span className={'badge '+(a.notes&&!a.stale?'positive':'')}>{a.notes?(a.stale?'이전 HoYoLAB 데이터':'실제 HoYoLAB 데이터'):'HoYoLAB 미연결'}</span>{a.notes?<><div className="between"><span>퓨어 레진</span><strong>{count(a.notes.resin)} / {count(a.notes.resinMax)}</strong></div><Meter value={a.notes.resin} max={a.notes.resinMax} label="레진"/></>:<p className="muted">원신 탭에서 계정을 연결해 주세요.</p>}</>}
