import React,{useEffect,useState} from 'react';
import {User,RefreshCw,KeyRound,Link,Clock} from 'lucide-react';
import {send} from './bridge';

const date=value=>value?new Date(value).toLocaleString('ko-KR'):'';
const rankName=r=>!r?'—':r.tier==='Unranked'?'Unranked':`${r.tier} ${r.division}`;
function Stat({label,value}){return <div className="riot-stat"><span>{label}</span><strong>{value??'—'}</strong></div>}
export function LeagueAccount({view={}}){
 const [riotId,setId]=useState(''),[platform,setPlatform]=useState('KR'),[queue,setQueue]=useState('RANKED_SOLO_5x5');
 useEffect(()=>{if(view.riotId)setId(view.riotId);if(view.platform)setPlatform(view.platform);},[view.riotId,view.platform]);
 const isSite=view.source==='iplol',data=view.data,rank=data?.ranks?.find(r=>r.queue===queue),snapshots=view.snapshots?.filter(s=>s.queue===queue)||[];
 return <section className="card league-account"><span className="eyebrow">{isSite?'IPLOL / ACCOUNT':'RIOT / ACCOUNT'}</span><div className="between"><h2>소환사와 랭크</h2><span className="badge">{view.busy?'불러오는 중':view.stale?'저장된 기록':view.connected?'연결됨':'계정 연결'}</span></div>
 <div className="riot-identity"><User size={20}/><div><span className="small muted">Riot ID · {view.platform||'KR'}{data?` · Lv. ${data.level}`:''}</span><h3>{data?.riotId||'아직 연결하지 않았어요'}</h3></div></div>
 <form className="league-connect" onSubmit={e=>{e.preventDefault();send('league.connect',{riotId,platform});}}>
 <label>Riot ID<input value={riotId} onChange={e=>setId(e.target.value)} placeholder="GameName#TagLine" maxLength={49} disabled={view.busy} autoComplete="off" required/></label>
 <label>서버<select value={platform} onChange={e=>setPlatform(e.target.value)} disabled={view.busy}>{[['KR','한국'],['JP1','일본'],['NA1','북미'],['EUW1','서유럽'],['EUN1','북/동유럽'],['BR1','브라질'],['LA1','라틴 북부'],['LA2','라틴 남부'],['TR1','튀르키예'],['RU','러시아']].filter(([v])=>!isSite||v==='KR').map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></label>
 <button className="button" disabled={(!isSite&&!view.hasKey)||view.busy}><Link size={15}/>{view.connected?'계정 변경':'계정 연결'}</button></form>
 <div className="league-actions">{!isSite&&<><button className="button secondary" disabled={view.busy} onClick={()=>send('league.key')}><KeyRound size={15}/>{view.hasKey?'API 키 변경':'API 키 등록'}</button><button className="button secondary" onClick={()=>send('league.portal')}>개발자 포털</button></>}{isSite&&<button className="button secondary" onClick={()=>send('league.site')}>아이피롤에서 확인</button>}{view.connected&&<><button className="button secondary" disabled={view.busy||(!isSite&&!view.hasKey)} onClick={()=>send('league.refresh')}><RefreshCw size={15}/>새로고침</button><button className="button secondary" disabled={view.busy} onClick={()=>send('league.disconnect')}>연결 해제</button></>}</div>
 <p className="small muted" role="status">{view.message}{data?.updatedAt&&<><br/>마지막 업데이트 {date(data.updatedAt)}{view.stale?' · 이전 정상 기록':''}</>}</p>
 {isSite&&<p className="small muted">출처: IPLOL · 한국 서버 · 원본 갱신 {date(data?.sourceUpdatedAt)||'확인 대기'} · 자동 조회는 30분 간격이며 최근 경기 수는 사이트 정책에 따라 제한돼요.</p>}<label className="league-queue">랭크 유형<select value={queue} onChange={e=>setQueue(e.target.value)}><option value="RANKED_SOLO_5x5">Solo / Duo</option><option value="RANKED_FLEX_SR">Flex</option></select></label>
 <div className="riot-stats"><Stat label="현재 Rank" value={rankName(rank)}/><Stat label="LP" value={rank?.points}/><Stat label="시즌 승 / 패" value={rank?.wins!=null?`${rank.wins} / ${rank.losses}`:null}/><Stat label="시즌 승률" value={rank?.winRate!=null?`${rank.winRate}%`:null}/></div>
 <p className="small muted">최근 LP 변화 · 현재 조회 결과에는 경기별 LP 변화가 제공되지 않습니다.</p>
 {view.snapshotMessage&&<p className="small muted">{view.snapshotMessage}</p>}
 {snapshots.length>0&&<details className="league-snapshots"><summary>저장한 랭크 기록 ({snapshots.length})</summary><p className="small muted">앱이 갱신한 시점의 실제 값이에요. 경기별 LP 증감을 뜻하지 않아요.</p>{snapshots.map((s,i)=><div key={i}><time>{date(s.timestamp)}</time><strong>{rankName(s)}{s.points!=null?` · ${s.points} LP`:''}</strong></div>)}</details>}
 </section>;
}
export function LeagueMatches({data}){return <section className="card riot-matches"><span className="eyebrow">MATCH HISTORY</span><h2>최근 경기</h2>{data?.matches?.length?<><p className="small muted">최근 수신 {data.matches.length}경기 · 조회처가 제공한 범위 · 사용자 설정 경기 제외</p>{data.matches.map(m=><article className="riot-match" key={m.id}><strong>{m.result}</strong><div>{m.imageUrl&&<img src={m.imageUrl} alt=""/>}<span>{m.characterName}</span></div><span>{m.kills} / {m.deaths} / {m.assists} K/D/A</span><span>{m.cs} CS</span><span>{Math.floor(m.duration/60)}분 {Math.floor(m.duration%60)}초</span><span>{m.queue}</span><time dateTime={m.playedAt}>{date(m.playedAt)}</time></article>)}</>:<div className="riot-empty"><Clock size={26}/><h3>{data?'최근 조회 범위에 경기 기록이 없어요':'아직 연결된 경기 기록이 없어요'}</h3><p>계정 연결 후 승패, 챔피언, K/D/A, CS와 경기시간을 확인할 수 있어요.</p></div>}</section>}
