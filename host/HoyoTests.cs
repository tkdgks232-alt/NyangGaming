using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class HoyoTests {
    internal static DailyNotes Notes(){return HoYoLabProvider.ParseNotes(HoYoLabProvider.Obj(new JavaScriptSerializer().DeserializeObject("{\"current_resin\":142,\"max_resin\":200,\"resin_recovery_time\":27720,\"current_expedition_num\":4,\"max_expedition_num\":5,\"daily_task\":{\"finished_num\":3,\"total_num\":4,\"is_extra_task_reward_received\":false},\"expeditions\":[{\"status\":\"Finished\",\"remained_time\":\"0\"},{\"status\":\"Ongoing\",\"remained_time\":\"7200\"},{\"status\":\"Finished\",\"remained_time\":\"0\"},{\"status\":\"Ongoing\",\"remained_time\":\"3600\"}]}")));}
    internal static List<GenshinRole> Roles(){return new List<GenshinRole>{new GenshinRole{uid="800000001",server="os_asia",serverName="Asia",nickname="검증용 여행자 (실제 계정 아님)"}};}
    internal static List<GenshinCharacter> CharacterFixture(){return new List<GenshinCharacter>{new GenshinCharacter{id=10000089,name="검증용 아주 긴 캐릭터 이름 · 실제 계정 아님",element="Hydro",level=90,constellation=2,friendship=10,icon="",detailed=true,weapon=new CharacterEquipment{name="검증용 긴 이름의 무기",level=90,refinement=1},stats=new List<CharacterStat>{new CharacterStat{name="치명타 확률",value="58.3%"},new CharacterStat{name="원소 충전 효율",value="142.7%"}},artifacts=new List<CharacterEquipment>{new CharacterEquipment{name="검증용 성유물",level=20,slot="생명의 꽃",set="검증용 세트"}}}};}
    sealed class Fake:IGenshinDataProvider,IGenshinCharacterProvider {
        internal int characterCalls;
        public Task<List<GenshinCharacter>> GetCharacters(Dictionary<string,string> cookies,GenshinRole role,int? id,CancellationToken token){characterCalls++;return Task.FromResult(CharacterFixture());}
        internal string failure;internal int calls;internal TaskCompletionSource<DailyNotes> pending;
        internal List<GenshinRole> roles=Roles();
        public Task<List<GenshinRole>> GetRoles(Dictionary<string,string> cookies,CancellationToken token){calls++;return Task.FromResult(roles);}
        public Task<DailyNotes> GetNotes(Dictionary<string,string> cookies,GenshinRole role,CancellationToken token){
            if(failure!=null)throw new HoyoFailure(failure,"테스트 오류");
            return pending==null?Task.FromResult(Notes()):pending.Task;
        }
    }
    internal static async Task Run(string directory){
        Directory.CreateDirectory(directory);string isolated=Path.Combine(directory,"vault-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(isolated);
        var report=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new InvalidOperationException("FAIL: "+name);report.Add("PASS: "+name);};
        try{
            var cookies=new Dictionary<string,string>{{"ltuid_v2","test-user"},{"ltoken_v2","test-only-never-real-secret"},{"tracking","discard"},{"cookie_token_v2","not-needed"}};
            var safe=HoYoLabProvider.FilterCookies(cookies);check(safe.Count==2&&HoYoLabProvider.HasSession(safe),"authentication allowlist discards unrelated/payment cookies");
            safe=HoYoLabProvider.FilterCookies(new Dictionary<string,string>{{"ltoken_v2","bad\r\nheader"},{"ltuid_v2","test"}});check(!HoYoLabProvider.HasSession(safe),"header injection rejected");
            var vault=new HoyoVault(isolated);vault.Save(new HoyoSession{cookies=HoYoLabProvider.FilterCookies(cookies),uid="800000001",server="os_asia"});
            check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(isolated,"hoyolab-session.dpapi"))).Contains("test-only"),"vault is ciphertext, not plaintext");
            check(vault.Load().cookies["ltoken_v2"]==cookies["ltoken_v2"],"DPAPI current-user round trip");
            var notes=Notes();check(notes.resin==142&&notes.resinMax==200&&notes.expeditions.Count==4&&notes.dailyFinished==3&&notes.rewardReceived==false,"notes resin/expeditions/daily parsing");
            var zero=HoYoLabProvider.ParseNotes(new Dictionary<string,object>{{"current_resin",0},{"max_resin",200}});check(zero.resin==0&&zero.dailyTotal==null&&zero.expeditionCount==null,"zero is real; missing daily/expedition values stay unavailable");
            bool rejected=false;try{HoYoLabProvider.ParseNotes(new Dictionary<string,object>());}catch(HoyoFailure){rejected=true;}check(rejected,"missing resin rejected, no fabricated values");
            var roleBody=HoYoLabProvider.Obj(new JavaScriptSerializer().DeserializeObject("{\"list\":[{\"game_biz\":\"hk4e_global\",\"game_uid\":\"800000001\",\"region\":\"os_asia\",\"nickname\":\"A\"},{\"game_biz\":\"hk4e_global\",\"game_uid\":\"700000001\",\"region\":\"os_euro\",\"nickname\":\"B\"},{\"game_biz\":\"hkrpg_global\",\"game_uid\":\"800000001\",\"region\":\"os_asia\"}]}"));
            check(HoYoLabProvider.ParseRoles(roleBody).Count==2,"UID/server auto parsing excludes other games");
            check(HoYoLabProvider.ApiError(10001).Kind=="expired"&&HoYoLabProvider.ApiError(10102).Kind=="private"&&HoYoLabProvider.ApiError(10101).Kind=="rateLimit"&&HoYoLabProvider.ApiError(1034).Kind=="verification"&&HoYoLabProvider.ApiError(1005).Kind=="service","distinct sanitized error states including unknown 1005");
            check(HoyoLoginWindow.IsOfficial("https://www.hoyolab.com/")&&!HoyoLoginWindow.IsOfficial("https://hoyolab.com.attacker.test/")&&!HoyoLoginWindow.IsOfficial("http://hoyolab.com/")&&!HoyoLoginWindow.IsOfficial("https://hoyolab.com:8443/"),"official login navigation guard");
            var fake=new Fake();using(var service=new GenshinService(isolated,true,fake)){
                await service.Accept(cookies,Roles());check(service.View.connected&&service.View.notes.resin==142&&service.View.selected.server=="os_asia","login -> auto UID selection -> notes");
                string dto=new JavaScriptSerializer().Serialize(service.View);check(!dto.Contains("ltoken")&&!dto.Contains("test-only")&&!dto.Contains("cookies"),"React snapshot contains no credential fields or values");
                int before=fake.calls;await service.Refresh(true);check(fake.calls==before,"manual refresh cooldown");
                await service.LoadCharacters(false);check(service.Characters.items.Count==1,"character provider reuses connected session");
                await service.LoadCharacters(false);check(fake.characterCalls==1,"character list cache avoids repeated requests");
                await service.LoadCharacters(false,10000089);check(service.Characters.detail.level==90,"character detail selected from owned list");
                await service.LoadCharacters(false,10000089);check(fake.characterCalls==2,"character detail cache avoids repeated requests");
                await service.LoadCharacters(false,123);check(fake.characterCalls==2,"unowned character ID rejected");
                fake.failure="network";await service.Refresh(false);check(service.View.state=="network"&&service.View.stale&&service.View.notes.resin==142,"network failure isolated and previous data marked stale");
                fake.failure="expired";await service.Select("800000001","os_asia");check(service.View.state=="expired"&&!service.View.connected,"expired session requires reconnect");
                fake.failure="rateLimit";await service.Accept(cookies,Roles());before=fake.calls;await service.Refresh(true);check(fake.calls==before&&service.View.state=="rateLimit","rate-limit backoff blocks manual hammering");
                fake.failure=null;fake.roles=HoYoLabProvider.ParseRoles(roleBody);await service.Accept(cookies,fake.roles);check(service.View.state=="selectRole"&&service.View.selected==null,"multiple characters require selection");
                await service.Select("700000001","os_euro");check(service.View.selected.uid=="700000001"&&vault.Load().server=="os_euro","selected UID/server persisted securely");
                using(var reloaded=new GenshinService(isolated,false,fake)){await reloaded.Tick();check(reloaded.View.selected.uid=="700000001"&&reloaded.View.notes.resin==142,"restart restores session and fetches notes");}
                fake.roles=new List<GenshinRole>();await service.Accept(cookies,fake.roles);check(service.View.state=="noCharacter"&&service.View.notes==null,"no game character handled");
                fake.roles=Roles();fake.pending=new TaskCompletionSource<DailyNotes>();var request=service.Accept(cookies,Roles());
                service.Disconnect();fake.pending.SetResult(Notes());await request;
                check(service.View.state=="disconnected"&&!vault.Exists&&service.View.notes==null,"disconnect cancels pending result and deletes credential only");
                check(service.Characters.items.Count==0&&service.Characters.detail==null,"disconnect clears character cache");
            }
            check(!vault.Exists,"no test credential retained");
            check(CharacterImageCache.SafeUrl("https://upload-os-bbs.hoyolab.com/a.png").Length>0&&CharacterImageCache.SafeUrl("https://upload-os-bbs.hoyolab.com.evil.test/a.png")==""&&CharacterImageCache.SafeUrl("file:///C:/secret")=="","character images restricted to official public CDN");
            check(OverlayHotkey.Valid("G")&&!OverlayHotkey.Valid("Ctrl+G"),"hotkey setting validated");
            check(GamingOverlay.BottomCenter(new System.Drawing.Rectangle(-1080,0,1080,1920),new System.Drawing.Size(400,100),12)==new System.Drawing.Point(-740,1808),"overlay negative-origin second monitor positioning");
            File.WriteAllLines(Path.Combine(directory,"hoyo-tests.txt"),report);
        }catch(Exception ex){File.WriteAllLines(Path.Combine(directory,"hoyo-tests.txt"),report.Concat(new[]{ex is InvalidOperationException?ex.Message:"FAIL: test infrastructure"}));throw;}
    }
}
