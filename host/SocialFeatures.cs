using System;using System.Collections.Generic;using System.IO;using System.Linq;

internal sealed class SocialPreferences {
 public bool loginNotifications=true;public string partyState="none",partyGame="";public string[] mutedFriends=new string[0];
}
internal sealed class SocialView {
 public SocialPreferences options=new SocialPreferences();public object[] parties=new object[0];public object weekly;
 public bool connected,weeklyStale;public string message="파티 상태 연결 대기 중",weeklyMessage="친구 프로필을 만들면 주간 요약을 볼 수 있어요.";
}
internal sealed class FriendArrivalTracker {
 readonly Dictionary<string,bool> previous=new Dictionary<string,bool>();readonly Dictionary<string,DateTime> lastAlert=new Dictionary<string,DateTime>();DateTime sampled=DateTime.MinValue;
 internal string[] Observe(object[] friends,DateTime now,bool reset,bool enabled,string[] muted){
  bool baseline=reset||sampled==DateTime.MinValue||now-sampled>TimeSpan.FromSeconds(90);var notices=new List<string>();var next=new Dictionary<string,bool>();
  foreach(var f in friends){string id=ErJson.Text(f,"id"),status=ErJson.Text(f,"status");bool online=status=="online"||status=="gaming"||status=="away";bool old;DateTime last;
   if(!baseline&&enabled&&online&&previous.TryGetValue(id,out old)&&!old&&!(muted??new string[0]).Contains(id)&&(!lastAlert.TryGetValue(id,out last)||now-last>=TimeSpan.FromMinutes(5))){notices.Add(id);lastAlert[id]=now;}
   next[id]=online;
  }
  previous.Clear();foreach(var kv in next)previous[kv.Key]=kv.Value;sampled=now;
  foreach(var id in lastAlert.Keys.Where(id=>!next.ContainsKey(id)).ToArray())lastAlert.Remove(id);
  return notices.ToArray();
 }
}
internal sealed partial class FriendsService {
 internal event Action<string,string> FriendArrived;
 readonly FriendArrivalTracker arrivals=new FriendArrivalTracker();DateTime socialRetry=DateTime.MinValue,weeklyDue=DateTime.MinValue;
 internal static bool ValidParty(string state,string game){return new[]{"none","looking","playing","away"}.Contains(state)&&(string.IsNullOrEmpty(game)||new[]{"genshin","eternal","league","valorant"}.Contains(game));}
 void LoadSocial(){try{View.social.options=json.Deserialize<SocialPreferences>(File.ReadAllText(Path.Combine(root,"social-options.json")))??new SocialPreferences();}catch{}var o=View.social.options;if(!ValidParty(o.partyState,o.partyGame)){o.partyState="none";o.partyGame="";}if(o.mutedFriends==null)o.mutedFriends=new string[0];}
 internal void SocialOptions(bool enabled,string state,string game,string friend,bool? muted){
  if(!ValidParty(state,game))return;var o=View.social.options;o.loginNotifications=enabled;o.partyState=state;o.partyGame=state=="none"||state=="away"?"":game??"";
  Guid id;if(muted.HasValue&&Guid.TryParse(friend,out id)){var ids=new HashSet<string>(o.mutedFriends);if(muted.Value)ids.Add(id.ToString());else ids.Remove(id.ToString());o.mutedFriends=ids.Take(100).ToArray();}
  if(!verify)Save("social-options.json",o);next=DateTime.MinValue;socialRetry=DateTime.MinValue;View.social.message="파티 상태를 반영 중이에요.";Notify();
 }
 void ObserveArrivals(){foreach(string id in arrivals.Observe(View.friends,DateTime.UtcNow,View.stale,View.social.options.loginNotifications,View.social.options.mutedFriends)){
  var f=View.friends.First(x=>ErJson.Text(x,"id")==id);if(FriendArrived!=null)try{FriendArrived(id,ErJson.Text(f,"nickname"));}catch{}
 }}
 async System.Threading.Tasks.Task SyncSocial(){
  if(DateTime.UtcNow<socialRetry)return;
  try{var o=View.social.options;var rows=await Rpc("nyang_party_pulse",new{p_state=View.options.appearOffline?"none":o.partyState,p_game=View.options.shareGame&&!View.options.appearOffline&&!string.IsNullOrEmpty(o.partyGame)?o.partyGame:null});View.social.parties=ErJson.Rows(rows).Cast<object>().ToArray();View.social.connected=true;View.social.message=View.options.appearOffline?"오프라인 표시 중에는 파티 상태도 숨겨져요.":"파티 상태는 온라인인 동안 승인한 친구에게 표시돼요.";}
  catch{View.social.connected=false;View.social.parties=new object[0];View.social.message="파티 상태 연결 실패 · 서버 설정 또는 인터넷 연결을 확인해 주세요.";socialRetry=DateTime.UtcNow.AddMinutes(5);}
  if(pageVisible&&DateTime.UtcNow>=weeklyDue){weeklyDue=DateTime.UtcNow.AddMinutes(5);try{View.social.weekly=await Rpc("nyang_weekly_summary",new{});View.social.weeklyStale=false;View.social.weeklyMessage="완료된 게임 세션 기준 · 한국 시간 월요일 시작";}catch{View.social.weeklyStale=true;View.social.weeklyMessage="주간 요약을 불러오지 못했어요. 서버 설정 또는 인터넷 연결을 확인해 주세요.";}}
 }
}
