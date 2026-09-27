import React from 'react';
import {Sparkles,Target,Shield,Crosshair,Play,ArrowUpRight,Compass,Check,Clock,TrendingUp} from 'lucide-react';
import {send} from './bridge';

const worlds={
 genshin:{name:'원신',english:'GENSHIN IMPACT',tag:'TEYVAT / ADVENTURE',title:<>별빛을 따라,<br/>티바트로.</>,description:'작은 일일 의뢰부터 새로운 여행까지. 모험의 준비를 한곳에서.',Icon:Compass},
 eternal:{name:'이터널 리턴',english:'ETERNAL RETURN',tag:'LUMIA ISLAND / SURVIVAL',title:<>오늘의 한 판,<br/>다음의 나.</>,description:'루미아 섬의 기록을 모으고, 한 걸음 더 높은 곳으로.',Icon:Target},
 league:{name:'League of Legends',english:'LEAGUE OF LEGENDS',tag:'SUMMONER’S RIFT / TEAMPLAY',title:<>협곡에서 시작되는,<br/>우리만의 한 판.</>,description:'익숙한 챔피언, 새로운 전략. 오늘의 플레이를 준비해요.',Icon:Shield},
 valorant:{name:'VALORANT',english:'VALORANT',tag:'PRECISION / TEAMWORK',title:<>한 라운드의 집중,<br/>다음 승리를 향해.</>,description:'팀과 함께 만드는 한 번의 기회. 다음 라운드를 준비해요.',Icon:Crosshair}
};

export function GameHero({id,settings,game={}}){
 const c=worlds[id],background=settings?.artwork?.[id+'-background'],character=settings?.artwork?.[id+'-character'];
 const state=game.running?'지금 플레이 중':game.clientState==='gameClient'?'LoL Client 실행 중 · 경기 대기':game.clientState==='riotClient'?'Riot Client 실행 중 · 게임 대기':'플레이 준비 완료';
 return <section className={'world-hero world-'+id} aria-label={c.name+' 게임 실행'}>
  <div className="world-scenery" aria-hidden="true"><img src={'game-art/'+id+'.png'} alt=""/>{background&&<img className="world-custom-background" src={background} style={{opacity:settings.artworkOpacity??.65}} alt=""/>}</div>
  <div className="world-veil" aria-hidden="true"/>
  {character&&<img className="world-character" src={character} alt="" aria-hidden="true"/>}
  <div className="world-copy"><div className="world-label"><div className="world-logo-frame"><img className={'world-logo-img '+(id==='eternal'?'logo-day':'')} src={'game-logos/'+id+'.png'} alt={c.name}/>{id==='eternal'&&<img className="world-logo-img logo-night" src="game-logos/eternal-white.png" alt={c.name}/>}</div></div><h1>{c.title}</h1><p>{c.description}</p>
   <div className="world-actions"><button className="button" disabled={game.running} onClick={()=>send('launch',{game:id})}><Play size={16}/>{game.running?'플레이 중':['league','valorant'].includes(id)?'게임 / 런처 실행':c.name+' 실행'}</button><span className="world-status"><i className={'dot '+(game.running?'':'idle')}/>{state}</span></div>
  </div><div className="world-caption" aria-hidden="true"><span>{c.tag}</span><span>YOUR WORLD, YOUR PACE.</span></div>
 </section>;
}

const value=n=>n==null?'—':Number(n).toLocaleString('ko-KR');
function Tile({Icon,label,children,note,className=''}){return <section className={'card world-tile '+className}><div className="world-tile-label"><Icon size={19}/><h2>{label}</h2></div><div className="world-tile-value">{children}</div><p>{note}</p></section>}
export function GameOverview({id,account}){
 if(id==='genshin'){
  const n=account?.notes,stale=account?.stale;
  const percent=n?.resin!=null&&n.resinMax>0?Math.max(0,Math.min(100,n.resin/n.resinMax*100)):0;
  return <div className="world-overview overview-genshin" aria-label="모험 정보 요약">
   <Tile Icon={Sparkles} label="퓨어 레진" className="world-resin" note={n?(stale?'이전 조회 정보 · 새로고침 필요':'HoYoLAB 조회 시점 기준'):'HoYoLAB 연결 후 확인'}><div className="resin-dial" style={{'--resin-fill':percent+'%'}}><span>{value(n?.resin)}<small>{n?.resinMax!=null?'/ '+value(n.resinMax):'연동 대기'}</small></span></div></Tile>
   <Tile Icon={Check} label="일일 의뢰" note={n?.rewardReceived==null?'계정을 연결하면 표시돼요':n.rewardReceived?'추가 보상 수령 완료':'추가 보상 미수령'}>{value(n?.dailyFinished)}<small>{n?.dailyTotal!=null?'/ '+value(n.dailyTotal):'연동 대기'}</small><div className="daily-marks" aria-hidden="true">{Array.from({length:Math.min(8,n?.dailyTotal||4)},(_,i)=><i className={n&&i<n.dailyFinished?'complete':''} key={i}><Check size={13}/></i>)}</div></Tile>
   <Tile Icon={Compass} label="탐사 파견" note={n?'완료 '+(n.expeditions?.filter(e=>e.status==='Finished').length??0)+'건 · 자세한 정보는 아래에서':'돌아오는 동료들의 소식을 한곳에'}>{value(n?.expeditionCount)}<small>{n?.expeditionMax!=null?'/ '+value(n.expeditionMax):'연동 대기'}</small><div className="expedition-route" aria-hidden="true"><i/><span/><i/><span/><Compass size={18}/></div></Tile>
  </div>;
 }
 const d=account?.connected?account.data:null,recent=d?.matches?.find(m=>m.mode===3&&m.season===d.seasonId);
 return <div className="world-overview overview-eternal" aria-label="루미아 기록 요약">
  <Tile Icon={Shield} label="현재 티어" note={d?d.nickname: '계정 연결 후 시즌 기록을 확인해요'} className="world-rank"><Shield className="world-rank-icon" size={38}/><strong>{d?.tier||'연동 대기'}</strong></Tile>
  <Tile Icon={TrendingUp} label="현재 시즌 RP" note={recent?.delta!=null?'최근 경기 '+(recent.delta>0?'+':'')+value(recent.delta)+' RP':'공식 기록이 연결되면 표시돼요'}>{value(d?.rp)}<small>RP</small></Tile>
  <Tile Icon={Target} label="최근 랭크 경기" note={recent?recent.character:'루미아 섬에서의 다음 기록을 기다려요'}>{recent?.rank!=null?'#'+value(recent.rank):'—'}<small>{recent?'Kill '+value(recent.kills)+' · Assist '+value(recent.assists):'연동 대기'}</small></Tile>
 </div>;
}
