using System;using System.IO;using System.Text;using System.Text.RegularExpressions;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;using Velopack;using Velopack.Sources;

internal sealed class UpdateView {
 public string currentVersion=UpdateBuild.Version,newVersion,notes="",state="idle",message="업데이트를 확인할 수 있어요.";
 public bool automatic=true,busy,dialog,installed;public int progress;
}
internal sealed class UpdatePreferences {public bool automatic=true;}
internal interface IAppUpdateBackend {
 bool Installed {get;} VelopackAsset Pending {get;}
 Task<UpdateInfo> Check();Task Download(UpdateInfo update,Action<int> progress,CancellationToken token);void Apply(VelopackAsset asset);
}
internal sealed class VeloUpdateBackend:IAppUpdateBackend {
 readonly UpdateManager manager;
 internal VeloUpdateBackend(string source,bool localTest=false){manager=localTest?new UpdateManager(source):new UpdateManager(new GithubSource(source,null,false));}
 public bool Installed {get{return manager.IsInstalled&&!manager.IsPortable;}}
 public VelopackAsset Pending {get{return manager.UpdatePendingRestart;}}
 public Task<UpdateInfo> Check(){return manager.CheckForUpdatesAsync();}
 public Task Download(UpdateInfo update,Action<int> progress,CancellationToken token){return manager.DownloadUpdatesAsync(update,progress,token);}
 public void Apply(VelopackAsset asset){manager.WaitExitThenApplyUpdates(asset,false,true,UpdateBuild.Testing?new[]{"--update-smoke"}:new string[0]);}
}
internal sealed class AppUpdateService:IDisposable {
 internal UpdateView View=new UpdateView();internal event Action Changed;
 readonly string prefs;readonly IAppUpdateBackend backend;readonly CancellationTokenSource lifetime=new CancellationTokenSource();
 readonly JavaScriptSerializer json=new JavaScriptSerializer();UpdateInfo available;VelopackAsset downloaded;bool disposed,started;
 internal static bool ValidRepository(string value){return Regex.IsMatch(value??"",@"^https://github\.com/[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+$")&&!value.EndsWith("/.")&&!value.EndsWith("/..");}
 internal AppUpdateService(string root,bool verify=false,IAppUpdateBackend injected=null){
  prefs=Path.Combine(root,"update-preferences.json");try{var saved=json.Deserialize<UpdatePreferences>(File.ReadAllText(prefs));if(saved!=null)View.automatic=saved.automatic;}catch{}
  if(verify&&injected==null){View.message="업데이트 화면 검증 모드";return;}
  try{backend=injected??(UpdateBuild.Testing?new VeloUpdateBackend(UpdateBuild.TestSource,true):ValidRepository(UpdateBuild.Repository)?new VeloUpdateBackend(UpdateBuild.Repository):null);
   if(backend==null){View.state="unconfigured";View.message="업데이트 배포 저장소가 아직 연결되지 않았어요.";return;}
   View.installed=backend.Installed;if(!View.installed){View.state="portable";View.message="자동 업데이트를 사용하려면 설치형 Setup을 처음 한 번 실행해 주세요. 기존 사용자 데이터는 유지돼요.";return;}
   downloaded=backend.Pending;if(downloaded!=null){SetRelease(downloaded);View.state="ready";View.progress=100;View.message="업데이트 준비 완료 · 재시작하면 적용돼요.";}
  }catch{View.state="error";View.message="업데이트 초기화에 실패했어요. 현재 버전은 계속 사용할 수 있어요.";}
 }
 void Notify(){if(!disposed&&Changed!=null)Changed();}
 internal void SetAutomatic(bool enabled){Directory.CreateDirectory(Path.GetDirectoryName(prefs));LeagueJson.AtomicBytes(prefs,Encoding.UTF8.GetBytes(json.Serialize(new UpdatePreferences{automatic=enabled})));View.automatic=enabled;Notify();}
 internal async Task Startup(){if(started||disposed)return;started=true;if(View.automatic){await Task.Delay(1800,lifetime.Token);if(!disposed)await Check(false);}}
 void SetRelease(VelopackAsset asset){View.newVersion=asset.Version.ToString();var text=asset.NotesMarkdown??"";View.notes=text.Length>16000?text.Substring(0,16000)+"…":text;}
 internal async Task Check(bool manual){
  if(disposed||View.busy)return;if(backend==null||!View.installed){if(manual)View.dialog=true;Notify();return;}
  if(downloaded!=null){View.state="ready";View.dialog=manual||View.automatic;Notify();return;}
  View.busy=true;View.state="checking";View.message="새 버전을 확인하고 있어요.";Notify();
  try{available=await Task.Run(()=>backend.Check());if(disposed)return;
   if(available==null){View.newVersion=null;View.notes="";View.state="current";View.message="현재 최신 버전을 사용하고 있습니다. v"+View.currentVersion;View.dialog=manual;}
   else{if(available.IsDowngrade||available.TargetFullRelease.Version<=Velopack.SemanticVersion.Parse(View.currentVersion))throw new InvalidDataException();SetRelease(available.TargetFullRelease);View.state="available";View.message="새로운 업데이트가 있어요.";View.dialog=true;}
  }catch{if(!disposed){View.state="error";View.message="업데이트 확인에 실패했어요. 인터넷 또는 GitHub 상태를 확인해 주세요. 현재 버전은 계속 사용할 수 있어요.";View.dialog=manual;}}
  finally{View.busy=false;Notify();}
 }
 internal async Task Download(){
  if(disposed||View.busy||available==null||downloaded!=null)return;View.busy=true;View.dialog=true;View.state="downloading";View.progress=0;View.message="업데이트 다운로드 중 · 완료 후 파일을 검증해요.";Notify();
  try{await backend.Download(available,p=>{View.progress=Math.Max(0,Math.Min(99,p));Notify();},lifetime.Token);if(disposed)return;downloaded=available.TargetFullRelease;View.progress=100;View.state="ready";View.message="업데이트 준비 완료 · 재시작하면 적용돼요.";}
  catch{if(!disposed){View.state="downloadError";View.message="다운로드 또는 파일 검증에 실패했어요. 현재 버전과 사용자 데이터는 유지됩니다. 다시 시도할 수 있어요.";}}
  finally{View.busy=false;Notify();}
 }
 internal void Dismiss(){if(View.state=="applying")return;View.dialog=false;Notify();}
 internal bool BeginApply(){if(disposed||View.busy||downloaded==null||View.state!="ready")return false;try{View.state="applying";View.busy=true;View.message="프로그램을 종료한 뒤 업데이트하고 다시 실행해요.";Notify();backend.Apply(downloaded);return true;}catch{View.busy=false;View.state="ready";View.message="업데이트 실행에 실패했어요. 현재 버전은 계속 사용할 수 있어요. 다시 시도해 주세요.";Notify();return false;}}
 internal void ApplyPreparationFailed(){View.message="설정 저장을 완료하지 못해 업데이트를 중단했어요. 현재 버전을 계속 사용할 수 있어요.";View.dialog=true;Notify();}
 internal void Fixture(UpdateView view){View=view;Notify();}
 public void Dispose(){disposed=true;lifetime.Cancel();lifetime.Dispose();}
}
