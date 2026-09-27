import React,{useEffect,useState} from 'react';
import {send} from './bridge';
export function ValorantAccount({profile={}}){
 const [id,setId]=useState('');useEffect(()=>{setId(profile.riotId||'');},[profile.riotId]);
 return <section className="card league-account valorant-account"><span className="eyebrow">VALKING / ACCOUNT</span><div className="between"><h2>경쟁전 브리핑</h2><span className="badge">{profile.busy?'조회 중':profile.stale?'저장된 기록':profile.state==='connected'?'연결됨':'계정 연결'}</span></div><div className="riot-identity"><h3>{profile.riotId||'아직 연결하지 않았어요'}</h3></div>
 <form className="league-connect" onSubmit={e=>{e.preventDefault();send('valorant.connect',{riotId:id});}}><label>Riot ID<input value={id} onChange={e=>setId(e.target.value)} placeholder="이름#태그" maxLength={49} required disabled={profile.busy}/></label><button className="button" disabled={profile.busy}>계정 연결</button></form>
 <div className="league-actions"><button className="button secondary" onClick={()=>send('valorant.site')}>Valking에서 확인</button>{profile.riotId&&<><button className="button secondary" disabled={profile.busy} onClick={()=>send('valorant.refresh')}>새로고침</button><button className="button secondary" disabled={profile.busy} onClick={()=>send('valorant.disconnect')}>연결 해제</button></>}</div>
 <p className="small muted" role="status">{profile.message}{profile.updatedAt&&<><br/>마지막 조회 {new Date(profile.updatedAt).toLocaleString('ko-KR')}</>}</p>
 <div className="riot-stats">{[['현재 Rank',profile.rank],['RR',profile.points],['사이트 랭크 승 / 패',profile.wins!=null?`${profile.wins} / ${profile.losses??'—'}`:null],['랭크 Headshot %',profile.headshotPercent!=null?`${profile.headshotPercent}%`:null]].map(([label,value])=><div className="riot-stat" key={label}><span>{label}</span><strong>{value??'—'}</strong></div>)}</div>
 <p className="small muted">출처: Valking · 사이트에 기록된 범위예요. 현재 조회 응답에는 RR과 경기별 RR 변화가 제공되지 않아요.</p></section>;
}
