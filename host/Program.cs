using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

static class Program {
    internal static bool SocialVerify,UpdateSmoke;internal static EventWaitHandle OpenRequest; internal static bool OpenFriends;internal static string PendingChatRoom;
    internal static string ValkingSetupId; internal static string IplolSetupId; internal static string LeagueSetupId,LeagueSetupPlatform,DakSetupName;
    [STAThread] static void Main(string[] args){
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).SetAppUserModelId(UpdateBuild.Testing?"NyangGaming.UpdateTest":"NyangGaming.Messenger").Run();
        UpdateSmoke=UpdateBuild.Testing&&Array.IndexOf(args,"--update-smoke")>=0;
        if(args.Length==2&&args[0]=="--update-selftest"){UpdateTests.Run(args[1]).GetAwaiter().GetResult();return;}
        System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;
        if(args.Length==3&&args[0]=="--valking-check"){ValorantTests.Run(args[1],args[2]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--valking-setup"&&RiotApiProvider.ValidId(args[1]))ValkingSetupId=args[1];
        if(args.Length==2&&args[0]=="--friends-live"){FriendsTests.Live(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==3&&args[0]=="--messenger-retry-check"){MessengerTests.RetryCheck(args[1],args[2]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--messenger-live"){MessengerTests.Live(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--notification-test"){MessengerTests.NotificationCheck(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==3&&args[0]=="--provider-selftest"){ProviderPolicyTests.Run(args[1],args[2]).GetAwaiter().GetResult();return;}
        if(args.Length==3&&args[0]=="--hippy-check"){ProviderPolicyTests.Live(args[1],args[2]).GetAwaiter().GetResult();return;}
        if(args.Length==3&&args[0]=="--social-live"){SocialTests.Live(args[1],args[2]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--social-selftest"){SocialTests.Run(args[1]);return;}
        if(args.Length==2&&args[0]=="--friends-selftest"){FriendsTests.Run(args[1]);return;}
        if(args.Length==2&&args[0]=="--hoyo-selftest"){HoyoTests.Run(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--riot-selftest"){RiotTests.Run(args[1]);return;}
        if(args.Length==2&&args[0]=="--dak-selftest"){EternalDakTests.Run(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==3&&args[0]=="--dak-check"){EternalDakTests.Live(args[1],args[2]).GetAwaiter().GetResult();return;}
        if(args.Length==3&&args[0]=="--craft-check"){EternalCraftTests.Run(args[1],args[2]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--er-selftest"){EternalReturnTests.Run(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--characters-check"){CharacterCheckRunner.Run(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==3&&args[0]=="--iplol-check"){LeagueIplolTests.Run(args[1],args[2]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--league-selftest"){LeagueTests.Run(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==3&&args[0]=="--league-setup"&&RiotApiProvider.ValidId(args[1])&&RiotApiProvider.ValidPlatform(args[2])){LeagueSetupId=args[1];LeagueSetupPlatform=args[2];}
        if(args.Length==2&&args[0]=="--ercraft-setup")DakSetupName=EternalCraftProvider.Normalize(args[1]);
        if(args.Length==2&&args[0]=="--iplol-setup"&&RiotApiProvider.ValidId(args[1]))IplolSetupId=args[1];
        if(args.Length==2&&args[0]=="--chat-room")PendingChatRoom=ChatNotifications.Parse(args[1]);OpenFriends=Array.IndexOf(args,"--friends")>=0;SocialVerify=Array.IndexOf(args,"--social-verify")>=0;bool verify=SocialVerify||Array.IndexOf(args,"--verify")>=0,created;
        using(var mutex=new Mutex(true,"Local\\KoruGaming_Next"+(verify?".Verify":UpdateBuild.Testing?".UpdateTest":""),out created))
        using(var open=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\NyangGaming.Open"+(verify?".Verify":UpdateBuild.Testing?".UpdateTest":""))){
            if(!created){if(PendingChatRoom!=null)ChatNotifications.Queue(PendingChatRoom);if(Array.IndexOf(args,"--tray")<0)open.Set();return;}
            OpenRequest=open;if(!verify&&!UpdateBuild.Testing)StartupRegistration.RebindInstalledPath();
            if(!verify&&!UpdateBuild.Testing)try{ChatNotifications.StartActivator();}catch(Exception ex){File.WriteAllText(Path.Combine(Path.GetTempPath(),"NyangGaming-notification-error.txt"),ex.ToString());}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new GamingWindow(verify,Array.IndexOf(args,"--tray")>=0||Array.IndexOf(args,"--toast-activated")>=0));if(!verify)ChatNotifications.StopActivator();
        }
    }
}
static class StartupRegistration {
    const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run", Name="NyangGaming";
    internal static string Command {get{return "\""+Application.ExecutablePath+"\" --tray";}}
    internal static void RebindInstalledPath(){try{if(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sq.version")))return;using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key,true)){string command=key==null?null:key.GetValue(Name) as string;if(string.IsNullOrEmpty(command))return;var match=System.Text.RegularExpressions.Regex.Match(command,@"^""([^""]+)"" --tray$");if(!match.Success)return;string file=Path.GetFileName(match.Groups[1].Value);if(new[]{"NyangGaming_Riot.exe","NyangGaming_Messenger.exe","KoruGaming_Next.exe","NyangGaming.exe"}.Contains(file,StringComparer.OrdinalIgnoreCase))key.SetValue(Name,Command,Microsoft.Win32.RegistryValueKind.String);}}catch{}}
    internal static bool Enabled(){try{using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key)){return key!=null&&string.Equals(key.GetValue(Name) as string,Command,StringComparison.OrdinalIgnoreCase);}}catch{return false;}}
    internal static void Set(bool enabled){using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key)){if(enabled)key.SetValue(Name,Command,Microsoft.Win32.RegistryValueKind.String);else key.DeleteValue(Name,false);}if(Enabled()!=enabled)throw new IOException("Startup registration failed");}
}
static class CharacterCheckRunner {
    internal static async Task Run(string path){
        var s=new GamingService(false);try{
            await s.Genshin.Tick();await s.Genshin.LoadCharacters(false);
            var c=s.Genshin.Characters;var report=new System.Collections.Generic.List<string>{"accountState="+s.Genshin.View.state,"characterState="+c.state,"count="+c.items.Count,"icons="+c.items.Count(x=>!string.IsNullOrEmpty(x.icon)),"cdnHosts="+string.Join(",",CharacterImageCache.ObservedHosts)};
            if(c.items.Count>0){await s.Genshin.LoadCharacters(false,c.items[0].id);var d=c.detail;report.Add("detail="+(d!=null));if(d!=null){report.Add("stats="+d.stats.Count);report.Add("artifacts="+d.artifacts.Count);report.Add("weapon="+(d.weapon!=null));}}
            File.WriteAllLines(path,report);
        }finally{s.Genshin.Dispose();}
    }
}
sealed partial class GamingWindow:Form {
    const string Origin="https://korugaming.example/index.html";
    readonly WebView2 web=new WebView2();readonly bool verify;readonly GamingService service;
    readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=3000000};
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=3000};
    bool sampling,ready,shutdownReady;DateTime sampleDue=DateTime.MinValue;
    HoyoLoginWindow login;
    GamingOverlay overlay;OverlayHotkey hotkey;
    NotifyIcon tray;ContextMenuStrip trayMenu;bool quitting;

    void OpenChat(){ShowMain();if(ready)web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{version=1,type="navigate",page="chat"}));}
    void ChatNotification(string room,string title,string body){if(IsDisposed)return;try{ChatNotifications.Show(room,title.Length>60?title.Substring(0,60):title,body.Length>180?body.Substring(0,180)+"…":body);}catch{service.Friends.Chat.View.message="메시지는 도착했지만 Windows 알림을 표시하지 못했어요.";Send();}}
    internal GamingWindow(bool test,bool startInTray=false){
        verify=test;service=new GamingService(test||UpdateBuild.Testing);Text=service.Settings.brand;ApplyBrandIcon();
        service.Friends.FriendArrived+=(id,name)=>{if(verify)return;try{ChatNotifications.ShowFriend(id,name);}catch{Toast(name+"님이 접속했어요.");}};
        service.Friends.Changed+=Send;service.Genshin.Changed+=Send;service.Eternal.Changed+=Send;service.Riot.League.Changed+=Send;service.Riot.Valorant.Changed+=Send;
        service.Friends.Chat.Changed+=Send;service.Friends.Chat.Navigate+=OpenChat;service.Friends.Chat.Notification+=ChatNotification;
        AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(1280,850);MinimumSize=new Size(560,480);StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(29,29,36);web.DefaultBackgroundColor=BackColor;web.Dock=DockStyle.Fill;Controls.Add(web);
        if(!verify)CreateTray();
        Shown+=async delegate{if(startInTray&&!verify)HideToTray();await Initialize();};
        service.Updates.Changed+=UpdateChanged;
        FormClosing+=async delegate(object sender,FormClosingEventArgs e){if(!verify&&!quitting&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;HideToTray();return;}if(!verify&&quitting&&!shutdownReady&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;timer.Stop();await service.Friends.Offline();shutdownReady=true;Close();}};
        timer.Tick+=async delegate{try{if(!verify&&Program.OpenRequest!=null&&Program.OpenRequest.WaitOne(0)){var requested=ChatNotifications.Take();if(requested=="friends")Program.OpenFriends=true;else if(requested!=null)Program.PendingChatRoom=requested;ShowMain();if(ready&&Program.OpenFriends){Program.OpenFriends=false;web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{version=1,type="navigate",page="friends"}));}}if(!verify&&ready&&Program.PendingChatRoom!=null&&service.Friends.ChatUser!=""){string requested=Program.PendingChatRoom;Program.PendingChatRoom=null;await service.Friends.Chat.Open(requested);}service.Tick();if(overlay!=null)overlay.Tick();await Task.WhenAll(service.Genshin.Tick(),service.Eternal.Tick(),service.Riot.League.Tick(),service.Riot.Valorant.Tick(),service.Friends.Tick(service.Games,Visible&&WindowState!=FormWindowState.Minimized),service.Friends.Chat.Tick(Visible&&WindowState!=FormWindowState.Minimized&&ContainsFocus));if((Visible&&WindowState!=FormWindowState.Minimized)||(overlay!=null&&overlay.IsShowing)){Send();if(!sampling&&DateTime.UtcNow>=sampleDue){sampling=true;sampleDue=DateTime.UtcNow.AddSeconds(10);try{await service.Sample();Send();if(overlay!=null)overlay.Render();}finally{sampling=false;}}}}catch{Toast("설정을 저장하지 못했어요. 저장 폴더 권한을 확인해 주세요.");}};
        FormClosed+=delegate{service.Updates.Changed-=UpdateChanged;service.Updates.Dispose();timer.Stop();timer.Dispose();if(hotkey!=null)hotkey.Dispose();if(overlay!=null)overlay.Dispose();if(login!=null&&!login.IsDisposed)login.Close();service.Genshin.Changed-=Send;service.Genshin.Dispose();service.Eternal.Changed-=Send;service.Eternal.Dispose();service.Riot.League.Changed-=Send;service.Riot.League.Dispose();service.Riot.Valorant.Changed-=Send;service.Riot.Valorant.Dispose();service.Friends.Changed-=Send;service.Friends.Dispose();try{service.Save();}catch{}};
        FormClosed+=delegate{if(tray!=null){tray.Visible=false;tray.Dispose();}if(trayMenu!=null)trayMenu.Dispose();if(ownedBrandIcon!=null)ownedBrandIcon.Dispose();};
    }
    void UpdateChanged(){if(IsDisposed||!IsHandleCreated)return;if(InvokeRequired){try{BeginInvoke((Action)UpdateChanged);}catch{}return;}Send();}
    async void StartUpdateCheck(){try{if(Program.UpdateSmoke){await SmokeUpdate();return;}await service.Updates.Startup();}catch(OperationCanceledException){}catch{}}
    async Task ApplyUpdate(){if(service.Updates.View.state!="ready"||service.Updates.View.busy)return;timer.Stop();try{service.Tick();service.Save();if(!service.Updates.BeginApply()){timer.Start();return;}}catch{service.Updates.ApplyPreparationFailed();timer.Start();return;}try{await service.Friends.Offline();}catch{}shutdownReady=true;quitting=true;Close();}
    async Task SmokeUpdate(){
        string log=Path.Combine(service.Root,"update-smoke-"+UpdateBuild.Version+".txt");try{
            web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{version=1,type="navigate",page="settings"}));await Task.Delay(500);
            if(UpdateBuild.Version=="1.0.1"){using(var db=new LeagueRankStore(Path.Combine(service.Root,"league-ranks.sqlite"))){if(db.Read(new LeagueIdentity{puuid="update-test",platform="KR"}).Length!=1)throw new Exception("SQLite rows lost");}File.WriteAllText(log,"PASS restarted version="+UpdateBuild.Version+" frontend="+await web.CoreWebView2.ExecuteScriptAsync("window.__NYANG_VERSION")+" theme="+service.Settings.theme);return;}
            await service.Updates.Check(true);await Task.Delay(500);File.WriteAllText(log,"detected="+service.Updates.View.state+" version="+service.Updates.View.newVersion);
            using(var f=File.Create(Path.Combine(service.Root,"update-dialog.png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
            await service.Updates.Download();File.AppendAllText(log," downloaded="+service.Updates.View.state+" progress="+service.Updates.View.progress);await Task.Delay(500);await ApplyUpdate();
        }catch(Exception e){File.AppendAllText(log," FAIL "+e);}}
    void CreateTray(){
        trayMenu=new ContextMenuStrip();trayMenu.Items.Add("Nyang Gaming 열기",null,delegate{ShowMain();});trayMenu.Items.Add(new ToolStripSeparator());trayMenu.Items.Add("완전히 종료",null,delegate{quitting=true;Close();});
        tray=new NotifyIcon{Icon=Icon??SystemIcons.Application,Text=service.Settings.brand,ContextMenuStrip=trayMenu,Visible=true};tray.DoubleClick+=delegate{ShowMain();};

    }
    // Keep the native handle stable: changing ShowInTaskbar recreates it and can orphan WebView2 windows.
    void HideToTray(){Hide();}
    void ShowMain(){if(IsDisposed)return;Show();if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;Activate();sampleDue=DateTime.MinValue;Send();}
    Icon ownedBrandIcon;
    void ApplyBrandIcon(){
        string url;service.Settings.artwork.TryGetValue("app-icon",out url);
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui","nyang-icon.png");
        Uri uri;if(Uri.TryCreate(url,UriKind.Absolute,out uri)&&uri.Host=="assets.korugaming.example"){
            string name=Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            if(name==Path.GetFileName(name)&&File.Exists(Path.Combine(service.Assets,name)))path=Path.Combine(service.Assets,name);
        }
        try{using(var image=Image.FromFile(path))using(var bitmap=new Bitmap(image,new Size(64,64))){
            IntPtr handle=bitmap.GetHicon();try{using(var borrowed=Icon.FromHandle(handle)){var old=ownedBrandIcon;ownedBrandIcon=(Icon)borrowed.Clone();Icon=ownedBrandIcon;if(tray!=null)tray.Icon=ownedBrandIcon;if(old!=null)old.Dispose();}}finally{DestroyIcon(handle);}
        }}catch{} 
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
    async Task Initialize(){try{
        var area=Screen.FromControl(this).WorkingArea;Size=new Size(Math.Min(Width,area.Width-40),Math.Min(Height,area.Height-40));CenterToScreen();
        var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(service.Root,"WebView2"));await web.EnsureCoreWebView2Async(env);
        var c=web.CoreWebView2;
        c.SetVirtualHostNameToFolderMapping("korugaming.example",Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui"),CoreWebView2HostResourceAccessKind.DenyCors);
        c.SetVirtualHostNameToFolderMapping("assets.korugaming.example",service.Assets,CoreWebView2HostResourceAccessKind.DenyCors);
        c.SetVirtualHostNameToFolderMapping("characters.korugaming.example",service.CharacterImages.Folder,CoreWebView2HostResourceAccessKind.DenyCors);
        if(!verify){overlay=new GamingOverlay(service,OpenFromOverlay);hotkey=new OverlayHotkey(overlay.Toggle);hotkey.Bind(service.Settings.overlay.hotkey);service.OverlayStatus=hotkey.Message;}
        c.SetVirtualHostNameToFolderMapping("league.korugaming.example",service.Riot.League.ImageFolder,CoreWebView2HostResourceAccessKind.DenyCors);
        c.Settings.AreDefaultContextMenusEnabled=false;c.Settings.AreDevToolsEnabled=verify;c.Settings.IsStatusBarEnabled=false;c.Settings.IsZoomControlEnabled=false;c.Settings.AreBrowserAcceleratorKeysEnabled=false;
        c.NavigationStarting+=delegate(object s,CoreWebView2NavigationStartingEventArgs e){if(e.Uri!=Origin)e.Cancel=true;};
        c.NewWindowRequested+=delegate(object s,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;};
        c.DownloadStarting+=delegate(object s,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;};
        c.PermissionRequested+=delegate(object s,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};
        c.WebMessageReceived+=delegate(object s,CoreWebView2WebMessageReceivedEventArgs e){if(e.Source==Origin)HandleMessage(e.WebMessageAsJson);};
        if(verify)c.NavigationCompleted+=async delegate(object s,CoreWebView2NavigationCompletedEventArgs e){if(e.IsSuccess){if(Program.SocialVerify)await VerifySocial();else await Verify();}};
        c.Navigate(Origin);timer.Start();
    }catch(Exception ex){if(verify){Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification"));File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification","error.txt"),ex.ToString());}else MessageBox.Show("WebView2 Runtime과 배포 폴더를 확인해 주세요.\n"+ex.Message);quitting=true;Close();}}
    void Send(){if(ready&&!IsDisposed&&web.CoreWebView2!=null){Text=service.Settings.brand;web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {version=1,type="snapshot",payload=service.Snapshot()}));}}
    void Toast(string message){if(ready&&!IsDisposed)web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{version=1,type="notice",message=message}));}
    void OpenFromOverlay(string section){ShowMain();if(ready)web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{version=1,type="navigate",page="genshin",section=section}));}
    static string Get(Dictionary<string,object> d,string key){object v;return d.TryGetValue(key,out v)?Convert.ToString(v):"";}
    async void HandleMessage(string raw){try{
        if(raw.Length>3000000)return;var d=json.Deserialize<Dictionary<string,object>>(raw);if(Get(d,"version")!="1")return;
        string type=Get(d,"type"),id=Get(d,"game");bool validGame=service.Games.Any(g=>g.id==id);
        if(type=="ready"){
            if(Get(d,"buildVersion")!=UpdateBuild.Version){MessageBox.Show(this,"프로그램과 화면 버전이 다릅니다. 전체 배포 파일을 다시 확인해 주세요.");quitting=true;Close();return;}
            ready=true;service.Tick();Send();if(!verify)StartUpdateCheck();
            if(!verify&&Program.OpenFriends){Program.OpenFriends=false;web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{version=1,type="navigate",page="friends"}));}
            if(!verify&&Program.ValkingSetupId!=null){string idToConnect=Program.ValkingSetupId;Program.ValkingSetupId=null;web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{version=1,type="navigate",page="valorant"}));await service.Riot.Valorant.Connect(idToConnect);}
            if(!verify&&Program.IplolSetupId!=null){string idToConnect=Program.IplolSetupId;Program.IplolSetupId=null;service.Riot.League.UseIplol();web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{version=1,type="navigate",page="league"}));await service.Riot.League.Connect(idToConnect,"KR");}
            if(!verify&&Program.DakSetupName!=null){
                string name=Program.DakSetupName;Program.DakSetupName=null;
                web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{version=1,type="navigate",page="eternal"}));
                service.Eternal.UseSource("ercraft");await service.Eternal.Search(name);
                if(service.Eternal.View.preview!=null)service.Eternal.Connect();
            }
            if(!verify&&Program.LeagueSetupId!=null){
                string setupId=Program.LeagueSetupId,setupPlatform=Program.LeagueSetupPlatform;Program.LeagueSetupId=null;
                service.Riot.League.View.riotId=setupId;service.Riot.League.View.platform=setupPlatform;Send();
                web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{version=1,type="navigate",page="league"}));
                await Task.Delay(350);
                if(!service.Riot.League.View.hasKey)using(var dialog=new LeagueKeyDialog()){if(dialog.ShowDialog(this)==DialogResult.OK)service.Riot.League.SetKey(dialog.KeyValue);}
                if(service.Riot.League.View.hasKey)await service.Riot.League.Connect(setupId,setupPlatform);
            }return;
        }
        if(type=="settings"){
            string theme=Get(d,"theme");if(theme=="default"||theme=="pink")service.Settings.theme=theme;
            string brand=Get(d,"brand").Trim();if(brand.Length>0&&brand.Length<=40&&!brand.Any(char.IsControl))service.Settings.brand=brand;
            object v;if(d.TryGetValue("demo",out v)&&v is bool)service.Settings.demo=(bool)v;
            double opacity;if(double.TryParse(Get(d,"artworkOpacity"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out opacity))service.Settings.artworkOpacity=Math.Max(0,Math.Min(1,opacity));
            service.Save();Send();return;
        }
        if(type=="update.changelog"){service.Updates.ShowChangelog();return;}
        if(type=="update.changelog.close"){service.Updates.CloseChangelog();return;}
        if(type=="update.later"){service.Updates.Dismiss();return;}

        if(type=="friends.social"){bool value;bool? muted=bool.TryParse(Get(d,"muted"),out value)?(bool?)value:null;service.Friends.SocialOptions(Get(d,"loginNotifications").ToLowerInvariant()=="true",Get(d,"partyState"),Get(d,"partyGame"),Get(d,"friend"),muted);await service.Friends.Tick(service.Games,Visible);return;}
        if(verify)return; // Verification may never launch games, browse, or open file pickers.
        if(type=="update.check"){await service.Updates.Check(true);return;}
        if(type=="update.automatic"){service.Updates.SetAutomatic(Get(d,"enabled").ToLowerInvariant()=="true");return;}
        if(type=="update.download"){await service.Updates.Download();return;}
        if(type=="update.apply"){await ApplyUpdate();return;}
        if(type=="chat.visible"){service.Friends.Chat.Visible(Get(d,"visible")=="True"||Get(d,"visible")=="true");return;}
        if(type=="chat.settings"){service.Friends.Chat.Settings(Get(d,"enabled").ToLowerInvariant()=="true");return;}
        if(type=="chat.direct"){await service.Friends.Chat.Direct(Get(d,"user"));return;}
        if(type=="chat.open"){await service.Friends.Chat.Open(Get(d,"room"));return;}
        if(type=="chat.group"){object value;var users=d.TryGetValue("users",out value)?(value as System.Collections.IEnumerable ?? new object[0]).Cast<object>().Select(x=>Convert.ToString(x)).ToArray():new string[0];await service.Friends.Chat.Group(Get(d,"name"),users,Get(d,"id"));return;}
        if(type=="chat.manage"){await service.Friends.Chat.Manage(Get(d,"room"),Get(d,"action"),Get(d,"name"),Get(d,"user"),d.ContainsKey("enabled")?(bool?)(Get(d,"enabled").ToLowerInvariant()=="true"):null);return;}
        if(type=="chat.images"){await service.Friends.Chat.ReloadImages();return;}
        if(type=="chat.image"){await service.Friends.Chat.SendImage(Get(d,"room"),Get(d,"body"),Get(d,"id"),Get(d,"data"));return;}
        if(type=="chat.send"){await service.Friends.Chat.SendMessage(Get(d,"room"),Get(d,"body"),Get(d,"id"));return;}
        if(type=="chat.older"){await service.Friends.Chat.Older();return;}
        if(type=="chat.read"){long seq;long.TryParse(Get(d,"seq"),out seq);await service.Friends.Chat.Read(Get(d,"room"),seq);return;}
        if(type=="chat.avatar"){await service.Friends.Chat.Avatar(Get(d,"image"),Get(d,"room"));return;}
        if(type=="friends.setup"){service.Friends.Setup(this);return;}
        if(type=="friends.history"){int year;int.TryParse(Get(d,"year"),out year);await service.Friends.History(Get(d,"user"),year,Get(d,"period"),Get(d,"game"),Get(d,"day"));return;}
        if(type=="friends.history.close"){service.Friends.CloseHistory();return;}
        if(type=="friends.history.privacy"){service.Friends.HistoryPrivacy(Get(d,"share").ToLowerInvariant()=="true");await service.Friends.Tick(service.Games,Visible);return;}
        if(type=="friends.profile"){await service.Friends.Profile(Get(d,"nickname"));return;}
        if(type=="friends.options"){int minutes;int.TryParse(Get(d,"awayMinutes"),out minutes);service.Friends.Options(Get(d,"shareGame").ToLowerInvariant()=="true",Get(d,"appearOffline").ToLowerInvariant()=="true",Get(d,"autoAway").ToLowerInvariant()=="true",minutes);await service.Friends.Tick(service.Games,Visible);return;}
        if(type=="friends.visible"){service.Friends.PageVisible(Get(d,"visible").ToLowerInvariant()=="true");return;}
        if(type=="friends.request"){await service.Friends.Action("request",Get(d,"code"),false);return;}
        if(type=="friends.respond"){await service.Friends.Action("respond",Get(d,"user"),Get(d,"accept").ToLowerInvariant()=="true");return;}
        if(type=="friends.remove"){await service.Friends.Action("remove",Get(d,"user"),false);return;}
        if(type=="friends.select"){await service.Friends.SelectChat(Get(d,"user"));return;}
        if(type=="friends.send"){await service.Friends.SendMessage(Get(d,"user"),Get(d,"body"),Get(d,"messageId"));return;}
        if(type=="startup.settings"){object enabled;if(d.TryGetValue("enabled",out enabled)&&enabled is bool){try{StartupRegistration.Set((bool)enabled);service.AutoStartEnabled=StartupRegistration.Enabled();Send();Toast(service.AutoStartEnabled?"Windows 로그인 시 트레이로 자동 실행해요.":"자동 실행을 껐어요.");}catch{service.AutoStartEnabled=StartupRegistration.Enabled();Send();Toast("자동 실행 설정을 변경하지 못했어요. Windows 권한을 확인해 주세요.");}}return;}
        if(type=="valorant.connect"){await service.Riot.Valorant.Connect(Get(d,"riotId"));return;}
        if(type=="valorant.refresh"){await service.Riot.Valorant.Refresh();return;}
        if(type=="valorant.disconnect"){service.Riot.Valorant.Disconnect();return;}
        if(type=="valorant.site"){Process.Start(new ProcessStartInfo(service.Riot.Valorant.SiteUrl){UseShellExecute=true});return;}
        if(type=="league.site"){Process.Start(new ProcessStartInfo("https://iplol.kr"){UseShellExecute=true});return;}
        if(type=="league.key"){using(var dialog=new LeagueKeyDialog()){if(dialog.ShowDialog(this)==DialogResult.OK)service.Riot.League.SetKey(dialog.KeyValue);}return;}
        if(type=="league.portal"){Process.Start(new ProcessStartInfo("https://developer.riotgames.com/"){UseShellExecute=true});return;}
        if(type=="league.connect"){await service.Riot.League.Connect(Get(d,"riotId"),Get(d,"platform"));return;}
        if(type=="league.refresh"){await service.Riot.League.Refresh();return;}
        if(type=="league.disconnect"){if(MessageBox.Show(this,"League 계정, API 키, 캐시와 저장한 랭크 기록을 삭제할까요?","League 연결 해제",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)service.Riot.League.Disconnect();return;}
        if(type=="eternal.source"){service.Eternal.UseSource(Get(d,"source"));return;}
        if(type=="eternal.hippy"){Process.Start(new ProcessStartInfo("https://hippygg.com/user"){UseShellExecute=true});return;}
        if(type=="eternal.site"){
            var entry=service.Eternal.View.data??service.Eternal.View.preview;
            bool hippy=service.Eternal.View.source=="hippy";string url=entry==null?(hippy?"https://hippygg.com/user":"https://ercraft.net"):(hippy?"https://hippygg.com/user/":"https://ercraft.net/player/")+Uri.EscapeDataString(entry.nickname);
            Process.Start(new ProcessStartInfo(url){UseShellExecute=true});return;
        }
        if(type=="eternal.key"){using(var dialog=new EternalKeyDialog()){if(dialog.ShowDialog(this)==DialogResult.OK)service.Eternal.SetKey(dialog.KeyValue);}return;}
        if(type=="eternal.portal"){Process.Start(new ProcessStartInfo("https://developer.eternalreturn.io/"){UseShellExecute=true});return;}
        if(type=="eternal.search"){await service.Eternal.Search(Get(d,"nickname"));return;}
        if(type=="eternal.connect"){service.Eternal.Connect();return;}
        if(type=="eternal.refresh"){await service.Eternal.Refresh(Get(d,"manual")=="True");return;}
        if(type=="eternal.disconnect"){
            if(MessageBox.Show(this,"이터널 리턴 계정·캐시·API 키를 삭제할까요? 원신 연결과 다른 설정은 유지됩니다.","이터널 리턴 연결 해제",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)service.Eternal.Disconnect();return;
        }
        if(type=="characters.load"){await service.Genshin.LoadCharacters(Get(d,"manual")=="True");return;}
        if(type=="characters.select"){int characterId;if(int.TryParse(Get(d,"id"),out characterId))await service.Genshin.LoadCharacters(Get(d,"manual")=="True",characterId);return;}
        if(type=="overlay.settings"){
            object value;if(d.TryGetValue("autoShow",out value)&&value is bool){service.Settings.overlay.autoShow=(bool)value;if(!(bool)value)overlay.Suppress();else overlay.EnableAuto();}
            string design=Get(d,"design");if(HudTheme.Valid(design))service.Settings.overlay.design=design;
            string key=Get(d,"hotkey");if(OverlayHotkey.Valid(key)){service.Settings.overlay.hotkey=key;hotkey.Bind(key);service.OverlayStatus=hotkey.Message;}
            service.Save();overlay.Render();Send();return;
        }
        if(type=="overlay.preview"){overlay.Preview();return;}
        if(type=="overlay.hide"){overlay.Suppress();return;}
        if(type=="genshin.connect"){
            if(login!=null&&!login.IsDisposed){login.Activate();return;}
            login=new HoyoLoginWindow(service.Root,service.Genshin.Accept);login.Show(this);return;
        }
        if(type=="genshin.refresh"){await service.Genshin.Refresh(true);return;}
        if(type=="genshin.select"){await service.Genshin.Select(Get(d,"uid"),Get(d,"server"));return;}
        if(type=="genshin.disconnect"){
            if(MessageBox.Show(this,"저장된 HoYoLAB 인증과 원신 연결을 해제할까요? 다른 설정은 유지됩니다.","원신 연결 해제",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            if(login!=null&&!login.IsDisposed)login.Close();service.Genshin.Disconnect();return;
        }
        if((type=="pickGame"||type=="launch")&&validGame){
            string path;service.Settings.paths.TryGetValue(id,out path);
            if(type=="pickGame"||!File.Exists(path))using(var picker=new OpenFileDialog{Title="게임 실행 파일 또는 런처 선택",Filter="실행 파일 (*.exe)|*.exe"}){if(picker.ShowDialog(this)!=DialogResult.OK)return;path=picker.FileName;service.Settings.paths[id]=path;service.Save();}
            if(type=="launch"){
                if(service.Games.First(g=>g.id==id).running){Toast("이미 실행 중이에요.");return;}
                if(!File.Exists(path)||!string.Equals(Path.GetExtension(path),".exe",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException();
                Process.Start(new ProcessStartInfo(path){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(path)});Toast("실행을 요청했어요. 런처를 선택했다면 런처에서 게임을 시작해 주세요.");
            }Send();return;
        }
        if(type=="link"&&Get(d,"target")=="hoyolab"){Process.Start(new ProcessStartInfo("https://www.hoyolab.com/"){UseShellExecute=true});return;}
        if(type=="artwork"||type=="clearArtwork"){
            string slot=Get(d,"slot");if(!new[]{"app-icon","home-background","home-character","genshin-background","genshin-character","eternal-background","eternal-character"}.Contains(slot))return;
            if(type=="clearArtwork")service.Settings.artwork.Remove(slot);
            else using(var picker=new OpenFileDialog{Title="사진 또는 아이콘 선택",Filter=slot=="app-icon"?"아이콘 이미지|*.png;*.jpg;*.jpeg":"이미지|*.png;*.jpg;*.jpeg;*.webp"}){
                if(picker.ShowDialog(this)!=DialogResult.OK)return;
                if(new FileInfo(picker.FileName).Length>20*1024*1024){Toast("20MB 이하 이미지를 선택해 주세요.");return;}
                string ext=Path.GetExtension(picker.FileName).ToLowerInvariant();if(!new[]{".png",".jpg",".jpeg",".webp"}.Contains(ext))return;
                if(slot=="app-icon"){if(ext==".webp")return;using(var check=Image.FromFile(picker.FileName)){if(check.Width>8192||check.Height>8192){Toast("8192픽셀 이하 아이콘을 선택해 주세요.");return;}}}
                string name=slot+"-"+Guid.NewGuid().ToString("N")+ext;File.Copy(picker.FileName,Path.Combine(service.Assets,name));service.Settings.artwork[slot]="https://assets.korugaming.example/"+name;
            }service.Save();if(slot=="app-icon")ApplyBrandIcon();Send();return;
        }
    }catch{Toast("요청을 완료하지 못했어요. 실행 경로와 파일 권한을 확인해 주세요.");}}
    async Task Verify(){
        string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification");Directory.CreateDirectory(folder);
        try{
            for(int n=0;n<30&&!ready;n++)await Task.Delay(200);
            await web.CoreWebView2.ExecuteScriptAsync("chrome.webview.postMessage({type:'settings',version:1,brand:'Nyang Gaming'})");await Task.Delay(100);
            FriendsTests.Fixture(service.Friends);MessengerTests.Fixture(service.Friends);var lines=new List<string>();
            IntPtr trayHandle=Handle;CreateTray();HideToTray();lines.Add("tray hide: "+(!Visible&&tray.Visible));lines.Add("tray handle stable while hidden: "+(Handle==trayHandle));ShowMain();lines.Add("tray handle stable after restore: "+(Handle==trayHandle));lines.Add("tray restore: "+(Visible&&ShowInTaskbar));lines.Add("startup quoted command: "+(StartupRegistration.Command=="\""+Application.ExecutablePath+"\" --tray"));tray.Visible=false;tray.Dispose();tray=null;trayMenu.Dispose();trayMenu=null;
            foreach(string theme in new[]{"default","pink"}){
                await web.CoreWebView2.ExecuteScriptAsync("chrome.webview.postMessage({type:'settings',version:1,theme:'"+theme+"',demo:true})");await Task.Delay(250);
                foreach(string page in new[]{"home","genshin","eternal","valorant","league","friends","chat","settings"}){
                    await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page="+page+"]').click()");await Task.Delay(150);
                    foreach(double zoom in new[]{1.0,1.25,1.5}){
                        web.ZoomFactor=zoom;await Task.Delay(120);
                        lines.Add(theme+"/"+page+"/"+zoom+": "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({bridge:document.body.dataset.connected,overflow:document.documentElement.scrollWidth>innerWidth+1,clipped:[...document.querySelectorAll('h1,h2,h3,p,button,label,.value,.stat-value')].filter(e=>!e.classList.contains('sr-only')&&(e.scrollWidth>e.clientWidth+2||e.scrollHeight>e.clientHeight+2)).map(e=>e.textContent),errors:window.__errors||[]})"));
                    }
                    web.ZoomFactor=1;await Task.Delay(80);
                    using(var f=File.Create(Path.Combine(folder,theme+"-"+page+".png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
                }
            }
            await web.CoreWebView2.ExecuteScriptAsync("chrome.webview.postMessage({type:'settings',version:1,demo:false,brand:'검증용 긴 프로그램 이름 Gaming Room'})");await Task.Delay(200);
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=eternal]').click()");await Task.Delay(150);
            lines.Add("demo off: "+await web.CoreWebView2.ExecuteScriptAsync("document.body.innerText.includes('DIAMOND')"));
            lines.Add("window title: "+Text);
            foreach(int width in new[]{600,900,1300}){
                ClientSize=new Size(width,780);await Task.Delay(150);
                foreach(string page in new[]{"home","genshin","eternal","valorant","league","friends","chat","settings"}){
                    await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page="+page+"]').click()");await Task.Delay(100);
                    lines.Add("resize "+width+"/"+page+": "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({overflow:document.documentElement.scrollWidth>innerWidth+1,clipped:[...document.querySelectorAll('h1,h2,h3,p,button,label,.value,.stat-value')].filter(e=>!e.classList.contains('sr-only')&&(e.scrollWidth>e.clientWidth+2||e.scrollHeight>e.clientHeight+2)).map(e=>e.textContent)})"));
                }
            }
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=genshin]').click()");
            foreach(string state in new[]{"connected","network","expired","selectRole"}){
                service.Genshin.SetFixture(new GenshinView{state=state,connected=state!="expired",stale=state=="network"||state=="expired",roles=HoyoTests.Roles(),selected=state=="selectRole"?null:HoyoTests.Roles()[0],notes=state=="selectRole"?null:HoyoTests.Notes(),message="검증 전용 데이터 · 실제 계정 아님"});
                foreach(int width in new[]{600,1300})foreach(double zoom in new[]{1.0,1.25,1.5}){
                    ClientSize=new Size(width,900);web.ZoomFactor=zoom;await Task.Delay(140);
                    lines.Add("hoyo "+state+"/"+width+"/"+zoom+": "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({overflow:document.documentElement.scrollWidth>innerWidth+1,clipped:[...document.querySelectorAll('h1,h2,h3,p,button,label,.value,.stat-value')].filter(e=>!e.classList.contains('sr-only')&&(e.scrollWidth>e.clientWidth+2||e.scrollHeight>e.clientHeight+2)).map(e=>e.textContent),errors:window.__errors||[]})"));
                }
                web.ZoomFactor=1;await Task.Delay(100);
                await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.genshin-account').scrollIntoView()");
                using(var f=File.Create(Path.Combine(folder,"hoyo-"+state+".png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
            }
            service.Genshin.SetFixture(new GenshinView{state="connected",connected=true,selected=HoyoTests.Roles()[0],notes=HoyoTests.Notes()});
            service.Genshin.Characters=new CharacterView{state="ready",message="검증용 데이터 · 실제 계정 아님",items=HoyoTests.CharacterFixture(),detail=HoyoTests.CharacterFixture()[0],selectedId=10000089};Send();
            foreach(string theme in new[]{"default","pink"}){
                service.Settings.theme=theme;Send();
                await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-section=characters]').click()");
                foreach(int width in new[]{600,1300})foreach(double zoom in new[]{1.0,1.25,1.5}){
                    ClientSize=new Size(width,900);web.ZoomFactor=zoom;await Task.Delay(150);
                    lines.Add("characters "+theme+"/"+width+"/"+zoom+": "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({overflow:document.documentElement.scrollWidth>innerWidth+1,clipped:[...document.querySelectorAll('h1,h2,h3,p,button,label,dt,dd,.value')].filter(e=>!e.classList.contains('sr-only')&&(e.scrollWidth>e.clientWidth+2||e.scrollHeight>e.clientHeight+2)).map(e=>e.textContent),errors:window.__errors||[]})"));
                }
                web.ZoomFactor=1;await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.characters-section').scrollIntoView()");await Task.Delay(100);
                using(var f=File.Create(Path.Combine(folder,"characters-"+theme+".png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
                foreach(string design in new[]{"pink-capsule","rose-glass","midnight-lavender"}){service.Settings.overlay.design=design;using(var barTest=new GamingOverlay(service,delegate{})){lines.Add(theme+"/"+design+" "+barTest.VerifyWindow(Path.Combine(folder,"overlay-"+theme+"-"+design+".png")));}}
            }
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=eternal]').click()");
            foreach(string theme in new[]{"default","pink"})foreach(string state in new[]{"connected","network","rateLimit","preview"}){
                service.Settings.theme=theme;service.Eternal.SetFixture(new ErView{source="api",hasKey=true,connected=state!="preview",state=state=="preview"?"connected":state,stale=state=="network"||state=="rateLimit",message="검증 전용 · 실제 계정 아님",data=state=="preview"?null:EternalReturnTests.Fixture(),preview=state=="preview"?EternalReturnTests.Fixture():null});Send();
                foreach(int width in new[]{600,1300})foreach(double zoom in new[]{1.0,1.25,1.5}){
                    ClientSize=new Size(width,900);web.ZoomFactor=zoom;await Task.Delay(140);
                    await web.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.er-match').forEach(e=>e.open=true)");
                    lines.Add("eternal "+theme+"/"+state+"/"+width+"/"+zoom+": "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({overflow:document.documentElement.scrollWidth>innerWidth+1,clipped:[...document.querySelectorAll('h1,h2,h3,p,button,label,dt,dd,.value,.er-stat-grid strong')].filter(e=>!e.classList.contains('sr-only')&&(e.scrollWidth>e.clientWidth+2||e.scrollHeight>e.clientHeight+2)).map(e=>e.textContent),errors:window.__errors||[]})"));
                }
                if(state=="connected"){web.ZoomFactor=1;await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.er-rank').scrollIntoView()");await Task.Delay(100);using(var f=File.Create(Path.Combine(folder,"eternal-"+theme+".png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);}
            }
            string hippyPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"hippy-live.json");
            if(File.Exists(hippyPath))foreach(string theme in new[]{"default","pink"}){
                service.Settings.theme=theme;var hd=json.Deserialize<ErData>(File.ReadAllText(hippyPath));service.Eternal.SetFixture(new ErView{source="hippy",connected=true,state="connected",data=hd,message="히피지지 시즌 통계"});Send();
                foreach(int width in new[]{600,1300})foreach(double zoom in new[]{1.0,1.5}){ClientSize=new Size(width,900);web.ZoomFactor=zoom;await Task.Delay(180);lines.Add("hippy "+theme+"/"+width+"/"+zoom+": "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({overflow:document.documentElement.scrollWidth>innerWidth+1,season:document.body.innerText.includes('시즌 사용 캐릭터'),noFakeMatches:!document.body.innerText.includes('최근 0경기'),errors:window.__errors||[]})"));}
                web.ZoomFactor=1;await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.er-rank').scrollIntoView()");await Task.Delay(100);using(var f=File.Create(Path.Combine(folder,"hippy-"+theme+".png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
                service.Eternal.SetFixture(new ErView{source="hippy",state="disconnected"});Send();await Task.Delay(180);lines.Add("hippy selector: "+await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.er-filter select').value==='hippy'"));
            }
            string livePath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"craft-live.json");
            if(File.Exists(livePath)){
                var live=json.Deserialize<ErData>(File.ReadAllText(livePath));
                foreach(string theme in new[]{"default","pink"}){
                    service.Settings.theme=theme;service.Eternal.SetFixture(new ErView{source="ercraft",connected=true,state="connected",message="ERCraft 공개 전적 · 냥냥",data=live});Send();
                    foreach(int width in new[]{600,1300})foreach(double zoom in new[]{1.0,1.25,1.5}){
                        ClientSize=new Size(width,900);web.ZoomFactor=zoom;await Task.Delay(160);
                        lines.Add("dak "+theme+"/"+width+"/"+zoom+": "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({overflow:document.documentElement.scrollWidth>innerWidth+1,clipped:[...document.querySelectorAll('h1,h2,h3,p,button,label,dt,dd,.value')].filter(e=>!e.classList.contains('sr-only')&&(e.scrollWidth>e.clientWidth+2||e.scrollHeight>e.clientHeight+2)).map(e=>e.textContent),errors:window.__errors||[]})"));
                    }
                    web.ZoomFactor=1;await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.er-rank').scrollIntoView()");await Task.Delay(150);
                    using(var f=File.Create(Path.Combine(folder,"craft-live-"+theme+".png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
                }
            }
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=league]').click()");
            foreach(string theme in new[]{"default","pink"})foreach(string state in new[]{"connected","network","rateLimit","key"}){
                service.Settings.theme=theme;service.Riot.League.SetFixture(new LeagueView{source="api",connected=true,hasKey=true,state=state,stale=state!="connected",riotId="검증용 계정#실제아님",data=LeagueTests.Fixture(),message="검증 전용 데이터 · 실제 계정 아님"});Send();
                foreach(int width in new[]{600,1300})foreach(double zoom in new[]{1.0,1.25,1.5}){
                    ClientSize=new Size(width,900);web.ZoomFactor=zoom;await Task.Delay(150);
                    lines.Add("league "+theme+"/"+state+"/"+width+"/"+zoom+": "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({overflow:document.documentElement.scrollWidth>innerWidth+1,clipped:[...document.querySelectorAll('h1,h2,h3,p,button,label,time,.riot-stat strong')].filter(e=>!e.classList.contains('sr-only')&&(e.scrollWidth>e.clientWidth+2||e.scrollHeight>e.clientHeight+2)).map(e=>e.textContent),errors:window.__errors||[]})"));
                }
                if(state=="connected"){web.ZoomFactor=1;await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.league-account').scrollIntoView()");await Task.Delay(150);using(var f=File.Create(Path.Combine(folder,"league-fixture-"+theme+".png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);}
            }
            string ipPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"iplol-live.json");
            if(File.Exists(ipPath))foreach(string theme in new[]{"default","pink"}){
                service.Settings.theme=theme;service.Riot.League.SetFixture(new LeagueView{source="iplol",connected=true,state="connected",riotId="도끼춤#한번춰볼까",data=json.Deserialize<LeagueData>(File.ReadAllText(ipPath)),message="아이피롤 공개 전적"});Send();
                foreach(int width in new[]{600,1300})foreach(double zoom in new[]{1.0,1.25,1.5}){ClientSize=new Size(width,900);web.ZoomFactor=zoom;await Task.Delay(160);lines.Add("iplol "+theme+"/"+width+"/"+zoom+": "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({overflow:document.documentElement.scrollWidth>innerWidth+1,clipped:[...document.querySelectorAll('h1,h2,h3,p,button,label,time,.riot-stat strong')].filter(e=>!e.classList.contains('sr-only')&&(e.scrollWidth>e.clientWidth+2||e.scrollHeight>e.clientHeight+2)).map(e=>e.textContent),errors:window.__errors||[]})"));}
                web.ZoomFactor=1;await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.league-account').scrollIntoView()");await Task.Delay(200);using(var f=File.Create(Path.Combine(folder,"iplol-live-"+theme+".png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
            }
            string valPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"valking-live.json");
            if(File.Exists(valPath)){
                await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=valorant]').click()");
                foreach(string theme in new[]{"default","pink"}){
                    service.Settings.theme=theme;service.Riot.Valorant.SetFixture(json.Deserialize<RiotProfile>(File.ReadAllText(valPath)));Send();
                    foreach(int width in new[]{600,1300})foreach(double zoom in new[]{1.0,1.25,1.5}){ClientSize=new Size(width,900);web.ZoomFactor=zoom;await Task.Delay(180);lines.Add("valking "+theme+"/"+width+"/"+zoom+": "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({overflow:document.documentElement.scrollWidth>innerWidth+1,clipped:[...document.querySelectorAll('h1,h2,h3,p,button,label,time,.riot-stat strong')].filter(e=>!e.classList.contains('sr-only')&&(e.scrollWidth>e.clientWidth+2||e.scrollHeight>e.clientHeight+2)).map(e=>e.textContent),errors:window.__errors||[]})"));}
                    web.ZoomFactor=1;await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.valorant-account').scrollIntoView()");await Task.Delay(180);using(var f=File.Create(Path.Combine(folder,"valking-live-"+theme+".png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
                }
            }
            FriendsTests.Fixture(service.Friends);service.Settings.theme="pink";ClientSize=new Size(1300,1000);web.ZoomFactor=1;Send();await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=friends]').click()");await Task.Delay(300);
            lines.Add("history calendar: "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({days:document.querySelectorAll('.activity-day').length,levels:[...new Set([...document.querySelectorAll('.activity-day')].map(e=>e.className.match(/heat-[0-4]/)[0]))],summary:document.querySelector('.activity-summary').innerText})"));
            await web.CoreWebView2.ExecuteScriptAsync("window.__historyMessages=[];const originalPost=chrome.webview.postMessage.bind(chrome.webview);chrome.webview.postMessage=m=>{window.__historyMessages.push(m);originalPost(m)};document.querySelectorAll('.activity-day:not(:disabled)')[10].click()");await Task.Delay(120);
            lines.Add("history day click: "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({selected:document.querySelector('.activity-day.chosen').getAttribute('aria-label'),heading:document.querySelector('.activity-day-detail h3').innerText,request:window.__historyMessages.find(m=>m.type==='friends.history')})"));
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.activity-dashboard').scrollIntoView()");await Task.Delay(120);using(var f=File.Create(Path.Combine(folder,"history-pink.png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
            service.Friends.View.history=new{allowed=false};service.Friends.View.historyMessage="검증: 비공개 기록";Send();await Task.Delay(150);lines.Add("history denied UI: "+await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.activity-summary')===null&&document.querySelector('.activity-calendar')===null"));
            web.ZoomFactor=1;ClientSize=new Size(1300,1000);MessengerTests.Fixture(service.Friends);FriendsTests.Fixture(service.Friends);Send();await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=chat]').click()");await Task.Delay(150);
            string chatUiTest=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","frontend","chat-ui-test.js");if(File.Exists(chatUiTest)){await web.CoreWebView2.ExecuteScriptAsync(File.ReadAllText(chatUiTest));for(int wait=0;wait<100;wait++){if(await web.CoreWebView2.ExecuteScriptAsync("window.__chatUiDone===true")=="true")break;await Task.Delay(100);}lines.Add("chat UI completed: "+await web.CoreWebView2.ExecuteScriptAsync("window.__chatUiDone===true"));lines.Add("chat UI interactions: "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify(window.__chatUiResult)"));}
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=friends]').click()");await Task.Delay(100);await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=chat]').click()");await Task.Delay(150);
            service.Friends.Chat.View.room="group-test";Send();await Task.Delay(120);await web.CoreWebView2.ExecuteScriptAsync("window.__chatPosts=[];var box=document.querySelector('#messenger-input');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(box,'그룹 Enter 확인');box.dispatchEvent(new Event('input',{bubbles:true}));");await Task.Delay(80);await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('#messenger-input').dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',keyCode:13,bubbles:true,cancelable:true}));");await Task.Delay(80);lines.Add("group Enter send: "+await web.CoreWebView2.ExecuteScriptAsync("window.__chatPosts.some(m=>m.type==='chat.send'&&m.room==='group-test'&&m.body==='그룹 Enter 확인')"));
            MessengerTests.Fixture(service.Friends);Send();await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.messenger-layout').scrollIntoView()");await Task.Delay(120);
            using(var f=File.Create(Path.Combine(folder,"messenger-pink.png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
            service.Save();var reload=new GamingService(true);lines.Add("settings persistence: "+(reload.Settings.brand==Text&&!reload.Settings.demo));
            lines.Add("HUD design persistence: "+(reload.Settings.overlay.design==service.Settings.overlay.design));
            lines.Add("HUD default/migration: "+(new OverlaySettings().design=="pink-capsule"&&json.Deserialize<OverlaySettings>("{\"hotkey\":\"G\",\"themeSync\":true}").design=="pink-capsule"));
            lines.Add("snapshot: "+json.Serialize(service.Snapshot()));
            foreach(string theme in new[]{"default","pink"})foreach(string state in new[]{"available","downloading","ready","downloadError","current","unconfigured"}){
                service.Settings.theme=theme;service.Updates.Fixture(new UpdateView{currentVersion="1.0.0",newVersion=state=="current"||state=="unconfigured"?null:"1.0.1",state=state,dialog=true,busy=state=="downloading",progress=68,notes="친구 채팅 개선\n전적 안정화\n<script>window.__updateInjected=true</script>",message="검증용 업데이트 상태"});Send();
                foreach(int width in new[]{600,1300})foreach(double zoom in new[]{1.0,1.5}){ClientSize=new Size(width,900);web.ZoomFactor=zoom;await Task.Delay(120);lines.Add("update "+theme+"/"+state+"/"+width+"/"+zoom+": "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({dialog:!!document.querySelector('[aria-modal=true]'),overflow:document.documentElement.scrollWidth>innerWidth+1,plainNotes:!window.__updateInjected,errors:window.__errors||[]})"));}
                web.ZoomFactor=1;if(state=="available"||state=="downloading"||state=="ready")using(var f=File.Create(Path.Combine(folder,"update-"+theme+"-"+state+".png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
                await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.update-actions button').click()");await Task.Delay(100);lines.Add("update dismiss: "+await web.CoreWebView2.ExecuteScriptAsync("!document.querySelector('.update-dialog')"));
            }
            File.WriteAllLines(Path.Combine(folder,"checks.txt"),lines);
        }catch(Exception ex){File.WriteAllText(Path.Combine(folder,"error.txt"),ex.ToString());}
        Close();
    }
}



