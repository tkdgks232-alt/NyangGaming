using System;using System.IO;using System.Collections.Generic;
internal static class SocialTests {
 internal static async System.Threading.Tasks.Task Live(string folder,string output){var log=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);log.Add("PASS "+label);File.WriteAllLines(output,log);};try{
  string ar=Path.Combine(folder,"a"),br=Path.Combine(folder,"b");
  if(!File.Exists(Path.Combine(ar,"friends-session.dpapi"))||!File.Exists(Path.Combine(br,"friends-session.dpapi")))throw new Exception("Existing test sessions required");
  using(var a=new FriendsService(ar,false))using(var b=new FriendsService(br,false)){
   var sa=await a.ChatRpc("nyang_snapshot",new{});var sb=await b.ChatRpc("nyang_snapshot",new{});
   var ma=ErJson.Get(sa,"me");var mb=ErJson.Get(sb,"me");check(ErJson.Text(ma,"nickname").StartsWith("호스트검증")&&ErJson.Text(mb,"nickname").StartsWith("호스트검증"),"existing isolated test accounts only");
   string aid=ErJson.Text(ma,"id"),bid=ErJson.Text(mb,"id");Exception failure=null;
   try{
    await a.Action("request",ErJson.Text(mb,"code"),false);await b.Action("respond",aid,true);
    a.Options(true,false,false,10);b.Options(true,false,false,10);a.SocialOptions(true,"looking","eternal",null,null);b.SocialOptions(true,"none","",aid,false);a.PageVisible(true);
    int arrivals=0;b.FriendArrived+=(id,name)=>{if(id==aid)arrivals++;};
    await a.Tick(new GameState[0]);await b.Tick(new GameState[0]);
    check(a.View.social.connected&&b.View.social.connected,"production social RPCs connected");
    check(Array.Exists(b.View.social.parties,p=>ErJson.Text(p,"id")==aid&&ErJson.Text(p,"state")=="looking"&&ErJson.Text(p,"gameId")=="eternal"),"party transmitted between existing accounts");
    check(a.View.social.weekly!=null&&!a.View.social.weeklyStale&&ErJson.Text(a.View.social.weekly,"weekStart").Length>0,"production weekly summary returned");
    a.Options(true,true,false,10);await a.Tick(new GameState[0]);await b.Action("refresh",null,false);
    check(!Array.Exists(b.View.social.parties,p=>ErJson.Text(p,"id")==aid),"production offline setting hides party");
    a.Options(true,false,false,10);await a.Tick(new GameState[0]);await b.Action("refresh",null,false);check(arrivals==1,"production snapshot emits one friend arrival");
    await b.Action("refresh",null,false);check(arrivals==1,"repeat snapshot does not repeat alert");
    a.Options(false,false,false,10);await a.Tick(new GameState[0]);await b.Action("refresh",null,false);
    check(Array.Exists(b.View.social.parties,p=>ErJson.Text(p,"id")==aid&&string.IsNullOrEmpty(ErJson.Text(p,"gameId"))),"game sharing off removes party game");
   }catch(Exception e){failure=e;}try{await a.ChatRpc("nyang_party_pulse",new{p_state="none",p_game=(string)null});await b.ChatRpc("nyang_party_pulse",new{p_state="none",p_game=(string)null});await a.Action("remove",bid,false);await a.Offline();await b.Offline();}catch{log.Add("Cleanup needs review");}if(failure!=null)throw failure;
  }
  log.Add("ALL PASS: "+log.Count);
 }catch(Exception e){log.Add("FAIL "+e.GetType().Name+": "+e.Message);}File.WriteAllLines(output,log);}
 internal static void Run(string output){var log=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);log.Add("PASS "+label);};try{
  string id="00000000-0000-0000-0000-000000000001";var t=new FriendArrivalTracker();var now=DateTime.UtcNow;
  Func<string,object[]> rows=state=>new object[]{new Dictionary<string,object>{{"id",id},{"nickname","test"},{"status",state}}};
  check(t.Observe(rows("online"),now,false,true,null).Length==0,"startup never floods existing online friends");
  t.Observe(rows("offline"),now.AddSeconds(30),false,true,null);
  check(t.Observe(rows("gaming"),now.AddSeconds(60),false,true,null).Length==1,"offline to gaming alerts once");
  check(t.Observe(rows("away"),now.AddSeconds(65),false,true,null).Length==0,"online status changes do not alert");
  t.Observe(rows("offline"),now.AddSeconds(70),false,true,null);
  check(t.Observe(rows("online"),now.AddSeconds(80),false,true,null).Length==0,"rapid reconnect cooldown");
  t.Observe(rows("offline"),now.AddMinutes(6),true,true,null);
  check(t.Observe(rows("online"),now.AddMinutes(6).AddSeconds(10),false,true,new[]{id}).Length==0,"per friend mute");
  t.Observe(rows("offline"),now.AddMinutes(7),false,true,null);
  check(t.Observe(rows("online"),now.AddMinutes(7).AddSeconds(10),false,false,null).Length==0,"global notifications off");
  t.Observe(rows("offline"),now.AddMinutes(8),false,true,null);
  check(t.Observe(rows("online"),now.AddMinutes(9),true,true,null).Length==0,"network failure recovery baseline");
  t.Observe(rows("offline"),now.AddMinutes(10),false,true,null);
  check(t.Observe(rows("online"),now.AddMinutes(13),false,true,null).Length==0,"sleep gap is not friend arrival");
  t.Observe(new object[0],now.AddMinutes(13).AddSeconds(10),false,true,null);
  check(t.Observe(rows("online"),now.AddMinutes(13).AddSeconds(20),false,true,null).Length==0,"newly accepted friend not arrival");
  check(!FriendsService.ValidParty("invalid","league")&&!FriendsService.ValidParty("looking","browser")&&FriendsService.ValidParty("looking","eternal"),"party whitelist");
  using(var s=new FriendsService("",true)){s.SocialOptions(false,"looking","eternal",id,true);s.Options(false,true,false,15);check(s.View.social.options.partyState=="looking"&&!s.View.social.options.loginNotifications,"presence updates preserve independent social settings");s.SocialOptions(true,"away","league",id,false);check(s.View.social.options.partyGame==""&&s.View.social.options.mutedFriends.Length==0,"away removes game and unmute persists");}
  check(AppUpdateService.ShouldShowChangelog("1.0.1","1.0.2")&&!AppUpdateService.ShouldShowChangelog("1.0.2","1.0.2")&&AppUpdateService.ShouldShowChangelog(null,"1.0.2"),"release notes once per version and first install");
  log.Add("ALL PASS: "+log.Count);
 }catch(Exception e){log.Add("FAIL "+e);}File.WriteAllLines(output,log);}
}
