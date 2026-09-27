using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// No reference to existing KoruDesk credentials/settings. Host owns all native operations.
internal sealed class UserSettings {
    public string brand="Nyang Gaming", theme="pink";
    public int brandingVersion=0;
    public bool demo=true;
    public OverlaySettings overlay=new OverlaySettings();
    public double artworkOpacity=.65;
    public Dictionary<string,string> paths=new Dictionary<string,string>();
    public Dictionary<string,string> artwork=new Dictionary<string,string>();
    public Dictionary<string,double> seconds=new Dictionary<string,double>();
}
internal sealed class GameState {
    public string id,name; public bool running; public double today,week,session;
    public string clientState="stopped";
    internal string[] processes;
    internal IntPtr window;
    internal GameState(string key,string label,params string[] names){id=key;name=label;processes=names;}
}
internal sealed class Metrics {
    public double? cpu,ram,gpu,temperature,power;
    public string sampledAt;
}
// Account and frame providers intentionally return unavailable. No API keys / injection / capture.
internal interface IAccountProvider { object Read(string game); }
internal interface IFrameProvider { object Read(string game); }
internal sealed class PendingProvider:IAccountProvider,IFrameProvider {
    public object Read(string game){return new {source="unavailable",status="아직 연결하지 않았어요"};}
}
internal sealed class GamingService {
    internal readonly string Root, Assets;
    internal UserSettings Settings;
    internal readonly GameState[] Games={new GameState("genshin","원신","GenshinImpact","YuanShen"),new GameState("eternal","이터널 리턴","EternalReturn","BlackSurvival"),new GameState("valorant","VALORANT","VALORANT-Win64-Shipping"),new GameState("league","League of Legends","League of Legends")};
    internal readonly RiotService Riot;
    internal readonly FriendsService Friends;
    internal readonly AppUpdateService Updates;
    internal Metrics Metrics=new Metrics();
    internal readonly GenshinService Genshin;
    internal readonly EternalReturnService Eternal;
    internal readonly CharacterImageCache CharacterImages;
    internal string OverlayStatus="";
    internal bool AutoStartEnabled;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    DateTime last=DateTime.Now, saveDue=DateTime.MinValue;
    readonly PendingProvider pending=new PendingProvider();
    internal GamingService(bool verify){
        AutoStartEnabled=!verify&&StartupRegistration.Enabled();
        Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),UpdateBuild.Testing?"NyangGaming_UpdateTest":"KoruGaming_Next",verify&&!UpdateBuild.Testing?"VerificationData":"UserData");
        Assets=Path.Combine(Root,"Artwork");Directory.CreateDirectory(Assets);Updates=new AppUpdateService(Root,verify&&!UpdateBuild.Testing);
        Genshin=new GenshinService(Root,verify);
        Friends=new FriendsService(Root,verify);Eternal=new EternalReturnService(Root,verify);Riot=new RiotService(Root,verify);
        CharacterImages=new CharacterImageCache(Root);Genshin.SetImageCache(CharacterImages);
        try{Settings=json.Deserialize<UserSettings>(File.ReadAllText(Path.Combine(Root,"settings.json")));}catch{Settings=new UserSettings();}
        if(Settings==null)Settings=new UserSettings();
        if(Settings.overlay==null)Settings.overlay=new OverlaySettings();
        if(!HudTheme.Valid(Settings.overlay.design))Settings.overlay.design="pink-capsule";
        if(!OverlayHotkey.Valid(Settings.overlay.hotkey))Settings.overlay.hotkey="G";
        Settings.paths=Settings.paths??new Dictionary<string,string>();Settings.artwork=Settings.artwork??new Dictionary<string,string>();Settings.seconds=Settings.seconds??new Dictionary<string,double>();
        if(Settings.brandingVersion<1){
            string settingsFile=Path.Combine(Root,"settings.json"),backup=settingsFile+".before-nyang";
            if(File.Exists(settingsFile)&&!File.Exists(backup))File.Copy(settingsFile,backup);
            Settings.brand="Nyang Gaming";Settings.brandingVersion=1;
            Settings.artwork.Remove("home-background");Settings.artwork.Remove("home-character");
        }
        if(Settings.theme!="pink")Settings.theme="default";
        Settings.artworkOpacity=Math.Max(0,Math.Min(1,Settings.artworkOpacity));
    }
    internal void Save(){
        string file=Path.Combine(Root,"settings.json"), temp=file+".tmp";
        File.WriteAllText(temp,json.Serialize(Settings));
        if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
    }
    internal void Tick(){
        DateTime now=DateTime.Now;
        double elapsed=(now-last).TotalSeconds;
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var g in Games)g.window=IntPtr.Zero;
        foreach(var p in Process.GetProcesses()){using(p){try{names.Add(p.ProcessName);var g=Games.FirstOrDefault(x=>x.processes.Contains(p.ProcessName,StringComparer.OrdinalIgnoreCase));if(g!=null&&p.MainWindowHandle!=IntPtr.Zero)g.window=p.MainWindowHandle;}catch{}}}
        ApplyProcessSample(names,now,elapsed);
        last=now;
        if(now>=saveDue){Save();saveDue=now.AddMinutes(1);}
    }
    // One shared clock for all games; deterministic seam for detection/accounting tests.
    internal void ApplyProcessSample(HashSet<string> names,DateTime now,double elapsed){
        foreach(var g in Games){
            bool running=g.processes.Any(n=>names.Contains(n));
            // Ignore sleep/stalls, split midnight, never count a launcher as the game.
            if(running&&g.running&&elapsed>0&&elapsed<=10){
                g.session+=elapsed;
                DateTime cursor=now.AddSeconds(-elapsed);
                while(cursor<now){DateTime end=cursor.Date.AddDays(1);if(end>now)end=now;string k=g.id+"/"+cursor.ToString("yyyy-MM-dd");double v;Settings.seconds.TryGetValue(k,out v);Settings.seconds[k]=v+(end-cursor).TotalSeconds;cursor=end;}
            }
            if(g.id=="eternal"&&g.running&&!running)Eternal.GameExited();if(g.id=="league"&&g.running&&!running)Riot.League.GameExited();if(g.id=="valorant"&&g.running&&!running)Riot.Valorant.GameExited();
            if(!running||!g.running)g.session=0;
            g.clientState=RiotService.ClientState(g.id,running,names);
            g.running=running;Settings.seconds.TryGetValue(g.id+"/"+now.ToString("yyyy-MM-dd"),out g.today);g.week=0;
            for(int i=0;i<7;i++){double v;Settings.seconds.TryGetValue(g.id+"/"+now.AddDays(-i).ToString("yyyy-MM-dd"),out v);g.week+=v;}
        }
    }
    internal object Snapshot(){return new {settings=Settings,updates=Updates.View,autoStartEnabled=AutoStartEnabled,games=Games,metrics=Metrics,accounts=new {genshin=Genshin.View,eternal=Eternal.View},riot=Riot.Snapshot(),friends=Friends.View,chat=Friends.Chat.View,characters=Genshin.Characters,overlayStatus=OverlayStatus,frames=pending.Read("frames")};}
    internal async Task Sample(){Metrics=await Task.Run(()=>ReadMetrics());}
    static double? Number(string text){double n;return double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&!double.IsNaN(n)&&!double.IsInfinity(n)?(double?)n:null;}
    static Metrics ReadMetrics(){
        var m=new Metrics{sampledAt=DateTime.Now.ToString("HH:mm:ss")};
        try{using(var q=new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor"))using(var rows=q.Get())foreach(ManagementObject r in rows){m.cpu=Convert.ToDouble(r["LoadPercentage"]);break;}}catch{}
        try{using(var q=new ManagementObjectSearcher("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem"))using(var rows=q.Get())foreach(ManagementObject r in rows){double t=Convert.ToDouble(r["TotalVisibleMemorySize"]);if(t>0)m.ram=100*(t-Convert.ToDouble(r["FreePhysicalMemory"]))/t;break;}}catch{}
        string smi=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"nvidia-smi.exe");
        if(!File.Exists(smi))smi=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation","NVSMI","nvidia-smi.exe");
        if(File.Exists(smi))try{
            using(var p=Process.Start(new ProcessStartInfo(smi,"--query-gpu=utilization.gpu,temperature.gpu,power.draw --format=csv,noheader,nounits"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){
                var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
                if(p.WaitForExit(2500)){string[] v=output.Result.Split('\n')[0].Split(',');if(v.Length>=3){m.gpu=Number(v[0]);m.temperature=Number(v[1]);m.power=Number(v[2]);}}
                else{try{p.Kill();}catch{}}
            }
        }catch{}
        return m;
    }
}
