-- Nyang social update: party status and weekly summary. Run after history.sql.
begin;
create table if not exists nyang_private.party_status (
 user_id uuid primary key references nyang_private.profiles(id) on delete cascade,
 state text not null check(state in ('none','looking','playing','away')),
 game_id text check(game_id in ('genshin','eternal','league','valorant')),
 updated_at timestamptz not null default now()
);
alter table nyang_private.party_status enable row level security;
revoke all on nyang_private.party_status from public,anon,authenticated;

-- This separate RPC leaves existing clients, presence and chat intact.
create or replace function public.nyang_party_pulse(p_state text,p_game text default null) returns jsonb
language plpgsql security definer set search_path='' as $$
declare uid uuid:=auth.uid(); result jsonb;
begin
 if uid is null then raise exception 'authentication required'; end if;
 if p_state is null or p_state not in ('none','looking','playing','away') or
   (p_game is not null and p_game not in ('genshin','eternal','league','valorant')) then raise exception 'invalid party'; end if;
 if p_state in ('none','away') then p_game:=null; end if;
 insert into nyang_private.party_status(user_id,state,game_id) values(uid,p_state,p_game)
 on conflict(user_id) do update set state=excluded.state,game_id=excluded.game_id,updated_at=now();
 select coalesce(jsonb_agg(jsonb_build_object('id',s.user_id,'state',s.state,'gameId',s.game_id)),'[]'::jsonb) into result
 from nyang_private.party_status s join nyang_private.presence p on p.id=s.user_id
 where s.state<>'none' and s.updated_at>now()-interval '120 seconds'
 and p.updated_at>now()-interval '120 seconds' and p.status<>'offline'
 and exists(select 1 from nyang_private.friendships f where f.accepted
 and ((f.sender=uid and f.recipient=s.user_id) or (f.recipient=uid and f.sender=s.user_id)));
 return result;
end $$;

-- Own summary only. Calendar weeks start Monday 00:00 KST; last week is a full week.
create or replace function public.nyang_weekly_summary() returns jsonb
language plpgsql security definer set search_path='' as $$
declare uid uuid:=auth.uid(); start_at timestamptz; previous_at timestamptz; result jsonb;
begin
 if uid is null then raise exception 'authentication required'; end if;
 start_at:=date_trunc('week',now() at time zone 'Asia/Seoul') at time zone 'Asia/Seoul';
 previous_at:=start_at-interval '7 days';
 update nyang_private.game_sessions set ended_at=last_seen_at,estimated=true
 where user_id=uid and ended_at is null and last_seen_at<now()-interval '120 seconds';
 with totals as (
  select game_id,
   sum(greatest(0,extract(epoch from least(ended_at,now())-greatest(started_at,start_at)))) as current_seconds,
   sum(greatest(0,extract(epoch from least(ended_at,start_at)-greatest(started_at,previous_at)))) as previous_seconds
  from nyang_private.game_sessions
  where user_id=uid and ended_at is not null and ended_at>previous_at and started_at<now()
  group by game_id
 ) select jsonb_build_object(
  'weekStart',(start_at at time zone 'Asia/Seoul')::date,
  'previousStart',(previous_at at time zone 'Asia/Seoul')::date,
  'asOf',now(),'seconds',coalesce(sum(current_seconds),0),'previousSeconds',coalesce(sum(previous_seconds),0),
  'games',coalesce((select jsonb_agg(jsonb_build_object('gameId',game_id,'seconds',current_seconds) order by current_seconds desc,game_id)
  from totals where current_seconds>0),'[]'::jsonb)
 ) into result from totals;
 return result;
end $$;
revoke all on function public.nyang_party_pulse(text,text),public.nyang_weekly_summary() from public,anon;
grant execute on function public.nyang_party_pulse(text,text),public.nyang_weekly_summary() to authenticated;
notify pgrst, 'reload schema';
commit;
select 'nyang_social_ready' as result;
