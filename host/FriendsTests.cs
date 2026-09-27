using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
internal static class FriendsTests {
    internal static async System.Threading.Tasks.Task Live(string output){
        var lines=new List<string>();Action<bool,string> check=(ok,label)=>{lines.Add((ok?"PASS ":"FAIL ")+label);File.WriteAllLines(output,lines);if(!ok)throw new Exception(label);};
        string root=Path.Combine(Path.GetDirectoryName(output),"friends-host-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string ar=Path.Combine(root,"a"),br=Path.Combine(root,"b");Directory.CreateDirectory(ar);Directory.CreateDirectory(br);
        using(var a=new FriendsService(ar,false))using(var b=new FriendsService(br,false)){
            await a.Profile("호스트검증A");await b.Profile("호스트검증B");check(a.View.connected&&b.View.connected,"native host authentication and profile");
            string aid=ErJson.Text(a.View.me,"id"),bid=ErJson.Text(b.View.me,"id");await a.Action("request",ErJson.Text(b.View.me,"code"),false);await b.Action("refresh",null,false);check(b.View.requests.Length==1,"native friend request");await b.Action("respond",aid,true);await a.Action("refresh",null,false);check(a.View.friends.Length==1&&b.View.friends.Length==1,"native acceptance");
            await a.Tick(new[]{new GameState("genshin","원신"){running=true}});await b.Action("refresh",null,false);check(ErJson.Text(b.View.friends[0],"gameId")=="genshin","native heartbeat gaming");
            a.Options(false,false,false,10);await a.Tick(new[]{new GameState("genshin","원신"){running=true}});await b.Action("refresh",null,false);check(ErJson.Get(b.View.friends[0],"gameId")==null,"native sharing off clears server");
            a.PageVisible(true);await a.SelectChat(bid);string nonce=Guid.NewGuid().ToString();await a.SendMessage(bid,"호스트 코드 연결 검증 메시지",nonce);b.PageVisible(true);await b.SelectChat(aid);check(a.View.sentId==nonce&&b.View.messages.Length==1,"native send and receive");
            await System.Threading.Tasks.Task.Delay(1200);await a.Tick(new GameState[0]);string today=DateTime.UtcNow.AddHours(9).ToString("yyyy-MM-dd");int year=DateTime.UtcNow.AddHours(9).Year;
            await a.History(aid,year,"all",null,today);check(object.Equals(ErJson.Get(a.View.history,"allowed"),true)&&ErJson.Rows(ErJson.Get(a.View.history,"sessions")).Count()==1,"native detection produces one completed session");
            await b.History(aid,year,"all",null,today);check(object.Equals(ErJson.Get(b.View.history,"allowed"),false),"native private history denied");
            a.HistoryPrivacy(true);await a.Tick(new GameState[0]);await b.History(aid,year,"all",null,today);check(!a.View.historyPending&&object.Equals(ErJson.Get(b.View.history,"allowed"),true),"native sharing enables friend history");
            a.HistoryPrivacy(false);await a.Tick(new GameState[0]);await b.History(aid,year,"all",null,today);check(object.Equals(ErJson.Get(b.View.history,"allowed"),false),"native sharing revocation");
            await a.Offline();await b.Action("refresh",null,false);check(ErJson.Text(b.View.friends[0],"status")=="offline","native graceful offline");await a.Action("remove",bid,false);await b.Offline();
            check(File.Exists(Path.Combine(ar,"friends-session.dpapi")),"encrypted native session saved");
        }
    }
    internal static void Run(string output){var checks=new List<string>();Action<bool,string> check=(value,label)=>{checks.Add((value?"PASS ":"FAIL ")+label);if(!value)throw new Exception(label);};
        try{
            var options=new FriendsOptions();
            check(!options.shareHistory,"history private by default");
            check(FriendsService.SessionGames(new[]{new GameState("chrome","browser"){running=true},new GameState("eternal","ER"){running=true},new GameState("eternal","ER duplicate"){running=true},new GameState("genshin","GS"){running=false},new GameState("valorant","VAL"){running=true}}).SequenceEqual(new[]{"eternal","valorant"}),"session whitelist, running filter and deduplication");
            check(options.awayMinutes==10&&options.autoAway,"default 10-minute idle");
            check(FriendPresence.Decide(options,null,null,599).p_status=="online","before idle threshold");
            check(FriendPresence.Decide(options,null,null,600).p_status=="away","10-minute idle threshold");
            check(FriendPresence.Decide(options,null,null,0).p_status=="online","new input restores online");
            foreach(string game in new[]{"genshin","eternal","league","valorant"})check(FriendPresence.Decide(options,game,"2026-09-28T00:00:00Z",9999).p_status=="gaming","gaming beats idle "+game);
            check(FriendPresence.Decide(options,null,null,900).p_status=="away","game exit restores away");
            check(FriendPresence.Decide(options,null,null,0).p_status=="online","game exit restores online");
            foreach(string name in new[]{"Chrome","Discord","KakaoTalk","secret-file.txt"}){var p=FriendPresence.Decide(options,name,"private",0);check(p.p_game_id==null&&p.p_game_started_at==null&&p.p_status=="online","non-game excluded "+name);}
            options.shareGame=false;var hidden=FriendPresence.Decide(options,"genshin","private",0);check(hidden.p_game_id==null&&hidden.p_game_started_at==null&&hidden.p_status=="online","sharing off removes game");
            options.appearOffline=true;options.shareGame=true;hidden=FriendPresence.Decide(options,"eternal","private",9999);check(hidden.p_status=="offline"&&hidden.p_game_id==null&&hidden.p_game_started_at==null,"offline overrides all");
            options.appearOffline=false;options.autoAway=false;check(FriendPresence.Decide(options,null,null,99999).p_status=="online","auto-away off");
            var j=new JavaScriptSerializer();var dto=j.Deserialize<Dictionary<string,object>>(j.Serialize(hidden));check(dto.Keys.OrderBy(x=>x).SequenceEqual(new[]{"p_game_id","p_game_started_at","p_status"}),"only allowlisted presence fields serialized");
            check(FriendsService.ValidConfig("https://testproject.supabase.co/","sb_publishable_test"),"project configuration accepted");
            foreach(string url in new[]{"http://test.supabase.co/","https://test.supabase.co.evil.test/","https://evil.test/","https://user:pass@test.supabase.co/","https://test.supabase.co/other"})check(!FriendsService.ValidConfig(url,"sb_publishable_test"),"unsafe backend URL rejected");
            check(!FriendsService.ValidConfig("https://test.supabase.co/","sb_secret_test"),"privileged key rejected");
            using(var service=new FriendsService("",true)){service.HistoryPrivacy(true);service.Options(false,true,false,15);check(service.View.options.shareHistory,"presence settings preserve independent history sharing");service.HistoryPrivacy(false);check(service.View.options.appearOffline&&!service.View.options.shareGame,"history settings preserve presence settings");}
        }finally{File.WriteAllLines(output,checks);}
    }
    internal static void Fixture(FriendsService service){
        var v=service.View;v.configured=true;v.connected=true;v.message="화면 검증용 계정 · 실제 친구 또는 메시지가 아닙니다.";v.me=new{id="self",nickname="검증용 냥냥",code="NYANG-TESTONLY0000"};v.localStatus="online";
        v.historyUser="self";v.historyMessage="화면 검증 전용 기록 · 실제 플레이 기록 아님";
        string day=DateTime.UtcNow.AddHours(9).ToString("yyyy-MM-dd");var calendar=Enumerable.Range(0,30).Select(i=>new{day=DateTime.UtcNow.AddHours(9).AddDays(-i).ToString("yyyy-MM-dd"),seconds=new[]{0,600,7200,14400,25200}[i%5]}).ToArray();
        v.history=new{allowed=true,shareHistory=false,firstDate=calendar.Last().day,summary=new{all=216000,week=36000,month=216000,selected=216000,days=24,averageSession=7200,longestDay=new{day=day,seconds=14400}},games=new[]{new{gameId="eternal",seconds=108000},new{gameId="genshin",seconds=64800},new{gameId="valorant",seconds=43200}},calendar=calendar,trend=calendar,dayGames=new[]{new{gameId="eternal",seconds=3600}},sessions=new[]{new{id="fixture",gameId="eternal",startedAt=DateTime.UtcNow.AddHours(-1).ToString("o"),endedAt=DateTime.UtcNow.ToString("o"),durationSeconds=3600,estimated=false}}};
        v.friends=new object[]{new{id="game",nickname="게임 중인 친구",status="gaming",gameId="genshin",gameStartedAt=DateTime.UtcNow.AddMinutes(-84).ToString("o")},new{id="online",nickname="온라인 친구",status="online"},new{id="away",nickname="자리비움 친구",status="away"},new{id="offline",nickname="오프라인 친구",status="offline"}};
        v.requests=new object[]{new{id="request",nickname="새 친구의 요청",incoming=true}};v.chatUser="game";v.messages=new object[]{new{id="1",sender="game",body="검증용 메시지: 같이 게임할까?",sent_at=DateTime.UtcNow.ToString("o")},new{id="2",sender="self",body="응! 잠시 후에 만나자 🐾",sent_at=DateTime.UtcNow.ToString("o")}};
    }
}
