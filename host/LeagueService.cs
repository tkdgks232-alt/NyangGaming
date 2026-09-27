using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class LeagueView {
    public bool connected,hasKey,busy,stale; public string source="iplol";
    public string state="disconnected",message="Riot ID로 계정을 연결해 주세요.",platform="KR",riotId="",nextRefreshAt,snapshotMessage;
    public LeagueData data;
    public LeagueSnapshot[] snapshots=new LeagueSnapshot[0];
}
internal sealed class LeagueStored { public LeagueIdentity identity;public LeagueData data;public string source="api"; }
internal sealed class LeagueService:IDisposable {
    internal LeagueView View=new LeagueView();internal event Action Changed;
    internal Func<DateTime> Now=()=>DateTime.UtcNow;
    internal readonly string ImageFolder;
    readonly bool verify;readonly string keyPath,accountPath,dbPath;readonly ILeagueProvider provider,website;
    readonly JavaScriptSerializer json=LeagueJson.Serializer();
    LeagueIdentity identity;string key;bool disposed,authBlocked;
    CancellationTokenSource cancel=new CancellationTokenSource();int revision,exitRevision,exitAttempt;
    DateTime due=DateTime.MinValue,cooldown=DateTime.MinValue,exitDue=DateTime.MaxValue;
    HashSet<string> exitIds=new HashSet<string>();
    internal int ExitAttempts {get{return exitAttempt;}}
    internal bool ExitPending {get{return exitDue!=DateTime.MaxValue;}}
    internal LeagueService(string root,bool test,ILeagueProvider injected=null,ILeagueProvider siteInjected=null){
        verify=test;Directory.CreateDirectory(root);ImageFolder=Path.Combine(root,"LeagueImages");Directory.CreateDirectory(ImageFolder);
        keyPath=Path.Combine(root,"league-key.dpapi");accountPath=Path.Combine(root,"league-account.json");dbPath=Path.Combine(root,"league-ranks.sqlite");provider=injected??new RiotApiProvider(root);website=siteInjected??new LeagueIplolProvider(root);
        if(verify)return;
        try{key=Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(keyPath),null,DataProtectionScope.CurrentUser));}catch{}
        View.hasKey=ValidKey(key);View.source=View.hasKey||injected!=null?"api":"iplol";
        try{var stored=File.Exists(accountPath)?json.Deserialize<LeagueStored>(File.ReadAllText(accountPath)):null;if(stored!=null&&stored.identity!=null&&RiotApiProvider.ValidPlatform(stored.identity.platform)&&!string.IsNullOrEmpty(stored.identity.puuid)){View.source=stored.source=="iplol"?"iplol":"api";identity=stored.identity;View.connected=true;View.data=stored.data;View.riotId=identity.riotId;View.platform=identity.platform;View.stale=stored.data!=null;View.state="restoring";View.message="저장된 계정의 최신 기록을 확인하고 있어요.";ReadSnapshots();}}catch{View.message="저장된 계정 기록을 읽지 못했어요. Riot ID로 다시 연결해 주세요.";}
    }
    internal static bool ValidKey(string value){return !string.IsNullOrWhiteSpace(value)&&value.StartsWith("RGAPI-",StringComparison.Ordinal)&&value.Length<=256&&!value.Any(char.IsWhiteSpace)&&!value.Any(char.IsControl);}
    void Notify(){if(!disposed&&Changed!=null)Changed();}
    void Reset(){revision++;exitRevision++;cancel.Cancel();cancel.Dispose();cancel=new CancellationTokenSource();View.busy=false;exitDue=DateTime.MaxValue;exitAttempt=0;}
    internal void SetKey(string value){if(!ValidKey(value))throw new InvalidOperationException();LeagueJson.AtomicBytes(keyPath,ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser));Reset();View.source="api";key=value;authBlocked=false;View.hasKey=true;View.state=View.connected?"restoring":"disconnected";View.message="API 키를 암호화 저장했어요. Riot ID로 연결해 주세요.";cooldown=DateTime.MinValue;due=DateTime.MinValue;Notify();}
    internal Task Connect(string id,string platform){
        id=(id??"").Trim();if(!RiotApiProvider.ValidId(id)||!RiotApiProvider.ValidPlatform(platform)){View.message="Riot ID(GameName#TagLine)와 서버를 확인해 주세요.";Notify();return Task.FromResult(0);}
        return Fetch(new LeagueIdentity{riotId=id,platform=platform},true,true);
    }
    internal void UseIplol(){Reset();View.source="iplol";authBlocked=false;View.data=null;View.snapshots=new LeagueSnapshot[0];cooldown=DateTime.MinValue;due=DateTime.MinValue;Notify();}
    internal Task Refresh(){return Fetch(identity,false,true);}
    async Task Fetch(LeagueIdentity target,bool connecting,bool manual){
        if(verify||disposed||View.busy||target==null)return;
        if((View.source=="api"&&!View.hasKey)||authBlocked){View.state=authBlocked?"siteBlocked":"key";View.message=authBlocked?"데이터 조회 실패 · 선택한 Provider의 자동 조회가 중단됐어요.":"공식 API 키를 등록해 주세요.";Notify();return;}
        if(Now()<cooldown){if(manual){View.message="요청 간격 또는 Riot 호출 제한으로 잠시 기다려 주세요.";Notify();}return;}
        int run=revision;var token=cancel.Token;cooldown=Now().AddSeconds(View.source=="iplol"?60:30);due=Now().AddMinutes(View.source=="iplol"?30:15);View.busy=true;View.nextRefreshAt=due.ToString("o");View.message=View.source=="iplol"?"아이피롤에서 공개 전적을 확인하고 있어요.":"공식 Riot API에서 기록을 확인하고 있어요.";Notify();
        try{
            var result=await (View.source=="iplol"?website:provider).Read(View.source=="iplol"?null:key,target,connecting?null:View.data,token);if(run!=revision||disposed)return;
            if(result==null||result.identity==null||string.IsNullOrEmpty(result.identity.puuid)||result.data==null)throw LeagueJson.Bad();
            LeagueJson.Atomic(accountPath,json.Serialize(new LeagueStored{identity=result.identity,data=result.data,source=View.source}));
            identity=result.identity;View.riotId=identity.riotId;View.platform=identity.platform;View.data=result.data;View.connected=true;View.stale=false;View.state="connected";View.message=View.source=="iplol"?"아이피롤 저장 기록을 불러왔어요. 원본 갱신 시각을 확인해 주세요.":"공식 API의 최신 기록이에요.";
            if(connecting){exitRevision++;exitDue=DateTime.MaxValue;exitAttempt=0;}
            try{using(var db=new LeagueRankStore(dbPath)){db.Append(identity,result.data,Now());View.snapshots=db.Read(identity);}View.snapshotMessage=null;}catch{View.snapshotMessage="현재 기록은 갱신했지만 랭크 스냅샷을 저장하지 못했어요.";}
        }catch(OperationCanceledException){}
        catch(LeagueFailure ex){if(run!=revision||disposed)return;View.state=ex.State;View.message=ex.Message;View.stale=View.data!=null;authBlocked=ex.State=="key"||ex.State=="siteBlocked";cooldown=Now().AddSeconds(Math.Max(30,ex.WaitSeconds));due=cooldown;if(authBlocked)exitDue=DateTime.MaxValue;}
        catch{if(run!=revision||disposed)return;View.state="storage";View.message="응답 처리 또는 저장을 완료하지 못했어요. 마지막 정상 기록을 유지해요.";View.stale=View.data!=null;}
        finally{if(run==revision&&!disposed){View.busy=false;View.nextRefreshAt=due.ToString("o");Notify();}}
    }
    void ReadSnapshots(){try{using(var db=new LeagueRankStore(dbPath))View.snapshots=db.Read(identity);}catch{View.snapshotMessage="랭크 스냅샷을 읽지 못했어요.";}}
    internal void GameExited(){if(!View.connected||authBlocked)return;exitRevision++;exitAttempt=0;exitIds=new HashSet<string>(View.data==null?new string[0]:View.data.matches.Select(m=>m.id));exitDue=Now().AddSeconds(30);View.message="경기 종료를 감지했어요. 30초 후 새 기록을 확인해요.";Notify();}
    internal async Task Tick(){
        if(verify||disposed||!View.connected||View.busy||authBlocked||(View.source=="api"&&!View.hasKey))return;
        if(Now()>=exitDue&&Now()>=cooldown){
            int run=exitRevision;await Fetch(identity,false,false);if(disposed||run!=exitRevision)return;
            bool newer=View.data!=null&&View.data.matches.Any(m=>!exitIds.Contains(m.id));exitAttempt++;
            exitDue=newer||exitAttempt>=3||authBlocked?DateTime.MaxValue:Now().AddSeconds(exitAttempt==1?90:180);
            if(!newer&&exitAttempt>=3&&View.state=="connected")View.message="새 경기는 아직 반영되지 않았어요. 다음 정기 갱신에서 다시 확인해요.";Notify();
        }else if(exitDue==DateTime.MaxValue&&Now()>=due)await Fetch(identity,false,false);
    }
    internal void Disconnect(){
        Reset();foreach(string path in new[]{accountPath,keyPath,dbPath})if(File.Exists(path))File.Delete(path);
        identity=null;key=null;authBlocked=false;cooldown=DateTime.MinValue;View=new LeagueView{message="League 계정·키·랭크 기록을 삭제했어요."};Notify();
    }
    internal void SetFixture(LeagueView value){if(!verify)throw new InvalidOperationException();View=value;Notify();}
    public void Dispose(){disposed=true;cancel.Cancel();cancel.Dispose();provider.Dispose();website.Dispose();}
}
internal sealed class LeagueKeyDialog:Form {
    readonly TextBox input=new TextBox{UseSystemPasswordChar=true,Dock=DockStyle.Fill,MaxLength=256};
    internal string KeyValue {get{return input.Text.Trim();}}
    internal LeagueKeyDialog(){
        Text="League of Legends · Riot API 키 등록";AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("맑은 고딕",10);StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(540,215);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=3};
        panel.Controls.Add(new Label{Text="Riot Developer Portal의 RGAPI-로 시작하는 키를 입력하세요.\n이 Windows 계정에만 암호화 저장하며 화면·로그에 표시하지 않아요.\n개발용 키는 24시간 후 만료되므로 다시 등록해야 해요.",AutoSize=true,Dock=DockStyle.Fill});panel.Controls.Add(input);
        var save=new Button{Text="암호화 저장",AutoSize=true,Padding=new Padding(10,4,10,4)};save.Click+=delegate{if(LeagueService.ValidKey(KeyValue)){DialogResult=DialogResult.OK;Close();}else MessageBox.Show(this,"RGAPI-로 시작하는 API 키를 공백 없이 입력해 주세요.");};panel.Controls.Add(save);Controls.Add(panel);AcceptButton=save;
    }
    protected override void Dispose(bool disposing){if(disposing)input.Clear();base.Dispose(disposing);}
}
